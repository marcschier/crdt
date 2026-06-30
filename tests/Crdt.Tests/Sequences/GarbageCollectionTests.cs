// Copyright (c) marcschier. Licensed under the MIT License.

namespace Crdt.Tests.Sequences;

public sealed class GarbageCollectionTests
{
    private static readonly ReplicaId A = ReplicaId.FromUInt64(1);

    [Test]
    public async Task Rga_CollectStable_Preserves_Value_And_Convergence()
    {
        var retained = new Rga<string>();
        retained.Append(A, "a");
        retained.Append(A, "b");
        retained.Delete(1);
        Rga<string> collected = retained.Clone();

        collected.CollectStable(StableCut.Meet([collected.ObservedVersion]));
        collected.CollectStable(StableCut.Meet([collected.ObservedVersion]));
        retained.Merge(collected);
        collected.Merge(retained);

        await Assert.That(string.Join("", collected.ToArray())).IsEqualTo("a");
        await Assert.That(string.Join("", retained.ToArray())).IsEqualTo("a");
    }

    [Test]
    public async Task Rga_CollectStable_Does_Not_Reclaim_Above_Cut()
    {
        var rga = new Rga<string>();
        rga.Append(A, "a");
        rga.Delete(0);
        string before = rga.ToJson(CrdtValues.String);

        rga.CollectStable(StableCut.Meet([]));

        await Assert.That(rga.ToJson(CrdtValues.String)).IsEqualTo(before);
    }

    [Test]
    public async Task Rga_CollectStable_Keeps_Deleted_Anchors()
    {
        var rga = new Rga<string>();
        rga.Append(A, "a");
        rga.Append(A, "b");
        rga.Delete(0);

        rga.CollectStable(StableCut.Meet([rga.ObservedVersion]));

        await Assert.That(rga.ToJson(CrdtValues.String).Contains("\"a\"")).IsTrue();
        await Assert.That(string.Join("", rga.ToArray())).IsEqualTo("b");
    }

    [Test]
    public async Task Woot_CollectStable_Reclaims_Stable_Deleted_Leaf()
    {
        var sequence = new WootSequence<string>();
        sequence.Append(A, "a");
        sequence.Append(A, "b");
        sequence.Delete(1);

        sequence.CollectStable(StableCut.Meet([sequence.ObservedVersion]));
        sequence.CollectStable(StableCut.Meet([sequence.ObservedVersion]));

        await Assert.That(sequence.ToJson(CrdtValues.String).Contains("\"b\"")).IsFalse();
        await Assert.That(string.Join("", sequence.ToArray())).IsEqualTo("a");
    }

    [Test]
    public async Task Yata_CollectStable_Reclaims_Stable_Deleted_Leaf()
    {
        var sequence = new YataSequence<string>();
        sequence.Append(A, "a");
        sequence.Append(A, "b");
        sequence.Delete(1);

        sequence.CollectStable(StableCut.Meet([sequence.ObservedVersion]));

        await Assert.That(sequence.ToJson(CrdtValues.String).Contains("\"b\"")).IsFalse();
        await Assert.That(string.Join("", sequence.ToArray())).IsEqualTo("a");
    }

    [Test]
    public async Task Fugue_CollectStable_Reclaims_Stable_Deleted_Leaf()
    {
        var sequence = new FugueSequence<string>(A);
        sequence.Append("a");
        sequence.Append("b");
        sequence.Delete(1);

        sequence.CollectStable(StableCut.Meet([sequence.ObservedVersion]));

        await Assert.That(sequence.ToJson(CrdtValues.String).Contains("\"b\"")).IsFalse();
        await Assert.That(string.Join("", sequence.ToArray())).IsEqualTo("a");
    }

    [Test]
    public async Task Logoot_CollectStable_Reclaims_Stable_Deleted_Position()
    {
        var sequence = new LogootSequence<string>();
        sequence.Append(A, "a");
        sequence.Append(A, "b");
        sequence.Delete(1);

        sequence.CollectStable(StableCut.Meet([sequence.ObservedVersion]));

        await Assert.That(sequence.ToJson(CrdtValues.String).Contains("\"b\"")).IsFalse();
        await Assert.That(string.Join("", sequence.ToArray())).IsEqualTo("a");
    }

    [Test]
    public async Task LSeq_CollectStable_Reclaims_Stable_Deleted_Position()
    {
        var sequence = new LSeqSequence<string>();
        sequence.Append(A, "a");
        sequence.Append(A, "b");
        sequence.Delete(1);

        sequence.CollectStable(StableCut.Meet([sequence.ObservedVersion]));

        await Assert.That(sequence.ToJson(CrdtValues.String).Contains("\"b\"")).IsFalse();
        await Assert.That(string.Join("", sequence.ToArray())).IsEqualTo("a");
    }

    [Test]
    public async Task Treedoc_CollectStable_Reclaims_Stable_Deleted_Position()
    {
        var sequence = new TreedocSequence<string>();
        sequence.Append(A, "a");
        sequence.Append(A, "b");
        sequence.Delete(1);

        sequence.CollectStable(StableCut.Meet([sequence.ObservedVersion]));

        await Assert.That(sequence.ToJson(CrdtValues.String).Contains("\"b\"")).IsFalse();
        await Assert.That(string.Join("", sequence.ToArray())).IsEqualTo("a");
    }

    [Test]
    public async Task LSeq_CollectStable_Does_Not_Reclaim_Above_Cut()
    {
        var sequence = new LSeqSequence<string>();
        sequence.Append(A, "a");
        sequence.Append(A, "b");
        sequence.Delete(1);
        string before = sequence.ToJson(CrdtValues.String);

        sequence.CollectStable(StableCut.Meet([]));

        await Assert.That(sequence.ToJson(CrdtValues.String)).IsEqualTo(before);
    }
}
