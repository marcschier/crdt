# Crdt.Samples.Gossip

The same convergence story as [`Crdt.Samples`](../Crdt.Samples), but over a **real network transport**: three nodes gossip CRDT state to each other over loopback TCP using `Crdt.Transport`.

## What it demonstrates

- Wiring a CRDT into the replication stack: `CrdtReplica<TState>` + `ReplicationEngine<TState>` + `TcpGossipTransport`.
- Two independent 3-node clusters running concurrently — one replicating a `PNCounter`, one replicating a `Text` document.
- Each node mutates locally, broadcasts its state, and the clusters converge through periodic anti-entropy gossip.

## How it works

`startClusterAsync` creates three `TcpGossipTransport` instances bound to loopback on OS-assigned ports (`port 0`), reads each one's `LocalEndPoint`, and registers every node as a peer of every other. Each transport is wrapped in a `CrdtReplica<TState>` (with reflection-free serialize/deserialize delegates) and a `ReplicationEngine<TState>`.

Every node then applies a local mutation and calls `BroadcastStateAsync`. The engines exchange frames over TCP — both on the direct send and via the transport's periodic push-pull gossip loop — until `waitUntilAsync` observes that all replicas hold the same value. The `PNCounter` cluster converges to the net of all increments and decrements; the `Text` cluster converges to one document containing every node's contribution.

## Run it

From the repository root:

```shell
dotnet run --project samples/Crdt.Samples.Gossip -c Release
```

(Requires the .NET 10 SDK; the sample targets `net10.0`.)

### Expected output

```text
PNCounter converged: 27
Text converged: node-2;node-1;node-0;
```

The `Text` segment order is decided by the replicated sequence algorithm — it is deterministic and identical on every replica, so it may differ from the order the segments were appended in.

## NativeAOT smoke test

This sample doubles as a Native AOT smoke test of the transport stack. Publish it as a native binary and run it:

```shell
dotnet publish samples/Crdt.Samples.Gossip -c Release -r <rid> -p:AotSmoke=true
```

Replace `<rid>` with your runtime identifier (for example `win-x64`, `linux-x64`, or `osx-arm64`), then run the produced executable from `bin/Release/net10.0/<rid>/publish/`. The `AotSmoke` switch turns on `PublishAot` for this project only, so it never propagates into the multi-targeted `Crdt` library (which would fail its `netstandard2.1` target).

## Further reading

- [Transports](../../docs/transports.md) — in-memory, TCP (with optional TLS), UDP, and DTLS-secured datagram gossip.
- [Replication models](../../docs/replication-models.md) — when to use state, delta, or operations.
