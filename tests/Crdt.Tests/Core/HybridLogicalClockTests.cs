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

    [Test]
    [Arguments(0)]
    [Arguments(10_000)]
    public async Task Witness_Carries_Full_Remote_Counter_Into_Wall_Clock(int wallOffset)
    {
        var clock = new HybridLogicalClock(Self, new FakeTimeProvider());
        Timestamp local = clock.Now();
        var remote = new Timestamp(local.WallClock + wallOffset, ulong.MaxValue, ReplicaId.FromUInt64(99));

        Timestamp witnessed = clock.Witness(remote);

        await Assert.That(witnessed).IsEqualTo(new Timestamp(remote.WallClock + 1, 0, Self));
        await Assert.That(witnessed > remote).IsTrue();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Full_Local_Counter_Carries_And_Roundtrips(bool witnessOlderTimestamp)
    {
        var clock = new HybridLogicalClock(Self, new FakeTimeProvider());
        var remote = new Timestamp(clock.Now().WallClock, ulong.MaxValue - 1, ReplicaId.FromUInt64(99));
        Timestamp before = clock.Witness(remote);
        Timestamp after = witnessOlderTimestamp ? clock.Witness(remote) : clock.Now();

        Timestamp decodedBefore;
        Timestamp decodedAfter;
        using (var buffer = new PooledBufferWriter())
        {
            var writer = new CrdtWriter(buffer);
            writer.WriteTimestamp(before);
            writer.WriteTimestamp(after);
            var reader = new CrdtReader(buffer.WrittenSpan);
            decodedBefore = reader.ReadTimestamp();
            decodedAfter = reader.ReadTimestamp();
        }

        await Assert.That(before).IsEqualTo(new Timestamp(remote.WallClock, ulong.MaxValue, Self));
        await Assert.That(after).IsEqualTo(new Timestamp(remote.WallClock + 1, 0, Self));
        await Assert.That(decodedBefore).IsEqualTo(before);
        await Assert.That(decodedAfter).IsEqualTo(after);
        await Assert.That(decodedAfter > decodedBefore).IsTrue();
    }

    [Test]
    public async Task Witness_Without_Representable_Successor_Leaves_Clock_Unchanged()
    {
        var clock = new HybridLogicalClock(Self, new FakeTimeProvider());
        Timestamp before = clock.Now();
        var remote = new Timestamp(long.MaxValue, ulong.MaxValue, ReplicaId.FromUInt64(99));

        await Assert.That(() => clock.Witness(remote)).Throws<OverflowException>();

        await Assert.That(clock.Now()).IsEqualTo(new Timestamp(before.WallClock, before.Counter + 1, Self));
    }
}
