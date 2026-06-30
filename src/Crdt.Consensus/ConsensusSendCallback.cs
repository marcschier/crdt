// Copyright (c) marcschier. Licensed under the MIT License.

namespace Crdt.Consensus;

/// <summary>Sends one complete consensus transport frame.</summary>
/// <param name="message">The complete encoded frame to send.</param>
/// <param name="cancellationToken">A token that cancels the send operation.</param>
/// <returns>A task-like value that completes when the frame has been queued or sent.</returns>
public delegate ValueTask ConsensusSendCallback(
    ReadOnlyMemory<byte> message,
    CancellationToken cancellationToken);
