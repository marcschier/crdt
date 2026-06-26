// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace Crdt;

/// <summary>
/// A collaborative text document: a convenience CRDT for concurrently-edited strings, built
/// on an <see cref="Rga{T}"/> of UTF-16 code units. Inserts and deletes from multiple
/// replicas converge to a single consistent string.
/// </summary>
/// <remarks>
/// <see cref="Text"/> exposes a state-based (merge) interface for replication; for
/// operation-based or delta-state replication at character granularity, use the underlying
/// <see cref="Rga{T}"/> directly. Mutable and not thread-safe.
/// </remarks>
public sealed class Text : IConvergent<Text>, IEquatable<Text>
{
    private readonly Rga<char> _rga;

    /// <summary>Initializes an empty document.</summary>
    public Text() => _rga = new Rga<char>();

    private Text(Rga<char> rga) => _rga = rga;

    /// <summary>Gets the number of characters in the document.</summary>
    public int Length => _rga.Count;

    /// <summary>Gets the character at <paramref name="index"/>.</summary>
    /// <param name="index">The zero-based character index.</param>
    public char this[int index] => _rga[index];

    /// <summary>Gets the current document content as a string.</summary>
    public string Value => new(_rga.ToArray());

    /// <summary>Inserts a string at <paramref name="index"/> on behalf of a replica.</summary>
    /// <param name="replica">The local replica.</param>
    /// <param name="index">The character position at which to insert (0..Length).</param>
    /// <param name="text">The text to insert.</param>
    public void Insert(ReplicaId replica, int index, string text)
    {
        Throw.IfNull(text);
        for (int i = 0; i < text.Length; i++)
        {
            _rga.Insert(replica, index + i, text[i]);
        }
    }

    /// <summary>Appends a string on behalf of a replica.</summary>
    /// <param name="replica">The local replica.</param>
    /// <param name="text">The text to append.</param>
    public void Append(ReplicaId replica, string text) => Insert(replica, Length, text);

    /// <summary>Deletes <paramref name="count"/> characters starting at <paramref name="index"/>.</summary>
    /// <param name="index">The first character position to delete.</param>
    /// <param name="count">The number of characters to delete.</param>
    public void Delete(int index, int count = 1)
    {
        for (int i = 0; i < count; i++)
        {
            _rga.Delete(index);
        }
    }

    /// <inheritdoc/>
    public void Merge(Text other)
    {
        Throw.IfNull(other);
        _rga.Merge(other._rga);
    }

    /// <inheritdoc/>
    public CrdtOrder Compare(Text other)
    {
        Throw.IfNull(other);
        return _rga.Compare(other._rga);
    }

    /// <inheritdoc/>
    public Text Clone() => new(_rga.Clone());

    /// <summary>Serializes the document to the binary format.</summary>
    /// <returns>The encoded bytes.</returns>
    public byte[] ToByteArray() => _rga.ToByteArray(CharSerializer.Instance);

    /// <summary>Decodes a document from the binary format.</summary>
    /// <param name="data">The encoded bytes.</param>
    /// <param name="options">Optional decoding limits.</param>
    /// <returns>The decoded document.</returns>
    public static Text ReadFrom(ReadOnlySpan<byte> data, CrdtReaderOptions? options = null) =>
        new(Rga<char>.ReadFrom(data, CharSerializer.Instance, options));

    /// <summary>Serializes the document to JSON.</summary>
    /// <returns>The JSON string.</returns>
    public string ToJson() => _rga.ToJson(CharSerializer.Instance);

    /// <summary>Decodes a document from JSON.</summary>
    /// <param name="json">The JSON string.</param>
    /// <returns>The decoded document.</returns>
    public static Text FromJson(string json) => new(Rga<char>.FromJson(json, CharSerializer.Instance));

    /// <inheritdoc/>
    public override string ToString() => Value;

    /// <inheritdoc/>
    public bool Equals(Text? other) => other is not null && _rga.Equals(other._rga);

    /// <inheritdoc/>
    public override bool Equals([NotNullWhen(true)] object? obj) => Equals(obj as Text);

    /// <inheritdoc/>
    public override int GetHashCode() => _rga.GetHashCode();

    private sealed class CharSerializer : ICrdtValueSerializer<char>
    {
        public static CharSerializer Instance { get; } = new();

        public void Write(ref CrdtWriter writer, char value) => writer.WriteVarUInt64(value);

        public char Read(ref CrdtReader reader) => (char)reader.ReadVarUInt64();

        public void WriteJson(Utf8JsonWriter writer, char value) => writer.WriteStringValue(value.ToString());

        public char ReadJson(ref Utf8JsonReader reader)
        {
            string? text = reader.GetString();
            return text is null or "" ? '\0' : text[0];
        }
    }
}
