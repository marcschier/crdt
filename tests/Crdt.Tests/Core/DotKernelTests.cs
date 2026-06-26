// Copyright (c) marcschier. Licensed under the MIT License.

namespace Crdt.Tests.Core;

public sealed class DotKernelTests
{
    private static readonly ReplicaId A = ReplicaId.FromUInt64(1);
    private static readonly ReplicaId B = ReplicaId.FromUInt64(2);

    private static string SortedValues(DotKernel<string> kernel) =>
        string.Join(",", kernel.Values.OrderBy(static v => v, StringComparer.Ordinal));

    [Test]
    public async Task Add_Stores_Value_Under_Fresh_Dot()
    {
        var kernel = new DotKernel<string>();
        Dot dot = kernel.Add(A, "x");

        await Assert.That(dot.Sequence).IsEqualTo(1UL);
        await Assert.That(kernel.Count).IsEqualTo(1);
        await Assert.That(kernel.Values.Single()).IsEqualTo("x");
    }

    [Test]
    public async Task Merge_Propagates_Causal_Remove()
    {
        var a = new DotKernel<string>();
        a.Add(A, "x");

        DotKernel<string> b = a.Clone();
        // b observed x and then removes it; b's context still records the dot.
        Dot dotToRemove = b.Entries.Keys.Single();
        b.RemoveDot(dotToRemove);

        // a still holds x, but b has causally removed it -> merge drops it.
        a.Merge(b);

        await Assert.That(a.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Merge_Is_AddWins_For_Concurrent_Add_And_Remove()
    {
        var a = new DotKernel<string>();
        a.Add(A, "x"); // dot (A,1)

        DotKernel<string> b = a.Clone();
        b.RemoveDot(b.Entries.Keys.Single()); // b removes (A,1)

        // Concurrently, a re-adds x under a brand new dot (A,2) that b never observed.
        a.Add(A, "x");

        a.Merge(b);

        // (A,1) is dropped (b removed it) but (A,2) survives: the element remains present.
        await Assert.That(a.Values).Contains("x");
        await Assert.That(a.Count).IsEqualTo(1);
    }

    [Test]
    public async Task Merge_Is_Idempotent()
    {
        var a = new DotKernel<string>();
        a.Add(A, "x");
        a.Add(B, "y");

        string before = SortedValues(a);
        a.Merge(a.Clone());

        await Assert.That(SortedValues(a)).IsEqualTo(before);
    }

    [Test]
    public async Task Merge_Is_Commutative_In_Values()
    {
        var a = new DotKernel<string>();
        a.Add(A, "x");

        var b = new DotKernel<string>();
        b.Add(B, "y");

        DotKernel<string> ab = a.Clone();
        ab.Merge(b);

        DotKernel<string> ba = b.Clone();
        ba.Merge(a);

        await Assert.That(SortedValues(ab)).IsEqualTo(SortedValues(ba));
    }

    [Test]
    public async Task Insert_Records_Dot_As_Observed()
    {
        var kernel = new DotKernel<string>();
        var dot = new Dot(A, 7);
        kernel.Insert(dot, "z");

        await Assert.That(kernel.Context.Contains(dot)).IsTrue();
        await Assert.That(kernel.Values.Single()).IsEqualTo("z");
    }
}
