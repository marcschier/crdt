// Copyright (c) marcschier. Licensed under the MIT License.

using Crdt.Tests.Testing;

namespace Crdt.Tests.Maps;

public sealed class ORMapTests
{
    private static readonly ReplicaId ReplicaA = ReplicaId.FromUInt64(1);
    private static readonly ReplicaId ReplicaB = ReplicaId.FromUInt64(2);
    private static readonly ReplicaId ReplicaC = ReplicaId.FromUInt64(3);
    private static readonly GCounterValueOps ValueOps = new();

    [Test]
    public async Task Update_Creates_And_Merges_Value_Crdt_Under_Key()
    {
        var map = new ORMap<string, GCounter>(ValueOps);
        map.Update(ReplicaA, "counter", Counter(ReplicaA, 2));
        map.Update(ReplicaA, "counter", Counter(ReplicaA, 5));
        bool hasValue = map.TryGetValue("counter", out GCounter? value);

        await Assert.That(hasValue).IsTrue();
        await Assert.That(value!.Value).IsEqualTo(5UL);
        await Assert.That(map.ContainsKey("counter")).IsTrue();
        await Assert.That(map.Count).IsEqualTo(1);
    }

    [Test]
    public async Task Remove_Drops_Key()
    {
        var map = new ORMap<string, GCounter>(ValueOps);
        map.Update(ReplicaA, "counter", Counter(ReplicaA, 1));

        map.Remove("counter");

        await Assert.That(map.ContainsKey("counter")).IsFalse();
        await Assert.That(map.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Concurrent_Update_Wins_Over_Observed_Remove()
    {
        var original = new ORMap<string, GCounter>(ValueOps);
        original.Update(ReplicaA, "counter", Counter(ReplicaA, 1));
        var remover = original.Clone();
        var updater = original.Clone();

        remover.Remove("counter");
        updater.Update(ReplicaB, "counter", Counter(ReplicaB, 2));
        remover.Merge(updater);
        updater.Merge(remover);

        await Assert.That(remover.ContainsKey("counter")).IsTrue();
        await Assert.That(updater.ContainsKey("counter")).IsTrue();
        await Assert.That(remover["counter"].Value).IsEqualTo(3UL);
        await Assert.That(updater["counter"].Value).IsEqualTo(3UL);
    }

    [Test]
    public async Task Concurrent_Updates_To_Same_Key_Merge_Value_Crdts()
    {
        var left = new ORMap<string, GCounter>(ValueOps);
        var right = new ORMap<string, GCounter>(ValueOps);
        left.Update(ReplicaA, "counter", Counter(ReplicaA, 2));
        right.Update(ReplicaB, "counter", Counter(ReplicaB, 3));

        left.Merge(right);
        right.Merge(left);

        await Assert.That(left.Equals(right)).IsTrue();
        await Assert.That(left["counter"].Value).IsEqualTo(5UL);
    }

    [Test]
    public void Satisfies_Semilattice_Laws()
    {
        CrdtLaws.AssertSemilattice(
            Sample(ReplicaA, "a", 1),
            Sample(ReplicaB, "a", 2),
            Sample(ReplicaC, "b", 3),
            static (left, right) => left.Equals(right));
    }

    [Test]
    public async Task Delta_Carries_Updates_And_Removes()
    {
        var source = new ORMap<string, GCounter>(ValueOps);
        source.Update(ReplicaA, "counter", Counter(ReplicaA, 1));
        source.Remove("counter");

        bool extracted = source.TryExtractDelta(out ORMap<string, GCounter>? delta);
        var target = new ORMap<string, GCounter>(ValueOps);
        target.MergeDelta(delta!);
        bool extractedAgain = source.TryExtractDelta(out _);

        await Assert.That(extracted).IsTrue();
        await Assert.That(target.ContainsKey("counter")).IsFalse();
        await Assert.That(extractedAgain).IsFalse();
    }

    [Test]
    public void Operations_Converge_Under_Reordering_And_Duplication()
    {
        var r0 = new ORMap<string, GCounter>(ValueOps);
        var r1 = new ORMap<string, GCounter>(ValueOps);
        var r2 = new ORMap<string, GCounter>(ValueOps);
        var sim = new OperationDeliverySimulator<ORMap<string, GCounter>, ORMapOperation<string, GCounter>>(r0, r1, r2);

        sim.Broadcast(0, r0.Update(ReplicaA, "left", Counter(ReplicaA, 1)));
        sim.Broadcast(1, r1.Update(ReplicaB, "counter", Counter(ReplicaB, 2)));
        sim.Broadcast(1, r1.Remove("counter"));
        sim.Broadcast(2, r2.Update(ReplicaC, "other", Counter(ReplicaC, 4)));

        sim.DeliverAll(seed: 37, duplicate: true);

        sim.AssertConverged(static (x, y) => x.Equals(y));
    }

    [Test]
    public async Task Binary_And_Operation_Roundtrip()
    {
        var map = new ORMap<string, GCounter>(ValueOps);
        ORMapOperation<string, GCounter> operation = map.Update(ReplicaA, "counter", Counter(ReplicaA, 7));
        map.Update(ReplicaB, "other", Counter(ReplicaB, 3));
        map.Remove("other");

        ORMap<string, GCounter> restored = ORMap<string, GCounter>.ReadFrom(
            map.ToByteArray(CrdtValues.String), CrdtValues.String, ValueOps);
        ORMapOperation<string, GCounter> restoredOperation = ORMapOperation<string, GCounter>.ReadFrom(
            operation.ToByteArray(CrdtValues.String, ValueOps), CrdtValues.String, ValueOps);

        await Assert.That(restored).IsEqualTo(map);
        await Assert.That(restoredOperation.Value!.Value).IsEqualTo(7UL);
    }

    [Test]
    public async Task Json_Roundtrips()
    {
        var map = new ORMap<string, GCounter>(ValueOps);
        map.Update(ReplicaA, "counter", Counter(ReplicaA, 7));
        map.Update(ReplicaB, "other", Counter(ReplicaB, 3));
        map.Remove("other");

        ORMap<string, GCounter> restored = ORMap<string, GCounter>.FromJson(
            map.ToJson(CrdtValues.String), CrdtValues.String, ValueOps);

        await Assert.That(restored).IsEqualTo(map);
    }

    [Test]
    public async Task Compare_Reflects_Causal_State_Order()
    {
        var small = new ORMap<string, GCounter>(ValueOps);
        small.Update(ReplicaA, "counter", Counter(ReplicaA, 1));
        var large = small.Clone();
        large.Update(ReplicaB, "other", Counter(ReplicaB, 2));
        var concurrent = new ORMap<string, GCounter>(ValueOps);
        concurrent.Update(ReplicaC, "third", Counter(ReplicaC, 3));

        await Assert.That(small.Compare(large)).IsEqualTo(CrdtOrder.Less);
        await Assert.That(large.Compare(small)).IsEqualTo(CrdtOrder.Greater);
        await Assert.That(small.Compare(concurrent)).IsEqualTo(CrdtOrder.Concurrent);
        await Assert.That(small.Compare(small.Clone())).IsEqualTo(CrdtOrder.Equal);
    }

    private static ORMap<string, GCounter> Sample(ReplicaId replica, string key, ulong amount)
    {
        var map = new ORMap<string, GCounter>(ValueOps);
        map.Update(replica, key, Counter(replica, amount));
        return map;
    }

    private static GCounter Counter(ReplicaId replica, ulong amount)
    {
        var counter = new GCounter();
        counter.Increment(replica, amount);
        return counter;
    }
}
