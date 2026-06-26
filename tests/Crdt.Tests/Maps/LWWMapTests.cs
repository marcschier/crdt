// Copyright (c) marcschier. Licensed under the MIT License.

using Crdt.Tests.Testing;

namespace Crdt.Tests.Maps;

public sealed class LWWMapTests
{
    private static readonly ReplicaId ReplicaA = ReplicaId.FromUInt64(1);
    private static readonly ReplicaId ReplicaB = ReplicaId.FromUInt64(2);
    private static readonly ReplicaId ReplicaC = ReplicaId.FromUInt64(3);

    [Test]
    public async Task Set_Stores_Value_And_Key()
    {
        var map = new LWWMap<string, string>();
        LWWMapOperation<string, string> operation = map.Set("a", "alpha", Ts(1, 0, 1));
        bool hasValue = map.TryGetValue("a", out string? value);

        await Assert.That(hasValue).IsTrue();
        await Assert.That(value).IsEqualTo("alpha");
        await Assert.That(map["a"]).IsEqualTo("alpha");
        await Assert.That(map.ContainsKey("missing")).IsFalse();
        await Assert.That(map.Count).IsEqualTo(1);
        await Assert.That(operation.Key).IsEqualTo("a");
    }

    [Test]
    public async Task Set_Uses_Hybrid_Logical_Clock()
    {
        var time = new FakeTimeProvider();
        var clock = new HybridLogicalClock(ReplicaA, time);
        var map = new LWWMap<string, string>();

        map.Set("a", "alpha", clock);
        time.Advance(TimeSpan.FromMilliseconds(1));
        map.Set("a", "beta", clock);

        await Assert.That(map["a"]).IsEqualTo("beta");
    }

    [Test]
    public async Task Merge_Keeps_Greatest_Timestamp_Per_Key()
    {
        var left = new LWWMap<string, string>();
        left.Set("a", "old", Ts(1, 0, 1));
        left.Set("b", "left", Ts(3, 0, 1));
        var right = new LWWMap<string, string>();
        right.Set("a", "new", Ts(2, 0, 1));
        right.Set("b", "ignored", Ts(1, 0, 1));

        left.Merge(right);

        await Assert.That(left["a"]).IsEqualTo("new");
        await Assert.That(left["b"]).IsEqualTo("left");
    }

    [Test]
    public async Task Remove_Hides_And_ReAdd_Restores_Key()
    {
        var map = new LWWMap<string, string>();
        map.Set("a", "alpha", Ts(1, 0, 1));
        map.Remove("a", Ts(2, 0, 1));
        map.Set("a", "beta", Ts(3, 0, 1));

        await Assert.That(map.ContainsKey("a")).IsTrue();
        await Assert.That(map["a"]).IsEqualTo("beta");
        await Assert.That(map.Count).IsEqualTo(1);
    }

    [Test]
    public async Task Newer_Remove_Wins_Over_Older_Set()
    {
        var left = new LWWMap<string, string>();
        left.Set("a", "alpha", Ts(1, 0, 1));
        var right = left.Clone();
        right.Remove("a", Ts(2, 0, 1));
        left.Set("a", "ignored", Ts(1, 1, 1));

        left.Merge(right);

        await Assert.That(left.ContainsKey("a")).IsFalse();
    }

    [Test]
    public async Task Timestamp_Origin_Breaks_Ties()
    {
        var left = new LWWMap<string, string>();
        var right = new LWWMap<string, string>();
        left.Set("a", "low", new Timestamp(10, 0, ReplicaA));
        right.Set("a", "high", new Timestamp(10, 0, ReplicaB));

        left.Merge(right);

        await Assert.That(left["a"]).IsEqualTo("high");
    }

    [Test]
    public void Merge_Converges_And_Satisfies_Laws()
    {
        CrdtLaws.AssertSemilattice(Sample(ReplicaA, "a", "b"), Sample(ReplicaB, "b", "c"), Sample(ReplicaC, "d", "e"));
    }

    [Test]
    public async Task Compare_Reflects_Product_Timestamp_Order()
    {
        var small = new LWWMap<string, string>();
        small.Set("a", "alpha", Ts(1, 0, 1));
        var large = small.Clone();
        large.Set("b", "beta", Ts(2, 0, 1));
        var concurrent = new LWWMap<string, string>();
        concurrent.Set("c", "gamma", Ts(3, 0, 1));

        await Assert.That(small.Compare(large)).IsEqualTo(CrdtOrder.Less);
        await Assert.That(large.Compare(small)).IsEqualTo(CrdtOrder.Greater);
        await Assert.That(small.Compare(concurrent)).IsEqualTo(CrdtOrder.Concurrent);
        await Assert.That(small.Compare(small.Clone())).IsEqualTo(CrdtOrder.Equal);
    }

    [Test]
    public async Task Delta_Carries_Recent_Key_States()
    {
        var source = new LWWMap<string, string>();
        source.Set("a", "alpha", Ts(1, 0, 1));
        source.Remove("a", Ts(2, 0, 1));

        bool extracted = source.TryExtractDelta(out LWWMap<string, string>? delta);
        var target = new LWWMap<string, string>();
        target.MergeDelta(delta!);
        bool extractedAgain = source.TryExtractDelta(out _);

        await Assert.That(extracted).IsTrue();
        await Assert.That(target.ContainsKey("a")).IsFalse();
        await Assert.That(extractedAgain).IsFalse();
    }

    [Test]
    public void Operations_Converge_Under_Reordering_And_Duplication()
    {
        var r0 = new LWWMap<string, string>();
        var r1 = new LWWMap<string, string>();
        var r2 = new LWWMap<string, string>();
        var sim = new OperationDeliverySimulator<LWWMap<string, string>, LWWMapOperation<string, string>>(r0, r1, r2);

        sim.Broadcast(0, r0.Set("a", "alpha", Ts(1, 0, 1)));
        sim.Broadcast(1, r1.Set("b", "beta", Ts(2, 0, 1)));
        sim.Broadcast(2, r2.Remove("a", Ts(3, 0, 1)));

        sim.DeliverAll(seed: 17, duplicate: true);

        sim.AssertConverged(static (x, y) => x.Equals(y));
    }

    [Test]
    public async Task Binary_And_Operation_Roundtrip()
    {
        var map = new LWWMap<string, long>();
        LWWMapOperation<string, long> operation = map.Set("a", 42, Ts(7, 0, 1));
        map.Remove("b", Ts(8, 0, 1));

        LWWMap<string, long> restored = LWWMap<string, long>.ReadFrom(
            map.ToByteArray(CrdtValues.String, CrdtValues.Int64), CrdtValues.String, CrdtValues.Int64);
        LWWMapOperation<string, long> restoredOperation = LWWMapOperation<string, long>.ReadFrom(
            operation.ToByteArray(CrdtValues.String, CrdtValues.Int64), CrdtValues.String, CrdtValues.Int64);

        await Assert.That(restored).IsEqualTo(map);
        await Assert.That(restoredOperation.Value).IsEqualTo(42);
    }

    [Test]
    public async Task Json_Roundtrips()
    {
        var map = new LWWMap<string, long>();
        map.Set("a", 42, Ts(9, 0, 1));
        map.Remove("b", Ts(10, 0, 1));

        LWWMap<string, long> restored = LWWMap<string, long>.FromJson(
            map.ToJson(CrdtValues.String, CrdtValues.Int64), CrdtValues.String, CrdtValues.Int64);

        await Assert.That(restored).IsEqualTo(map);
    }

    private static LWWMap<string, string> Sample(ReplicaId replica, string first, string second)
    {
        var map = new LWWMap<string, string>();
        map.Set(first, first, new Timestamp(1, 0, replica));
        map.Set(second, second, new Timestamp(2, 0, replica));
        return map;
    }

    private static Timestamp Ts(long wallClock, ulong counter, ulong replica) =>
        new(wallClock, counter, ReplicaId.FromUInt64(replica));
}
