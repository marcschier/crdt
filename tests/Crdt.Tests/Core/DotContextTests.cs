// Copyright (c) marcschier. Licensed under the MIT License.

namespace Crdt.Tests.Core;

public sealed class DotContextTests
{
    private static readonly ReplicaId A = ReplicaId.FromUInt64(1);

    [Test]
    public async Task NextDot_Allocates_Contiguous_Sequences()
    {
        var ctx = new DotContext();
        await Assert.That(ctx.NextDot(A).Sequence).IsEqualTo(1UL);
        await Assert.That(ctx.NextDot(A).Sequence).IsEqualTo(2UL);
        await Assert.That(ctx.Contains(new Dot(A, 2))).IsTrue();
    }

    [Test]
    public async Task Add_OutOfOrder_Then_Fill_Gap_Compacts()
    {
        var ctx = new DotContext();
        ctx.Add(new Dot(A, 2));

        await Assert.That(ctx.Contains(new Dot(A, 2))).IsTrue();
        await Assert.That(ctx.Contains(new Dot(A, 1))).IsFalse();

        ctx.Add(new Dot(A, 1));

        await Assert.That(ctx.Contains(new Dot(A, 1))).IsTrue();
        await Assert.That(ctx.Contains(new Dot(A, 2))).IsTrue();

        // After compaction the contiguous prefix is 2, so the next minted dot is 3.
        await Assert.That(ctx.NextDot(A).Sequence).IsEqualTo(3UL);
    }

    [Test]
    public async Task Add_Idempotent()
    {
        var ctx = new DotContext();
        ctx.Add(new Dot(A, 1));
        ctx.Add(new Dot(A, 1));
        await Assert.That(ctx.Contains(new Dot(A, 1))).IsTrue();
    }

    [Test]
    public async Task Merge_Unions_Observed_Dots_And_Compacts()
    {
        var left = new DotContext();
        left.Add(new Dot(A, 1));

        var right = new DotContext();
        right.Add(new Dot(A, 2));

        left.Merge(right);

        await Assert.That(left.Contains(new Dot(A, 1))).IsTrue();
        await Assert.That(left.Contains(new Dot(A, 2))).IsTrue();
        await Assert.That(left.NextDot(A).Sequence).IsEqualTo(3UL);
    }

    [Test]
    public async Task Clone_Is_Independent()
    {
        var ctx = new DotContext();
        ctx.Add(new Dot(A, 1));
        DotContext clone = ctx.Clone();
        clone.NextDot(A);

        await Assert.That(ctx.Contains(new Dot(A, 2))).IsFalse();
        await Assert.That(clone.Contains(new Dot(A, 2))).IsTrue();
    }
}
