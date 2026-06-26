// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;

namespace Crdt;

/// <summary>
/// An operation broadcast by a <see cref="DisableWinsFlag"/> when it is enabled or disabled.
/// </summary>
public readonly struct DisableWinsFlagOperation :
    IEquatable<DisableWinsFlagOperation>,
    IBinaryWritable
{
    private const byte EnableKind = 1;
    private const byte DisableKind = 2;
    private readonly Dot[]? _dots;

    private DisableWinsFlagOperation(byte kind, Dot dot, Dot[] dots)
    {
        Kind = kind;
        Dot = dot;
        _dots = dots;
    }

    /// <summary>Gets a value indicating whether this operation enables the flag.</summary>
    public bool IsEnable => Kind == EnableKind;

    /// <summary>Gets the dot introduced by a disable operation.</summary>
    public Dot Dot { get; }

    /// <summary>Gets the disabled-marker dots observed and removed by an enable operation.</summary>
    public IReadOnlyList<Dot> Dots => _dots ?? Array.Empty<Dot>();

    internal byte Kind { get; }

    internal static DisableWinsFlagOperation Enable(IEnumerable<Dot> dots) =>
        new(EnableKind, default, ObservedFlagKernel.SortDistinct(dots));

    internal static DisableWinsFlagOperation Disable(Dot dot) => new(DisableKind, dot, Array.Empty<Dot>());

    /// <inheritdoc/>
    public void Write(ref CrdtWriter writer)
    {
        writer.WriteByte(Kind);
        if (!IsEnable)
        {
            writer.WriteDot(Dot);
            return;
        }

        Dot[] dots = _dots ?? Array.Empty<Dot>();
        writer.WriteVarUInt64((ulong)dots.Length);
        foreach (Dot dot in dots)
        {
            writer.WriteDot(dot);
        }
    }

    /// <summary>
    /// Decodes a <see cref="DisableWinsFlagOperation"/> from its binary representation.
    /// </summary>
    /// <param name="data">The encoded bytes.</param>
    /// <param name="options">Optional decoding limits.</param>
    /// <returns>The decoded operation.</returns>
    public static DisableWinsFlagOperation ReadFrom(
        ReadOnlySpan<byte> data,
        CrdtReaderOptions? options = null)
    {
        var reader = new CrdtReader(data, options);
        return Read(ref reader);
    }

    internal static DisableWinsFlagOperation Read(ref CrdtReader reader)
    {
        byte kind = reader.ReadByte();
        if (kind == DisableKind)
        {
            return Disable(reader.ReadDot());
        }

        if (kind != EnableKind)
        {
            Throw.InvalidData<DisableWinsFlagOperation>("Invalid disable-wins flag operation kind.");
        }

        int count = reader.ReadCount();
        var dots = new Dot[count];
        for (int i = 0; i < count; i++)
        {
            dots[i] = reader.ReadDot();
        }

        return new DisableWinsFlagOperation(EnableKind, default, dots);
    }

    /// <inheritdoc/>
    public bool Equals(DisableWinsFlagOperation other) =>
        Kind == other.Kind && Dot.Equals(other.Dot) && ObservedFlagKernel.SequenceEqual(Dots, other.Dots);

    /// <inheritdoc/>
    public override bool Equals([NotNullWhen(true)] object? obj) =>
        obj is DisableWinsFlagOperation other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Kind);
        hash.Add(Dot);
        foreach (Dot dot in Dots)
        {
            hash.Add(dot);
        }

        return hash.ToHashCode();
    }

    /// <summary>Equality operator.</summary>
    public static bool operator ==(DisableWinsFlagOperation left, DisableWinsFlagOperation right) =>
        left.Equals(right);

    /// <summary>Inequality operator.</summary>
    public static bool operator !=(DisableWinsFlagOperation left, DisableWinsFlagOperation right) =>
        !left.Equals(right);
}
