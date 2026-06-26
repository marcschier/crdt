// Copyright (c) marcschier. Licensed under the MIT License.

using System.Text.Json;

namespace Crdt;

/// <summary>
/// Ready-made <see cref="ICrdtValueSerializer{T}"/> implementations for the primitive value
/// types most commonly stored in CRDTs. Pass one to a generic CRDT's binary or JSON
/// serialization methods.
/// </summary>
public static class CrdtValues
{
    /// <summary>A serializer for <see cref="string"/> values (non-null; nulls decode to empty).</summary>
    public static ICrdtValueSerializer<string> String { get; } = new StringSerializer();

    /// <summary>A serializer for <see cref="long"/> values.</summary>
    public static ICrdtValueSerializer<long> Int64 { get; } = new Int64Serializer();

    /// <summary>A serializer for <see cref="ulong"/> values.</summary>
    public static ICrdtValueSerializer<ulong> UInt64 { get; } = new UInt64Serializer();

    /// <summary>A serializer for <see cref="int"/> values.</summary>
    public static ICrdtValueSerializer<int> Int32 { get; } = new Int32Serializer();

    /// <summary>A serializer for <see cref="bool"/> values.</summary>
    public static ICrdtValueSerializer<bool> Boolean { get; } = new BooleanSerializer();

    /// <summary>A serializer for <see cref="System.Guid"/> values.</summary>
    public static ICrdtValueSerializer<Guid> Guid { get; } = new GuidSerializer();

    /// <summary>A serializer for <see cref="ReplicaId"/> values.</summary>
    public static ICrdtValueSerializer<ReplicaId> Replica { get; } = new ReplicaIdSerializer();

    private sealed class StringSerializer : ICrdtValueSerializer<string>
    {
        public void Write(ref CrdtWriter writer, string value) => writer.WriteString(value);

        public string Read(ref CrdtReader reader) => reader.ReadString() ?? string.Empty;

        public void WriteJson(Utf8JsonWriter writer, string value) => writer.WriteStringValue(value);

        public string ReadJson(ref Utf8JsonReader reader) => reader.GetString() ?? string.Empty;
    }

    private sealed class Int64Serializer : ICrdtValueSerializer<long>
    {
        public void Write(ref CrdtWriter writer, long value) => writer.WriteVarInt64(value);

        public long Read(ref CrdtReader reader) => reader.ReadVarInt64();

        public void WriteJson(Utf8JsonWriter writer, long value) => writer.WriteNumberValue(value);

        public long ReadJson(ref Utf8JsonReader reader) => reader.GetInt64();
    }

    private sealed class UInt64Serializer : ICrdtValueSerializer<ulong>
    {
        public void Write(ref CrdtWriter writer, ulong value) => writer.WriteVarUInt64(value);

        public ulong Read(ref CrdtReader reader) => reader.ReadVarUInt64();

        public void WriteJson(Utf8JsonWriter writer, ulong value) => writer.WriteNumberValue(value);

        public ulong ReadJson(ref Utf8JsonReader reader) => reader.GetUInt64();
    }

    private sealed class Int32Serializer : ICrdtValueSerializer<int>
    {
        public void Write(ref CrdtWriter writer, int value) => writer.WriteVarInt64(value);

        public int Read(ref CrdtReader reader) => checked((int)reader.ReadVarInt64());

        public void WriteJson(Utf8JsonWriter writer, int value) => writer.WriteNumberValue(value);

        public int ReadJson(ref Utf8JsonReader reader) => reader.GetInt32();
    }

    private sealed class BooleanSerializer : ICrdtValueSerializer<bool>
    {
        public void Write(ref CrdtWriter writer, bool value) => writer.WriteBool(value);

        public bool Read(ref CrdtReader reader) => reader.ReadBool();

        public void WriteJson(Utf8JsonWriter writer, bool value) => writer.WriteBooleanValue(value);

        public bool ReadJson(ref Utf8JsonReader reader) => reader.GetBoolean();
    }

    private sealed class GuidSerializer : ICrdtValueSerializer<Guid>
    {
        public void Write(ref CrdtWriter writer, Guid value) => writer.WriteGuid(value);

        public Guid Read(ref CrdtReader reader) => reader.ReadGuid();

        public void WriteJson(Utf8JsonWriter writer, Guid value) => writer.WriteStringValue(value);

        public Guid ReadJson(ref Utf8JsonReader reader) => reader.GetGuid();
    }

    private sealed class ReplicaIdSerializer : ICrdtValueSerializer<ReplicaId>
    {
        public void Write(ref CrdtWriter writer, ReplicaId value) => writer.WriteReplicaId(value);

        public ReplicaId Read(ref CrdtReader reader) => reader.ReadReplicaId();

        public void WriteJson(Utf8JsonWriter writer, ReplicaId value) => writer.WriteStringValue(value.Value);

        public ReplicaId ReadJson(ref Utf8JsonReader reader) => new(reader.GetGuid());
    }
}
