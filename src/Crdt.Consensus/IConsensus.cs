// Copyright (c) marcschier. Licensed under the MIT License.

namespace Crdt.Consensus;

/// <summary>Coordinates ordered, committed entries for a replicated component.</summary>
public interface IConsensus : IAsyncDisposable
{
    /// <summary>Raised when the elected leader changes.</summary>
    event Action<ReplicaId?>? LeadershipChanged;

    /// <summary>Raised when a committed entry is delivered locally.</summary>
    event Action<ReadOnlyMemory<byte>>? EntryCommitted;

    /// <summary>Raised when the current membership view changes.</summary>
    event Action? MembersChanged;

    /// <summary>Gets the local replica's current consensus role.</summary>
    ConsensusRole Role { get; }

    /// <summary>Gets the current leader id, or <see langword="null"/> when no leader is known.</summary>
    ReplicaId? LeaderId { get; }

    /// <summary>Gets a value indicating whether the local replica is the current leader.</summary>
    bool IsLeader { get; }

    /// <summary>Gets the current live membership view.</summary>
    IReadOnlyCollection<ReplicaId> Members { get; }

    /// <summary>Starts the consensus component.</summary>
    /// <param name="ct">A token that cancels the start operation.</param>
    /// <returns>A task-like value that completes when the component has started.</returns>
    ValueTask StartAsync(CancellationToken ct = default);

    /// <summary>Proposes an entry and completes when it is committed by the live membership view.</summary>
    /// <param name="entry">The entry payload to commit.</param>
    /// <param name="ct">A token that cancels the proposal wait.</param>
    /// <returns>
    /// <see langword="true"/> when the entry commits; otherwise <see langword="false"/> when the local
    /// replica is not leader or the component shuts down before commitment.
    /// </returns>
    ValueTask<bool> ProposeAsync(ReadOnlyMemory<byte> entry, CancellationToken ct = default);
}
