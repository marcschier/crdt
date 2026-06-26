// Copyright (c) marcschier. Licensed under the MIT License.

using Crdt.Tests.Testing;

namespace Crdt.Tests.Flags;

public sealed class EnableWinsFlagTests
{
    private static readonly ReplicaId A = ReplicaId.FromUInt64(1);
    private static readonly ReplicaId B = ReplicaId.FromUInt64(2);
    private static readonly ReplicaId C = ReplicaId.FromUInt64(3);

    [Test]
    public async Task Enable_Then_Disable_Toggles_Value()
    {
        var flag = new EnableWinsFlag();

        flag.Enable(A);
        await Assert.That(flag.Value).IsTrue();

        flag.Disable(A);
        await Assert.That(flag.Value).IsFalse();
    }

    [Test]
    public async Task Concurrent_Enable_Wins_Over_Disable()
    {
        var left = new EnableWinsFlag();
        left.Enable(A);
        EnableWinsFlag right = left.Clone();

        left.Disable(A);
        right.Enable(B);

        left.Merge(right);
        right.Merge(left);

        await Assert.That(left.Value).IsTrue();
        await Assert.That(right.Value).IsTrue();
        await Assert.That(left).IsEqualTo(right);
    }

    [Test]
    public void Satisfies_Semilattice_Laws()
    {
        CrdtLaws.AssertSemilattice(Sample(A), Sample(B), Sample(A, C));
    }

    [Test]
    public void Merge_Converges_In_Different_Orders()
    {
        CrdtLaws.AssertConverges(
            new[] { Sample(A), Sample(B), DisabledAfterEnable(C) },
            static (x, y) => x.Equals(y));
    }

    [Test]
    public async Task Delta_Carries_Enable_And_Disable()
    {
        var source = new EnableWinsFlag();
        source.Enable(A);
        bool enableExtracted = source.TryExtractDelta(out EnableWinsFlag? enableDelta);

        var target = new EnableWinsFlag();
        target.MergeDelta(enableDelta!);

        source.Disable(A);
        bool disableExtracted = source.TryExtractDelta(out EnableWinsFlag? disableDelta);
        target.MergeDelta(disableDelta!);

        await Assert.That(enableExtracted).IsTrue();
        await Assert.That(disableExtracted).IsTrue();
        await Assert.That(target.Value).IsFalse();
    }

    [Test]
    public async Task Operations_Converge_Under_Reordering_And_Duplication()
    {
        var r0 = new EnableWinsFlag();
        var r1 = new EnableWinsFlag();
        var r2 = new EnableWinsFlag();
        var sim = new OperationDeliverySimulator<EnableWinsFlag, EnableWinsFlagOperation>(r0, r1, r2);

        sim.Broadcast(0, r0.Enable(A));
        sim.Broadcast(0, r0.Disable(A));
        sim.Broadcast(1, r1.Enable(B));

        sim.DeliverAll(seed: 29, duplicate: true);

        sim.AssertConverged(static (x, y) => x.Equals(y));
        await AssertEnabled(sim);
    }

    [Test]
    public async Task Binary_Roundtrips()
    {
        var flag = Sample(A, B);
        flag.Disable(C);
        flag.Enable(C);

        EnableWinsFlag restored = EnableWinsFlag.ReadFrom(flag.ToByteArray());
        await Assert.That(restored).IsEqualTo(flag);
    }

    [Test]
    public async Task Json_Roundtrips()
    {
        var flag = Sample(A, B);
        flag.Disable(C);
        flag.Enable(C);

        EnableWinsFlag restored = EnableWinsFlag.FromJson(flag.ToJson());
        await Assert.That(restored).IsEqualTo(flag);
    }

    private static EnableWinsFlag Sample(params ReplicaId[] replicas)
    {
        var flag = new EnableWinsFlag();
        foreach (ReplicaId replica in replicas)
        {
            flag.Enable(replica);
        }

        return flag;
    }

    private static EnableWinsFlag DisabledAfterEnable(ReplicaId replica)
    {
        var flag = new EnableWinsFlag();
        flag.Enable(replica);
        flag.Disable(replica);
        return flag;
    }

    private static async Task AssertEnabled(OperationDeliverySimulator<EnableWinsFlag, EnableWinsFlagOperation> sim)
    {
        for (int i = 0; i < sim.Count; i++)
        {
            await Assert.That(sim[i].Value).IsTrue();
        }
    }
}
