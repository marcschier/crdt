// Copyright (c) marcschier. Licensed under the MIT License.

using Crdt.Tests.Testing;

namespace Crdt.Tests.Sequences;

public sealed class RgaTests
{
    private static readonly ReplicaId A = ReplicaId.FromUInt64(1);
    private static readonly ReplicaId B = ReplicaId.FromUInt64(2);

    private static string Render(Rga<string> rga) => string.Join("", rga.ToArray());

    [Test]
    public async Task Insert_And_Append_Build_Sequence()
    {
        var rga = new Rga<string>();
        rga.Append(A, "a");
        rga.Append(A, "c");
        rga.Insert(A, 1, "b");

        await Assert.That(Render(rga)).IsEqualTo("abc");
        await Assert.That(rga.Count).IsEqualTo(3);
        await Assert.That(rga[1]).IsEqualTo("b");
    }

    [Test]
    public async Task Delete_Removes_Visible_Element()
    {
        var rga = new Rga<string>();
        rga.Append(A, "a");
        rga.Append(A, "b");
        rga.Append(A, "c");
        rga.Delete(1);

        await Assert.That(Render(rga)).IsEqualTo("ac");
    }

    [Test]
    public async Task Insert_Out_Of_Bounds_Throws()
    {
        var rga = new Rga<string>();
        await Assert.That(() => rga.Insert(A, 5, "x")).Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task Concurrent_Inserts_Converge_Deterministically()
    {
        var left = new Rga<string>();
        left.Append(A, "x");

        Rga<string> right = left.Clone();

        // Concurrent inserts at the same position by different replicas.
        left.Insert(A, 1, "a");
        right.Insert(B, 1, "b");

        Rga<string> leftMerged = left.Clone();
        leftMerged.Merge(right);
        Rga<string> rightMerged = right.Clone();
        rightMerged.Merge(left);

        // Both replicas converge to the same sequence regardless of merge direction.
        await Assert.That(Render(leftMerged)).IsEqualTo(Render(rightMerged));
        await Assert.That(leftMerged.Count).IsEqualTo(3);
    }

    [Test]
    public void Satisfies_Semilattice_Laws()
    {
        CrdtLaws.AssertSemilattice(Sample(A, "ab"), Sample(B, "cd"), Sample(A, "ef"));
    }

    [Test]
    public async Task Delta_Carries_Local_Changes()
    {
        var source = new Rga<string>();
        source.Append(A, "a");
        source.Append(A, "b");

        bool extracted = source.TryExtractDelta(out Rga<string>? delta);
        var target = new Rga<string>();
        target.MergeDelta(delta!);

        await Assert.That(extracted).IsTrue();
        await Assert.That(Render(target)).IsEqualTo("ab");
    }

    [Test]
    public void Operations_Converge_Under_Reordering_And_Duplication()
    {
        var r0 = new Rga<string>();
        var r1 = new Rga<string>();
        var sim = new OperationDeliverySimulator<Rga<string>, RgaOperation<string>>(r0, r1);

        sim.Broadcast(0, r0.Append(A, "h"));
        sim.Broadcast(0, r0.Append(A, "i"));
        sim.Broadcast(1, r1.Append(B, "y"));

        sim.DeliverAll(seed: 11, duplicate: true);

        sim.AssertConverged(static (x, y) => x.Equals(y));
    }

    [Test]
    public async Task Apply_Tolerates_Delete_Before_Insert()
    {
        var rga = new Rga<string>();
        RgaOperation<string> insert = new Rga<string>().Append(A, "x");

        // Deliver the delete for a not-yet-known element first, then the insert.
        rga.Apply(RgaOperation<string>.Delete(insert.Id));
        rga.Apply(insert);

        // The element is present structurally but tombstoned, so it is not visible.
        await Assert.That(rga.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Apply_Tolerates_Child_Before_Parent()
    {
        // Build two linked inserts on a source replica.
        var source = new Rga<string>();
        RgaOperation<string> first = source.Append(A, "a");
        RgaOperation<string> second = source.Append(A, "b");

        var target = new Rga<string>();
        // Deliver the child (second) before its parent (first).
        target.Apply(second);
        await Assert.That(target.Count).IsEqualTo(0); // not reachable from root yet

        target.Apply(first);
        await Assert.That(string.Join("", target.ToArray())).IsEqualTo("ab");
    }

    [Test]
    public async Task Binary_Roundtrips()
    {
        var rga = new Rga<string>();
        rga.Append(A, "a");
        rga.Append(A, "b");
        rga.Delete(0);

        Rga<string> restored = Rga<string>.ReadFrom(rga.ToByteArray(CrdtValues.String), CrdtValues.String);
        await Assert.That(restored).IsEqualTo(rga);
        await Assert.That(Render(restored)).IsEqualTo("b");
    }

    [Test]
    public async Task Json_Roundtrips()
    {
        var rga = new Rga<string>();
        rga.Append(A, "hello");
        rga.Append(A, "world");

        Rga<string> restored = Rga<string>.FromJson(rga.ToJson(CrdtValues.String), CrdtValues.String);
        await Assert.That(Render(restored)).IsEqualTo(Render(rga));
    }

    private static Rga<string> Sample(ReplicaId replica, string letters)
    {
        var rga = new Rga<string>();
        foreach (char letter in letters)
        {
            rga.Append(replica, letter.ToString());
        }

        return rga;
    }
}
