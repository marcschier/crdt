# Crdt.Samples

A self-contained console **tour of every CRDT** in the library — **no networking**. For each type, independent in-process replicas are mutated and then reconciled with `Merge`, showing that the result is the same regardless of the order or duplication of exchanges. It doubles as a smoke test of the whole public API.

## What it demonstrates

One labelled line per type, grouped by family — including the advanced/esoteric types:

- **Counters** — `GCounter`, `PNCounter`, `BCounter` (escrow/bounded), `ResettableCounter` (observed reset), `HandoffCounter` (tiered fan-in).
- **Sets** — `GSet`, `TwoPhaseSet`, `LWWElementSet`, `ORSet`, `CausalLengthSet`.
- **Registers, maps, flags** — `LWWRegister`, `MVRegister`, `LWWMap`, `ORMap` (of `GCounter`), `GFlag`, `EnableWinsFlag`, `DisableWinsFlag`.
- **Graphs** — `TwoPTwoPGraph`, `AddOnlyDag` (with a topological sort).
- **Sequences and text** — the six interchangeable algorithms (`Rga`, `LogootSequence`, `LSeqSequence`, `TreedocSequence`, `YataSequence`, `WootSequence`), `Text`, and `FugueSequence`.
- **Trees, documents, clocks** — `ReplicatedTree`, `JsonCrdt`, `IntervalTreeClock`.

## How it works

Each family lives in its own file (`CounterSamples.cs`, `SetSamples.cs`, …). A demo builds plain CRDT values, mutates them on separate "replicas", and merges — replication is faked by calling `Merge` directly, which is enough to show convergence without a transport. There is no network and no persistence; the goal is to make the algebra of merging visible across the entire catalogue.

## Run it

From the repository root:

```shell
dotnet run --project samples/Crdt.Samples -c Release
```

(Requires the .NET 10 SDK; the sample targets `net10.0`.)

### Expected output

```text
=== Crdt data-type tour ===
Each line shows independent replicas mutating, then merging to a converged value.

## Counters
  GCounter          -> 8 (expected 8)
  PNCounter         -> 7 (expected 7)
  BCounter          -> 6 (>= min 0, expected 6)
  ResettableCounter -> 2 (the concurrent +2 survives the reset)
  HandoffCounter    -> 3 (expected 3)

## Sets
  GSet              -> {a, b} (union)
  TwoPhaseSet       -> contains 'x' = False (remove wins, forever)
  LWWElementSet     -> contains 'x' = True (latest write wins)
  ORSet             -> contains 'tag' = True (add wins over concurrent remove)
  CausalLengthSet   -> contains 'x' = True (re-add without tombstones)

## Registers, maps, and flags
  LWWRegister       -> "second" (later write wins)
  MVRegister        -> {x, y} (concurrent values kept)
  LWWMap            -> count = 2 (latest write)
  ORMap             -> visits = 4 (value is a merged GCounter)
  GFlag             -> True (true wins, one-way)
  EnableWinsFlag    -> True (enable wins the tie)
  DisableWinsFlag   -> False (disable wins the tie)

## Graphs
  TwoPTwoPGraph     -> 3 vertices, 2 edges (converged)
  AddOnlyDag        -> order [a -> b -> c], cycle = False

## Sequences and text
  Rga               -> [one, three, two] (converged, 3 items)
  LogootSequence    -> [one, two, three] (converged, 3 items)
  LSeqSequence      -> [one, two, three] (converged, 3 items)
  TreedocSequence   -> [one, two, three] (converged, 3 items)
  YataSequence      -> [one, two, three] (converged, 3 items)
  WootSequence      -> [one, two, three] (converged, 3 items)
  Text              -> "Hello world"
  FugueSequence     -> "Hi"

## Trees, documents, and causal clocks
  ReplicatedTree    -> 2 nodes; 'report' parent = documents
  JsonCrdt          -> {"title":"Hello","views":1}
  IntervalTreeClock -> after fork + event: (y <= x) = True; rejoin <= itself = True

All replicas converged. ✓
```

The exact converged order of the sequence algorithms is each algorithm's own deterministic result — replicas of the *same* type always agree, but different algorithms may resolve concurrent inserts differently (note `Rga` above).

## Further reading

- [Choosing a data type](../../docs/choosing-data-types.md) — when to use which type, and rough cost.
- [Data types](../../docs/data-types.md) — every CRDT with usage examples.
- [Replication models](../../docs/replication-models.md) — state-based, delta-state, and operation-based.
- For versions that replicate over a real network transport, see [`Crdt.Samples.Gossip`](../Crdt.Samples.Gossip), [`Crdt.Samples.Mqtt`](../Crdt.Samples.Mqtt), and [`Crdt.Samples.NanoMsg`](../Crdt.Samples.NanoMsg).
