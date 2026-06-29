# Crdt.Samples.Pgm

The same convergence story as [`Crdt.Samples.Gossip`](../Crdt.Samples.Gossip), but over **PGM**
(Pragmatic General Multicast, RFC 3208): three nodes gossip CRDT state to each other across a multicast
session using `Crdt.Transport.Pgm`.

## What it demonstrates

- Wiring a CRDT into the replication stack over PGM: `CrdtReplica<TState>` + `ReplicationEngine<TState>` +
  `PgmBusTransport`.
- Two independent 3-node clusters running concurrently — one replicating a `PNCounter`, one replicating a
  `Text` document — each with its own in-memory multicast bus.
- Each node publishes to and subscribes from the same multicast session. The sample uses
  `Pgm.Net.InMemoryMulticastBus`, so no real multicast network or broker is needed.

## How it works

`startClusterAsync` creates three `PgmBusTransport` instances over one shared `InMemoryMulticastBus`. Each
transport is wrapped in a `CrdtReplica<TState>` (with reflection-free serialize/deserialize delegates) and a
`ReplicationEngine<TState>`.

Every node then applies a local mutation and re-broadcasts its state on a short loop until
`broadcastUntilConvergedAsync` observes that all replicas hold the same value. State merges are idempotent, so
repeated broadcasts are harmless. The `PNCounter` cluster converges to the net of all increments and decrements;
the `Text` cluster converges to one document containing every node's contribution.

## Run it

This sample is fully self-contained — the PGM transport uses a process-local in-memory multicast bus. From the
repository root:

```shell
dotnet run --project samples/Crdt.Samples.Pgm -c Release
```

(Requires the .NET 10 SDK; the sample targets `net10.0`.)

### Expected output

```text
PNCounter converged: 27
Text converged: node-2;node-1;node-0;
```

The `Text` segment order is decided by the replicated sequence algorithm — it is deterministic and identical on
every replica, so it may differ from the order the segments were appended in.

## NativeAOT smoke test

This sample doubles as a Native AOT smoke test of the PGM transport stack. Publish it as a native binary and run
it:

```shell
dotnet publish samples/Crdt.Samples.Pgm -c Release -r <rid> -p:AotSmoke=true
```

Replace `<rid>` with your runtime identifier (for example `win-x64`, `linux-x64`, or `osx-arm64`), then run the
produced executable from `bin/Release/net10.0/<rid>/publish/`. The `AotSmoke` switch turns on `PublishAot` for
this project only, so it never propagates into the multi-targeted `Crdt` libraries (which would fail their
`netstandard` targets).

## Further reading

- [Transports](../../docs/transports.md) — in-memory, TCP (with optional TLS), UDP, DTLS-secured, MQTT broker,
  nanomsg/NNG BUS gossip, and PGM multicast.
- [Replication models](../../docs/replication-models.md) — when to use state, delta, or operations.
