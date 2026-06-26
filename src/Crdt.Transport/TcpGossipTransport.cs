// Copyright (c) marcschier. Licensed under the MIT License.

using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

namespace Crdt.Transport;

/// <summary>A TCP transport that combines direct sends with periodic push-pull anti-entropy gossip.</summary>
public sealed class TcpGossipTransport : ITransport
{
    private readonly object _gate = new();
    private readonly TcpGossipTransportOptions _options;
    private readonly HashSet<IPEndPoint> _peers = [];
    private readonly CancellationTokenSource _stop = new();
    private TcpListener? _listener;
    private Task? _acceptLoop;
    private Task? _gossipLoop;
    private byte[]? _lastFrame;
    private bool _started;

    /// <summary>Initializes a TCP gossip transport.</summary>
    /// <param name="address">The local address to bind.</param>
    /// <param name="port">The local TCP port to bind, or 0 for an OS-assigned port.</param>
    /// <param name="gossipInterval">The periodic gossip interval.</param>
    /// <param name="maxFrameLength">The maximum accepted frame body length.</param>
    public TcpGossipTransport(
        IPAddress address,
        int port,
        TimeSpan? gossipInterval = null,
        int maxFrameLength = FrameCodec.DefaultMaxFrameLength)
        : this(new TcpGossipTransportOptions
        {
            Address = address,
            Port = port,
            GossipInterval = gossipInterval ?? TimeSpan.FromMilliseconds(250),
            MaxFrameLength = maxFrameLength,
        })
    {
    }

    /// <summary>Initializes a TCP gossip transport.</summary>
    /// <param name="options">The transport options.</param>
    public TcpGossipTransport(TcpGossipTransportOptions options)
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
            if (_listener?.LocalEndpoint is IPEndPoint endpoint)
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
        ArgumentNullException.ThrowIfNull(endpoint);
        lock (_gate)
        {
            if (_listener?.LocalEndpoint is IPEndPoint local && SameEndpoint(local, endpoint))
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
        ArgumentNullException.ThrowIfNull(endpoints);
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
            return ValueTask.CompletedTask;
        }

        _started = true;
        _listener = new TcpListener(_options.Address, _options.Port);
        _listener.Start();
        _acceptLoop = AcceptLoopAsync(_stop.Token);
        _gossipLoop = GossipLoopAsync(_stop.Token);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public async ValueTask SendAsync(ReadOnlyMemory<byte> frame, CancellationToken ct = default)
    {
        byte[] copy = frame.ToArray();
        FrameCodec.Decode(copy, _options.MaxFrameLength);
        Volatile.Write(ref _lastFrame, copy);

        foreach (IPEndPoint peer in SnapshotPeers())
        {
            await ExchangeAsync(peer, copy, ct).ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync().ConfigureAwait(false);
        _listener?.Stop();
        await AwaitLoopAsync(_acceptLoop).ConfigureAwait(false);
        await AwaitLoopAsync(_gossipLoop).ConfigureAwait(false);
        _stop.Dispose();
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient? client = null;
            try
            {
                client = await _listener!.AcceptTcpClientAsync(ct).ConfigureAwait(false);
                _ = HandleClientAsync(client, ct);
            }
            catch (OperationCanceledException)
            {
                client?.Dispose();
                return;
            }
            catch (SocketException) when (ct.IsCancellationRequested)
            {
                client?.Dispose();
                return;
            }
            catch (ObjectDisposedException)
            {
                client?.Dispose();
                return;
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
                await ExchangeAsync(peer, frame, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        using (client)
        {
            await using NetworkStream network = client.GetStream();
            SslStream? tls = null;
            try
            {
                Stream stream = network;
                if (_options.Tls is { } tlsOptions)
                {
                    tls = new SslStream(network, leaveInnerStreamOpen: true);
                    await AuthenticateServerAsync(tls, tlsOptions, ct).ConfigureAwait(false);
                    stream = tls;
                }

                byte[] frame = await ReadFrameAsync(stream, _options.MaxFrameLength, ct).ConfigureAwait(false);
                RaiseFrame(frame);
                byte[] response = Volatile.Read(ref _lastFrame) ?? FrameCodec.Encode(MessageType.Ack, []);
                await stream.WriteAsync(response, ct).ConfigureAwait(false);
                await stream.FlushAsync(ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (IsNetworkFailure(ex, ct))
            {
            }
            finally
            {
                if (tls is not null)
                {
                    await tls.DisposeAsync().ConfigureAwait(false);
                }
            }
        }
    }

    private async ValueTask ExchangeAsync(IPEndPoint endpoint, byte[] frame, CancellationToken ct)
    {
        using var client = new TcpClient(endpoint.AddressFamily);
        SslStream? tls = null;
        try
        {
            await client.ConnectAsync(endpoint.Address, endpoint.Port, ct).ConfigureAwait(false);
            await using NetworkStream network = client.GetStream();
            try
            {
                Stream stream = network;
                if (_options.Tls is { } tlsOptions)
                {
                    tls = new SslStream(network, leaveInnerStreamOpen: true);
                    string targetHost = tlsOptions.TargetHost ?? endpoint.Address.ToString();
                    await AuthenticateClientAsync(tls, tlsOptions, targetHost, ct).ConfigureAwait(false);
                    stream = tls;
                }

                await stream.WriteAsync(frame, ct).ConfigureAwait(false);
                await stream.FlushAsync(ct).ConfigureAwait(false);
                byte[] response = await ReadFrameAsync(stream, _options.MaxFrameLength, ct).ConfigureAwait(false);
                RaiseFrame(response);
            }
            finally
            {
                if (tls is not null)
                {
                    await tls.DisposeAsync().ConfigureAwait(false);
                }
            }
        }
        catch (Exception ex) when (IsNetworkFailure(ex, ct))
        {
        }
    }

    private void RaiseFrame(byte[] frame)
    {
        DecodedFrame decoded = FrameCodec.Decode(frame, _options.MaxFrameLength);
        if (decoded.MessageType != MessageType.Ack)
        {
            FrameReceived?.Invoke(frame);
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
        return peers.Length == 0 ? null : peers[Random.Shared.Next(peers.Length)];
    }

    private static async ValueTask<byte[]> ReadFrameAsync(
        Stream stream,
        int maxFrameLength,
        CancellationToken ct)
    {
        var prefixBuffer = new byte[10];
        int prefixLength = 0;
        ulong bodyLength;
        while (true)
        {
            if (prefixLength == prefixBuffer.Length)
            {
                throw new InvalidDataException("Varint length prefix is too long.");
            }

            await ReadExactlyAsync(stream, prefixBuffer.AsMemory(prefixLength, 1), ct).ConfigureAwait(false);
            prefixLength++;
            if ((prefixBuffer[prefixLength - 1] & 0x80) == 0)
            {
                bodyLength = FrameCodec.ReadVarUInt64(prefixBuffer.AsSpan(0, prefixLength), out _);
                break;
            }
        }

        FrameCodec.ValidateBodyLength(bodyLength, maxFrameLength);
        int bodyBytes = checked((int)bodyLength);
        var frame = new byte[prefixLength + bodyBytes];
        prefixBuffer.AsSpan(0, prefixLength).CopyTo(frame);
        await ReadExactlyAsync(stream, frame.AsMemory(prefixLength, bodyBytes), ct).ConfigureAwait(false);
        return frame;
    }

    private static async ValueTask ReadExactlyAsync(
        Stream stream,
        Memory<byte> buffer,
        CancellationToken ct)
    {
        int read = 0;
        while (read < buffer.Length)
        {
            int bytes = await stream.ReadAsync(buffer.Slice(read), ct).ConfigureAwait(false);
            if (bytes == 0)
            {
                throw new IOException("The peer disconnected before the frame completed.");
            }

            read += bytes;
        }
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

    private static async Task AuthenticateServerAsync(
        SslStream tls,
        GossipTlsOptions options,
        CancellationToken ct)
    {
        var authOptions = new SslServerAuthenticationOptions
        {
            ServerCertificate = options.ServerCertificate,
            ClientCertificateRequired = options.RequireClientCertificate,
            EnabledSslProtocols = options.EnabledSslProtocols,
            CertificateRevocationCheckMode = options.CheckCertificateRevocation
                ? X509RevocationMode.Online
                : X509RevocationMode.NoCheck,
        };

        // Only validate a client certificate when one is required; otherwise the validation callback
        // would be invoked with a null certificate and reject the (legitimately absent) client cert.
        if (options.RequireClientCertificate)
        {
            authOptions.RemoteCertificateValidationCallback = options.RemoteCertificateValidationCallback;
        }

        await tls.AuthenticateAsServerAsync(authOptions, ct).ConfigureAwait(false);
    }

    private static async Task AuthenticateClientAsync(
        SslStream tls,
        GossipTlsOptions options,
        string targetHost,
        CancellationToken ct)
    {
        var authOptions = new SslClientAuthenticationOptions
        {
            TargetHost = targetHost,
            ClientCertificates = options.ClientCertificates,
            EnabledSslProtocols = options.EnabledSslProtocols,
            RemoteCertificateValidationCallback = options.RemoteCertificateValidationCallback,
            CertificateRevocationCheckMode = options.CheckCertificateRevocation
                ? X509RevocationMode.Online
                : X509RevocationMode.NoCheck,
        };

        await tls.AuthenticateAsClientAsync(authOptions, ct).ConfigureAwait(false);
    }

    private static bool IsNetworkFailure(Exception ex, CancellationToken ct) =>
        ex is IOException or SocketException or ObjectDisposedException or InvalidDataException
            or AuthenticationException ||
        ex is OperationCanceledException && ct.IsCancellationRequested;

    private static bool SameEndpoint(IPEndPoint left, IPEndPoint right) =>
        left.Port == right.Port && left.Address.Equals(right.Address);
}
