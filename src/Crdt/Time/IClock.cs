// Copyright (c) marcschier. Licensed under the MIT License.

namespace Crdt;

/// <summary>
/// A logical clock that issues <see cref="Timestamp"/> values for last-writer-wins CRDTs.
/// Abstracting the clock lets LWW types accept any timestamp source and lets tests supply
/// deterministic or mocked clocks. <see cref="HybridLogicalClock"/> is the default
/// implementation.
/// </summary>
public interface IClock
{
    /// <summary>Gets the replica that owns this clock.</summary>
    ReplicaId Replica { get; }

    /// <summary>Produces the next timestamp for a local event (strictly monotonic).</summary>
    /// <returns>A fresh timestamp.</returns>
    Timestamp Now();

    /// <summary>Advances the clock past a remote timestamp and returns a dominating local one.</summary>
    /// <param name="remote">The observed remote timestamp.</param>
    /// <returns>A fresh timestamp dominating <paramref name="remote"/>.</returns>
    Timestamp Witness(Timestamp remote);
}
