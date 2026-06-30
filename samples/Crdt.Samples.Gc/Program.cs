// Copyright (c) marcschier. Licensed under the MIT License.

using System.Text.Json;
using Crdt.Consensus;
using Crdt.Gc;
using Crdt.Transport;

namespace Crdt.Samples.Gc;

internal static class Program
{
    private const int ReplicaCount = 3;
    private static readonly TimeSpan OperationTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(50);

    private static async Task Main()
    {
        await using SampleCluster cluster = await SampleCluster.StartAsync(ReplicaCount);

        foreach (SampleReplica replica in cluster.Replicas)
        {
            char visible = (char)('A' + replica.Index);
            char deleted = (char)('a' + replica.Index);
            replica.Value.Append(replica.ReplicaId, visible);
            replica.Value.Append(replica.ReplicaId, deleted);
            replica.Value.Delete(replica.Value.Count - 1);
        }

        await cluster.BroadcastUntilConvergedAsync();

        int[] before = cluster.TombstoneCounts();
        Console.WriteLine($"Elected leader: {cluster.LeaderName}");
        Console.WriteLine($"Tombstones before GC: {cluster.FormatTombstoneCounts()}");

        await cluster.StartGarbageCollectionAsync();
        await cluster.WaitForGarbageCollectionAsync(before);

        Console.WriteLine($"Tombstones after GC: {cluster.FormatTombstoneCounts()}");
        Console.WriteLine(
            $"Replicas converged after GC: {cluster.ReplicasConverged()} (value: \"{cluster.VisibleText}\")");
    }

    private sealed class SampleCluster : IAsyncDisposable
    {
        private readonly InMemoryNetwork _network = new();
        private readonly List<SampleReplica> _replicas = [];

        private SampleCluster()
        {
        }

        public IReadOnlyList<SampleReplica> Replicas => _replicas;

        public string LeaderName => _replicas.First(replica => replica.Consensus.IsLeader).Name;

        public string VisibleText => new(_replicas[0].Value.ToArray());

        public static async Task<SampleCluster> StartAsync(int replicaCount)
        {
            var cluster = new SampleCluster();
            try
            {
                ReplicaId[] members = Enumerable.Range(1, replicaCount)
                    .Select(static value => ReplicaId.FromUInt64((ulong)value))
                    .ToArray();

                for (int i = 0; i < members.Length; i++)
                {
                    cluster._replicas.Add(cluster.CreateReplica(i, members[i], members));
                }

                foreach (SampleReplica replica in cluster._replicas)
                {
                    await replica.Engine.StartAsync();
                }

                foreach (SampleReplica replica in cluster._replicas)
                {
                    await replica.Consensus.StartAsync();
                }

                await cluster._network.DrainAsync();
                return cluster;
            }
            catch
            {
                await cluster.DisposeAsync();
                throw;
            }
        }

        public async Task StartGarbageCollectionAsync()
        {
            foreach (SampleReplica replica in _replicas)
            {
                await replica.Coordinator.StartAsync();
            }
        }

        public async Task BroadcastUntilConvergedAsync()
        {
            using var timeout = new CancellationTokenSource(OperationTimeout);
            while (!timeout.IsCancellationRequested)
            {
                foreach (SampleReplica replica in _replicas)
                {
                    await replica.Engine.BroadcastStateAsync();
                }

                await _network.DrainAsync();
                if (ReplicasConverged())
                {
                    return;
                }

                await Task.Delay(PollInterval);
            }

            throw new TimeoutException("The sample cluster did not converge before the deadline.");
        }

        public async Task WaitForGarbageCollectionAsync(int[] before)
        {
            using var timeout = new CancellationTokenSource(OperationTimeout);
            while (!timeout.IsCancellationRequested)
            {
                await _network.DrainAsync();
                if (ReplicasConverged() && TombstonesWereReclaimed(before))
                {
                    return;
                }

                await Task.Delay(PollInterval);
            }

            throw new TimeoutException("The sample cluster did not garbage-collect before the deadline.");
        }

        public bool ReplicasConverged()
        {
            Rga<char> first = _replicas[0].Value;
            return _replicas.Skip(1).All(replica => replica.Value.Equals(first));
        }

        public int[] TombstoneCounts()
        {
            var counts = new int[_replicas.Count];
            for (int i = 0; i < _replicas.Count; i++)
            {
                counts[i] = CountTombstones(_replicas[i].Value);
            }

            return counts;
        }

        public string FormatTombstoneCounts()
        {
            return string.Join(
                ", ",
                _replicas.Select(static replica => $"{replica.Name}={CountTombstones(replica.Value)}"));
        }

        public async ValueTask DisposeAsync()
        {
            foreach (SampleReplica replica in _replicas)
            {
                await replica.DisposeAsync();
            }

            await _network.DisposeAsync();
        }

        private SampleReplica CreateReplica(int index, ReplicaId replicaId, ReplicaId[] members)
        {
            InMemoryTransport transport = _network.CreateTransport();
            var value = new Rga<char>();
            var replica = new CrdtReplica<Rga<char>>(
                value,
                static state => state.ToByteArray(CharSerializer.Instance),
                static bytes => Rga<char>.ReadFrom(bytes.Span, CharSerializer.Instance));
            var engine = new ReplicationEngine<Rga<char>>(replica, transport);
            HeartbeatFailureDetector detector = CreateFailureDetector(replicaId, members, transport);
            var consensus = new DeterministicLeaderConsensus(new DeterministicLeaderConsensusOptions
            {
                LocalReplicaId = replicaId,
                FailureDetector = detector,
                DisposeFailureDetector = false,
                Transport = new ConsensusTransportOptions
                {
                    Transport = transport,
                    StartTransport = false,
                },
            });
            var coordinator = new GarbageCollectionCoordinator(new GarbageCollectionCoordinatorOptions
            {
                LocalReplicaId = replicaId,
                Consensus = consensus,
                FailureDetector = detector,
                Transport = transport,
                StartTransport = false,
                ReportInterval = TimeSpan.FromMilliseconds(200),
                ReportStalenessTimeout = TimeSpan.FromSeconds(3),
            });
            coordinator.Register(value);

            return new SampleReplica(
                index,
                $"node-{index + 1}",
                replicaId,
                value,
                engine,
                detector,
                consensus,
                coordinator);
        }

        private bool TombstonesWereReclaimed(int[] before)
        {
            int[] after = TombstoneCounts();
            for (int i = 0; i < after.Length; i++)
            {
                if (after[i] >= before[i])
                {
                    return false;
                }
            }

            return true;
        }

        private static HeartbeatFailureDetector CreateFailureDetector(
            ReplicaId replicaId,
            IEnumerable<ReplicaId> members,
            ITransport transport)
        {
            var options = new HeartbeatFailureDetectorOptions
            {
                LocalReplicaId = replicaId,
                Transport = new ConsensusTransportOptions
                {
                    Transport = transport,
                    StartTransport = false,
                },
                HeartbeatInterval = TimeSpan.FromSeconds(5),
                Timeout = TimeSpan.FromSeconds(30),
            };

            foreach (ReplicaId member in members)
            {
                options.InitialMembers.Add(member);
            }

            return new HeartbeatFailureDetector(options);
        }
    }

    private sealed class SampleReplica : IAsyncDisposable
    {
        public SampleReplica(
            int index,
            string name,
            ReplicaId replicaId,
            Rga<char> value,
            ReplicationEngine<Rga<char>> engine,
            HeartbeatFailureDetector detector,
            DeterministicLeaderConsensus consensus,
            GarbageCollectionCoordinator coordinator)
        {
            Index = index;
            Name = name;
            ReplicaId = replicaId;
            Value = value;
            Engine = engine;
            Detector = detector;
            Consensus = consensus;
            Coordinator = coordinator;
        }

        public int Index { get; }

        public string Name { get; }

        public ReplicaId ReplicaId { get; }

        public Rga<char> Value { get; }

        public ReplicationEngine<Rga<char>> Engine { get; }

        public HeartbeatFailureDetector Detector { get; }

        public DeterministicLeaderConsensus Consensus { get; }

        public GarbageCollectionCoordinator Coordinator { get; }

        public async ValueTask DisposeAsync()
        {
            await Coordinator.DisposeAsync();
            await Consensus.DisposeAsync();
            await Detector.DisposeAsync();
            await Engine.DisposeAsync();
        }
    }

    private sealed class CharSerializer : ICrdtValueSerializer<char>
    {
        public static CharSerializer Instance { get; } = new();

        public void Write(ref CrdtWriter writer, char value)
        {
            writer.WriteVarUInt64(value);
        }

        public char Read(ref CrdtReader reader)
        {
            return (char)reader.ReadVarUInt64();
        }

        public void WriteJson(Utf8JsonWriter writer, char value)
        {
            writer.WriteStringValue(value.ToString());
        }

        public char ReadJson(ref Utf8JsonReader reader)
        {
            string? text = reader.GetString();
            return text is null or "" ? '\0' : text[0];
        }
    }

    private static int CountTombstones(Rga<char> value)
    {
        using JsonDocument json = JsonDocument.Parse(value.ToJson(CharSerializer.Instance));
        return json.RootElement.GetProperty("deleted").GetArrayLength();
    }
}
