// Copyright (c) marcschier. Licensed under the MIT License.

using System.Text.Json;

namespace Crdt.Tests.Counters;

public sealed class HandoffCounterCoverageTests
{
    private static readonly ReplicaId A = ReplicaId.FromUInt64(101);
    private static readonly ReplicaId B = ReplicaId.FromUInt64(102);
    private static readonly ReplicaId C = ReplicaId.FromUInt64(103);
    private static readonly ReplicaId D = ReplicaId.FromUInt64(104);
    private static readonly ReplicaId AggregatorOne = ReplicaId.FromUInt64(201);
    private static readonly ReplicaId AggregatorTwo = ReplicaId.FromUInt64(202);
    private static readonly ReplicaId Root = ReplicaId.FromUInt64(301);

    [Test]
    public async Task Merge_Multi_Tier_Topology_Converges_And_Remains_Stable()
    {
        var a = Client(A, 4);
        var b = Client(B, 6);
        var c = Client(C, 8);
        var d = Client(D, 10);
        var leftAggregator = new HandoffCounter(AggregatorOne, 1);
        var rightAggregator = new HandoffCounter(AggregatorTwo, 1);
        var root = new HandoffCounter(Root, 2);

        leftAggregator.Merge(a);
        a.Merge(leftAggregator);
        leftAggregator.Merge(b);
        b.Merge(leftAggregator);
        rightAggregator.Merge(c);
        c.Merge(rightAggregator);
        rightAggregator.Merge(d);
        d.Merge(rightAggregator);

        root.Merge(rightAggregator);
        root.Merge(leftAggregator);
        leftAggregator.Merge(root);
        rightAggregator.Merge(root);
        root.Merge(leftAggregator);
        root.Merge(rightAggregator);
        a.Merge(root);
        b.Merge(root);
        c.Merge(root);
        d.Merge(root);

        await Assert.That(root.Value).IsEqualTo(28UL);
        await Assert.That(leftAggregator.Value).IsEqualTo(28UL);
        await Assert.That(rightAggregator.Value).IsEqualTo(28UL);
        await Assert.That(a.Value).IsEqualTo(28UL);
        await Assert.That(b.Value).IsEqualTo(28UL);
        await Assert.That(c.Value).IsEqualTo(28UL);
        await Assert.That(d.Value).IsEqualTo(28UL);

        HandoffCounter stable = root.Clone();
        root.Merge(leftAggregator);
        root.Merge(rightAggregator);
        root.Merge(root);

        await Assert.That(root).IsEqualTo(stable);
        await Assert.That(root.Value).IsEqualTo(28UL);
    }

    [Test]
    public async Task Merge_Repeated_Handoff_From_Same_Client_Advances_And_Compacts()
    {
        var client = Client(A, 3);
        var server = new HandoffCounter(AggregatorOne, 1);

        server.Merge(client);
        client.Merge(server);

        await Assert.That(server.Value).IsEqualTo(3UL);
        await Assert.That(server.AggregatedValue).IsEqualTo(3UL);
        await Assert.That(client.UnhandedCount).IsEqualTo(0);
        await Assert.That(server.SlotCount).IsEqualTo(0);

        client.Increment(5);
        server.Merge(client);
        client.Merge(server);
        server.Merge(client);

        await Assert.That(server.Value).IsEqualTo(8UL);
        await Assert.That(server.AggregatedValue).IsEqualTo(8UL);
        await Assert.That(client.Value).IsEqualTo(8UL);
        await Assert.That(client.UnhandedCount).IsEqualTo(0);
        await Assert.That(server.SlotCount).IsEqualTo(0);
        await Assert.That(client.TokenCount).IsEqualTo(0);
    }

    [Test]
    public async Task Merge_With_Self_Is_Idempotent()
    {
        var counter = Client(A, 9);
        ulong value = counter.Value;

        counter.Merge(counter);

        await Assert.That(counter.Value).IsEqualTo(value);
        await Assert.That(counter.Compare(counter.Clone())).IsEqualTo(CrdtOrder.Equal);
    }

    [Test]
    public async Task Merge_Order_Is_Commutative_For_Independent_States()
    {
        var leftFirst = Client(A, 2);
        var right = Client(B, 5);
        var rightFirst = right.Clone();

        leftFirst.Merge(right);
        rightFirst.Merge(Client(A, 2));

        await Assert.That(leftFirst.Compare(rightFirst)).IsEqualTo(CrdtOrder.Equal);
        await Assert.That(leftFirst.Value).IsEqualTo(7UL);
        await Assert.That(rightFirst.Value).IsEqualTo(7UL);
    }

    [Test]
    public async Task Merge_Grouping_Is_Associative_For_Three_Nodes()
    {
        var a = Client(A, 2);
        var b = Client(B, 3);
        var c = Client(C, 5);
        var left = a.Clone();
        var right = a.Clone();
        var grouped = b.Clone();

        left.Merge(b);
        left.Merge(c);
        grouped.Merge(c);
        right.Merge(grouped);

        await Assert.That(left.Compare(right)).IsEqualTo(CrdtOrder.Equal);
        await Assert.That(left.Value).IsEqualTo(10UL);
        await Assert.That(right.Value).IsEqualTo(10UL);
    }

    [Test]
    public async Task Serialization_Roundtrips_State_With_Non_Empty_Slots_And_Tokens()
    {
        HandoffCounter state = ProtocolStateWithSlotsAndTokens();

        HandoffCounter fromBinary = HandoffCounter.ReadFrom(state.ToByteArray());
        HandoffCounter fromJson = HandoffCounter.FromJson(state.ToJson());

        await Assert.That(state.SlotCount).IsEqualTo(2);
        await Assert.That(state.TokenCount).IsEqualTo(2);
        await Assert.That(fromBinary).IsEqualTo(state);
        await Assert.That(fromJson).IsEqualTo(state);
        await Assert.That(fromBinary.Value).IsEqualTo(21UL);
        await Assert.That(fromJson.Value).IsEqualTo(21UL);
    }

    [Test]
    public async Task Merge_Chooses_Highest_Clock_Slot_And_Highest_Value_Token()
    {
        HandoffCounter state = ProtocolStateWithSlotsAndTokens();
        HandoffCounter newer = ProtocolStateWithNewerSlotAndToken();

        state.Merge(newer);

        KeyValuePair<ReplicaId, HandoffSlot> slot = state.SortedSlots().Single(x => x.Key == A);
        KeyValuePair<ReplicaId, HandoffToken> token = state.SortedTokens().Single(x => x.Key == Root);

        await Assert.That(slot.Value.DestinationClock).IsEqualTo(12UL);
        await Assert.That(slot.Value.SourceClock).IsEqualTo(101UL);
        await Assert.That(token.Value.Value).IsEqualTo(30UL);
        await Assert.That(token.Value.DestinationClock).IsEqualTo(15UL);
        await Assert.That(state.Value).IsEqualTo(44UL);
    }

    [Test]
    public async Task Equality_And_HashCode_Reflect_All_State()
    {
        HandoffCounter state = ProtocolStateWithSlotsAndTokens();
        HandoffCounter equal = HandoffCounter.FromJson(state.ToJson());
        HandoffCounter differentValue = ProtocolStateWithNewerSlotAndToken();
        object differentType = "not a handoff counter";

        await Assert.That(state.Equals(equal)).IsTrue();
        await Assert.That(state.Equals((object)equal)).IsTrue();
        await Assert.That(state.GetHashCode()).IsEqualTo(equal.GetHashCode());
        await Assert.That(state.Equals(differentValue)).IsFalse();
        await Assert.That(object.Equals(state, null)).IsFalse();
        await Assert.That(object.Equals(state, differentType)).IsFalse();
    }

    [Test]
    public async Task Constructor_Increment_Merge_Compare_And_Json_Guards_Throw()
    {
        var counter = new HandoffCounter(A, 0);

        await Assert.That(() => new HandoffCounter(A, -1)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => counter.Increment(0)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => counter.Merge(null!)).Throws<ArgumentNullException>();
        await Assert.That(() => counter.Compare(null!)).Throws<ArgumentNullException>();
        await Assert.That(() => HandoffCounter.FromJson(null!)).Throws<ArgumentNullException>();
        await Assert.That(() => HandoffCounter.FromJson("null")).Throws<FormatException>();
    }

    [Test]
    public async Task ReadFrom_Rejects_Invalid_Format_Version()
    {
        byte[] invalid = [0];

        await Assert.That(() => HandoffCounter.ReadFrom(invalid)).Throws<FormatException>();
    }

    [Test]
    public async Task Internal_Slot_And_Token_Value_Objects_Compare_And_Hash()
    {
        var slot = new HandoffSlot(0, 5, 7);
        var sameSlot = new HandoffSlot(0, 5, 7);
        var newerSlot = new HandoffSlot(0, 6, 7);
        var token = new HandoffToken(1, 9, 2, 3);
        var sameToken = new HandoffToken(1, 9, 2, 3);
        var largerToken = new HandoffToken(1, 10, 1, 1);

        await Assert.That(slot.Equals(sameSlot)).IsTrue();
        await Assert.That(slot.Equals((object)sameSlot)).IsTrue();
        await Assert.That(slot.Equals("slot")).IsFalse();
        await Assert.That(slot.GetHashCode()).IsEqualTo(sameSlot.GetHashCode());
        await Assert.That(newerSlot.CompareTo(slot)).IsGreaterThan(0);
        await Assert.That(token.Equals(sameToken)).IsTrue();
        await Assert.That(token.Equals((object)sameToken)).IsTrue();
        await Assert.That(token.Equals("token")).IsFalse();
        await Assert.That(token.GetHashCode()).IsEqualTo(sameToken.GetHashCode());
        await Assert.That(largerToken.CompareTo(token)).IsGreaterThan(0);
    }

    private static HandoffCounter Client(ReplicaId id, ulong amount)
    {
        var counter = new HandoffCounter(id, 0);
        counter.Increment(amount);
        return counter;
    }

    private static HandoffCounter ProtocolStateWithSlotsAndTokens()
    {
        var dto = new HandoffCounterDto(
            AggregatorOne,
            1,
            21,
            [new HandoffCounterEntryDto(AggregatorOne, 4)],
            [new HandoffCounterEntryDto(A, 7), new HandoffCounterEntryDto(B, 10)],
            [
                new HandoffCounterSlotDto(A, 0, 100, 10),
                new HandoffCounterSlotDto(B, 0, 100, 11),
            ],
            [
                new HandoffCounterTokenDto(Root, 2, 18, 9, 10),
                new HandoffCounterTokenDto(AggregatorTwo, 1, 12, 8, 9),
            ],
            3,
            11);

        return FromDto(dto);
    }

    private static HandoffCounter ProtocolStateWithNewerSlotAndToken()
    {
        var dto = new HandoffCounterDto(
            AggregatorOne,
            1,
            30,
            [],
            [new HandoffCounterEntryDto(A, 30)],
            [new HandoffCounterSlotDto(A, 0, 101, 12)],
            [new HandoffCounterTokenDto(Root, 2, 30, 14, 15)],
            5,
            12);

        return FromDto(dto);
    }

    private static HandoffCounter FromDto(HandoffCounterDto dto)
    {
        string json = JsonSerializer.Serialize(dto, CrdtHandoffCounterJson.Default.HandoffCounterDto);
        return HandoffCounter.FromJson(json);
    }
}
