# Transports

`Crdt.Transport` provides reflection-free building blocks for moving CRDT state, deltas, and operations between replicas. The package is designed for NativeAOT: it never discovers serializers with reflection, and every replicated type is serialized through delegates supplied by the application.

## CrdtReplica

`CrdtReplica<TState>` wraps a mutable CRDT value plus the functions that serialize and deserialize that value. For non-generic CRDTs, those delegates usually call the type's binary helpers; for generic CRDTs, pass the serializer required by the CRDT type.

```csharp
var counter = new PNCounter();
var replica = new CrdtReplica<PNCounter>(
    counter,
    static c => c.ToByteArray(),
    static bytes => PNCounter.ReadFrom(bytes.Span));
```

Delta-capable CRDTs can use the overload that accepts `TryExtractDelta`, delta serialization, delta deserialization, and merge delegates. This keeps the transport layer independent of concrete CRDT implementations while still avoiding reflection.

## ITransport

`ITransport` is the minimal frame transport abstraction:

```csharp
public interface ITransport : IAsyncDisposable
{
    event Action<ReadOnlyMemory<byte>> FrameReceived;
    ValueTask StartAsync(CancellationToken ct = default);
    ValueTask SendAsync(ReadOnlyMemory<byte> frame, CancellationToken ct = default);
}
```

A frame is one complete length-prefixed message. `FrameCodec` encodes `[varint length][1-byte message type][payload]` and validates malformed, truncated, or oversized input before a payload is merged.

## ReplicationEngine

`ReplicationEngine<TState>` ties a `CrdtReplica<TState>` to an `ITransport`. In state mode, `BroadcastStateAsync` sends a full snapshot. In delta mode, it sends a delta when one is available and falls back to a full snapshot. In operation mode, call `BroadcastOperationAsync` with already serialized operation bytes and provide an apply delegate in the constructor.

```csharp
await using var network = new InMemoryNetwork();
ITransport transport = network.CreateTransport();
var engine = new ReplicationEngine<PNCounter>(replica, transport, ReplicationMode.State);

await engine.StartAsync();
counter.Increment(ReplicaId.New(), 5);
await engine.BroadcastStateAsync();
```

The engine raises `Changed` after a received state, delta, or operation is applied.

## In-memory transport

`InMemoryTransport` is deterministic and intended for tests, simulations, and single-process samples. An `InMemoryNetwork` registry holds all started peers; sending from one peer queues the frame to every other peer through channels, and each peer pumps queued frames into `FrameReceived`. Tests can call `DrainAsync` to wait until the network is idle without relying on fixed sleeps.

## TCP gossip transport

`TcpGossipTransport` binds a `TcpListener` and maintains a set of peer endpoints. `SendAsync` stores the most recent valid frame and pushes it to all peers. A periodic gossip loop picks a random peer and performs a push-pull exchange of the most recent frame, so state or delta snapshots continue to spread even if a direct send is missed. Reads use exact-length loops over the connection stream, and malformed frames or disconnects are dropped without stopping the listener. TLS can be enabled per transport — see [Securing transports with TLS](#securing-transports-with-tls).

Use port `0` to let the OS allocate a loopback port, then read `LocalEndPoint` after `StartAsync` and add the endpoints to the other nodes:

```csharp
var a = new TcpGossipTransport(IPAddress.Loopback, 0, TimeSpan.FromMilliseconds(100));
var b = new TcpGossipTransport(IPAddress.Loopback, 0, TimeSpan.FromMilliseconds(100));
await a.StartAsync();
await b.StartAsync();
a.AddPeer(b.LocalEndPoint);
b.AddPeer(a.LocalEndPoint);
```

## UDP datagram gossip transport

`UdpGossipTransport` is a connectionless alternative to the TCP transport. It binds a UDP socket and sends each frame as exactly one datagram. `SendAsync` stores the most recent valid frame and sends it to every known peer; a periodic anti-entropy loop re-sends the latest frame to a random peer, so updates keep spreading despite UDP's best-effort, lossy delivery.

```csharp
var a = new UdpGossipTransport(IPAddress.Loopback, 0, TimeSpan.FromMilliseconds(100));
var b = new UdpGossipTransport(IPAddress.Loopback, 0, TimeSpan.FromMilliseconds(100));
await a.StartAsync();
await b.StartAsync();
a.AddPeer(b.LocalEndPoint);
b.AddPeer(a.LocalEndPoint);
```

Because every frame travels in a single datagram, a frame must fit within `UdpGossipTransportOptions.MaxDatagramSize` (65507 bytes by default, the IPv4 UDP payload limit). `SendAsync` throws `ArgumentException` for larger frames. For state that does not fit in one datagram, prefer delta or operation mode, or use the TCP transport. Any single datagram may be lost; convergence relies on the periodic gossip loop.

## Securing transports with TLS

`TcpGossipTransport` can wrap each connection in TLS by setting `TcpGossipTransportOptions.Tls`. When `Tls` is `null` (the default) the transport stays plaintext. A gossip node is both a server (accepting peers) and a client (connecting to peers), so `GossipTlsOptions` carries both roles:

```csharp
var tls = new GossipTlsOptions
{
    ServerCertificate = certificate,                 // presented when accepting peers
    TargetHost = "gossip.internal",                  // SNI / validation host when connecting
    RemoteCertificateValidationCallback = ValidatePeer,
};
var transport = new TcpGossipTransport(new TcpGossipTransportOptions
{
    Address = IPAddress.Loopback,
    Port = 0,
    Tls = tls,
});
```

Set `RequireClientCertificate` and `ClientCertificates` for mutual TLS; the `RemoteCertificateValidationCallback` then also validates the peer's client certificate. `EnabledSslProtocols` defaults to `SslProtocols.None`, letting the operating system negotiate TLS 1.2/1.3.

## Secure datagrams (DTLS)

`DtlsGossipTransport`, in the separate opt-in package **`Crdt.Transport.Dtls`** (built on [DtlsSharp](https://github.com/marcschier/dtls)), is the DTLS-secured counterpart to `UdpGossipTransport`. Each peer pair is protected by a DTLS session: outbound frames travel over a client session (`DtlsClient`), and inbound datagrams are demultiplexed by remote endpoint and accepted as per-peer server sessions (`DtlsServer`). The core `Crdt.Transport` package stays dependency-free.

Like the core package and `Crdt.Transport.Mqtt`, it targets the full set — `netstandard2.0`, `netstandard2.1`, `net8.0`, `net9.0`, and `net10.0` (the netstandard builds are polyfilled; `net8.0`+ output is unaffected).

```shell
dotnet add package Crdt.Transport.Dtls
```

Authentication is configured through DtlsSharp's `DtlsServerOptions` (server role) and `DtlsClientOptions` (client role) — certificate, pre-shared key (PSK), or raw public keys (RFC 7250), over DTLS 1.2 or 1.3. The example below uses a shared PSK:

```csharp
static readonly byte[] identity = Encoding.UTF8.GetBytes("gossip");
static readonly byte[] key = RandomNumberGenerator.GetBytes(16);

var transport = new DtlsGossipTransport(new DtlsGossipTransportOptions
{
    Address = IPAddress.Loopback,
    Port = 0,
    ServerOptions = new DtlsServerOptions
    {
        MinimumVersion = DtlsProtocolVersion.Dtls13,
        MaximumVersion = DtlsProtocolVersion.Dtls13,
        PskCallback = _ => key,
    },
    ClientOptions = new DtlsClientOptions
    {
        MinimumVersion = DtlsProtocolVersion.Dtls13,
        MaximumVersion = DtlsProtocolVersion.Dtls13,
        PskCallback = _ => new PskCredential(identity, key),
    },
});
await transport.StartAsync();
transport.AddPeer(peerEndPoint);
```

As with the plaintext UDP transport, each frame is one datagram (bounded by `MaxDatagramSize`) and delivery is best-effort; convergence is driven by the periodic gossip loop.

## MQTT broker gossip

`MqttGossipTransport`, in the separate opt-in package **`Crdt.Transport.Mqtt`** (built on [Mqtt.Client](https://www.nuget.org/packages/Mqtt.Client)), replicates frames through an MQTT broker instead of peer-to-peer. It is a natural fit when replicas cannot reach each other directly but can all reach a broker (MQTT 3.1.1/5.0 over TCP, TLS, or WebSockets). The core `Crdt.Transport` package stays dependency-free.

```shell
dotnet add package Crdt.Transport.Mqtt
```

Each replica **publishes** its frames to its own subtopic, `{TopicRoot}/{ClientId}`, and **subscribes** to a single-level wildcard over the shared root, `{TopicRoot}/+`, ignoring messages whose last topic segment is its own client id. The broker fans every frame out to the other replicas, so there is no peer list to maintain:

```csharp
var transport = new MqttGossipTransport(new MqttGossipTransportOptions
{
    BrokerUri = "mqtt://broker.internal:1883",  // mqtt / mqtts / ws / wss
    TopicRoot = "crdt/gossip",                  // shared by every replica in the group
    ClientId = "node-a",                        // unique per replica; also the publish subtopic
    Qos = MqttQoS.AtLeastOnce,                  // QoS 1 retries until acknowledged
});
await transport.StartAsync();                   // connects and subscribes
```

`ClientId` must be a single topic segment (no `/`, `+`, or `#`) and unique per broker connection; `TopicRoot` must not contain the wildcards `+` or `#`. A frame may not exceed `MaxFrameLength`; `SendAsync` throws `ArgumentException` for larger frames.

By default each frame is published **retained** (`RetainLastFrame`), so a late-joining replica immediately receives every peer's most recent state when it subscribes — a broker-native form of anti-entropy that pairs well with state mode. Set `RetainLastFrame = false` to publish without retention.

Authentication and secure transports are configured either through the discrete `Username`/`Password` options or through the `ConfigureClient` hook, which exposes the underlying `Mqtt.Client` builder for TLS, a SOCKS5 proxy, a reconnect policy, keep-alive, or logging:

```csharp
var transport = new MqttGossipTransport(new MqttGossipTransportOptions
{
    BrokerUri = "mqtts://broker.internal:8883",
    TopicRoot = "crdt/gossip",
    ClientId = "node-a",
    Username = "gossip",
    Password = secret,
    ConfigureClient = builder => builder
        .WithTls(tls => { /* certificate validation, client cert, ... */ })
        .WithKeepAlive(30),
});
```

Self-delivery is avoided by the subtopic-and-filter scheme, so the transport works against both MQTT 3.1.1 and 5.0 brokers without relying on the MQTT 5 *No Local* flag. Even if a frame were redelivered to its publisher, CRDT merges are idempotent, so convergence is unaffected.

## Scalability protocols (BUS)

`NanoMsgBusTransport`, in the separate opt-in package **`Crdt.Transport.NanoMsg`** (built on [NanoMsgSharp](https://www.nuget.org/packages/NanoMsgSharp), a pure-managed nanomsg/NNG implementation), replicates frames over the **BUS** scalability protocol — a peer-to-peer, many-to-many broadcast mesh. It needs no broker and no native dependency, and works over any NanoMsgSharp transport scheme: `tcp://`, `tls+tcp://`, `ipc://`, `ws://`, `wss://`, or `inproc://`.

```shell
dotnet add package Crdt.Transport.NanoMsg
```

Each replica binds a local endpoint and dials its peers; `SendAsync` broadcasts every frame to all directly connected peers, and BUS does not echo a node's own sends, so there is nothing to filter. Use a `tcp://…:0` bind to let the OS assign a port and read it back from `BoundPort`:

```csharp
var a = new NanoMsgBusTransport(new NanoMsgBusTransportOptions { BindAddress = "tcp://127.0.0.1:0" });
var b = new NanoMsgBusTransport(new NanoMsgBusTransportOptions { BindAddress = "tcp://127.0.0.1:0" });
await a.StartAsync();
await b.StartAsync();
a.AddPeer($"tcp://127.0.0.1:{b.BoundPort}");   // one connection per pair is bidirectional
```

A frame may not exceed `MaxFrameLength`; `SendAsync` throws `ArgumentException` for larger frames. TLS (`tls+tcp://`, `wss://`), timeouts, watermarks, and message-size limits are configured through the underlying `NanoSocketOptions` on `SocketOptions`. BUS delivers only to directly connected peers (no multi-hop forwarding), so peers should form a connected mesh; the application's broadcast cadence then drives convergence. `Connect` dials in the background and reconnects automatically.

## PGM reliable multicast

`PgmBusTransport`, in the separate opt-in package **`Crdt.Transport.Pgm`** (built on [Pgm](https://www.nuget.org/packages/Pgm), a pure-managed PGM/RFC 3208 implementation), replicates frames over reliable multicast. Every replica publishes CRDT frames to one multicast group and receives frames from the same group, so there is no broker and no peer list to maintain.

```shell
dotnet add package Crdt.Transport.Pgm
```

Configure every replica with the same multicast group and UDP port:

```csharp
var transport = new PgmBusTransport(new PgmBusTransportOptions
{
    MulticastGroup = IPAddress.Parse("239.192.0.42"),
    Port = 7500,
});
await transport.StartAsync();
```

A frame may not exceed `MaxFrameLength`; `SendAsync` throws `ArgumentException` for larger frames. `PgmBusTransport` validates every outbound and inbound frame with `FrameCodec`. For deterministic process-local tests, set `InMemoryBus` to a shared `InMemoryMulticastBus` (or set `UseInMemoryBus = true` to use the transport's shared bus); the transport creates one publisher channel and one subscriber channel from that bus.

PGM repairs dropped packets within the publisher's transmit window and preserves ordering per publisher. Multicast environments may also deliver a node's own publication back to its subscriber; CRDT merges are idempotent, so self-delivery does not affect convergence.

## Modes

State mode is the simplest and most robust choice: every message carries a complete snapshot and merges are idempotent. Delta mode reduces bandwidth for CRDTs that implement `IDeltaConvergent<TState,TDelta>`; the application supplies the extraction and merge delegates. Operation mode is for CRDT operation payloads that are already idempotent, such as `PNCounterOperation`; the engine applies each operation through the caller's delegate.
