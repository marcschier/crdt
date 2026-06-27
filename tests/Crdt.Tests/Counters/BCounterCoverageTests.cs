// Copyright (c) marcschier. Licensed under the MIT License.

namespace Crdt.Tests.Counters;

public sealed class BCounterCoverageTests
{
    private static readonly ReplicaId A = ReplicaId.FromUInt64(101);
    private static readonly ReplicaId B = ReplicaId.FromUInt64(102);
    private static readonly ReplicaId C = ReplicaId.FromUInt64(103);
    private static readonly ReplicaId D = ReplicaId.FromUInt64(104);

    [Test]
    public async Task Operation_Binary_Roundtrips_Each_Kind()
    {
        BCounterOperation increment = new(BCounterOperationKind.Increment, A, 7);
        BCounterOperation decrement = new(BCounterOperationKind.Decrement, B, 3);
        BCounterOperation transfer = new(A, C, 5);

        await Assert.That(BCounterOperation.ReadFrom(increment.ToByteArray())).IsEqualTo(increment);
        await Assert.That(BCounterOperation.ReadFrom(decrement.ToByteArray())).IsEqualTo(decrement);
        await Assert.That(BCounterOperation.ReadFrom(transfer.ToByteArray())).IsEqualTo(transfer);
    }

    [Test]
    public async Task Operation_Equality_And_HashCode_Use_All_Fields()
    {
        BCounterOperation left = new(A, B, 9);
        BCounterOperation same = new(A, B, 9);
        BCounterOperation differentTo = new(A, C, 9);
        BCounterOperation differentValue = new(A, B, 10);
        BCounterOperation defaultLeft = default;
        BCounterOperation defaultRight = default;

        await Assert.That(left.Equals(same)).IsTrue();
        await Assert.That(left == same).IsTrue();
        await Assert.That(left != differentTo).IsTrue();
        await Assert.That(left.Equals(differentTo)).IsFalse();
        await Assert.That(left.Equals(differentValue)).IsFalse();
        await Assert.That(left.Equals("not an operation")).IsFalse();
        await Assert.That(left.GetHashCode()).IsEqualTo(same.GetHashCode());
        await Assert.That(defaultLeft).IsEqualTo(defaultRight);
        await Assert.That(defaultLeft.GetHashCode()).IsEqualTo(defaultRight.GetHashCode());
    }

    [Test]
    public async Task Operation_Read_Rejects_Unknown_Kind()
    {
        await Assert.That(() => BCounterOperation.ReadFrom(new byte[] { 255 })).Throws<FormatException>();
    }

    [Test]
    public async Task Compare_Returns_All_Lattice_Orders()
    {
        var baseline = new BCounter();
        baseline.Increment(A, 5);

        BCounter greater = baseline.Clone();
        greater.Increment(A, 1);

        var concurrent = new BCounter();
        concurrent.Increment(B, 4);

        await Assert.That(baseline.Compare(baseline.Clone())).IsEqualTo(CrdtOrder.Equal);
        await Assert.That(baseline.Compare(greater)).IsEqualTo(CrdtOrder.Less);
        await Assert.That(greater.Compare(baseline)).IsEqualTo(CrdtOrder.Greater);
        await Assert.That(baseline.Compare(concurrent)).IsEqualTo(CrdtOrder.Concurrent);
    }

    [Test]
    public async Task Clone_Is_Independent()
    {
        var original = new BCounter(2);
        original.Increment(A, 8);
        bool transferred = original.TryTransfer(A, B, 3, out _);

        BCounter clone = original.Clone();
        clone.Increment(A, 4);
        bool cloneDecremented = clone.TryDecrement(B, 2, out _);

        await Assert.That(transferred).IsTrue();
        await Assert.That(cloneDecremented).IsTrue();
        await Assert.That(original.Value).IsEqualTo(10L);
        await Assert.That(clone.Value).IsEqualTo(12L);
        await Assert.That(original.TransferOf(A, B)).IsEqualTo(3UL);
        await Assert.That(original.DecrementOf(B)).IsEqualTo(0UL);
    }

    [Test]
    public async Task TryTransfer_Failure_And_Exact_Exhaustion_Preserve_State()
    {
        var counter = new BCounter();
        counter.Increment(A, 5);

        bool failed = counter.TryTransfer(A, B, 6, out BCounterOperation failedOperation);
        bool exhausted = counter.TryTransfer(A, B, 5, out BCounterOperation transferOperation);
        bool failedAfterExhaustion = counter.TryTransfer(A, C, 1, out BCounterOperation secondFailedOperation);

        await Assert.That(failed).IsFalse();
        await Assert.That(failedOperation).IsEqualTo(default);
        await Assert.That(exhausted).IsTrue();
        await Assert.That(transferOperation).IsEqualTo(new BCounterOperation(A, B, 5));
        await Assert.That(failedAfterExhaustion).IsFalse();
        await Assert.That(secondFailedOperation).IsEqualTo(default);
        await Assert.That(counter.LocalRights(A)).IsEqualTo(0UL);
        await Assert.That(counter.TransferOf(A, B)).IsEqualTo(5UL);
        await Assert.That(counter.TransferOf(A, C)).IsEqualTo(0UL);
        await Assert.That(counter.Value).IsEqualTo(5L);
    }

    [Test]
    public async Task Merge_And_Compare_Require_Equal_Min()
    {
        var left = new BCounter(0);
        var right = new BCounter(1);

        await Assert.That(() => left.Merge(right)).Throws<InvalidOperationException>();
        await Assert.That(() => left.Compare(right)).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task LocalRights_And_Accessors_Track_MultiHop_Transfers()
    {
        var counter = new BCounter();
        counter.Increment(A, 10);
        bool toB = counter.TryTransfer(A, B, 7, out _);
        bool toC = counter.TryTransfer(B, C, 4, out _);
        bool cDecremented = counter.TryDecrement(C, 2, out _);

        await Assert.That(toB).IsTrue();
        await Assert.That(toC).IsTrue();
        await Assert.That(cDecremented).IsTrue();
        await Assert.That(counter.IncrementOf(A)).IsEqualTo(10UL);
        await Assert.That(counter.DecrementOf(C)).IsEqualTo(2UL);
        await Assert.That(counter.TransferOf(A, B)).IsEqualTo(7UL);
        await Assert.That(counter.TransferOf(B, C)).IsEqualTo(4UL);
        await Assert.That(counter.TransferOf(A, C)).IsEqualTo(0UL);
        await Assert.That(counter.LocalRights(A)).IsEqualTo(3UL);
        await Assert.That(counter.LocalRights(B)).IsEqualTo(3UL);
        await Assert.That(counter.LocalRights(C)).IsEqualTo(2UL);
        await Assert.That(counter.LocalRights(D)).IsEqualTo(0UL);
    }

    [Test]
    public async Task State_Binary_And_Json_Roundtrip_Multiple_Transfer_Pairs()
    {
        BCounter counter = MultiTransferCounter();

        BCounter binary = BCounter.ReadFrom(counter.ToByteArray());
        BCounter json = BCounter.FromJson(counter.ToJson());

        await Assert.That(binary).IsEqualTo(counter);
        await Assert.That(json).IsEqualTo(counter);
        await Assert.That(binary.TransferOf(A, B)).IsEqualTo(4UL);
        await Assert.That(json.TransferOf(A, C)).IsEqualTo(3UL);
    }

    [Test]
    public async Task Counter_Equals_And_HashCode_Cover_Equal_And_Unequal_Cases()
    {
        BCounter left = MultiTransferCounter();
        BCounter same = BCounter.FromJson(left.ToJson());
        BCounter differentMin = new(1);
        BCounter differentIncrement = left.Clone();
        BCounter differentTransfer = left.Clone();
        BCounter? nullCounter = null;
        object otherType = "not a counter";
        differentIncrement.Increment(A, 1);
        bool transferred = differentTransfer.TryTransfer(B, C, 1, out _);

        await Assert.That(transferred).IsTrue();
        await Assert.That(left.Equals(same)).IsTrue();
        await Assert.That(left.GetHashCode()).IsEqualTo(same.GetHashCode());
        await Assert.That(left.Equals(differentMin)).IsFalse();
        await Assert.That(left.Equals(differentIncrement)).IsFalse();
        await Assert.That(left.Equals(differentTransfer)).IsFalse();
        await Assert.That(left.Equals(nullCounter)).IsFalse();
        await Assert.That(EqualsObject(left, otherType)).IsFalse();
    }

    [Test]
    public async Task Throw_Guards_Reject_Invalid_Arguments()
    {
        var counter = new BCounter();

        await Assert.That(() => counter.Increment(A, 0)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => counter.TryDecrement(A, 0, out _)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => counter.TryTransfer(A, B, 0, out _)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => counter.Merge(null!)).Throws<ArgumentNullException>();
        await Assert.That(() => counter.Compare(null!)).Throws<ArgumentNullException>();
        await Assert.That(() => BCounter.FromJson(null!)).Throws<ArgumentNullException>();
    }

    [Test]
    public async Task Apply_Default_Operation_Is_Idempotent()
    {
        var counter = new BCounter();

        bool first = counter.Apply(default);
        bool second = counter.Apply(default);

        await Assert.That(first).IsTrue();
        await Assert.That(second).IsFalse();
        await Assert.That(counter.Value).IsEqualTo(0L);
    }

    private static BCounter MultiTransferCounter()
    {
        var counter = new BCounter(3);
        counter.Increment(A, 12);
        bool ab = counter.TryTransfer(A, B, 4, out _);
        bool ac = counter.TryTransfer(A, C, 3, out _);
        bool bd = counter.TryTransfer(B, D, 2, out _);
        bool cd = counter.TryTransfer(C, D, 1, out _);
        bool dDecremented = counter.TryDecrement(D, 2, out _);

        if (!ab || !ac || !bd || !cd || !dDecremented)
        {
            throw new InvalidOperationException("The multi-transfer bounded counter could not be constructed.");
        }

        return counter;
    }

    private static bool EqualsObject(BCounter counter, object value) => counter.Equals(value);
}
