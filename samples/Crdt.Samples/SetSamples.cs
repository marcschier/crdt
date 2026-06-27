// Copyright (c) marcschier. Licensed under the MIT License.

namespace Crdt.Samples;

/// <summary>Demonstrates the set CRDTs, including the causal-length set.</summary>
internal static class SetSamples
{
    private static readonly ReplicaId A = ReplicaId.FromUInt64(1);
    private static readonly ReplicaId B = ReplicaId.FromUInt64(2);

    public static void Run()
    {
        Console.WriteLine("## Sets");

        // GSet — grow-only; merge is union.
        var gsa = new GSet<string>();
        var gsb = new GSet<string>();
        gsa.Add("a");
        gsb.Add("b");
        gsa.Merge(gsb);
        Console.WriteLine($"  GSet              -> {{{string.Join(", ", gsa.Elements)}}} (union)");

        // TwoPhaseSet — add and remove, but a removed element can never be re-added.
        var tp = new TwoPhaseSet<string>();
        tp.Add("x");
        tp.Remove("x");
        tp.Add("x");   // ignored — "x" is tombstoned permanently
        Console.WriteLine($"  TwoPhaseSet       -> contains 'x' = {tp.Contains("x")} (remove wins, forever)");

        // LWWElementSet — add/remove by timestamp; an element can be re-added after removal.
        var clock = new HybridLogicalClock(A);
        var lww = new LWWElementSet<string>();
        lww.Add("x", clock);
        lww.Remove("x", clock);
        lww.Add("x", clock);
        Console.WriteLine($"  LWWElementSet     -> contains 'x' = {lww.Contains("x")} (latest write wins)");

        // ORSet — observed-remove (add-wins), no tombstones; the recommended general-purpose set.
        var ora = new ORSet<string>();
        ora.Add(A, "tag");
        var orb = new ORSet<string>();
        orb.Merge(ora);            // B observes A's add
        ora.Remove("tag");         // A removes it
        orb.Add(B, "tag");         // B concurrently re-adds it
        ora.Merge(orb);
        orb.Merge(ora);
        Console.WriteLine($"  ORSet             -> contains 'tag' = {ora.Contains("tag")} (add wins)");

        // CausalLengthSet — repeated add/remove cycles without accumulating tombstones.
        var cls = new CausalLengthSet<string>();
        cls.Add("x");
        cls.Remove("x");
        cls.Add("x");
        Console.WriteLine($"  CausalLengthSet   -> contains 'x' = {cls.Contains("x")} (re-add without tombstones)");

        Console.WriteLine();
    }
}
