// Copyright (c) marcschier. Licensed under the MIT License.

using Crdt.Tests.Testing;

namespace Crdt.Tests.Counters;

public sealed class BCounterTests
{
    private static readonly ReplicaId A = ReplicaId.FromUInt64(1);
    private static readonly ReplicaId B = ReplicaId.FromUInt64(2);
    private static readonly ReplicaId C = ReplicaId.FromUInt64(3);

    [Test]
    public async Task Value_Uses_Min_Increments_And_Decrements()
    {
        var counter = new BCounter(10);
        counter.Increment(A, 7);
        bool decremented = counter.TryDecrement(A, 3, out _);

        await Assert.That(decremented).IsTrue();
        await Assert.That(counter.Value).IsEqualTo(14L);
        await Assert.That(counter.LocalRights(A)).IsEqualTo(4UL);
    }

    [Test]
    public async Task Successful_Decrements_Do_Not_Drop_Below_Min_After_Concurrent_Merge()
    {
        var left = new BCounter(5);
        BCounterOperation inc = left.Increment(A, 10);
        bool transferred = left.TryTransfer(A, B, 4, out BCounterOperation transfer);

        var right = new BCounter(5);
        right.Apply(inc);
        right.Apply(transfer);

        bool leftDecremented = left.TryDecrement(A, 6, out _);
        bool rightDecremented = right.TryDecrement(B, 4, out _);

        left.Merge(right);
        right.Merge(left);

        await Assert.That(transferred).IsTrue();
        await Assert.That(leftDecremented).IsTrue();
        await Assert.That(rightDecremented).IsTrue();
        await Assert.That(left.Value >= left.Min).IsTrue();
        await Assert.That(right.Value >= right.Min).IsTrue();
        await Assert.That(left.Value).IsEqualTo(5L);
    }

    [Test]
    public async Task TryDecrement_Fails_Without_Sufficient_Local_Rights()
    {
        var counter = new BCounter();
        counter.Increment(A, 2);

        bool decremented = counter.TryDecrement(A, 3, out BCounterOperation operation);

        await Assert.That(decremented).IsFalse();
        await Assert.That(operation).IsEqualTo(default);
        await Assert.That(counter.Value).IsEqualTo(2L);
    }

    [Test]
    public async Task TryTransfer_Enables_Remote_Decrement()
    {
        var alice = new BCounter();
        BCounterOperation increment = alice.Increment(A, 8);

        var bob = new BCounter();
        bob.Apply(increment);

        bool transferred = alice.TryTransfer(A, B, 5, out BCounterOperation transfer);
        bob.Apply(transfer);
        bool decremented = bob.TryDecrement(B, 4, out _);

        await Assert.That(transferred).IsTrue();
        await Assert.That(decremented).IsTrue();
        await Assert.That(bob.LocalRights(B)).IsEqualTo(1UL);
        await Assert.That(bob.Value).IsEqualTo(4L);
    }

    [Test]
    public void Merge_Is_Idempotent_Commutative_And_Associative()
    {
        CrdtLaws.AssertSemilattice(Sample(A, B, 10, 3), Sample(B, C, 7, 2), Sample(C, A, 5, 1));
    }

    [Test]
    public async Task Apply_Is_Idempotent()
    {
        var source = new BCounter();
        BCounterOperation increment = source.Increment(A, 5);
        bool transferred = source.TryTransfer(A, B, 2, out BCounterOperation transfer);

        var target = new BCounter();
        bool firstIncrement = target.Apply(increment);
        bool secondIncrement = target.Apply(increment);
        bool firstTransfer = target.Apply(transfer);
        bool secondTransfer = target.Apply(transfer);

        await Assert.That(transferred).IsTrue();
        await Assert.That(firstIncrement).IsTrue();
        await Assert.That(secondIncrement).IsFalse();
        await Assert.That(firstTransfer).IsTrue();
        await Assert.That(secondTransfer).IsFalse();
        await Assert.That(target).IsEqualTo(source);
    }

    [Test]
    public async Task Binary_Roundtrips()
    {
        var counter = Sample(A, B, 12, 4);

        BCounter restored = BCounter.ReadFrom(counter.ToByteArray());

        await Assert.That(restored).IsEqualTo(counter);
        await Assert.That(restored.Value).IsEqualTo(counter.Value);
    }

    [Test]
    public async Task Json_Roundtrips()
    {
        var counter = Sample(A, B, 12, 4);

        BCounter restored = BCounter.FromJson(counter.ToJson());

        await Assert.That(restored).IsEqualTo(counter);
        await Assert.That(restored.Value).IsEqualTo(counter.Value);
    }

    private static BCounter Sample(ReplicaId owner, ReplicaId receiver, ulong increment, ulong decrement)
    {
        var counter = new BCounter();
        counter.Increment(owner, increment);
        bool transferred = counter.TryTransfer(owner, receiver, decrement, out _);
        bool decremented = counter.TryDecrement(receiver, decrement, out _);

        if (!transferred || !decremented)
        {
            throw new InvalidOperationException("The sample bounded counter could not be constructed.");
        }

        return counter;
    }
}
