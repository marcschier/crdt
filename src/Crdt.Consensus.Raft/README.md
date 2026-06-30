# Crdt.Consensus.Raft

An `IConsensus` implementation backed by the [`RaftCs`](https://www.nuget.org/packages/RaftCs) library
(`Raft` namespace): leader election, log replication, and committed membership over the CRDT transport.

## Wiring

Create a `RaftConsensusOptions` instance with:

- `LocalReplicaId` set to the durable local `ReplicaId`.
- `Transport` set to the CRDT transport used for Raft frames.
- `FailureDetector` set to the live membership source.
- Optionally, a shared `IReplicaIdRegistry` with explicit node id registrations.

Then construct and start `RaftConsensus`, and use it through `IConsensus`. The
default registry hashes `ReplicaId.Value` to a non-zero `ulong`; register
explicit ids when the cluster already has numeric node assignments or when you
need to eliminate the documented hash-collision caveat.

## RaftCs integration notes

The adapter observes role and leadership transitions from `RaftNode.StateChanges` and committed
membership from `RaftNode.CommittedConfigurations`, so it reflects committed Raft state rather than
polling. `CrdtRaftTransport` adapts `IRaftTransport` onto the broadcast CRDT `ITransport`, addressing
each frame to a recipient node id and filtering inbound frames for the local node.

