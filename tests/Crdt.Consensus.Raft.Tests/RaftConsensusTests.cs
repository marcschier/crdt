// Copyright (c) marcschier. Licensed under the MIT License.

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using Crdt.Transport;

namespace Crdt.Consensus.Raft.Tests;

public sealed class RaftConsensusTests
{
    private static readonly ReplicaId A = ReplicaId.FromUInt64(1);
    private static readonly ReplicaId B = ReplicaId.FromUInt64(2);
    private static readonly ReplicaId C = ReplicaId.FromUInt64(3);

    [Test]
    public async Task ThreeNodes_ElectLeader_AndReplicateProposalToAll()
    {
        await using var cluster = new RaftCluster(A, B, C);
        await cluster.StartAsync();

        RaftConsensus leader = await cluster.WaitForSingleLeaderAsync(TimeSpan.FromSeconds(10));
        byte[] payload = Encoding.UTF8.GetBytes("set x = 1");

        bool committed = await leader.ProposeAsync(payload);

        await Assert.That(committed).IsTrue();
        await cluster.WaitForCommittedAsync(payload, TimeSpan.FromSeconds(10));
        await Assert.That(cluster.CommittedCount(payload)).IsEqualTo(3);
    }

    [Test]
    public async Task Follower_Propose_ReturnsFalse()
    {
        await using var cluster = new RaftCluster(A, B, C);
        await cluster.StartAsync();

        RaftConsensus leader = await cluster.WaitForSingleLeaderAsync(TimeSpan.FromSeconds(10));
        RaftConsensus follower = cluster.First(node => !node.IsLeader);

        bool committed = await follower.ProposeAsync(Encoding.UTF8.GetBytes("rejected"));

        await Assert.That(committed).IsFalse();
        await Assert.That(leader.IsLeader).IsTrue();
    }

    private sealed class RaftCluster : IAsyncDisposable
    {
        private readonly InMemoryNetwork _network = new();
        private readonly List<RaftConsensus> _nodes = [];
        private readonly List<ManualFailureDetector> _detectors = [];
        private readonly ConcurrentDictionary<RaftConsensus, ConcurrentBag<byte[]>> _committed = [];

        public RaftCluster(params ReplicaId[] members)
        {
            int tick = 3;
            foreach (ReplicaId member in members)
            {
                var detector = new ManualFailureDetector(member, members);
                InMemoryTransport transport = _network.CreateTransport();
                var registry = new DefaultReplicaIdRegistry();
                registry.Register(A, 1);
                registry.Register(B, 2);
                registry.Register(C, 3);

                var node = new RaftConsensus(new RaftConsensusOptions
                {
                    LocalReplicaId = member,
                    FailureDetector = detector,
                    DisposeFailureDetector = false,
                    ReplicaIdRegistry = registry,
                    ElectionTick = tick,
                    HeartbeatTick = 1,
                    TickInterval = TimeSpan.FromMilliseconds(10),
                    RandomizedElectionTimeout = tick,
                    Transport = new ConsensusTransportOptions
                    {
                        Transport = transport,
                        StartTransport = true,
                    },
                });

                var committed = new ConcurrentBag<byte[]>();
                _committed[node] = committed;
                node.EntryCommitted += entry => committed.Add(entry.ToArray());

                _nodes.Add(node);
                _detectors.Add(detector);

                // Stagger election timeouts so one node consistently campaigns first.
                tick += 4;
            }
        }

        public async Task StartAsync()
        {
            foreach (RaftConsensus node in _nodes)
            {
                await node.StartAsync();
            }
        }

        public RaftConsensus First(Func<RaftConsensus, bool> predicate)
        {
            return _nodes.First(predicate);
        }

        public int CommittedCount(byte[] payload)
        {
            int count = 0;
            foreach (RaftConsensus node in _nodes)
            {
                if (_committed[node].Any(entry => entry.AsSpan().SequenceEqual(payload)))
                {
                    count++;
                }
            }

            return count;
        }

        public async Task<RaftConsensus> WaitForSingleLeaderAsync(TimeSpan timeout)
        {
            var stopwatch = Stopwatch.StartNew();
            while (stopwatch.Elapsed < timeout)
            {
                List<RaftConsensus> leaders = _nodes.Where(node => node.IsLeader).ToList();
                if (leaders.Count == 1)
                {
                    return leaders[0];
                }

                await Task.Delay(20);
            }

            throw new TimeoutException("No single Raft leader was elected within the timeout.");
        }

        public async Task WaitForCommittedAsync(byte[] payload, TimeSpan timeout)
        {
            var stopwatch = Stopwatch.StartNew();
            while (stopwatch.Elapsed < timeout)
            {
                if (CommittedCount(payload) == _nodes.Count)
                {
                    return;
                }

                await Task.Delay(20);
            }

            throw new TimeoutException("The proposal was not committed by all nodes within the timeout.");
        }

        public async ValueTask DisposeAsync()
        {
            foreach (RaftConsensus node in _nodes)
            {
                await node.DisposeAsync();
            }

            foreach (ManualFailureDetector detector in _detectors)
            {
                await detector.DisposeAsync();
            }

            await _network.DisposeAsync();
        }
    }

    private sealed class ManualFailureDetector : IFailureDetector
    {
        private readonly SortedSet<ReplicaId> _members;

        public ManualFailureDetector(ReplicaId localReplicaId, params ReplicaId[] members)
        {
            LocalReplicaId = localReplicaId;
            _members = new SortedSet<ReplicaId>(members)
            {
                localReplicaId,
            };
        }

        public event Action? MembersChanged;

        public ReplicaId LocalReplicaId { get; }

        public IReadOnlyCollection<ReplicaId> Members => _members.ToArray();

        public ValueTask StartAsync(CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            _ = MembersChanged;
            return default;
        }

        public void AddMember(ReplicaId replicaId)
        {
            if (_members.Add(replicaId))
            {
                MembersChanged?.Invoke();
            }
        }

        public bool RemoveMember(ReplicaId replicaId)
        {
            bool removed = _members.Remove(replicaId);
            if (removed)
            {
                MembersChanged?.Invoke();
            }

            return removed;
        }

        public void ObserveHeartbeat(ReplicaId replicaId)
        {
            AddMember(replicaId);
        }

        public ValueTask DisposeAsync()
        {
            return default;
        }
    }
}
