// Copyright (c) marcschier. Licensed under the MIT License.

using System.Net;
using Pgm;
using Pgm.Net;

namespace Crdt.Transport.Pgm;

/// <summary>Configures a <see cref="PgmBusTransport"/> instance.</summary>
public sealed class PgmBusTransportOptions
{
    /// <summary>Gets or sets the multicast group address shared by every replica in the PGM session.</summary>
    public IPAddress MulticastGroup { get; set; } = new PgmPublisherOptions().MulticastGroup;

    /// <summary>Gets or sets the UDP port used for multicast datagrams.</summary>
    public int Port { get; set; } = new PgmPublisherOptions().Port;

    /// <summary>
    /// Gets or sets the publisher datagram channel. When set, <see cref="SubscriberChannel"/> must also be
    /// set; ownership is transferred to the transport.
    /// </summary>
    public IPgmDatagramChannel? PublisherChannel { get; set; }

    /// <summary>
    /// Gets or sets the subscriber datagram channel. When set, <see cref="PublisherChannel"/> must also be
    /// set; ownership is transferred to the transport.
    /// </summary>
    public IPgmDatagramChannel? SubscriberChannel { get; set; }

    /// <summary>
    /// Gets or sets a process-local multicast bus used to create publisher and subscriber channels for this
    /// transport. Supplying the same bus to multiple transports places them in one in-memory multicast group.
    /// </summary>
    public InMemoryMulticastBus? InMemoryBus { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to use a shared process-local in-memory multicast bus when
    /// <see cref="InMemoryBus"/> and explicit channels are not configured.
    /// </summary>
    public bool UseInMemoryBus { get; set; }

    /// <summary>Gets or sets the maximum accepted frame body length, in bytes.</summary>
    public int MaxFrameLength { get; set; } = FrameCodec.DefaultMaxFrameLength;
}
