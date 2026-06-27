// Copyright (c) marcschier. Licensed under the MIT License.

using Crdt;
using Crdt.Transport;
using Crdt.Transport.Mqtt;

// Resolve the broker: first CLI argument, else CRDT_MQTT_BROKER (host:port), else localhost:1883.
string brokerUri = args.Length > 0
    ? args[0]
    : "mqtt://" + (Environment.GetEnvironmentVariable("CRDT_MQTT_BROKER") ?? "localhost:1883");

// A unique root per run keeps repeated runs (and their retained messages) isolated on the broker.
string runRoot = "crdt/sample/" + Guid.NewGuid().ToString("N");

Console.WriteLine($"Broker:    {brokerUri}");
Console.WriteLine($"TopicRoot: {runRoot}");

List<ReplicationEngine<PNCounter>> counterEngines = [];
List<ReplicationEngine<Text>> textEngines = [];
try
{
    counterEngines = await startClusterAsync(
        "pncounter",
        static counter => counter.ToByteArray(),
        static bytes => PNCounter.ReadFrom(bytes.Span));
    textEngines = await startClusterAsync(
        "text",
        static text => text.ToByteArray(),
        static bytes => Text.ReadFrom(bytes.Span));

    for (int i = 0; i < counterEngines.Count; i++)
    {
        ReplicaId replica = ReplicaId.FromUInt64((ulong)i + 1UL);
        counterEngines[i].Replica.Value.Increment(replica, (ulong)(10 + i));
        counterEngines[i].Replica.Value.Decrement(replica, (ulong)i + 1UL);
        textEngines[i].Replica.Value.Append(replica, $"node-{i};");
    }

    foreach (ReplicationEngine<PNCounter> engine in counterEngines)
    {
        await engine.BroadcastStateAsync();
    }

    foreach (ReplicationEngine<Text> engine in textEngines)
    {
        await engine.BroadcastStateAsync();
    }

    await waitUntilAsync(
        () => counterEngines.All(engine => engine.Replica.Value.Value == counterEngines[0].Replica.Value.Value));
    await waitUntilAsync(
        () => textEngines.All(engine => engine.Replica.Value.Value == textEngines[0].Replica.Value.Value));

    Console.WriteLine($"PNCounter converged: {counterEngines[0].Replica.Value.Value}");
    Console.WriteLine($"Text converged: {textEngines[0].Replica.Value.Value}");
}
finally
{
    await disposeAllAsync(counterEngines);
    await disposeAllAsync(textEngines);
}

// Starts three replicas that gossip over MQTT under a per-type topic root, so the counter and text
// clusters never receive each other's frames.
async Task<List<ReplicationEngine<TState>>> startClusterAsync<TState>(
    string name,
    Func<TState, byte[]> serialize,
    Func<ReadOnlyMemory<byte>, TState> deserialize)
    where TState : IConvergent<TState>, new()
{
    string topicRoot = runRoot + "/" + name;
    List<MqttGossipTransport> transports = [];
    List<ReplicationEngine<TState>> engines = [];
    try
    {
        for (int i = 0; i < 3; i++)
        {
            var transport = new MqttGossipTransport(new MqttGossipTransportOptions
            {
                BrokerUri = brokerUri,
                TopicRoot = topicRoot,
                ClientId = $"node-{i}-{Guid.NewGuid():N}",
            });
            await transport.StartAsync();
            transports.Add(transport);
        }

        foreach (MqttGossipTransport transport in transports)
        {
            var replica = new CrdtReplica<TState>(new TState(), serialize, deserialize);
            engines.Add(new ReplicationEngine<TState>(replica, transport));
        }

        return engines;
    }
    catch
    {
        await disposeAllAsync(engines);
        foreach (MqttGossipTransport transport in transports)
        {
            await transport.DisposeAsync();
        }

        throw;
    }
}

static async Task waitUntilAsync(Func<bool> condition)
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
    while (!timeout.IsCancellationRequested)
    {
        if (condition())
        {
            return;
        }

        await Task.Delay(TimeSpan.FromMilliseconds(100));
    }

    throw new TimeoutException("The sample cluster did not converge before the deadline.");
}

static async ValueTask disposeAllAsync<TState>(IEnumerable<ReplicationEngine<TState>> engines)
    where TState : IConvergent<TState>
{
    foreach (ReplicationEngine<TState> engine in engines)
    {
        await engine.DisposeAsync();
    }
}
