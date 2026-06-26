// Copyright (c) marcschier. Licensed under the MIT License.

namespace Crdt.Tests.Core;

public sealed class HybridLogicalClockTests
{
    private static readonly ReplicaId Self = ReplicaId.FromUInt64(1);

    [Test]
    public async Task Now_Is_Strictly_Monotonic_At_Same_Physical_Time()
    {
        var time = new FakeTimeProvider();
        var clock = new HybridLogicalClock(Self, time);

        Timestamp t1 = clock.Now();
        Timestamp t2 = clock.Now();

        await Assert.That(t2 > t1).IsTrue();
        await Assert.That(t1.WallClock).IsEqualTo(t2.WallClock);
        await Assert.That(t2.Counter).IsEqualTo(1UL);
    }

    [Test]
    public async Task Now_Resets_Counter_When_Physical_Time_Advances()
    {
        var time = new FakeTimeProvider();
        var clock = new HybridLogicalClock(Self, time);

        clock.Now();
        clock.Now();
        time.Advance(TimeSpan.FromSeconds(1));
        Timestamp t = clock.Now();

        await Assert.That(t.Counter).IsEqualTo(0UL);
    }

    [Test]
    public async Task Witness_Advances_Past_A_Future_Remote_Timestamp()
    {
        var time = new FakeTimeProvider();
        var clock = new HybridLogicalClock(Self, time);
        Timestamp local = clock.Now();

        var remote = new Timestamp(local.WallClock + 10_000, 5, ReplicaId.FromUInt64(99));
        Timestamp witnessed = clock.Witness(remote);

        await Assert.That(witnessed > remote).IsTrue();
        await Assert.That(witnessed.WallClock).IsEqualTo(remote.WallClock);
        await Assert.That(witnessed.Counter).IsEqualTo(6UL);
        await Assert.That(witnessed.Origin).IsEqualTo(Self);
    }

    [Test]
    public async Task Now_After_Witness_Stays_Monotonic()
    {
        var time = new FakeTimeProvider();
        var clock = new HybridLogicalClock(Self, time);

        var remote = new Timestamp(clock.Now().WallClock + 5_000, 2, ReplicaId.FromUInt64(50));
        Timestamp witnessed = clock.Witness(remote);
        Timestamp next = clock.Now();

        await Assert.That(next > witnessed).IsTrue();
    }
}
