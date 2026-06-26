// Copyright (c) marcschier. Licensed under the MIT License.

using Crdt.Tests.Testing;

namespace Crdt.Tests.Sets;

public sealed class TwoPhaseSetTests
{
    [Test]
    public async Task Add_Makes_Element_Present()
    {
        var set = new TwoPhaseSet<string>();
        set.Add("a");
        set.Add("b");

        await Assert.That(set.Contains("a")).IsTrue();
        await Assert.That(set.Contains("c")).IsFalse();
        await Assert.That(set.Count).IsEqualTo(2);
    }

    [Test]
    public async Task Merge_Converges_Adds_And_Removes()
    {
        var left = new TwoPhaseSet<string>();
        left.Add("a");
        left.Add("b");

        var right = new TwoPhaseSet<string>();
        right.Add("b");
        right.Add("c");
        right.Remove("b");

        left.Merge(right);
        right.Merge(left);

        await Assert.That(left.Equals(right)).IsTrue();
        await Assert.That(left.Contains("a")).IsTrue();
        await Assert.That(left.Contains("b")).IsFalse();
        await Assert.That(left.Count).IsEqualTo(2);
    }

    [Test]
    public void Satisfies_Semilattice_Laws()
    {
        CrdtLaws.AssertSemilattice(Sample("a", "b"), Sample("b", "c"), Sample("a", "d"));
    }

    [Test]
    public async Task Delta_Carries_Adds_And_Removes()
    {
        var source = new TwoPhaseSet<string>();
        source.Add("a");
        source.Add("b");
        source.Remove("a");

        bool extracted = source.TryExtractDelta(out TwoPhaseSet<string>? delta);
        var target = new TwoPhaseSet<string>();
        target.MergeDelta(delta!);

        await Assert.That(extracted).IsTrue();
        await Assert.That(target.Contains("a")).IsFalse();
        await Assert.That(target.Contains("b")).IsTrue();
    }

    [Test]
    public void Operations_Converge_Under_Reordering_And_Duplication()
    {
        var r0 = new TwoPhaseSet<string>();
        var r1 = new TwoPhaseSet<string>();
        var r2 = new TwoPhaseSet<string>();
        var sim = new OperationDeliverySimulator<TwoPhaseSet<string>, TwoPhaseSetOperation<string>>(r0, r1, r2);

        sim.Broadcast(0, r0.Add("a"));
        sim.Broadcast(1, r1.Add("b"));
        sim.Broadcast(1, r1.Remove("b"));
        sim.Broadcast(2, r2.Add("c"));

        sim.DeliverAll(seed: 17, duplicate: true);

        sim.AssertConverged(static (x, y) => x.Equals(y));
    }

    [Test]
    public async Task Binary_Roundtrips()
    {
        var set = new TwoPhaseSet<string>();
        set.Add("alpha");
        set.Add("beta");
        set.Remove("alpha");

        TwoPhaseSet<string> restored = TwoPhaseSet<string>.ReadFrom(
            set.ToByteArray(CrdtValues.String),
            CrdtValues.String);

        await Assert.That(restored).IsEqualTo(set);
    }

    [Test]
    public async Task Json_Roundtrips()
    {
        var set = new TwoPhaseSet<long>();
        set.Add(7);
        set.Add(42);
        set.Remove(7);

        TwoPhaseSet<long> restored = TwoPhaseSet<long>.FromJson(set.ToJson(CrdtValues.Int64), CrdtValues.Int64);

        await Assert.That(restored).IsEqualTo(set);
    }

    [Test]
    public async Task Compare_Reflects_Product_Order()
    {
        var small = new TwoPhaseSet<string>();
        small.Add("a");

        var large = small.Clone();
        large.Add("b");

        var removed = new TwoPhaseSet<string>();
        removed.Add("a");
        removed.Add("c");
        removed.Remove("c");

        await Assert.That(small.Compare(large)).IsEqualTo(CrdtOrder.Less);
        await Assert.That(large.Compare(small)).IsEqualTo(CrdtOrder.Greater);
        await Assert.That(small.Compare(removed)).IsEqualTo(CrdtOrder.Less);
        await Assert.That(large.Compare(removed)).IsEqualTo(CrdtOrder.Concurrent);
        await Assert.That(small.Compare(small.Clone())).IsEqualTo(CrdtOrder.Equal);
    }

    [Test]
    public async Task Remove_Is_Permanent_And_Readd_Does_Not_Restore()
    {
        var set = new TwoPhaseSet<string>();
        set.Add("a");
        set.Remove("a");
        set.Add("a");

        await Assert.That(set.Contains("a")).IsFalse();
        await Assert.That(set.Count).IsEqualTo(0);
    }

    private static TwoPhaseSet<string> Sample(string first, string second)
    {
        var set = new TwoPhaseSet<string>();
        set.Add(first);
        set.Add(second);
        return set;
    }
}
