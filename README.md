# Crdt

[![CI](https://github.com/marcschier/crdt/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/marcschier/crdt/actions/workflows/ci.yml) [![NuGet](https://img.shields.io/nuget/v/Crdt?logo=nuget&label=NuGet)](https://www.nuget.org/packages/Crdt) [![GitHub Packages](https://img.shields.io/badge/GitHub%20Packages-Crdt-2088FF?logo=github&logoColor=white)](https://github.com/marcschier/crdt/pkgs/nuget/Crdt)

High-performance, **NativeAOT-ready** [Conflict-free Replicated Data Types](https://en.wikipedia.org/wiki/Conflict-free_replicated_data_type) (CRDTs) for modern .NET.

`Crdt` implements the full catalogue of CRDTs — counters, registers, sets, maps, flags, graphs, trees, JSON documents, and sequence/text types — in **state-based**, **delta-state**, and **operation-based** flavours, with compact binary and `System.Text.Json` source-generated serialization. It also includes advanced/esoteric variants (bounded, resettable, and handoff counters, causal-length sets, a tree with move, Fugue, and interval tree clocks).

## ✨ Why Crdt

- **Strong eventual consistency** without coordination: replicas converge after exchanging state, deltas, or operations.
- **Fast**: `readonly struct` building blocks, `Span<T>`/`IBufferWriter<byte>` serialization, `BinaryPrimitives`/`Unsafe` fast paths, no LINQ on hot paths.
- **NativeAOT & trimming clean** on .NET 8/9/10 — the library is annotated `IsAotCompatible`, and the test suite itself is verified running as a NativeAOT binary.
- **Broad reach**: targets `netstandard2.0`, `netstandard2.1`, `net8.0`, `net9.0`, `net10.0` (polyfilled on older runtimes).

## Supported target frameworks

| TFM | Notes |
| --- | --- |
| `net10.0`, `net9.0`, `net8.0` | Full feature set; NativeAOT supported. |
| `netstandard2.1` | Compatibility target (Unity / Mono / .NET Core 3.x) via polyfills; not itself AOT-published. |
| `netstandard2.0` | Broad compatibility target (.NET Framework 4.6.1+, older Unity / Mono) via `System.Memory` + `Microsoft.Bcl.HashCode` and source polyfills; not AOT-published. |

## 📦 Install

```shell
dotnet add package Crdt
```

The optional replication/transport layer — in-memory, TCP (with optional TLS), and UDP anti-entropy gossip, in state-based, delta-state, and operation-based modes — ships as a separate package:

```shell
dotnet add package Crdt.Transport
```

DTLS-secured datagram gossip (built on [DtlsSharp](https://github.com/marcschier/dtls)) lives in a further opt-in package:

```shell
dotnet add package Crdt.Transport.Dtls
```

MQTT broker gossip (built on [Mqtt.Client](https://www.nuget.org/packages/Mqtt.Client)) — for replicas that reach each other through an MQTT 3.1.1/5.0 broker rather than peer-to-peer — ships as another opt-in package:

```shell
dotnet add package Crdt.Transport.Mqtt
```

Peer-to-peer gossip over the nanomsg/NNG **BUS** scalability protocol (built on [NanoMsgSharp](https://www.nuget.org/packages/NanoMsgSharp), pure-managed, no broker) — over TCP, TLS, IPC, WebSockets, or in-process — ships as another opt-in package:

```shell
dotnet add package Crdt.Transport.NanoMsg
```

PGM reliable multicast (built on [Pgm](https://www.nuget.org/packages/Pgm), pure-managed RFC 3208 multicast) ships as another opt-in package:

```shell
dotnet add package Crdt.Transport.Pgm
```

## 🧩 CRDT catalogue

| Family | Types |
| --- | --- |
| Counters | `GCounter`, `PNCounter`, `BCounter`, `ResettableCounter`, `HandoffCounter` |
| Registers | `LWWRegister<T>`, `MVRegister<T>` |
| Sets | `GSet<T>`, `TwoPhaseSet<T>`, `LWWElementSet<T>`, `ORSet<T>`, `CausalLengthSet<T>` |
| Maps | `ORMap<TKey,TValue>`, `LWWMap<TKey,TValue>` |
| Flags | `EnableWinsFlag`, `DisableWinsFlag`, `GFlag` |
| Graphs | `TwoPTwoPGraph`, `AddOnlyDag` |
| Trees | `ReplicatedTree` (highly-available move) |
| Documents | `JsonCrdt` (nested JSON) |
| Sequences / Text | `Rga<T>`, `LogootSequence<T>`, `LSeqSequence<T>`, `TreedocSequence<T>`, `YataSequence<T>`, `WootSequence<T>`, `FugueSequence<T>`, `Text` |
| Causal clocks | `HybridLogicalClock`, `VersionVector`, `IntervalTreeClock` |

The `Counters`, `Sets`, `Trees`, `Documents`, `Sequences`, and `Causal clocks` rows include several **advanced / esoteric CRDTs** (bounded, resettable, and handoff counters; causal-length set; tree-with-move; JSON document; Fugue; interval tree clocks). See [Advanced / esoteric CRDTs](./docs/data-types.md#advanced--esoteric-crdts) for what each one is for.

## 🔁 Replication models

- **State-based (CvRDT)** — exchange and `Merge` full states. Robust to reordering and duplication.
- **Delta-state** — propagate small deltas of the same lattice; converges with a causal delta protocol.
- **Operation-based (CmRDT)** — propagate operations carrying their causal context; effects are idempotent / de-duplicated.

`Crdt` provides the data structures, merge/delta/operation logic, and serialization. Wiring replicas to a network (gossip, anti-entropy, broadcast) is left to the application.

## 📚 Documentation

- [Getting started](./docs/getting-started.md) — install, your first counter, merging replicas.
- [Data types](./docs/data-types.md) — the full catalogue with usage examples for every CRDT.
- [Choosing a data type](./docs/choosing-data-types.md) — when to use which type, when not to, and rough performance/resource cost.
- [Replication models](./docs/replication-models.md) — state-based, delta-state, and operation-based, and when to use each.
- [Garbage collection](./docs/garbage-collection.md) — causal stability, stable cuts, tombstone reclamation, and pluggable consensus.
- [Serialization](./docs/serialization.md) — the binary and JSON formats, value serializers, and hostile-input limits.
- [Architecture](./docs/architecture.md) — semilattices, dots, the causal kernel (ORSWOT), and the Hybrid Logical Clock.
- [Performance & NativeAOT](./docs/performance.md) — AOT compliance, trimming, and benchmarking notes.
- [Transports](./docs/transports.md) — the optional `Crdt.Transport` package: in-memory, TCP/UDP, DTLS-secured, MQTT broker, nanomsg/NNG BUS, and PGM multicast gossip replication.

## 🛠️ Building from source

```shell
dotnet build Crdt.slnx -c Release
dotnet test  Crdt.slnx -c Release
```

Linting / formatting:

```shell
dotnet format Crdt.slnx --verify-no-changes
pwsh -NoProfile -File eng/check-line-length.ps1
```

NativeAOT test gate (publishes and runs the suite as a native binary):

```shell
dotnet publish tests/Crdt.Tests/Crdt.Tests.csproj -c Release -f net10.0 -r <rid> -p:AotTest=true
```

## 📄 License

[MIT](LICENSE)
