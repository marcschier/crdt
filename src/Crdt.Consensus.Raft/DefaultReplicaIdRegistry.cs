// Copyright (c) marcschier. Licensed under the MIT License.

namespace Crdt.Consensus.Raft;

/// <summary>Default in-memory <see cref="IReplicaIdRegistry"/> implementation.</summary>
/// <remarks>
/// Unknown replica ids are assigned a stable non-zero 64-bit FNV-1a hash of their
/// underlying <see cref="Guid"/>. Hash collisions are detected and fail fast; callers that
/// need collision-free predefined ids should call <see cref="Register"/> before use.
/// Explicit registrations override a previously-created hash mapping for the same replica.
/// </remarks>
public sealed class DefaultReplicaIdRegistry : IReplicaIdRegistry
{
    private const ulong FnvOffsetBasis = 14695981039346656037UL;
    private const ulong FnvPrime = 1099511628211UL;

    private readonly object _gate = new();
    private readonly Dictionary<ReplicaId, ulong> _byReplicaId = new();
    private readonly Dictionary<ulong, ReplicaId> _byNodeId = new();

    /// <inheritdoc/>
    public ulong GetNodeId(ReplicaId replicaId)
    {
        lock (_gate)
        {
            if (_byReplicaId.TryGetValue(replicaId, out ulong existing))
            {
                return existing;
            }

            ulong nodeId = ComputeNodeId(replicaId.Value);
            if (_byNodeId.TryGetValue(nodeId, out ReplicaId collision) && collision != replicaId)
            {
                throw new InvalidOperationException(
                    "ReplicaId hash collision detected. Register explicit Raft node ids.");
            }

            _byReplicaId.Add(replicaId, nodeId);
            _byNodeId.Add(nodeId, replicaId);
            return nodeId;
        }
    }

    /// <inheritdoc/>
    public bool TryGetReplicaId(ulong nodeId, out ReplicaId replicaId)
    {
        lock (_gate)
        {
            return _byNodeId.TryGetValue(nodeId, out replicaId);
        }
    }

    /// <inheritdoc/>
    public void Register(ReplicaId replicaId, ulong nodeId)
    {
        if (nodeId == CrdtRaftTransport.BroadcastNodeId)
        {
            throw new ArgumentOutOfRangeException(nameof(nodeId), "Raft node id must be non-zero.");
        }

        lock (_gate)
        {
            if (_byNodeId.TryGetValue(nodeId, out ReplicaId existingReplica) && existingReplica != replicaId)
            {
                throw new InvalidOperationException("Raft node id is already registered to another replica.");
            }

            if (_byReplicaId.TryGetValue(replicaId, out ulong existingNodeId))
            {
                _byNodeId.Remove(existingNodeId);
            }

            _byReplicaId[replicaId] = nodeId;
            _byNodeId[nodeId] = replicaId;
        }
    }

    private static ulong ComputeNodeId(Guid replicaId)
    {
        byte[] bytes = replicaId.ToByteArray();
        ulong hash = FnvOffsetBasis;
        for (int i = 0; i < bytes.Length; i++)
        {
            hash ^= bytes[i];
            hash *= FnvPrime;
        }

        return hash == CrdtRaftTransport.BroadcastNodeId ? 1UL : hash;
    }
}
