// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics;
using System.Globalization;
using System.Net;
using BenchmarkDotNet.Attributes;
using Crdt.Transport;
using Crdt.Transport.Mqtt;
using Crdt.Transport.NanoMsg;

namespace Crdt.Benchmarks;

public enum ChangedFraction
{
    One,
    Half,
    All,
}

public enum ConvergenceTransport
{
    InMemory,
    Tcp,
    Udp,
    NanoMsg,
    Mqtt,
}

[MemoryDiagnoser]
public abstract class ConvergenceBenchmarks
{
    [Params(3, 10, 50)]
    public int ReplicaCount { get; set; }

    [Params(ChangedFraction.One, ChangedFraction.Half, ChangedFraction.All)]
    public ChangedFraction ChangedFraction { get; set; }

    [ParamsSource(nameof(Transports))]
    public ConvergenceTransport Transport { get; set; }

    public static IEnumerable<ConvergenceTransport> Transports
    {
        get
        {
            yield return ConvergenceTransport.InMemory;
            yield return ConvergenceTransport.Tcp;
            yield return ConvergenceTransport.Udp;
            yield return ConvergenceTransport.NanoMsg;

            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CRDT_MQTT_BROKER")))
            {
                yield return ConvergenceTransport.Mqtt;
            }
        }
    }

    protected Task<double> ConvergeAsync<TState>(ConvergenceCase<TState> convergenceCase)
        where TState : IConvergent<TState> =>
        ConvergenceHarness.RunAsync(Transport, ReplicaCount, ChangedFraction, convergenceCase);
}

public class CounterConvergenceBenchmarks : ConvergenceBenchmarks
{
    [Benchmark]
    public Task<double> Converge_GCounter() => ConvergeAsync(ConvergenceCases.GCounter);

    [Benchmark]
    public Task<double> Converge_PNCounter() => ConvergeAsync(ConvergenceCases.PNCounter);
}

public class SetConvergenceBenchmarks : ConvergenceBenchmarks
{
    [Benchmark]
    public Task<double> Converge_GSet() => ConvergeAsync(ConvergenceCases.GSet);

    [Benchmark]
    public Task<double> Converge_TwoPhaseSet() => ConvergeAsync(ConvergenceCases.TwoPhaseSet);

    [Benchmark]
    public Task<double> Converge_LWWElementSet() => ConvergeAsync(ConvergenceCases.LwwElementSet);

    [Benchmark]
    public Task<double> Converge_ORSet() => ConvergeAsync(ConvergenceCases.OrSet);
}

public class RegisterConvergenceBenchmarks : ConvergenceBenchmarks
{
    [Benchmark]
    public Task<double> Converge_LWWRegister() => ConvergeAsync(ConvergenceCases.LwwRegister);

    [Benchmark]
    public Task<double> Converge_MVRegister() => ConvergeAsync(ConvergenceCases.MvRegister);
}

public class MapConvergenceBenchmarks : ConvergenceBenchmarks
{
    [Benchmark]
    public Task<double> Converge_LWWMap() => ConvergeAsync(ConvergenceCases.LwwMap);

    [Benchmark]
    public Task<double> Converge_ORMap() => ConvergeAsync(ConvergenceCases.OrMap);
}

public class FlagConvergenceBenchmarks : ConvergenceBenchmarks
{
    [Benchmark]
    public Task<double> Converge_GFlag() => ConvergeAsync(ConvergenceCases.GFlag);

    [Benchmark]
    public Task<double> Converge_EnableWinsFlag() => ConvergeAsync(ConvergenceCases.EnableWinsFlag);

    [Benchmark]
    public Task<double> Converge_DisableWinsFlag() => ConvergeAsync(ConvergenceCases.DisableWinsFlag);
}

public class GraphConvergenceBenchmarks : ConvergenceBenchmarks
{
    [Benchmark]
    public Task<double> Converge_TwoPTwoPGraph() => ConvergeAsync(ConvergenceCases.TwoPTwoPGraph);

    [Benchmark]
    public Task<double> Converge_AddOnlyDag() => ConvergeAsync(ConvergenceCases.AddOnlyDag);
}

public class SequenceConvergenceBenchmarks : ConvergenceBenchmarks
{
    [Benchmark]
    public Task<double> Converge_Rga() => ConvergeAsync(ConvergenceCases.Rga);

    [Benchmark]
    public Task<double> Converge_Logoot() => ConvergeAsync(ConvergenceCases.Logoot);

    [Benchmark]
    public Task<double> Converge_LSeq() => ConvergeAsync(ConvergenceCases.LSeq);

    [Benchmark]
    public Task<double> Converge_Treedoc() => ConvergeAsync(ConvergenceCases.Treedoc);

    [Benchmark]
    public Task<double> Converge_Yata() => ConvergeAsync(ConvergenceCases.Yata);

    [Benchmark]
    public Task<double> Converge_Woot() => ConvergeAsync(ConvergenceCases.Woot);
}

public sealed class ConvergenceCase<TState>
    where TState : IConvergent<TState>
{
    public ConvergenceCase(
        string name,
        Func<TState> create,
        Func<TState, byte[]> serialize,
        Func<ReadOnlyMemory<byte>, TState> deserialize,
        Action<TState, int, ReplicaId> mutate)
    {
        Name = name;
        Create = create;
        Serialize = serialize;
        Deserialize = deserialize;
        Mutate = mutate;
    }

    public string Name { get; }

    public Func<TState> Create { get; }

    public Func<TState, byte[]> Serialize { get; }

    public Func<ReadOnlyMemory<byte>, TState> Deserialize { get; }

    public Action<TState, int, ReplicaId> Mutate { get; }
}

internal static class ConvergenceCases
{
    public static readonly ConvergenceCase<GCounter> GCounter = new(
        nameof(GCounter),
        static () => new GCounter(),
        static value => value.ToByteArray(),
        static bytes => global::Crdt.GCounter.ReadFrom(bytes.Span),
        static (value, replicaIndex, replica) => value.Increment(replica, Amount(replicaIndex)));

    public static readonly ConvergenceCase<PNCounter> PNCounter = new(
        nameof(PNCounter),
        static () => new PNCounter(),
        static value => value.ToByteArray(),
        static bytes => global::Crdt.PNCounter.ReadFrom(bytes.Span),
        static (value, replicaIndex, replica) =>
        {
            value.Increment(replica, Amount(replicaIndex) + 1UL);
            value.Decrement(replica, (ulong)((replicaIndex % 3) + 1));
        });

    public static readonly ConvergenceCase<GSet<long>> GSet = new(
        nameof(GSet),
        static () => new GSet<long>(),
        static value => value.ToByteArray(CrdtValues.Int64),
        static bytes => GSet<long>.ReadFrom(bytes.Span, CrdtValues.Int64),
        static (value, replicaIndex, _) => value.Add(LongValue(replicaIndex)));

    public static readonly ConvergenceCase<TwoPhaseSet<long>> TwoPhaseSet = new(
        nameof(TwoPhaseSet),
        static () => new TwoPhaseSet<long>(),
        static value => value.ToByteArray(CrdtValues.Int64),
        static bytes => TwoPhaseSet<long>.ReadFrom(bytes.Span, CrdtValues.Int64),
        static (value, replicaIndex, _) =>
        {
            long element = LongValue(replicaIndex);
            value.Add(element);
            if (replicaIndex % 4 == 0)
            {
                value.Remove(element);
            }
        });

    public static readonly ConvergenceCase<LWWElementSet<long>> LwwElementSet = new(
        nameof(LWWElementSet<long>),
        static () => new LWWElementSet<long>(),
        static value => value.ToByteArray(CrdtValues.Int64),
        static bytes => LWWElementSet<long>.ReadFrom(bytes.Span, CrdtValues.Int64),
        static (value, replicaIndex, replica) =>
        {
            long element = LongValue(replicaIndex);
            value.Add(element, Timestamp(replicaIndex * 2, replica));
            if (replicaIndex % 4 == 0)
            {
                value.Remove(element, Timestamp((replicaIndex * 2) + 1, replica));
            }
        });

    public static readonly ConvergenceCase<ORSet<long>> OrSet = new(
        "ORSet",
        static () => new ORSet<long>(),
        static value => value.ToByteArray(CrdtValues.Int64),
        static bytes => ORSet<long>.ReadFrom(bytes.Span, CrdtValues.Int64),
        static (value, replicaIndex, replica) =>
        {
            long element = LongValue(replicaIndex);
            value.Add(replica, element);
            if (replicaIndex % 5 == 0)
            {
                value.Remove(element);
            }
        });

    public static readonly ConvergenceCase<LWWRegister<long>> LwwRegister = new(
        nameof(LWWRegister<long>),
        static () => new LWWRegister<long>(),
        static value => value.ToByteArray(CrdtValues.Int64),
        static bytes => LWWRegister<long>.ReadFrom(bytes.Span, CrdtValues.Int64),
        static (value, replicaIndex, replica) =>
            value.Set(LongValue(replicaIndex), Timestamp(replicaIndex, replica)));

    public static readonly ConvergenceCase<MVRegister<long>> MvRegister = new(
        nameof(MVRegister<long>),
        static () => new MVRegister<long>(),
        static value => value.ToByteArray(CrdtValues.Int64),
        static bytes => MVRegister<long>.ReadFrom(bytes.Span, CrdtValues.Int64),
        static (value, replicaIndex, replica) => value.Assign(replica, LongValue(replicaIndex)));

    public static readonly ConvergenceCase<LWWMap<long, long>> LwwMap = new(
        nameof(LWWMap<long, long>),
        static () => new LWWMap<long, long>(),
        static value => value.ToByteArray(CrdtValues.Int64, CrdtValues.Int64),
        static bytes => LWWMap<long, long>.ReadFrom(bytes.Span, CrdtValues.Int64, CrdtValues.Int64),
        static (value, replicaIndex, replica) =>
            value.Set(LongValue(replicaIndex), replicaIndex, Timestamp(replicaIndex, replica)));

    public static readonly ConvergenceCase<ORMap<string, GCounter>> OrMap = new(
        nameof(ORMap<string, GCounter>),
        static () => new ORMap<string, GCounter>(Workloads.GCounterOps),
        static value => value.ToByteArray(CrdtValues.String),
        static bytes => ORMap<string, GCounter>.ReadFrom(bytes.Span, CrdtValues.String, Workloads.GCounterOps),
        static (value, replicaIndex, replica) =>
        {
            var counter = new GCounter();
            counter.Increment(replica, Amount(replicaIndex));
            value.Update(replica, Key(replicaIndex), counter);
        });

    public static readonly ConvergenceCase<GFlag> GFlag = new(
        nameof(GFlag),
        static () => new GFlag(),
        static value => value.ToByteArray(),
        static bytes => global::Crdt.GFlag.ReadFrom(bytes.Span),
        static (value, _, _) => value.Enable());

    public static readonly ConvergenceCase<EnableWinsFlag> EnableWinsFlag = new(
        nameof(EnableWinsFlag),
        static () => new EnableWinsFlag(),
        static value => value.ToByteArray(),
        static bytes => global::Crdt.EnableWinsFlag.ReadFrom(bytes.Span),
        static (value, replicaIndex, replica) =>
        {
            value.Enable(replica);
            if (replicaIndex % 3 == 0)
            {
                value.Disable(replica);
            }
        });

    public static readonly ConvergenceCase<DisableWinsFlag> DisableWinsFlag = new(
        nameof(DisableWinsFlag),
        static () => new DisableWinsFlag(),
        static value => value.ToByteArray(),
        static bytes => global::Crdt.DisableWinsFlag.ReadFrom(bytes.Span),
        static (value, replicaIndex, replica) =>
        {
            value.Enable(replica);
            if (replicaIndex % 3 == 0)
            {
                value.Disable(replica);
            }
        });

    public static readonly ConvergenceCase<TwoPTwoPGraph<long>> TwoPTwoPGraph = new(
        nameof(TwoPTwoPGraph<long>),
        static () => new TwoPTwoPGraph<long>(),
        static value => value.ToByteArray(CrdtValues.Int64),
        static bytes => TwoPTwoPGraph<long>.ReadFrom(bytes.Span, CrdtValues.Int64),
        static (value, replicaIndex, _) =>
        {
            long from = LongValue(replicaIndex) * 2L;
            long to = from + 1L;
            value.AddVertex(from);
            value.AddVertex(to);
            value.AddEdge(from, to);
        });

    public static readonly ConvergenceCase<AddOnlyDag<long>> AddOnlyDag = new(
        nameof(AddOnlyDag<long>),
        static () => new AddOnlyDag<long>(),
        static value => value.ToByteArray(CrdtValues.Int64),
        static bytes => AddOnlyDag<long>.ReadFrom(bytes.Span, CrdtValues.Int64),
        static (value, replicaIndex, _) =>
        {
            long from = LongValue(replicaIndex) * 2L;
            long to = from + 1L;
            value.AddVertex(from);
            value.AddVertex(to);
            value.AddEdge(from, to);
        });

    public static readonly ConvergenceCase<Rga<long>> Rga = new(
        nameof(Rga<long>),
        static () => new Rga<long>(),
        static value => value.ToByteArray(CrdtValues.Int64),
        static bytes => Rga<long>.ReadFrom(bytes.Span, CrdtValues.Int64),
        static (value, replicaIndex, replica) => value.Append(replica, LongValue(replicaIndex)));

    public static readonly ConvergenceCase<LogootSequence<long>> Logoot = new(
        nameof(LogootSequence<long>),
        static () => new LogootSequence<long>(),
        static value => value.ToByteArray(CrdtValues.Int64),
        static bytes => LogootSequence<long>.ReadFrom(bytes.Span, CrdtValues.Int64),
        static (value, replicaIndex, replica) => value.Append(replica, LongValue(replicaIndex)));

    public static readonly ConvergenceCase<LSeqSequence<long>> LSeq = new(
        nameof(LSeqSequence<long>),
        static () => new LSeqSequence<long>(),
        static value => value.ToByteArray(CrdtValues.Int64),
        static bytes => LSeqSequence<long>.ReadFrom(bytes.Span, CrdtValues.Int64),
        static (value, replicaIndex, replica) => value.Append(replica, LongValue(replicaIndex)));

    public static readonly ConvergenceCase<TreedocSequence<long>> Treedoc = new(
        nameof(TreedocSequence<long>),
        static () => new TreedocSequence<long>(),
        static value => value.ToByteArray(CrdtValues.Int64),
        static bytes => TreedocSequence<long>.ReadFrom(bytes.Span, CrdtValues.Int64),
        static (value, replicaIndex, replica) => value.Append(replica, LongValue(replicaIndex)));

    public static readonly ConvergenceCase<YataSequence<long>> Yata = new(
        nameof(YataSequence<long>),
        static () => new YataSequence<long>(),
        static value => value.ToByteArray(CrdtValues.Int64),
        static bytes => YataSequence<long>.ReadFrom(bytes.Span, CrdtValues.Int64),
        static (value, replicaIndex, replica) => value.Append(replica, LongValue(replicaIndex)));

    public static readonly ConvergenceCase<WootSequence<long>> Woot = new(
        nameof(WootSequence<long>),
        static () => new WootSequence<long>(),
        static value => value.ToByteArray(CrdtValues.Int64),
        static bytes => WootSequence<long>.ReadFrom(bytes.Span, CrdtValues.Int64),
        static (value, replicaIndex, replica) => value.Append(replica, LongValue(replicaIndex)));

    private static ulong Amount(int replicaIndex) => (ulong)(replicaIndex + 1);

    private static long LongValue(int replicaIndex) => 1_000_000L + replicaIndex;

    private static Timestamp Timestamp(int replicaIndex, ReplicaId replica) => new(replicaIndex + 1L, 0, replica);

    private static string Key(int replicaIndex) => "key-" + replicaIndex.ToString(CultureInfo.InvariantCulture);
}

internal static class ConvergenceHarness
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    public static async Task<double> RunAsync<TState>(
        ConvergenceTransport transport,
        int replicaCount,
        ChangedFraction changedFraction,
        ConvergenceCase<TState> convergenceCase)
        where TState : IConvergent<TState>
    {
        await using ConvergenceCluster<TState> cluster =
            await ConvergenceCluster<TState>.StartAsync(transport, replicaCount, convergenceCase);

        Mutate(cluster, changedFraction, convergenceCase);

        Stopwatch stopwatch = Stopwatch.StartNew();
        await cluster.BroadcastUntilEqualAsync(Timeout);
        stopwatch.Stop();
        return stopwatch.Elapsed.TotalMilliseconds;
    }

    private static void Mutate<TState>(
        ConvergenceCluster<TState> cluster,
        ChangedFraction changedFraction,
        ConvergenceCase<TState> convergenceCase)
        where TState : IConvergent<TState>
    {
        int changed = ChangedReplicaCount(cluster.Engines.Count, changedFraction);
        for (int i = 0; i < changed; i++)
        {
            ReplicaId replica = ReplicaId.FromUInt64((ulong)i + 1UL);
            convergenceCase.Mutate(cluster.Engines[i].Replica.Value, i, replica);
        }
    }

    private static int ChangedReplicaCount(int replicaCount, ChangedFraction changedFraction) =>
        changedFraction switch
        {
            ChangedFraction.One => 1,
            ChangedFraction.Half => (replicaCount + 1) / 2,
            ChangedFraction.All => replicaCount,
            _ => throw new ArgumentOutOfRangeException(nameof(changedFraction), changedFraction, null),
        };
}

internal sealed class ConvergenceCluster<TState> : IAsyncDisposable
    where TState : IConvergent<TState>
{
    private readonly ConvergenceTransport _transport;
    private readonly IAsyncDisposable? _network;

    private ConvergenceCluster(
        ConvergenceTransport transport,
        List<ReplicationEngine<TState>> engines,
        IAsyncDisposable? network)
    {
        _transport = transport;
        Engines = engines;
        _network = network;
    }

    public List<ReplicationEngine<TState>> Engines { get; }

    public static async Task<ConvergenceCluster<TState>> StartAsync(
        ConvergenceTransport transport,
        int replicaCount,
        ConvergenceCase<TState> convergenceCase)
    {
        return transport switch
        {
            ConvergenceTransport.InMemory => await StartInMemoryAsync(replicaCount, convergenceCase),
            ConvergenceTransport.Tcp => await StartTcpAsync(replicaCount, convergenceCase),
            ConvergenceTransport.Udp => await StartUdpAsync(replicaCount, convergenceCase),
            ConvergenceTransport.NanoMsg => await StartNanoMsgAsync(replicaCount, convergenceCase),
            ConvergenceTransport.Mqtt => await StartMqttAsync(replicaCount, convergenceCase),
            _ => throw new ArgumentOutOfRangeException(nameof(transport), transport, null),
        };
    }

    public async Task BroadcastUntilEqualAsync(TimeSpan timeout)
    {
        using var cancellation = new CancellationTokenSource(timeout);
        while (!cancellation.IsCancellationRequested)
        {
            foreach (ReplicationEngine<TState> engine in Engines)
            {
                await engine.BroadcastStateAsync(cancellation.Token);
            }

            await WaitForDeliveryAsync(cancellation.Token);
            if (AllSnapshotsEqual())
            {
                return;
            }
        }

        throw new TimeoutException($"The {_transport} {typeof(TState).Name} replicas did not converge.");
    }

    public async ValueTask DisposeAsync()
    {
        foreach (ReplicationEngine<TState> engine in Engines)
        {
            await engine.DisposeAsync();
        }

        if (_network is not null)
        {
            await _network.DisposeAsync();
        }
    }

    private static async Task<ConvergenceCluster<TState>> StartInMemoryAsync(
        int replicaCount,
        ConvergenceCase<TState> convergenceCase)
    {
        var network = new InMemoryNetwork();
        List<ReplicationEngine<TState>> engines = [];
        try
        {
            for (int i = 0; i < replicaCount; i++)
            {
                ReplicationEngine<TState> engine = CreateEngine(convergenceCase, network.CreateTransport());
                await engine.StartAsync();
                engines.Add(engine);
            }

            return new ConvergenceCluster<TState>(ConvergenceTransport.InMemory, engines, network);
        }
        catch
        {
            await DisposeAllAsync(engines);
            await network.DisposeAsync();
            throw;
        }
    }

    private static async Task<ConvergenceCluster<TState>> StartTcpAsync(
        int replicaCount,
        ConvergenceCase<TState> convergenceCase)
    {
        List<TcpGossipTransport> transports = [];
        List<ReplicationEngine<TState>> engines = [];
        try
        {
            for (int i = 0; i < replicaCount; i++)
            {
                var transport = new TcpGossipTransport(IPAddress.Loopback, 0, TimeSpan.FromMilliseconds(25));
                await transport.StartAsync();
                transports.Add(transport);
            }

            foreach (TcpGossipTransport transport in transports)
            {
                transport.AddPeers(transports.Select(static candidate => candidate.LocalEndPoint));
                engines.Add(CreateEngine(convergenceCase, transport));
            }

            return new ConvergenceCluster<TState>(ConvergenceTransport.Tcp, engines, null);
        }
        catch
        {
            await DisposeAllAsync(engines);
            await DisposeAllAsync(transports);
            throw;
        }
    }

    private static async Task<ConvergenceCluster<TState>> StartUdpAsync(
        int replicaCount,
        ConvergenceCase<TState> convergenceCase)
    {
        List<UdpGossipTransport> transports = [];
        List<ReplicationEngine<TState>> engines = [];
        try
        {
            for (int i = 0; i < replicaCount; i++)
            {
                var transport = new UdpGossipTransport(IPAddress.Loopback, 0, TimeSpan.FromMilliseconds(25));
                await transport.StartAsync();
                transports.Add(transport);
            }

            foreach (UdpGossipTransport transport in transports)
            {
                transport.AddPeers(transports.Select(static candidate => candidate.LocalEndPoint));
                engines.Add(CreateEngine(convergenceCase, transport));
            }

            return new ConvergenceCluster<TState>(ConvergenceTransport.Udp, engines, null);
        }
        catch
        {
            await DisposeAllAsync(engines);
            await DisposeAllAsync(transports);
            throw;
        }
    }

    private static async Task<ConvergenceCluster<TState>> StartNanoMsgAsync(
        int replicaCount,
        ConvergenceCase<TState> convergenceCase)
    {
        string runRoot = "crdt-conv-" + Guid.NewGuid().ToString("N");
        string[] addresses = Enumerable.Range(0, replicaCount)
            .Select(i => "inproc://" + runRoot + "-node-" + i.ToString(CultureInfo.InvariantCulture))
            .ToArray();
        List<NanoMsgBusTransport> transports = [];
        List<ReplicationEngine<TState>> engines = [];
        try
        {
            for (int i = 0; i < replicaCount; i++)
            {
                var transport = new NanoMsgBusTransport(new NanoMsgBusTransportOptions
                {
                    BindAddress = addresses[i],
                });
                await transport.StartAsync();
                transports.Add(transport);
            }

            for (int i = 0; i < transports.Count; i++)
            {
                for (int j = 0; j < i; j++)
                {
                    transports[i].AddPeer(addresses[j]);
                }

                engines.Add(CreateEngine(convergenceCase, transports[i]));
            }

            return new ConvergenceCluster<TState>(ConvergenceTransport.NanoMsg, engines, null);
        }
        catch
        {
            await DisposeAllAsync(engines);
            await DisposeAllAsync(transports);
            throw;
        }
    }

    private static async Task<ConvergenceCluster<TState>> StartMqttAsync(
        int replicaCount,
        ConvergenceCase<TState> convergenceCase)
    {
        string? broker = Environment.GetEnvironmentVariable("CRDT_MQTT_BROKER");
        if (string.IsNullOrWhiteSpace(broker))
        {
            throw new InvalidOperationException("Set CRDT_MQTT_BROKER to include MQTT convergence benchmarks.");
        }

        string brokerUri = broker.Contains("://", StringComparison.Ordinal) ? broker : "mqtt://" + broker;
        string topicRoot = "crdt/bench/convergence/" + Guid.NewGuid().ToString("N");
        List<MqttGossipTransport> transports = [];
        List<ReplicationEngine<TState>> engines = [];
        try
        {
            for (int i = 0; i < replicaCount; i++)
            {
                var transport = new MqttGossipTransport(new MqttGossipTransportOptions
                {
                    BrokerUri = brokerUri,
                    TopicRoot = topicRoot,
                    ClientId = "node-" + i.ToString(CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N"),
                });
                await transport.StartAsync();
                transports.Add(transport);
                engines.Add(CreateEngine(convergenceCase, transport));
            }

            return new ConvergenceCluster<TState>(ConvergenceTransport.Mqtt, engines, null);
        }
        catch
        {
            await DisposeAllAsync(engines);
            await DisposeAllAsync(transports);
            throw;
        }
    }

    private static ReplicationEngine<TState> CreateEngine(
        ConvergenceCase<TState> convergenceCase,
        ITransport transport)
    {
        var replica = new CrdtReplica<TState>(
            convergenceCase.Create(),
            convergenceCase.Serialize,
            convergenceCase.Deserialize);
        return new ReplicationEngine<TState>(replica, transport);
    }

    private static async ValueTask DisposeAllAsync<TDisposable>(IEnumerable<TDisposable> disposables)
        where TDisposable : IAsyncDisposable
    {
        foreach (TDisposable disposable in disposables)
        {
            await disposable.DisposeAsync();
        }
    }

    private bool AllSnapshotsEqual()
    {
        byte[] expected = Engines[0].Replica.SnapshotState();
        for (int i = 1; i < Engines.Count; i++)
        {
            if (!Engines[i].Replica.SnapshotState().AsSpan().SequenceEqual(expected))
            {
                return false;
            }
        }

        return true;
    }

    private async Task WaitForDeliveryAsync(CancellationToken cancellationToken)
    {
        if (_network is InMemoryNetwork network)
        {
            await network.DrainAsync(cancellationToken);
            return;
        }

        TimeSpan delay = _transport is ConvergenceTransport.NanoMsg or ConvergenceTransport.Mqtt
            ? TimeSpan.FromMilliseconds(25)
            : TimeSpan.FromMilliseconds(5);
        await Task.Delay(delay, cancellationToken);
    }
}
