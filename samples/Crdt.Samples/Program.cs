// Copyright (c) marcschier. Licensed under the MIT License.

using Crdt;

Console.WriteLine("=== Crdt samples ===");
Console.WriteLine();

counterClusterSample();
setReplicationSample();
collaborativeTextSample();

Console.WriteLine("All replicas converged. ✓");

// ---------------------------------------------------------------------------
// A PN-Counter replicated across three nodes that mutate independently, then
// exchange full state (state-based replication) and converge.
// ---------------------------------------------------------------------------
static void counterClusterSample()
{
    Console.WriteLine("-- PN-Counter cluster (state-based) --");

    ReplicaId[] ids = [ReplicaId.FromUInt64(1), ReplicaId.FromUInt64(2), ReplicaId.FromUInt64(3)];
    PNCounter[] nodes = [new PNCounter(), new PNCounter(), new PNCounter()];

    nodes[0].Increment(ids[0], 10);
    nodes[1].Increment(ids[1], 5);
    nodes[2].Decrement(ids[2], 3);

    gossipState(nodes, static (target, source) => target.Merge(source));

    foreach (PNCounter node in nodes)
    {
        Console.WriteLine($"  node value = {node.Value}");
    }

    Console.WriteLine($"  converged value = {nodes[0].Value} (expected 12)");
    Console.WriteLine();
}

// ---------------------------------------------------------------------------
// An observed-remove set where a concurrent add wins over a concurrent remove.
// ---------------------------------------------------------------------------
static void setReplicationSample()
{
    Console.WriteLine("-- OR-Set (add-wins over concurrent remove) --");

    ReplicaId alice = ReplicaId.FromUInt64(1);
    ReplicaId bob = ReplicaId.FromUInt64(2);

    var onAlice = new ORSet<string>();
    onAlice.Add(alice, "feature-x");

    // Bob receives Alice's state.
    var onBob = ORSet<string>.ReadFrom(onAlice.ToByteArray(CrdtValues.String), CrdtValues.String);

    // Concurrently: Alice removes the tag, Bob re-adds it with a fresh dot.
    onAlice.Remove("feature-x");
    onBob.Add(bob, "feature-x");

    onAlice.Merge(onBob);
    onBob.Merge(onAlice);

    Console.WriteLine($"  alice contains 'feature-x' = {onAlice.Contains("feature-x")}");
    Console.WriteLine($"  bob   contains 'feature-x' = {onBob.Contains("feature-x")}");
    Console.WriteLine("  (add wins — the element survives)");
    Console.WriteLine();
}

// ---------------------------------------------------------------------------
// Two people editing the same document concurrently; their edits converge to
// a single consistent string regardless of merge order.
// ---------------------------------------------------------------------------
static void collaborativeTextSample()
{
    Console.WriteLine("-- Collaborative text --");

    ReplicaId alice = ReplicaId.FromUInt64(1);
    ReplicaId bob = ReplicaId.FromUInt64(2);

    var aliceDoc = new Text();
    aliceDoc.Append(alice, "the quick fox");

    // Bob starts from a serialized snapshot of Alice's document.
    Text bobDoc = Text.FromJson(aliceDoc.ToJson());

    // Concurrent edits: Alice inserts an adjective, Bob fixes the ending.
    aliceDoc.Insert(alice, 10, "brown ");
    bobDoc.Append(bob, " jumps");

    Text aliceMerged = aliceDoc.Clone();
    aliceMerged.Merge(bobDoc);

    Text bobMerged = bobDoc.Clone();
    bobMerged.Merge(aliceDoc);

    Console.WriteLine($"  alice sees: \"{aliceMerged.Value}\"");
    Console.WriteLine($"  bob   sees: \"{bobMerged.Value}\"");
    Console.WriteLine($"  identical  = {aliceMerged.Value == bobMerged.Value}");
    Console.WriteLine();
}

// A trivial in-memory "transport": every node merges every other node's state.
static void gossipState<T>(T[] nodes, Action<T, T> merge)
    where T : IConvergent<T>
{
    foreach (T target in nodes)
    {
        foreach (T source in nodes)
        {
            if (!ReferenceEquals(target, source))
            {
                merge(target, source);
            }
        }
    }
}
