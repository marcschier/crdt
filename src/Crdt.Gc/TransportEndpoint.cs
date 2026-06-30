// Copyright (c) marcschier. Licensed under the MIT License.

using Crdt.Consensus;
using Crdt.Transport;

namespace Crdt.Gc;

internal sealed class TransportEndpoint : IAsyncDisposable
{
    private readonly ITransport? _transport;
    private readonly ConsensusSendCallback? _sendAsync;
    private readonly Action<Action<ReadOnlyMemory<byte>>>? _registerReceiver;
    private readonly Action<Action<ReadOnlyMemory<byte>>>? _unregisterReceiver;
    private readonly bool _startTransport;

    private TransportEndpoint(
        ITransport? transport,
        ConsensusSendCallback? sendAsync,
        Action<Action<ReadOnlyMemory<byte>>>? registerReceiver,
        Action<Action<ReadOnlyMemory<byte>>>? unregisterReceiver,
        bool startTransport)
    {
        _transport = transport;
        _sendAsync = sendAsync;
        _registerReceiver = registerReceiver;
        _unregisterReceiver = unregisterReceiver;
        _startTransport = startTransport;
    }

    public static TransportEndpoint Create(GarbageCollectionCoordinatorOptions options)
    {
        bool hasTransport = options.Transport is not null;
        bool hasTransportOptions = options.TransportOptions is not null;
        if (hasTransport == hasTransportOptions)
        {
            throw new ArgumentException(
                "Configure either Transport or TransportOptions, but not both.",
                nameof(options));
        }

        if (options.Transport is not null)
        {
            return new TransportEndpoint(options.Transport, null, null, null, options.StartTransport);
        }

        ConsensusTransportOptions transportOptions = options.TransportOptions!;
        bool hasOptionTransport = transportOptions.Transport is not null;
        bool hasSend = transportOptions.SendAsync is not null;
        bool hasRegister = transportOptions.RegisterReceiver is not null;
        bool hasUnregister = transportOptions.UnregisterReceiver is not null;
        bool hasDelegates = hasSend || hasRegister || hasUnregister;
        if (hasOptionTransport == hasDelegates)
        {
            throw new ArgumentException(
                "TransportOptions must configure either Transport or the send/receive delegate pair.",
                nameof(options));
        }

        if (transportOptions.Transport is not null)
        {
            return new TransportEndpoint(
                transportOptions.Transport,
                null,
                null,
                null,
                options.StartTransport && transportOptions.StartTransport);
        }

        if (!hasSend || !hasRegister || !hasUnregister)
        {
            throw new ArgumentException(
                "TransportOptions must configure Transport or SendAsync, RegisterReceiver, and UnregisterReceiver.",
                nameof(options));
        }

        return new TransportEndpoint(
            null,
            transportOptions.SendAsync,
            transportOptions.RegisterReceiver,
            transportOptions.UnregisterReceiver,
            false);
    }

    public void Register(Action<ReadOnlyMemory<byte>> receiver)
    {
        if (_transport is not null)
        {
            _transport.FrameReceived += receiver;
            return;
        }

        _registerReceiver!(receiver);
    }

    public void Unregister(Action<ReadOnlyMemory<byte>> receiver)
    {
        if (_transport is not null)
        {
            _transport.FrameReceived -= receiver;
            return;
        }

        _unregisterReceiver!(receiver);
    }

    public ValueTask StartAsync(CancellationToken ct)
    {
        if (_transport is not null && _startTransport)
        {
            return _transport.StartAsync(ct);
        }

        ct.ThrowIfCancellationRequested();
        return default;
    }

    public ValueTask SendAsync(ReadOnlyMemory<byte> message, CancellationToken ct)
    {
        if (_transport is not null)
        {
            return _transport.SendAsync(message, ct);
        }

        return _sendAsync!(message, ct);
    }

    public ValueTask DisposeAsync()
    {
        return _transport is null ? default : _transport.DisposeAsync();
    }
}
