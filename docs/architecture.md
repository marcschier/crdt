# Architecture

This page explains the building blocks the CRDTs are made of. You do not need any of this to *use* the library, but it helps when implementing a custom type or reasoning about convergence.

## Join-semilattices

Every state-based and delta-state CRDT is a **join-semilattice**: a set of states with a `Merge` (join) operation that is

- **commutative** — `merge(a, b) == merge(b, a)`,
- **associative** — `merge(merge(a, b), c) == merge(a, merge(b, c))`, and
- **idempotent** — `merge(a, a) == a`.

These three properties are exactly what make convergence robust to message reordering and duplication. Mutations move the state *upward* in the lattice (they are monotone), so the merged result always dominates both inputs. The test suite verifies these laws for every type with a reusable `CrdtLaws` harness.

## Replica identity and dots

A `ReplicaId` is a 128-bit identity (a GUID) minted per replica. A `Dot` is a single event identifier — a `(ReplicaId, sequence)` pair where `sequence` increases by one for each event a replica produces. Dots are the atoms of causal history; nearly every causal CRDT is built from them.

```csharp
ReplicaId r = ReplicaId.New();
var first = new Dot(r, 1);   // the replica's first event
```

## Version vectors

A `VersionVector` maps each replica to the highest contiguous sequence number observed from it. It summarises "everything I have seen" compactly, and its `Merge` is the element-wise maximum. Version vectors drive `RGA`'s identifier allocation and underpin causal comparison.

## The causal kernel (ORSWOT)

Observed-remove types — `ORSet`, `ORMap`, `MVRegister`, and the enable/disable-wins flags — share an internal **dot store** kernel. It maps each live value to the dot that introduced it and tracks a *causal context* of every dot ever observed. The merge rule is:

1. Drop a local value if the other replica has **observed and removed** its dot.
2. Absorb a remote value if we have **neither seen nor removed** its dot.
3. Keep values present on both sides, then union the causal contexts.

This yields **add-wins** semantics — a concurrent add beats a concurrent remove, because the add introduces a *fresh* dot the remove never saw — and, crucially, it needs **no tombstones**: a removal is recorded as the mere *absence* of a value whose dot is still in the context. The causal context is kept compact by folding contiguous dots back into a version vector and keeping only the out-of-order ones in a small "dot cloud".

The disable-wins flag is the exact dual: disabling adds the dot and enabling removes it, so a concurrent disable wins.

## Last-writer-wins and the Hybrid Logical Clock

`LWWRegister`, `LWWElementSet`, and `LWWMap` resolve concurrent writes by timestamp. A `Timestamp` is a total order combining a physical wall-clock reading, a logical counter (for events within the same instant), and the originating replica (the final tie-breaker).

Generate timestamps with a `HybridLogicalClock`. An HLC stays close to physical time, is strictly monotonic locally, and advances when it *witnesses* a timestamp from another replica — so causally-later writes always carry larger timestamps even when machine clocks disagree:

```csharp
var clock = new HybridLogicalClock(me);            // uses TimeProvider.System
var register = new LWWRegister<string>();
register.Set("draft", clock);                      // stamps with clock.Now()
```

The clock takes a `TimeProvider`, which makes time fully deterministic in tests. It is also the one place that is thread-safe, since it must hand out monotonic timestamps.

## Sequences

`Rga<T>` (Replicated Growable Array) represents an ordered list as a tree of dot-identified nodes. Each element references the element it was inserted after; concurrent insertions at the same point are ordered deterministically by identity. The visible sequence is a pre-order traversal that skips tombstones, and an element only becomes visible once its ancestors are present — which is why operations may be delivered out of order. The state itself is a pair of grow-only sets (nodes and tombstones), so it is an ordinary join-semilattice. `Text` is a thin, string-friendly wrapper over `Rga<char>`.

Five further sequence algorithms ship alongside RGA — Logoot, LSEQ, Treedoc, YATA, and WOOT — each with a different identifier scheme (dense position lists, binary-tree paths, or origin/precedence references) but the same converging behaviour and a common API, so they are interchangeable.

## Determinism and serialization

Identity-keyed structures serialize in a canonical, replica-sorted order, so equal states produce identical bytes. Equality and hashing are defined over logical state, not internal storage order. See [serialization](serialization.md) for the formats and the limits that protect decoders from hostile input.

## Concurrency

CRDT instances are **mutable and not thread-safe**: concurrent local mutation of a single instance requires external synchronization. Exchanging a clone or a serialized snapshot with another thread or machine is always safe.
