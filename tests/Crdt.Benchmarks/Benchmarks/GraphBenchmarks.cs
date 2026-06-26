// Copyright (c) marcschier. Licensed under the MIT License.

using BenchmarkDotNet.Attributes;

namespace Crdt.Benchmarks;

[MemoryDiagnoser]
public class GraphBenchmarks
{
    private (TwoPTwoPGraph<long> Left, TwoPTwoPGraph<long> Right) _twoPTwoPMerge;
    private (AddOnlyDag<long> Left, AddOnlyDag<long> Right) _addOnlyDagMerge;
    private Workloads.Encoded<TwoPTwoPGraph<long>> _twoPTwoP;
    private Workloads.Encoded<AddOnlyDag<long>> _addOnlyDag;

    [Params(100, 1000)]
    public int Size { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _twoPTwoPMerge = Workloads.TwoPTwoPGraphPair(Size);
        _addOnlyDagMerge = Workloads.AddOnlyDagPair(Size);
        _twoPTwoP = Workloads.Encode(
            Workloads.TwoPTwoPGraph(Size, 0),
            x => x.ToByteArray(CrdtValues.Int64),
            x => x.ToJson(CrdtValues.Int64));
        _addOnlyDag = Workloads.Encode(
            Workloads.AddOnlyDag(Size, 0),
            x => x.ToByteArray(CrdtValues.Int64),
            x => x.ToJson(CrdtValues.Int64));
    }

    [Benchmark]
    public TwoPTwoPGraph<long> Mutate_TwoPTwoPGraph() => Workloads.TwoPTwoPGraph(Size, 0);

    [Benchmark]
    public AddOnlyDag<long> Mutate_AddOnlyDag() => Workloads.AddOnlyDag(Size, 0);

    [Benchmark]
    public TwoPTwoPGraph<long> Merge_TwoPTwoPGraph()
    {
        TwoPTwoPGraph<long> clone = _twoPTwoPMerge.Left.Clone();
        clone.Merge(_twoPTwoPMerge.Right);
        return clone;
    }

    [Benchmark]
    public AddOnlyDag<long> Merge_AddOnlyDag()
    {
        AddOnlyDag<long> clone = _addOnlyDagMerge.Left.Clone();
        clone.Merge(_addOnlyDagMerge.Right);
        return clone;
    }

    [Benchmark]
    public byte[] WriteBinary_TwoPTwoPGraph() => _twoPTwoP.Value.ToByteArray(CrdtValues.Int64);

    [Benchmark]
    public byte[] WriteBinary_AddOnlyDag() => _addOnlyDag.Value.ToByteArray(CrdtValues.Int64);

    [Benchmark]
    public TwoPTwoPGraph<long> ReadBinary_TwoPTwoPGraph() =>
        TwoPTwoPGraph<long>.ReadFrom(_twoPTwoP.Bytes, CrdtValues.Int64);

    [Benchmark]
    public AddOnlyDag<long> ReadBinary_AddOnlyDag() => AddOnlyDag<long>.ReadFrom(_addOnlyDag.Bytes, CrdtValues.Int64);

    [Benchmark]
    public string WriteJson_TwoPTwoPGraph() => _twoPTwoP.Value.ToJson(CrdtValues.Int64);

    [Benchmark]
    public string WriteJson_AddOnlyDag() => _addOnlyDag.Value.ToJson(CrdtValues.Int64);

    [Benchmark]
    public TwoPTwoPGraph<long> ReadJson_TwoPTwoPGraph() =>
        TwoPTwoPGraph<long>.FromJson(_twoPTwoP.Json, CrdtValues.Int64);

    [Benchmark]
    public AddOnlyDag<long> ReadJson_AddOnlyDag() => AddOnlyDag<long>.FromJson(_addOnlyDag.Json, CrdtValues.Int64);
}
