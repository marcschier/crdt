// Copyright (c) marcschier. Licensed under the MIT License.

using NanoMsg;

namespace Crdt.Transport.NanoMsg;

/// <summary>Configures a <see cref="NanoMsgBusTransport"/> instance.</summary>
public sealed class NanoMsgBusTransportOptions
{
    /// <summary>
    /// Gets or sets the local endpoint to bind and accept peers on, for example
    /// <c>tcp://127.0.0.1:5560</c>, <c>tcp://*:0</c> (OS-assigned port), or <c>inproc://name</c>. When
    /// <see langword="null"/> or empty, the transport only dials the configured <see cref="Peers"/>.
    /// </summary>
    public string? BindAddress { get; set; }

    /// <summary>
    /// Gets the peer endpoints to dial, for example <c>tcp://10.0.0.2:5560</c>. Peers may also be added
    /// after construction with <see cref="NanoMsgBusTransport.AddPeer(string)"/>.
    /// </summary>
    public IList<string> Peers { get; } = [];

    /// <summary>
    /// Gets or sets the underlying socket options (TLS, timeouts, watermarks, message-size limits). When
    /// <see langword="null"/>, a default <see cref="NanoSocketOptions"/> is used.
    /// </summary>
    public NanoSocketOptions? SocketOptions { get; set; }

    /// <summary>Gets or sets the maximum accepted frame body length, in bytes.</summary>
    public int MaxFrameLength { get; set; } = FrameCodec.DefaultMaxFrameLength;
}
