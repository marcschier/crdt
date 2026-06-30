# Crdt.Consensus.Raft

This project is a scaffolded `IConsensus` implementation backed by the `RaftCs`
library (`Raft` namespace). It is intentionally not added to `Crdt.slnx` until
`RaftCs` and `RaftCs.Transport` are published to nuget.org.

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

## Deferred build/test

Build and test are deferred until the placeholder package versions in
`Directory.Packages.props` resolve from nuget.org. Once published, add this
project to `Crdt.slnx`, run restore/build/test, and replace the placeholder
version comment with the validated published version.

## RaftCs integration notes

RaftCs currently exposes `RaftNode.Role` and `RaftNode.LeaderId` as polling
properties. This adapter polls those values for `LeadershipChanged`. It also
tracks requested membership changes optimistically because RaftCs does not yet
expose a committed `ConfState` snapshot or state-changed event. A future RaftCs
state-changed/configuration event would let this adapter raise membership events
from committed Raft state instead of the failure detector view.
