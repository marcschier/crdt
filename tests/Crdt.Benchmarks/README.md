# CRDT Benchmarks

Convergence benchmarks answer: for CRDT *C* over transport *X*, with *N* replicas where *Y* of them make changes,
how fast does the state converge (every replica equal)? Mutate/merge/serialize micro-benchmarks per family also run.

Run the full convergence matrix from `tests\Crdt.Benchmarks`:

```pwsh
dotnet run -c Release --filter '*Convergence*'
```

Run a subset by BenchmarkDotNet filter:

```pwsh
dotnet run -c Release --filter '*Counter*'
dotnet run -c Release --filter '*Converge_GCounter*' --job dry
```

Convergence benchmarks vary replica count (`ReplicaCount` = 3, 10, 50), changed replica fraction (`ChangedFraction`
= One, Half, All), and transport (`InMemory`, `Tcp`, `Udp`, `NanoMsg`, `Pgm`). MQTT is included only when
`CRDT_MQTT_BROKER` is set, for example `CRDT_MQTT_BROKER=localhost:1883`.
