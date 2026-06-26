// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace Crdt;

/// <summary>
/// A globally-unique identifier for a single replica (node) participating in a
/// replicated data type. Replica identity is the basis of every CRDT's causal
/// bookkeeping: counters slot per replica, dots are stamped with the replica that
/// created them, and last-writer-wins ties are broken by replica order.
/// </summary>
/// <remarks>
/// A <see cref="ReplicaId"/> wraps a 128-bit <see cref="Guid"/>, giving collision-free
/// identity without coordination. Use <see cref="New"/> to mint a fresh id per replica
/// and persist it for the lifetime of that replica.
/// </remarks>
[JsonConverter(typeof(ReplicaIdJsonConverter))]
public readonly struct ReplicaId : IEquatable<ReplicaId>, IComparable<ReplicaId>
{
    /// <summary>Initializes a new <see cref="ReplicaId"/> from an existing <see cref="Guid"/>.</summary>
    /// <param name="value">The underlying identifier value.</param>
    public ReplicaId(Guid value) => Value = value;

    /// <summary>Gets the underlying <see cref="Guid"/> value.</summary>
    public Guid Value { get; }

    /// <summary>Gets the empty (all-zero) replica id. Useful as a sentinel/default.</summary>
    public static ReplicaId Empty => default;

    /// <summary>Creates a fresh, random replica id.</summary>
    /// <returns>A new, unique <see cref="ReplicaId"/>.</returns>
    public static ReplicaId New() => new(Guid.NewGuid());

    /// <summary>
    /// Creates a deterministic replica id from a 64-bit value. Handy for tests and for
    /// systems that already assign dense numeric node indices.
    /// </summary>
    /// <param name="value">The numeric seed placed in the low eight bytes of the id.</param>
    /// <returns>A deterministic <see cref="ReplicaId"/>.</returns>
    public static ReplicaId FromUInt64(ulong value)
    {
        Span<byte> bytes = stackalloc byte[16];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt64BigEndian(bytes.Slice(8), value);
        return new ReplicaId(new Guid(bytes));
    }

    /// <summary>Parses a replica id from its canonical <see cref="Guid"/> string form.</summary>
    /// <param name="text">The text to parse.</param>
    /// <returns>The parsed <see cref="ReplicaId"/>.</returns>
    public static ReplicaId Parse(string text)
    {
        Throw.IfNull(text);
        return new ReplicaId(Guid.Parse(text));
    }

    /// <summary>Attempts to parse a replica id from its canonical <see cref="Guid"/> string form.</summary>
    /// <param name="text">The text to parse.</param>
    /// <param name="result">The parsed value when successful.</param>
    /// <returns><see langword="true"/> if parsing succeeded; otherwise <see langword="false"/>.</returns>
    public static bool TryParse([NotNullWhen(true)] string? text, out ReplicaId result)
    {
        if (Guid.TryParse(text, out Guid g))
        {
            result = new ReplicaId(g);
            return true;
        }

        result = default;
        return false;
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Equals(ReplicaId other) => Value.Equals(other.Value);

    /// <inheritdoc/>
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is ReplicaId other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => Value.GetHashCode();

    /// <summary>Defines a stable, deterministic total order over replica ids.</summary>
    /// <param name="other">The replica id to compare against.</param>
    /// <returns>A signed ordering value.</returns>
    public int CompareTo(ReplicaId other) => Value.CompareTo(other.Value);

    /// <inheritdoc/>
    public override string ToString() => Value.ToString("D");

    /// <summary>Equality operator.</summary>
    public static bool operator ==(ReplicaId left, ReplicaId right) => left.Equals(right);

    /// <summary>Inequality operator.</summary>
    public static bool operator !=(ReplicaId left, ReplicaId right) => !left.Equals(right);

    /// <summary>Less-than operator (canonical order).</summary>
    public static bool operator <(ReplicaId left, ReplicaId right) => left.CompareTo(right) < 0;

    /// <summary>Greater-than operator (canonical order).</summary>
    public static bool operator >(ReplicaId left, ReplicaId right) => left.CompareTo(right) > 0;

    /// <summary>Less-than-or-equal operator (canonical order).</summary>
    public static bool operator <=(ReplicaId left, ReplicaId right) => left.CompareTo(right) <= 0;

    /// <summary>Greater-than-or-equal operator (canonical order).</summary>
    public static bool operator >=(ReplicaId left, ReplicaId right) => left.CompareTo(right) >= 0;
}
