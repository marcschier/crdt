// Copyright (c) marcschier. Licensed under the MIT License.

using Crdt.Tests.Testing;

namespace Crdt.Tests.Sets;

public sealed class ORSetTests
{
    private static readonly ReplicaId ReplicaA = ReplicaId.FromUInt64(1);
    private static readonly ReplicaId ReplicaB = ReplicaId.FromUInt64(2);
    private static readonly ReplicaId ReplicaC = ReplicaId.FromUInt64(3);

    [Test]
    public async Task Add_Makes_Element_Present()
    {
        var set = new ORSet<string>();
        set.Add(ReplicaA, "a");
        set.Add(ReplicaA, "b");

        await Assert.That(set.Contains("a")).IsTrue();
        await Assert.That(set.Contains("c")).IsFalse();
        await Assert.That(set.Count).IsEqualTo(2);
        await Assert.That(set.DotCount).IsEqualTo(2);
    }

    [Test]
    public async Task Merge_Converges_Adds_And_Observed_Removes()
    {
        var left = new ORSet<string>();
        left.Add(ReplicaA, "a");
        left.Add(ReplicaA, "b");

        var right = left.Clone();
        right.Remove("b");
        right.Add(ReplicaB, "c");

        left.Merge(right);
        right.Merge(left);

        await Assert.That(left.Equals(right)).IsTrue();
        await Assert.That(left.Contains("a")).IsTrue();
        await Assert.That(left.Contains("b")).IsFalse();
        await Assert.That(left.Contains("c")).IsTrue();
    }

    [Test]
    public void Satisfies_Semilattice_Laws()
    {
        CrdtLaws.AssertSemilattice(Sample(ReplicaA, "a", "b"), Sample(ReplicaB, "b", "c"), Sample(ReplicaC, "a", "d"));
    }

    [Test]
    public async Task Delta_Carries_Adds_And_Removes()
    {
        var source = new ORSet<string>();
        source.Add(ReplicaA, "a");
        source.Add(ReplicaA, "b");
        source.Remove("a");

        bool extracted = source.TryExtractDelta(out ORSet<string>? delta);
        var target = new ORSet<string>();
        target.MergeDelta(delta!);

        await Assert.That(extracted).IsTrue();
        await Assert.That(target.Contains("a")).IsFalse();
        await Assert.That(target.Contains("b")).IsTrue();
    }

    [Test]
    public void Operations_Converge_Under_Reordering_And_Duplication()
    {
        var r0 = new ORSet<string>();
        var r1 = new ORSet<string>();
        var r2 = new ORSet<string>();
        var sim = new OperationDeliverySimulator<ORSet<string>, ORSetOperation<string>>(r0, r1, r2);

        sim.Broadcast(0, r0.Add(ReplicaA, "a"));
        sim.Broadcast(1, r1.Add(ReplicaB, "b"));
        sim.Broadcast(1, r1.Remove("b"));
        sim.Broadcast(2, r2.Add(ReplicaC, "c"));

        sim.DeliverAll(seed: 29, duplicate: true);

        sim.AssertConverged(static (x, y) => x.Equals(y));
    }

    [Test]
    public async Task Binary_Roundtrips()
    {
        var set = new ORSet<string>();
        set.Add(ReplicaA, "alpha");
        set.Add(ReplicaB, "beta");
        set.Remove("alpha");

        ORSet<string> restored = ORSet<string>.ReadFrom(set.ToByteArray(CrdtValues.String), CrdtValues.String);

        await Assert.That(restored).IsEqualTo(set);
    }

    [Test]
    public async Task Json_Roundtrips()
    {
        var set = new ORSet<long>();
        set.Add(ReplicaA, 7);
        set.Add(ReplicaB, 42);
        set.Remove(7);

        ORSet<long> restored = ORSet<long>.FromJson(set.ToJson(CrdtValues.Int64), CrdtValues.Int64);

        await Assert.That(restored).IsEqualTo(set);
    }

    [Test]
    public async Task Compare_Reflects_Causal_State_Order()
    {
        var small = new ORSet<string>();
        small.Add(ReplicaA, "a");

        var large = small.Clone();
        large.Add(ReplicaB, "b");

        var concurrent = new ORSet<string>();
        concurrent.Add(ReplicaC, "c");

        await Assert.That(small.Compare(large)).IsEqualTo(CrdtOrder.Less);
        await Assert.That(large.Compare(small)).IsEqualTo(CrdtOrder.Greater);
        await Assert.That(small.Compare(concurrent)).IsEqualTo(CrdtOrder.Concurrent);
        await Assert.That(small.Compare(small.Clone())).IsEqualTo(CrdtOrder.Equal);
    }

    [Test]
    public async Task Concurrent_Add_Wins_Over_Observed_Remove()
    {
        var original = new ORSet<string>();
        original.Add(ReplicaA, "a");

        var remover = original.Clone();
        var adder = original.Clone();
        remover.Remove("a");
        adder.Add(ReplicaB, "a");

        remover.Merge(adder);

        await Assert.That(remover.Contains("a")).IsTrue();
        await Assert.That(remover.Count).IsEqualTo(1);
    }

    private static ORSet<string> Sample(ReplicaId replica, string first, string second)
    {
        var set = new ORSet<string>();
        set.Add(replica, first);
        set.Add(replica, second);
        return set;
    }
}
