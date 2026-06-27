// Copyright (c) marcschier. Licensed under the MIT License.

namespace Crdt.Samples;

/// <summary>Demonstrates the register, map, and flag CRDTs.</summary>
internal static class RegisterMapFlagSamples
{
    private static readonly ReplicaId A = ReplicaId.FromUInt64(1);
    private static readonly ReplicaId B = ReplicaId.FromUInt64(2);

    public static void Run()
    {
        Console.WriteLine("## Registers, maps, and flags");

        // LWWRegister — the later write (by timestamp) wins.
        var clock = new HybridLogicalClock(A);
        var lwa = new LWWRegister<string>();
        var lwb = new LWWRegister<string>();
        lwa.Set("first", clock);
        lwb.Set("second", clock);   // a later timestamp from the shared clock
        lwa.Merge(lwb);
        lwa.TryGetValue(out string? lwValue);
        Console.WriteLine($"  LWWRegister       -> \"{lwValue}\" (later write wins)");

        // MVRegister — concurrent assignments are all kept until a causal write supersedes them.
        var mva = new MVRegister<string>();
        var mvb = new MVRegister<string>();
        mva.Assign(A, "x");
        mvb.Assign(B, "y");
        mva.Merge(mvb);
        Console.WriteLine($"  MVRegister        -> {{{string.Join(", ", mva.Values)}}} (concurrent values kept)");

        // LWWMap — each key resolves last-writer-wins.
        var map = new LWWMap<string, int>();
        map.Set("count", 1, clock);
        map.Set("count", 2, clock);
        map.TryGetValue("count", out int count);
        Console.WriteLine($"  LWWMap            -> count = {count} (latest write)");

        // ORMap — an add-wins map whose values are themselves CRDTs (a GCounter per key here).
        var counter = new GCounter();
        counter.Increment(A, 4);
        var omap = new ORMap<string, GCounter>(new GCounterValueOps());
        omap.Update(A, "visits", counter);
        omap.TryGetValue("visits", out GCounter? visits);
        Console.WriteLine($"  ORMap             -> visits = {visits?.Value} (value is a merged GCounter)");

        // GFlag — one-way false -> true.
        var gf = new GFlag();
        gf.Enable();
        Console.WriteLine($"  GFlag             -> {gf.Value} (true wins, one-way)");

        // EnableWinsFlag — a concurrent enable beats a concurrent disable.
        var ewa = new EnableWinsFlag();
        ewa.Enable(A);
        var ewb = new EnableWinsFlag();
        ewb.Merge(ewa);
        ewa.Disable(A);
        ewb.Enable(B);
        ewa.Merge(ewb);
        Console.WriteLine($"  EnableWinsFlag    -> {ewa.Value} (enable wins the tie)");

        // DisableWinsFlag — a concurrent disable beats a concurrent enable.
        var dwa = new DisableWinsFlag();
        dwa.Enable(A);
        var dwb = new DisableWinsFlag();
        dwb.Merge(dwa);
        dwa.Disable(A);
        dwb.Enable(B);
        dwa.Merge(dwb);
        Console.WriteLine($"  DisableWinsFlag   -> {dwa.Value} (disable wins the tie)");

        Console.WriteLine();
    }
}
