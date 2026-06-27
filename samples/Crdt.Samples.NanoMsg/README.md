# Crdt.Samples.NanoMsg

The same convergence story as [`Crdt.Samples.Gossip`](../Crdt.Samples.Gossip), but over the **nanomsg/NNG BUS** scalability protocol: three nodes gossip CRDT state to each other across a peer-to-peer BUS mesh using `Crdt.Transport.NanoMsg`.

## What it demonstrates

- Wiring a CRDT into the replication stack over a BUS mesh: `CrdtReplica<TState>` + `ReplicationEngine<TState>` + `NanoMsgBusTransport`.
- Two independent 3-node clusters running concurrently — one replicating a `PNCounter`, one replicating a `Text` document — each its own set of BUS sockets.
- Each node binds a loopback `tcp://` endpoint, dials its peers, broadcasts its state, and the clusters converge. BUS does not echo a node's own sends, so there is nothing to filter; no broker or native dependency is involved.

## How it works

`startClusterAsync` creates three `NanoMsgBusTransport` instances, each bound to `tcp://127.0.0.1:0` (an OS-assigned port). It reads each one's `BoundPort` and dials a single connection per pair (each node connects to every lower-indexed node — a BUS connection is bidirectional). Each transport is wrapped in a `CrdtReplica<TState>` (with reflection-free serialize/deserialize delegates) and a `ReplicationEngine<TState>`.

Every node then applies a local mutation and, because BUS dials in the background, re-broadcasts its state on a short loop until `broadcastUntilConvergedAsync` observes that all replicas hold the same value (state merges are idempotent, so repeated broadcasts are harmless). The `PNCounter` cluster converges to the net of all increments and decrements; the `Text` cluster converges to one document containing every node's contribution.

## Run it

This sample is fully self-contained — NanoMsgSharp is pure-managed, so no broker or native library is needed. From the repository root:

```shell
dotnet run --project samples/Crdt.Samples.NanoMsg -c Release
```

(Requires the .NET 10 SDK; the sample targets `net10.0`.)

### Expected output

```text
PNCounter converged: 27
Text converged: node-2;node-1;node-0;
```

The `Text` segment order is decided by the replicated sequence algorithm — it is deterministic and identical on every replica, so it may differ from the order the segments were appended in.

## NativeAOT smoke test

This sample doubles as a Native AOT smoke test of the NanoMsg transport stack. Publish it as a native binary and run it:

```shell
dotnet publish samples/Crdt.Samples.NanoMsg -c Release -r <rid> -p:AotSmoke=true
```

Replace `<rid>` with your runtime identifier (for example `win-x64`, `linux-x64`, or `osx-arm64`), then run the produced executable from `bin/Release/net10.0/<rid>/publish/`. The `AotSmoke` switch turns on `PublishAot` for this project only, so it never propagates into the multi-targeted `Crdt` libraries (which would fail their `netstandard` targets).

## Further reading

- [Transports](../../docs/transports.md) — in-memory, TCP (with optional TLS), UDP, DTLS-secured, MQTT broker, and nanomsg/NNG BUS gossip.
- [Replication models](../../docs/replication-models.md) — when to use state, delta, or operations.
