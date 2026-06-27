# Data types

The full catalogue, grouped by family. Every type supports state-based, delta-state, and operation-based replication and both binary and JSON serialization (see [replication models](replication-models.md) and [serialization](serialization.md)).

## Counters

### `GCounter` — grow-only counter

Increments only; value is the sum across replicas.

```csharp
var c = new GCounter();
c.Increment(me);       // +1
c.Increment(me, 9);    // +9
ulong value = c.Value; // 10
```

### `PNCounter` — positive-negative counter

Increments and decrements; value can go negative.

```csharp
var c = new PNCounter();
c.Increment(me, 10);
c.Decrement(me, 3);
long value = c.Value;  // 7
```

## Registers

### `LWWRegister<T>` — last-writer-wins register

Holds one value; concurrent writes resolve by timestamp.

```csharp
var clock = new HybridLogicalClock(me);
var reg = new LWWRegister<string>();
reg.Set("hello", clock);
reg.Set("world", clock);   // wins (later timestamp)
```

### `MVRegister<T>` — multi-value register

Keeps every concurrently-written value until a causal write supersedes them.

```csharp
var reg = new MVRegister<string>();
reg.Assign(alice, "A");
// concurrently on another replica: reg2.Assign(bob, "B"); then merge
reg.Merge(reg2);
// reg.Values contains both "A" and "B" until someone overwrites causally
```

## Sets

### `GSet<T>` — grow-only set

Add only; merge is union.

```csharp
var s = new GSet<string>();
s.Add("a");
bool has = s.Contains("a"); // true
```

### `TwoPhaseSet<T>` — two-phase set

Add and remove, but a removed element can never be re-added (remove-wins, permanent).

```csharp
var s = new TwoPhaseSet<string>();
s.Add("x");
s.Remove("x");   // tombstoned forever
```

### `LWWElementSet<T>` — last-writer-wins element set

Add and remove by timestamp; an element can be re-added after removal. A `Bias` controls ties.

```csharp
var clock = new HybridLogicalClock(me);
var s = new LWWElementSet<string>();
s.Add("x", clock);
s.Remove("x", clock);
s.Add("x", clock);   // present again
```

### `ORSet<T>` — observed-remove set (ORSWOT)

Add and remove with **add-wins** semantics and no tombstones — the recommended general-purpose set.

```csharp
var s = new ORSet<string>();
s.Add(me, "x");
s.Remove("x");
// A concurrent Add on another replica beats this Remove after merge.
```

## Maps

### `LWWMap<TKey, TValue>` — last-writer-wins map

Each key's value resolves by timestamp; keys can be removed and re-added.

```csharp
var clock = new HybridLogicalClock(me);
var m = new LWWMap<string, int>();
m.Set("count", 1, clock);
m.Set("count", 2, clock);   // wins
m.Remove("count", clock);
```

### `ORMap<TKey, TValue>` — observed-remove map of CRDTs

An add-wins map whose **values are themselves CRDTs**, merged through an `ICrdtValueOps<TValue>` you supply.

```csharp
var m = new ORMap<string, GCounter>(new GCounterValueOps());
m.Update(me, "visits", BumpedCounter(me));   // merges into the key's counter
m.TryGetValue("visits", out GCounter visits);
```

A concurrent update and remove of the same key resolves add-wins; concurrent updates to the same key merge the value CRDTs.

## Flags

### `GFlag` — grow-only flag

One bit, false to true only ("true wins").

```csharp
var f = new GFlag();
f.Enable();
bool on = f.Value; // true
```

### `EnableWinsFlag` / `DisableWinsFlag` — observed flags

Toggle on and off; concurrent enable+disable resolves in favour of enable (or disable) respectively.

```csharp
var f = new EnableWinsFlag();
f.Enable(me);
f.Disable(me);
// A concurrent Enable on another replica wins after merge.
```

## Graphs

### `TwoPTwoPGraph<TVertex>` — two-phase graph

Vertices and edges, each a two-phase set. Adding an edge requires both endpoints to exist; removing a vertex hides its incident edges.

```csharp
var g = new TwoPTwoPGraph<string>();
g.AddVertex("a");
g.AddVertex("b");
g.AddEdge("a", "b");
```

### `AddOnlyDag<TVertex>` — add-only monotonic DAG

A grow-only directed acyclic graph with a local cycle-prevention check, plus `HasCycle()` and `TopologicalSort()`. See the type's remarks for the concurrency discipline that keeps merges acyclic.

```csharp
var g = new AddOnlyDag<string>();
g.AddVertex("a");
g.AddVertex("b");
g.AddEdge("a", "b");   // throws if it would close a cycle
```

## Sequences and text

The library provides six interchangeable sequence CRDT algorithms — all converge to the same ordered list under concurrent edits, but differ in their internal identifier scheme and performance characteristics. `Rga<T>` is the recommended default.

| Type | Approach |
| --- | --- |
| `Rga<T>` | Replicated Growable Array — insert-after a dot-identified predecessor (industrial default). |
| `LogootSequence<T>` | Dense position identifiers (a list of `(digit, replica)` segments). |
| `LSeqSequence<T>` | Logoot with an adaptive allocation strategy to bound identifier growth. |
| `TreedocSequence<T>` | Positions as paths in an infinite binary tree. |
| `YataSequence<T>` | YATA / Yjs-style integration with origin-left/right references. |
| `WootSequence<T>` | WOOT — visibility flags with precedence-based integration. |

### `Rga<T>` — replicated growable array

An ordered list for collaborative editing; concurrent inserts converge deterministically.

```csharp
var list = new Rga<string>();
list.Append(me, "a");
list.Insert(me, 0, "first");
string[] items = list.ToArray();
```

All six share the same shape — `Insert(replica, index, value)`, `Append`, `Delete(index)`, `Count`, `this[index]`, `ToArray()`, `Merge`, and binary/JSON serialization via a value serializer — so you can swap algorithms without changing call sites.

### `Text` — collaborative string

A string-friendly wrapper over `Rga<char>`.

```csharp
var doc = new Text();
doc.Append(me, "Hello");
doc.Insert(me, 5, " world");
string s = doc.Value; // "Hello world"
```

## Advanced / esoteric CRDTs

Specialized, research-grade CRDTs for advanced scenarios. Each supports state-based `Merge` plus compact binary and `System.Text.Json` serialization; several also expose an operation-based API. Unlike the core catalogue, not every advanced type implements all three replication models — the notes below call out the supported ones.

### `BCounter` — bounded counter (escrow)

A counter that supports increments and decrements while guaranteeing the value never drops below a configured lower bound, even under concurrency. Each replica owns a number of decrement *rights*; spare rights can be transferred to another replica so it can decrement locally. Operation-based and state-based.

```csharp
var c = new BCounter(min: 0);     // value can never drop below 0
c.Increment(alice, 10);
c.TryDecrement(alice, 4, out _);  // succeeds: alice owns enough rights
long value = c.Value;             // 6
c.TryTransfer(alice, bob, 3, out _); // hand 3 rights to bob so he can decrement
```

### `ResettableCounter` — resettable PN-counter

A positive-negative counter with an observed-reset. Each contribution is stored under a unique dot; `Reset()` removes exactly the contributions observed at reset time, so increments made concurrently with the reset survive. Operation-based and state-based.

```csharp
var c = new ResettableCounter();
c.Increment(me, 5);
c.Decrement(me, 2);
c.Reset();             // removes observed contributions
long value = c.Value;  // 0 — contributions concurrent with the reset still count
```

### `HandoffCounter` — handoff counter

A counter designed for large, tiered topologies (for example many edge clients aggregating into fewer servers). Tier-0 nodes hand their counts off to higher-tier aggregators on merge, bounding the metadata each node must carry. State-based.

```csharp
var client = new HandoffCounter(clientId, tier: 0);
client.Increment(3);
var server = new HandoffCounter(serverId, tier: 1);
server.Merge(client);        // the client hands its count to the aggregator
ulong value = server.Value;  // 3
```

### `CausalLengthSet<T>` — causal-length set

A set that supports repeated add/remove cycles without tombstone sets. Each element carries a monotonically increasing causal length: odd means present, even means absent, and merge keeps the maximum length per element. Operation-based and state-based.

```csharp
var set = new CausalLengthSet<string>();
set.Add("x");
set.Remove("x");
set.Add("x");                     // re-add works without accumulating tombstones
bool present = set.Contains("x"); // true
```

### `ReplicatedTree` — tree with move

A replicated tree supporting a highly-available `Move` operation. Moves are replayed in timestamp order with the undo/do/redo algorithm of Kleppmann et al.; a concurrent move that would introduce a cycle is deterministically skipped on every replica. Operation-based.

```csharp
var tree = new ReplicatedTree(me);
tree.Move("documents", "root", "Documents");
tree.Move("report", "documents", "Q3 Report");
// a concurrent move that would create a cycle is skipped identically everywhere
IReadOnlyDictionary<string, (string Parent, string Meta)> nodes = tree.Nodes;
```

### `FugueSequence<T>` — non-interleaving sequence

A FugueMax tree sequence whose visible order is a deterministic in-order traversal. Fugue guarantees *maximal non-interleaving*: characters typed concurrently by different replicas are never shuffled together. State-based with idempotent operation application.

```csharp
var seq = new FugueSequence<char>(me);
seq.InsertAt(0, 'H');
seq.Append('i');
string text = seq.Text; // "Hi" — concurrent runs never interleave
```

### `JsonCrdt` — JSON document

A nested JSON document CRDT composed of maps, lists (ordered by an RGA), and last-writer-wins register leaves. Edits address nodes by a path of map keys and stable list-element ids. Operation-based with full state merge. (v1: object/array/primitive leaves, numbers as `double`.)

```csharp
var clock = new HybridLogicalClock(me);
var doc = new JsonCrdt();
doc.SetString(me, clock.Now(), "title", "Hello");
doc.SetNumber(me, clock.Now(), "views", 1);
doc.SetArray(me, clock.Now(), Array.Empty<JsonPathSegment>(), "tags");
JsonNode root = doc.Root; // structured tree of maps, lists, and registers
```

### `IntervalTreeClock` — interval tree clock

A compact causal clock (Almeida, Baquero, Fonte) whose identity can be **forked** and **joined** without assigning globally unique replica ids — well suited to dynamic populations where replicas come and go. Provides `Fork`/`Event`/`Join` and a causal `Compare`/`Leq`. State-based.

```csharp
var seed = IntervalTreeClock.Seed(); // (1, 0)
var (a, b) = seed.Fork();            // two disjoint identities, shared history
a = a.Event();                       // record a local event on a
bool seen = b.Leq(a);                // true: a has observed at least as much as b
var joined = a.Join(b);              // recombine identity and event history
```
