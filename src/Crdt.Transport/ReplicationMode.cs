// Copyright (c) marcschier. Licensed under the MIT License.

namespace Crdt.Transport;

/// <summary>Selects which replication payload a <see cref="ReplicationEngine{TState}"/> sends and accepts.</summary>
public enum ReplicationMode
{
    /// <summary>Replicate complete state snapshots.</summary>
    State = 0,

    /// <summary>Replicate delta-state updates when available, falling back to state snapshots.</summary>
    Delta = 1,

    /// <summary>Replicate operation payloads supplied by the caller.</summary>
    Operation = 2,
}
