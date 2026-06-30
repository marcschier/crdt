// Copyright (c) marcschier. Licensed under the MIT License.

using Crdt.Transport;

namespace Crdt.Consensus;

/// <summary>Configures the message transport used by consensus components.</summary>
/// <remarks>
/// Configure either <see cref="Transport"/> or the delegate pair
/// <see cref="SendAsync"/>, <see cref="RegisterReceiver"/>, and <see cref="UnregisterReceiver"/>.
/// </remarks>
public sealed class ConsensusTransportOptions
{
    /// <summary>Gets or sets an existing CRDT transport used to exchange consensus frames.</summary>
    public ITransport? Transport { get; set; }

    /// <summary>
    /// Gets or sets a delegate that sends one complete consensus transport frame when
    /// <see cref="Transport"/> is not used.
    /// </summary>
    public ConsensusSendCallback? SendAsync { get; set; }

    /// <summary>
    /// Gets or sets a delegate that subscribes a receiver for complete consensus transport frames when
    /// <see cref="Transport"/> is not used.
    /// </summary>
    public Action<Action<ReadOnlyMemory<byte>>>? RegisterReceiver { get; set; }

    /// <summary>
    /// Gets or sets a delegate that unsubscribes a receiver previously passed to
    /// <see cref="RegisterReceiver"/> when <see cref="Transport"/> is not used.
    /// </summary>
    public Action<Action<ReadOnlyMemory<byte>>>? UnregisterReceiver { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether <see cref="Transport"/> is started by the component.
    /// </summary>
    public bool StartTransport { get; set; } = true;

    internal void Validate()
    {
        bool hasTransport = Transport is not null;
        bool hasSend = SendAsync is not null;
        bool hasRegister = RegisterReceiver is not null;
        bool hasUnregister = UnregisterReceiver is not null;
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

    internal void Register(Action<ReadOnlyMemory<byte>> receiver)
    {
        if (Transport is not null)
        {
            Transport.FrameReceived += receiver;
            return;
        }

        RegisterReceiver!(receiver);
    }

    internal void Unregister(Action<ReadOnlyMemory<byte>> receiver)
    {
        if (Transport is not null)
        {
            Transport.FrameReceived -= receiver;
            return;
        }

        UnregisterReceiver!(receiver);
    }

    internal ValueTask SendMessageAsync(ReadOnlyMemory<byte> message, CancellationToken ct)
    {
        if (Transport is not null)
        {
            return Transport.SendAsync(message, ct);
        }

        return SendAsync!(message, ct);
    }

    internal ValueTask StartAsync(CancellationToken ct)
    {
        if (StartTransport && Transport is not null)
        {
            return Transport.StartAsync(ct);
        }

        ct.ThrowIfCancellationRequested();
        return default;
    }
}
