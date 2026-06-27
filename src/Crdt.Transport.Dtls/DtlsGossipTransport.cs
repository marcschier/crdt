// Copyright (c) marcschier. Licensed under the MIT License.

using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
using Dtls;
using Dtls.Transport;

namespace Crdt.Transport.Dtls;

/// <summary>
/// A DTLS-secured, connectionless datagram transport that gossips frames to peers. Each peer pair is
/// protected by a DTLS session: outbound frames travel over a client session established with
/// <see cref="DtlsClient"/>, and inbound datagrams are demultiplexed by remote endpoint and accepted
/// as per-peer server sessions with <see cref="DtlsServer"/>.
/// </summary>
/// <remarks>
/// Like the plaintext UDP transport, delivery of any single datagram is best-effort; convergence is
/// driven by the periodic anti-entropy loop. Each frame is one application datagram and must fit in
/// <see cref="DtlsGossipTransportOptions.MaxDatagramSize"/>.
/// </remarks>
public sealed class DtlsGossipTransport : ITransport
{
    private readonly object _gate = new();
    private readonly DtlsGossipTransportOptions _options;
    private readonly DtlsServerOptions _serverOptions;
    private readonly DtlsClientOptions _clientOptions;
    private readonly HashSet<IPEndPoint> _peers = [];
    private readonly CancellationTokenSource _stop = new();
    private readonly ConcurrentDictionary<IPEndPoint, PeerInbox> _inbound = new();
    private readonly ConcurrentDictionary<IPEndPoint, Task<DtlsConnection?>> _outbound = new();
    private DtlsServer? _server;
    private Socket? _socket;
    private Task? _receiveLoop;
    private Task? _gossipLoop;
    private byte[]? _lastFrame;
    private bool _started;

    /// <summary>Initializes a DTLS gossip transport.</summary>
    /// <param name="options">The transport options, including DTLS server and client options.</param>
    public DtlsGossipTransport(DtlsGossipTransportOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _serverOptions = options.ServerOptions
            ?? throw new ArgumentException("ServerOptions is required.", nameof(options));
        _clientOptions = options.ClientOptions
            ?? throw new ArgumentException("ClientOptions is required.", nameof(options));
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
        _server = new DtlsServer(_serverOptions);
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

        foreach (IPEndPoint peer in SnapshotPeers())
        {
            await SendToPeerAsync(peer, copy, ct).ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync().ConfigureAwait(false);
        _socket?.Dispose();
        await AwaitLoopAsync(_receiveLoop).ConfigureAwait(false);
        await AwaitLoopAsync(_gossipLoop).ConfigureAwait(false);

        foreach (KeyValuePair<IPEndPoint, Task<DtlsConnection?>> entry in _outbound)
        {
            await DisposeConnectionTaskAsync(entry.Value).ConfigureAwait(false);
        }

        _outbound.Clear();

        foreach (KeyValuePair<IPEndPoint, PeerInbox> entry in _inbound)
        {
            entry.Value.Dispose();
        }

        _inbound.Clear();
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
            SocketReceiveFromResult result;
            try
            {
                result = await _socket!
                    .ReceiveFromAsync(buffer, SocketFlags.None, remote, ct).ConfigureAwait(false);
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
                continue;
            }

            if (result.ReceivedBytes <= 0)
            {
                continue;
            }

            var datagram = buffer.AsSpan(0, result.ReceivedBytes).ToArray();
            var peer = (IPEndPoint)result.RemoteEndPoint;
            PeerInbox inbox = _inbound.GetOrAdd(peer, StartInbound);
            inbox.Enqueue(datagram);
        }
    }

    private PeerInbox StartInbound(IPEndPoint remote)
    {
        var inbox = new PeerInbox(_socket!, remote, _options.MaxDatagramSize);
        _ = AcceptAndReadAsync(remote, inbox, _stop.Token);
        return inbox;
    }

    private async Task AcceptAndReadAsync(IPEndPoint remote, PeerInbox inbox, CancellationToken ct)
    {
        DtlsConnection? connection = null;
        try
        {
            connection = await _server!.AcceptAsync(inbox, ct).ConfigureAwait(false);
            var buffer = new byte[_options.MaxDatagramSize];
            while (!ct.IsCancellationRequested)
            {
                int received = await connection.ReceiveAsync(buffer, ct).ConfigureAwait(false);
                if (received <= 0)
                {
                    break;
                }

                var frame = buffer.AsSpan(0, received).ToArray();
                if (FrameCodec.TryDecode(frame, out _, _options.MaxFrameLength))
                {
                    FrameReceived?.Invoke(frame);
                }
            }
        }
        catch (Exception ex) when (IsExpectedFault(ex, ct))
        {
        }
        finally
        {
            _inbound.TryRemove(new KeyValuePair<IPEndPoint, PeerInbox>(remote, inbox));
            await CloseAsync(connection).ConfigureAwait(false);
            inbox.Dispose();
        }
    }

    private async Task SendToPeerAsync(IPEndPoint peer, byte[] frame, CancellationToken ct)
    {
        try
        {
            DtlsConnection? connection = await GetOrConnectAsync(peer).ConfigureAwait(false);
            if (connection is null)
            {
                return;
            }

            await connection.SendAsync(frame, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsExpectedFault(ex, ct))
        {
            DropOutbound(peer);
        }
    }

    private async Task<DtlsConnection?> GetOrConnectAsync(IPEndPoint peer)
    {
        Task<DtlsConnection?> task = _outbound.GetOrAdd(peer, ConnectOutboundAsync);
        DtlsConnection? connection = await task.ConfigureAwait(false);
        if (connection is null)
        {
            _outbound.TryRemove(new KeyValuePair<IPEndPoint, Task<DtlsConnection?>>(peer, task));
        }

        return connection;
    }

    private async Task<DtlsConnection?> ConnectOutboundAsync(IPEndPoint peer)
    {
        UdpDatagramTransport? transport = null;
        try
        {
            transport = UdpDatagramTransport.Connect(peer);
            return await DtlsClient.ConnectAsync(transport, _clientOptions, _stop.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsExpectedFault(ex, _stop.Token))
        {
            transport?.Dispose();
            return null;
        }
    }

    private void DropOutbound(IPEndPoint peer)
    {
        if (_outbound.TryRemove(peer, out Task<DtlsConnection?>? task))
        {
            _ = DisposeConnectionTaskAsync(task);
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

            await SendToPeerAsync(peer, frame, ct).ConfigureAwait(false);
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

    private static async Task DisposeConnectionTaskAsync(Task<DtlsConnection?> task)
    {
        DtlsConnection? connection;
        try
        {
            connection = await task.ConfigureAwait(false);
        }
        catch (Exception ex) when (IsExpectedFault(ex, CancellationToken.None))
        {
            return;
        }

        await CloseAsync(connection).ConfigureAwait(false);
    }

    private static async Task CloseAsync(DtlsConnection? connection)
    {
        if (connection is null)
        {
            return;
        }

        try
        {
            await connection.CloseAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsExpectedFault(ex, CancellationToken.None))
        {
        }

        connection.Dispose();
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

    private static bool IsExpectedFault(Exception ex, CancellationToken ct) =>
        ex is IOException or SocketException or ObjectDisposedException or DtlsException
            or InvalidDataException or ChannelClosedException ||
        ex is OperationCanceledException && ct.IsCancellationRequested;

    private static bool SameEndpoint(IPEndPoint left, IPEndPoint right) =>
        left.Port == right.Port && left.Address.Equals(right.Address);

    /// <summary>A per-peer datagram channel fed by the shared server socket's demultiplexer.</summary>
    private sealed class PeerInbox : IDatagramTransport
    {
        private readonly Socket _socket;
        private readonly IPEndPoint _remote;
        private readonly int _maxDatagramSize;
        private readonly Channel<byte[]> _inbox;

        public PeerInbox(Socket socket, IPEndPoint remote, int maxDatagramSize)
        {
            _socket = socket;
            _remote = remote;
            _maxDatagramSize = maxDatagramSize;
            _inbox = Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false,
            });
        }

        public int MaxDatagramSize => _maxDatagramSize;

        public void Enqueue(byte[] datagram) => _inbox.Writer.TryWrite(datagram);

        public async ValueTask SendAsync(
            ReadOnlyMemory<byte> datagram,
            CancellationToken cancellationToken = default)
        {
            await _socket.SendToAsync(datagram, SocketFlags.None, _remote, cancellationToken)
                .ConfigureAwait(false);
        }

        public async ValueTask<int> ReceiveAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            try
            {
                byte[] datagram = await _inbox.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
                datagram.AsSpan().CopyTo(buffer.Span);
                return datagram.Length;
            }
            catch (ChannelClosedException)
            {
                return 0;
            }
        }

        public void Dispose() => _inbox.Writer.TryComplete();
    }
}
