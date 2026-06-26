// Copyright (c) marcschier. Licensed under the MIT License.

using System.Net;

namespace Crdt.Transport.Tests;

public sealed class TcpGossipTransportTests
{
    [Test]
    public async Task Tcp_State_Gossip_Converges_PNCounters()
    {
        Cluster cluster = await Cluster.StartAsync(ReplicationMode.State);
        try
        {
            for (int i = 0; i < cluster.Engines.Count; i++)
            {
                ReplicaId replicaId = ReplicaId.FromUInt64((ulong)i + 100UL);
                cluster.Engines[i].Replica.Value.Increment(replicaId, (ulong)(i + 1));
                cluster.Engines[i].Replica.Value.Decrement(replicaId, 1);
            }

            foreach (ReplicationEngine<PNCounter> engine in cluster.Engines)
            {
                await engine.BroadcastStateAsync();
            }

            await WaitUntilConvergedAsync(cluster.Engines);
        }
        finally
        {
            await cluster.DisposeAsync();
        }
    }

    [Test]
    public async Task Tcp_Delta_Gossip_Converges_PNCounters()
    {
        Cluster cluster = await Cluster.StartAsync(ReplicationMode.Delta);
        try
        {
            for (int i = 0; i < cluster.Engines.Count; i++)
            {
                ReplicaId replicaId = ReplicaId.FromUInt64((ulong)i + 200UL);
                cluster.Engines[i].Replica.Value.Increment(replicaId, (ulong)(i + 2));
                cluster.Engines[i].Replica.Value.Decrement(replicaId, 1);
            }

            foreach (ReplicationEngine<PNCounter> engine in cluster.Engines)
            {
                await engine.BroadcastStateAsync();
            }

            await WaitUntilConvergedAsync(cluster.Engines);
        }
        finally
        {
            await cluster.DisposeAsync();
        }
    }

    [Test]
    public async Task Tcp_Operation_Broadcast_Converges_PNCounters()
    {
        Cluster cluster = await Cluster.StartAsync(ReplicationMode.Operation);
        try
        {
            for (int i = 0; i < cluster.Engines.Count; i++)
            {
                ReplicaId replicaId = ReplicaId.FromUInt64((ulong)i + 300UL);
                PNCounterOperation op = cluster.Engines[i].Replica.Value.Increment(replicaId, (ulong)(i + 3));
                await cluster.Engines[i].BroadcastOperationAsync(op.ToByteArray());
            }

            await WaitUntilConvergedAsync(cluster.Engines);
        }
        finally
        {
            await cluster.DisposeAsync();
        }
    }

    private static async Task WaitUntilConvergedAsync(List<ReplicationEngine<PNCounter>> engines)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!timeout.IsCancellationRequested)
        {
            long value = engines[0].Replica.Value.Value;
            if (engines.All(engine => engine.Replica.Value.Value == value))
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100), CancellationToken.None);
        }

        await Assert.That(engines.Select(engine => engine.Replica.Value.Value).ToArray()).IsEquivalentTo(
            Enumerable.Repeat(engines[0].Replica.Value.Value, engines.Count).ToArray());
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
            List<TcpGossipTransport> transports = [];
            List<ReplicationEngine<PNCounter>> engines = [];
            try
            {
                for (int i = 0; i < 3; i++)
                {
                    var transport = new TcpGossipTransport(
                        IPAddress.Loopback,
                        0,
                        TimeSpan.FromMilliseconds(75));
                    await transport.StartAsync();
                    transports.Add(transport);
                }

                foreach (TcpGossipTransport transport in transports)
                {
                    transport.AddPeers(transports.Select(t => t.LocalEndPoint));
                }

                foreach (TcpGossipTransport transport in transports)
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

                foreach (TcpGossipTransport transport in transports)
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
