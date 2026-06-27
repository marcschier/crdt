# Choosing a data type

This guide helps you pick the right CRDT for a job. For each type it answers three questions — **when to use it**, **when to avoid it** (and what to prefer instead), and its **rough performance and resource cost** — plus a minimal API snippet.

It complements the [data types catalogue](data-types.md) (which lists every type with examples) by adding the *decision* and *cost* lens. For replication mechanics see [replication models](replication-models.md), for wire formats see [serialization](serialization.md), and for AOT/benchmark notes see [performance](performance.md). Every type in this library converges under `Merge` (commutative, associative, idempotent); the choice is about *which semantics* you want and *what they cost*.

## How to choose

1. **Pick the semantics first.** Decide how concurrent conflicts should resolve (add-wins vs remove-wins, last-writer-wins vs keep-all, can a removed thing come back, must an invariant hold). The wrong semantics cannot be fixed by tuning.
2. **Then weigh the cost.** CRDTs carry metadata so that merges are order- and duplication-independent. The key question is *what makes that metadata grow* — the number of replicas, the number of live elements, or the entire history of operations — and whether it is ever reclaimed.
3. **Then pick the replication model.** Any type can ship full **state**; many also support small **delta** or **operation** messages. That is a transport/bandwidth decision, independent of the type — see [replication models](replication-models.md).

### Decision by intent

| You want to model… | Start with | Notes |
| --- | --- | --- |
| A count that only goes up | `GCounter` | Visits, hits, monotonic totals. |
| A count that goes up and down | `PNCounter` | The general counter. |
| A count that must never cross a bound | `BCounter` | Stock, quota, balance ≥ 0. |
| A count you can reset to zero | `ResettableCounter` | Periodic metrics. |
| A count aggregated from many clients | `HandoffCounter` | Edge → server fan-in. |
| A set you only add to | `GSet<T>` | Tags that never disappear. |
| A general add/remove set | `ORSet<T>` | The default set (add-wins). |
| A set with frequent re-adds | `CausalLengthSet<T>` | Membership that flaps. |
| One value, last write wins | `LWWRegister<T>` | A status field, a name. |
| One field, keep concurrent writes | `MVRegister<T>` | Surface conflicts to the user. |
| A key/value map of scalars | `LWWMap<TKey,TValue>` | Settings, attributes. |
| A key/value map of CRDTs | `ORMap<TKey,TValue>` | Per-key counters/sets/registers. |
| A boolean | `GFlag` / `EnableWinsFlag` / `DisableWinsFlag` | One-way vs toggle, and which side wins. |
| A graph | `AddOnlyDag<T>` / `TwoPTwoPGraph<T>` | Grow-only DAG vs add/remove graph. |
| Ordered text / a list | `Rga<T>` / `Text` | Collaborative editing. |
| Text where merges must not interleave | `FugueSequence<T>` | Highest merge quality. |
| A movable tree / outline | `ReplicatedTree` | File trees, nested documents. |
| A whole JSON document | `JsonCrdt` | Nested object/array/scalar state. |
| Causality without global ids | `IntervalTreeClock` | Dynamic membership. |

A runnable tour of every type below lives in [`samples/Crdt.Samples`](../samples/Crdt.Samples).

## Counters

### `GCounter` — grow-only counter
- **Use when** the value only increases (page views, bytes sent, monotonic IDs).
- **Avoid when** you ever need to decrease — use `PNCounter`.
- **Cost** — one `ulong` per replica that has ever incremented; metadata is **bounded by the number of replicas**, never by the number of increments. Merge and read are O(replicas). The cheapest counter.

```csharp
var c = new GCounter();
c.Increment(me, 9);
ulong v = c.Value;
```

### `PNCounter` — positive-negative counter
- **Use when** you need both increments and decrements (likes, inventory deltas, a balance with no floor).
- **Avoid when** the value must not cross a bound (use `BCounter`), or you need periodic resets (use `ResettableCounter`).
- **Cost** — two `GCounter`s (increments + decrements): two `ulong`s per replica. Bounded by the number of replicas.

```csharp
var c = new PNCounter();
c.Increment(me, 10);
c.Decrement(me, 3);
long v = c.Value; // 7
```

### `BCounter` — bounded (escrow) counter
- **Use when** an invariant must hold under concurrency — most often **value ≥ a lower bound** (stock that can't go negative, a non-overdraw balance). Each replica owns decrement *rights*; transfer spare rights to where they are needed.
- **Avoid when** there is no bound to preserve (`PNCounter` is simpler and cheaper), or decrements happen far from where the rights live and you can't pre-distribute them.
- **Cost** — increment/decrement totals per replica **plus** a per-replica-pair transfer ledger; grows with replicas and the number of distinct transfer pairs. Heavier than `PNCounter`.

```csharp
var c = new BCounter(min: 0);
c.Increment(me, 10);
c.TryDecrement(me, 4, out _);     // fails rather than crossing the bound
c.TryTransfer(me, peer, 3, out _); // lend decrement rights to a peer
```

### `ResettableCounter` — resettable PN-counter
- **Use when** you accumulate then zero out periodically (per-minute metrics, a clearable badge count) and need the reset to be **observed** — increments made concurrently with the reset survive.
- **Avoid when** you never reset — `PNCounter` is much lighter.
- **Cost** — each contribution is stored under a unique dot; metadata grows with the **number of contributions** between resets (a reset prunes the observed ones). Heavier than `PNCounter`; keep an eye on hot counters that reset rarely.

```csharp
var c = new ResettableCounter();
c.Increment(me, 5);
c.Reset();            // removes only what this replica has observed
```

### `HandoffCounter` — handoff counter
- **Use when** many low-tier nodes aggregate into fewer high-tier nodes (thousands of edge clients → a few servers) and you want each node's metadata **bounded** rather than growing with the whole population.
- **Avoid when** you have a small, flat set of peers — a plain `PNCounter`/`GCounter` is simpler.
- **Cost** — bounded per node by design (slots/tokens per tier), at the price of a more intricate merge. Best in tiered topologies.

```csharp
var client = new HandoffCounter(clientId, tier: 0);
client.Increment(3);
var server = new HandoffCounter(serverId, tier: 1);
server.Merge(client); // the client hands its count up a tier
```

## Sets

### `GSet<T>` — grow-only set
- **Use when** elements are only ever added (observed feature flags, a union of seen ids).
- **Avoid when** you ever need to remove — nothing here can.
- **Cost** — one entry per element; bounded by the number of elements. The cheapest set.

### `TwoPhaseSet<T>` — two-phase set
- **Use when** a removal is **permanent** and an element must never come back (revoked tokens, consumed ids).
- **Avoid when** elements may be re-added — removed elements are tombstoned forever, so churn makes metadata grow without bound. Prefer `ORSet` or `CausalLengthSet`.
- **Cost** — an add-set **plus** a remove-set (tombstones) that is never reclaimed. Unbounded under remove churn.

### `LWWElementSet<T>` — last-writer-wins element set
- **Use when** membership is driven by timestamps and an element may be re-added after removal, with ties resolved by a configurable bias.
- **Avoid when** you want add-wins semantics without trusting clocks — prefer `ORSet`.
- **Cost** — an add-timestamp and a remove-timestamp per element; bounded by distinct elements, but it retains both timestamps.

### `ORSet<T>` — observed-remove set (ORSWOT)
- **Use when** you want a robust general-purpose add/remove set with **add-wins** semantics and **no tombstones**. This is the recommended default.
- **Avoid when** removal must be permanent (`TwoPhaseSet`) or the element set churns so hard that even causal metadata is too much (`CausalLengthSet`).
- **Cost** — a dot per live add plus a version-vector context; tombstone-free and compacts as causal history stabilises, but concurrent adds of the same element add metadata until merged.

```csharp
var s = new ORSet<string>();
s.Add(me, "x");
s.Remove("x"); // a concurrent Add elsewhere wins after merge
```

### `CausalLengthSet<T>` — causal-length set
- **Use when** elements are added and removed **repeatedly** and you want the smallest possible per-element metadata.
- **Avoid when** a simple grow-only or permanent-remove model fits — the causal-length bookkeeping isn't free.
- **Cost** — a single monotonically increasing integer per element (odd = present, even = absent); no separate tombstone set. Very compact for flapping membership.

## Registers

### `LWWRegister<T>` — last-writer-wins register
- **Use when** a field holds one value and the most recent write should win (a display name, a status).
- **Avoid when** losing a concurrent write is unacceptable — use `MVRegister`.
- **Cost** — one value plus one timestamp. Trivial.

```csharp
var r = new LWWRegister<string>();
r.Set("hello", clock);
```

### `MVRegister<T>` — multi-value register
- **Use when** concurrent writes must be **preserved and surfaced** (let the user or app resolve, like a version-conflict prompt).
- **Avoid when** you just want one winner — `LWWRegister` is simpler.
- **Cost** — keeps every concurrently-written value (each under a dot) until a causally-later write supersedes them; small unless concurrency is high and sustained.

## Maps

### `LWWMap<TKey, TValue>` — last-writer-wins map
- **Use when** you need a dictionary of scalar values where each key resolves last-writer-wins and keys can be removed/re-added.
- **Avoid when** values are themselves CRDTs that should *merge* rather than overwrite — use `ORMap`.
- **Cost** — a value and a timestamp per key; bounded by the number of keys.

### `ORMap<TKey, TValue>` — observed-remove map of CRDTs
- **Use when** each key's value is itself a CRDT (a per-key counter, set, or register) that must merge, with add-wins key semantics.
- **Avoid when** values are plain scalars — `LWWMap` is lighter.
- **Cost** — the map's add-wins context **plus the cost of every value CRDT**. Plan for the sum of the parts.

```csharp
var m = new ORMap<string, GCounter>(new GCounterValueOps());
m.Update(me, "visits", bumpedCounter);
```

## Flags

### `GFlag` — grow-only flag
- **Use when** a one-way latch is enough (a "has ever happened" bit).
- **Avoid when** it must turn off again.
- **Cost** — a single bit. Trivial.

### `EnableWinsFlag` / `DisableWinsFlag` — observed flags
- **Use when** a boolean toggles on and off and you must define which side wins a concurrent enable/disable (`EnableWinsFlag` favours on, `DisableWinsFlag` favours off).
- **Avoid when** a one-way latch suffices (`GFlag`).
- **Cost** — ORSWOT-style dots tracking the winning side; small and tombstone-free.

## Graphs

### `AddOnlyDag<TVertex>` — add-only monotonic DAG
- **Use when** you build a directed acyclic graph that only grows (dependency graphs, lineage) and want a local cycle-prevention check plus `TopologicalSort()`/`HasCycle()`.
- **Avoid when** you must remove vertices or edges — it is grow-only.
- **Cost** — grow-only vertex and edge sets; bounded by graph size. Follow the type's concurrency discipline so merges stay acyclic.

### `TwoPTwoPGraph<TVertex>` — two-phase graph
- **Use when** you need add **and** remove of vertices/edges and permanent removal is acceptable.
- **Avoid when** vertices/edges churn (two-phase tombstones accumulate) — model differently or prune out of band.
- **Cost** — vertices and edges are each two-phase sets, so removals are permanent tombstones that are never reclaimed.

## Sequences and text

All six sequence algorithms expose the **same API** (`Insert(replica, index, value)`, `Append`, `Delete`, `Count`, `this[index]`, `ToArray()`) and converge to the same ordered list — so you can swap one for another without changing call sites. They differ in their internal position-identifier scheme and therefore in cost. `Rga<T>` is the recommended default.

- **Use a sequence when** you need an ordered, collaboratively-edited list or text.
- **Cost (all of them)** — every inserted element carries a position identifier, and deletes leave tombstones, so metadata grows with the **total number of insertions over the document's lifetime**, not just the live length. Long-lived, heavily-edited documents accumulate history.

| Algorithm | Identifier scheme | Pick it when |
| --- | --- | --- |
| `Rga<T>` | Dot-identified insert-after | The default — compact ids, good all-round behaviour. |
| `LogootSequence<T>` | Dense `(digit, replica)` positions | You want simple dense positions and can tolerate id growth under adversarial interleaving. |
| `LSeqSequence<T>` | Logoot with adaptive allocation | Logoot semantics but bounded id growth. |
| `TreedocSequence<T>` | Paths in an infinite binary tree | Tree-structured positions. |
| `YataSequence<T>` | Origin-left/right integration (YATA/Yjs) | Familiarity with the Yjs model. |
| `WootSequence<T>` | Visibility flags + precedence | You specifically want WOOT semantics (heavier metadata). |

```csharp
var list = new Rga<string>();
list.Append(me, "a");
list.Insert(me, 0, "first");
string[] items = list.ToArray();
```

### `Text` — collaborative string
- **Use when** you want the sequence API specialised for `char` with a `.Value` string.
- **Cost** — an `Rga<char>`; same history-proportional cost as the sequences above.

### `FugueSequence<T>` — non-interleaving sequence
- **Use when** **merge quality matters most** — Fugue guarantees *maximal non-interleaving*, so runs typed concurrently by different replicas are never shuffled character-by-character (a known weakness of some sequence CRDTs).
- **Avoid when** the plain `Rga<T>` ordering is good enough and you prefer the simpler/industrial default.
- **Cost** — a tree node per element plus tombstones; comparable to the other sequences, with the stronger ordering guarantee.

## Trees and documents

### `ReplicatedTree` — tree with move
- **Use when** you replicate a tree/outline/filesystem and need a **highly-available move** that never produces cycles — concurrent moves that would create a cycle are deterministically skipped on every replica.
- **Avoid when** a flat collection suffices, or you don't need `Move` (a map of parent pointers would be lighter).
- **Cost** — the node set plus a move log replayed in timestamp order. Cost grows with the number of moves; the live tree itself is bounded by node count.

```csharp
var tree = new ReplicatedTree(me);
tree.Move("report", "documents", "Q3 Report");
```

### `JsonCrdt` — JSON document
- **Use when** you replicate a **nested JSON document** (objects, arrays, scalars) edited concurrently by path.
- **Avoid when** a single flat CRDT (a map, a register, one sequence) models your data — `JsonCrdt` is the heaviest type here.
- **Cost** — per-node metadata, last-writer-wins register leaves, and an RGA per array; the richest structure carries the most metadata. Reach for it when the data really is a free-form document.

```csharp
var doc = new JsonCrdt();
doc.SetString(me, clock.Now(), "title", "Hello");
doc.SetNumber(me, clock.Now(), "views", 1);
```

## Causal clocks

### `IntervalTreeClock` — interval tree clock
- **Use when** you need to track causality across a **dynamic** population where replicas come and go and you don't want to assign or retire global replica ids — fork an identity when a replica joins, join it back when one leaves.
- **Avoid when** membership is fixed and small — a `VersionVector` (sized by replica count) or the built-in `HybridLogicalClock` is simpler. Note this is a *clock*, not a data container.
- **Cost** — a compact id-tree and event-tree that grow as identities fork and shrink (normalise) as they join; far smaller than a version vector over a large, changing population.

```csharp
var seed = IntervalTreeClock.Seed();
var (a, b) = seed.Fork();   // hand b to a new replica
a = a.Event();
var rejoined = a.Join(b);   // when b leaves
```

## Cost cheat-sheet

"Bounded" means metadata is capped by a structural quantity (replicas, live elements); "history" means it grows with the number of operations over time (deletes leave tombstones) until causal stabilisation or compaction reclaims it.

| Type | Metadata grows with | Bounded? | Tombstones | Notes |
| --- | --- | --- | --- | --- |
| `GCounter` | replicas | yes | no | Cheapest counter. |
| `PNCounter` | replicas | yes | no | The general counter. |
| `BCounter` | replicas + transfer pairs | yes* | no | Preserves a lower bound. |
| `ResettableCounter` | contributions per reset window | history | pruned on reset | Heavier; watch rarely-reset hot counters. |
| `HandoffCounter` | bounded per node by tier | yes | no | For large tiered fan-in. |
| `GSet` | elements | yes | no | Add-only. |
| `TwoPhaseSet` | adds + removes | **no** | permanent | Avoid under re-add churn. |
| `LWWElementSet` | elements | yes | timestamps | Re-addable by timestamp. |
| `ORSet` | live adds + context | yes* | no | Recommended general set. |
| `CausalLengthSet` | elements | yes | no | One integer per element. |
| `LWWRegister` | constant | yes | no | One value + timestamp. |
| `MVRegister` | concurrent values | yes* | no | Keeps conflicts. |
| `LWWMap` | keys | yes | timestamps | Scalar values. |
| `ORMap` | keys + value CRDTs | depends | no | Cost of the values dominates. |
| `GFlag` | constant | yes | no | One bit. |
| `EnableWinsFlag` / `DisableWinsFlag` | dots for the winning side | yes* | no | Choose the winning side. |
| `AddOnlyDag` | vertices + edges | yes | no | Grow-only. |
| `TwoPTwoPGraph` | adds + removes | **no** | permanent | Avoid under churn. |
| `Rga` / `Text` | total insertions | history | yes | Default sequence. |
| `LogootSequence` | insertions (ids can grow) | history | yes | Dense positions. |
| `LSeqSequence` | insertions (bounded ids) | history | yes | Adaptive Logoot. |
| `TreedocSequence` | insertions | history | yes | Tree-path ids. |
| `YataSequence` | insertions | history | yes | Yjs-style. |
| `WootSequence` | insertions | history | yes | Heavier metadata. |
| `FugueSequence` | insertions | history | yes | Best non-interleaving. |
| `ReplicatedTree` | nodes + moves | history (moves) | no | Move log replay. |
| `JsonCrdt` | document structure | depends | per-node | Heaviest; full documents. |
| `IntervalTreeClock` | fork/join activity | normalises | n/a | A clock, not a container. |

\* Bounded in normal operation, but transient metadata appears under concurrency and is reclaimed as causal history stabilises.

## See also

- [Data types](data-types.md) — the catalogue with a worked example for every type.
- [Replication models](replication-models.md) — state vs delta vs operation, the bandwidth lever.
- [Serialization](serialization.md) — binary and JSON formats and their size characteristics.
- [Performance & NativeAOT](performance.md) — measured numbers and the benchmark suite.
