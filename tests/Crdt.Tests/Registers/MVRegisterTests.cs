// Copyright (c) marcschier. Licensed under the MIT License.

using Crdt.Tests.Testing;

namespace Crdt.Tests.Registers;

public sealed class MVRegisterTests
{
    [Test]
    public async Task Assign_Stores_One_Value()
    {
        var register = new MVRegister<string>();

        register.Assign(ReplicaId.FromUInt64(1), "alpha");

        await Assert.That(register.Count).IsEqualTo(1);
        await Assert.That(HasValue(register, "alpha")).IsTrue();
    }

    [Test]
    public async Task Concurrent_Assigns_From_Different_Replicas_Both_Survive()
    {
        var left = new MVRegister<string>();
        var right = new MVRegister<string>();

        left.Assign(ReplicaId.FromUInt64(1), "left");
        right.Assign(ReplicaId.FromUInt64(2), "right");
        left.Merge(right);

        await Assert.That(left.Count).IsEqualTo(2);
        await Assert.That(HasValue(left, "left")).IsTrue();
        await Assert.That(HasValue(left, "right")).IsTrue();
    }

    [Test]
    public async Task Causal_Assign_Collapses_Observed_Values()
    {
        var left = new MVRegister<string>();
        var right = new MVRegister<string>();
        left.Assign(ReplicaId.FromUInt64(1), "left");
        right.Assign(ReplicaId.FromUInt64(2), "right");
        left.Merge(right);

        left.Assign(ReplicaId.FromUInt64(1), "winner");
        right.Merge(left);

        await Assert.That(right.Count).IsEqualTo(1);
        await Assert.That(HasValue(right, "winner")).IsTrue();
    }

    [Test]
    public void Satisfies_Semilattice_Laws()
    {
        CrdtLaws.AssertSemilattice(Sample(1, "a"), Sample(2, "b"), Sample(3, "c"), SameValues);
    }

    [Test]
    public async Task Delta_Carries_Causal_Register_State()
    {
        var source = new MVRegister<string>();
        source.Assign(ReplicaId.FromUInt64(1), "alpha");

        bool extracted = source.TryExtractDelta(out MVRegister<string>? delta);
        var target = new MVRegister<string>();
        target.MergeDelta(delta!);
        bool extractedAgain = source.TryExtractDelta(out _);

        await Assert.That(extracted).IsTrue();
        await Assert.That(target).IsEqualTo(source);
        await Assert.That(extractedAgain).IsFalse();
    }

    [Test]
    public void Operations_Converge_Under_Reordering_And_Duplication()
    {
        var r0 = new MVRegister<string>();
        var r1 = new MVRegister<string>();
        var r2 = new MVRegister<string>();
        var sim = new OperationDeliverySimulator<MVRegister<string>, MVRegisterOperation<string>>(r0, r1, r2);

        sim.Broadcast(0, r0.Assign(ReplicaId.FromUInt64(1), "a"));
        sim.Broadcast(1, r1.Assign(ReplicaId.FromUInt64(2), "b"));
        sim.Broadcast(2, r2.Assign(ReplicaId.FromUInt64(3), "c"));

        sim.DeliverAll(seed: 17, duplicate: true);

        sim.AssertConverged(SameValues);
    }

    [Test]
    public async Task Operation_Carries_Causal_Removals()
    {
        var source = new MVRegister<string>();
        var target = new MVRegister<string>();
        MVRegisterOperation<string> first = source.Assign(ReplicaId.FromUInt64(1), "first");
        target.Apply(first);

        MVRegisterOperation<string> second = source.Assign(ReplicaId.FromUInt64(1), "second");
        target.Apply(second);

        await Assert.That(target.Count).IsEqualTo(1);
        await Assert.That(HasValue(target, "second")).IsTrue();
    }

    [Test]
    public async Task Binary_Roundtrips()
    {
        var register = new MVRegister<string>();
        register.Assign(ReplicaId.FromUInt64(1), "alpha");
        register.Assign(ReplicaId.FromUInt64(1), "beta");

        MVRegister<string> restored =
            MVRegister<string>.ReadFrom(register.ToByteArray(CrdtValues.String), CrdtValues.String);
        var operationSource = new MVRegister<string>();
        MVRegisterOperation<string> operation = operationSource.Assign(ReplicaId.FromUInt64(2), "gamma");
        MVRegisterOperation<string> restoredOperation =
            MVRegisterOperation<string>.ReadFrom(operation.ToByteArray(CrdtValues.String), CrdtValues.String);

        await Assert.That(restored).IsEqualTo(register);
        await Assert.That(restoredOperation.Value).IsEqualTo("gamma");
    }

    [Test]
    public async Task Json_Roundtrips()
    {
        var register = new MVRegister<long>();
        register.Assign(ReplicaId.FromUInt64(1), 7);
        register.Assign(ReplicaId.FromUInt64(2), 42);

        MVRegister<long> restored = MVRegister<long>.FromJson(register.ToJson(CrdtValues.Int64), CrdtValues.Int64);

        await Assert.That(SameValues(restored, register)).IsTrue();
    }

    private static MVRegister<string> Sample(ulong replica, string value)
    {
        var register = new MVRegister<string>();
        register.Assign(ReplicaId.FromUInt64(replica), value);
        return register;
    }

    private static bool SameValues<T>(MVRegister<T> left, MVRegister<T> right)
        where T : notnull => new HashSet<T>(left.Values).SetEquals(right.Values);

    private static bool HasValue<T>(MVRegister<T> register, T value)
        where T : notnull => register.Values.Contains(value);
}
