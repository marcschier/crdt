// Copyright (c) marcschier. Licensed under the MIT License.

using Pgm.Net;

namespace Crdt.Transport.Pgm.Tests;

/// <summary>
/// End-to-end convergence tests over a shared in-memory PGM multicast bus — no real multicast network is
/// required.
/// </summary>
public sealed class PgmBusConvergenceTests
{
    [Test]
    public async Task Bus_State_Gossip_Converges_Two_GCounters()
    {
        Cluster<GCounter> cluster = await Cluster<GCounter>.StartAsync(
            2,
            static () => new GCounter(),
            static counter => counter.ToByteArray(),
            static bytes => GCounter.ReadFrom(bytes.Span));
        try
        {
            for (int i = 0; i < cluster.Engines.Count; i++)
            {
                ReplicaId replicaId = ReplicaId.FromUInt64((ulong)i + 3_100UL);
                cluster.Engines[i].Replica.Value.Increment(replicaId, (ulong)(i + 1));
            }

            await WaitUntilConvergedAsync(
                cluster.Engines,
                static engines => engines.All(engine => engine.Replica.Value.Value == 3UL));
        }
        finally
        {
            await cluster.DisposeAsync();
        }
    }

    [Test]
    public async Task Bus_State_Gossip_Converges_Three_ORSets()
    {
        Cluster<ORSet<string>> cluster = await Cluster<ORSet<string>>.StartAsync(
            3,
            static () => new ORSet<string>(),
            static set => set.ToByteArray(CrdtValues.String),
            static bytes => ORSet<string>.ReadFrom(bytes.Span, CrdtValues.String));
        try
        {
            for (int i = 0; i < cluster.Engines.Count; i++)
            {
                ReplicaId replicaId = ReplicaId.FromUInt64((ulong)i + 3_200UL);
                cluster.Engines[i].Replica.Value.Add(replicaId, $"node-{i}");
            }

            await WaitUntilConvergedAsync(
                cluster.Engines,
                static engines => engines.All(engine => HasExpectedElements(engine.Replica.Value)));
        }
        finally
        {
            await cluster.DisposeAsync();
        }
    }

    private static bool HasExpectedElements(ORSet<string> set)
    {
        return set.Count == 3
            && set.Contains("node-0")
            && set.Contains("node-1")
            && set.Contains("node-2");
    }

    private static async Task WaitUntilConvergedAsync<TState>(
        IReadOnlyList<ReplicationEngine<TState>> engines,
        Func<IReadOnlyList<ReplicationEngine<TState>>, bool> converged)
        where TState : IConvergent<TState>
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (!timeout.IsCancellationRequested)
        {
            foreach (ReplicationEngine<TState> engine in engines)
            {
                await engine.BroadcastStateAsync();
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50), CancellationToken.None);
            if (converged(engines))
            {
                return;
            }
        }

        await Assert.That(converged(engines)).IsTrue();
    }

    private sealed class Cluster<TState> : IAsyncDisposable
        where TState : IConvergent<TState>
    {
        private Cluster(List<ReplicationEngine<TState>> engines)
        {
            Engines = engines;
        }

        public List<ReplicationEngine<TState>> Engines { get; }

        public static async Task<Cluster<TState>> StartAsync(
            int replicaCount,
            Func<TState> createState,
            Func<TState, byte[]> serialize,
            Func<ReadOnlyMemory<byte>, TState> deserialize)
        {
            var bus = new InMemoryMulticastBus();
            List<ReplicationEngine<TState>> engines = [];
            try
            {
                for (int i = 0; i < replicaCount; i++)
                {
                    var transport = new PgmBusTransport(new PgmBusTransportOptions
                    {
                        InMemoryBus = bus,
                    });
                    var replica = new CrdtReplica<TState>(createState(), serialize, deserialize);
                    var engine = new ReplicationEngine<TState>(replica, transport);
                    await engine.StartAsync();
                    engines.Add(engine);
                }

                return new Cluster<TState>(engines);
            }
            catch
            {
                foreach (ReplicationEngine<TState> engine in engines)
                {
                    await engine.DisposeAsync();
                }

                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            foreach (ReplicationEngine<TState> engine in Engines)
            {
                await engine.DisposeAsync();
            }
        }
    }
}
