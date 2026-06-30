// Copyright (c) marcschier. Licensed under the MIT License.

using Crdt.Transport;

namespace Crdt.Consensus;

/// <summary>Configures a <see cref="DeterministicLeaderConsensus"/> instance.</summary>
public sealed class DeterministicLeaderConsensusOptions
{
    /// <summary>Gets or sets the local replica id.</summary>
    public ReplicaId LocalReplicaId { get; set; }

    /// <summary>Gets or sets the live-membership source used for deterministic leader election.</summary>
    public IFailureDetector? FailureDetector { get; set; }

    /// <summary>Gets or sets the transport used to exchange proposal, acknowledgement, and commit frames.</summary>
    public ConsensusTransportOptions? Transport { get; set; }

    /// <summary>Gets or sets a value indicating whether disposing consensus also disposes the detector.</summary>
    public bool DisposeFailureDetector { get; set; } = true;

    /// <summary>Gets or sets the maximum accepted encoded frame body length in bytes.</summary>
    public int MaxFrameLength { get; set; } = FrameCodec.DefaultMaxFrameLength;
}
