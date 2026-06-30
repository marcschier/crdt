// Copyright (c) marcschier. Licensed under the MIT License.

using Crdt.Consensus;
using Crdt.Transport;

namespace Crdt.Consensus.Raft;

/// <summary>Configures a <see cref="RaftConsensus"/> instance.</summary>
public sealed class RaftConsensusOptions
{
    /// <summary>Gets or sets the local replica id.</summary>
    public ReplicaId LocalReplicaId { get; set; }

    /// <summary>Gets or sets the CRDT transport used to exchange Raft frames.</summary>
    public ConsensusTransportOptions? Transport { get; set; }

    /// <summary>Gets or sets the live-membership source used to reconcile Raft configuration.</summary>
    public IFailureDetector? FailureDetector { get; set; }

    /// <summary>Gets or sets the replica/node id registry.</summary>
    public IReplicaIdRegistry? ReplicaIdRegistry { get; set; }

    /// <summary>Gets or sets a value indicating whether disposing consensus also disposes the detector.</summary>
    public bool DisposeFailureDetector { get; set; } = true;

    /// <summary>Gets or sets the number of Raft ticks between election timeouts.</summary>
    public int ElectionTick { get; set; } = 10;

    /// <summary>Gets or sets the number of Raft ticks between leader heartbeats.</summary>
    public int HeartbeatTick { get; set; } = 1;

    /// <summary>Gets or sets a value indicating whether Raft pre-vote is enabled.</summary>
    public bool PreVote { get; set; }

    /// <summary>Gets or sets a value indicating whether a leader steps down without quorum contact.</summary>
    public bool CheckQuorum { get; set; }

    /// <summary>Gets or sets the soft maximum payload bytes per Raft append message.</summary>
    public ulong MaxSizePerMessage { get; set; } = 1024 * 1024;

    /// <summary>Gets or sets the per-peer in-flight Raft append window capacity.</summary>
    public int MaxInflightMessages { get; set; } = 256;

    /// <summary>Gets or sets the per-peer in-flight Raft append byte window capacity.</summary>
    public ulong MaxInflightBytes { get; set; } = ulong.MaxValue;

    /// <summary>Gets or sets the soft cap on total uncommitted entry payload bytes.</summary>
    public ulong MaxUncommittedEntriesSize { get; set; } = ulong.MaxValue;

    /// <summary>Gets or sets a value indicating whether followers reject local proposals.</summary>
    public bool DisableProposalForwarding { get; set; }

    /// <summary>Gets or sets a fixed randomized election timeout in ticks, or zero for RaftCs defaults.</summary>
    public int RandomizedElectionTimeout { get; set; }

    /// <summary>Gets or sets the wall-clock interval between logical Raft ticks.</summary>
    public TimeSpan TickInterval { get; set; } = TimeSpan.FromMilliseconds(50);

    /// <summary>Gets or sets the interval used to poll RaftCs state because no state-changed event exists.</summary>
    public TimeSpan StatePollingInterval { get; set; } = TimeSpan.FromMilliseconds(100);

    /// <summary>Gets or sets the maximum accepted encoded CRDT transport frame body length in bytes.</summary>
    public int MaxFrameLength { get; set; } = FrameCodec.DefaultMaxFrameLength;
}
