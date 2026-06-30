// Copyright (c) marcschier. Licensed under the MIT License.

namespace Crdt.Consensus.Tests;

public sealed class DeterministicLeaderConsensusTests
{
    private static readonly ReplicaId A = ReplicaId.FromUInt64(1);
    private static readonly ReplicaId B = ReplicaId.FromUInt64(2);
    private static readonly ReplicaId C = ReplicaId.FromUInt64(3);

    [Test]
    public async Task Constructor_LiveMembers_ElectsLowestReplicaId()
    {
        var bus = new InProcessMessageBus();
        await using var detector = new ManualFailureDetector(A, A, B, C);
        await using var consensus = CreateConsensus(A, detector, bus);

        await Assert.That(consensus.LeaderId).IsEqualTo(A);
        await Assert.That(consensus.Role).IsEqualTo(ConsensusRole.Leader);
        await Assert.That(consensus.IsLeader).IsTrue();
    }

    [Test]
    public async Task MembersChanged_RemovesLowestReplicaId_ElectsNextLowestReplicaId()
    {
        var bus = new InProcessMessageBus();
        await using var detector = new ManualFailureDetector(A, A, B, C);
        await using var consensus = CreateConsensus(B, detector, bus);
        ReplicaId? observedLeader = null;
        consensus.LeadershipChanged += leaderId => observedLeader = leaderId;

        detector.RemoveMember(A);

        await Assert.That(consensus.LeaderId).IsEqualTo(B);
        await Assert.That(observedLeader).IsEqualTo(B);
        await Assert.That(consensus.Role).IsEqualTo(ConsensusRole.Leader);
    }

    [Test]
    public async Task ProposeAsync_LeaderReceivesAllAcks_CommitsToAllMembers()
    {
        var bus = new InProcessMessageBus();
        await using var detectorA = new ManualFailureDetector(A, A, B, C);
        await using var detectorB = new ManualFailureDetector(B, A, B, C);
        await using var detectorC = new ManualFailureDetector(C, A, B, C);
        await using DeterministicLeaderConsensus consensusA = CreateConsensus(A, detectorA, bus);
        await using DeterministicLeaderConsensus consensusB = CreateConsensus(B, detectorB, bus);
        await using DeterministicLeaderConsensus consensusC = CreateConsensus(C, detectorC, bus);
        List<byte[]> committedA = [];
        List<byte[]> committedB = [];
        List<byte[]> committedC = [];
        consensusA.EntryCommitted += entry => committedA.Add(entry.ToArray());
        consensusB.EntryCommitted += entry => committedB.Add(entry.ToArray());
        consensusC.EntryCommitted += entry => committedC.Add(entry.ToArray());
        await consensusA.StartAsync();
        await consensusB.StartAsync();
        await consensusC.StartAsync();

        bool committed = await consensusA.ProposeAsync(new byte[] { 1, 2, 3 });

        await Assert.That(committed).IsTrue();
        await Assert.That(committedA.Select(e => e.ToArray()).ToArray()).IsEquivalentTo([new byte[] { 1, 2, 3 }]);
        await Assert.That(committedB.Select(e => e.ToArray()).ToArray()).IsEquivalentTo([new byte[] { 1, 2, 3 }]);
        await Assert.That(committedC.Select(e => e.ToArray()).ToArray()).IsEquivalentTo([new byte[] { 1, 2, 3 }]);
    }

    private static DeterministicLeaderConsensus CreateConsensus(
        ReplicaId localReplicaId,
        IFailureDetector detector,
        InProcessMessageBus bus) =>
        new(new DeterministicLeaderConsensusOptions
        {
            LocalReplicaId = localReplicaId,
            FailureDetector = detector,
            DisposeFailureDetector = false,
            Transport = bus.CreateEndpoint(localReplicaId),
        });

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

        public ValueTask DisposeAsync() => default;
    }

    private sealed class InProcessMessageBus
    {
        private readonly object _gate = new();
        private readonly Dictionary<ReplicaId, List<Action<ReadOnlyMemory<byte>>>> _receivers = [];

        public ConsensusTransportOptions CreateEndpoint(ReplicaId localReplicaId) =>
            new()
            {
                SendAsync = (message, ct) => SendAsync(localReplicaId, message, ct),
                RegisterReceiver = receiver => Register(localReplicaId, receiver),
                UnregisterReceiver = receiver => Unregister(localReplicaId, receiver),
            };

        private ValueTask SendAsync(ReplicaId sender, ReadOnlyMemory<byte> message, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            List<Action<ReadOnlyMemory<byte>>> receivers = [];
            lock (_gate)
            {
                foreach (KeyValuePair<ReplicaId, List<Action<ReadOnlyMemory<byte>>>> endpoint in _receivers)
                {
                    if (endpoint.Key == sender)
                    {
                        continue;
                    }

                    receivers.AddRange(endpoint.Value);
                }
            }

            byte[] copy = message.ToArray();
            foreach (Action<ReadOnlyMemory<byte>> receiver in receivers)
            {
                receiver(copy);
            }

            return default;
        }

        private void Register(ReplicaId localReplicaId, Action<ReadOnlyMemory<byte>> receiver)
        {
            lock (_gate)
            {
                if (!_receivers.TryGetValue(localReplicaId, out List<Action<ReadOnlyMemory<byte>>>? receivers))
                {
                    receivers = [];
                    _receivers.Add(localReplicaId, receivers);
                }

                receivers.Add(receiver);
            }
        }

        private void Unregister(ReplicaId localReplicaId, Action<ReadOnlyMemory<byte>> receiver)
        {
            lock (_gate)
            {
                if (_receivers.TryGetValue(localReplicaId, out List<Action<ReadOnlyMemory<byte>>>? receivers))
                {
                    receivers.Remove(receiver);
                }
            }
        }
    }
}
