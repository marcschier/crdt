// Copyright (c) marcschier. Licensed under the MIT License.

namespace Crdt;

/// <summary>
/// A Hybrid Logical Clock (HLC): generates <see cref="Timestamp"/> values that are
/// monotonically increasing locally, stay close to physical (wall-clock) time, and advance
/// when timestamps are received from other replicas. HLC timestamps give last-writer-wins
/// CRDTs a sensible, causally-consistent total order without requiring synchronized clocks.
/// </summary>
/// <remarks>
/// This clock is thread-safe: <see cref="Now"/> and <see cref="Witness"/> may be called
/// concurrently. The physical time source is an injected <see cref="TimeProvider"/>
/// (defaulting to <see cref="TimeProvider.System"/>), which makes the clock fully
/// deterministic in tests.
/// </remarks>
public sealed class HybridLogicalClock : IClock
{
    private readonly TimeProvider _timeProvider;
    private readonly object _gate = new();
    private long _wallClock;
    private ulong _counter;

    /// <summary>Initializes a new <see cref="HybridLogicalClock"/>.</summary>
    /// <param name="replica">The replica that owns this clock; stamped as the timestamp origin.</param>
    /// <param name="timeProvider">The physical time source; defaults to <see cref="TimeProvider.System"/>.</param>
    public HybridLogicalClock(ReplicaId replica, TimeProvider? timeProvider = null)
    {
        Replica = replica;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Gets the replica that owns this clock.</summary>
    public ReplicaId Replica { get; }

    private long PhysicalNow() => _timeProvider.GetUtcNow().ToUnixTimeMilliseconds();

    /// <summary>
    /// Produces the next timestamp for a local event, guaranteed strictly greater than any
    /// timestamp this clock has previously produced or witnessed.
    /// </summary>
    /// <returns>A fresh, monotonic timestamp.</returns>
    public Timestamp Now()
    {
        lock (_gate)
        {
            long physical = PhysicalNow();
            if (physical > _wallClock)
            {
                _wallClock = physical;
                _counter = 0UL;
            }
            else
            {
                _counter++;
            }

            return new Timestamp(_wallClock, _counter, Replica);
        }
    }

    /// <summary>
    /// Advances this clock to reflect a timestamp received from another replica, then returns
    /// a fresh local timestamp that strictly dominates both the local and remote clocks
    /// (per the HLC receive rule).
    /// </summary>
    /// <param name="remote">The timestamp observed from another replica.</param>
    /// <returns>A fresh, monotonic timestamp dominating <paramref name="remote"/>.</returns>
    public Timestamp Witness(Timestamp remote)
    {
        lock (_gate)
        {
            long physical = PhysicalNow();
            long previousWall = _wallClock;
            long newWall = Max(previousWall, remote.WallClock, physical);

            if (newWall == previousWall && newWall == remote.WallClock)
            {
                _counter = Max(_counter, remote.Counter) + 1UL;
            }
            else if (newWall == previousWall)
            {
                _counter++;
            }
            else if (newWall == remote.WallClock)
            {
                _counter = remote.Counter + 1UL;
            }
            else
            {
                _counter = 0UL;
            }

            _wallClock = newWall;
            return new Timestamp(_wallClock, _counter, Replica);
        }
    }

    private static long Max(long a, long b, long c) => Math.Max(a, Math.Max(b, c));

    private static ulong Max(ulong a, ulong b) => a > b ? a : b;
}
