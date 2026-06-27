// Copyright (c) marcschier. Licensed under the MIT License.

using Crdt.Tests.Testing;

namespace Crdt.Tests.Counters;

public sealed class ResettableCounterTests
{
    private static readonly ReplicaId A = ReplicaId.FromUInt64(1);
    private static readonly ReplicaId B = ReplicaId.FromUInt64(2);
    private static readonly ReplicaId C = ReplicaId.FromUInt64(3);

    [Test]
    public async Task Reset_Removes_Observed_Increments()
    {
        var counter = new ResettableCounter();
        counter.Increment(A, 5);
        counter.Decrement(B, 2);

        counter.Reset();

        await Assert.That(counter.Value).IsEqualTo(0L);
    }

    [Test]
    public async Task Concurrent_Increment_Survives_Reset_After_Merge()
    {
        var left = new ResettableCounter();
        left.Increment(A, 5);

        ResettableCounter right = left.Clone();
        ResettableCounterOperation reset = left.Reset();
        ResettableCounterOperation concurrent = right.Increment(B, 3);

        left.Apply(concurrent);
        right.Apply(reset);
        left.Merge(right);
        right.Merge(left);

        await Assert.That(left.Value).IsEqualTo(3L);
        await Assert.That(right.Value).IsEqualTo(3L);
        await Assert.That(left).IsEqualTo(right);
    }

    [Test]
    public async Task Increment_After_Reset_Accumulates_From_Zero()
    {
        var counter = new ResettableCounter();
        counter.Increment(A, 8);
        counter.Reset();
        counter.Increment(A, 2);
        counter.Increment(B, 4);

        await Assert.That(counter.Value).IsEqualTo(6L);
    }

    [Test]
    public async Task Decrement_Subtracts_From_Value()
    {
        var counter = new ResettableCounter();
        counter.Increment(A, 5);
        counter.Decrement(B, 7);

        await Assert.That(counter.Value).IsEqualTo(-2L);
    }

    [Test]
    public async Task Merge_And_Apply_Converge_Under_Reordering_And_Duplication()
    {
        var r0 = new ResettableCounter();
        var r1 = new ResettableCounter();
        var r2 = new ResettableCounter();
        var sim = new OperationDeliverySimulator<ResettableCounter, ResettableCounterOperation>(r0, r1, r2);

        ResettableCounterOperation a1 = r0.Increment(A, 5);
        sim.Broadcast(0, a1);
        r1.Apply(a1);
        ResettableCounterOperation reset = r1.Reset();
        sim.Broadcast(1, reset);
        sim.Broadcast(2, r2.Increment(C, 4));
        sim.Broadcast(0, r0.Decrement(A, 2));

        sim.DeliverAll(seed: 19, duplicate: true);
        r0.Merge(r1);
        r1.Merge(r2);
        r2.Merge(r0);
        r0.Merge(r2);
        r1.Merge(r0);
        r2.Merge(r1);

        await Assert.That(r0.Value).IsEqualTo(2L);
        sim.AssertConverged(static (x, y) => x.Equals(y));
    }

    [Test]
    public async Task Merge_And_Apply_Are_Idempotent()
    {
        var source = new ResettableCounter();
        ResettableCounterOperation increment = source.Increment(A, 6);
        ResettableCounterOperation reset = source.Reset();
        ResettableCounterOperation afterReset = source.Increment(A, 2);

        var target = new ResettableCounter();
        await Assert.That(target.Apply(increment)).IsTrue();
        await Assert.That(target.Apply(increment)).IsFalse();
        await Assert.That(target.Apply(reset)).IsTrue();
        await Assert.That(target.Apply(reset)).IsFalse();
        await Assert.That(target.Apply(afterReset)).IsTrue();
        await Assert.That(target.Apply(afterReset)).IsFalse();

        target.Merge(source);
        target.Merge(source);

        await Assert.That(target).IsEqualTo(source);
        await Assert.That(target.Value).IsEqualTo(2L);
    }

    [Test]
    public async Task Binary_Roundtrips()
    {
        var counter = new ResettableCounter();
        counter.Increment(A, 9);
        counter.Decrement(B, 4);
        counter.Reset();
        counter.Increment(C, 7);

        ResettableCounter restored = ResettableCounter.ReadFrom(counter.ToByteArray());

        await Assert.That(restored).IsEqualTo(counter);
        await Assert.That(restored.Value).IsEqualTo(counter.Value);
    }

    [Test]
    public async Task Json_Roundtrips()
    {
        var counter = new ResettableCounter();
        counter.Increment(A, 9);
        counter.Decrement(B, 4);
        counter.Reset();
        counter.Increment(C, 7);

        ResettableCounter restored = ResettableCounter.FromJson(counter.ToJson());

        await Assert.That(restored).IsEqualTo(counter);
        await Assert.That(restored.Value).IsEqualTo(counter.Value);
    }
}
