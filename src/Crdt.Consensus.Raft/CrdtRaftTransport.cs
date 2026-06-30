// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using Crdt.Transport;

namespace Crdt.Consensus.Raft;

/// <summary>Adapts a CRDT consensus transport to the <c>IRaftTransport</c> API.</summary>
/// <remarks>
/// RaftCs sends point-to-point frames. CRDT transports broadcast complete frames, so this
/// adapter wraps each Raft frame with the intended recipient node id and filters inbound
/// frames for the local node id or <see cref="BroadcastNodeId"/>.
/// </remarks>
public sealed class CrdtRaftTransport : global::Raft.Transport.IRaftTransport
{
    /// <summary>Reserved recipient id meaning every Raft adapter may accept the frame.</summary>
    public const ulong BroadcastNodeId = 0;

    private const byte Magic0 = (byte)'C';
    private const byte Magic1 = (byte)'R';
    private const byte Magic2 = (byte)'F';
    private const byte Magic3 = (byte)'T';
    private const byte Version = 1;
    private const int VersionOffset = 4;
    private const int RecipientOffset = 5;
    private const int PayloadLengthOffset = RecipientOffset + 8;
    private const int HeaderLength = PayloadLengthOffset + 4;

    private readonly ConsensusTransportOptions _transport;
    private readonly ulong _localNodeId;
    private readonly int _maxFrameLength;
    private int _started;
    private int _disposed;

    /// <summary>Initializes a CRDT-backed Raft transport adapter.</summary>
    /// <param name="transport">The CRDT consensus transport options.</param>
    /// <param name="registry">The registry used to map the local replica id.</param>
    /// <param name="localReplicaId">The local CRDT replica id.</param>
    /// <param name="maxFrameLength">The maximum encoded CRDT frame body length.</param>
    /// <exception cref="ArgumentNullException">A required reference is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A required option is missing or invalid.</exception>
    public CrdtRaftTransport(
        ConsensusTransportOptions transport,
        IReplicaIdRegistry registry,
        ReplicaId localReplicaId,
        int maxFrameLength = FrameCodec.DefaultMaxFrameLength)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
#if NET8_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(registry);
#else
        if (registry is null)
        {
            throw new ArgumentNullException(nameof(registry));
        }
#endif
        ValidateTransport(_transport);

        if (maxFrameLength <= 0)
        {
            throw new ArgumentException("MaxFrameLength must be positive.", nameof(maxFrameLength));
        }

        _localNodeId = registry.GetNodeId(localReplicaId);
        _maxFrameLength = maxFrameLength;
    }

    /// <inheritdoc/>
    public event Action<ReadOnlyMemory<byte>>? FrameReceived;

    /// <inheritdoc/>
    public async ValueTask StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        if (Interlocked.Exchange(ref _started, 1) == 1)
        {
            return;
        }

        Register(OnFrameReceived);
        if (_transport.StartTransport && _transport.Transport is not null)
        {
            await _transport.Transport.StartAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public ValueTask SendAsync(
        ulong recipient,
        ReadOnlyMemory<byte> frame,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        byte[] wrapped = Encode(recipient, frame, _maxFrameLength);
        return SendWrappedAsync(wrapped, cancellationToken);
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return default;
        }

        if (Volatile.Read(ref _started) == 1)
        {
            Unregister(OnFrameReceived);
        }

        return default;
    }

    private static void ValidateTransport(ConsensusTransportOptions transport)
    {
        bool hasTransport = transport.Transport is not null;
        bool hasSend = transport.SendAsync is not null;
        bool hasRegister = transport.RegisterReceiver is not null;
        bool hasUnregister = transport.UnregisterReceiver is not null;
        bool hasDelegates = hasSend || hasRegister || hasUnregister;

        if (hasTransport == hasDelegates)
        {
            throw new ArgumentException(
                "Configure either Transport or the send/receive delegate pair, but not both.");
        }

        if (hasDelegates && (!hasSend || !hasRegister || !hasUnregister))
        {
            throw new ArgumentException(
                "SendAsync, RegisterReceiver, and UnregisterReceiver must all be configured together.");
        }
    }

    private static byte[] Encode(ulong recipient, ReadOnlyMemory<byte> payload, int maxFrameLength)
    {
        int wrapperLength = checked(HeaderLength + payload.Length);
        var wrapper = new byte[wrapperLength];
        wrapper[0] = Magic0;
        wrapper[1] = Magic1;
        wrapper[2] = Magic2;
        wrapper[3] = Magic3;
        wrapper[VersionOffset] = Version;
        BinaryPrimitives.WriteUInt64BigEndian(wrapper.AsSpan(RecipientOffset, 8), recipient);
        BinaryPrimitives.WriteInt32BigEndian(wrapper.AsSpan(PayloadLengthOffset, 4), payload.Length);
        payload.Span.CopyTo(wrapper.AsSpan(HeaderLength));
        return FrameCodec.Encode(MessageType.Operation, wrapper, maxFrameLength);
    }

    private bool TryDecode(ReadOnlyMemory<byte> message, out ulong recipient, out ReadOnlyMemory<byte> payload)
    {
        recipient = default;
        payload = default;
        if (!FrameCodec.TryDecode(message, out DecodedFrame decoded, _maxFrameLength)
            || decoded.MessageType != MessageType.Operation)
        {
            return false;
        }

        ReadOnlyMemory<byte> bodyMemory = decoded.Payload;
        ReadOnlySpan<byte> body = bodyMemory.Span;
        if (body.Length < HeaderLength
            || body[0] != Magic0
            || body[1] != Magic1
            || body[2] != Magic2
            || body[3] != Magic3
            || body[VersionOffset] != Version)
        {
            return false;
        }

        int payloadLength = BinaryPrimitives.ReadInt32BigEndian(body.Slice(PayloadLengthOffset, 4));
        if (payloadLength < 0 || HeaderLength + payloadLength != body.Length)
        {
            return false;
        }

        recipient = BinaryPrimitives.ReadUInt64BigEndian(body.Slice(RecipientOffset, 8));
        payload = bodyMemory.Slice(HeaderLength, payloadLength);
        return true;
    }

    private ValueTask SendWrappedAsync(ReadOnlyMemory<byte> wrapped, CancellationToken cancellationToken)
    {
        if (_transport.Transport is not null)
        {
            return _transport.Transport.SendAsync(wrapped, cancellationToken);
        }

        return _transport.SendAsync!(wrapped, cancellationToken);
    }

    private void Register(Action<ReadOnlyMemory<byte>> receiver)
    {
        if (_transport.Transport is not null)
        {
            _transport.Transport.FrameReceived += receiver;
            return;
        }

        _transport.RegisterReceiver!(receiver);
    }

    private void Unregister(Action<ReadOnlyMemory<byte>> receiver)
    {
        if (_transport.Transport is not null)
        {
            _transport.Transport.FrameReceived -= receiver;
            return;
        }

        _transport.UnregisterReceiver!(receiver);
    }

    private void OnFrameReceived(ReadOnlyMemory<byte> message)
    {
        if (!TryDecode(message, out ulong recipient, out ReadOnlyMemory<byte> frame))
        {
            return;
        }

        if (recipient != _localNodeId && recipient != BroadcastNodeId)
        {
            return;
        }

        FrameReceived?.Invoke(frame);
    }

    private void ThrowIfDisposed()
    {
#if NET8_0_OR_GREATER
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) == 1, this);
#else
        if (Volatile.Read(ref _disposed) == 1)
        {
            throw new ObjectDisposedException(nameof(CrdtRaftTransport));
        }
#endif
    }
}
