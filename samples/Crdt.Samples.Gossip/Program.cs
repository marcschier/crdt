// Copyright (c) marcschier. Licensed under the MIT License.

using System.Net;

using Crdt;
using Crdt.Transport;

List<ReplicationEngine<PNCounter>> counterEngines = [];
List<ReplicationEngine<Text>> textEngines = [];
try
{
    counterEngines = await startClusterAsync(
        static counter => counter.ToByteArray(),
        static bytes => PNCounter.ReadFrom(bytes.Span));
    textEngines = await startClusterAsync(
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

static async Task<List<ReplicationEngine<TState>>> startClusterAsync<TState>(
    Func<TState, byte[]> serialize,
    Func<ReadOnlyMemory<byte>, TState> deserialize)
    where TState : IConvergent<TState>, new()
{
    List<TcpGossipTransport> transports = [];
    List<ReplicationEngine<TState>> engines = [];
    try
    {
        for (int i = 0; i < 3; i++)
        {
            var transport = new TcpGossipTransport(IPAddress.Loopback, 0, TimeSpan.FromMilliseconds(100));
            await transport.StartAsync();
            transports.Add(transport);
        }

        foreach (TcpGossipTransport transport in transports)
        {
            transport.AddPeers(transports.Select(peer => peer.LocalEndPoint));
        }

        foreach (TcpGossipTransport transport in transports)
        {
            var replica = new CrdtReplica<TState>(new TState(), serialize, deserialize);
            engines.Add(new ReplicationEngine<TState>(replica, transport));
        }

        return engines;
    }
    catch
    {
        await disposeAllAsync(engines);
        foreach (TcpGossipTransport transport in transports)
        {
            await transport.DisposeAsync();
        }

        throw;
    }
}

static async Task waitUntilAsync(Func<bool> condition)
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
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
