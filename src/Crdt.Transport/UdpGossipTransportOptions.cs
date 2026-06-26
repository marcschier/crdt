// Copyright (c) marcschier. Licensed under the MIT License.

using System.Net;

namespace Crdt.Transport;

/// <summary>Configures a <see cref="UdpGossipTransport"/> instance.</summary>
public sealed class UdpGossipTransportOptions
{
    /// <summary>The default maximum datagram payload in bytes (the IPv4 UDP payload limit).</summary>
    public const int DefaultMaxDatagramSize = 65507;

    /// <summary>Gets or sets the local address to bind.</summary>
    public IPAddress Address { get; set; } = IPAddress.Loopback;

    /// <summary>Gets or sets the local UDP port to bind, or 0 for an OS-assigned port.</summary>
    public int Port { get; set; }

    /// <summary>Gets or sets the periodic anti-entropy gossip interval.</summary>
    public TimeSpan GossipInterval { get; set; } = TimeSpan.FromMilliseconds(250);

    /// <summary>Gets or sets the maximum accepted frame body length.</summary>
    public int MaxFrameLength { get; set; } = FrameCodec.DefaultMaxFrameLength;

    /// <summary>
    /// Gets or sets the maximum datagram payload, in bytes. A single CRDT frame must fit in one
    /// datagram; frames larger than this are rejected. Use delta or operation mode, or the TCP
    /// transport, for state that does not fit in a datagram.
    /// </summary>
    public int MaxDatagramSize { get; set; } = DefaultMaxDatagramSize;
}
