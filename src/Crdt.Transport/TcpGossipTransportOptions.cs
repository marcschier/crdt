// Copyright (c) marcschier. Licensed under the MIT License.

using System.Net;

namespace Crdt.Transport;

/// <summary>Configures a <see cref="TcpGossipTransport"/> instance.</summary>
public sealed class TcpGossipTransportOptions
{
    /// <summary>Gets or sets the local address to bind.</summary>
    public IPAddress Address { get; set; } = IPAddress.Loopback;

    /// <summary>Gets or sets the local TCP port to bind, or 0 for an OS-assigned port.</summary>
    public int Port { get; set; }

    /// <summary>Gets or sets the periodic gossip interval.</summary>
    public TimeSpan GossipInterval { get; set; } = TimeSpan.FromMilliseconds(250);

    /// <summary>Gets or sets the maximum accepted frame body length.</summary>
    public int MaxFrameLength { get; set; } = FrameCodec.DefaultMaxFrameLength;

    /// <summary>
    /// Gets or sets the optional TLS configuration. When <see langword="null"/> (the default), the
    /// transport exchanges frames in plaintext.
    /// </summary>
    public GossipTlsOptions? Tls { get; set; }
}
