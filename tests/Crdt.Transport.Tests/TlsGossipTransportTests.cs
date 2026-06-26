// Copyright (c) marcschier. Licensed under the MIT License.

using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Crdt.Transport.Tests;

public sealed class TlsGossipTransportTests
{
    [Test]
    public async Task Tls_State_Gossip_Converges_PNCounters()
    {
        Cluster cluster = await Cluster.StartAsync(ReplicationMode.State, mutualTls: false);
        try
        {
            for (int i = 0; i < cluster.Engines.Count; i++)
            {
                ReplicaId replicaId = ReplicaId.FromUInt64((ulong)i + 400UL);
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
    public async Task Tls_Delta_Gossip_Converges_PNCounters()
    {
        Cluster cluster = await Cluster.StartAsync(ReplicationMode.Delta, mutualTls: false);
        try
        {
            for (int i = 0; i < cluster.Engines.Count; i++)
            {
                ReplicaId replicaId = ReplicaId.FromUInt64((ulong)i + 500UL);
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
    public async Task MutualTls_State_Gossip_Converges_PNCounters()
    {
        Cluster cluster = await Cluster.StartAsync(ReplicationMode.State, mutualTls: true);
        try
        {
            for (int i = 0; i < cluster.Engines.Count; i++)
            {
                ReplicaId replicaId = ReplicaId.FromUInt64((ulong)i + 600UL);
                cluster.Engines[i].Replica.Value.Increment(replicaId, (ulong)(i + 3));
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

    private static X509Certificate2 CreateSelfSignedCertificate()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=crdt-gossip-tests", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using X509Certificate2 certificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));

        // SslStream server authentication on Windows needs a private key loaded into a key set, so
        // round-trip the freshly created certificate through a PKCS#12 blob.
        byte[] pfx = certificate.Export(X509ContentType.Pfx);
#if NET9_0_OR_GREATER
        return X509CertificateLoader.LoadPkcs12(pfx, null, X509KeyStorageFlags.Exportable);
#else
        return new X509Certificate2(pfx, (string?)null, X509KeyStorageFlags.Exportable);
#endif
    }

    private sealed class Cluster : IAsyncDisposable
    {
        private readonly X509Certificate2 _certificate;

        private Cluster(List<ReplicationEngine<PNCounter>> engines, X509Certificate2 certificate)
        {
            Engines = engines;
            _certificate = certificate;
        }

        public List<ReplicationEngine<PNCounter>> Engines { get; }

        public static async Task<Cluster> StartAsync(ReplicationMode mode, bool mutualTls)
        {
            X509Certificate2 certificate = CreateSelfSignedCertificate();
            byte[] expectedHash = certificate.GetCertHash();
            List<TcpGossipTransport> transports = [];
            List<ReplicationEngine<PNCounter>> engines = [];
            try
            {
                for (int i = 0; i < 3; i++)
                {
                    var tls = new GossipTlsOptions
                    {
                        ServerCertificate = certificate,
                        TargetHost = "crdt-gossip-tests",
                        RemoteCertificateValidationCallback = (_, cert, _, _) =>
                            cert is not null && cert.GetCertHash().AsSpan().SequenceEqual(expectedHash),
                        RequireClientCertificate = mutualTls,
                        ClientCertificates = mutualTls
                            ? [certificate]
                            : null,
                    };
                    var transport = new TcpGossipTransport(new TcpGossipTransportOptions
                    {
                        Address = IPAddress.Loopback,
                        Port = 0,
                        GossipInterval = TimeSpan.FromMilliseconds(75),
                        Tls = tls,
                    });
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

                return new Cluster(engines, certificate);
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

                certificate.Dispose();
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            foreach (ReplicationEngine<PNCounter> engine in Engines)
            {
                await engine.DisposeAsync();
            }

            _certificate.Dispose();
        }

        private static PNCounter ReadPNCounter(ReadOnlyMemory<byte> bytes) => PNCounter.ReadFrom(bytes.Span);
    }
}
