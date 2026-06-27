// Copyright (c) marcschier. Licensed under the MIT License.

using Mqtt.Client;

namespace Crdt.Transport.Mqtt.Tests;

/// <summary>
/// End-to-end convergence tests that require a reachable MQTT broker. Set the environment variable
/// <c>CRDT_MQTT_BROKER</c> (for example <c>localhost:1883</c>) to enable them; otherwise each test self-skips.
/// </summary>
public sealed class MqttGossipConvergenceTests
{
    [Test]
    public async Task Mqtt_State_Gossip_Converges_PNCounters()
    {
        string brokerUri = RequireBroker();
        Cluster cluster = await Cluster.StartAsync(ReplicationMode.State, brokerUri);
        try
        {
            for (int i = 0; i < cluster.Engines.Count; i++)
            {
                ReplicaId replicaId = ReplicaId.FromUInt64((ulong)i + 1_700UL);
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
    public async Task Mqtt_Delta_Gossip_Converges_PNCounters()
    {
        string brokerUri = RequireBroker();
        Cluster cluster = await Cluster.StartAsync(ReplicationMode.Delta, brokerUri);
        try
        {
            for (int i = 0; i < cluster.Engines.Count; i++)
            {
                ReplicaId replicaId = ReplicaId.FromUInt64((ulong)i + 1_800UL);
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
    public async Task Mqtt_Operation_Broadcast_Converges_PNCounters()
    {
        string brokerUri = RequireBroker();
        Cluster cluster = await Cluster.StartAsync(ReplicationMode.Operation, brokerUri);
        try
        {
            for (int i = 0; i < cluster.Engines.Count; i++)
            {
                ReplicaId replicaId = ReplicaId.FromUInt64((ulong)i + 1_900UL);
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

    private static string RequireBroker()
    {
        string? value = Environment.GetEnvironmentVariable("CRDT_MQTT_BROKER");
        Skip.Unless(
            !string.IsNullOrWhiteSpace(value),
            "Set CRDT_MQTT_BROKER (host:port) to run the MQTT broker integration tests.");
        return "mqtt://" + value;
    }

    private static async Task WaitUntilConvergedAsync(List<ReplicationEngine<PNCounter>> engines)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (!timeout.IsCancellationRequested)
        {
            long value = engines[0].Replica.Value.Value;
            if (value != 0 && engines.All(engine => engine.Replica.Value.Value == value))
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100), CancellationToken.None);
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

        public static async Task<Cluster> StartAsync(ReplicationMode mode, string brokerUri)
        {
            string topicRoot = "crdt/test/" + Guid.NewGuid().ToString("N");
            List<MqttGossipTransport> transports = [];
            List<ReplicationEngine<PNCounter>> engines = [];
            try
            {
                for (int i = 0; i < 3; i++)
                {
                    var transport = new MqttGossipTransport(new MqttGossipTransportOptions
                    {
                        BrokerUri = brokerUri,
                        TopicRoot = topicRoot,
                        ClientId = "node-" + i + "-" + Guid.NewGuid().ToString("N"),
                        Qos = MqttQoS.AtLeastOnce,
                    });
                    await transport.StartAsync();
                    transports.Add(transport);
                }

                foreach (MqttGossipTransport transport in transports)
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

                foreach (MqttGossipTransport transport in transports)
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
