// Copyright (c) marcschier. Licensed under the MIT License.

using Crdt;
using Crdt.Transport;
using Crdt.Transport.NanoMsg;

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

    await broadcastUntilConvergedAsync(
        counterEngines,
        () => counterEngines.All(e => e.Replica.Value.Value == counterEngines[0].Replica.Value.Value));
    await broadcastUntilConvergedAsync(
        textEngines,
        () => textEngines.All(e => e.Replica.Value.Value == textEngines[0].Replica.Value.Value));

    Console.WriteLine($"PNCounter converged: {counterEngines[0].Replica.Value.Value}");
    Console.WriteLine($"Text converged: {textEngines[0].Replica.Value.Value}");
}
finally
{
    await disposeAllAsync(counterEngines);
    await disposeAllAsync(textEngines);
}

// Starts a three-node BUS mesh over tcp loopback: each node binds an OS-assigned port, then dials every
// lower-indexed node so each pair shares one (bidirectional) connection.
static async Task<List<ReplicationEngine<TState>>> startClusterAsync<TState>(
    Func<TState, byte[]> serialize,
    Func<ReadOnlyMemory<byte>, TState> deserialize)
    where TState : IConvergent<TState>, new()
{
    List<NanoMsgBusTransport> transports = [];
    List<ReplicationEngine<TState>> engines = [];
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

        for (int i = 0; i < transports.Count; i++)
        {
            for (int j = 0; j < i; j++)
            {
                transports[i].AddPeer($"tcp://127.0.0.1:{transports[j].BoundPort}");
            }
        }

        foreach (NanoMsgBusTransport transport in transports)
        {
            var replica = new CrdtReplica<TState>(new TState(), serialize, deserialize);
            engines.Add(new ReplicationEngine<TState>(replica, transport));
        }

        return engines;
    }
    catch
    {
        await disposeAllAsync(engines);
        foreach (NanoMsgBusTransport transport in transports)
        {
            await transport.DisposeAsync();
        }

        throw;
    }
}

// BUS dials in the background, so each node re-broadcasts its state until the mesh is connected and every
// replica agrees. State merges are idempotent, so repeated broadcasts are harmless.
static async Task broadcastUntilConvergedAsync<TState>(
    List<ReplicationEngine<TState>> engines,
    Func<bool> converged)
    where TState : IConvergent<TState>
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
    while (!timeout.IsCancellationRequested)
    {
        foreach (ReplicationEngine<TState> engine in engines)
        {
            await engine.BroadcastStateAsync();
        }

        await Task.Delay(TimeSpan.FromMilliseconds(150));
        if (converged())
        {
            return;
        }
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
