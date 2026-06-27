// Copyright (c) marcschier. Licensed under the MIT License.

using NanoMsg;

namespace Crdt.Transport.NanoMsg;

/// <summary>
/// A peer-to-peer gossip transport that replicates CRDT frames over the nanomsg/NNG BUS protocol. Each
/// replica broadcasts every frame to all directly connected peers and receives frames from any peer; the
/// BUS pattern does not echo a node's own sends. Endpoints may use any NanoMsgSharp transport scheme
/// (<c>tcp</c>, <c>tls+tcp</c>, <c>ipc</c>, <c>ws</c>, <c>wss</c>, or <c>inproc</c>).
/// </summary>
/// <remarks>
/// BUS delivers only to directly connected peers (no multi-hop forwarding), so peers should form a
/// connected mesh; convergence is then driven by the application's broadcast cadence. Each frame is one
/// complete <see cref="FrameCodec"/> message.
/// </remarks>
public sealed class NanoMsgBusTransport : ITransport
{
    private readonly NanoMsgBusTransportOptions _options;
    private readonly object _gate = new();
    private readonly List<string> _pendingPeers = [];
    private readonly CancellationTokenSource _stop = new();
    private BusSocket? _socket;
    private Task? _receiveLoop;
    private int _started;
    private int _disposed;
    private int _boundPort = -1;

    /// <summary>Initializes a NanoMsg BUS transport.</summary>
    /// <param name="options">The transport options.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">No bind address or peers are configured, or a value is invalid.</exception>
    public NanoMsgBusTransport(NanoMsgBusTransportOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));

        if (string.IsNullOrWhiteSpace(_options.BindAddress) && _options.Peers.Count == 0)
        {
            throw new ArgumentException(
                "At least one of BindAddress or Peers must be configured.", nameof(options));
        }

        if (_options.MaxFrameLength <= 0)
        {
            throw new ArgumentException("MaxFrameLength must be positive.", nameof(options));
        }
    }

    /// <inheritdoc/>
    public event Action<ReadOnlyMemory<byte>>? FrameReceived;

    /// <summary>
    /// Gets the resolved local TCP port assigned after <see cref="StartAsync"/> binds a <c>tcp</c>
    /// endpoint, or <c>-1</c> before binding (and for non-tcp transports, which report <c>0</c>).
    /// </summary>
    public int BoundPort => Volatile.Read(ref _boundPort);

    /// <summary>Adds a peer endpoint to dial. May be called before or after <see cref="StartAsync"/>.</summary>
    /// <param name="address">The peer endpoint address, for example <c>tcp://10.0.0.2:5560</c>.</param>
    public void AddPeer(string address)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            throw new ArgumentException("The peer address must be non-empty.", nameof(address));
        }

        BusSocket? socket;
        lock (_gate)
        {
            socket = _socket;
            if (socket is null)
            {
                _pendingPeers.Add(address);
                return;
            }
        }

        socket.Connect(address);
    }

    /// <summary>Adds multiple peer endpoints to dial.</summary>
    /// <param name="addresses">The peer endpoint addresses.</param>
    public void AddPeers(IEnumerable<string> addresses)
    {
        foreach (string address in addresses ?? throw new ArgumentNullException(nameof(addresses)))
        {
            AddPeer(address);
        }
    }

    /// <inheritdoc/>
    public async ValueTask StartAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (Interlocked.Exchange(ref _started, 1) == 1)
        {
            return;
        }

        var socket = new BusSocket(_options.SocketOptions ?? new NanoSocketOptions());
        if (!string.IsNullOrWhiteSpace(_options.BindAddress))
        {
            int port = await socket.BindAsync(_options.BindAddress!, ct).ConfigureAwait(false);
            Volatile.Write(ref _boundPort, port);
        }

        string[] peers;
        lock (_gate)
        {
            _socket = socket;
            peers = [.. _options.Peers, .. _pendingPeers];
            _pendingPeers.Clear();
        }

        foreach (string peer in peers)
        {
            socket.Connect(peer);
        }

        _receiveLoop = ReceiveLoopAsync(socket, _stop.Token);
    }

    /// <inheritdoc/>
    public async ValueTask SendAsync(ReadOnlyMemory<byte> frame, CancellationToken ct = default)
    {
        if (frame.Length > _options.MaxFrameLength)
        {
            throw new ArgumentException(
                "The frame is larger than the maximum frame length.", nameof(frame));
        }

        // Validate the frame is a well-formed length-prefixed message before it leaves this node.
        FrameCodec.Decode(frame, _options.MaxFrameLength);

        BusSocket? socket = Volatile.Read(ref _socket);
        if (socket is null)
        {
            throw new InvalidOperationException("The transport has not started.");
        }

        try
        {
            await socket.SendAsync(frame, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested && IsTransientFault(ex))
        {
            // Best-effort broadcast: a peer connection in the middle of (re)connecting can transiently
            // fault. The application's repeated anti-entropy broadcasts retry, so convergence is
            // unaffected — mirroring the connectionless UDP and DTLS gossip transports.
        }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        _stop.Cancel();

        BusSocket? socket = Volatile.Read(ref _socket);
        if (socket is not null)
        {
            try
            {
                await socket.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex) when (IsExpectedFault(ex) || IsTransientFault(ex))
            {
                // Tearing down in-flight connect/read loops can surface a completed-pipe fault; ignore.
            }
        }

        if (_receiveLoop is not null)
        {
            try
            {
                await _receiveLoop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        _stop.Dispose();
    }

    private async Task ReceiveLoopAsync(BusSocket socket, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            NanoMessage message;
            try
            {
                message = await socket.ReceiveAsync(ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (IsExpectedFault(ex))
            {
                return;
            }
            catch (Exception ex) when (IsTransientFault(ex))
            {
                // A peer connection (re)connecting can transiently fault the receive path; retry.
                await Task.Delay(10, CancellationToken.None).ConfigureAwait(false);
                continue;
            }

            using (message)
            {
                byte[] frame = message.Payload.ToArray();
                if (FrameCodec.TryDecode(frame, out _, _options.MaxFrameLength))
                {
                    FrameReceived?.Invoke(frame);
                }
            }
        }
    }

    private static bool IsExpectedFault(Exception ex) =>
        ex is OperationCanceledException or ObjectDisposedException;

    private static bool IsTransientFault(Exception ex) =>
        ex is InvalidOperationException or IOException;
}
