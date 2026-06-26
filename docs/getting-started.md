# Getting started

## Install

```shell
dotnet add package Crdt
```

`Crdt` targets `netstandard2.1`, `net8.0`, `net9.0`, and `net10.0`. On .NET 8 and later it is annotated `IsAotCompatible` and is verified to run under NativeAOT.

## Replica identity

Every replica (node, device, process) that mutates a CRDT needs a stable identity. Mint one with `ReplicaId.New()` and persist it for the lifetime of that replica:

```csharp
ReplicaId me = ReplicaId.New();
```

Identity is what lets the library attribute changes and break ties deterministically. For deterministic tests you can use `ReplicaId.FromUInt64(1)`.

## Your first CRDT

A grow-only counter is the simplest useful CRDT:

```csharp
using Crdt;

var counter = new GCounter();
counter.Increment(me);        // +1
counter.Increment(me, 10);    // +10
Console.WriteLine(counter.Value); // 11
```

## Merging two replicas

The whole point of a CRDT is that two replicas can diverge and then reconcile without conflicts:

```csharp
ReplicaId alice = ReplicaId.FromUInt64(1);
ReplicaId bob = ReplicaId.FromUInt64(2);

var onAlice = new PNCounter();
var onBob = new PNCounter();

onAlice.Increment(alice, 100);
onBob.Decrement(bob, 30);

// Each side eventually receives the other's state and merges it.
onAlice.Merge(onBob);
onBob.Merge(onAlice);

Console.WriteLine(onAlice.Value); // 70
Console.WriteLine(onBob.Value);   // 70
```

`Merge` is safe to call repeatedly and in any order — that is the defining property of a CRDT.

## Choosing how to replicate

You have three options for moving changes between replicas; pick per type and per workload (see [replication models](replication-models.md)):

```csharp
// 1. State-based: send the whole thing, merge it.
byte[] state = counter.ToByteArray();
var remote = GCounter.ReadFrom(state);
local.Merge(remote);

// 2. Delta-state: send only what changed since you last shipped a delta.
if (counter.TryExtractDelta(out GCounter? delta))
{
    // ship 'delta', then on the other side:
    other.MergeDelta(delta);
}

// 3. Operation-based: broadcast each operation as it happens.
GCounterOperation op = counter.Increment(me);
// ship 'op', then on every other replica:
other.Apply(op);
```

## Working with values

Most collection types are generic and need a *value serializer* so they can encode their elements. The library ships serializers for common types in `CrdtValues`:

```csharp
var tags = new ORSet<string>();
tags.Add(me, "urgent");
tags.Add(me, "review");

byte[] bytes = tags.ToByteArray(CrdtValues.String);
ORSet<string> restored = ORSet<string>.ReadFrom(bytes, CrdtValues.String);
```

For your own element types, implement `ICrdtValueSerializer<T>` (four small methods) and pass it the same way.

## Collaborative text

The `Text` type is a ready-made collaborative string:

```csharp
var doc = new Text();
doc.Append(alice, "Hello");
doc.Insert(alice, 5, " world");

// On another replica, concurrent edits converge:
Text other = Text.FromJson(doc.ToJson());
other.Insert(bob, 0, "[draft] ");
doc.Merge(other);

Console.WriteLine(doc.Value); // "[draft] Hello world"
```

## Next steps

- Browse the [data type catalogue](data-types.md) to find the right CRDT for your problem.
- Read about [replication models](replication-models.md) to decide between state, delta, and operations.
- See [serialization](serialization.md) for the wire formats and safety limits.
