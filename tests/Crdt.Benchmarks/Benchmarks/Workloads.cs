// Copyright (c) marcschier. Licensed under the MIT License.

namespace Crdt.Benchmarks;

internal static class Workloads
{
    public static readonly ReplicaId ReplicaA = ReplicaId.FromUInt64(1);
    public static readonly ReplicaId ReplicaB = ReplicaId.FromUInt64(2);
    public static readonly GCounterValueOps GCounterOps = new();

    public readonly record struct Encoded<T>(T Value, byte[] Bytes, string Json);

    public static Encoded<T> Encode<T>(T value, Func<T, byte[]> writeBinary, Func<T, string> writeJson) =>
        new(value, writeBinary(value), writeJson(value));

    public static GCounter GCounter(int size, ulong replicaSeed)
    {
        var counter = new GCounter();
        for (int i = 0; i < size; i++)
        {
            counter.Increment(Replica(replicaSeed + (ulong)i), (ulong)(i + 1));
        }

        return counter;
    }

    public static (GCounter Left, GCounter Right) GCounterPair(int size) =>
        (GCounter(size, 1000), GCounter(size, 2000));

    public static PNCounter PNCounter(int size, ulong replicaSeed)
    {
        var counter = new PNCounter();
        for (int i = 0; i < size; i++)
        {
            ReplicaId replica = Replica(replicaSeed + (ulong)i);
            counter.Increment(replica, (ulong)(i + 1));
            counter.Decrement(replica, (ulong)((i % 7) + 1));
        }

        return counter;
    }

    public static (PNCounter Left, PNCounter Right) PNCounterPair(int size) =>
        (PNCounter(size, 3000), PNCounter(size, 4000));

    public static LWWRegister<long> LwwRegister(int size, ulong replicaValue)
    {
        ReplicaId replica = Replica(replicaValue);
        var register = new LWWRegister<long>();
        for (int i = 0; i < size; i++)
        {
            register.Set(i, Timestamp(i, replica));
        }

        return register;
    }

    public static (LWWRegister<long> Left, LWWRegister<long> Right) LwwRegisterPair(int size) =>
        (LwwRegister(size, 5000), LwwRegister(size, 6000));

    public static MVRegister<long> MvRegister(int size, ulong replicaSeed, long valueOffset)
    {
        var register = new MVRegister<long>();
        for (int i = 0; i < size; i++)
        {
            var assignment = new MVRegister<long>();
            assignment.Assign(Replica(replicaSeed + (ulong)i), valueOffset + i);
            register.Merge(assignment);
        }

        return register;
    }

    public static (MVRegister<long> Left, MVRegister<long> Right) MvRegisterPair(int size) =>
        (MvRegister(size, 7000, 0), MvRegister(size, 8000, size));

    public static GSet<long> GSet(int size, long offset)
    {
        var set = new GSet<long>();
        for (int i = 0; i < size; i++)
        {
            set.Add(offset + i);
        }

        return set;
    }

    public static (GSet<long> Left, GSet<long> Right) GSetPair(int size) =>
        (GSet(size, 0), GSet(size, size));

    public static TwoPhaseSet<long> TwoPhaseSet(int size, long offset)
    {
        var set = new TwoPhaseSet<long>();
        for (int i = 0; i < size; i++)
        {
            long value = offset + i;
            set.Add(value);
            if (i % 4 == 0)
            {
                set.Remove(value);
            }
        }

        return set;
    }

    public static (TwoPhaseSet<long> Left, TwoPhaseSet<long> Right) TwoPhaseSetPair(int size) =>
        (TwoPhaseSet(size, 0), TwoPhaseSet(size, size));

    public static LWWElementSet<long> LwwElementSet(int size, ulong replicaValue, long offset)
    {
        ReplicaId replica = Replica(replicaValue);
        var set = new LWWElementSet<long>();
        for (int i = 0; i < size; i++)
        {
            long value = offset + i;
            set.Add(value, Timestamp(i * 2, replica));
            if (i % 4 == 0)
            {
                set.Remove(value, Timestamp((i * 2) + 1, replica));
            }
        }

        return set;
    }

    public static (LWWElementSet<long> Left, LWWElementSet<long> Right) LwwElementSetPair(int size) =>
        (LwwElementSet(size, 9000, 0), LwwElementSet(size, 10000, size));

    public static ORSet<long> OrSet(int size, ulong replicaValue, long offset)
    {
        ReplicaId replica = Replica(replicaValue);
        var set = new ORSet<long>();
        for (int i = 0; i < size; i++)
        {
            long value = offset + i;
            set.Add(replica, value);
            if (i % 5 == 0)
            {
                set.Remove(value);
            }
        }

        return set;
    }

    public static (ORSet<long> Left, ORSet<long> Right) OrSetPair(int size) =>
        (OrSet(size, 11000, 0), OrSet(size, 12000, size));

    public static LWWMap<long, long> LwwMap(int size, ulong replicaValue, long offset)
    {
        ReplicaId replica = Replica(replicaValue);
        var map = new LWWMap<long, long>();
        for (int i = 0; i < size; i++)
        {
            map.Set(offset + i, i, Timestamp(i, replica));
        }

        return map;
    }

    public static (LWWMap<long, long> Left, LWWMap<long, long> Right) LwwMapPair(int size) =>
        (LwwMap(size, 13000, 0), LwwMap(size, 14000, size));

    public static ORMap<string, GCounter> OrMap(int size, ulong replicaValue, int keyOffset)
    {
        ReplicaId replica = Replica(replicaValue);
        var map = new ORMap<string, GCounter>(GCounterOps);
        for (int i = 0; i < size; i++)
        {
            map.Update(replica, Key(keyOffset + i), CounterValue(replica, (ulong)(i + 1)));
        }

        return map;
    }

    public static (ORMap<string, GCounter> Left, ORMap<string, GCounter> Right) OrMapPair(int size) =>
        (OrMap(size, 15000, 0), OrMap(size, 16000, size));

    public static GFlag GFlag(int size)
    {
        var flag = new GFlag();
        for (int i = 0; i < size; i++)
        {
            flag.Enable();
        }

        return flag;
    }

    public static (GFlag Left, GFlag Right) GFlagPair(int size) =>
        (GFlag(size), GFlag(size));

    public static EnableWinsFlag EnableWinsFlag(int size, ulong replicaSeed)
    {
        var flag = new EnableWinsFlag();
        for (int i = 0; i < size; i++)
        {
            ReplicaId replica = Replica(replicaSeed + (ulong)i);
            flag.Enable(replica);
            if (i % 3 == 0)
            {
                flag.Disable(replica);
            }
        }

        return flag;
    }

    public static (EnableWinsFlag Left, EnableWinsFlag Right) EnableWinsFlagPair(int size) =>
        (EnableWinsFlag(size, 17000), EnableWinsFlag(size, 18000));

    public static DisableWinsFlag DisableWinsFlag(int size, ulong replicaSeed)
    {
        var flag = new DisableWinsFlag();
        for (int i = 0; i < size; i++)
        {
            ReplicaId replica = Replica(replicaSeed + (ulong)i);
            flag.Enable(replica);
            if (i % 3 == 0)
            {
                flag.Disable(replica);
            }
        }

        return flag;
    }

    public static (DisableWinsFlag Left, DisableWinsFlag Right) DisableWinsFlagPair(int size) =>
        (DisableWinsFlag(size, 19000), DisableWinsFlag(size, 20000));

    public static TwoPTwoPGraph<long> TwoPTwoPGraph(int size, long offset)
    {
        var graph = new TwoPTwoPGraph<long>();
        for (int i = 0; i < size; i++)
        {
            long value = offset + i;
            graph.AddVertex(value);
            if (i > 0)
            {
                graph.AddEdge(value - 1, value);
            }
        }

        return graph;
    }

    public static (TwoPTwoPGraph<long> Left, TwoPTwoPGraph<long> Right) TwoPTwoPGraphPair(int size) =>
        (TwoPTwoPGraph(size, 0), TwoPTwoPGraph(size, size));

    public static AddOnlyDag<long> AddOnlyDag(int size, long offset)
    {
        var graph = new AddOnlyDag<long>();
        for (int i = 0; i < size; i++)
        {
            long value = offset + i;
            graph.AddVertex(value);
            if (i > 0)
            {
                graph.AddEdge(value - 1, value);
            }
        }

        return graph;
    }

    public static (AddOnlyDag<long> Left, AddOnlyDag<long> Right) AddOnlyDagPair(int size) =>
        (AddOnlyDag(size, 0), AddOnlyDag(size, size));

    public static Rga<long> Rga(int size, ulong replicaValue, long offset)
    {
        ReplicaId replica = Replica(replicaValue);
        var sequence = new Rga<long>();
        for (int i = 0; i < size; i++)
        {
            sequence.Append(replica, offset + i);
        }

        return sequence;
    }

    public static (Rga<long> Left, Rga<long> Right) RgaPair(int size) =>
        (Rga(size, 21000, 0), Rga(size, 22000, size));

    public static LogootSequence<long> Logoot(int size, ulong replicaValue, long offset)
    {
        ReplicaId replica = Replica(replicaValue);
        var sequence = new LogootSequence<long>();
        for (int i = 0; i < size; i++)
        {
            sequence.Append(replica, offset + i);
        }

        return sequence;
    }

    public static (LogootSequence<long> Left, LogootSequence<long> Right) LogootPair(int size) =>
        (Logoot(size, 23000, 0), Logoot(size, 24000, size));

    public static LSeqSequence<long> LSeq(int size, ulong replicaValue, long offset)
    {
        ReplicaId replica = Replica(replicaValue);
        var sequence = new LSeqSequence<long>();
        for (int i = 0; i < size; i++)
        {
            sequence.Append(replica, offset + i);
        }

        return sequence;
    }

    public static (LSeqSequence<long> Left, LSeqSequence<long> Right) LSeqPair(int size) =>
        (LSeq(size, 25000, 0), LSeq(size, 26000, size));

    public static TreedocSequence<long> Treedoc(int size, ulong replicaValue, long offset)
    {
        ReplicaId replica = Replica(replicaValue);
        var sequence = new TreedocSequence<long>();
        for (int i = 0; i < size; i++)
        {
            sequence.Append(replica, offset + i);
        }

        return sequence;
    }

    public static (TreedocSequence<long> Left, TreedocSequence<long> Right) TreedocPair(int size) =>
        (Treedoc(size, 27000, 0), Treedoc(size, 28000, size));

    public static YataSequence<long> Yata(int size, ulong replicaValue, long offset)
    {
        ReplicaId replica = Replica(replicaValue);
        var sequence = new YataSequence<long>();
        for (int i = 0; i < size; i++)
        {
            sequence.Append(replica, offset + i);
        }

        return sequence;
    }

    public static (YataSequence<long> Left, YataSequence<long> Right) YataPair(int size) =>
        (Yata(size, 29000, 0), Yata(size, 30000, size));

    public static WootSequence<long> Woot(int size, ulong replicaValue, long offset)
    {
        ReplicaId replica = Replica(replicaValue);
        var sequence = new WootSequence<long>();
        for (int i = 0; i < size; i++)
        {
            sequence.Append(replica, offset + i);
        }

        return sequence;
    }

    public static (WootSequence<long> Left, WootSequence<long> Right) WootPair(int size) =>
        (Woot(size, 31000, 0), Woot(size, 32000, size));

    private static GCounter CounterValue(ReplicaId replica, ulong amount)
    {
        var counter = new GCounter();
        counter.Increment(replica, amount);
        return counter;
    }

    private static ReplicaId Replica(ulong value) => ReplicaId.FromUInt64(value);

    private static Timestamp Timestamp(int value, ReplicaId replica) => new(value + 1L, 0, replica);

    private static string Key(int value) => "key-" + value;
}
