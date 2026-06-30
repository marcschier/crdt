// Copyright (c) marcschier. Licensed under the MIT License.

namespace Crdt;

/// <summary>Exposes conservative garbage collection for causally stable CRDT metadata.</summary>
/// <remarks>
/// Implementations must preserve the visible value and convergence semantics. Collection may
/// reclaim less than the supplied <see cref="StableCut"/> permits when metadata is still needed
/// as an ordering anchor or to guard against not-yet-integrated operations.
/// </remarks>
public interface IGarbageCollectable
{
    /// <summary>Gets a snapshot of the causal frontier observed by this value.</summary>
    VersionVector ObservedVersion { get; }

    /// <summary>Reclaims metadata that is stable at or below <paramref name="cut"/>.</summary>
    /// <param name="cut">The stable causal cut computed for the live replicas.</param>
    void CollectStable(StableCut cut);
}
