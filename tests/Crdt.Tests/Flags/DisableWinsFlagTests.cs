// Copyright (c) marcschier. Licensed under the MIT License.

using Crdt.Tests.Testing;

namespace Crdt.Tests.Flags;

public sealed class DisableWinsFlagTests
{
    private static readonly ReplicaId A = ReplicaId.FromUInt64(1);
    private static readonly ReplicaId B = ReplicaId.FromUInt64(2);
    private static readonly ReplicaId C = ReplicaId.FromUInt64(3);

    [Test]
    public async Task Default_Is_True()
    {
        var flag = new DisableWinsFlag();
        await Assert.That(flag.Value).IsTrue();
    }

    [Test]
    public async Task Disable_Then_Enable_Toggles_Value()
    {
        var flag = new DisableWinsFlag();

        flag.Disable(A);
        await Assert.That(flag.Value).IsFalse();

        flag.Enable(A);
        await Assert.That(flag.Value).IsTrue();
    }

    [Test]
    public async Task Concurrent_Disable_Wins_Over_Enable()
    {
        var left = new DisableWinsFlag();
        left.Disable(A);
        DisableWinsFlag right = left.Clone();

        left.Enable(A);
        right.Disable(B);

        left.Merge(right);
        right.Merge(left);

        await Assert.That(left.Value).IsFalse();
        await Assert.That(right.Value).IsFalse();
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
            new[] { Sample(A), Sample(B), EnabledAfterDisable(C) },
            static (x, y) => x.Equals(y));
    }

    [Test]
    public async Task Delta_Carries_Disable_And_Enable()
    {
        var source = new DisableWinsFlag();
        source.Disable(A);
        bool disableExtracted = source.TryExtractDelta(out DisableWinsFlag? disableDelta);

        var target = new DisableWinsFlag();
        target.MergeDelta(disableDelta!);

        source.Enable(A);
        bool enableExtracted = source.TryExtractDelta(out DisableWinsFlag? enableDelta);
        target.MergeDelta(enableDelta!);

        await Assert.That(disableExtracted).IsTrue();
        await Assert.That(enableExtracted).IsTrue();
        await Assert.That(target.Value).IsTrue();
    }

    [Test]
    public async Task Operations_Converge_Under_Reordering_And_Duplication()
    {
        var r0 = new DisableWinsFlag();
        var r1 = new DisableWinsFlag();
        var r2 = new DisableWinsFlag();
        var sim = new OperationDeliverySimulator<DisableWinsFlag, DisableWinsFlagOperation>(r0, r1, r2);

        sim.Broadcast(0, r0.Disable(A));
        sim.Broadcast(0, r0.Enable(A));
        sim.Broadcast(1, r1.Disable(B));

        sim.DeliverAll(seed: 31, duplicate: true);

        sim.AssertConverged(static (x, y) => x.Equals(y));
        await AssertDisabled(sim);
    }

    [Test]
    public async Task Binary_Roundtrips()
    {
        var flag = Sample(A, B);
        flag.Enable(C);
        flag.Disable(C);

        DisableWinsFlag restored = DisableWinsFlag.ReadFrom(flag.ToByteArray());
        await Assert.That(restored).IsEqualTo(flag);
    }

    [Test]
    public async Task Json_Roundtrips()
    {
        var flag = Sample(A, B);
        flag.Enable(C);
        flag.Disable(C);

        DisableWinsFlag restored = DisableWinsFlag.FromJson(flag.ToJson());
        await Assert.That(restored).IsEqualTo(flag);
    }

    private static DisableWinsFlag Sample(params ReplicaId[] replicas)
    {
        var flag = new DisableWinsFlag();
        foreach (ReplicaId replica in replicas)
        {
            flag.Disable(replica);
        }

        return flag;
    }

    private static DisableWinsFlag EnabledAfterDisable(ReplicaId replica)
    {
        var flag = new DisableWinsFlag();
        flag.Disable(replica);
        flag.Enable(replica);
        return flag;
    }

    private static async Task AssertDisabled(OperationDeliverySimulator<DisableWinsFlag, DisableWinsFlagOperation> sim)
    {
        for (int i = 0; i < sim.Count; i++)
        {
            await Assert.That(sim[i].Value).IsFalse();
        }
    }
}
