// Copyright (c) marcschier. Licensed under the MIT License.

using Crdt.Tests.Testing;

namespace Crdt.Tests.Sets;

public sealed class LWWElementSetTests
{
    private static readonly ReplicaId ReplicaA = ReplicaId.FromUInt64(1);
    private static readonly ReplicaId ReplicaB = ReplicaId.FromUInt64(2);

    [Test]
    public async Task Add_Makes_Element_Present()
    {
        var set = new LWWElementSet<string>();
        set.Add("a", Ts(1));
        set.Add("b", Ts(2));

        await Assert.That(set.Contains("a")).IsTrue();
        await Assert.That(set.Contains("c")).IsFalse();
        await Assert.That(set.Count).IsEqualTo(2);
    }

    [Test]
    public async Task Merge_Converges_By_Max_Timestamps()
    {
        var left = new LWWElementSet<string>();
        left.Add("a", Ts(1));
        left.Add("b", Ts(2));

        var right = new LWWElementSet<string>();
        right.Add("b", Ts(3));
        right.Remove("b", Ts(4));
        right.Add("c", Ts(5));

        left.Merge(right);
        right.Merge(left);

        await Assert.That(left.Equals(right)).IsTrue();
        await Assert.That(left.Contains("b")).IsFalse();
        await Assert.That(left.Contains("c")).IsTrue();
    }

    [Test]
    public void Satisfies_Semilattice_Laws()
    {
        CrdtLaws.AssertSemilattice(Sample("a", "b", 1), Sample("b", "c", 10), Sample("a", "d", 20));
    }

    [Test]
    public async Task Delta_Carries_Timestamped_Adds_And_Removes()
    {
        var source = new LWWElementSet<string>();
        source.Add("a", Ts(1));
        source.Remove("a", Ts(2));
        source.Add("b", Ts(3));

        bool extracted = source.TryExtractDelta(out LWWElementSet<string>? delta);
        var target = new LWWElementSet<string>();
        target.MergeDelta(delta!);

        await Assert.That(extracted).IsTrue();
        await Assert.That(target.Contains("a")).IsFalse();
        await Assert.That(target.Contains("b")).IsTrue();
    }

    [Test]
    public void Operations_Converge_Under_Reordering_And_Duplication()
    {
        var r0 = new LWWElementSet<string>();
        var r1 = new LWWElementSet<string>();
        var r2 = new LWWElementSet<string>();
        var sim = new OperationDeliverySimulator<LWWElementSet<string>, LWWElementSetOperation<string>>(r0, r1, r2);

        sim.Broadcast(0, r0.Add("a", Ts(1)));
        sim.Broadcast(1, r1.Add("b", Ts(2)));
        sim.Broadcast(1, r1.Remove("b", Ts(3)));
        sim.Broadcast(2, r2.Add("c", Ts(4)));

        sim.DeliverAll(seed: 23, duplicate: true);

        sim.AssertConverged(static (x, y) => x.Equals(y));
    }

    [Test]
    public async Task Binary_Roundtrips()
    {
        var set = new LWWElementSet<string>();
        set.Add("alpha", Ts(1));
        set.Add("beta", Ts(2));
        set.Remove("alpha", Ts(3));

        LWWElementSet<string> restored = LWWElementSet<string>.ReadFrom(
            set.ToByteArray(CrdtValues.String),
            CrdtValues.String);

        await Assert.That(restored).IsEqualTo(set);
    }

    [Test]
    public async Task Json_Roundtrips()
    {
        var set = new LWWElementSet<long>(LWWElementSetBias.RemoveWins);
        set.Add(7, Ts(1));
        set.Remove(7, Ts(2));
        set.Add(42, Ts(3));

        LWWElementSet<long> restored = LWWElementSet<long>.FromJson(set.ToJson(CrdtValues.Int64), CrdtValues.Int64);

        await Assert.That(restored).IsEqualTo(set);
    }

    [Test]
    public async Task Compare_Reflects_Timestamp_Map_Order()
    {
        var small = new LWWElementSet<string>();
        small.Add("a", Ts(1));

        var large = small.Clone();
        large.Add("b", Ts(2));

        var concurrent = new LWWElementSet<string>();
        concurrent.Add("a", Ts(5));
        concurrent.Add("c", Ts(1));

        await Assert.That(small.Compare(large)).IsEqualTo(CrdtOrder.Less);
        await Assert.That(large.Compare(small)).IsEqualTo(CrdtOrder.Greater);
        await Assert.That(large.Compare(concurrent)).IsEqualTo(CrdtOrder.Concurrent);
        await Assert.That(small.Compare(small.Clone())).IsEqualTo(CrdtOrder.Equal);
    }

    [Test]
    public async Task Readd_After_Remove_Works_When_Add_Timestamp_Wins()
    {
        var set = new LWWElementSet<string>();
        set.Add("a", Ts(1));
        set.Remove("a", Ts(2));
        set.Add("a", Ts(3));

        await Assert.That(set.Contains("a")).IsTrue();
    }

    [Test]
    public async Task Bias_Decides_Equal_Timestamps()
    {
        Timestamp timestamp = Ts(10);
        var addWins = new LWWElementSet<string>(LWWElementSetBias.AddWins);
        var removeWins = new LWWElementSet<string>(LWWElementSetBias.RemoveWins);

        addWins.Add("a", timestamp);
        addWins.Remove("a", timestamp);
        removeWins.Add("a", timestamp);
        removeWins.Remove("a", timestamp);

        await Assert.That(addWins.Contains("a")).IsTrue();
        await Assert.That(removeWins.Contains("a")).IsFalse();
    }

    private static LWWElementSet<string> Sample(string first, string second, long wallClock)
    {
        var set = new LWWElementSet<string>();
        set.Add(first, Ts(wallClock));
        set.Add(second, Ts(wallClock + 1));
        return set;
    }

    private static Timestamp Ts(long wallClock) => new(wallClock, 0, ReplicaA);

    private static Timestamp TsFromB(long wallClock) => new(wallClock, 0, ReplicaB);
}
