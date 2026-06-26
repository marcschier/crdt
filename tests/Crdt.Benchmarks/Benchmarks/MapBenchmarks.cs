// Copyright (c) marcschier. Licensed under the MIT License.

using BenchmarkDotNet.Attributes;

namespace Crdt.Benchmarks;

[MemoryDiagnoser]
public class MapBenchmarks
{
    private (LWWMap<long, long> Left, LWWMap<long, long> Right) _lwwMerge;
    private (ORMap<string, GCounter> Left, ORMap<string, GCounter> Right) _orMerge;
    private Workloads.Encoded<LWWMap<long, long>> _lww;
    private Workloads.Encoded<ORMap<string, GCounter>> _orMap;

    [Params(100, 1000)]
    public int Size { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _lwwMerge = Workloads.LwwMapPair(Size);
        _orMerge = Workloads.OrMapPair(Size);
        _lww = Workloads.Encode(
            Workloads.LwwMap(Size, 45000, 0),
            x => x.ToByteArray(CrdtValues.Int64, CrdtValues.Int64),
            x => x.ToJson(CrdtValues.Int64, CrdtValues.Int64));
        _orMap = Workloads.Encode(
            Workloads.OrMap(Size, 46000, 0),
            x => x.ToByteArray(CrdtValues.String),
            x => x.ToJson(CrdtValues.String));
    }

    [Benchmark]
    public LWWMap<long, long> Mutate_LWWMap() => Workloads.LwwMap(Size, 47000, 0);

    [Benchmark]
    public ORMap<string, GCounter> Mutate_ORMap() => Workloads.OrMap(Size, 48000, 0);

    [Benchmark]
    public LWWMap<long, long> Merge_LWWMap()
    {
        LWWMap<long, long> clone = _lwwMerge.Left.Clone();
        clone.Merge(_lwwMerge.Right);
        return clone;
    }

    [Benchmark]
    public ORMap<string, GCounter> Merge_ORMap()
    {
        ORMap<string, GCounter> clone = _orMerge.Left.Clone();
        clone.Merge(_orMerge.Right);
        return clone;
    }

    [Benchmark]
    public byte[] WriteBinary_LWWMap() => _lww.Value.ToByteArray(CrdtValues.Int64, CrdtValues.Int64);

    [Benchmark]
    public byte[] WriteBinary_ORMap() => _orMap.Value.ToByteArray(CrdtValues.String);

    [Benchmark]
    public LWWMap<long, long> ReadBinary_LWWMap() =>
        LWWMap<long, long>.ReadFrom(_lww.Bytes, CrdtValues.Int64, CrdtValues.Int64);

    [Benchmark]
    public ORMap<string, GCounter> ReadBinary_ORMap() =>
        ORMap<string, GCounter>.ReadFrom(_orMap.Bytes, CrdtValues.String, Workloads.GCounterOps);

    [Benchmark]
    public string WriteJson_LWWMap() => _lww.Value.ToJson(CrdtValues.Int64, CrdtValues.Int64);

    [Benchmark]
    public string WriteJson_ORMap() => _orMap.Value.ToJson(CrdtValues.String);

    [Benchmark]
    public LWWMap<long, long> ReadJson_LWWMap() =>
        LWWMap<long, long>.FromJson(_lww.Json, CrdtValues.Int64, CrdtValues.Int64);

    [Benchmark]
    public ORMap<string, GCounter> ReadJson_ORMap() =>
        ORMap<string, GCounter>.FromJson(_orMap.Json, CrdtValues.String, Workloads.GCounterOps);
}
