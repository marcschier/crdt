# Crdt.Samples.Gc

A deterministic, process-local garbage-collection walkthrough: three replicas share an `InMemoryNetwork`, replicate an `Rga<char>` through `ReplicationEngine<TState>`, and then let `GarbageCollectionCoordinator` reclaim stable sequence tombstones after the deterministic leader commits a stable cut.

## What it demonstrates

- Wiring one in-process transport per replica into CRDT replication, `HeartbeatFailureDetector`, `DeterministicLeaderConsensus`, and `GarbageCollectionCoordinator`.
- Replicas appending visible characters, deleting their local leaf character to create RGA tombstones, and gossiping state until every replica converges.
- The elected leader computing a causal-stability watermark, committing it through pluggable `IConsensus`, broadcasting it, and every replica reclaiming the same stable tombstones while the visible value stays converged.

## How it works

`SampleCluster.StartAsync` creates three `InMemoryTransport` instances from one `InMemoryNetwork`. Each transport is shared by a `CrdtReplica<Rga<char>>` + `ReplicationEngine<Rga<char>>`, a `HeartbeatFailureDetector`, a `DeterministicLeaderConsensus`, and a `GarbageCollectionCoordinator`. The transport frame types keep replication, consensus heartbeats/proposals, and GC reports/watermarks separated on the same in-process bus.

Each replica appends an uppercase character and then appends and deletes a lowercase leaf. After `BroadcastStateAsync` converges the RGA state, each replica has the same visible text and the same tombstone count. Starting the coordinators sends each replica's observed version vector; the deterministic consensus leader (the lowest live replica id) computes the pointwise minimum, commits it as the GC watermark, and broadcasts it. Replicas call `CollectStable` and drop the all-observed tombstones.

Consensus is deliberately pluggable. This sample uses `DeterministicLeaderConsensus` so it is self-contained and deterministic; a production cluster can replace it with a Raft-backed `IConsensus` implementation from `Crdt.Consensus.Raft` once RaftCs is published.

## Run it

From the repository root:

```shell
dotnet run --project samples/Crdt.Samples.Gc -c Release
```

(Requires the .NET 10 SDK; the sample targets `net10.0`.)

### Expected output shape

```text
Elected leader: node-1
Tombstones before GC: node-1=3, node-2=3, node-3=3
Tombstones after GC: node-1=0, node-2=0, node-3=0
Replicas converged after GC: True (value: "CBA")
```

The visible RGA order is decided by the sequence CRDT's deterministic identity ordering, so it can differ from append order while still being identical on every replica.

## NativeAOT smoke test

This sample doubles as a Native AOT smoke test of the in-memory replication, consensus, and GC stack. Publish it as a native binary and run it:

```shell
dotnet publish samples/Crdt.Samples.Gc -c Release -r <rid> -p:AotSmoke=true
```

Replace `<rid>` with your runtime identifier (for example `win-x64`, `linux-x64`, or `osx-arm64`). The `AotSmoke` switch turns on `PublishAot` for this project only, so it never propagates into the multi-targeted `Crdt` libraries.

## Further reading

- [Garbage collection](../../docs/garbage-collection.md) — causal stability, stable cuts, safety, and pluggable consensus.
- [Transports](../../docs/transports.md) — in-memory and network transports used by replication and GC frames.
- [Replication models](../../docs/replication-models.md) — when to use state, delta, or operations.
