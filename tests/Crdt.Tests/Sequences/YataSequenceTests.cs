// Copyright (c) marcschier. Licensed under the MIT License.

using Crdt.Tests.Testing;

namespace Crdt.Tests.Sequences;

public sealed class YataSequenceTests
{
    private static readonly ReplicaId A = ReplicaId.FromUInt64(1);
    private static readonly ReplicaId B = ReplicaId.FromUInt64(2);
    private static readonly ReplicaId C = ReplicaId.FromUInt64(3);

    private static string Render(YataSequence<string> sequence) => string.Join("", sequence.ToArray());

    [Test]
    public async Task Insert_Append_And_Delete_Build_Sequence()
    {
        var sequence = new YataSequence<string>();
        sequence.Append(A, "a");
        sequence.Append(A, "c");
        sequence.Insert(A, 1, "b");
        sequence.Delete(1);
        sequence.Insert(A, 1, "b");

        string rendered = Render(sequence);
        int count = sequence.Count;
        string middle = sequence[1];

        await Assert.That(rendered).IsEqualTo("abc");
        await Assert.That(count).IsEqualTo(3);
        await Assert.That(middle).IsEqualTo("b");
    }

    [Test]
    public async Task Concurrent_Inserts_At_Same_Position_Converge()
    {
        var left = new YataSequence<string>();
        left.Append(A, "x");
        YataSequence<string> right = left.Clone();

        left.Insert(A, 1, "a");
        right.Insert(B, 1, "b");

        YataSequence<string> leftMerged = left.Clone();
        leftMerged.Merge(right);
        YataSequence<string> rightMerged = right.Clone();
        rightMerged.Merge(left);

        string leftText = Render(leftMerged);
        string rightText = Render(rightMerged);
        int count = leftMerged.Count;

        await Assert.That(leftText).IsEqualTo(rightText);
        await Assert.That(count).IsEqualTo(3);
    }

    [Test]
    public async Task Multi_Replica_Interleaving_Converges()
    {
        var baseSequence = new YataSequence<string>();
        baseSequence.Append(A, "a");
        YataSequence<string> first = baseSequence.Clone();
        YataSequence<string> second = baseSequence.Clone();
        YataSequence<string> third = baseSequence.Clone();

        first.Insert(A, 1, "b");
        second.Insert(B, 1, "c");
        third.Append(C, "d");

        YataSequence<string> mergedOne = first.Clone();
        mergedOne.Merge(second);
        mergedOne.Merge(third);
        YataSequence<string> mergedTwo = third.Clone();
        mergedTwo.Merge(first);
        mergedTwo.Merge(second);

        string one = Render(mergedOne);
        string two = Render(mergedTwo);

        await Assert.That(one).IsEqualTo(two);
        await Assert.That(one.Length).IsEqualTo(4);
    }

    [Test]
    public void Satisfies_Semilattice_Laws()
    {
        CrdtLaws.AssertSemilattice(
            Sample(A, "ab"),
            Sample(B, "cd"),
            Sample(C, "ef"),
            static (x, y) => Render(x) == Render(y));
    }

    [Test]
    public async Task Delta_Carries_Local_Changes()
    {
        var source = new YataSequence<string>();
        source.Append(A, "a");
        source.Append(A, "b");
        source.Delete(0);

        bool extracted = source.TryExtractDelta(out YataSequence<string>? delta);
        var target = new YataSequence<string>();
        target.MergeDelta(delta!);
        string rendered = Render(target);

        await Assert.That(extracted).IsTrue();
        await Assert.That(rendered).IsEqualTo("b");
    }

    [Test]
    public void Operations_Converge_Under_Reordering_And_Duplication()
    {
        var first = new YataSequence<string>();
        var second = new YataSequence<string>();
        var sim = new OperationDeliverySimulator<YataSequence<string>, YataOperation<string>>(first, second);

        sim.Broadcast(0, first.Append(A, "h"));
        sim.Broadcast(0, first.Append(A, "i"));
        sim.Broadcast(1, second.Append(B, "y"));

        sim.DeliverAll(seed: 21, duplicate: true);

        sim.AssertConverged(static (x, y) => Render(x) == Render(y));
    }

    [Test]
    public async Task Apply_Tolerates_Delete_Before_Insert()
    {
        YataOperation<string> insert = new YataSequence<string>().Append(A, "x");
        var target = new YataSequence<string>();

        target.Apply(YataOperation<string>.Delete(insert.Id));
        target.Apply(insert);
        int count = target.Count;

        await Assert.That(count).IsEqualTo(0);
    }

    [Test]
    public async Task Apply_Tolerates_Child_Before_Origins()
    {
        var source = new YataSequence<string>();
        YataOperation<string> first = source.Append(A, "a");
        YataOperation<string> second = source.Append(A, "b");
        var target = new YataSequence<string>();

        target.Apply(second);
        int countBefore = target.Count;
        target.Apply(first);
        string rendered = Render(target);

        await Assert.That(countBefore).IsEqualTo(0);
        await Assert.That(rendered).IsEqualTo("ab");
    }

    [Test]
    public async Task Binary_Roundtrips()
    {
        var sequence = new YataSequence<string>();
        sequence.Append(A, "a");
        sequence.Append(A, "b");
        sequence.Delete(0);

        byte[] data = sequence.ToByteArray(CrdtValues.String);
        YataSequence<string> restored = YataSequence<string>.ReadFrom(data, CrdtValues.String);
        string rendered = Render(restored);

        await Assert.That(restored).IsEqualTo(sequence);
        await Assert.That(rendered).IsEqualTo("b");
    }

    [Test]
    public async Task Json_Roundtrips()
    {
        var sequence = new YataSequence<string>();
        sequence.Append(A, "hello");
        sequence.Append(A, "world");
        sequence.Delete(0);

        string json = sequence.ToJson(CrdtValues.String);
        YataSequence<string> restored = YataSequence<string>.FromJson(json, CrdtValues.String);
        string restoredText = Render(restored);
        string originalText = Render(sequence);

        await Assert.That(restoredText).IsEqualTo(originalText);
    }

    private static YataSequence<string> Sample(ReplicaId replica, string letters)
    {
        var sequence = new YataSequence<string>();
        foreach (char letter in letters)
        {
            sequence.Append(replica, letter.ToString());
        }

        return sequence;
    }
}
