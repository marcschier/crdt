// Copyright (c) marcschier. Licensed under the MIT License.

using System.Threading.Channels;

namespace Crdt.Transport;

/// <summary>An in-process transport that broadcasts frames to peers in an <see cref="InMemoryNetwork"/>.</summary>
public sealed class InMemoryTransport : ITransport
{
    private readonly InMemoryNetwork _network;
    private readonly Channel<byte[]> _inbox;
    private readonly CancellationTokenSource _stop = new();
    private Task? _pump;
    private int _pendingFrames;
    private bool _started;

    /// <summary>Initializes a transport attached to <paramref name="network"/>.</summary>
    /// <param name="network">The in-memory peer registry.</param>
    public InMemoryTransport(InMemoryNetwork network)
    {
        _network = network ?? throw new ArgumentNullException(nameof(network));
        _inbox = Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });
    }

    /// <inheritdoc/>
    public event Action<ReadOnlyMemory<byte>>? FrameReceived;

    internal int PendingFrames => Volatile.Read(ref _pendingFrames);

    /// <inheritdoc/>
    public ValueTask StartAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (_started)
        {
            return default;
        }

        _started = true;
        _network.Register(this);
        _pump = PumpAsync(_stop.Token);
        return default;
    }

    /// <inheritdoc/>
    public ValueTask SendAsync(ReadOnlyMemory<byte> frame, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        foreach (InMemoryTransport peer in _network.PeersExcept(this))
        {
            peer.Enqueue(frame);
        }

        return default;
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        _network.Unregister(this);
        _inbox.Writer.TryComplete();
        await _stop.CancelAsync().ConfigureAwait(false);
        if (_pump is not null)
        {
            try
            {
                await _pump.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        _stop.Dispose();
    }

    private void Enqueue(ReadOnlyMemory<byte> frame)
    {
        Interlocked.Increment(ref _pendingFrames);
        if (!_inbox.Writer.TryWrite(frame.ToArray()))
        {
            Interlocked.Decrement(ref _pendingFrames);
            throw new InvalidOperationException("The in-memory transport is closed.");
        }
    }

    private async Task PumpAsync(CancellationToken ct)
    {
        await foreach (byte[] frame in _inbox.Reader.ReadAllAsync(ct).ConfigureAwait(false))
        {
            try
            {
                FrameReceived?.Invoke(frame);
            }
            finally
            {
                Interlocked.Decrement(ref _pendingFrames);
            }
        }
    }
}
