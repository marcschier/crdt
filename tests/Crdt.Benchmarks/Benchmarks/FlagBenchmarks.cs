// Copyright (c) marcschier. Licensed under the MIT License.

using BenchmarkDotNet.Attributes;

namespace Crdt.Benchmarks;

[MemoryDiagnoser]
public class FlagBenchmarks
{
    private (GFlag Left, GFlag Right) _gFlagMerge;
    private (EnableWinsFlag Left, EnableWinsFlag Right) _enableWinsMerge;
    private (DisableWinsFlag Left, DisableWinsFlag Right) _disableWinsMerge;
    private Workloads.Encoded<GFlag> _gFlag;
    private Workloads.Encoded<EnableWinsFlag> _enableWins;
    private Workloads.Encoded<DisableWinsFlag> _disableWins;

    [Params(100, 1000)]
    public int Size { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _gFlagMerge = Workloads.GFlagPair(Size);
        _enableWinsMerge = Workloads.EnableWinsFlagPair(Size);
        _disableWinsMerge = Workloads.DisableWinsFlagPair(Size);
        _gFlag = Workloads.Encode(Workloads.GFlag(Size), x => x.ToByteArray(), x => x.ToJson());
        _enableWins = Workloads.Encode(
            Workloads.EnableWinsFlag(Size, 49000),
            x => x.ToByteArray(),
            x => x.ToJson());
        _disableWins = Workloads.Encode(
            Workloads.DisableWinsFlag(Size, 50000),
            x => x.ToByteArray(),
            x => x.ToJson());
    }

    [Benchmark]
    public GFlag Mutate_GFlag() => Workloads.GFlag(Size);

    [Benchmark]
    public EnableWinsFlag Mutate_EnableWinsFlag() => Workloads.EnableWinsFlag(Size, 51000);

    [Benchmark]
    public DisableWinsFlag Mutate_DisableWinsFlag() => Workloads.DisableWinsFlag(Size, 52000);

    [Benchmark]
    public GFlag Merge_GFlag()
    {
        GFlag clone = _gFlagMerge.Left.Clone();
        clone.Merge(_gFlagMerge.Right);
        return clone;
    }

    [Benchmark]
    public EnableWinsFlag Merge_EnableWinsFlag()
    {
        EnableWinsFlag clone = _enableWinsMerge.Left.Clone();
        clone.Merge(_enableWinsMerge.Right);
        return clone;
    }

    [Benchmark]
    public DisableWinsFlag Merge_DisableWinsFlag()
    {
        DisableWinsFlag clone = _disableWinsMerge.Left.Clone();
        clone.Merge(_disableWinsMerge.Right);
        return clone;
    }

    [Benchmark]
    public byte[] WriteBinary_GFlag() => _gFlag.Value.ToByteArray();

    [Benchmark]
    public byte[] WriteBinary_EnableWinsFlag() => _enableWins.Value.ToByteArray();

    [Benchmark]
    public byte[] WriteBinary_DisableWinsFlag() => _disableWins.Value.ToByteArray();

    [Benchmark]
    public GFlag ReadBinary_GFlag() => GFlag.ReadFrom(_gFlag.Bytes);

    [Benchmark]
    public EnableWinsFlag ReadBinary_EnableWinsFlag() => EnableWinsFlag.ReadFrom(_enableWins.Bytes);

    [Benchmark]
    public DisableWinsFlag ReadBinary_DisableWinsFlag() => DisableWinsFlag.ReadFrom(_disableWins.Bytes);

    [Benchmark]
    public string WriteJson_GFlag() => _gFlag.Value.ToJson();

    [Benchmark]
    public string WriteJson_EnableWinsFlag() => _enableWins.Value.ToJson();

    [Benchmark]
    public string WriteJson_DisableWinsFlag() => _disableWins.Value.ToJson();

    [Benchmark]
    public GFlag ReadJson_GFlag() => GFlag.FromJson(_gFlag.Json);

    [Benchmark]
    public EnableWinsFlag ReadJson_EnableWinsFlag() => EnableWinsFlag.FromJson(_enableWins.Json);

    [Benchmark]
    public DisableWinsFlag ReadJson_DisableWinsFlag() => DisableWinsFlag.FromJson(_disableWins.Json);
}
