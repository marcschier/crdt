# Performance & NativeAOT

## Design for speed

The library is built for low allocation and high throughput:

- Identity and event types (`ReplicaId`, `Dot`, `Timestamp`) are `readonly struct`s.
- Serialization is `Span<T>`/`IBufferWriter<byte>` based, with LEB128 varints and pooled buffers; there is no reflection on any hot path.
- Hot paths avoid LINQ and unnecessary allocation.
- On .NET 8+ the build uses the runtime's modern primitives (`BinaryPrimitives`, `MemoryMarshal`, `Unsafe`); on `netstandard2.0` and `netstandard2.1` the same code paths are kept working through polyfills.

## NativeAOT

On .NET 8 and later the library is annotated `IsAotCompatible`, which turns on the trimming and AOT analyzers during the build — so AOT incompatibilities are caught at compile time, as build errors, rather than at publish time.

The test suite itself is published and executed as a NativeAOT binary in CI, which proves end to end that the public API — including binary and source-generated JSON serialization — runs without a JIT:

```shell
dotnet publish tests/Crdt.Tests/Crdt.Tests.csproj -c Release -f net10.0 -r <rid> -p:AotTest=true
./tests/Crdt.Tests/bin/Release/net10.0/<rid>/publish/Crdt.Tests
```

`netstandard2.0` and `netstandard2.1` are compatibility targets for older runtimes (.NET Framework 4.6.1+, Unity, Mono, .NET Core 3.x) and are not themselves NativeAOT-published; AOT applies to the net8/9/10 targets.

## Notes on complexity

- Counters, registers, sets, maps, and flags are backed by hash maps; merges and lookups are near-linear in the number of entries.
- Observed-remove types keep no tombstones, so their size tracks *live* data plus a compact causal context rather than growing with every deletion.
- `Rga<T>` resolves the visible sequence with a pre-order traversal; index-based insert/delete are linear in the document length. For very large documents this is the main thing to measure for your workload.

## Benchmarking

The repository includes a BenchmarkDotNet project under `tests/Crdt.Benchmarks` covering per-family merge, mutation, and binary/JSON serialization comparisons. Run it with:

```shell
dotnet run -c Release --project tests/Crdt.Benchmarks
```

Because BenchmarkDotNet spawns and instruments processes, the benchmark project runs under the JIT, not NativeAOT.
