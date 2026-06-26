// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;

namespace Crdt;

/// <summary>
/// The marker operation broadcast by a <see cref="GFlag"/> when it becomes enabled.
/// </summary>
public readonly struct GFlagOperation : IEquatable<GFlagOperation>, IBinaryWritable
{
    /// <inheritdoc/>
    public void Write(ref CrdtWriter writer) => writer.WriteByte(1);

    /// <summary>Decodes a <see cref="GFlagOperation"/> from its binary representation.</summary>
    /// <param name="data">The encoded bytes.</param>
    /// <param name="options">Optional decoding limits.</param>
    /// <returns>The decoded operation.</returns>
    public static GFlagOperation ReadFrom(ReadOnlySpan<byte> data, CrdtReaderOptions? options = null)
    {
        var reader = new CrdtReader(data, options);
        return Read(ref reader);
    }

    internal static GFlagOperation Read(ref CrdtReader reader)
    {
        byte marker = reader.ReadByte();
        if (marker != 1)
        {
            Throw.InvalidData<GFlagOperation>("Invalid GFlag operation marker.");
        }

        return default;
    }

    /// <inheritdoc/>
    public bool Equals(GFlagOperation other) => true;

    /// <inheritdoc/>
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is GFlagOperation;

    /// <inheritdoc/>
    public override int GetHashCode() => 1;

    /// <summary>Equality operator.</summary>
    public static bool operator ==(GFlagOperation left, GFlagOperation right) => left.Equals(right);

    /// <summary>Inequality operator.</summary>
    public static bool operator !=(GFlagOperation left, GFlagOperation right) => !left.Equals(right);
}
