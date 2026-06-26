// Copyright (c) marcschier. Licensed under the MIT License.

namespace Crdt.Transport;

/// <summary>Receives transport frames and sends complete length-prefixed frames to peers.</summary>
public interface ITransport : IAsyncDisposable
{
    /// <summary>Raised when a complete frame is received from a peer.</summary>
    event Action<ReadOnlyMemory<byte>>? FrameReceived;

    /// <summary>Starts the transport and any receive loops it owns.</summary>
    /// <param name="ct">A token that cancels the start operation.</param>
    /// <returns>A task-like value that completes when the transport has started.</returns>
    ValueTask StartAsync(CancellationToken ct = default);

    /// <summary>Sends a complete frame to this transport's peers.</summary>
    /// <param name="frame">The encoded frame, including its length prefix.</param>
    /// <param name="ct">A token that cancels the send operation.</param>
    /// <returns>A task-like value that completes when the frame has been queued or sent.</returns>
    ValueTask SendAsync(ReadOnlyMemory<byte> frame, CancellationToken ct = default);
}
