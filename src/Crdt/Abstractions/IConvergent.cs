// Copyright (c) marcschier. Licensed under the MIT License.

namespace Crdt;

/// <summary>
/// A state-based (convergent) CRDT: a value that forms a join-semilattice. Replicas
/// converge by repeatedly <see cref="Merge"/>-ing each other's full state. The merge is
/// commutative, associative, and idempotent, which makes convergence robust to message
/// reordering and duplication.
/// </summary>
/// <typeparam name="TSelf">The implementing type (curiously-recurring pattern).</typeparam>
/// <remarks>
/// Implementations are <strong>mutable and not thread-safe</strong>: concurrent local
/// operations on a single instance require external synchronization. Cross-replica
/// exchange (sending a clone/snapshot to another replica) is always safe.
/// </remarks>
public interface IConvergent<TSelf>
    where TSelf : IConvergent<TSelf>
{
    /// <summary>
    /// Joins <paramref name="other"/>'s state into this instance (in place), computing the
    /// least upper bound of the two states.
    /// </summary>
    /// <param name="other">The state to merge in. Not mutated.</param>
    void Merge(TSelf other);

    /// <summary>
    /// Compares this state with <paramref name="other"/> under the lattice's partial order.
    /// </summary>
    /// <param name="other">The state to compare against.</param>
    /// <returns>
    /// <see cref="CrdtOrder.Equal"/>, <see cref="CrdtOrder.Less"/>,
    /// <see cref="CrdtOrder.Greater"/>, or <see cref="CrdtOrder.Concurrent"/>.
    /// </returns>
    CrdtOrder Compare(TSelf other);

    /// <summary>Creates a deep, independent copy of this state.</summary>
    /// <returns>A clone that can be mutated without affecting this instance.</returns>
    TSelf Clone();
}
