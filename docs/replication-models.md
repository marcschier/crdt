# Replication models

A CRDT can be replicated in three ways. Every applicable type in this library supports all three through a common set of interfaces, so you can choose — and even mix — strategies per workload.

## State-based (CvRDT)

You exchange the **entire state** and merge it. This is the most robust model: `Merge` is commutative, associative, and idempotent, so it tolerates arbitrary message reordering and duplication. The only requirement on your transport is that state eventually reaches every replica (for example, via a gossip protocol).

```csharp
public interface IConvergent<TSelf> where TSelf : IConvergent<TSelf>
{
    void Merge(TSelf other);
    CrdtOrder Compare(TSelf other);
    TSelf Clone();
}
```

Use it when state is small, when your transport offers few guarantees, or when simplicity matters most. The cost is bandwidth: you ship everything every time.

## Delta-state

You ship only the **recent changes** (a *delta*) instead of the whole state. A delta belongs to the same join-semilattice as the state, so it merges with full states and with other deltas.

```csharp
public interface IDeltaConvergent<TSelf, TDelta> : IConvergent<TSelf>
    where TSelf : IDeltaConvergent<TSelf, TDelta>
{
    bool TryExtractDelta(out TDelta delta);
    void MergeDelta(TDelta delta);
}
```

`TryExtractDelta` returns the changes accumulated since the previous extraction and clears the internal delta buffer:

```csharp
counter.Increment(me, 5);
if (counter.TryExtractDelta(out GCounter? delta))
{
    // transmit 'delta' (much smaller than the full counter)
    peer.MergeDelta(delta);
}
```

Delta dissemination converges to the same result as full-state merging **provided each type's causal-delivery requirements are met**. For the grow-only and last-writer-wins types this is automatic. For causal types (observed-remove set/map, multi-value register, enable/disable-wins flags) the delta carries the causal context it needs; re-gossiping received deltas is the transport's job.

Use it when state is large but changes are frequent and small.

## Operation-based (CmRDT)

You broadcast each **operation** and replay it on every replica.

```csharp
public interface IOperationConvergent<TOperation>
{
    bool Apply(TOperation operation);
}
```

The mutating methods return the operation to broadcast:

```csharp
GCounterOperation op = counter.Increment(me);  // returns the op
// broadcast 'op'; on each remote replica:
remote.Apply(op);                              // returns true if it changed state
```

Operations in this library carry the metadata they need to be **idempotent** — applying the same operation twice is a no-op — so you only need at-least-once delivery, not exactly-once. Where a type requires causal ordering, that requirement is documented on the type. Sequence operations (`Rga<T>`) are order-tolerant: an inserted element simply stays invisible until its predecessor arrives.

Use it when operations are much smaller than state and your transport can deliver them reliably.

## How they relate

State-based and delta-state share the *same* `Merge` (a delta is just a small state). Operation-based is a separate path, but for many types an operation's effect coincides with merging a tiny delta. You can use different models for different types in the same application, or even switch models for one type over time (for example, snapshot with state-based merge, then stream with operations).

## The partial order

`Compare` reports how two states relate under the lattice's partial order:

```csharp
public enum CrdtOrder { Equal, Less, Greater, Concurrent }
```

`Concurrent` means neither state dominates the other — they were updated independently and a merge is required to reconcile them. This is the situation CRDTs are designed to resolve automatically.
