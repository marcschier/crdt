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
/// When the counter is full, the clock carries into the wall-clock component by one
/// millisecond and resets the counter to zero.
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
    /// <exception cref="OverflowException">There is no representable successor to the current reading.</exception>
    public Timestamp Now()
    {
        lock (_gate)
        {
            long physical = PhysicalNow();
            return physical > _wallClock ? Update(physical, 0) : Increment(_wallClock, _counter);
        }
    }

    /// <summary>
    /// Advances this clock to reflect a timestamp received from another replica, then returns
    /// a fresh local timestamp that strictly dominates both the local and remote clocks
    /// (per the HLC receive rule).
    /// </summary>
    /// <param name="remote">The timestamp observed from another replica.</param>
    /// <returns>A fresh, monotonic timestamp dominating <paramref name="remote"/>.</returns>
    /// <exception cref="OverflowException">
    /// There is no representable wall-clock and counter pair greater than the current or received reading.
    /// The clock is unchanged.
    /// </exception>
    public Timestamp Witness(Timestamp remote)
    {
        lock (_gate)
        {
            long wallClock = Math.Max(_wallClock, Math.Max(remote.WallClock, PhysicalNow()));
            if (wallClock == _wallClock)
            {
                ulong counter = wallClock == remote.WallClock ? Math.Max(_counter, remote.Counter) : _counter;
                return Increment(wallClock, counter);
            }

            return wallClock == remote.WallClock ? Increment(wallClock, remote.Counter) : Update(wallClock, 0);
        }
    }

    private Timestamp Increment(long wallClock, ulong counter)
    {
        // Carry into the wall-clock component to preserve lexicographic timestamp order.
        return counter == ulong.MaxValue ? Update(checked(wallClock + 1), 0) : Update(wallClock, counter + 1);
    }

    private Timestamp Update(long wallClock, ulong counter)
    {
        _wallClock = wallClock;
        _counter = counter;
        return new Timestamp(wallClock, counter, Replica);
    }
}
