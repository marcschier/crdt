// Copyright (c) marcschier. Licensed under the MIT License.

namespace Crdt.Samples;

/// <summary>Demonstrates the graph CRDTs and the interchangeable sequence/text CRDTs.</summary>
internal static class GraphSequenceSamples
{
    private static readonly ReplicaId A = ReplicaId.FromUInt64(1);
    private static readonly ReplicaId B = ReplicaId.FromUInt64(2);

    public static void Run()
    {
        Console.WriteLine("## Graphs");

        // TwoPTwoPGraph — vertices and edges, each a two-phase set.
        var g1 = new TwoPTwoPGraph<string>();
        g1.AddVertex("a");
        g1.AddVertex("b");
        g1.AddEdge("a", "b");
        var g2 = new TwoPTwoPGraph<string>();
        g2.Merge(g1);
        g2.AddVertex("c");
        g2.AddEdge("b", "c");
        g1.Merge(g2);
        Console.WriteLine($"  TwoPTwoPGraph     -> {g1.VertexCount} vertices, {g1.EdgeCount} edges (converged)");

        // AddOnlyDag — grow-only DAG with cycle prevention, a topological sort, and HasCycle.
        var dag = new AddOnlyDag<string>();
        dag.AddVertex("a");
        dag.AddVertex("b");
        dag.AddVertex("c");
        dag.AddEdge("a", "b");
        dag.AddEdge("b", "c");
        Console.WriteLine(
            $"  AddOnlyDag        -> order [{string.Join(" -> ", dag.TopologicalSort())}], cycle = {dag.HasCycle()}");

        Console.WriteLine();
        Console.WriteLine("## Sequences and text");

        // Six interchangeable sequence algorithms — same API, same converged order.
        SequenceDemo("Rga", () => new Rga<string>(), (s, r, v) => s.Append(r, v), s => s.ToArray());
        SequenceDemo(
            "LogootSequence", () => new LogootSequence<string>(), (s, r, v) => s.Append(r, v), s => s.ToArray());
        SequenceDemo("LSeqSequence", () => new LSeqSequence<string>(), (s, r, v) => s.Append(r, v), s => s.ToArray());
        SequenceDemo(
            "TreedocSequence", () => new TreedocSequence<string>(), (s, r, v) => s.Append(r, v), s => s.ToArray());
        SequenceDemo("YataSequence", () => new YataSequence<string>(), (s, r, v) => s.Append(r, v), s => s.ToArray());
        SequenceDemo("WootSequence", () => new WootSequence<string>(), (s, r, v) => s.Append(r, v), s => s.ToArray());

        // Text — a string-friendly RGA.
        var text = new Text();
        text.Append(A, "Hello");
        text.Insert(A, 5, " world");
        Console.WriteLine($"  Text              -> \"{text.Value}\"");

        // FugueSequence — maximal non-interleaving; concurrent runs are never shuffled together.
        var fugue = new FugueSequence<char>(A);
        fugue.InsertAt(0, 'H');
        fugue.Append('i');
        Console.WriteLine($"  FugueSequence     -> \"{fugue.Text}\"");

        Console.WriteLine();
    }

    private static void SequenceDemo<TSeq>(
        string name,
        Func<TSeq> create,
        Action<TSeq, ReplicaId, string> append,
        Func<TSeq, string[]> toArray)
        where TSeq : IConvergent<TSeq>
    {
        TSeq a = create();
        append(a, A, "one");
        TSeq b = create();
        b.Merge(a);                 // B starts from A's state
        append(a, A, "two");
        append(b, B, "three");
        a.Merge(b);
        b.Merge(a);
        string[] items = toArray(a);
        Console.WriteLine($"  {name,-17} -> [{string.Join(", ", items)}] (converged, {items.Length} items)");
    }
}
