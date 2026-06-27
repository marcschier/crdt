// Copyright (c) marcschier. Licensed under the MIT License.

namespace Crdt.Tests.Causality;

public sealed class IntervalTreeClockTests
{
    [Test]
    public async Task Seed_Fork_Yields_Two_Stamps_That_Join_Back()
    {
        IntervalTreeClock seed = IntervalTreeClock.Seed();
        (IntervalTreeClock a, IntervalTreeClock b) = seed.Fork();

        IntervalTreeClock joined = a.Join(b);

        await Assert.That(joined).IsEqualTo(seed);
    }

    [Test]
    public async Task Event_Is_Strictly_Greater()
    {
        IntervalTreeClock seed = IntervalTreeClock.Seed();
        IntervalTreeClock advanced = seed.Event();

        await Assert.That(seed.Leq(advanced)).IsTrue();
        await Assert.That(advanced.Leq(seed)).IsFalse();
        await Assert.That(advanced.Compare(seed)).IsEqualTo(CrdtOrder.Greater);
    }

    [Test]
    public async Task Concurrent_Events_From_Forked_Stamps_Are_Concurrent()
    {
        (IntervalTreeClock a, IntervalTreeClock b) = IntervalTreeClock.Seed().Fork();

        IntervalTreeClock left = a.Event();
        IntervalTreeClock right = b.Event();

        await Assert.That(left.Compare(right)).IsEqualTo(CrdtOrder.Concurrent);
        await Assert.That(right.Compare(left)).IsEqualTo(CrdtOrder.Concurrent);
    }

    [Test]
    public async Task Json_Read_Normalizes_Compactable_Event_Nodes()
    {
        const string expanded = """
            {"id":{"leaf":1},"event":{"base":2,"left":{"value":1},"right":{"value":1}}}
            """;
        const string compact = """
            {"id":{"leaf":1},"event":{"value":3}}
            """;

        IntervalTreeClock fromExpanded = IntervalTreeClock.FromJson(expanded);
        IntervalTreeClock fromCompact = IntervalTreeClock.FromJson(compact);

        await Assert.That(fromExpanded).IsEqualTo(fromCompact);
        await Assert.That(fromExpanded.ToJson()).IsEqualTo(fromCompact.ToJson());
    }

    [Test]
    public async Task Fork_Event_Join_Converges_Associatively()
    {
        (IntervalTreeClock a, IntervalTreeClock b) = IntervalTreeClock.Seed().Fork();
        (IntervalTreeClock b1, IntervalTreeClock b2) = b.Fork();

        IntervalTreeClock ea = a.Event();
        IntervalTreeClock eb1 = b1.Event();
        IntervalTreeClock eb2 = b2.Event();

        IntervalTreeClock leftAssociated = ea.Join(eb1).Join(eb2);
        IntervalTreeClock rightAssociated = ea.Join(eb1.Join(eb2));

        var merged = ea.Clone();
        merged.Merge(eb1);
        merged.Merge(eb2);

        await Assert.That(leftAssociated).IsEqualTo(rightAssociated);
        await Assert.That(merged).IsEqualTo(leftAssociated);
        await Assert.That(ea.Leq(leftAssociated)).IsTrue();
        await Assert.That(eb1.Leq(leftAssociated)).IsTrue();
        await Assert.That(eb2.Leq(leftAssociated)).IsTrue();
    }

    [Test]
    public async Task Binary_And_Json_Roundtrip()
    {
        (IntervalTreeClock a, IntervalTreeClock b) = IntervalTreeClock.Seed().Fork();
        IntervalTreeClock clock = a.Event().Join(b.Event());

        IntervalTreeClock binary = IntervalTreeClock.ReadFrom(clock.ToByteArray());
        IntervalTreeClock json = IntervalTreeClock.FromJson(clock.ToJson());

        await Assert.That(binary).IsEqualTo(clock);
        await Assert.That(json).IsEqualTo(clock);
    }
}
