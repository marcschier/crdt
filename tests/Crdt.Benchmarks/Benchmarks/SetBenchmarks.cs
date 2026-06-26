// Copyright (c) marcschier. Licensed under the MIT License.

using BenchmarkDotNet.Attributes;

namespace Crdt.Benchmarks;

[MemoryDiagnoser]
public class SetBenchmarks
{
    private (GSet<long> Left, GSet<long> Right) _gSetMerge;
    private (TwoPhaseSet<long> Left, TwoPhaseSet<long> Right) _twoPhaseMerge;
    private (LWWElementSet<long> Left, LWWElementSet<long> Right) _lwwMerge;
    private (ORSet<long> Left, ORSet<long> Right) _orMerge;
    private Workloads.Encoded<GSet<long>> _gSet;
    private Workloads.Encoded<TwoPhaseSet<long>> _twoPhase;
    private Workloads.Encoded<LWWElementSet<long>> _lww;
    private Workloads.Encoded<ORSet<long>> _orSet;

    [Params(100, 1000)]
    public int Size { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _gSetMerge = Workloads.GSetPair(Size);
        _twoPhaseMerge = Workloads.TwoPhaseSetPair(Size);
        _lwwMerge = Workloads.LwwElementSetPair(Size);
        _orMerge = Workloads.OrSetPair(Size);
        _gSet = Workloads.Encode(
            Workloads.GSet(Size, 0),
            x => x.ToByteArray(CrdtValues.Int64),
            x => x.ToJson(CrdtValues.Int64));
        _twoPhase = Workloads.Encode(
            Workloads.TwoPhaseSet(Size, 0),
            x => x.ToByteArray(CrdtValues.Int64),
            x => x.ToJson(CrdtValues.Int64));
        _lww = Workloads.Encode(
            Workloads.LwwElementSet(Size, 41000, 0),
            x => x.ToByteArray(CrdtValues.Int64),
            x => x.ToJson(CrdtValues.Int64));
        _orSet = Workloads.Encode(
            Workloads.OrSet(Size, 42000, 0),
            x => x.ToByteArray(CrdtValues.Int64),
            x => x.ToJson(CrdtValues.Int64));
    }

    [Benchmark]
    public GSet<long> Mutate_GSet() => Workloads.GSet(Size, 0);

    [Benchmark]
    public TwoPhaseSet<long> Mutate_TwoPhaseSet() => Workloads.TwoPhaseSet(Size, 0);

    [Benchmark]
    public LWWElementSet<long> Mutate_LWWElementSet() => Workloads.LwwElementSet(Size, 43000, 0);

    [Benchmark]
    public ORSet<long> Mutate_ORSet() => Workloads.OrSet(Size, 44000, 0);

    [Benchmark]
    public GSet<long> Merge_GSet()
    {
        GSet<long> clone = _gSetMerge.Left.Clone();
        clone.Merge(_gSetMerge.Right);
        return clone;
    }

    [Benchmark]
    public TwoPhaseSet<long> Merge_TwoPhaseSet()
    {
        TwoPhaseSet<long> clone = _twoPhaseMerge.Left.Clone();
        clone.Merge(_twoPhaseMerge.Right);
        return clone;
    }

    [Benchmark]
    public LWWElementSet<long> Merge_LWWElementSet()
    {
        LWWElementSet<long> clone = _lwwMerge.Left.Clone();
        clone.Merge(_lwwMerge.Right);
        return clone;
    }

    [Benchmark]
    public ORSet<long> Merge_ORSet()
    {
        ORSet<long> clone = _orMerge.Left.Clone();
        clone.Merge(_orMerge.Right);
        return clone;
    }

    [Benchmark]
    public byte[] WriteBinary_GSet() => _gSet.Value.ToByteArray(CrdtValues.Int64);

    [Benchmark]
    public byte[] WriteBinary_TwoPhaseSet() => _twoPhase.Value.ToByteArray(CrdtValues.Int64);

    [Benchmark]
    public byte[] WriteBinary_LWWElementSet() => _lww.Value.ToByteArray(CrdtValues.Int64);

    [Benchmark]
    public byte[] WriteBinary_ORSet() => _orSet.Value.ToByteArray(CrdtValues.Int64);

    [Benchmark]
    public GSet<long> ReadBinary_GSet() => GSet<long>.ReadFrom(_gSet.Bytes, CrdtValues.Int64);

    [Benchmark]
    public TwoPhaseSet<long> ReadBinary_TwoPhaseSet() =>
        TwoPhaseSet<long>.ReadFrom(_twoPhase.Bytes, CrdtValues.Int64);

    [Benchmark]
    public LWWElementSet<long> ReadBinary_LWWElementSet() =>
        LWWElementSet<long>.ReadFrom(_lww.Bytes, CrdtValues.Int64);

    [Benchmark]
    public ORSet<long> ReadBinary_ORSet() => ORSet<long>.ReadFrom(_orSet.Bytes, CrdtValues.Int64);

    [Benchmark]
    public string WriteJson_GSet() => _gSet.Value.ToJson(CrdtValues.Int64);

    [Benchmark]
    public string WriteJson_TwoPhaseSet() => _twoPhase.Value.ToJson(CrdtValues.Int64);

    [Benchmark]
    public string WriteJson_LWWElementSet() => _lww.Value.ToJson(CrdtValues.Int64);

    [Benchmark]
    public string WriteJson_ORSet() => _orSet.Value.ToJson(CrdtValues.Int64);

    [Benchmark]
    public GSet<long> ReadJson_GSet() => GSet<long>.FromJson(_gSet.Json, CrdtValues.Int64);

    [Benchmark]
    public TwoPhaseSet<long> ReadJson_TwoPhaseSet() =>
        TwoPhaseSet<long>.FromJson(_twoPhase.Json, CrdtValues.Int64);

    [Benchmark]
    public LWWElementSet<long> ReadJson_LWWElementSet() =>
        LWWElementSet<long>.FromJson(_lww.Json, CrdtValues.Int64);

    [Benchmark]
    public ORSet<long> ReadJson_ORSet() => ORSet<long>.FromJson(_orSet.Json, CrdtValues.Int64);
}
