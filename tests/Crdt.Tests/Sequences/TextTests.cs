// Copyright (c) marcschier. Licensed under the MIT License.

namespace Crdt.Tests.Sequences;

public sealed class TextTests
{
    private static readonly ReplicaId A = ReplicaId.FromUInt64(1);
    private static readonly ReplicaId B = ReplicaId.FromUInt64(2);

    [Test]
    public async Task Insert_Builds_String()
    {
        var text = new Text();
        text.Append(A, "Hello");
        text.Insert(A, 5, " world");

        await Assert.That(text.Value).IsEqualTo("Hello world");
        await Assert.That(text.Length).IsEqualTo(11);
    }

    [Test]
    public async Task Delete_Removes_Range()
    {
        var text = new Text();
        text.Append(A, "Hello world");
        text.Delete(5, 6);

        await Assert.That(text.Value).IsEqualTo("Hello");
    }

    [Test]
    public async Task Concurrent_Edits_Converge()
    {
        var left = new Text();
        left.Append(A, "abc");
        Text right = left.Clone();

        left.Insert(A, 0, "X");
        right.Insert(B, 3, "Y");

        Text leftMerged = left.Clone();
        leftMerged.Merge(right);
        Text rightMerged = right.Clone();
        rightMerged.Merge(left);

        await Assert.That(leftMerged.Value).IsEqualTo(rightMerged.Value);
        await Assert.That(leftMerged.Length).IsEqualTo(5);
    }

    [Test]
    public async Task Binary_Roundtrips()
    {
        var text = new Text();
        text.Append(A, "round trip ✓");

        Text restored = Text.ReadFrom(text.ToByteArray());
        await Assert.That(restored.Value).IsEqualTo(text.Value);
    }

    [Test]
    public async Task Json_Roundtrips()
    {
        var text = new Text();
        text.Append(A, "json ünî");

        Text restored = Text.FromJson(text.ToJson());
        await Assert.That(restored.Value).IsEqualTo(text.Value);
    }
}
