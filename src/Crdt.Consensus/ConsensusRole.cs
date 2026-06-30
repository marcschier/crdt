// Copyright (c) marcschier. Licensed under the MIT License.

namespace Crdt.Consensus;

/// <summary>Identifies a replica's role in a consensus group.</summary>
public enum ConsensusRole
{
    /// <summary>The replica follows another leader or is waiting for one to be selected.</summary>
    Follower = 0,

    /// <summary>The replica is attempting to become leader.</summary>
    Candidate = 1,

    /// <summary>The replica is the current leader.</summary>
    Leader = 2,
}
