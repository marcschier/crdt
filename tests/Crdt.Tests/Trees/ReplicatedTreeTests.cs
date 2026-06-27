// Copyright (c) marcschier. Licensed under the MIT License.

namespace Crdt.Tests.Trees;

public sealed class ReplicatedTreeTests
{
    [Test]
    public async Task Move_Reparents_Node_And_Stores_Metadata()
    {
        var tree = new ReplicatedTree(ReplicaId.FromUInt64(1));

        tree.Move("a", "root", "alpha");
        TreeMoveOperation operation = tree.Move("a", "b", "beta");

        await Assert.That(operation.Child).IsEqualTo("a");
        await Assert.That(tree.GetParent("a")).IsEqualTo("b");
        await Assert.That(tree.Nodes["a"].Meta).IsEqualTo("beta");
        await Assert.That(tree.HasCycle()).IsFalse();
    }

    [Test]
    public async Task Apply_Duplicate_Operation_Is_No_Op()
    {
        var source = new ReplicatedTree(ReplicaId.FromUInt64(1));
        TreeMoveOperation operation = source.Move("a", "root", "alpha");
        var target = new ReplicatedTree(ReplicaId.FromUInt64(2));

        bool first = target.Apply(operation);
        bool second = target.Apply(operation);

        await Assert.That(first).IsTrue();
        await Assert.That(second).IsFalse();
        await Assert.That(target.Log.Count).IsEqualTo(1);
        await Assert.That(target.GetParent("a")).IsEqualTo("root");
    }

    [Test]
    public async Task Concurrent_Cross_Moves_Converge_To_Cycle_Free_Tree()
    {
        var left = new ReplicatedTree(ReplicaId.FromUInt64(1));
        var right = new ReplicatedTree(ReplicaId.FromUInt64(2));

        TreeMoveOperation aUnderB = left.Move("a", "b", "left");
        TreeMoveOperation bUnderA = right.Move("b", "a", "right");

        left.Apply(bUnderA);
        right.Apply(aUnderB);

        await Assert.That(left).IsEqualTo(right);
        await Assert.That(left.HasCycle()).IsFalse();
        await Assert.That(left.Log.Count(move => move.Skipped)).IsEqualTo(1);
    }

    [Test]
    public async Task Apply_Order_Does_Not_Affect_Convergence()
    {
        TreeMoveOperation[] operations = CreateOperations();
        var first = new ReplicatedTree(ReplicaId.FromUInt64(10));
        var second = new ReplicatedTree(ReplicaId.FromUInt64(11));

        foreach (TreeMoveOperation operation in Shuffle(operations, seed: 17))
        {
            first.Apply(operation);
        }

        foreach (TreeMoveOperation operation in Shuffle(operations, seed: 93))
        {
            second.Apply(operation);
        }

        await Assert.That(first).IsEqualTo(second);
        await Assert.That(first.HasCycle()).IsFalse();
    }

    [Test]
    public async Task Root_And_Orphan_Parents_Are_Allowed()
    {
        var tree = new ReplicatedTree(ReplicaId.FromUInt64(1));

        tree.Move("a", "missing-parent", "orphan");
        tree.Move("root", "forest", "root-meta");

        await Assert.That(tree.GetParent("a")).IsEqualTo("missing-parent");
        await Assert.That(tree.GetParent("missing-parent")).IsNull();
        await Assert.That(tree.IsAncestor("missing-parent", "a")).IsTrue();
        await Assert.That(tree.IsAncestor("a", "missing-parent")).IsFalse();
    }

    [Test]
    public async Task Merge_Compares_By_Move_Log_Inclusion()
    {
        var left = new ReplicatedTree(ReplicaId.FromUInt64(1));
        var right = new ReplicatedTree(ReplicaId.FromUInt64(2));
        TreeMoveOperation operation = left.Move("a", "root", "alpha");

        await Assert.That(right.Compare(left)).IsEqualTo(CrdtOrder.Less);
        right.Apply(operation);
        await Assert.That(right.Compare(left)).IsEqualTo(CrdtOrder.Equal);

        left.Move("b", "root", "beta");
        right.Move("c", "root", "gamma");

        await Assert.That(left.Compare(right)).IsEqualTo(CrdtOrder.Concurrent);
        left.Merge(right);
        right.Merge(left);
        await Assert.That(left).IsEqualTo(right);
    }

    [Test]
    public async Task Binary_Roundtrips_Tree_And_Log()
    {
        var tree = SampleTree();

        ReplicatedTree restored = ReplicatedTree.ReadFrom(tree.ToByteArray());

        await Assert.That(restored).IsEqualTo(tree);
        await Assert.That(restored.Log.Count).IsEqualTo(tree.Log.Count);
    }

    [Test]
    public async Task Json_Roundtrips_Tree_And_Log()
    {
        var tree = SampleTree();

        ReplicatedTree restored = ReplicatedTree.FromJson(tree.ToJson());

        await Assert.That(restored).IsEqualTo(tree);
        await Assert.That(restored.Log.Count).IsEqualTo(tree.Log.Count);
    }

    private static TreeMoveOperation[] CreateOperations()
    {
        var r1 = new ReplicatedTree(ReplicaId.FromUInt64(1));
        var r2 = new ReplicatedTree(ReplicaId.FromUInt64(2));
        var r3 = new ReplicatedTree(ReplicaId.FromUInt64(3));
        return
        [
            r1.Move("a", "root", "a0"),
            r1.Move("b", "root", "b0"),
            r1.Move("c", "a", "c0"),
            r2.Move("a", "b", "a1"),
            r3.Move("b", "c", "b1"),
            r2.Move("d", "a", "d0"),
            r3.Move("c", "root", "c1"),
        ];
    }

    private static ReplicatedTree SampleTree()
    {
        var tree = new ReplicatedTree(ReplicaId.FromUInt64(7));
        tree.Move("a", "root", "alpha");
        tree.Move("b", "a", "beta");
        tree.Move("c", "b", "gamma");
        tree.Apply(new TreeMoveOperation(new MoveTimestamp(2, ReplicaId.FromUInt64(8)), "a", "c", "cycle"));
        return tree;
    }

    private static TreeMoveOperation[] Shuffle(TreeMoveOperation[] operations, int seed)
    {
        TreeMoveOperation[] copy = operations.ToArray();
        var random = new Random(seed);
        for (int i = copy.Length - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            (copy[i], copy[j]) = (copy[j], copy[i]);
        }

        return copy;
    }
}
