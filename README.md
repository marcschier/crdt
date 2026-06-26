# Crdt

[![CI](https://github.com/marcschier/crdt/actions/workflows/ci.yml/badge.svg)](https://github.com/marcschier/crdt/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/Crdt.svg)](https://github.com/marcschier/crdt/packages)

High-performance, **NativeAOT-ready** [Conflict-free Replicated Data Types](https://en.wikipedia.org/wiki/Conflict-free_replicated_data_type) (CRDTs) for modern .NET.

`Crdt` implements the full catalogue of CRDTs — counters, registers, sets, maps, flags, graphs, and sequence/text types — in **state-based**, **delta-state**, and **operation-based** flavours, with compact binary and `System.Text.Json` source-generated serialization.

## ✨ Why Crdt

- **Strong eventual consistency** without coordination: replicas converge after exchanging state, deltas, or operations.
- **Fast**: `readonly struct` building blocks, `Span<T>`/`IBufferWriter<byte>` serialization, `BinaryPrimitives`/`Unsafe` fast paths, no LINQ on hot paths.
- **NativeAOT & trimming clean** on .NET 8/9/10 — the library is annotated `IsAotCompatible`, and the test suite itself is verified running as a NativeAOT binary.
- **Broad reach**: targets `netstandard2.1`, `net8.0`, `net9.0`, `net10.0` (polyfilled on older runtimes).

## Supported target frameworks

| TFM | Notes |
| --- | --- |
| `net10.0`, `net9.0`, `net8.0` | Full feature set; NativeAOT supported. |
| `netstandard2.1` | Compatibility target (Unity / Mono / .NET Core 3.x) via polyfills; not itself AOT-published. |

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

## 🧩 CRDT catalogue

| Family | Types |
| --- | --- |
| Counters | `GCounter`, `PNCounter` |
| Registers | `LWWRegister<T>`, `MVRegister<T>` |
| Sets | `GSet<T>`, `TwoPhaseSet<T>`, `LWWElementSet<T>`, `ORSet<T>` |
| Maps | `ORMap<TKey,TValue>`, `LWWMap<TKey,TValue>` |
| Flags | `EnableWinsFlag`, `DisableWinsFlag`, `GFlag` |
| Graphs | `TwoPTwoPGraph`, `AddOnlyDag` |
| Sequences / Text | `Rga<T>`, `LogootSequence<T>`, `LSeqSequence<T>`, `TreedocSequence<T>`, `YataSequence<T>`, `WootSequence<T>`, `Text` |

## 🔁 Replication models

- **State-based (CvRDT)** — exchange and `Merge` full states. Robust to reordering and duplication.
- **Delta-state** — propagate small deltas of the same lattice; converges with a causal delta protocol.
- **Operation-based (CmRDT)** — propagate operations carrying their causal context; effects are idempotent / de-duplicated.

`Crdt` provides the data structures, merge/delta/operation logic, and serialization. Wiring replicas to a network (gossip, anti-entropy, broadcast) is left to the application.

## 📚 Documentation

The full developer guide lives in [`docs/`](https://github.com/marcschier/crdt/tree/master/docs):

- [Getting started](https://github.com/marcschier/crdt/blob/master/docs/getting-started.md) — install, your first counter, merging replicas.
- [Data types](https://github.com/marcschier/crdt/blob/master/docs/data-types.md) — the full catalogue with usage examples for every CRDT.
- [Replication models](https://github.com/marcschier/crdt/blob/master/docs/replication-models.md) — state-based, delta-state, and operation-based, and when to use each.
- [Serialization](https://github.com/marcschier/crdt/blob/master/docs/serialization.md) — the binary and JSON formats, value serializers, and hostile-input limits.
- [Architecture](https://github.com/marcschier/crdt/blob/master/docs/architecture.md) — semilattices, dots, the causal kernel (ORSWOT), and the Hybrid Logical Clock.
- [Performance & NativeAOT](https://github.com/marcschier/crdt/blob/master/docs/performance.md) — AOT compliance, trimming, and benchmarking notes.
- [Transports](https://github.com/marcschier/crdt/blob/master/docs/transports.md) — the optional `Crdt.Transport` package: in-memory and TCP gossip replication.

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
