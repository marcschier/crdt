// Copyright (c) marcschier. Licensed under the MIT License.

using BenchmarkDotNet.Attributes;

namespace Crdt.Benchmarks;

[MemoryDiagnoser]
public class SequenceBenchmarks
{
    private (Rga<long> Left, Rga<long> Right) _rgaMerge;
    private (LogootSequence<long> Left, LogootSequence<long> Right) _logootMerge;
    private (LSeqSequence<long> Left, LSeqSequence<long> Right) _lSeqMerge;
    private (TreedocSequence<long> Left, TreedocSequence<long> Right) _treedocMerge;
    private (YataSequence<long> Left, YataSequence<long> Right) _yataMerge;
    private (WootSequence<long> Left, WootSequence<long> Right) _wootMerge;
    private Workloads.Encoded<Rga<long>> _rga;
    private Workloads.Encoded<LogootSequence<long>> _logoot;
    private Workloads.Encoded<LSeqSequence<long>> _lSeq;
    private Workloads.Encoded<TreedocSequence<long>> _treedoc;
    private Workloads.Encoded<YataSequence<long>> _yata;
    private Workloads.Encoded<WootSequence<long>> _woot;

    [Params(100, 1000)]
    public int Size { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _rgaMerge = Workloads.RgaPair(Size);
        _logootMerge = Workloads.LogootPair(Size);
        _lSeqMerge = Workloads.LSeqPair(Size);
        _treedocMerge = Workloads.TreedocPair(Size);
        _yataMerge = Workloads.YataPair(Size);
        _wootMerge = Workloads.WootPair(Size);
        _rga = Workloads.Encode(Workloads.Rga(Size, 53000, 0), writeBinary, writeJson);
        _logoot = Workloads.Encode(Workloads.Logoot(Size, 54000, 0), writeBinary, writeJson);
        _lSeq = Workloads.Encode(Workloads.LSeq(Size, 55000, 0), writeBinary, writeJson);
        _treedoc = Workloads.Encode(Workloads.Treedoc(Size, 56000, 0), writeBinary, writeJson);
        _yata = Workloads.Encode(Workloads.Yata(Size, 57000, 0), writeBinary, writeJson);
        _woot = Workloads.Encode(Workloads.Woot(Size, 58000, 0), writeBinary, writeJson);

        static byte[] writeBinary<T>(T sequence) where T : notnull => sequence switch
        {
            Rga<long> value => value.ToByteArray(CrdtValues.Int64),
            LogootSequence<long> value => value.ToByteArray(CrdtValues.Int64),
            LSeqSequence<long> value => value.ToByteArray(CrdtValues.Int64),
            TreedocSequence<long> value => value.ToByteArray(CrdtValues.Int64),
            YataSequence<long> value => value.ToByteArray(CrdtValues.Int64),
            WootSequence<long> value => value.ToByteArray(CrdtValues.Int64),
            _ => [],
        };

        static string writeJson<T>(T sequence) where T : notnull => sequence switch
        {
            Rga<long> value => value.ToJson(CrdtValues.Int64),
            LogootSequence<long> value => value.ToJson(CrdtValues.Int64),
            LSeqSequence<long> value => value.ToJson(CrdtValues.Int64),
            TreedocSequence<long> value => value.ToJson(CrdtValues.Int64),
            YataSequence<long> value => value.ToJson(CrdtValues.Int64),
            WootSequence<long> value => value.ToJson(CrdtValues.Int64),
            _ => string.Empty,
        };
    }

    [Benchmark]
    public Rga<long> Mutate_Rga() => Workloads.Rga(Size, 59000, 0);

    [Benchmark]
    public LogootSequence<long> Mutate_Logoot() => Workloads.Logoot(Size, 60000, 0);

    [Benchmark]
    public LSeqSequence<long> Mutate_LSeq() => Workloads.LSeq(Size, 61000, 0);

    [Benchmark]
    public TreedocSequence<long> Mutate_Treedoc() => Workloads.Treedoc(Size, 62000, 0);

    [Benchmark]
    public YataSequence<long> Mutate_Yata() => Workloads.Yata(Size, 63000, 0);

    [Benchmark]
    public WootSequence<long> Mutate_Woot() => Workloads.Woot(Size, 64000, 0);

    [Benchmark]
    public Rga<long> Merge_Rga()
    {
        Rga<long> clone = _rgaMerge.Left.Clone();
        clone.Merge(_rgaMerge.Right);
        return clone;
    }

    [Benchmark]
    public LogootSequence<long> Merge_Logoot()
    {
        LogootSequence<long> clone = _logootMerge.Left.Clone();
        clone.Merge(_logootMerge.Right);
        return clone;
    }

    [Benchmark]
    public LSeqSequence<long> Merge_LSeq()
    {
        LSeqSequence<long> clone = _lSeqMerge.Left.Clone();
        clone.Merge(_lSeqMerge.Right);
        return clone;
    }

    [Benchmark]
    public TreedocSequence<long> Merge_Treedoc()
    {
        TreedocSequence<long> clone = _treedocMerge.Left.Clone();
        clone.Merge(_treedocMerge.Right);
        return clone;
    }

    [Benchmark]
    public YataSequence<long> Merge_Yata()
    {
        YataSequence<long> clone = _yataMerge.Left.Clone();
        clone.Merge(_yataMerge.Right);
        return clone;
    }

    [Benchmark]
    public WootSequence<long> Merge_Woot()
    {
        WootSequence<long> clone = _wootMerge.Left.Clone();
        clone.Merge(_wootMerge.Right);
        return clone;
    }

    [Benchmark]
    public byte[] WriteBinary_Rga() => _rga.Value.ToByteArray(CrdtValues.Int64);

    [Benchmark]
    public byte[] WriteBinary_Logoot() => _logoot.Value.ToByteArray(CrdtValues.Int64);

    [Benchmark]
    public byte[] WriteBinary_LSeq() => _lSeq.Value.ToByteArray(CrdtValues.Int64);

    [Benchmark]
    public byte[] WriteBinary_Treedoc() => _treedoc.Value.ToByteArray(CrdtValues.Int64);

    [Benchmark]
    public byte[] WriteBinary_Yata() => _yata.Value.ToByteArray(CrdtValues.Int64);

    [Benchmark]
    public byte[] WriteBinary_Woot() => _woot.Value.ToByteArray(CrdtValues.Int64);

    [Benchmark]
    public Rga<long> ReadBinary_Rga() => Rga<long>.ReadFrom(_rga.Bytes, CrdtValues.Int64);

    [Benchmark]
    public LogootSequence<long> ReadBinary_Logoot() =>
        LogootSequence<long>.ReadFrom(_logoot.Bytes, CrdtValues.Int64);

    [Benchmark]
    public LSeqSequence<long> ReadBinary_LSeq() => LSeqSequence<long>.ReadFrom(_lSeq.Bytes, CrdtValues.Int64);

    [Benchmark]
    public TreedocSequence<long> ReadBinary_Treedoc() =>
        TreedocSequence<long>.ReadFrom(_treedoc.Bytes, CrdtValues.Int64);

    [Benchmark]
    public YataSequence<long> ReadBinary_Yata() => YataSequence<long>.ReadFrom(_yata.Bytes, CrdtValues.Int64);

    [Benchmark]
    public WootSequence<long> ReadBinary_Woot() => WootSequence<long>.ReadFrom(_woot.Bytes, CrdtValues.Int64);

    [Benchmark]
    public string WriteJson_Rga() => _rga.Value.ToJson(CrdtValues.Int64);

    [Benchmark]
    public string WriteJson_Logoot() => _logoot.Value.ToJson(CrdtValues.Int64);

    [Benchmark]
    public string WriteJson_LSeq() => _lSeq.Value.ToJson(CrdtValues.Int64);

    [Benchmark]
    public string WriteJson_Treedoc() => _treedoc.Value.ToJson(CrdtValues.Int64);

    [Benchmark]
    public string WriteJson_Yata() => _yata.Value.ToJson(CrdtValues.Int64);

    [Benchmark]
    public string WriteJson_Woot() => _woot.Value.ToJson(CrdtValues.Int64);

    [Benchmark]
    public Rga<long> ReadJson_Rga() => Rga<long>.FromJson(_rga.Json, CrdtValues.Int64);

    [Benchmark]
    public LogootSequence<long> ReadJson_Logoot() =>
        LogootSequence<long>.FromJson(_logoot.Json, CrdtValues.Int64);

    [Benchmark]
    public LSeqSequence<long> ReadJson_LSeq() => LSeqSequence<long>.FromJson(_lSeq.Json, CrdtValues.Int64);

    [Benchmark]
    public TreedocSequence<long> ReadJson_Treedoc() =>
        TreedocSequence<long>.FromJson(_treedoc.Json, CrdtValues.Int64);

    [Benchmark]
    public YataSequence<long> ReadJson_Yata() => YataSequence<long>.FromJson(_yata.Json, CrdtValues.Int64);

    [Benchmark]
    public WootSequence<long> ReadJson_Woot() => WootSequence<long>.FromJson(_woot.Json, CrdtValues.Int64);
}
