# Crdt documentation

`Crdt` is a high-performance, NativeAOT-ready library of [Conflict-free Replicated Data Types](https://en.wikipedia.org/wiki/Conflict-free_replicated_data_type) for modern .NET. This folder is the developer guide: how to pick a type, how replication works, how to serialize, and how the internals fit together.

## Contents

- [Getting started](getting-started.md) — install, your first counter, merging replicas.
- [Data types](data-types.md) — the full catalogue with usage examples for every CRDT.
- [Replication models](replication-models.md) — state-based, delta-state, and operation-based, and when to use each.
- [Serialization](serialization.md) — the binary and JSON formats, value serializers, and hostile-input limits.
- [Architecture](architecture.md) — semilattices, dots, the causal kernel (ORSWOT), and the Hybrid Logical Clock.
- [Performance & NativeAOT](performance.md) — AOT compliance, trimming, and benchmarking notes.
- [Transports](transports.md) — the optional `Crdt.Transport` package: in-memory and TCP gossip replication.

## The one-paragraph mental model

Every CRDT in this library is a value that can be replicated to many machines, mutated independently on each, and then reconciled automatically. Reconciliation is a `Merge` that is commutative, associative, and idempotent — so it does not matter in what order, or how many times, replicas exchange their state: they always converge to the same result. Some types additionally let you ship only the recent *delta* of changes, or broadcast individual *operations*, instead of the whole state.

## A 30-second example

```csharp
using Crdt;

// Two replicas, each with its own identity.
ReplicaId alice = ReplicaId.New();
ReplicaId bob = ReplicaId.New();

var a = new GCounter();
var b = new GCounter();

a.Increment(alice, 3);   // Alice counts 3
b.Increment(bob, 5);     // Bob counts 5 (concurrently, no coordination)

// Exchange state and merge — order and duplication do not matter.
a.Merge(b);
b.Merge(a);

Console.WriteLine(a.Value); // 8
Console.WriteLine(b.Value); // 8  (converged)
```

## Scope

The library provides the data structures, their merge/delta/operation logic, and serialization. It does **not** include a network layer: delivering bytes between replicas (gossip, anti-entropy, a message bus, a database) is the application's responsibility. The [samples](../samples) show a simple in-memory transport for illustration.
