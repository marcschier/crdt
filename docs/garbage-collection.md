# Garbage collection

CRDTs are safe to replicate without coordination, but several tombstone-bearing types keep metadata for removed elements so late or reordered messages can still merge correctly. Garbage collection in `Crdt` is a coordinated, conservative layer for reclaiming metadata only after every live replica has observed the dots that created it.

## Model

Every garbage-collectable CRDT exposes `IGarbageCollectable.ObservedVersion`, a version vector describing the dots the local value has observed, and `CollectStable(StableCut)`, which reclaims any metadata the type can prove is no longer needed below a stable cut. A dot is causally stable when it is covered by every live replica's observed version vector. The leader computes that stable cut as the pointwise minimum (meet) of the live replicas' reports: for each replica id, keep the lowest counter reported by any live member.

`GarbageCollectionCoordinator` periodically reports the union of the observed frontiers for its registered local values. The current leader waits until every live member has a fresh report, computes the meet, commits the encoded watermark through `IConsensus`, broadcasts it, and each replica applies it locally by calling `CollectStable` on every registered value.

## Safety guarantee

The guarantee is intentionally conservative: a replica reclaims only metadata for dots that every live member has observed. If any live member is unreachable, missing from the report set, or has only a stale report, the leader does not advance the watermark. This can retain tombstones longer than strictly necessary, but it prevents reclaiming metadata that an isolated or slow live replica still needs to merge safely.

Membership therefore matters. Use a failure detector or another membership source that matches your availability and safety policy. Removing a member from the live set lets future cuts advance without that member; keeping it in the live set pauses GC until it reports again.

## Types that benefit

Sequence and text CRDTs benefit most because deletes leave per-position metadata. `Rga<T>`, `WootSequence<T>`, `YataSequence<T>`, and `FugueSequence<T>` can reclaim stable deleted leaves or positions while preserving the visible sequence and future convergence. Positional sequence variants such as `LogootSequence<T>`, `LSeqSequence<T>`, and `TreedocSequence<T>` also participate through their shared core.

Some types implement `IGarbageCollectable` but intentionally reclaim little or nothing from a stable cut alone. `TwoPhaseSet<T>` and `CausalLengthSet<T>` keep remove metadata whose value-level safety cannot be proven from only a causal watermark, so `CollectStable` is near-zero or limited unless the application has additional value-level proof.

## Consensus is pluggable

`GarbageCollectionCoordinator` depends on `IConsensus`, not on a specific consensus algorithm. `DeterministicLeaderConsensus` is included for deterministic tests and samples: it elects the lowest live replica id and commits entries after the live members acknowledge them. Production deployments can swap in a stronger implementation without changing CRDT values or the coordinator wiring. A Raft-backed package (`Crdt.Consensus.Raft`) is intended to drop in once RaftCs is published.

## Usage sketch

```csharp
using Crdt;
using Crdt.Consensus;
using Crdt.Gc;
using Crdt.Transport;

await using var network = new InMemoryNetwork();
ITransport transport = network.CreateTransport();
ReplicaId local = ReplicaId.FromUInt64(1);
ReplicaId peer = ReplicaId.FromUInt64(2);
var value = new Rga<string>();

var detectorOptions = new HeartbeatFailureDetectorOptions
{
    LocalReplicaId = local,
    Transport = new ConsensusTransportOptions { Transport = transport },
};
detectorOptions.InitialMembers.Add(local);
detectorOptions.InitialMembers.Add(peer);

var detector = new HeartbeatFailureDetector(detectorOptions);
var consensus = new DeterministicLeaderConsensus(new DeterministicLeaderConsensusOptions
{
    LocalReplicaId = local,
    FailureDetector = detector,
    Transport = new ConsensusTransportOptions
    {
        Transport = transport,
        StartTransport = false,
    },
});

await using var coordinator = new GarbageCollectionCoordinator(new GarbageCollectionCoordinatorOptions
{
    LocalReplicaId = local,
    Consensus = consensus,
    FailureDetector = detector,
    Transport = transport,
    StartTransport = false,
});
coordinator.Register(value);
await coordinator.StartAsync();
```

The same `ITransport` can be shared with `ReplicationEngine<TState>` because CRDT state, consensus envelopes, version reports, and GC watermarks use distinct transport message types. See [`samples/Crdt.Samples.Gc`](../samples/Crdt.Samples.Gc) for a complete in-process cluster.
