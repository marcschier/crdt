// Copyright (c) marcschier. Licensed under the MIT License.

namespace Crdt;

/// <summary>
/// Provides the operations a container CRDT (notably <c>ORMap</c>) needs to manage values
/// that are themselves CRDTs, without relying on reflection, the <c>new()</c> constraint,
/// or static-abstract interface members (none of which are available/safe across all
/// target frameworks under NativeAOT).
/// </summary>
/// <typeparam name="T">The value type managed by the container.</typeparam>
/// <remarks>
/// Built-in CRDTs ship ready-made <see cref="ICrdtValueOps{T}"/> implementations; consumers
/// supply their own for custom value types. Serialization hooks are layered on top of this
/// abstraction by the serialization module.
/// </remarks>
public interface ICrdtValueOps<T> : ICrdtValueSerializer<T>
{
    /// <summary>Gets a fresh, empty value (the bottom element of the value's lattice).</summary>
    /// <returns>A new zero/identity value.</returns>
    T CreateZero();

    /// <summary>Joins <paramref name="other"/> into <paramref name="current"/> in place if possible,
    /// returning the merged value (for value-type semantics, callers use the return value).</summary>
    /// <param name="current">The current value.</param>
    /// <param name="other">The value to merge in.</param>
    /// <returns>The merged value.</returns>
    T Merge(T current, T other);

    /// <summary>Creates a deep, independent copy of <paramref name="value"/>.</summary>
    /// <param name="value">The value to clone.</param>
    /// <returns>An independent clone.</returns>
    T Clone(T value);

    /// <summary>Determines whether two values are logically equal.</summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <returns><see langword="true"/> if the values are logically equal.</returns>
    bool AreEqual(T left, T right);

    /// <summary>Determines whether <paramref name="value"/> is the empty/identity value,
    /// allowing the container to drop entries that have merged down to nothing.</summary>
    /// <param name="value">The value to test.</param>
    /// <returns><see langword="true"/> if the value is empty.</returns>
    bool IsZero(T value);
}
