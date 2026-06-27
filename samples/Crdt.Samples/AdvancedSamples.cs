// Copyright (c) marcschier. Licensed under the MIT License.

namespace Crdt.Samples;

/// <summary>Demonstrates the tree, JSON document, and interval-tree-clock CRDTs.</summary>
internal static class AdvancedSamples
{
    private static readonly ReplicaId A = ReplicaId.FromUInt64(1);

    public static void Run()
    {
        Console.WriteLine("## Trees, documents, and causal clocks");

        // ReplicatedTree — highly-available Move; concurrent cyclic moves are skipped identically.
        var tree = new ReplicatedTree(A);
        tree.Move("documents", "root", "Documents");
        tree.Move("report", "documents", "Q3 Report");
        Console.WriteLine(
            $"  ReplicatedTree    -> {tree.Nodes.Count} nodes; 'report' parent = {tree.Nodes["report"].Parent}");

        // JsonCrdt — a nested JSON document edited by path; serializes to plain JSON.
        var clock = new HybridLogicalClock(A);
        var doc = new JsonCrdt();
        doc.SetString(A, clock.Now(), "title", "Hello");
        doc.SetNumber(A, clock.Now(), "views", 1);
        Console.WriteLine($"  JsonCrdt          -> {doc.ToJson()}");

        // IntervalTreeClock — a fork/join causal clock that needs no global replica ids.
        IntervalTreeClock seed = IntervalTreeClock.Seed();
        (IntervalTreeClock x, IntervalTreeClock y) = seed.Fork();
        x = x.Event();                 // record a local event on x
        bool seen = y.Leq(x);          // x has observed at least as much as y
        IntervalTreeClock joined = x.Join(y);
        Console.WriteLine(
            $"  IntervalTreeClock -> after fork + event: (y <= x) = {seen}; rejoin <= itself = {joined.Leq(joined)}");

        Console.WriteLine();
    }
}
