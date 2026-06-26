// Copyright (c) marcschier. Licensed under the MIT License.

using BenchmarkDotNet.Attributes;

namespace Crdt.Benchmarks;

[MemoryDiagnoser]
public class RegisterBenchmarks
{
    private (LWWRegister<long> Left, LWWRegister<long> Right) _lwwMerge;
    private (MVRegister<long> Left, MVRegister<long> Right) _mvMerge;
    private Workloads.Encoded<LWWRegister<long>> _lww;
    private Workloads.Encoded<MVRegister<long>> _mv;

    [Params(100, 1000)]
    public int Size { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _lwwMerge = Workloads.LwwRegisterPair(Size);
        _mvMerge = Workloads.MvRegisterPair(Size);
        _lww = Workloads.Encode(
            Workloads.LwwRegister(Size, 37000),
            x => x.ToByteArray(CrdtValues.Int64),
            x => x.ToJson(CrdtValues.Int64));
        _mv = Workloads.Encode(
            Workloads.MvRegister(Size, 38000, 0),
            x => x.ToByteArray(CrdtValues.Int64),
            x => x.ToJson(CrdtValues.Int64));
    }

    [Benchmark]
    public LWWRegister<long> Mutate_LWWRegister() => Workloads.LwwRegister(Size, 39000);

    [Benchmark]
    public MVRegister<long> Mutate_MVRegister() => Workloads.MvRegister(Size, 40000, 0);

    [Benchmark]
    public LWWRegister<long> Merge_LWWRegister()
    {
        LWWRegister<long> clone = _lwwMerge.Left.Clone();
        clone.Merge(_lwwMerge.Right);
        return clone;
    }

    [Benchmark]
    public MVRegister<long> Merge_MVRegister()
    {
        MVRegister<long> clone = _mvMerge.Left.Clone();
        clone.Merge(_mvMerge.Right);
        return clone;
    }

    [Benchmark]
    public byte[] WriteBinary_LWWRegister() => _lww.Value.ToByteArray(CrdtValues.Int64);

    [Benchmark]
    public byte[] WriteBinary_MVRegister() => _mv.Value.ToByteArray(CrdtValues.Int64);

    [Benchmark]
    public LWWRegister<long> ReadBinary_LWWRegister() =>
        LWWRegister<long>.ReadFrom(_lww.Bytes, CrdtValues.Int64);

    [Benchmark]
    public MVRegister<long> ReadBinary_MVRegister() => MVRegister<long>.ReadFrom(_mv.Bytes, CrdtValues.Int64);

    [Benchmark]
    public string WriteJson_LWWRegister() => _lww.Value.ToJson(CrdtValues.Int64);

    [Benchmark]
    public string WriteJson_MVRegister() => _mv.Value.ToJson(CrdtValues.Int64);

    [Benchmark]
    public LWWRegister<long> ReadJson_LWWRegister() => LWWRegister<long>.FromJson(_lww.Json, CrdtValues.Int64);

    [Benchmark]
    public MVRegister<long> ReadJson_MVRegister() => MVRegister<long>.FromJson(_mv.Json, CrdtValues.Int64);
}
