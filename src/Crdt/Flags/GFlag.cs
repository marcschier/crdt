// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace Crdt;

/// <summary>
/// A grow-only event flag: a boolean CRDT that starts disabled and can only move from
/// <see langword="false"/> to <see langword="true"/>.
/// </summary>
/// <remarks>
/// Merging uses logical OR, so <see langword="true"/> wins over <see langword="false"/>.
/// Mutable and not thread-safe.
/// </remarks>
public sealed class GFlag :
    IConvergent<GFlag>,
    IDeltaConvergent<GFlag, GFlag>,
    IOperationConvergent<GFlagOperation>,
    IBinaryWritable,
    IEquatable<GFlag>
{
    private bool _value;
    private bool _delta;

    /// <summary>Initializes a disabled grow-only flag.</summary>
    public GFlag()
    {
    }

    private GFlag(bool value) => _value = value;

    /// <summary>Gets a value indicating whether the flag is enabled.</summary>
    public bool Value => _value;

    /// <summary>Enables the flag and returns the operation to broadcast.</summary>
    /// <returns>The enable marker operation.</returns>
    public GFlagOperation Enable()
    {
        if (!_value)
        {
            _value = true;
            _delta = true;
        }

        return default;
    }

    /// <inheritdoc/>
    public void Merge(GFlag other)
    {
        Throw.IfNull(other);
        _value |= other._value;
    }

    /// <inheritdoc/>
    public CrdtOrder Compare(GFlag other)
    {
        Throw.IfNull(other);
        return (_value, other._value) switch
        {
            (false, true) => CrdtOrder.Less,
            (true, false) => CrdtOrder.Greater,
            _ => CrdtOrder.Equal,
        };
    }

    /// <inheritdoc/>
    public GFlag Clone() => new(_value);

    /// <inheritdoc/>
    public bool TryExtractDelta([MaybeNullWhen(false)] out GFlag delta)
    {
        if (!_delta)
        {
            delta = null;
            return false;
        }

        delta = new GFlag(true);
        _delta = false;
        return true;
    }

    /// <inheritdoc/>
    public void MergeDelta(GFlag delta)
    {
        Throw.IfNull(delta);
        Merge(delta);
    }

    /// <inheritdoc/>
    public bool Apply(GFlagOperation operation)
    {
        if (_value)
        {
            return false;
        }

        _value = true;
        return true;
    }

    /// <inheritdoc/>
    public void Write(ref CrdtWriter writer) => writer.WriteBool(_value);

    /// <summary>Decodes a <see cref="GFlag"/> from its binary representation.</summary>
    /// <param name="data">The encoded bytes.</param>
    /// <param name="options">Optional decoding limits.</param>
    /// <returns>The decoded flag.</returns>
    public static GFlag ReadFrom(ReadOnlySpan<byte> data, CrdtReaderOptions? options = null)
    {
        var reader = new CrdtReader(data, options);
        return new GFlag(reader.ReadBool());
    }

    /// <summary>Serializes this flag to its canonical JSON representation.</summary>
    /// <returns>The JSON string.</returns>
    public string ToJson()
    {
        if (!_value)
        {
            return "null";
        }

        return JsonSerializer.Serialize(new GFlagDto(), CrdtFlagsJson.Default.GFlagDto);
    }

    /// <summary>Deserializes a <see cref="GFlag"/> from JSON.</summary>
    /// <param name="json">The JSON string.</param>
    /// <returns>The decoded flag.</returns>
    public static GFlag FromJson(string json)
    {
        Throw.IfNull(json);
        GFlagDto? dto = JsonSerializer.Deserialize(json, CrdtFlagsJson.Default.GFlagDto);
        return new GFlag(dto is not null);
    }

    /// <inheritdoc/>
    public bool Equals(GFlag? other) => other is not null && _value == other._value;

    /// <inheritdoc/>
    public override bool Equals([NotNullWhen(true)] object? obj) => Equals(obj as GFlag);

    /// <inheritdoc/>
    public override int GetHashCode() => _value.GetHashCode();
}
