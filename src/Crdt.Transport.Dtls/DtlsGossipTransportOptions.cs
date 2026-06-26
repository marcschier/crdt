// Copyright (c) marcschier. Licensed under the MIT License.

using System.Net;
using Dtls;

namespace Crdt.Transport.Dtls;

/// <summary>Configures a <see cref="DtlsGossipTransport"/> instance.</summary>
public sealed class DtlsGossipTransportOptions
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
    /// DTLS-protected datagram; larger frames are rejected.
    /// </summary>
    public int MaxDatagramSize { get; set; } = DefaultMaxDatagramSize;

    /// <summary>Gets or sets the DTLS options used to accept inbound peer sessions (server role).</summary>
    /// <remarks>Required; must configure a server credential (certificate, PSK, or raw public key).</remarks>
    public DtlsServerOptions? ServerOptions { get; set; }

    /// <summary>Gets or sets the DTLS options used to establish outbound peer sessions (client role).</summary>
    /// <remarks>Required; configures peer authentication (certificate validation, PSK, or raw public key).</remarks>
    public DtlsClientOptions? ClientOptions { get; set; }
}
