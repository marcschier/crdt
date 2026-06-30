// Copyright (c) marcschier. Licensed under the MIT License.

using Crdt.Transport;

namespace Crdt.Consensus;

/// <summary>Configures a <see cref="HeartbeatFailureDetector"/> instance.</summary>
public sealed class HeartbeatFailureDetectorOptions
{
    /// <summary>Gets or sets the local replica id.</summary>
    public ReplicaId LocalReplicaId { get; set; }

    /// <summary>Gets or sets the transport used to send and receive heartbeat frames.</summary>
    public ConsensusTransportOptions? Transport { get; set; }

    /// <summary>Gets the members considered live when the detector starts.</summary>
    public IList<ReplicaId> InitialMembers { get; } = [];

    /// <summary>Gets or sets the interval between emitted heartbeats.</summary>
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Gets or sets the time after which a member is removed without a heartbeat.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>Gets or sets the maximum accepted encoded frame body length in bytes.</summary>
    public int MaxFrameLength { get; set; } = FrameCodec.DefaultMaxFrameLength;
}
