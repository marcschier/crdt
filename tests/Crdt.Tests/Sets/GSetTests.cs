// Copyright (c) marcschier. Licensed under the MIT License.

using Crdt.Tests.Testing;

namespace Crdt.Tests.Sets;

public sealed class GSetTests
{
    [Test]
    public async Task Add_Makes_Element_Present()
    {
        var set = new GSet<string>();
        set.Add("a");
        set.Add("b");

        await Assert.That(set.Contains("a")).IsTrue();
        await Assert.That(set.Contains("c")).IsFalse();
        await Assert.That(set.Count).IsEqualTo(2);
    }

    [Test]
    public async Task Merge_Is_Union()
    {
        var left = new GSet<string>();
        left.Add("a");
        left.Add("b");

        var right = new GSet<string>();
        right.Add("b");
        right.Add("c");

        left.Merge(right);

        await Assert.That(left.Count).IsEqualTo(3);
        await Assert.That(left.Contains("c")).IsTrue();
    }

    [Test]
    public void Satisfies_Semilattice_Laws()
    {
        CrdtLaws.AssertSemilattice(Sample("a", "b"), Sample("b", "c"), Sample("a", "d"));
    }

    [Test]
    public async Task Delta_Carries_Only_New_Elements()
    {
        var source = new GSet<string>();
        source.Add("a");
        source.Add("b");

        bool extracted = source.TryExtractDelta(out GSet<string>? delta);
        var target = new GSet<string>();
        target.Add("z");
        target.MergeDelta(delta!);

        await Assert.That(extracted).IsTrue();
        await Assert.That(target.Contains("a")).IsTrue();
        await Assert.That(target.Contains("z")).IsTrue();
        await Assert.That(target.Count).IsEqualTo(3);
    }

    [Test]
    public void Operations_Converge_Under_Reordering_And_Duplication()
    {
        var r0 = new GSet<string>();
        var r1 = new GSet<string>();
        var r2 = new GSet<string>();
        var sim = new OperationDeliverySimulator<GSet<string>, GSetOperation<string>>(r0, r1, r2);

        sim.Broadcast(0, r0.Add("a"));
        sim.Broadcast(1, r1.Add("b"));
        sim.Broadcast(2, r2.Add("c"));

        sim.DeliverAll(seed: 5, duplicate: true);

        sim.AssertConverged(static (x, y) => x.Equals(y));
    }

    [Test]
    public async Task Binary_Roundtrips()
    {
        var set = new GSet<string>();
        set.Add("alpha");
        set.Add("beta");

        GSet<string> restored = GSet<string>.ReadFrom(set.ToByteArray(CrdtValues.String), CrdtValues.String);
        await Assert.That(restored).IsEqualTo(set);
    }

    [Test]
    public async Task Json_Roundtrips()
    {
        var set = new GSet<long>();
        set.Add(7);
        set.Add(42);

        GSet<long> restored = GSet<long>.FromJson(set.ToJson(CrdtValues.Int64), CrdtValues.Int64);
        await Assert.That(restored).IsEqualTo(set);
    }

    [Test]
    public async Task Compare_Reflects_Subset_Relationship()
    {
        var small = new GSet<string>();
        small.Add("a");

        var large = small.Clone();
        large.Add("b");

        var concurrent = new GSet<string>();
        concurrent.Add("c");

        await Assert.That(small.Compare(large)).IsEqualTo(CrdtOrder.Less);
        await Assert.That(large.Compare(small)).IsEqualTo(CrdtOrder.Greater);
        await Assert.That(small.Compare(concurrent)).IsEqualTo(CrdtOrder.Concurrent);
        await Assert.That(small.Compare(small.Clone())).IsEqualTo(CrdtOrder.Equal);
    }

    private static GSet<string> Sample(string first, string second)
    {
        var set = new GSet<string>();
        set.Add(first);
        set.Add(second);
        return set;
    }
}
