// Copyright (c) marcschier. Licensed under the MIT License.

using Crdt.Tests.Testing;

namespace Crdt.Tests.Counters;

public sealed class PNCounterTests
{
    private static readonly ReplicaId A = ReplicaId.FromUInt64(1);
    private static readonly ReplicaId B = ReplicaId.FromUInt64(2);
    private static readonly ReplicaId C = ReplicaId.FromUInt64(3);

    [Test]
    public async Task Increment_And_Decrement_Net_Out()
    {
        var counter = new PNCounter();
        counter.Increment(A, 10);
        counter.Decrement(A, 3);
        counter.Decrement(B, 2);

        await Assert.That(counter.Value).IsEqualTo(5L);
    }

    [Test]
    public async Task Value_Can_Go_Negative()
    {
        var counter = new PNCounter();
        counter.Decrement(A, 7);
        await Assert.That(counter.Value).IsEqualTo(-7L);
    }

    [Test]
    public async Task Merge_Converges()
    {
        var left = new PNCounter();
        left.Increment(A, 5);
        left.Decrement(B, 1);

        var right = new PNCounter();
        right.Increment(A, 2);
        right.Decrement(B, 4);

        left.Merge(right);

        // P[A] = max(5,2) = 5; N[B] = max(1,4) = 4 -> 5 - 4 = 1
        await Assert.That(left.Value).IsEqualTo(1L);
    }

    [Test]
    public void Satisfies_Semilattice_Laws()
    {
        CrdtLaws.AssertSemilattice(Sample(A, 5, 1), Sample(B, 2, 7), Sample(C, 9, 3));
    }

    [Test]
    public async Task Delta_Merge_Matches_State_Merge()
    {
        var source = new PNCounter();
        source.Increment(A, 5);
        source.Decrement(A, 2);

        bool extracted = source.TryExtractDelta(out PNCounter? delta);
        var target = new PNCounter();
        target.MergeDelta(delta!);

        await Assert.That(extracted).IsTrue();
        await Assert.That(target.Value).IsEqualTo(source.Value);
    }

    [Test]
    public void Operations_Converge_Under_Reordering_And_Duplication()
    {
        var r0 = new PNCounter();
        var r1 = new PNCounter();
        var r2 = new PNCounter();
        var sim = new OperationDeliverySimulator<PNCounter, PNCounterOperation>(r0, r1, r2);

        sim.Broadcast(0, r0.Increment(A, 5));
        sim.Broadcast(1, r1.Decrement(B, 3));
        sim.Broadcast(0, r0.Decrement(A, 2));

        sim.DeliverAll(seed: 7, duplicate: true);

        sim.AssertConverged(static (x, y) => x.Equals(y));
    }

    [Test]
    public async Task Binary_Roundtrips()
    {
        var counter = new PNCounter();
        counter.Increment(A, 9);
        counter.Decrement(B, 4);

        PNCounter restored = PNCounter.ReadFrom(counter.ToByteArray());
        await Assert.That(restored).IsEqualTo(counter);
        await Assert.That(restored.Value).IsEqualTo(counter.Value);
    }

    [Test]
    public async Task Json_Roundtrips()
    {
        var counter = new PNCounter();
        counter.Increment(A, 9);
        counter.Decrement(B, 4);

        PNCounter restored = PNCounter.FromJson(counter.ToJson());
        await Assert.That(restored).IsEqualTo(counter);
    }

    private static PNCounter Sample(ReplicaId replica, ulong increment, ulong decrement)
    {
        var counter = new PNCounter();
        counter.Increment(replica, increment);
        counter.Decrement(replica, decrement);
        return counter;
    }
}
