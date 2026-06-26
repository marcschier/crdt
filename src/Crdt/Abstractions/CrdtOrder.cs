// Copyright (c) marcschier. Licensed under the MIT License.

namespace Crdt;

/// <summary>
/// The result of comparing two CRDT states under the partial order induced by their
/// join-semilattice. Unlike a total order, two states can be <see cref="Concurrent"/>:
/// neither dominates the other, and a merge is required to reconcile them.
/// </summary>
public enum CrdtOrder
{
    /// <summary>The two states are equal.</summary>
    Equal,

    /// <summary>This state is strictly dominated by (happened-before) the other.</summary>
    Less,

    /// <summary>This state strictly dominates (happened-after) the other.</summary>
    Greater,

    /// <summary>The states are concurrent; neither dominates the other.</summary>
    Concurrent,
}
