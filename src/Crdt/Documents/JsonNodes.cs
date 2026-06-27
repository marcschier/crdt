// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace Crdt;

public sealed partial class JsonCrdt
{
    private sealed class MapEntry : IEquatable<MapEntry>
    {
        public MapEntry(JsonNode value, Timestamp timestamp, HashSet<Dot> dots)
        {
            Value = value;
            Timestamp = timestamp;
            Dots = dots;
        }

        public JsonNode Value { get; private set; }

        public Timestamp Timestamp { get; private set; }

        public HashSet<Dot> Dots { get; }

        public void Assign(JsonNode value, Timestamp timestamp, Dot dot)
        {
            if (ShouldAccept(timestamp, Timestamp) || (timestamp == Timestamp && dot > MaxDot()))
            {
                Value = value;
                Timestamp = timestamp;
            }
            else if (Value.Kind == value.Kind)
            {
                Value.Merge(value);
            }

            Dots.Add(dot);
        }

        public MapEntry Clone() => new(Value.Clone(), Timestamp, new HashSet<Dot>(Dots));

        public bool Equals(MapEntry? other) => other is not null && Timestamp == other.Timestamp
            && Value.Equals(other.Value) && Dots.SetEquals(other.Dots);

        public override bool Equals(object? obj) => Equals(obj as MapEntry);

        public override int GetHashCode() => HashCode.Combine(Value, Timestamp, Dots.Count);

        private Dot MaxDot()
        {
            Dot max = default;
            foreach (Dot dot in Dots)
            {
                if (dot > max)
                {
                    max = dot;
                }
            }

            return max;
        }
    }

    private sealed class MapNode : JsonNode
    {
        private readonly Dictionary<string, MapEntry> _entries = [];
        private readonly HashSet<Dot> _removed = [];

        public override JsonLiteralKind Kind => JsonLiteralKind.Object;

        public bool TryGetValue(string key, [NotNullWhen(true)] out JsonNode? value)
        {
            if (_entries.TryGetValue(key, out MapEntry? entry))
            {
                value = entry.Value;
                return true;
            }

            value = null;
            return false;
        }

        public List<Dot> DotsFor(string key) =>
            _entries.TryGetValue(key, out MapEntry? entry) ? new List<Dot>(entry.Dots) : [];

        public bool ApplySet(string key, JsonNode value, Dot dot)
        {
            if (_removed.Contains(dot))
            {
                return false;
            }

            Timestamp timestamp = ExtractTimestamp(value);
            if (!_entries.TryGetValue(key, out MapEntry? entry))
            {
                _entries[key] = new MapEntry(value, timestamp, new HashSet<Dot> { dot });
                return true;
            }

            JsonNode before = entry.Value.Clone();
            int dotCount = entry.Dots.Count;
            entry.Assign(value, timestamp, dot);
            return dotCount != entry.Dots.Count || !before.Equals(entry.Value);
        }

        public bool ApplyDelete(string key, IEnumerable<Dot> removedDots)
        {
            bool changed = false;
            foreach (Dot dot in removedDots)
            {
                changed |= _removed.Add(dot);
            }

            if (!_entries.TryGetValue(key, out MapEntry? entry))
            {
                return changed;
            }

            foreach (Dot dot in removedDots)
            {
                changed |= entry.Dots.Remove(dot);
            }

            if (entry.Dots.Count == 0)
            {
                _entries.Remove(key);
            }

            return changed;
        }

        internal override JsonNode Clone()
        {
            var clone = new MapNode();
            foreach (KeyValuePair<string, MapEntry> entry in _entries)
            {
                clone._entries[entry.Key] = entry.Value.Clone();
            }

            foreach (Dot dot in _removed)
            {
                clone._removed.Add(dot);
            }

            return clone;
        }

        internal override void Merge(JsonNode other)
        {
            if (other is not MapNode map)
            {
                return;
            }

            foreach (KeyValuePair<string, MapEntry> entry in map._entries)
            {
                if (!_entries.TryGetValue(entry.Key, out MapEntry? current))
                {
                    MapEntry clone = entry.Value.Clone();
                    clone.Dots.ExceptWith(_removed);
                    if (clone.Dots.Count != 0)
                    {
                        _entries[entry.Key] = clone;
                    }
                }
                else if (ShouldAccept(entry.Value.Timestamp, current.Timestamp)
                    || (entry.Value.Timestamp == current.Timestamp && Max(entry.Value.Dots) > Max(current.Dots)))
                {
                    HashSet<Dot> dots = new(current.Dots);
                    foreach (Dot dot in entry.Value.Dots)
                    {
                        dots.Add(dot);
                    }

                    dots.ExceptWith(_removed);
                    _entries[entry.Key] = new MapEntry(entry.Value.Value.Clone(), entry.Value.Timestamp, dots);
                }
                else if (entry.Value.Timestamp == current.Timestamp && current.Value.Kind == entry.Value.Value.Kind)
                {
                    current.Value.Merge(entry.Value.Value);
                    foreach (Dot dot in entry.Value.Dots)
                    {
                        if (!_removed.Contains(dot))
                        {
                            current.Dots.Add(dot);
                        }
                    }
                }
                else
                {
                    foreach (Dot dot in entry.Value.Dots)
                    {
                        if (!_removed.Contains(dot))
                        {
                            current.Dots.Add(dot);
                        }
                    }
                }
            }

            foreach (Dot dot in map._removed)
            {
                _removed.Add(dot);
            }

            RemoveDeletedDots();
        }

        internal override void Write(ref CrdtWriter writer)
        {
            writer.WriteByte((byte)Kind);
            var keys = new List<string>(_entries.Keys);
            keys.Sort(StringComparer.Ordinal);
            writer.WriteVarUInt64((ulong)keys.Count);
            foreach (string key in keys)
            {
                MapEntry entry = _entries[key];
                writer.WriteString(key);
                writer.WriteTimestamp(entry.Timestamp);
                entry.Value.Write(ref writer);
                var dots = new List<Dot>(entry.Dots);
                dots.Sort();
                writer.WriteVarUInt64((ulong)dots.Count);
                foreach (Dot dot in dots)
                {
                    writer.WriteDot(dot);
                }
            }

            var removed = new List<Dot>(_removed);
            removed.Sort();
            writer.WriteVarUInt64((ulong)removed.Count);
            foreach (Dot dot in removed)
            {
                writer.WriteDot(dot);
            }
        }

        internal override void WriteJson(Utf8JsonWriter writer)
        {
            writer.WriteStartObject();
            var keys = new List<string>(_entries.Keys);
            keys.Sort(StringComparer.Ordinal);
            foreach (string key in keys)
            {
                writer.WritePropertyName(key);
                _entries[key].Value.WriteJson(writer);
            }

            writer.WriteEndObject();
        }

        public override bool Equals(JsonNode? other)
        {
            if (other is not MapNode map || map._entries.Count != _entries.Count || !map._removed.SetEquals(_removed))
            {
                return false;
            }

            foreach (KeyValuePair<string, MapEntry> entry in _entries)
            {
                if (!map._entries.TryGetValue(entry.Key, out MapEntry? otherEntry) || !entry.Value.Equals(otherEntry))
                {
                    return false;
                }
            }

            return true;
        }

        public override bool Equals(object? obj) => Equals(obj as JsonNode);

        public override int GetHashCode() => _entries.Count;

        internal static MapNode ReadMap(ref CrdtReader reader)
        {
            var map = new MapNode();
            int count = reader.ReadCount();
            for (int i = 0; i < count; i++)
            {
                string key = reader.ReadString() ?? string.Empty;
                Timestamp timestamp = reader.ReadTimestamp();
                JsonNode value = JsonNode.Read(ref reader);
                int dotCount = reader.ReadCount();
                var dots = new HashSet<Dot>();
                for (int j = 0; j < dotCount; j++)
                {
                    dots.Add(reader.ReadDot());
                }

                map._entries[key] = new MapEntry(value, timestamp, dots);
            }

            int removedCount = reader.ReadCount();
            for (int i = 0; i < removedCount; i++)
            {
                map._removed.Add(reader.ReadDot());
            }

            return map;
        }

        private void RemoveDeletedDots()
        {
            var empty = new List<string>();
            foreach (KeyValuePair<string, MapEntry> entry in _entries)
            {
                entry.Value.Dots.ExceptWith(_removed);
                if (entry.Value.Dots.Count == 0)
                {
                    empty.Add(entry.Key);
                }
            }

            foreach (string key in empty)
            {
                _entries.Remove(key);
            }
        }
    }

    private sealed class ListNode : JsonNode
    {
        private static Dot RootId => default;
        private readonly Dictionary<Dot, ListEntry> _entries = [];
        private readonly HashSet<Dot> _deleted = [];

        public override JsonLiteralKind Kind => JsonLiteralKind.Array;

        public bool TryGetElement(Dot id, [NotNullWhen(true)] out JsonNode? value)
        {
            if (_entries.TryGetValue(id, out ListEntry? entry) && !_deleted.Contains(id))
            {
                value = entry.Value;
                return true;
            }

            value = null;
            return false;
        }

        public bool ApplyInsert(Dot id, Dot parent, JsonNode value)
        {
            if (_entries.ContainsKey(id))
            {
                return false;
            }

            _entries[id] = new ListEntry(parent, value);
            return true;
        }

        public bool ApplyDelete(Dot id) => _deleted.Add(id);

        public List<Dot> VisibleIds()
        {
            Dictionary<Dot, List<Dot>> children = BuildChildren();
            var result = new List<Dot>(_entries.Count);
            var stack = new Stack<Dot>();
            PushChildren(stack, children, RootId);
            while (stack.Count > 0)
            {
                Dot id = stack.Pop();
                if (!_deleted.Contains(id))
                {
                    result.Add(id);
                }

                PushChildren(stack, children, id);
            }

            return result;
        }

        internal override JsonNode Clone()
        {
            var clone = new ListNode();
            foreach (KeyValuePair<Dot, ListEntry> entry in _entries)
            {
                clone._entries[entry.Key] = new ListEntry(entry.Value.Parent, entry.Value.Value.Clone());
            }

            foreach (Dot dot in _deleted)
            {
                clone._deleted.Add(dot);
            }

            return clone;
        }

        internal override void Merge(JsonNode other)
        {
            if (other is not ListNode list)
            {
                return;
            }

            foreach (KeyValuePair<Dot, ListEntry> entry in list._entries)
            {
                if (_entries.TryGetValue(entry.Key, out ListEntry? current))
                {
                    current.Value.Merge(entry.Value.Value);
                }
                else
                {
                    _entries[entry.Key] = new ListEntry(entry.Value.Parent, entry.Value.Value.Clone());
                }
            }

            foreach (Dot dot in list._deleted)
            {
                _deleted.Add(dot);
            }
        }

        internal override void Write(ref CrdtWriter writer)
        {
            writer.WriteByte((byte)Kind);
            var ids = new List<Dot>(_entries.Keys);
            ids.Sort();
            writer.WriteVarUInt64((ulong)ids.Count);
            foreach (Dot id in ids)
            {
                ListEntry entry = _entries[id];
                writer.WriteDot(id);
                writer.WriteDot(entry.Parent);
                entry.Value.Write(ref writer);
            }

            var deleted = new List<Dot>(_deleted);
            deleted.Sort();
            writer.WriteVarUInt64((ulong)deleted.Count);
            foreach (Dot dot in deleted)
            {
                writer.WriteDot(dot);
            }
        }

        internal override void WriteJson(Utf8JsonWriter writer)
        {
            writer.WriteStartArray();
            foreach (Dot id in VisibleIds())
            {
                _entries[id].Value.WriteJson(writer);
            }

            writer.WriteEndArray();
        }

        public override bool Equals(JsonNode? other)
        {
            if (other is not ListNode list
                || list._entries.Count != _entries.Count
                || list._deleted.Count != _deleted.Count)
            {
                return false;
            }

            foreach (KeyValuePair<Dot, ListEntry> entry in _entries)
            {
                if (!list._entries.TryGetValue(entry.Key, out ListEntry? otherEntry)
                    || entry.Value.Parent != otherEntry.Parent || !entry.Value.Value.Equals(otherEntry.Value))
                {
                    return false;
                }
            }

            return _deleted.SetEquals(list._deleted);
        }

        public override bool Equals(object? obj) => Equals(obj as JsonNode);

        public override int GetHashCode() => HashCode.Combine(_entries.Count, _deleted.Count);

        internal static ListNode ReadList(ref CrdtReader reader)
        {
            var list = new ListNode();
            int count = reader.ReadCount();
            for (int i = 0; i < count; i++)
            {
                Dot id = reader.ReadDot();
                Dot parent = reader.ReadDot();
                list._entries[id] = new ListEntry(parent, JsonNode.Read(ref reader));
            }

            int deletedCount = reader.ReadCount();
            for (int i = 0; i < deletedCount; i++)
            {
                list._deleted.Add(reader.ReadDot());
            }

            return list;
        }

        private static void PushChildren(Stack<Dot> stack, Dictionary<Dot, List<Dot>> children, Dot parent)
        {
            if (!children.TryGetValue(parent, out List<Dot>? list))
            {
                return;
            }

            foreach (Dot child in list)
            {
                stack.Push(child);
            }
        }

        private Dictionary<Dot, List<Dot>> BuildChildren()
        {
            var children = new Dictionary<Dot, List<Dot>>();
            foreach (KeyValuePair<Dot, ListEntry> entry in _entries)
            {
                if (!children.TryGetValue(entry.Value.Parent, out List<Dot>? list))
                {
                    list = [];
                    children[entry.Value.Parent] = list;
                }

                list.Add(entry.Key);
            }

            foreach (List<Dot> list in children.Values)
            {
                list.Sort();
            }

            return children;
        }
    }

    private sealed class ListEntry
    {
        public ListEntry(Dot parent, JsonNode value)
        {
            Parent = parent;
            Value = value;
        }

        public Dot Parent { get; }

        public JsonNode Value { get; }
    }

    private sealed class RegisterNode : JsonNode
    {
        public RegisterNode(JsonPrimitive value, Timestamp timestamp)
        {
            Value = value;
            Timestamp = timestamp;
        }

        public override JsonLiteralKind Kind => JsonLiteralKind.Primitive;

        public JsonPrimitive Value { get; private set; }

        public Timestamp Timestamp { get; private set; }

        public bool ApplyAssign(JsonPrimitive value, Timestamp timestamp)
        {
            if (!ShouldAccept(timestamp, Timestamp))
            {
                return false;
            }

            Value = value;
            Timestamp = timestamp;
            return true;
        }

        internal override JsonNode Clone() => new RegisterNode(Value, Timestamp);

        internal override void Merge(JsonNode other)
        {
            if (other is RegisterNode register && ShouldAccept(register.Timestamp, Timestamp))
            {
                Value = register.Value;
                Timestamp = register.Timestamp;
            }
        }

        internal override void Write(ref CrdtWriter writer)
        {
            writer.WriteByte((byte)Kind);
            writer.WriteTimestamp(Timestamp);
            Value.Write(ref writer);
        }

        internal override void WriteJson(Utf8JsonWriter writer)
        {
            if (Value.Kind == JsonPrimitiveKind.String)
            {
                writer.WriteStringValue(Value.GetString());
            }
            else if (Value.Kind == JsonPrimitiveKind.Number)
            {
                writer.WriteNumberValue(Value.GetNumber());
            }
            else if (Value.Kind == JsonPrimitiveKind.Boolean)
            {
                writer.WriteBooleanValue(Value.GetBoolean());
            }
            else
            {
                writer.WriteNullValue();
            }
        }

        public override bool Equals(JsonNode? other) => other is RegisterNode register
            && Timestamp == register.Timestamp && Value.Equals(register.Value);

        public override bool Equals(object? obj) => Equals(obj as JsonNode);

        public override int GetHashCode() => HashCode.Combine(Value, Timestamp);

        internal static RegisterNode ReadRegister(ref CrdtReader reader)
        {
            Timestamp timestamp = reader.ReadTimestamp();
            return new RegisterNode(JsonPrimitive.Read(ref reader), timestamp);
        }
    }

    private static Timestamp ExtractTimestamp(JsonNode node) =>
        node is RegisterNode register ? register.Timestamp : Timestamp.MinValue;

    private static Dot Max(HashSet<Dot> dots)
    {
        Dot max = default;
        foreach (Dot dot in dots)
        {
            if (dot > max)
            {
                max = dot;
            }
        }

        return max;
    }
}
