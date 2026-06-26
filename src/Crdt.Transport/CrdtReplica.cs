// Copyright (c) marcschier. Licensed under the MIT License.

namespace Crdt.Transport;

/// <summary>Wraps a CRDT state value with reflection-free state and delta serialization delegates.</summary>
/// <typeparam name="TState">The CRDT state type.</typeparam>
public sealed class CrdtReplica<TState>
    where TState : IConvergent<TState>
{
    private readonly object _gate = new();
    private readonly Func<TState, byte[]> _serializeState;
    private readonly Func<ReadOnlyMemory<byte>, TState> _deserializeState;
    private readonly Func<byte[]?>? _tryGetDeltaBytes;
    private readonly Action<ReadOnlyMemory<byte>>? _mergeDeltaBytes;

    /// <summary>Initializes a replica that supports full-state snapshot replication.</summary>
    /// <param name="value">The local mutable CRDT state value.</param>
    /// <param name="serializeState">Serializes a state value without reflection.</param>
    /// <param name="deserializeState">Deserializes a state value without reflection.</param>
    public CrdtReplica(
        TState value,
        Func<TState, byte[]> serializeState,
        Func<ReadOnlyMemory<byte>, TState> deserializeState)
    {
        Value = value ?? throw new ArgumentNullException(nameof(value));
        _serializeState = serializeState ?? throw new ArgumentNullException(nameof(serializeState));
        _deserializeState = deserializeState ?? throw new ArgumentNullException(nameof(deserializeState));
    }

    /// <summary>Initializes a replica that supports full-state and same-type delta-state replication.</summary>
    /// <param name="value">The local mutable CRDT state value.</param>
    /// <param name="serializeState">Serializes a state value without reflection.</param>
    /// <param name="deserializeState">Deserializes a state value without reflection.</param>
    /// <param name="extractDelta">Extracts and clears a pending delta from the state.</param>
    /// <param name="serializeDelta">Serializes a delta value without reflection.</param>
    /// <param name="deserializeDelta">Deserializes a delta value without reflection.</param>
    /// <param name="mergeDelta">Merges a decoded delta into the state.</param>
    public CrdtReplica(
        TState value,
        Func<TState, byte[]> serializeState,
        Func<ReadOnlyMemory<byte>, TState> deserializeState,
        CrdtDeltaExtractor<TState, TState> extractDelta,
        Func<TState, byte[]> serializeDelta,
        Func<ReadOnlyMemory<byte>, TState> deserializeDelta,
        Action<TState, TState> mergeDelta)
        : this(value, serializeState, deserializeState)
    {
        ArgumentNullException.ThrowIfNull(extractDelta);
        ArgumentNullException.ThrowIfNull(serializeDelta);
        ArgumentNullException.ThrowIfNull(deserializeDelta);
        ArgumentNullException.ThrowIfNull(mergeDelta);

        _tryGetDeltaBytes = () =>
        {
            lock (_gate)
            {
                if (!extractDelta(Value, out TState delta))
                {
                    return null;
                }

                return serializeDelta(delta);
            }
        };

        _mergeDeltaBytes = bytes =>
        {
            TState delta = deserializeDelta(bytes);
            lock (_gate)
            {
                mergeDelta(Value, delta);
            }
        };
    }

    /// <summary>Gets the local mutable CRDT value.</summary>
    public TState Value { get; }

    /// <summary>Serializes the current state snapshot.</summary>
    /// <returns>The serialized state bytes.</returns>
    public byte[] SnapshotState()
    {
        lock (_gate)
        {
            return _serializeState(Value);
        }
    }

    /// <summary>Deserializes and merges a state snapshot into the local value.</summary>
    /// <param name="stateBytes">The serialized state bytes.</param>
    public void MergeStateBytes(ReadOnlyMemory<byte> stateBytes)
    {
        TState state = _deserializeState(stateBytes);
        lock (_gate)
        {
            Value.Merge(state);
        }
    }

    /// <summary>Attempts to serialize the currently buffered delta.</summary>
    /// <param name="deltaBytes">The serialized delta bytes when a delta is available.</param>
    /// <returns><see langword="true"/> when a delta was available; otherwise <see langword="false"/>.</returns>
    public bool TryGetDeltaBytes(out byte[] deltaBytes)
    {
        if (_tryGetDeltaBytes is null)
        {
            deltaBytes = [];
            return false;
        }

        byte[]? bytes = _tryGetDeltaBytes();
        if (bytes is null)
        {
            deltaBytes = [];
            return false;
        }

        deltaBytes = bytes;
        return true;
    }

    /// <summary>Deserializes and merges a delta into the local value.</summary>
    /// <param name="deltaBytes">The serialized delta bytes.</param>
    public void MergeDeltaBytes(ReadOnlyMemory<byte> deltaBytes)
    {
        if (_mergeDeltaBytes is null)
        {
            MergeStateBytes(deltaBytes);
            return;
        }

        _mergeDeltaBytes(deltaBytes);
    }
}
