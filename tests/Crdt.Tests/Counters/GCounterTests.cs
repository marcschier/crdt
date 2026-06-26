// Copyright (c) marcschier. Licensed under the MIT License.

using Crdt.Tests.Testing;

namespace Crdt.Tests.Counters;

public sealed class GCounterTests
{
    private static readonly ReplicaId A = ReplicaId.FromUInt64(1);
    private static readonly ReplicaId B = ReplicaId.FromUInt64(2);
    private static readonly ReplicaId C = ReplicaId.FromUInt64(3);

    [Test]
    public async Task Increment_Increases_Value()
    {
        var counter = new GCounter();
        counter.Increment(A, 5);
        counter.Increment(A, 2);
        counter.Increment(B);

        await Assert.That(counter.Value).IsEqualTo(8UL);
        await Assert.That(counter[A]).IsEqualTo(7UL);
        await Assert.That(counter[B]).IsEqualTo(1UL);
    }

    [Test]
    public async Task Increment_By_Zero_Throws()
    {
        var counter = new GCounter();
        await Assert.That(() => counter.Increment(A, 0)).Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task Merge_Takes_Per_Replica_Maximum()
    {
        var left = new GCounter();
        left.Increment(A, 5);
        left.Increment(B, 1);

        var right = new GCounter();
        right.Increment(A, 2);
        right.Increment(B, 4);

        left.Merge(right);

        await Assert.That(left[A]).IsEqualTo(5UL);
        await Assert.That(left[B]).IsEqualTo(4UL);
        await Assert.That(left.Value).IsEqualTo(9UL);
    }

    [Test]
    public void Satisfies_Semilattice_Laws()
    {
        CrdtLaws.AssertSemilattice(Sample(A, 3, B, 1), Sample(B, 5, C, 2), Sample(A, 1, C, 9));
    }

    [Test]
    public async Task Delta_Merge_Matches_State_Merge()
    {
        var source = new GCounter();
        source.Increment(A, 5);
        source.Increment(A, 3);
        source.Increment(B, 2);

        bool extracted = source.TryExtractDelta(out GCounter? delta);
        var target = new GCounter();
        target.MergeDelta(delta!);

        await Assert.That(extracted).IsTrue();
        await Assert.That(target.Value).IsEqualTo(source.Value);
        await Assert.That(target[A]).IsEqualTo(8UL);
    }

    [Test]
    public async Task Delta_Is_Empty_When_No_Local_Changes()
    {
        var counter = new GCounter();
        bool extracted = counter.TryExtractDelta(out _);
        await Assert.That(extracted).IsFalse();
    }

    [Test]
    public void Operations_Converge_Under_Reordering_And_Duplication()
    {
        var r0 = new GCounter();
        var r1 = new GCounter();
        var r2 = new GCounter();
        var sim = new OperationDeliverySimulator<GCounter, GCounterOperation>(r0, r1, r2);

        sim.Broadcast(0, r0.Increment(A, 5));
        sim.Broadcast(1, r1.Increment(B, 3));
        sim.Broadcast(0, r0.Increment(A, 2));

        sim.DeliverAll(seed: 42, duplicate: true);

        sim.AssertConverged(static (x, y) => x.Equals(y));
    }

    [Test]
    public async Task Binary_Roundtrips()
    {
        var counter = new GCounter();
        counter.Increment(A, 7);
        counter.Increment(B, 4);

        GCounter restored = GCounter.ReadFrom(counter.ToByteArray());
        await Assert.That(restored).IsEqualTo(counter);
    }

    [Test]
    public async Task Json_Roundtrips()
    {
        var counter = new GCounter();
        counter.Increment(A, 7);
        counter.Increment(B, 4);

        GCounter restored = GCounter.FromJson(counter.ToJson());
        await Assert.That(restored).IsEqualTo(counter);
    }

    [Test]
    public async Task Compare_Reflects_Dominance()
    {
        var baseline = new GCounter();
        baseline.Increment(A, 1);

        var greater = baseline.Clone();
        greater.Increment(A, 1);

        var concurrent = new GCounter();
        concurrent.Increment(B, 1);

        await Assert.That(baseline.Compare(greater)).IsEqualTo(CrdtOrder.Less);
        await Assert.That(greater.Compare(baseline)).IsEqualTo(CrdtOrder.Greater);
        await Assert.That(baseline.Compare(concurrent)).IsEqualTo(CrdtOrder.Concurrent);
        await Assert.That(baseline.Compare(baseline.Clone())).IsEqualTo(CrdtOrder.Equal);
    }

    private static GCounter Sample(ReplicaId r1, ulong v1, ReplicaId r2, ulong v2)
    {
        var counter = new GCounter();
        counter.Increment(r1, v1);
        counter.Increment(r2, v2);
        return counter;
    }
}
