// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers;
using System.Text.Json;

namespace Crdt.Tests.Sequences;

public sealed class FugueSequenceCoverageTests
{
    private static readonly ReplicaId A = ReplicaId.FromUInt64(101);
    private static readonly ReplicaId B = ReplicaId.FromUInt64(202);

    [Test]
    public async Task Insert_And_Delete_Operations_Binary_Roundtrip()
    {
        var source = new FugueSequence<char>(A);
        FugueOperation<char> insert = source.Append('q');
        FugueOperation<char> delete = source.Delete(0);

        FugueOperation<char> insertRestored = Roundtrip(insert);
        FugueOperation<char> deleteRestored = Roundtrip(delete);

        await Assert.That(insertRestored.Kind).IsEqualTo(FugueOperationKind.Insert);
        await Assert.That(insertRestored.Id).IsEqualTo(insert.Id);
        await Assert.That(insertRestored.ParentId).IsEqualTo(insert.ParentId);
        await Assert.That(insertRestored.Side).IsEqualTo(insert.Side);
        await Assert.That(insertRestored.Value).IsEqualTo('q');
        await Assert.That(deleteRestored.Kind).IsEqualTo(FugueOperationKind.Delete);
        await Assert.That(deleteRestored.Id).IsEqualTo(delete.Id);
    }

    [Test]
    public async Task Sequence_Binary_And_Json_Roundtrip_Preserves_Tombstoned_State()
    {
        FugueSequence<char> sequence = BuildNonTrivialSequence();

        FugueSequence<char> binary =
            FugueSequence<char>.ReadFrom(sequence.ToByteArray(CharSerializer.Instance), CharSerializer.Instance);
        FugueSequence<char> json = FugueSequence<char>.FromJson(sequence.ToJson(CharSerializer.Instance),
            CharSerializer.Instance);

        await Assert.That(binary).IsEqualTo(sequence);
        await Assert.That(json).IsEqualTo(sequence);
        await Assert.That(binary.Text).IsEqualTo(sequence.Text);
        await Assert.That(json.Text).IsEqualTo(sequence.Text);
    }

    [Test]
    public async Task Out_Of_Order_Operations_Hide_Children_Until_Ancestors_Arrive()
    {
        var source = new FugueSequence<char>(A);
        FugueOperation<char> parent = source.Append('a');
        FugueOperation<char> child = source.Append('b');
        FugueOperation<char> deleteChild = source.Delete(1);
        var target = new FugueSequence<char>(B);

        bool childApplied = target.Apply(child);
        bool childDuplicateApplied = target.Apply(child);
        int hiddenCount = target.Count;
        bool deleteApplied = target.Apply(deleteChild);
        bool deleteDuplicateApplied = target.Apply(deleteChild);
        bool parentApplied = target.Apply(parent);
        bool parentDuplicateApplied = target.Apply(parent);

        await Assert.That(childApplied).IsTrue();
        await Assert.That(childDuplicateApplied).IsFalse();
        await Assert.That(hiddenCount).IsEqualTo(0);
        await Assert.That(deleteApplied).IsTrue();
        await Assert.That(deleteDuplicateApplied).IsFalse();
        await Assert.That(parentApplied).IsTrue();
        await Assert.That(parentDuplicateApplied).IsFalse();
        await Assert.That(target.Text).IsEqualTo("a");
    }

    [Test]
    public async Task Child_Becomes_Visible_When_Ancestors_Arrive()
    {
        var source = new FugueSequence<char>(A);
        FugueOperation<char> parent = source.Append('a');
        FugueOperation<char> child = source.Append('b');
        var target = new FugueSequence<char>(B);

        target.Apply(child);
        string beforeParent = target.Text;
        target.Apply(parent);

        await Assert.That(beforeParent).IsEqualTo(string.Empty);
        await Assert.That(target.Text).IsEqualTo("ab");
    }

    [Test]
    public async Task Concurrent_Runs_At_Same_Gap_Are_Not_Interleaved()
    {
        var left = new FugueSequence<char>(A);
        var right = new FugueSequence<char>(B);

        InsertRun(left, "abc");
        InsertRun(right, "xyz");
        FugueSequence<char> leftMerged = left.Clone();
        leftMerged.Merge(right);
        FugueSequence<char> rightMerged = right.Clone();
        rightMerged.Merge(left);

        string text = leftMerged.Text;

        await Assert.That(text).IsEqualTo(rightMerged.Text);
        await Assert.That(text is "abcxyz" or "xyzabc").IsTrue();
        await Assert.That(text).IsNotEqualTo("axbycz");
        await Assert.That(text).DoesNotContain("ax");
    }

    [Test]
    public async Task Delete_Boundaries_Indexer_And_Array_Surface()
    {
        var sequence = new FugueSequence<char>(A);

        sequence.InsertAt(0, 'b');
        sequence.InsertAt(0, 'a');
        sequence.InsertAt(sequence.Count, 'd');
        sequence.InsertAt(2, 'c');
        FugueOperation<char> deleted = sequence.Delete(1);
        bool duplicateDeleteChanged = sequence.Apply(deleted);

        await Assert.That(sequence[0]).IsEqualTo('a');
        await Assert.That(sequence[1]).IsEqualTo('c');
        await Assert.That(new string(sequence.ToArray())).IsEqualTo("acd");
        await Assert.That(new string(sequence.Value.ToArray())).IsEqualTo("acd");
        await Assert.That(sequence.Text).IsEqualTo("acd");
        await Assert.That(duplicateDeleteChanged).IsFalse();
        await Assert.That(() => sequence.Delete(3)).Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task Delete_Then_Merge_Converges()
    {
        var left = new FugueSequence<char>(A);
        InsertRun(left, "abcd");
        var right = new FugueSequence<char>(B);
        right.Merge(left);

        FugueOperation<char> delete = left.Delete(2);
        bool remoteChanged = right.Apply(delete);

        left.Merge(right);
        right.Merge(left);

        await Assert.That(remoteChanged).IsTrue();
        await Assert.That(left.Text).IsEqualTo("abd");
        await Assert.That(right.Text).IsEqualTo("abd");
        await Assert.That(left).IsEqualTo(right);
    }

    [Test]
    public async Task Equals_GetHashCode_And_Compare_Cover_Equal_And_Unequal_States()
    {
        FugueSequence<char> first = BuildNonTrivialSequence();
        FugueSequence<char> equal =
            FugueSequence<char>.ReadFrom(first.ToByteArray(CharSerializer.Instance), CharSerializer.Instance);
        var greater = new FugueSequence<char>(B);
        greater.Merge(first);
        greater.Append('z');
        var concurrent = new FugueSequence<char>(B);
        concurrent.Append('y');

        await Assert.That(first.Equals(equal)).IsTrue();
        await Assert.That(first.Equals(null as FugueSequence<char>)).IsFalse();
        await Assert.That(first.Equals("not a sequence")).IsFalse();
        await Assert.That(first.GetHashCode()).IsEqualTo(equal.GetHashCode());
        await Assert.That(first.Compare(equal)).IsEqualTo(CrdtOrder.Equal);
        await Assert.That(first.Compare(greater)).IsEqualTo(CrdtOrder.Less);
        await Assert.That(greater.Compare(first)).IsEqualTo(CrdtOrder.Greater);
        await Assert.That(first.Compare(concurrent)).IsEqualTo(CrdtOrder.Concurrent);
        await Assert.That(first.Equals(greater)).IsFalse();
    }

    [Test]
    public async Task Guard_Clauses_Throw_For_Null_And_Out_Of_Range_Inputs()
    {
        var sequence = new FugueSequence<char>(A);
        sequence.Append('x');

        await Assert.That(() => sequence.InsertAt(-1, 'a')).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => sequence.InsertAt(2, 'a')).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => sequence[-1]).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => sequence.Delete(1)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => sequence.Merge(null!)).Throws<ArgumentNullException>();
        await Assert.That(() => sequence.Compare(null!)).Throws<ArgumentNullException>();
        await Assert.That(() => sequence.ToByteArray(null!)).Throws<ArgumentNullException>();
        await Assert.That(() => sequence.ToJson(null!)).Throws<ArgumentNullException>();
        await Assert.That(() => FugueSequence<char>.ReadFrom([], null!)).Throws<ArgumentNullException>();
        await Assert.That(() => FugueSequence<char>.FromJson(null!, CharSerializer.Instance))
            .Throws<ArgumentNullException>();
        await Assert.That(() => FugueSequence<char>.FromJson("{}", null!)).Throws<ArgumentNullException>();
        await Assert.That(() => new FugueSequence<string>(A).Text).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Operation_Guards_Throw_For_Null_Serializer()
    {
        var source = new FugueSequence<char>(A);
        FugueOperation<char> insert = source.Append('x');
        var output = new ArrayBufferWriter<byte>();

        await Assert.That(() => insert.WriteTo(output, null!)).Throws<ArgumentNullException>();
        await Assert.That(() => FugueOperation<char>.ReadFrom([], null!)).Throws<ArgumentNullException>();
    }

    private static FugueOperation<char> Roundtrip(FugueOperation<char> operation)
    {
        var output = new ArrayBufferWriter<byte>();
        operation.WriteTo(output, CharSerializer.Instance);
        return FugueOperation<char>.ReadFrom(output.WrittenSpan, CharSerializer.Instance);
    }

    private static FugueSequence<char> BuildNonTrivialSequence()
    {
        var sequence = new FugueSequence<char>(A);
        sequence.InsertAt(0, 'b');
        sequence.InsertAt(0, 'a');
        sequence.InsertAt(sequence.Count, 'e');
        sequence.InsertAt(2, 'c');
        sequence.InsertAt(3, 'd');
        sequence.Delete(1);
        return sequence;
    }

    private static void InsertRun(FugueSequence<char> sequence, string text)
    {
        foreach (char value in text)
        {
            sequence.Append(value);
        }
    }

    private sealed class CharSerializer : ICrdtValueSerializer<char>
    {
        public static CharSerializer Instance { get; } = new();

        public void Write(ref CrdtWriter writer, char value) => writer.WriteVarUInt64(value);

        public char Read(ref CrdtReader reader) => (char)reader.ReadVarUInt64();

        public void WriteJson(Utf8JsonWriter writer, char value) => writer.WriteStringValue(value.ToString());

        public char ReadJson(ref Utf8JsonReader reader)
        {
            string? text = reader.GetString();
            return text is null or "" ? '\0' : text[0];
        }
    }
}
