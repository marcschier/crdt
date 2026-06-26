// Copyright (c) marcschier. Licensed under the MIT License.

using Crdt.Tests.Testing;

namespace Crdt.Tests.Registers;

public sealed class LWWRegisterTests
{
    [Test]
    public async Task Set_Stores_Value_And_Timestamp()
    {
        var register = new LWWRegister<string>();
        Timestamp timestamp = Ts(1, 0, 1);

        LWWRegisterOperation<string> operation = register.Set("alpha", timestamp);
        bool hasValue = register.TryGetValue(out string? value);

        await Assert.That(hasValue).IsTrue();
        await Assert.That(value).IsEqualTo("alpha");
        await Assert.That(register.Value).IsEqualTo("alpha");
        await Assert.That(register.Timestamp).IsEqualTo(timestamp);
        await Assert.That(operation.Timestamp).IsEqualTo(timestamp);
    }

    [Test]
    public async Task Set_Uses_Hybrid_Logical_Clock()
    {
        var time = new FakeTimeProvider();
        var clock = new HybridLogicalClock(ReplicaId.FromUInt64(1), time);
        var register = new LWWRegister<string>();

        register.Set("alpha", clock);
        time.Advance(TimeSpan.FromMilliseconds(1));
        register.Set("beta", clock);

        await Assert.That(register.Value).IsEqualTo("beta");
        await Assert.That(register.Timestamp.Origin).IsEqualTo(clock.Replica);
    }

    [Test]
    public async Task Merge_Keeps_Greatest_Timestamp()
    {
        var left = new LWWRegister<string>();
        left.Set("old", Ts(1, 0, 1));
        var right = new LWWRegister<string>();
        right.Set("new", Ts(2, 0, 1));

        left.Merge(right);
        right.Merge(Sample("ignored", Ts(1, 1, 1)));

        await Assert.That(left.Value).IsEqualTo("new");
        await Assert.That(right.Value).IsEqualTo("new");
    }

    [Test]
    public async Task Timestamp_Origin_Breaks_Ties()
    {
        ReplicaId low = ReplicaId.FromUInt64(1);
        ReplicaId high = ReplicaId.FromUInt64(2);
        var left = Sample("low", new Timestamp(10, 0, low));
        var right = Sample("high", new Timestamp(10, 0, high));

        left.Merge(right);

        await Assert.That(left.Value).IsEqualTo("high");
    }

    [Test]
    public async Task Reset_Updates_Value()
    {
        var register = new LWWRegister<int>();

        register.Set(1, Ts(1, 0, 1));
        register.Set(2, Ts(2, 0, 1));

        await Assert.That(register.Value).IsEqualTo(2);
    }

    [Test]
    public async Task Compare_Orders_Empty_And_Timestamps()
    {
        var empty = new LWWRegister<string>();
        var early = Sample("early", Ts(1, 0, 1));
        var late = Sample("late", Ts(2, 0, 1));

        await Assert.That(empty.Compare(early)).IsEqualTo(CrdtOrder.Less);
        await Assert.That(late.Compare(early)).IsEqualTo(CrdtOrder.Greater);
        await Assert.That(early.Compare(late)).IsEqualTo(CrdtOrder.Less);
        await Assert.That(early.Compare(early.Clone())).IsEqualTo(CrdtOrder.Equal);
    }

    [Test]
    public void Satisfies_Semilattice_Laws()
    {
        CrdtLaws.AssertSemilattice(
            Sample("a", Ts(1, 0, 1)),
            Sample("b", Ts(2, 0, 1)),
            Sample("c", Ts(3, 0, 1)));
    }

    [Test]
    public async Task Delta_Carries_Register_State()
    {
        var source = new LWWRegister<string>();
        source.Set("alpha", Ts(1, 0, 1));

        bool extracted = source.TryExtractDelta(out LWWRegister<string>? delta);
        var target = new LWWRegister<string>();
        target.MergeDelta(delta!);
        bool extractedAgain = source.TryExtractDelta(out _);

        await Assert.That(extracted).IsTrue();
        await Assert.That(target.Value).IsEqualTo("alpha");
        await Assert.That(extractedAgain).IsFalse();
    }

    [Test]
    public void Operations_Converge_Under_Reordering_And_Duplication()
    {
        var r0 = new LWWRegister<string>();
        var r1 = new LWWRegister<string>();
        var r2 = new LWWRegister<string>();
        var sim = new OperationDeliverySimulator<LWWRegister<string>, LWWRegisterOperation<string>>(r0, r1, r2);

        sim.Broadcast(0, r0.Set("a", Ts(1, 0, 1)));
        sim.Broadcast(1, r1.Set("b", Ts(3, 0, 1)));
        sim.Broadcast(2, r2.Set("c", Ts(2, 0, 1)));

        sim.DeliverAll(seed: 11, duplicate: true);

        sim.AssertConverged(static (x, y) => x.Equals(y));
    }

    [Test]
    public async Task Binary_Roundtrips()
    {
        var register = Sample("alpha", Ts(7, 2, 1));

        LWWRegister<string> restored =
            LWWRegister<string>.ReadFrom(register.ToByteArray(CrdtValues.String), CrdtValues.String);
        var operationSource = new LWWRegister<string>();
        LWWRegisterOperation<string> operation = operationSource.Set("beta", Ts(8, 0, 1));
        LWWRegisterOperation<string> restoredOperation =
            LWWRegisterOperation<string>.ReadFrom(operation.ToByteArray(CrdtValues.String), CrdtValues.String);

        await Assert.That(restored).IsEqualTo(register);
        await Assert.That(restoredOperation.Value).IsEqualTo("beta");
    }

    [Test]
    public async Task Json_Roundtrips()
    {
        var register = new LWWRegister<long>();
        register.Set(42, Ts(9, 1, 1));

        LWWRegister<long> restored = LWWRegister<long>.FromJson(register.ToJson(CrdtValues.Int64), CrdtValues.Int64);

        await Assert.That(restored).IsEqualTo(register);
    }

    private static LWWRegister<string> Sample(string value, Timestamp timestamp)
    {
        var register = new LWWRegister<string>();
        register.Set(value, timestamp);
        return register;
    }

    private static Timestamp Ts(long wallClock, ulong counter, ulong replica) =>
        new(wallClock, counter, ReplicaId.FromUInt64(replica));
}
