// Copyright (c) marcschier. Licensed under the MIT License.

using Crdt.Tests.Testing;

namespace Crdt.Tests.Flags;

public sealed class GFlagTests
{
    [Test]
    public async Task Default_Is_False()
    {
        var flag = new GFlag();
        await Assert.That(flag.Value).IsFalse();
    }

    [Test]
    public async Task Enable_Makes_Value_True()
    {
        var flag = new GFlag();
        flag.Enable();
        await Assert.That(flag.Value).IsTrue();
    }

    [Test]
    public async Task Merge_Uses_Logical_Or()
    {
        var left = new GFlag();
        var right = new GFlag();
        right.Enable();

        left.Merge(right);

        await Assert.That(left.Value).IsTrue();
    }

    [Test]
    public void Satisfies_Semilattice_Laws()
    {
        CrdtLaws.AssertSemilattice(new GFlag(), Enabled(), Enabled());
    }

    [Test]
    public async Task Delta_Carries_Enable()
    {
        var source = new GFlag();
        source.Enable();

        bool extracted = source.TryExtractDelta(out GFlag? delta);
        var target = new GFlag();
        target.MergeDelta(delta!);

        await Assert.That(extracted).IsTrue();
        await Assert.That(target.Value).IsTrue();
        await Assert.That(source.TryExtractDelta(out _)).IsFalse();
    }

    [Test]
    public void Operations_Converge_Under_Reordering_And_Duplication()
    {
        var r0 = new GFlag();
        var r1 = new GFlag();
        var r2 = new GFlag();
        var sim = new OperationDeliverySimulator<GFlag, GFlagOperation>(r0, r1, r2);

        sim.Broadcast(0, r0.Enable());

        sim.DeliverAll(seed: 17, duplicate: true);

        sim.AssertConverged(static (x, y) => x.Equals(y));
    }

    [Test]
    public async Task Binary_Roundtrips()
    {
        var flag = Enabled();
        GFlag restored = GFlag.ReadFrom(flag.ToByteArray());
        await Assert.That(restored).IsEqualTo(flag);
    }

    [Test]
    public async Task Json_Roundtrips()
    {
        var flag = Enabled();
        GFlag restored = GFlag.FromJson(flag.ToJson());
        await Assert.That(restored).IsEqualTo(flag);
    }

    [Test]
    public async Task Compare_Reflects_False_Before_True()
    {
        var disabled = new GFlag();
        var enabled = Enabled();

        await Assert.That(disabled.Compare(enabled)).IsEqualTo(CrdtOrder.Less);
        await Assert.That(enabled.Compare(disabled)).IsEqualTo(CrdtOrder.Greater);
        await Assert.That(enabled.Compare(enabled.Clone())).IsEqualTo(CrdtOrder.Equal);
    }

    private static GFlag Enabled()
    {
        var flag = new GFlag();
        flag.Enable();
        return flag;
    }
}
