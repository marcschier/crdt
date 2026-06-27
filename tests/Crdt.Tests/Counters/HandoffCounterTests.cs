// Copyright (c) marcschier. Licensed under the MIT License.

using Crdt.Tests.Testing;

namespace Crdt.Tests.Counters;

public sealed class HandoffCounterTests
{
    private static readonly ReplicaId A = ReplicaId.FromUInt64(1);
    private static readonly ReplicaId B = ReplicaId.FromUInt64(2);
    private static readonly ReplicaId C = ReplicaId.FromUInt64(3);
    private static readonly ReplicaId S = ReplicaId.FromUInt64(10);

    [Test]
    public async Task Increment_Increases_Local_Value()
    {
        var counter = new HandoffCounter(A, 0);

        counter.Increment(4);
        counter.Increment();

        await Assert.That(counter.Value).IsEqualTo(5UL);
        await Assert.That(counter.TotalFor(A)).IsEqualTo(5UL);
    }

    [Test]
    public async Task Increment_By_Zero_Throws()
    {
        var counter = new HandoffCounter(A, 0);

        await Assert.That(() => counter.Increment(0)).Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task Merge_With_Server_In_Various_Orders_Converges_To_Total()
    {
        var a = new HandoffCounter(A, 0);
        var b = new HandoffCounter(B, 0);
        var c = new HandoffCounter(C, 0);
        var server = new HandoffCounter(S, 1);
        a.Increment(5);
        b.Increment(7);
        c.Increment(11);

        server.Merge(b);
        server.Merge(a);
        server.Merge(b);
        a.Merge(server);
        server.Merge(c);
        b.Merge(server);
        c.Merge(server);
        server.Merge(a);
        server.Merge(c);
        a.Merge(server);
        b.Merge(server);
        c.Merge(server);

        await Assert.That(server.Value).IsEqualTo(23UL);
        await Assert.That(a.Value).IsEqualTo(23UL);
        await Assert.That(b.Value).IsEqualTo(23UL);
        await Assert.That(c.Value).IsEqualTo(23UL);
    }

    [Test]
    public async Task Merge_Handoff_Bounds_Client_Unhanded_State()
    {
        var client = new HandoffCounter(A, 0);
        var server = new HandoffCounter(S, 1);
        client.Increment(9);

        server.Merge(client);
        client.Merge(server);

        await Assert.That(server.AggregatedValue).IsEqualTo(9UL);
        await Assert.That(client.UnhandedCount).IsEqualTo(0);
        await Assert.That(client.Value).IsEqualTo(9UL);

        client.Increment(4);
        server.Merge(client);
        client.Merge(server);

        await Assert.That(server.AggregatedValue).IsEqualTo(13UL);
        await Assert.That(client.UnhandedCount).IsEqualTo(0);
        await Assert.That(client.Value).IsEqualTo(13UL);
    }

    [Test]
    public void Merge_Is_Idempotent_Commutative_And_Associative()
    {
        CrdtLaws.AssertSemilattice(
            Sample(A, 0, 3),
            Sample(B, 0, 5),
            Sample(S, 1, 7));
    }

    [Test]
    public async Task Merge_Is_Idempotent_With_Duplicated_Deliveries()
    {
        var client = Sample(A, 0, 6);
        var server = new HandoffCounter(S, 1);

        server.Merge(client);
        HandoffCounter once = server.Clone();
        server.Merge(client);

        await Assert.That(server.Compare(once)).IsEqualTo(CrdtOrder.Equal);
        await Assert.That(server.Value).IsEqualTo(6UL);
    }

    [Test]
    public async Task Compare_Reflects_Logical_Dominance()
    {
        var baseline = Sample(A, 0, 2);
        var greater = Sample(A, 0, 3);
        var concurrent = Sample(B, 0, 1);

        await Assert.That(baseline.Compare(greater)).IsEqualTo(CrdtOrder.Less);
        await Assert.That(greater.Compare(baseline)).IsEqualTo(CrdtOrder.Greater);
        await Assert.That(baseline.Compare(concurrent)).IsEqualTo(CrdtOrder.Concurrent);
        await Assert.That(baseline.Compare(baseline.Clone())).IsEqualTo(CrdtOrder.Equal);
    }

    [Test]
    public async Task Binary_Roundtrips()
    {
        var counter = new HandoffCounter(S, 1);
        counter.Merge(Sample(A, 0, 7));
        counter.Merge(Sample(B, 0, 4));

        HandoffCounter restored = HandoffCounter.ReadFrom(counter.ToByteArray());

        await Assert.That(restored).IsEqualTo(counter);
        await Assert.That(restored.Value).IsEqualTo(11UL);
    }

    [Test]
    public async Task Json_Roundtrips()
    {
        var counter = new HandoffCounter(S, 1);
        counter.Merge(Sample(A, 0, 7));
        counter.Merge(Sample(B, 0, 4));

        HandoffCounter restored = HandoffCounter.FromJson(counter.ToJson());

        await Assert.That(restored).IsEqualTo(counter);
        await Assert.That(restored.Value).IsEqualTo(11UL);
    }

    private static HandoffCounter Sample(ReplicaId replica, int tier, ulong amount)
    {
        var counter = new HandoffCounter(replica, tier);
        counter.Increment(amount);
        return counter;
    }
}
