// Copyright (c) marcschier. Licensed under the MIT License.

namespace Crdt.Consensus.Raft;

/// <summary>Maintains the bijection between CRDT replica ids and Raft node ids.</summary>
/// <remarks>
/// Raft uses non-zero 64-bit node ids, while CRDT replica identity is a
/// <see cref="ReplicaId"/> wrapping a <see cref="Guid"/>. Implementations must keep both
/// directions stable for the lifetime of a Raft cluster.
/// </remarks>
public interface IReplicaIdRegistry
{
    /// <summary>Gets or creates the non-zero Raft node id for <paramref name="replicaId"/>.</summary>
    /// <param name="replicaId">The CRDT replica id.</param>
    /// <returns>The stable non-zero Raft node id.</returns>
    ulong GetNodeId(ReplicaId replicaId);

    /// <summary>Attempts to resolve a Raft node id back to its CRDT replica id.</summary>
    /// <param name="nodeId">The non-zero Raft node id.</param>
    /// <param name="replicaId">The mapped CRDT replica id when the mapping exists.</param>
    /// <returns><see langword="true"/> when <paramref name="nodeId"/> is mapped.</returns>
    bool TryGetReplicaId(ulong nodeId, out ReplicaId replicaId);

    /// <summary>Registers an explicit CRDT replica id to Raft node id mapping.</summary>
    /// <param name="replicaId">The CRDT replica id.</param>
    /// <param name="nodeId">The non-zero Raft node id.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="nodeId"/> is zero.</exception>
    /// <exception cref="InvalidOperationException">The node id is already mapped to another replica.</exception>
    void Register(ReplicaId replicaId, ulong nodeId);
}
