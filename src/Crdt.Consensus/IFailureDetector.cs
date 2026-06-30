// Copyright (c) marcschier. Licensed under the MIT License.

namespace Crdt.Consensus;

/// <summary>Tracks the live members visible to a local replica.</summary>
public interface IFailureDetector : IAsyncDisposable
{
    /// <summary>Raised when the live membership view changes.</summary>
    event Action? MembersChanged;

    /// <summary>Gets the local replica id.</summary>
    ReplicaId LocalReplicaId { get; }

    /// <summary>Gets the current live membership view.</summary>
    IReadOnlyCollection<ReplicaId> Members { get; }

    /// <summary>Starts the failure detector.</summary>
    /// <param name="ct">A token that cancels the start operation.</param>
    /// <returns>A task-like value that completes when the detector has started.</returns>
    ValueTask StartAsync(CancellationToken ct = default);

    /// <summary>Adds or refreshes a live member in the membership view.</summary>
    /// <param name="replicaId">The member replica id.</param>
    void AddMember(ReplicaId replicaId);

    /// <summary>Removes a member from the membership view.</summary>
    /// <param name="replicaId">The member replica id.</param>
    /// <returns><see langword="true"/> when the member was present; otherwise <see langword="false"/>.</returns>
    bool RemoveMember(ReplicaId replicaId);

    /// <summary>Observes a heartbeat from a member and marks it live.</summary>
    /// <param name="replicaId">The heartbeat sender.</param>
    void ObserveHeartbeat(ReplicaId replicaId);
}
