// Copyright (c) marcschier. Licensed under the MIT License.

namespace Crdt.Samples;

/// <summary>Demonstrates the counter CRDTs, including the advanced escrow/reset/handoff variants.</summary>
internal static class CounterSamples
{
    private static readonly ReplicaId A = ReplicaId.FromUInt64(1);
    private static readonly ReplicaId B = ReplicaId.FromUInt64(2);

    public static void Run()
    {
        Console.WriteLine("## Counters");

        // GCounter — grow-only; merge keeps the per-replica maxima, so the value is their sum.
        var ga = new GCounter();
        var gb = new GCounter();
        ga.Increment(A, 3);
        gb.Increment(B, 5);
        ga.Merge(gb);
        gb.Merge(ga);
        Console.WriteLine($"  GCounter          -> {ga.Value} (expected 8)");

        // PNCounter — increments and decrements.
        var pa = new PNCounter();
        var pb = new PNCounter();
        pa.Increment(A, 10);
        pb.Decrement(B, 3);
        pa.Merge(pb);
        pb.Merge(pa);
        Console.WriteLine($"  PNCounter         -> {pa.Value} (expected 7)");

        // BCounter — bounded escrow counter; the value can never drop below the configured minimum.
        var bc = new BCounter(min: 0);
        bc.Increment(A, 10);
        bc.TryDecrement(A, 4, out _);
        bc.TryTransfer(A, B, 3, out _);   // hand 3 decrement rights to B
        Console.WriteLine($"  BCounter          -> {bc.Value} (>= min 0, expected 6)");

        // ResettableCounter — observed reset; an increment concurrent with the reset survives.
        var ra = new ResettableCounter();
        ra.Increment(A, 5);
        ResettableCounter rb = ra.Clone();
        ra.Reset();                       // A resets the +5 it has observed
        rb.Increment(B, 2);               // B increments concurrently (not seen by the reset)
        ra.Merge(rb);
        rb.Merge(ra);
        Console.WriteLine($"  ResettableCounter -> {ra.Value} (the concurrent +2 survives the reset)");

        // HandoffCounter — a tier-0 client hands its count to a tier-1 aggregator on merge.
        var client = new HandoffCounter(A, tier: 0);
        client.Increment(3);
        var server = new HandoffCounter(B, tier: 1);
        server.Merge(client);
        Console.WriteLine($"  HandoffCounter    -> {server.Value} (expected 3)");

        Console.WriteLine();
    }
}
