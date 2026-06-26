// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;

namespace Crdt;

/// <summary>
/// A multi-value register: a causally-tracked CRDT that stores every concurrently assigned
/// value while causal assignments replace the values observed by the assigning replica.
/// </summary>
/// <typeparam name="T">The stored value type.</typeparam>
/// <remarks>Mutable and not thread-safe.</remarks>
public sealed class MVRegister<T> :
    IConvergent<MVRegister<T>>,
    IDeltaConvergent<MVRegister<T>, MVRegister<T>>,
    IOperationConvergent<MVRegisterOperation<T>>,
    IEquatable<MVRegister<T>>
    where T : notnull
{
    private readonly DotKernel<T> _kernel;
    private MVRegister<T>? _delta;

    /// <summary>Initializes an empty multi-value register.</summary>
    public MVRegister() => _kernel = new DotKernel<T>();

    private MVRegister(DotKernel<T> kernel) => _kernel = kernel;

    /// <summary>Gets the number of live assignments in the register.</summary>
    public int Count => _kernel.Count;

    /// <summary>Gets the distinct live values currently present in the register.</summary>
    public IReadOnlyCollection<T> Values => new HashSet<T>(_kernel.Values);

    /// <summary>
    /// Assigns <paramref name="value"/> under a fresh dot from <paramref name="replica"/>,
    /// overwriting values this replica has observed while preserving concurrent assignments.
    /// </summary>
    /// <param name="replica">The assigning replica.</param>
    /// <param name="value">The assigned value.</param>
    /// <returns>The operation describing the assignment.</returns>
    public MVRegisterOperation<T> Assign(ReplicaId replica, T value)
    {
        var liveDots = new List<Dot>(_kernel.Entries.Keys);
        foreach (Dot dot in liveDots)
        {
            _kernel.RemoveDot(dot);
        }

        Dot newDot = _kernel.Add(replica, value);
        _delta = Clone();
        return new MVRegisterOperation<T>(newDot, value, _kernel.Context.Clone());
    }

    /// <inheritdoc/>
    public void Merge(MVRegister<T> other)
    {
        Throw.IfNull(other);
        _kernel.Merge(other._kernel);
    }

    /// <inheritdoc/>
    public CrdtOrder Compare(MVRegister<T> other)
    {
        Throw.IfNull(other);
        MVRegister<T> left = Clone();
        left.Merge(other);
        bool thisGreaterOrEqual = left.Equals(this);

        MVRegister<T> right = other.Clone();
        right.Merge(this);
        bool otherGreaterOrEqual = right.Equals(other);

        return (otherGreaterOrEqual, thisGreaterOrEqual) switch
        {
            (true, true) => CrdtOrder.Equal,
            (true, false) => CrdtOrder.Less,
            (false, true) => CrdtOrder.Greater,
            _ => CrdtOrder.Concurrent,
        };
    }

    /// <inheritdoc/>
    public MVRegister<T> Clone() => new(_kernel.Clone());

    /// <inheritdoc/>
    public bool TryExtractDelta([MaybeNullWhen(false)] out MVRegister<T> delta)
    {
        if (_delta is null)
        {
            delta = null;
            return false;
        }

        delta = _delta;
        _delta = null;
        return true;
    }

    /// <inheritdoc/>
    public void MergeDelta(MVRegister<T> delta)
    {
        Throw.IfNull(delta);
        Merge(delta);
    }

    /// <inheritdoc/>
    public bool Apply(MVRegisterOperation<T> operation)
    {
        var delta = new MVRegister<T>();
        delta._kernel.Insert(operation.Dot, operation.Value!);
        delta._kernel.Context.Merge(operation.Context);
        var before = Clone();
        Merge(delta);
        return !Equals(before);
    }

    /// <summary>Serializes the register to the binary format using <paramref name="serializer"/>.</summary>
    /// <param name="output">The destination buffer writer.</param>
    /// <param name="serializer">The value serializer.</param>
    public void WriteTo(IBufferWriter<byte> output, ICrdtValueSerializer<T> serializer)
    {
        Throw.IfNull(serializer);
        var writer = new CrdtWriter(output);
        Write(ref writer, serializer);
    }

    /// <summary>Serializes the register to a new byte array using <paramref name="serializer"/>.</summary>
    /// <param name="serializer">The value serializer.</param>
    /// <returns>The encoded bytes.</returns>
    public byte[] ToByteArray(ICrdtValueSerializer<T> serializer)
    {
        using var buffer = new PooledBufferWriter();
        WriteTo(buffer, serializer);
        return buffer.WrittenSpan.ToArray();
    }

    internal void Write(ref CrdtWriter writer, ICrdtValueSerializer<T> serializer)
    {
        writer.WriteVarUInt64((ulong)_kernel.Count);
        foreach (KeyValuePair<Dot, T> entry in _kernel.SortedEntries())
        {
            writer.WriteDot(entry.Key);
            serializer.Write(ref writer, entry.Value);
        }

        _kernel.Context.Write(ref writer);
    }

    /// <summary>Decodes a register from the binary format using <paramref name="serializer"/>.</summary>
    /// <param name="data">The encoded bytes.</param>
    /// <param name="serializer">The value serializer.</param>
    /// <param name="options">Optional decoding limits.</param>
    /// <returns>The decoded register.</returns>
    public static MVRegister<T> ReadFrom(
        ReadOnlySpan<byte> data,
        ICrdtValueSerializer<T> serializer,
        CrdtReaderOptions? options = null)
    {
        Throw.IfNull(serializer);
        var reader = new CrdtReader(data, options);
        return Read(ref reader, serializer);
    }

    internal static MVRegister<T> Read(ref CrdtReader reader, ICrdtValueSerializer<T> serializer)
    {
        var register = new MVRegister<T>();
        int count = reader.ReadCount();
        for (int i = 0; i < count; i++)
        {
            Dot dot = reader.ReadDot();
            T value = serializer.Read(ref reader);
            register._kernel.Insert(dot, value);
        }

        register._kernel.Context.Merge(DotContext.Read(ref reader));
        return register;
    }

    /// <summary>Serializes the register to JSON using <paramref name="serializer"/>.</summary>
    /// <param name="serializer">The value serializer.</param>
    /// <returns>The JSON string.</returns>
    public string ToJson(ICrdtValueSerializer<T> serializer)
    {
        Throw.IfNull(serializer);
        using var buffer = new PooledBufferWriter();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WritePropertyName("entries");
            writer.WriteStartArray();
            foreach (KeyValuePair<Dot, T> entry in _kernel.SortedEntries())
            {
                writer.WriteStartObject();
                WriteDotJson(writer, entry.Key);
                writer.WritePropertyName("value");
                serializer.WriteJson(writer, entry.Value);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            WriteContextJson(writer, _kernel.Context);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    /// <summary>Deserializes a register from JSON using <paramref name="serializer"/>.</summary>
    /// <param name="json">The JSON string.</param>
    /// <param name="serializer">The value serializer.</param>
    /// <returns>The decoded register.</returns>
    public static MVRegister<T> FromJson(string json, ICrdtValueSerializer<T> serializer)
    {
        Throw.IfNull(json);
        Throw.IfNull(serializer);
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json));
        var register = new MVRegister<T>();
        DotContext? context = null;

        reader.Read();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            string? propertyName = reader.GetString();
            reader.Read();
            if (propertyName == "entries")
            {
                ReadEntriesJson(ref reader, serializer, register);
            }
            else if (propertyName == "context")
            {
                context = ReadContextJson(ref reader);
            }
            else
            {
                reader.Skip();
            }
        }

        if (context is not null)
        {
            register._kernel.Context.Merge(context);
        }

        return register;
    }

    /// <inheritdoc/>
    public bool Equals(MVRegister<T>? other)
    {
        if (other is null)
        {
            return false;
        }

        return new HashSet<T>(_kernel.Values).SetEquals(other._kernel.Values);
    }

    /// <inheritdoc/>
    public override bool Equals([NotNullWhen(true)] object? obj) => Equals(obj as MVRegister<T>);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        int hash = 0;
        foreach (T value in new HashSet<T>(_kernel.Values))
        {
            hash ^= value?.GetHashCode() ?? 0;
        }

        return hash;
    }

    private static void ReadEntriesJson(
        ref Utf8JsonReader reader,
        ICrdtValueSerializer<T> serializer,
        MVRegister<T> register)
    {
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            Dot dot = default;
            T? value = default;
            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                string? propertyName = reader.GetString();
                reader.Read();
                if (propertyName == "dot")
                {
                    dot = ReadDotJson(ref reader);
                }
                else if (propertyName == "value")
                {
                    value = serializer.ReadJson(ref reader);
                }
                else
                {
                    reader.Skip();
                }
            }

            register._kernel.Insert(dot, value!);
        }
    }

    private static void WriteContextJson(Utf8JsonWriter writer, DotContext context)
    {
        writer.WritePropertyName("context");
        writer.WriteStartObject();
        writer.WritePropertyName("compact");
        writer.WriteStartArray();
        foreach (KeyValuePair<ReplicaId, ulong> entry in context.CompactEntries())
        {
            writer.WriteStartObject();
            writer.WriteString("replica", entry.Key.Value);
            writer.WriteNumber("sequence", entry.Value);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WritePropertyName("cloud");
        writer.WriteStartArray();
        foreach (Dot dot in context.CloudDots())
        {
            writer.WriteStartObject();
            WriteDotJson(writer, dot);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static DotContext ReadContextJson(ref Utf8JsonReader reader)
    {
        var context = new DotContext();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            string? propertyName = reader.GetString();
            reader.Read();
            if (propertyName == "compact")
            {
                ReadCompactJson(ref reader, context);
            }
            else if (propertyName == "cloud")
            {
                ReadCloudJson(ref reader, context);
            }
            else
            {
                reader.Skip();
            }
        }

        return context;
    }

    private static void ReadCompactJson(ref Utf8JsonReader reader, DotContext context)
    {
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            ReplicaId replica = ReplicaId.Empty;
            ulong sequence = 0UL;
            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                string? propertyName = reader.GetString();
                reader.Read();
                if (propertyName == "replica")
                {
                    replica = new ReplicaId(reader.GetGuid());
                }
                else if (propertyName == "sequence")
                {
                    sequence = reader.GetUInt64();
                }
                else
                {
                    reader.Skip();
                }
            }

            for (ulong i = 1UL; i <= sequence; i++)
            {
                context.Add(new Dot(replica, i));
            }
        }
    }

    private static void ReadCloudJson(ref Utf8JsonReader reader, DotContext context)
    {
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            Dot dot = default;
            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                string? propertyName = reader.GetString();
                reader.Read();
                if (propertyName == "dot")
                {
                    dot = ReadDotJson(ref reader);
                }
                else
                {
                    reader.Skip();
                }
            }

            context.Add(dot);
        }
    }

    private static void WriteDotJson(Utf8JsonWriter writer, Dot dot)
    {
        writer.WritePropertyName("dot");
        writer.WriteStartObject();
        writer.WriteString("replica", dot.Replica.Value);
        writer.WriteNumber("sequence", dot.Sequence);
        writer.WriteEndObject();
    }

    private static Dot ReadDotJson(ref Utf8JsonReader reader)
    {
        ReplicaId replica = ReplicaId.Empty;
        ulong sequence = 0UL;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            string? propertyName = reader.GetString();
            reader.Read();
            if (propertyName == "replica")
            {
                replica = new ReplicaId(reader.GetGuid());
            }
            else if (propertyName == "sequence")
            {
                sequence = reader.GetUInt64();
            }
            else
            {
                reader.Skip();
            }
        }

        return new Dot(replica, sequence);
    }
}
