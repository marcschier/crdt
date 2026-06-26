// Copyright (c) marcschier. Licensed under the MIT License.

using BenchmarkDotNet.Attributes;

namespace Crdt.Benchmarks;

[MemoryDiagnoser]
public class CounterBenchmarks
{
    private (GCounter Left, GCounter Right) _gCounterMerge;
    private (PNCounter Left, PNCounter Right) _pnCounterMerge;
    private Workloads.Encoded<GCounter> _gCounter;
    private Workloads.Encoded<PNCounter> _pnCounter;

    [Params(100, 1000)]
    public int Size { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _gCounterMerge = Workloads.GCounterPair(Size);
        _pnCounterMerge = Workloads.PNCounterPair(Size);
        _gCounter = Workloads.Encode(Workloads.GCounter(Size, 33000), x => x.ToByteArray(), x => x.ToJson());
        _pnCounter = Workloads.Encode(Workloads.PNCounter(Size, 34000), x => x.ToByteArray(), x => x.ToJson());
    }

    [Benchmark]
    public GCounter Mutate_GCounter() => Workloads.GCounter(Size, 35000);

    [Benchmark]
    public PNCounter Mutate_PNCounter() => Workloads.PNCounter(Size, 36000);

    [Benchmark]
    public GCounter Merge_GCounter()
    {
        GCounter clone = _gCounterMerge.Left.Clone();
        clone.Merge(_gCounterMerge.Right);
        return clone;
    }

    [Benchmark]
    public PNCounter Merge_PNCounter()
    {
        PNCounter clone = _pnCounterMerge.Left.Clone();
        clone.Merge(_pnCounterMerge.Right);
        return clone;
    }

    [Benchmark]
    public byte[] WriteBinary_GCounter() => _gCounter.Value.ToByteArray();

    [Benchmark]
    public byte[] WriteBinary_PNCounter() => _pnCounter.Value.ToByteArray();

    [Benchmark]
    public GCounter ReadBinary_GCounter() => GCounter.ReadFrom(_gCounter.Bytes);

    [Benchmark]
    public PNCounter ReadBinary_PNCounter() => PNCounter.ReadFrom(_pnCounter.Bytes);

    [Benchmark]
    public string WriteJson_GCounter() => _gCounter.Value.ToJson();

    [Benchmark]
    public string WriteJson_PNCounter() => _pnCounter.Value.ToJson();

    [Benchmark]
    public GCounter ReadJson_GCounter() => GCounter.FromJson(_gCounter.Json);

    [Benchmark]
    public PNCounter ReadJson_PNCounter() => PNCounter.FromJson(_pnCounter.Json);
}
