# CRDT Benchmarks

Run the full convergence matrix from `tests\Crdt.Benchmarks`:

```pwsh
dotnet run -c Release --filter '*Convergence*'
```

Run a subset by BenchmarkDotNet filter:

```pwsh
dotnet run -c Release --filter '*Counter*'
dotnet run -c Release --filter '*Converge_GCounter*' --job dry
```

Convergence benchmarks vary replica count (`ReplicaCount`), changed replica fraction (`ChangedFraction`),
and transport (`InMemory`, `Tcp`, `Udp`, `NanoMsg`). MQTT is included only when `CRDT_MQTT_BROKER` is set,
for example `CRDT_MQTT_BROKER=localhost:1883`.
