// Copyright (c) marcschier. Licensed under the MIT License.

namespace Crdt.Transport.NanoMsg.Tests;

/// <summary>
/// End-to-end convergence tests over a three-node BUS mesh using the in-process <c>tcp://</c> loopback
/// transport — no broker or external service is required.
/// </summary>
public sealed class NanoMsgBusConvergenceTests
{
    [Test]
    public async Task Bus_State_Gossip_Converges_PNCounters()
    {
        Cluster cluster = await Cluster.StartAsync(ReplicationMode.State);
        try
        {
            for (int i = 0; i < cluster.Engines.Count; i++)
            {
                ReplicaId replicaId = ReplicaId.FromUInt64((ulong)i + 2_100UL);
                cluster.Engines[i].Replica.Value.Increment(replicaId, (ulong)(i + 1));
                cluster.Engines[i].Replica.Value.Decrement(replicaId, 1);
            }

            await WaitUntilConvergedAsync(cluster.Engines, i => cluster.Engines[i].BroadcastStateAsync());
        }
        finally
        {
            await cluster.DisposeAsync();
        }
    }

    [Test]
    public async Task Bus_Delta_Gossip_Converges_PNCounters()
    {
        Cluster cluster = await Cluster.StartAsync(ReplicationMode.Delta);
        try
        {
            for (int i = 0; i < cluster.Engines.Count; i++)
            {
                ReplicaId replicaId = ReplicaId.FromUInt64((ulong)i + 2_200UL);
                cluster.Engines[i].Replica.Value.Increment(replicaId, (ulong)(i + 2));
                cluster.Engines[i].Replica.Value.Decrement(replicaId, 1);
            }

            await WaitUntilConvergedAsync(cluster.Engines, i => cluster.Engines[i].BroadcastStateAsync());
        }
        finally
        {
            await cluster.DisposeAsync();
        }
    }

    [Test]
    public async Task Bus_Operation_Broadcast_Converges_PNCounters()
    {
        Cluster cluster = await Cluster.StartAsync(ReplicationMode.Operation);
        try
        {
            byte[][] operations = new byte[cluster.Engines.Count][];
            for (int i = 0; i < cluster.Engines.Count; i++)
            {
                ReplicaId replicaId = ReplicaId.FromUInt64((ulong)i + 2_300UL);
                PNCounterOperation op = cluster.Engines[i].Replica.Value.Increment(replicaId, (ulong)(i + 3));
                operations[i] = op.ToByteArray();
            }

            await WaitUntilConvergedAsync(
                cluster.Engines, i => cluster.Engines[i].BroadcastOperationAsync(operations[i]));
        }
        finally
        {
            await cluster.DisposeAsync();
        }
    }

    // BUS connects (and reconnects) in the background, so frames are re-broadcast on each tick until the
    // mesh is connected and every replica holds the same value. All three modes are idempotent.
    private static async Task WaitUntilConvergedAsync(
        List<ReplicationEngine<PNCounter>> engines,
        Func<int, ValueTask> broadcast)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (!timeout.IsCancellationRequested)
        {
            for (int i = 0; i < engines.Count; i++)
            {
                await broadcast(i);
            }

            await Task.Delay(TimeSpan.FromMilliseconds(150), CancellationToken.None);

            long value = engines[0].Replica.Value.Value;
            if (value != 0 && engines.All(engine => engine.Replica.Value.Value == value))
            {
                return;
            }
        }

        long[] values = engines.Select(engine => engine.Replica.Value.Value).ToArray();
        await Assert.That(values.All(v => v == values[0] && v != 0)).IsTrue();
    }

    private sealed class Cluster : IAsyncDisposable
    {
        private Cluster(List<ReplicationEngine<PNCounter>> engines)
        {
            Engines = engines;
        }

        public List<ReplicationEngine<PNCounter>> Engines { get; }

        public static async Task<Cluster> StartAsync(ReplicationMode mode)
        {
            List<NanoMsgBusTransport> transports = [];
            List<ReplicationEngine<PNCounter>> engines = [];
            try
            {
                for (int i = 0; i < 3; i++)
                {
                    var transport = new NanoMsgBusTransport(new NanoMsgBusTransportOptions
                    {
                        BindAddress = "tcp://127.0.0.1:0",
                    });
                    await transport.StartAsync();
                    transports.Add(transport);
                }

                // One connection per pair: each node dials every lower-indexed (already-bound) node.
                for (int i = 0; i < transports.Count; i++)
                {
                    for (int j = 0; j < i; j++)
                    {
                        transports[i].AddPeer($"tcp://127.0.0.1:{transports[j].BoundPort}");
                    }
                }

                foreach (NanoMsgBusTransport transport in transports)
                {
                    var counter = new PNCounter();
                    CrdtReplica<PNCounter> replica = mode == ReplicationMode.Delta
                        ? new CrdtReplica<PNCounter>(
                            counter,
                            static c => c.ToByteArray(),
                            ReadPNCounter,
                            static (PNCounter c, out PNCounter delta) => c.TryExtractDelta(out delta!),
                            static c => c.ToByteArray(),
                            ReadPNCounter,
                            static (c, delta) => c.MergeDelta(delta))
                        : new CrdtReplica<PNCounter>(counter, static c => c.ToByteArray(), ReadPNCounter);
                    var engine = new ReplicationEngine<PNCounter>(
                        replica,
                        transport,
                        mode,
                        payload => replica.Value.Apply(PNCounterOperation.ReadFrom(payload.Span)));
                    engines.Add(engine);
                }

                return new Cluster(engines);
            }
            catch
            {
                foreach (ReplicationEngine<PNCounter> engine in engines)
                {
                    await engine.DisposeAsync();
                }

                foreach (NanoMsgBusTransport transport in transports)
                {
                    await transport.DisposeAsync();
                }

                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            foreach (ReplicationEngine<PNCounter> engine in Engines)
            {
                await engine.DisposeAsync();
            }
        }

        private static PNCounter ReadPNCounter(ReadOnlyMemory<byte> bytes) => PNCounter.ReadFrom(bytes.Span);
    }
}
