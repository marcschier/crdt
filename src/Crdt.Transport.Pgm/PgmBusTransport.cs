// Copyright (c) marcschier. Licensed under the MIT License.

using System.Net;
using Pgm;
using Pgm.Net;
using Pgm.Packets;
using Pgm.Sender;

namespace Crdt.Transport.Pgm;

/// <summary>
/// A reliable multicast transport that replicates CRDT frames over PGM (Pragmatic General Multicast,
/// RFC 3208). Each replica publishes frames to a multicast group and receives frames from the same group.
/// </summary>
/// <remarks>
/// PGM provides reliable ordered delivery from each publisher to receivers in the multicast session. Each CRDT
/// frame is published as one PGM APDU and validated with <see cref="FrameCodec"/> before send and dispatch.
/// </remarks>
public sealed class PgmBusTransport : ITransport
{
    private static readonly Lazy<InMemoryMulticastBus> SharedInMemoryBus = new(CreateInMemoryBus);
    private static int _nextSourcePort = 7_499;

    private readonly PgmBusTransportOptions _options;
    private readonly CancellationTokenSource _stop = new();
    private PgmSender? _publisher;
    private PgmSubscriber? _subscriber;
    private Task? _receiveLoop;
    private int _started;
    private int _disposed;

    /// <summary>Initializes a PGM multicast transport.</summary>
    /// <param name="options">The transport options.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">One or more option values are invalid.</exception>
    public PgmBusTransport(PgmBusTransportOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        ValidateOptions(_options);
    }

    /// <inheritdoc/>
    public event Action<ReadOnlyMemory<byte>>? FrameReceived;

    /// <inheritdoc/>
    public async ValueTask StartAsync(CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ct.ThrowIfCancellationRequested();

        if (Interlocked.Exchange(ref _started, 1) == 1)
        {
            return;
        }

        PgmSender? publisher = null;
        PgmSubscriber? subscriber = null;
        try
        {
            (publisher, subscriber) = CreateEndpoints();
            await subscriber.StartAsync(ct).ConfigureAwait(false);
            await publisher.StartAsync(ct).ConfigureAwait(false);

            _publisher = publisher;
            _subscriber = subscriber;
            _receiveLoop = ReceiveLoopAsync(subscriber, _stop.Token);
        }
        catch
        {
            Interlocked.Exchange(ref _started, 0);
            await DisposeEndpointAsync(publisher).ConfigureAwait(false);
            await DisposeEndpointAsync(subscriber).ConfigureAwait(false);
            throw;
        }
    }

    /// <inheritdoc/>
    public async ValueTask SendAsync(ReadOnlyMemory<byte> frame, CancellationToken ct = default)
    {
        ThrowIfDisposed();

        if (frame.Length > _options.MaxFrameLength)
        {
            throw new ArgumentException(
                "The frame is larger than the maximum frame length.", nameof(frame));
        }

        FrameCodec.Decode(frame, _options.MaxFrameLength);

        PgmSender? publisher = Volatile.Read(ref _publisher);
        if (publisher is null)
        {
            throw new InvalidOperationException("The transport has not started.");
        }

        await publisher.SendAsync(frame, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        _stop.Cancel();

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

        await DisposeEndpointAsync(Volatile.Read(ref _publisher)).ConfigureAwait(false);
        await DisposeEndpointAsync(Volatile.Read(ref _subscriber)).ConfigureAwait(false);
        _stop.Dispose();
    }

    private static void ValidateOptions(PgmBusTransportOptions options)
    {
        if (options.MulticastGroup is null)
        {
            throw new ArgumentException("MulticastGroup must be configured.", nameof(options));
        }

        if (options.Port is < 0 or > 65535)
        {
            throw new ArgumentException("Port must be between 0 and 65535.", nameof(options));
        }

        if (options.MaxFrameLength <= 0)
        {
            throw new ArgumentException("MaxFrameLength must be positive.", nameof(options));
        }

        bool hasPublisherChannel = options.PublisherChannel is not null;
        bool hasSubscriberChannel = options.SubscriberChannel is not null;
        if (hasPublisherChannel != hasSubscriberChannel)
        {
            throw new ArgumentException(
                "PublisherChannel and SubscriberChannel must either both be configured or both be null.",
                nameof(options));
        }

        if ((hasPublisherChannel && options.InMemoryBus is not null) || (hasPublisherChannel && options.UseInMemoryBus))
        {
            throw new ArgumentException(
                "Explicit datagram channels cannot be combined with an in-memory multicast bus.", nameof(options));
        }
    }

    private static async ValueTask DisposeEndpointAsync(IAsyncDisposable? endpoint)
    {
        if (endpoint is not null)
        {
            await endpoint.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static InMemoryMulticastBus CreateInMemoryBus()
    {
        return new InMemoryMulticastBus();
    }

    private (PgmSender Publisher, PgmSubscriber Subscriber) CreateEndpoints()
    {
        PgmSenderOptions publisherOptions = CreatePublisherOptions();
        PgmSubscriberOptions subscriberOptions = CreateSubscriberOptions();

        if (_options.InMemoryBus is not null || _options.UseInMemoryBus)
        {
            InMemoryMulticastBus bus = _options.InMemoryBus ?? SharedInMemoryBus.Value;
            return (
                new PgmSender(bus.CreateChannel(), publisherOptions),
                new PgmSubscriber(bus.CreateChannel(), subscriberOptions));
        }

        if (_options.PublisherChannel is not null && _options.SubscriberChannel is not null)
        {
            return (
                new PgmSender(_options.PublisherChannel, publisherOptions),
                new PgmSubscriber(_options.SubscriberChannel, subscriberOptions));
        }

        return (
            new PgmSender(
                new UdpMulticastChannel(_options.MulticastGroup, _options.Port),
                publisherOptions),
            new PgmSubscriber(subscriberOptions));
    }

    private PgmSenderOptions CreatePublisherOptions()
    {
        return new PgmSenderOptions
        {
            SourcePort = NextSourcePort(),
            SourceAddress = ToPgmAddress(LoopbackFor(_options.MulticastGroup)),
            GroupAddress = ToPgmAddress(_options.MulticastGroup),
            ProactiveParityPacketCount = 0,
        };
    }

    private PgmSubscriberOptions CreateSubscriberOptions()
    {
        return new PgmSubscriberOptions
        {
            MulticastGroup = _options.MulticastGroup,
            Port = _options.Port,
        };
    }

    private static ushort NextSourcePort()
    {
        int sourcePort = Interlocked.Increment(ref _nextSourcePort);
        if (sourcePort > ushort.MaxValue)
        {
            sourcePort = 7_500;
            Interlocked.Exchange(ref _nextSourcePort, sourcePort);
        }

        return (ushort)sourcePort;
    }

    private static IPAddress LoopbackFor(IPAddress multicastGroup)
    {
        return multicastGroup.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6
            ? IPAddress.IPv6Loopback
            : IPAddress.Loopback;
    }

    private static PgmNetworkAddress ToPgmAddress(IPAddress address)
    {
        PgmAddressFamily family = address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
            ? PgmAddressFamily.IPv4
            : PgmAddressFamily.IPv6;
        return new PgmNetworkAddress(family, address.GetAddressBytes());
    }

    private async Task ReceiveLoopAsync(PgmSubscriber subscriber, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            byte[] frame;
            try
            {
                frame = await subscriber.ReceiveAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            if (FrameCodec.TryDecode(frame, out _, _options.MaxFrameLength))
            {
                FrameReceived?.Invoke(frame);
            }
        }
    }

    private void ThrowIfDisposed()
    {
#if NET8_0_OR_GREATER
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) == 1, this);
#else
        if (Volatile.Read(ref _disposed) == 1)
        {
            throw new ObjectDisposedException(nameof(PgmBusTransport));
        }
#endif
    }
}
