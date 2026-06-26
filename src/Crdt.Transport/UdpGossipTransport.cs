// Copyright (c) marcschier. Licensed under the MIT License.

using System.Net;
using System.Net.Sockets;

namespace Crdt.Transport;

/// <summary>A connectionless UDP transport that periodically gossips the latest frame to peers.</summary>
/// <remarks>
/// UDP is best-effort and lossy: delivery of any single datagram is not guaranteed. Convergence is
/// driven by the periodic anti-entropy loop, which re-sends the most recent frame to a random peer.
/// Each frame is sent as exactly one datagram and must fit within
/// <see cref="UdpGossipTransportOptions.MaxDatagramSize"/>.
/// </remarks>
public sealed class UdpGossipTransport : ITransport
{
    private readonly object _gate = new();
    private readonly UdpGossipTransportOptions _options;
    private readonly HashSet<IPEndPoint> _peers = [];
    private readonly CancellationTokenSource _stop = new();
    private Socket? _socket;
    private Task? _receiveLoop;
    private Task? _gossipLoop;
    private byte[]? _lastFrame;
    private bool _started;

    /// <summary>Initializes a UDP gossip transport.</summary>
    /// <param name="address">The local address to bind.</param>
    /// <param name="port">The local UDP port to bind, or 0 for an OS-assigned port.</param>
    /// <param name="gossipInterval">The periodic gossip interval.</param>
    /// <param name="maxFrameLength">The maximum accepted frame body length.</param>
    public UdpGossipTransport(
        IPAddress address,
        int port,
        TimeSpan? gossipInterval = null,
        int maxFrameLength = FrameCodec.DefaultMaxFrameLength)
        : this(new UdpGossipTransportOptions
        {
            Address = address ?? throw new ArgumentNullException(nameof(address)),
            Port = port,
            GossipInterval = gossipInterval ?? TimeSpan.FromMilliseconds(250),
            MaxFrameLength = maxFrameLength,
        })
    {
    }

    /// <summary>Initializes a UDP gossip transport.</summary>
    /// <param name="options">The transport options.</param>
    public UdpGossipTransport(UdpGossipTransportOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <inheritdoc/>
    public event Action<ReadOnlyMemory<byte>>? FrameReceived;

    /// <summary>Gets the bound local endpoint after <see cref="StartAsync"/> completes.</summary>
    public IPEndPoint LocalEndPoint
    {
        get
        {
            if (_socket?.LocalEndPoint is IPEndPoint endpoint)
            {
                return endpoint;
            }

            throw new InvalidOperationException("The transport has not started.");
        }
    }

    /// <summary>Adds a gossip peer endpoint.</summary>
    /// <param name="endpoint">The peer endpoint.</param>
    public void AddPeer(IPEndPoint endpoint)
    {
        Throw.IfNull(endpoint, nameof(endpoint));
        lock (_gate)
        {
            if (_socket?.LocalEndPoint is IPEndPoint local && SameEndpoint(local, endpoint))
            {
                return;
            }

            _peers.Add(endpoint);
        }
    }

    /// <summary>Adds multiple gossip peer endpoints.</summary>
    /// <param name="endpoints">The peer endpoints.</param>
    public void AddPeers(IEnumerable<IPEndPoint> endpoints)
    {
        Throw.IfNull(endpoints, nameof(endpoints));
        foreach (IPEndPoint endpoint in endpoints)
        {
            AddPeer(endpoint);
        }
    }

    /// <inheritdoc/>
    public ValueTask StartAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (_started)
        {
            return default;
        }

        _started = true;
        _socket = new Socket(_options.Address.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
        _socket.Bind(new IPEndPoint(_options.Address, _options.Port));
        _receiveLoop = ReceiveLoopAsync(_stop.Token);
        _gossipLoop = GossipLoopAsync(_stop.Token);
        return default;
    }

    /// <inheritdoc/>
    public async ValueTask SendAsync(ReadOnlyMemory<byte> frame, CancellationToken ct = default)
    {
        if (frame.Length > _options.MaxDatagramSize)
        {
            throw new ArgumentException(
                "The frame is larger than the maximum datagram size.", nameof(frame));
        }

        byte[] copy = frame.ToArray();
        FrameCodec.Decode(copy, _options.MaxFrameLength);
        Volatile.Write(ref _lastFrame, copy);

        Socket socket = _socket ?? throw new InvalidOperationException("The transport has not started.");
        foreach (IPEndPoint peer in SnapshotPeers())
        {
            try
            {
                await socket.SendToAsync(copy, SocketFlags.None, peer, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (IsNetworkFailure(ex, ct))
            {
            }
        }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync().ConfigureAwait(false);
        _socket?.Dispose();
        await AwaitLoopAsync(_receiveLoop).ConfigureAwait(false);
        await AwaitLoopAsync(_gossipLoop).ConfigureAwait(false);
        _stop.Dispose();
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        var buffer = new byte[_options.MaxDatagramSize];
        EndPoint remote = new IPEndPoint(
            _options.Address.AddressFamily == AddressFamily.InterNetworkV6 ? IPAddress.IPv6Any : IPAddress.Any,
            0);

        while (!ct.IsCancellationRequested)
        {
            int received;
            try
            {
                SocketReceiveFromResult result = await _socket!
                    .ReceiveFromAsync(buffer, SocketFlags.None, remote, ct).ConfigureAwait(false);
                received = result.ReceivedBytes;
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (SocketException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (SocketException)
            {
                // A prior send may have triggered an ICMP port-unreachable (Windows surfaces this as
                // a ConnectionReset on the next receive). Drop it and keep listening.
                continue;
            }

            if (received <= 0)
            {
                continue;
            }

            var datagram = buffer.AsSpan(0, received).ToArray();
            try
            {
                if (FrameCodec.TryDecode(datagram, out _, _options.MaxFrameLength))
                {
                    FrameReceived?.Invoke(datagram);
                }
            }
            catch (Exception ex) when (ex is InvalidDataException or OverflowException)
            {
                // A single poisoned datagram must not tear down the shared receive loop.
            }
        }
    }

    private async Task GossipLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(_options.GossipInterval);
        while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
        {
            byte[]? frame = Volatile.Read(ref _lastFrame);
            IPEndPoint? peer = PickPeer();
            if (frame is null || peer is null)
            {
                continue;
            }

            try
            {
                await _socket!.SendToAsync(frame, SocketFlags.None, peer, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex) when (IsNetworkFailure(ex, ct))
            {
            }
        }
    }

    private IPEndPoint[] SnapshotPeers()
    {
        lock (_gate)
        {
            return [.. _peers];
        }
    }

    private IPEndPoint? PickPeer()
    {
        IPEndPoint[] peers = SnapshotPeers();
        return peers.Length == 0 ? null : peers[SharedRandom.Next(peers.Length)];
    }

    private static async ValueTask AwaitLoopAsync(Task? loop)
    {
        if (loop is null)
        {
            return;
        }

        try
        {
            await loop.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static bool IsNetworkFailure(Exception ex, CancellationToken ct) =>
        ex is IOException or SocketException or ObjectDisposedException or InvalidDataException ||
        ex is OperationCanceledException && ct.IsCancellationRequested;

    private static bool SameEndpoint(IPEndPoint left, IPEndPoint right) =>
        left.Port == right.Port && left.Address.Equals(right.Address);
}
