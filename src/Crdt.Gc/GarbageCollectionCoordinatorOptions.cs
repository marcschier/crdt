// Copyright (c) marcschier. Licensed under the MIT License.

using Crdt.Consensus;
using Crdt.Transport;

namespace Crdt.Gc;

/// <summary>Configures a <see cref="GarbageCollectionCoordinator"/> instance.</summary>
/// <remarks>
/// Configure either <see cref="Transport"/> or <see cref="TransportOptions"/>. When the coordinator
/// shares an <see cref="ITransport"/> with <see cref="ReplicationEngine{TState}"/>, garbage-collection
/// frames are distinguished by <see cref="MessageType.GcVersionReport"/> and
/// <see cref="MessageType.GcWatermark"/>. Replication engines ignore those message types.
/// </remarks>
public sealed class GarbageCollectionCoordinatorOptions
{
    /// <summary>Gets or sets the local replica id.</summary>
    public ReplicaId LocalReplicaId { get; set; }

    /// <summary>Gets or sets the consensus component that commits stable watermarks.</summary>
    public IConsensus? Consensus { get; set; }

    /// <summary>Gets or sets the optional failure detector used as the live-membership source.</summary>
    /// <remarks>When omitted, <see cref="IConsensus.Members"/> supplies the live-membership view.</remarks>
    public IFailureDetector? FailureDetector { get; set; }

    /// <summary>Gets or sets the transport shared with CRDT replication for GC frames.</summary>
    public ITransport? Transport { get; set; }

    /// <summary>Gets or sets delegate-based transport options for GC frames.</summary>
    public ConsensusTransportOptions? TransportOptions { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether <see cref="Transport"/> is started by the coordinator.
    /// </summary>
    public bool StartTransport { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether disposing the coordinator disposes consensus.</summary>
    public bool DisposeConsensus { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether disposing the coordinator disposes the failure detector.
    /// </summary>
    public bool DisposeFailureDetector { get; set; }

    /// <summary>Gets or sets a value indicating whether disposing the coordinator disposes the transport.</summary>
    public bool DisposeTransport { get; set; }

    /// <summary>Gets or sets the interval between local version reports.</summary>
    public TimeSpan ReportInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Gets or sets the maximum age for a report to be included in a stable cut.</summary>
    public TimeSpan ReportStalenessTimeout { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>Gets or sets the maximum accepted encoded transport frame body length in bytes.</summary>
    public int MaxFrameLength { get; set; } = FrameCodec.DefaultMaxFrameLength;
}
