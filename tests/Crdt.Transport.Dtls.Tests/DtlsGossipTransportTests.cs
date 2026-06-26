// Copyright (c) marcschier. Licensed under the MIT License.

using System.Net;
using System.Text;
using Dtls;

namespace Crdt.Transport.Dtls.Tests;

public sealed class DtlsGossipTransportTests
{
    private static readonly byte[] PskIdentity = Encoding.UTF8.GetBytes("crdt-gossip");

    private static readonly byte[] PskKey =
    [
        0x00, 0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77,
        0x88, 0x99, 0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF,
    ];

    [Test]
    public async Task Dtls_State_Gossip_Converges_PNCounters()
    {
        Cluster cluster = await Cluster.StartAsync(ReplicationMode.State);
        try
        {
            for (int i = 0; i < cluster.Engines.Count; i++)
            {
                ReplicaId replicaId = ReplicaId.FromUInt64((ulong)i + 700UL);
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
    public async Task Dtls_Delta_Gossip_Converges_PNCounters()
    {
        Cluster cluster = await Cluster.StartAsync(ReplicationMode.Delta);
        try
        {
            for (int i = 0; i < cluster.Engines.Count; i++)
            {
                ReplicaId replicaId = ReplicaId.FromUInt64((ulong)i + 800UL);
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
    public async Task Dtls_Operation_Broadcast_Converges_PNCounters()
    {
        Cluster cluster = await Cluster.StartAsync(ReplicationMode.Operation);
        try
        {
            for (int i = 0; i < cluster.Engines.Count; i++)
            {
                ReplicaId replicaId = ReplicaId.FromUInt64((ulong)i + 900UL);
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
    public async Task Dtls_LocalEndPoint_Reflects_Bound_Port()
    {
        var transport = new DtlsGossipTransport(new DtlsGossipTransportOptions
        {
            Address = IPAddress.Loopback,
            Port = 0,
            ServerOptions = CreateServerOptions(),
            ClientOptions = CreateClientOptions(),
        });

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
    public async Task Dtls_SendAsync_Rejects_Oversize_Frame()
    {
        var transport = new DtlsGossipTransport(new DtlsGossipTransportOptions
        {
            Address = IPAddress.Loopback,
            Port = 0,
            MaxDatagramSize = 16,
            ServerOptions = CreateServerOptions(),
            ClientOptions = CreateClientOptions(),
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

    private static DtlsServerOptions CreateServerOptions() => new()
    {
        MinimumVersion = DtlsProtocolVersion.Dtls13,
        MaximumVersion = DtlsProtocolVersion.Dtls13,
        PskCallback = static _ => PskKey,
    };

    private static DtlsClientOptions CreateClientOptions() => new()
    {
        MinimumVersion = DtlsProtocolVersion.Dtls13,
        MaximumVersion = DtlsProtocolVersion.Dtls13,
        PskCallback = static _ => new PskCredential(PskIdentity, PskKey),
    };

    private static async Task WaitUntilConvergedAsync(List<ReplicationEngine<PNCounter>> engines)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
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
            DtlsServerOptions serverOptions = CreateServerOptions();
            DtlsClientOptions clientOptions = CreateClientOptions();
            List<DtlsGossipTransport> transports = [];
            List<ReplicationEngine<PNCounter>> engines = [];
            try
            {
                for (int i = 0; i < 3; i++)
                {
                    var transport = new DtlsGossipTransport(new DtlsGossipTransportOptions
                    {
                        Address = IPAddress.Loopback,
                        Port = 0,
                        GossipInterval = TimeSpan.FromMilliseconds(100),
                        ServerOptions = serverOptions,
                        ClientOptions = clientOptions,
                    });
                    await transport.StartAsync();
                    transports.Add(transport);
                }

                foreach (DtlsGossipTransport transport in transports)
                {
                    transport.AddPeers(transports.Select(t => t.LocalEndPoint));
                }

                foreach (DtlsGossipTransport transport in transports)
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

                foreach (DtlsGossipTransport transport in transports)
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
