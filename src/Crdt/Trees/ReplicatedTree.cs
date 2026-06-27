// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers;
using System.Diagnostics.CodeAnalysis;

namespace Crdt;

/// <summary>
/// A replicated tree CRDT with the highly-available move operation.
/// </summary>
/// <remarks>
/// Move operations are kept in timestamp order and replayed with the undo/do/redo algorithm from
/// Kleppmann et al. Concurrent moves that would introduce a cycle are deterministically skipped.
/// Mutable and not thread-safe.
/// </remarks>
public sealed partial class ReplicatedTree :
    IConvergent<ReplicatedTree>,
    IOperationConvergent<TreeMoveOperation>,
    IEquatable<ReplicatedTree>,
    IBinaryWritable
{
    private readonly Dictionary<string, (string Parent, string Meta)> _tree;
    private readonly List<LogMove> _log;
    private readonly ReplicaId _replica;
    private ulong _counter;

    /// <summary>Initializes an empty tree with a fresh local replica id.</summary>
    public ReplicatedTree()
        : this(ReplicaId.New())
    {
    }

    /// <summary>Initializes an empty tree for <paramref name="replica"/>.</summary>
    /// <param name="replica">The local replica id used for locally-generated moves.</param>
    public ReplicatedTree(ReplicaId replica)
        : this(replica, 0, new Dictionary<string, (string Parent, string Meta)>(), [])
    {
    }

    private ReplicatedTree(
        ReplicaId replica,
        ulong counter,
        Dictionary<string, (string Parent, string Meta)> tree,
        List<LogMove> log)
    {
        _replica = replica;
        _counter = counter;
        _tree = tree;
        _log = log;
    }

    /// <summary>Gets the current child-to-parent and metadata map.</summary>
    public IReadOnlyDictionary<string, (string Parent, string Meta)> Nodes => _tree;

    /// <summary>Gets the move log in ascending timestamp order.</summary>
    public IReadOnlyList<LogMove> Log => _log;

    /// <summary>Gets the local replica id.</summary>
    public ReplicaId Replica => _replica;

    /// <summary>Gets the local Lamport counter.</summary>
    public ulong Counter => _counter;

    /// <summary>Gets the number of moved child nodes currently present.</summary>
    public int Count => _tree.Count;

    /// <summary>Moves <paramref name="child"/> under <paramref name="newParent"/>.</summary>
    /// <param name="child">The child node id to move.</param>
    /// <param name="newParent">The new parent node id.</param>
    /// <param name="meta">The metadata to store on the child.</param>
    /// <returns>The operation to broadcast.</returns>
    public TreeMoveOperation Move(string child, string newParent, string meta)
    {
        Throw.IfNull(child);
        Throw.IfNull(newParent);
        Throw.IfNull(meta);
        _counter = Math.Max(_counter, MaxSeenCounter()) + 1UL;
        var operation = new TreeMoveOperation(new MoveTimestamp(_counter, _replica), child, newParent, meta);
        Apply(operation);
        return operation;
    }

    /// <inheritdoc/>
    public bool Apply(TreeMoveOperation operation)
    {
        int insertIndex = FindInsertIndex(operation.Timestamp);
        if (insertIndex < _log.Count && _log[insertIndex].Timestamp == operation.Timestamp)
        {
            return false;
        }

        _counter = Math.Max(_counter, operation.Timestamp.Counter);
        List<LogMove> suffix = _log.GetRange(insertIndex, _log.Count - insertIndex);
        for (int i = _log.Count - 1; i >= insertIndex; i--)
        {
            Undo(_log[i]);
            _log.RemoveAt(i);
        }

        _log.Insert(insertIndex, Do(operation));
        for (int i = 0; i < suffix.Count; i++)
        {
            _log.Insert(insertIndex + i + 1, Do(suffix[i].ToOperation()));
        }

        return true;
    }

    /// <inheritdoc/>
    public void Merge(ReplicatedTree other)
    {
        Throw.IfNull(other);
        foreach (LogMove move in other._log)
        {
            if (!ContainsTimestamp(move.Timestamp))
            {
                Apply(move.ToOperation());
            }
        }
    }

    /// <inheritdoc/>
    public CrdtOrder Compare(ReplicatedTree other)
    {
        Throw.IfNull(other);
        bool thisInOther = IsTimestampSubsetOf(_log, other._log);
        bool otherInThis = IsTimestampSubsetOf(other._log, _log);
        if (thisInOther && otherInThis)
        {
            return CrdtOrder.Equal;
        }

        if (thisInOther)
        {
            return CrdtOrder.Less;
        }

        return otherInThis ? CrdtOrder.Greater : CrdtOrder.Concurrent;
    }

    /// <inheritdoc/>
    public ReplicatedTree Clone() => new(_replica, _counter, CloneTree(), new List<LogMove>(_log));

    /// <summary>Gets the parent of <paramref name="node"/> if the node currently exists.</summary>
    /// <param name="node">The node id.</param>
    /// <returns>The current parent, or <see langword="null"/> if the node has no entry.</returns>
    public string? GetParent(string node)
    {
        Throw.IfNull(node);
        return _tree.TryGetValue(node, out (string Parent, string Meta) entry) ? entry.Parent : null;
    }

    /// <summary>
    /// Determines whether <paramref name="maybeAncestor"/> is an ancestor of <paramref name="node"/>.
    /// </summary>
    /// <param name="maybeAncestor">The possible ancestor node id.</param>
    /// <param name="node">The node whose parent chain is walked.</param>
    /// <returns><see langword="true"/> if the parent chain reaches <paramref name="maybeAncestor"/>.</returns>
    public bool IsAncestor(string maybeAncestor, string node)
    {
        Throw.IfNull(maybeAncestor);
        Throw.IfNull(node);
        string current = node;
        HashSet<string> seen = [];
        while (_tree.TryGetValue(current, out (string Parent, string Meta) entry) && seen.Add(current))
        {
            if (StringComparer.Ordinal.Equals(entry.Parent, maybeAncestor))
            {
                return true;
            }

            current = entry.Parent;
        }

        return false;
    }

    /// <summary>Determines whether the current tree contains a parent cycle.</summary>
    /// <returns><see langword="true"/> if a cycle exists; otherwise <see langword="false"/>.</returns>
    public bool HasCycle()
    {
        foreach (string node in _tree.Keys)
        {
            if (IsAncestor(node, node))
            {
                return true;
            }
        }

        return false;
    }

    /// <inheritdoc/>
    public void Write(ref CrdtWriter writer)
    {
        writer.WriteByte(CrdtBinary.FormatVersion);
        writer.WriteReplicaId(_replica);
        writer.WriteVarUInt64(_counter);
        writer.WriteVarUInt64((ulong)_tree.Count);
        foreach (KeyValuePair<string, (string Parent, string Meta)> pair in OrderedTree())
        {
            writer.WriteString(pair.Key);
            writer.WriteString(pair.Value.Parent);
            writer.WriteString(pair.Value.Meta);
        }

        writer.WriteVarUInt64((ulong)_log.Count);
        foreach (LogMove move in _log)
        {
            move.Write(ref writer);
        }
    }

    /// <summary>Serializes the tree into <paramref name="output"/>.</summary>
    /// <param name="output">The destination buffer writer.</param>
    public void WriteTo(IBufferWriter<byte> output)
    {
        Throw.IfNull(output);
        var writer = new CrdtWriter(output);
        Write(ref writer);
    }

    /// <summary>Reads a tree from the binary reader.</summary>
    /// <param name="reader">The source reader.</param>
    /// <returns>The decoded tree.</returns>
    public static ReplicatedTree Read(ref CrdtReader reader)
    {
        byte version = reader.ReadByte();
        if (version != CrdtBinary.FormatVersion)
        {
            Throw.InvalidData<ReplicatedTree>("Unsupported replicated tree binary format version.");
        }

        ReplicaId replica = reader.ReadReplicaId();
        ulong counter = reader.ReadVarUInt64();
        int treeCount = reader.ReadCount();
        var tree = new Dictionary<string, (string Parent, string Meta)>(treeCount, StringComparer.Ordinal);
        for (int i = 0; i < treeCount; i++)
        {
            string child = reader.ReadString() ?? Throw.InvalidData<string>("Tree child cannot be null.");
            string parent = reader.ReadString() ?? Throw.InvalidData<string>("Tree parent cannot be null.");
            string meta = reader.ReadString() ?? Throw.InvalidData<string>("Tree metadata cannot be null.");
            tree.Add(child, (parent, meta));
        }

        int logCount = reader.ReadCount();
        var log = new List<LogMove>(logCount);
        MoveTimestamp previous = default;
        for (int i = 0; i < logCount; i++)
        {
            LogMove move = LogMove.Read(ref reader);
            if (i > 0 && move.Timestamp <= previous)
            {
                Throw.InvalidData<ReplicatedTree>("Move log is not in strictly ascending timestamp order.");
            }

            previous = move.Timestamp;
            log.Add(move);
            counter = Math.Max(counter, move.Timestamp.Counter);
        }

        return new ReplicatedTree(replica, counter, tree, log);
    }

    /// <summary>Decodes a tree from the binary format.</summary>
    /// <param name="data">The encoded bytes.</param>
    /// <param name="options">Optional decoding limits.</param>
    /// <returns>The decoded tree.</returns>
    public static ReplicatedTree ReadFrom(ReadOnlySpan<byte> data, CrdtReaderOptions? options = null)
    {
        var reader = new CrdtReader(data, options);
        return Read(ref reader);
    }

    /// <summary>Decodes a tree from the binary format.</summary>
    /// <param name="data">The encoded bytes.</param>
    /// <param name="options">Optional decoding limits.</param>
    /// <returns>The decoded tree.</returns>
    public static ReplicatedTree ReadFrom(byte[] data, CrdtReaderOptions? options = null)
    {
        Throw.IfNull(data);
        return ReadFrom(data.AsSpan(), options);
    }

    /// <inheritdoc/>
    public bool Equals(ReplicatedTree? other)
    {
        return other is not null && TreeEquals(other) && LogEquals(other._log);
    }

    /// <inheritdoc/>
    public override bool Equals([NotNullWhen(true)] object? obj) => Equals(obj as ReplicatedTree);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (KeyValuePair<string, (string Parent, string Meta)> pair in OrderedTree())
        {
            hash.Add(pair.Key, StringComparer.Ordinal);
            hash.Add(pair.Value.Parent, StringComparer.Ordinal);
            hash.Add(pair.Value.Meta, StringComparer.Ordinal);
        }

        foreach (LogMove move in _log)
        {
            hash.Add(move);
        }

        return hash.ToHashCode();
    }

    private LogMove Do(TreeMoveOperation operation)
    {
        _tree.TryGetValue(operation.Child, out (string Parent, string Meta) old);
        bool hadOld = _tree.ContainsKey(operation.Child);
        if (StringComparer.Ordinal.Equals(operation.Child, operation.NewParent) ||
            IsDescendantOf(operation.NewParent, operation.Child))
        {
            return new LogMove(
                operation.Timestamp,
                operation.Child,
                hadOld ? old.Parent : null,
                hadOld ? old.Meta : null,
                operation.NewParent,
                operation.Meta,
                skipped: true);
        }

        _tree[operation.Child] = (operation.NewParent, operation.Meta);
        return new LogMove(
            operation.Timestamp,
            operation.Child,
            hadOld ? old.Parent : null,
            hadOld ? old.Meta : null,
            operation.NewParent,
            operation.Meta,
            skipped: false);
    }

    private void Undo(LogMove move)
    {
        if (move.Skipped)
        {
            return;
        }

        if (move.OldParent is null)
        {
            _tree.Remove(move.Child);
        }
        else
        {
            _tree[move.Child] = (move.OldParent, move.OldMeta!);
        }
    }

    private bool IsDescendantOf(string node, string ancestor)
    {
        string current = node;
        HashSet<string> seen = [];
        while (_tree.TryGetValue(current, out (string Parent, string Meta) entry) && seen.Add(current))
        {
            if (StringComparer.Ordinal.Equals(entry.Parent, ancestor))
            {
                return true;
            }

            current = entry.Parent;
        }

        return false;
    }

    private bool ContainsTimestamp(MoveTimestamp timestamp)
    {
        int index = FindInsertIndex(timestamp);
        return index < _log.Count && _log[index].Timestamp == timestamp;
    }

    private int FindInsertIndex(MoveTimestamp timestamp)
    {
        int low = 0;
        int high = _log.Count;
        while (low < high)
        {
            int mid = low + ((high - low) / 2);
            if (_log[mid].Timestamp < timestamp)
            {
                low = mid + 1;
            }
            else
            {
                high = mid;
            }
        }

        return low;
    }

    private ulong MaxSeenCounter()
    {
        ulong max = _counter;
        foreach (LogMove move in _log)
        {
            max = Math.Max(max, move.Timestamp.Counter);
        }

        return max;
    }

    private Dictionary<string, (string Parent, string Meta)> CloneTree()
    {
        return new Dictionary<string, (string Parent, string Meta)>(_tree, StringComparer.Ordinal);
    }

    private IEnumerable<KeyValuePair<string, (string Parent, string Meta)>> OrderedTree()
    {
        var keys = new List<string>(_tree.Keys);
        keys.Sort(StringComparer.Ordinal);
        foreach (string key in keys)
        {
            yield return new KeyValuePair<string, (string Parent, string Meta)>(key, _tree[key]);
        }
    }

    private bool TreeEquals(ReplicatedTree other)
    {
        if (_tree.Count != other._tree.Count)
        {
            return false;
        }

        foreach (KeyValuePair<string, (string Parent, string Meta)> pair in _tree)
        {
            if (!other._tree.TryGetValue(pair.Key, out (string Parent, string Meta) otherEntry) ||
                !StringComparer.Ordinal.Equals(pair.Value.Parent, otherEntry.Parent) ||
                !StringComparer.Ordinal.Equals(pair.Value.Meta, otherEntry.Meta))
            {
                return false;
            }
        }

        return true;
    }

    private bool LogEquals(List<LogMove> other)
    {
        if (_log.Count != other.Count)
        {
            return false;
        }

        for (int i = 0; i < _log.Count; i++)
        {
            if (!_log[i].Equals(other[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsTimestampSubsetOf(List<LogMove> left, List<LogMove> right)
    {
        int i = 0;
        int j = 0;
        while (i < left.Count && j < right.Count)
        {
            int comparison = left[i].Timestamp.CompareTo(right[j].Timestamp);
            if (comparison == 0)
            {
                i++;
                j++;
            }
            else if (comparison > 0)
            {
                j++;
            }
            else
            {
                return false;
            }
        }

        return i == left.Count;
    }
}

/// <summary>
/// A recorded move in a <see cref="ReplicatedTree"/> log.
/// </summary>
public readonly record struct LogMove
{
    /// <summary>Initializes a new <see cref="LogMove"/>.</summary>
    /// <param name="timestamp">The operation timestamp.</param>
    /// <param name="child">The moved child node id.</param>
    /// <param name="oldParent">The previous parent, or <see langword="null"/> if the child was absent.</param>
    /// <param name="oldMeta">The previous metadata, or <see langword="null"/> if the child was absent.</param>
    /// <param name="newParent">The requested new parent node id.</param>
    /// <param name="newMeta">The requested new metadata.</param>
    /// <param name="skipped">Whether the move was skipped to preserve acyclicity.</param>
    public LogMove(
        MoveTimestamp timestamp,
        string child,
        string? oldParent,
        string? oldMeta,
        string newParent,
        string newMeta,
        bool skipped)
    {
        Throw.IfNull(child);
        Throw.IfNull(newParent);
        Throw.IfNull(newMeta);
        Timestamp = timestamp;
        Child = child;
        OldParent = oldParent;
        OldMeta = oldMeta;
        NewParent = newParent;
        NewMeta = newMeta;
        Skipped = skipped;
    }

    /// <summary>Gets the operation timestamp.</summary>
    public MoveTimestamp Timestamp { get; }

    /// <summary>Gets the moved child node id.</summary>
    public string Child { get; }

    /// <summary>Gets the previous parent, or <see langword="null"/> if the child was absent.</summary>
    public string? OldParent { get; }

    /// <summary>Gets the previous metadata, or <see langword="null"/> if the child was absent.</summary>
    public string? OldMeta { get; }

    /// <summary>Gets the requested new parent node id.</summary>
    public string NewParent { get; }

    /// <summary>Gets the requested new metadata.</summary>
    public string NewMeta { get; }

    /// <summary>Gets a value indicating whether the move was skipped to preserve acyclicity.</summary>
    public bool Skipped { get; }

    /// <summary>Writes this log entry to the binary writer.</summary>
    /// <param name="writer">The destination writer.</param>
    public void Write(ref CrdtWriter writer)
    {
        Timestamp.Write(ref writer);
        writer.WriteString(Child);
        writer.WriteString(OldParent);
        writer.WriteString(OldMeta);
        writer.WriteString(NewParent);
        writer.WriteString(NewMeta);
        writer.WriteBool(Skipped);
    }

    /// <summary>Reads a log entry from the binary reader.</summary>
    /// <param name="reader">The source reader.</param>
    /// <returns>The decoded log entry.</returns>
    public static LogMove Read(ref CrdtReader reader)
    {
        MoveTimestamp timestamp = MoveTimestamp.Read(ref reader);
        string child = reader.ReadString() ?? Throw.InvalidData<string>("Log child cannot be null.");
        string? oldParent = reader.ReadString();
        string? oldMeta = reader.ReadString();
        string newParent = reader.ReadString() ?? Throw.InvalidData<string>("Log new parent cannot be null.");
        string newMeta = reader.ReadString() ?? Throw.InvalidData<string>("Log metadata cannot be null.");
        bool skipped = reader.ReadBool();
        return new LogMove(timestamp, child, oldParent, oldMeta, newParent, newMeta, skipped);
    }

    internal TreeMoveOperation ToOperation() => new(Timestamp, Child, NewParent, NewMeta);
}

