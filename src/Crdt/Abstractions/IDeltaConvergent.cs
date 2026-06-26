// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;

namespace Crdt;

/// <summary>
/// A delta-state CRDT: a convergent CRDT that can additionally emit small <em>deltas</em>
/// capturing only its recent mutations, instead of shipping the full state on every
/// exchange. Deltas belong to the same join-semilattice as the state, so they merge with
/// one another (forming delta-groups) and into a full state via <see cref="MergeDelta"/>.
/// </summary>
/// <typeparam name="TSelf">The implementing CRDT type.</typeparam>
/// <typeparam name="TDelta">The delta type carrying recent mutations.</typeparam>
/// <remarks>
/// Delta dissemination converges to the same result as full-state merging <em>provided</em>
/// each type's causal-delivery requirements are met. Causal CRDTs (observed-remove set,
/// map, multi-value register, flags) require that a delta's causal context is preserved
/// when applied; see each type's documentation.
/// </remarks>
public interface IDeltaConvergent<TSelf, TDelta> : IConvergent<TSelf>
    where TSelf : IDeltaConvergent<TSelf, TDelta>
{
    /// <summary>
    /// Atomically returns the accumulated delta of mutations applied since the last
    /// extraction and clears the internal delta buffer.
    /// </summary>
    /// <param name="delta">The accumulated delta when one is available.</param>
    /// <returns>
    /// <see langword="true"/> if there were pending mutations to emit; otherwise
    /// <see langword="false"/> (and <paramref name="delta"/> is left at its default).
    /// </returns>
    bool TryExtractDelta([MaybeNullWhen(false)] out TDelta delta);

    /// <summary>Joins a received <paramref name="delta"/> into this instance (in place).</summary>
    /// <param name="delta">The delta to apply.</param>
    void MergeDelta(TDelta delta);
}
