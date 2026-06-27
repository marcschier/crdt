# Crdt.Samples.Mqtt

The same convergence story as [`Crdt.Samples.Gossip`](../Crdt.Samples.Gossip), but over a **message broker**: three nodes gossip CRDT state to each other through an MQTT broker using `Crdt.Transport.Mqtt`.

## What it demonstrates

- Wiring a CRDT into the replication stack over MQTT pub/sub: `CrdtReplica<TState>` + `ReplicationEngine<TState>` + `MqttGossipTransport`.
- Two independent 3-node clusters running concurrently — one replicating a `PNCounter`, one replicating a `Text` document — isolated by per-type topic roots.
- Each node publishes its state to its own subtopic and subscribes to a wildcard over the shared root, ignoring its own messages; the broker fans every frame out to the other replicas until the clusters converge.

## How it works

`StartClusterAsync` creates three `MqttGossipTransport` instances that connect to the broker under a topic root of `crdt/sample/<run-id>/<type>`. Each replica publishes to `{root}/{clientId}` and subscribes to `{root}/+`, filtering out the messages it published itself. Every transport is wrapped in a `CrdtReplica<TState>` (with reflection-free serialize/deserialize delegates) and a `ReplicationEngine<TState>`.

Every node then applies a local mutation and calls `BroadcastStateAsync`. The broker delivers each frame to the other two replicas (and, because frames are published retained, to any replica that subscribes later) until `WaitUntilAsync` observes that all replicas hold the same value. The `PNCounter` cluster converges to the net of all increments and decrements; the `Text` cluster converges to one document containing every node's contribution.

## Run it

This sample needs a reachable MQTT broker. The quickest way is a local [Eclipse Mosquitto](https://mosquitto.org/) in Docker:

```shell
docker run -d --name crdt-mosquitto -p 1883:1883 eclipse-mosquitto:2 \
  sh -c "printf 'listener 1883 0.0.0.0\nallow_anonymous true\n' > /mosquitto/config/mosquitto.conf && exec /usr/sbin/mosquitto -c /mosquitto/config/mosquitto.conf"
```

Then, from the repository root:

```shell
dotnet run --project samples/Crdt.Samples.Mqtt -c Release
```

By default the sample connects to `mqtt://localhost:1883`. Point it elsewhere with a CLI argument or the `CRDT_MQTT_BROKER` environment variable:

```shell
dotnet run --project samples/Crdt.Samples.Mqtt -c Release -- mqtt://broker.example:1883
# or
CRDT_MQTT_BROKER=broker.example:1883 dotnet run --project samples/Crdt.Samples.Mqtt -c Release
```

(Requires the .NET 10 SDK; the sample targets `net10.0`.)

### Expected output

```text
Broker:    mqtt://localhost:1883
TopicRoot: crdt/sample/<run-id>
PNCounter converged: 27
Text converged: node-2;node-1;node-0;
```

The `Text` segment order is decided by the replicated sequence algorithm — it is deterministic and identical on every replica, so it may differ from the order the segments were appended in.

## NativeAOT smoke test

This sample doubles as a Native AOT smoke test of the MQTT transport stack. Publish it as a native binary and run it against a broker:

```shell
dotnet publish samples/Crdt.Samples.Mqtt -c Release -r <rid> -p:AotSmoke=true
```

Replace `<rid>` with your runtime identifier (for example `win-x64`, `linux-x64`, or `osx-arm64`), then run the produced executable from `bin/Release/net10.0/<rid>/publish/`. The `AotSmoke` switch turns on `PublishAot` for this project only, so it never propagates into the multi-targeted `Crdt` libraries (which would fail their `netstandard` targets).

## Further reading

- [Transports](../../docs/transports.md) — in-memory, TCP (with optional TLS), UDP, DTLS-secured datagram, and MQTT broker gossip.
- [Replication models](../../docs/replication-models.md) — when to use state, delta, or operations.
