// Copyright (c) marcschier. Licensed under the MIT License.

namespace Crdt.Transport;

/// <summary>Coordinates CRDT serialization, merging, and transport delivery.</summary>
/// <typeparam name="TState">The replicated CRDT state type.</typeparam>
public sealed class ReplicationEngine<TState> : IAsyncDisposable
    where TState : IConvergent<TState>
{
    private readonly CrdtReplica<TState> _replica;
    private readonly ITransport _transport;
    private readonly Func<ReadOnlyMemory<byte>, bool>? _applyOperation;
    private bool _disposed;

    /// <summary>Initializes a replication engine.</summary>
    /// <param name="replica">The local CRDT replica wrapper.</param>
    /// <param name="transport">The transport used to exchange frames.</param>
    /// <param name="mode">The replication mode used by broadcasts.</param>
    /// <param name="applyOperation">Applies decoded operation bytes for operation mode.</param>
    public ReplicationEngine(
        CrdtReplica<TState> replica,
        ITransport transport,
        ReplicationMode mode = ReplicationMode.State,
        Func<ReadOnlyMemory<byte>, bool>? applyOperation = null)
    {
        _replica = replica ?? throw new ArgumentNullException(nameof(replica));
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        Mode = mode;
        _applyOperation = applyOperation;
        _transport.FrameReceived += OnFrameReceived;
    }

    /// <summary>Raised after a received frame changes or may have changed the local value.</summary>
    public event Action? Changed;

    /// <summary>Gets the local CRDT replica wrapper.</summary>
    public CrdtReplica<TState> Replica => _replica;

    /// <summary>Gets the transport used by this engine.</summary>
    public ITransport Transport => _transport;

    /// <summary>Gets or sets the replication mode used by <see cref="BroadcastStateAsync"/>.</summary>
    public ReplicationMode Mode { get; set; }

    /// <summary>Starts the underlying transport.</summary>
    /// <param name="ct">A token that cancels the start operation.</param>
    /// <returns>A task-like value that completes when the transport has started.</returns>
    public ValueTask StartAsync(CancellationToken ct = default) => _transport.StartAsync(ct);

    /// <summary>Broadcasts either a state snapshot or a delta, according to <see cref="Mode"/>.</summary>
    /// <param name="ct">A token that cancels the send operation.</param>
    /// <returns>A task-like value that completes when the frame has been sent.</returns>
    public ValueTask BroadcastStateAsync(CancellationToken ct = default)
    {
        if (Mode == ReplicationMode.Delta && _replica.TryGetDeltaBytes(out byte[] deltaBytes))
        {
            return SendAsync(MessageType.Delta, deltaBytes, ct);
        }

        return SendAsync(MessageType.State, _replica.SnapshotState(), ct);
    }

    /// <summary>Broadcasts operation bytes in operation mode.</summary>
    /// <param name="operation">The encoded operation payload.</param>
    /// <param name="ct">A token that cancels the send operation.</param>
    /// <returns>A task-like value that completes when the frame has been sent.</returns>
    public ValueTask BroadcastOperationAsync(ReadOnlyMemory<byte> operation, CancellationToken ct = default) =>
        SendAsync(MessageType.Operation, operation, ct);

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _transport.FrameReceived -= OnFrameReceived;
        await _transport.DisposeAsync().ConfigureAwait(false);
    }

    private ValueTask SendAsync(MessageType messageType, ReadOnlyMemory<byte> payload, CancellationToken ct)
    {
        byte[] frame = FrameCodec.Encode(messageType, payload.Span);
        return _transport.SendAsync(frame, ct);
    }

    private void OnFrameReceived(ReadOnlyMemory<byte> frame)
    {
        DecodedFrame decoded = FrameCodec.Decode(frame);
        switch (decoded.MessageType)
        {
            case MessageType.State:
                _replica.MergeStateBytes(decoded.Payload);
                Changed?.Invoke();
                break;
            case MessageType.Delta:
                _replica.MergeDeltaBytes(decoded.Payload);
                Changed?.Invoke();
                break;
            case MessageType.Operation:
                if (_applyOperation?.Invoke(decoded.Payload) == true)
                {
                    Changed?.Invoke();
                }

                break;
        }
    }
}
