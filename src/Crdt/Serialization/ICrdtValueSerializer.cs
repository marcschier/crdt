// Copyright (c) marcschier. Licensed under the MIT License.

using System.Text.Json;

namespace Crdt;

/// <summary>
/// A reflection-free codec for an element or value type stored inside a generic CRDT (set
/// element, register value, map key). It encodes a value to and from both the compact CRDT
/// binary format and JSON, which is what keeps generic CRDT serialization NativeAOT-safe.
/// Built-in implementations are exposed by <see cref="CrdtValues"/>; consumers supply their
/// own for custom types.
/// </summary>
/// <typeparam name="T">The value type.</typeparam>
public interface ICrdtValueSerializer<T>
{
    /// <summary>Writes <paramref name="value"/> to the binary writer.</summary>
    /// <param name="writer">The destination writer.</param>
    /// <param name="value">The value to write.</param>
    void Write(ref CrdtWriter writer, T value);

    /// <summary>Reads a value from the binary reader.</summary>
    /// <param name="reader">The source reader.</param>
    /// <returns>The decoded value.</returns>
    T Read(ref CrdtReader reader);

    /// <summary>Writes <paramref name="value"/> as a single JSON value.</summary>
    /// <param name="writer">The JSON writer.</param>
    /// <param name="value">The value to write.</param>
    void WriteJson(Utf8JsonWriter writer, T value);

    /// <summary>Reads a value from the JSON reader, which is positioned on the value's token.</summary>
    /// <param name="reader">The JSON reader.</param>
    /// <returns>The decoded value.</returns>
    T ReadJson(ref Utf8JsonReader reader);
}
