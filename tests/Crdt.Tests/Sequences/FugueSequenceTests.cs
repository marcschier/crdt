// Copyright (c) marcschier. Licensed under the MIT License.

using System.Text.Json;
using Crdt.Tests.Testing;

namespace Crdt.Tests.Sequences;

public sealed class FugueSequenceTests
{
    private static readonly ReplicaId A = ReplicaId.FromUInt64(1);
    private static readonly ReplicaId B = ReplicaId.FromUInt64(2);
    private static readonly ReplicaId C = ReplicaId.FromUInt64(3);

    private static string Render(FugueSequence<char> sequence) => new(sequence.ToArray());

    [Test]
    public async Task Concurrent_Runs_At_Same_Gap_Do_Not_Interleave()
    {
        var left = new FugueSequence<char>(A);
        var right = new FugueSequence<char>(B);

        InsertRun(left, "abc");
        InsertRun(right, "xyz");
        left.Merge(right);
        right.Merge(left);

        string rendered = Render(left);

        await Assert.That(rendered).IsEqualTo(Render(right));
        await Assert.That(rendered is "abcxyz" or "xyzabc").IsTrue();
        await Assert.That(rendered).IsNotEqualTo("axbycz");
    }

    [Test]
    public void Operations_Converge_Under_Reordering_And_Duplication()
    {
        var first = new FugueSequence<char>(A);
        var second = new FugueSequence<char>(B);
        var sim = new OperationDeliverySimulator<FugueSequence<char>, FugueOperation<char>>(first, second);

        sim.Broadcast(0, first.Append('h'));
        sim.Broadcast(0, first.Append('i'));
        sim.Broadcast(1, second.Append('y'));

        sim.DeliverAll(seed: 31, duplicate: true);

        sim.AssertConverged(static (x, y) => x.Equals(y) && Render(x) == Render(y));
    }

    [Test]
    public async Task Delete_Removes_Visible_Element()
    {
        var sequence = new FugueSequence<char>(A);
        InsertRun(sequence, "abc");
        FugueOperation<char> delete = sequence.Delete(1);
        var remote = new FugueSequence<char>(B);
        remote.Merge(sequence);

        bool changed = remote.Apply(delete);

        await Assert.That(Render(sequence)).IsEqualTo("ac");
        await Assert.That(Render(remote)).IsEqualTo("ac");
        await Assert.That(changed).IsFalse();
    }

    [Test]
    public async Task Mixed_Insert_Delete_Converges()
    {
        var first = new FugueSequence<char>(A);
        InsertRun(first, "abcd");
        var second = new FugueSequence<char>(B);
        second.Merge(first);

        first.Delete(1);
        first.InsertAt(1, 'x');
        second.Delete(2);
        second.InsertAt(2, 'y');

        FugueSequence<char> firstMerged = first.Clone();
        firstMerged.Merge(second);
        FugueSequence<char> secondMerged = second.Clone();
        secondMerged.Merge(first);

        await Assert.That(Render(firstMerged)).IsEqualTo(Render(secondMerged));
        await Assert.That(firstMerged.Count).IsEqualTo(4);
    }

    [Test]
    public async Task Inserts_At_Start_End_And_Middle()
    {
        var sequence = new FugueSequence<char>(A);

        sequence.InsertAt(0, 'b');
        sequence.InsertAt(0, 'a');
        sequence.Append('d');
        sequence.InsertAt(2, 'c');

        await Assert.That(Render(sequence)).IsEqualTo("abcd");
        await Assert.That(sequence.Text).IsEqualTo("abcd");
        await Assert.That(sequence.Value.Count).IsEqualTo(4);
    }

    [Test]
    public async Task Apply_Tolerates_Delete_Before_Insert()
    {
        FugueOperation<char> insert = new FugueSequence<char>(A).Append('x');
        var target = new FugueSequence<char>(B);

        target.Apply(FugueOperation<char>.Delete(insert.Id));
        target.Apply(insert);

        await Assert.That(target.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Apply_Tolerates_Child_Before_Parent()
    {
        var source = new FugueSequence<char>(A);
        FugueOperation<char> first = source.Append('a');
        FugueOperation<char> second = source.Append('b');
        var target = new FugueSequence<char>(B);

        target.Apply(second);
        int countBefore = target.Count;
        target.Apply(first);

        await Assert.That(countBefore).IsEqualTo(0);
        await Assert.That(Render(target)).IsEqualTo("ab");
    }

    [Test]
    public async Task Binary_Roundtrips()
    {
        var sequence = new FugueSequence<char>(A);
        InsertRun(sequence, "abc");
        sequence.Delete(1);

        byte[] data = sequence.ToByteArray(CharSerializer.Instance);
        FugueSequence<char> restored = FugueSequence<char>.ReadFrom(data, CharSerializer.Instance);

        await Assert.That(restored).IsEqualTo(sequence);
        await Assert.That(Render(restored)).IsEqualTo("ac");
    }

    [Test]
    public async Task Json_Roundtrips()
    {
        var sequence = new FugueSequence<string>(A);
        sequence.Append("hello");
        sequence.Append("world");
        sequence.Delete(0);

        string json = sequence.ToJson(CrdtValues.String);
        FugueSequence<string> restored = FugueSequence<string>.FromJson(json, CrdtValues.String);

        await Assert.That(restored).IsEqualTo(sequence);
        await Assert.That(string.Join("", restored.ToArray())).IsEqualTo("world");
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
