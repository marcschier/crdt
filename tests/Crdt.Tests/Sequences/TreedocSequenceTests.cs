// Copyright (c) marcschier. Licensed under the MIT License.

using Crdt.Tests.Testing;

namespace Crdt.Tests.Sequences;

public sealed class TreedocSequenceTests
{
    private static readonly ReplicaId A = ReplicaId.FromUInt64(1);
    private static readonly ReplicaId B = ReplicaId.FromUInt64(2);
    private static readonly ReplicaId C = ReplicaId.FromUInt64(3);

    private static string Render(TreedocSequence<string> sequence) => string.Join("", sequence.ToArray());

    [Test]
    public async Task Insert_And_Append_Build_Sequence()
    {
        var sequence = new TreedocSequence<string>();
        sequence.Append(A, "a");
        sequence.Append(A, "c");
        sequence.Insert(A, 1, "b");

        await Assert.That(Render(sequence)).IsEqualTo("abc");
        await Assert.That(sequence.Count).IsEqualTo(3);
        await Assert.That(sequence[1]).IsEqualTo("b");
    }

    [Test]
    public async Task Delete_Removes_Visible_Element()
    {
        var sequence = new TreedocSequence<string>();
        sequence.Append(A, "a");
        sequence.Append(A, "b");
        sequence.Append(A, "c");
        sequence.Delete(1);

        await Assert.That(Render(sequence)).IsEqualTo("ac");
    }

    [Test]
    public async Task Concurrent_Inserts_At_Same_Index_Converge()
    {
        var left = new TreedocSequence<string>();
        left.Append(A, "x");
        TreedocSequence<string> right = left.Clone();

        left.Insert(A, 1, "a");
        right.Insert(B, 1, "b");

        TreedocSequence<string> leftMerged = left.Clone();
        leftMerged.Merge(right);
        TreedocSequence<string> rightMerged = right.Clone();
        rightMerged.Merge(left);

        await Assert.That(Render(leftMerged)).IsEqualTo(Render(rightMerged));
        await Assert.That(leftMerged.Count).IsEqualTo(3);
    }

    [Test]
    public async Task Interleaved_Concurrent_Inserts_Converge()
    {
        var left = new TreedocSequence<string>();
        var right = new TreedocSequence<string>();
        left.Append(A, "a");
        right.Append(B, "b");
        left.Insert(A, 0, "c");
        right.Insert(B, 0, "d");

        TreedocSequence<string> leftMerged = left.Clone();
        leftMerged.Merge(right);
        TreedocSequence<string> rightMerged = right.Clone();
        rightMerged.Merge(left);

        await Assert.That(Render(leftMerged)).IsEqualTo(Render(rightMerged));
        await Assert.That(leftMerged.Count).IsEqualTo(4);
    }

    [Test]
    public void Satisfies_Semilattice_Laws()
    {
        CrdtLaws.AssertSemilattice(
            Sample(A, "ab"),
            Sample(B, "cd"),
            Sample(C, "ef"),
            static (x, y) => string.Join("", x.ToArray()) == string.Join("", y.ToArray()));
    }

    [Test]
    public async Task Delta_Carries_Local_Changes()
    {
        var source = new TreedocSequence<string>();
        source.Append(A, "a");
        source.Append(A, "b");

        bool extracted = source.TryExtractDelta(out TreedocSequence<string>? delta);
        var target = new TreedocSequence<string>();
        target.MergeDelta(delta!);

        await Assert.That(extracted).IsTrue();
        await Assert.That(Render(target)).IsEqualTo("ab");
    }

    [Test]
    public void Operations_Converge_Under_Reordering_And_Duplication()
    {
        var r0 = new TreedocSequence<string>();
        var r1 = new TreedocSequence<string>();
        var sim = new OperationDeliverySimulator<TreedocSequence<string>, TreedocOperation<string>>(r0, r1);

        sim.Broadcast(0, r0.Append(A, "h"));
        sim.Broadcast(0, r0.Append(A, "i"));
        sim.Broadcast(1, r1.Append(B, "y"));

        sim.DeliverAll(seed: 11, duplicate: true);
        sim.AssertConverged(static (x, y) => string.Join("", x.ToArray()) == string.Join("", y.ToArray()));
    }

    [Test]
    public async Task Binary_Roundtrips()
    {
        var sequence = new TreedocSequence<string>();
        sequence.Append(A, "a");
        sequence.Append(A, "b");
        sequence.Delete(0);

        TreedocSequence<string> restored = TreedocSequence<string>.ReadFrom(
            sequence.ToByteArray(CrdtValues.String),
            CrdtValues.String);

        await Assert.That(restored).IsEqualTo(sequence);
        await Assert.That(Render(restored)).IsEqualTo("b");
    }

    [Test]
    public async Task Json_Roundtrips()
    {
        var sequence = new TreedocSequence<string>();
        sequence.Append(A, "hello");
        sequence.Append(A, "world");

        TreedocSequence<string> restored = TreedocSequence<string>.FromJson(
            sequence.ToJson(CrdtValues.String),
            CrdtValues.String);

        await Assert.That(Render(restored)).IsEqualTo(Render(sequence));
    }

    private static TreedocSequence<string> Sample(ReplicaId replica, string letters)
    {
        var sequence = new TreedocSequence<string>();
        foreach (char letter in letters)
        {
            sequence.Append(replica, letter.ToString());
        }

        return sequence;
    }
}
