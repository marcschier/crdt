// Copyright (c) marcschier. Licensed under the MIT License.

namespace Crdt.Tests.Causality;

public sealed class IntervalTreeClockCoverageTests
{
    [Test]
    public async Task Binary_Write_And_ReadFrom_Roundtrip_Diverse_Stamps()
    {
        foreach (IntervalTreeClock stamp in DiverseStamps())
        {
            IntervalTreeClock roundtripped = RoundtripBinary(stamp);

            await Assert.That(roundtripped).IsEqualTo(stamp);
            await Assert.That(roundtripped.GetHashCode()).IsEqualTo(stamp.GetHashCode());
        }
    }

    [Test]
    public async Task Json_ToJson_And_FromJson_Roundtrip_Diverse_Stamps()
    {
        foreach (IntervalTreeClock stamp in DiverseStamps())
        {
            string json = stamp.ToJson();
            IntervalTreeClock roundtripped = IntervalTreeClock.FromJson(json);

            await Assert.That(roundtripped).IsEqualTo(stamp);
            await Assert.That(roundtripped.ToJson()).IsEqualTo(json);
        }
    }

    [Test]
    public async Task Repeated_Fork_On_Both_Halves_Events_And_Joins_Back_To_Seed_Identity()
    {
        (IntervalTreeClock left, IntervalTreeClock right) = IntervalTreeClock.Seed().Fork();
        (IntervalTreeClock leftLeft, IntervalTreeClock leftRight) = left.Fork();
        (IntervalTreeClock rightLeft, IntervalTreeClock rightRight) = right.Fork();

        IntervalTreeClock leftLeftEvent = leftLeft.Event();
        IntervalTreeClock leftRightEvent = leftRight.Event();
        IntervalTreeClock rightLeftEvent = rightLeft.Event();
        IntervalTreeClock rightRightEvent = rightRight.Event();

        IntervalTreeClock joined = leftLeftEvent
            .Join(leftRightEvent)
            .Join(rightLeftEvent)
            .Join(rightRightEvent);

        IntervalTreeClock seedAdvanced = IntervalTreeClock.Seed().Event();

        await Assert.That(joined.ToJson()).Contains("\"id\":{\"leaf\":1");
        await Assert.That(joined).IsEqualTo(seedAdvanced);
        await Assert.That(leftLeftEvent.Leq(joined)).IsTrue();
        await Assert.That(leftRightEvent.Leq(joined)).IsTrue();
        await Assert.That(rightLeftEvent.Leq(joined)).IsTrue();
        await Assert.That(rightRightEvent.Leq(joined)).IsTrue();
    }

    [Test]
    public async Task Event_On_Partial_Id_Fills_And_Normalizes_To_Compact_Leaf_Event()
    {
        IntervalTreeClock partial = IntervalTreeClock.FromJson("""
            {"id":{"left":{"leaf":1},"right":{"leaf":0}},"event":{"base":0,"left":{"value":0},"right":{"value":1}}}
            """);

        IntervalTreeClock filled = partial.Event();

        await Assert.That(partial.Leq(filled)).IsTrue();
        await Assert.That(filled.Leq(partial)).IsFalse();
        await Assert.That(filled.Compare(partial)).IsEqualTo(CrdtOrder.Greater);
        await Assert.That(filled.ToJson()).Contains("\"event\":{\"value\":1");
        await Assert.That(filled.ToJson()).DoesNotContain("\"base\":0");
    }

    [Test]
    public async Task Event_On_Deep_Partial_Id_Grows_With_Nested_Event_Shape()
    {
        IntervalTreeClock partial = IntervalTreeClock.FromJson("""
            {
              "id": {
                "left": { "left": { "leaf": 1 }, "right": { "leaf": 0 } },
                "right": { "left": { "leaf": 0 }, "right": { "leaf": 1 } }
              },
              "event": { "base": 0, "left": { "value": 1 }, "right": { "value": 0 } }
            }
            """);

        IntervalTreeClock grown = partial.Event();
        string json = grown.ToJson();

        await Assert.That(partial.Leq(grown)).IsTrue();
        await Assert.That(grown.Compare(partial)).IsEqualTo(CrdtOrder.Greater);
        await Assert.That(json).Contains("\"base\"");
        await Assert.That(json).Contains("\"left\"");
        await Assert.That(json).Contains("\"right\"");
    }

    [Test]
    public async Task Compare_And_Leq_Cover_All_Order_Relationships()
    {
        IntervalTreeClock seed = IntervalTreeClock.Seed();
        IntervalTreeClock sameSeed = IntervalTreeClock.Seed();
        IntervalTreeClock evented = seed.Event();
        (IntervalTreeClock forkLeft, IntervalTreeClock forkRight) = seed.Fork();
        IntervalTreeClock leftEvent = forkLeft.Event();
        IntervalTreeClock rightEvent = forkRight.Event();

        await Assert.That(seed.Compare(sameSeed)).IsEqualTo(CrdtOrder.Equal);
        await Assert.That(seed.Leq(sameSeed)).IsTrue();
        await Assert.That(sameSeed.Leq(seed)).IsTrue();

        await Assert.That(seed.Compare(evented)).IsEqualTo(CrdtOrder.Less);
        await Assert.That(seed.Leq(evented)).IsTrue();
        await Assert.That(evented.Leq(seed)).IsFalse();

        await Assert.That(evented.Compare(seed)).IsEqualTo(CrdtOrder.Greater);
        await Assert.That(leftEvent.Compare(rightEvent)).IsEqualTo(CrdtOrder.Concurrent);
        await Assert.That(leftEvent.Leq(rightEvent)).IsFalse();
        await Assert.That(rightEvent.Leq(leftEvent)).IsFalse();
    }

    [Test]
    public async Task Equals_And_GetHashCode_Are_Content_Based()
    {
        IntervalTreeClock seed = IntervalTreeClock.Seed();
        IntervalTreeClock equal = IntervalTreeClock.FromJson(seed.ToJson());
        IntervalTreeClock unequal = seed.Event();
        bool equalsDifferentType = ((object)seed).Equals(new object());

        await Assert.That(seed.Equals(equal)).IsTrue();
        await Assert.That(seed.Equals(unequal)).IsFalse();
        await Assert.That(seed.Equals((IntervalTreeClock?)null)).IsFalse();
        await Assert.That(equalsDifferentType).IsFalse();
        await Assert.That(seed!.GetHashCode()).IsEqualTo(equal!.GetHashCode());
        await Assert.That(seed.GetHashCode()).IsNotEqualTo(unequal.GetHashCode());
    }

    [Test]
    public async Task Merge_Is_InPlace_Join_Idempotent_And_Commutative()
    {
        (IntervalTreeClock leftId, IntervalTreeClock rightId) = IntervalTreeClock.Seed().Fork();
        IntervalTreeClock left = leftId.Event();
        IntervalTreeClock right = rightId.Event();
        IntervalTreeClock expected = left.Join(right);

        IntervalTreeClock leftThenRight = left.Clone();
        leftThenRight.Merge(right);

        IntervalTreeClock rightThenLeft = right.Clone();
        rightThenLeft.Merge(left);

        IntervalTreeClock idempotent = expected.Clone();
        idempotent.Merge(idempotent);

        await Assert.That(leftThenRight).IsEqualTo(expected);
        await Assert.That(rightThenLeft).IsEqualTo(expected);
        await Assert.That(idempotent).IsEqualTo(expected);
    }

    [Test]
    public async Task Public_Null_Guards_Throw()
    {
        IntervalTreeClock clock = IntervalTreeClock.Seed();

        await Assert.That(() => clock.Join(null!)).Throws<ArgumentNullException>();
        await Assert.That(() => clock.Merge(null!)).Throws<ArgumentNullException>();
        await Assert.That(() => clock.Leq(null!)).Throws<ArgumentNullException>();
        await Assert.That(() => clock.Compare(null!)).Throws<ArgumentNullException>();
        await Assert.That(() => IntervalTreeClock.FromJson(null!)).Throws<ArgumentNullException>();
    }

    [Test]
    public async Task Invalid_Binary_And_Json_Shapes_Throw()
    {
        await Assert.That(ReadBinaryWithInvalidIdentityLeaf).Throws<FormatException>();
        await Assert.That(ReadBinaryWithInvalidIdentityTag).Throws<FormatException>();
        await Assert.That(ReadBinaryWithInvalidEventTag).Throws<FormatException>();
        await Assert.That(ReadJsonWithInvalidIdentityLeaf).Throws<FormatException>();
        await Assert.That(ReadJsonWithMissingIdentityNodeChild).Throws<FormatException>();
        await Assert.That(ReadJsonWithMissingEventNodeChild).Throws<FormatException>();
    }

    private static IntervalTreeClock[] DiverseStamps()
    {
        IntervalTreeClock seed = IntervalTreeClock.Seed();
        (IntervalTreeClock left, IntervalTreeClock right) = seed.Fork();
        (IntervalTreeClock leftLeft, IntervalTreeClock leftRight) = left.Fork();
        IntervalTreeClock leftEvent = left.Event();
        IntervalTreeClock rightEvent = right.Event();
        IntervalTreeClock deepJoined = leftLeft.Event().Join(leftRight.Event()).Join(rightEvent);

        return
        [
            seed,
            left,
            right,
            leftEvent,
            rightEvent,
            leftEvent.Join(rightEvent),
            deepJoined,
        ];
    }

    private static IntervalTreeClock RoundtripBinary(IntervalTreeClock stamp)
    {
        using var buffer = new PooledBufferWriter();
        var writer = new CrdtWriter(buffer);
        stamp.Write(ref writer);
        return IntervalTreeClock.ReadFrom(buffer.WrittenSpan);
    }

    private static void ReadBinaryWithInvalidIdentityLeaf() =>
        IntervalTreeClock.ReadFrom([0, 2, 0, 0]);

    private static void ReadBinaryWithInvalidIdentityTag() =>
        IntervalTreeClock.ReadFrom([2]);

    private static void ReadBinaryWithInvalidEventTag() =>
        IntervalTreeClock.ReadFrom([0, 1, 2]);

    private static void ReadJsonWithInvalidIdentityLeaf() =>
        IntervalTreeClock.FromJson("""{"id":{"leaf":2},"event":{"value":0}}""");

    private static void ReadJsonWithMissingIdentityNodeChild() =>
        IntervalTreeClock.FromJson("""{"id":{"left":{"leaf":1}},"event":{"value":0}}""");

    private static void ReadJsonWithMissingEventNodeChild() =>
        IntervalTreeClock.FromJson("""{"id":{"leaf":1},"event":{"base":0,"left":{"value":0}}}""");
}
