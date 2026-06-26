// Copyright (c) marcschier. Licensed under the MIT License.

using System.Net;

namespace Crdt.Transport.Tests;

public sealed class UdpGossipTransportTests
{
    [Test]
    public async Task Udp_State_Gossip_Converges_PNCounters()
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
    public async Task Udp_Delta_Gossip_Converges_PNCounters()
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
    public async Task Udp_Operation_Broadcast_Converges_PNCounters()
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

    [Test]
    public async Task Udp_LocalEndPoint_Reflects_Bound_Port()
    {
        var transport = new UdpGossipTransport(IPAddress.Loopback, 0);
        bool throwsBeforeStart = false;
        try
        {
            _ = transport.LocalEndPoint;
        }
        catch (InvalidOperationException)
        {
            throwsBeforeStart = true;
        }

        await Assert.That(throwsBeforeStart).IsTrue();

        await transport.StartAsync();
        try
        {
            await Assert.That(transport.LocalEndPoint.Port > 0).IsTrue();
        }
        finally
        {
            await transport.DisposeAsync();
        }
    }

    [Test]
    public async Task Udp_SendAsync_Rejects_Oversize_Frame()
    {
        var transport = new UdpGossipTransport(new UdpGossipTransportOptions
        {
            Address = IPAddress.Loopback,
            Port = 0,
            MaxDatagramSize = 16,
        });

        await transport.StartAsync();
        bool rejected = false;
        try
        {
            byte[] big = FrameCodec.Encode(MessageType.State, new byte[64]);
            try
            {
                await transport.SendAsync(big);
            }
            catch (ArgumentException)
            {
                rejected = true;
            }

            await Assert.That(rejected).IsTrue();
        }
        finally
        {
            await transport.DisposeAsync();
        }
    }

    private static async Task WaitUntilConvergedAsync(List<ReplicationEngine<PNCounter>> engines)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (!timeout.IsCancellationRequested)
        {
            long value = engines[0].Replica.Value.Value;
            if (engines.All(engine => engine.Replica.Value.Value == value))
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100), CancellationToken.None);
        }

        long[] values = engines.Select(engine => engine.Replica.Value.Value).ToArray();
        await Assert.That(values.All(v => v == values[0])).IsTrue();
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
            List<UdpGossipTransport> transports = [];
            List<ReplicationEngine<PNCounter>> engines = [];
            try
            {
                for (int i = 0; i < 3; i++)
                {
                    var transport = new UdpGossipTransport(
                        IPAddress.Loopback,
                        0,
                        TimeSpan.FromMilliseconds(75));
                    await transport.StartAsync();
                    transports.Add(transport);
                }

                foreach (UdpGossipTransport transport in transports)
                {
                    transport.AddPeers(transports.Select(t => t.LocalEndPoint));
                }

                foreach (UdpGossipTransport transport in transports)
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

                foreach (UdpGossipTransport transport in transports)
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
