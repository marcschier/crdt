// Copyright (c) marcschier. Licensed under the MIT License.

using Crdt.Consensus;
using Crdt.Transport;

namespace Crdt.Gc.Tests;

public sealed class GarbageCollectionCoordinatorTests
{
    private static readonly ReplicaId A = ReplicaId.FromUInt64(1);
    private static readonly ReplicaId B = ReplicaId.FromUInt64(2);
    private static readonly ReplicaId C = ReplicaId.FromUInt64(3);

    [Test]
    public async Task LeaderAdvancesWatermark_WhenAllMembersReport_ReclaimsTombstonesAndPreservesValue()
    {
        await using var cluster = new Cluster(A, B, C);
        Rga<string> valueA = CreateDeletedConvergedValue();
        Rga<string> valueB = valueA.Clone();
        Rga<string> valueC = valueA.Clone();
        await using GarbageCollectionCoordinator coordinatorA = cluster.CreateCoordinator(A, valueA);
        await using GarbageCollectionCoordinator coordinatorB = cluster.CreateCoordinator(B, valueB);
        await using GarbageCollectionCoordinator coordinatorC = cluster.CreateCoordinator(C, valueC);
        int appliedA = 0;
        int appliedB = 0;
        int appliedC = 0;
        coordinatorA.WatermarkApplied += _ => appliedA++;
        coordinatorB.WatermarkApplied += _ => appliedB++;
        coordinatorC.WatermarkApplied += _ => appliedC++;

        await Cluster.StartAsync(coordinatorA, coordinatorB, coordinatorC);
        await cluster.DrainAsync();

        await Assert.That(appliedA).IsEqualTo(1);
        await Assert.That(appliedB).IsEqualTo(1);
        await Assert.That(appliedC).IsEqualTo(1);
        await Assert.That(valueA.ToJson(CrdtValues.String).Contains("\"b\"")).IsFalse();
        await Assert.That(valueB.ToJson(CrdtValues.String).Contains("\"b\"")).IsFalse();
        await Assert.That(valueC.ToJson(CrdtValues.String).Contains("\"b\"")).IsFalse();
        await Assert.That(string.Join("", valueA.ToArray())).IsEqualTo("a");
        await Assert.That(string.Join("", valueB.ToArray())).IsEqualTo("a");
        await Assert.That(string.Join("", valueC.ToArray())).IsEqualTo("a");

        valueA.Merge(valueB);
        valueB.Merge(valueC);
        valueC.Merge(valueA);

        await Assert.That(string.Join("", valueA.ToArray())).IsEqualTo("a");
        await Assert.That(string.Join("", valueB.ToArray())).IsEqualTo("a");
        await Assert.That(string.Join("", valueC.ToArray())).IsEqualTo("a");
    }

    [Test]
    public async Task LeaderPausesWatermark_WhenLiveMemberReportIsMissing()
    {
        await using var cluster = new Cluster(A, B, C);
        Rga<string> valueA = CreateDeletedConvergedValue();
        Rga<string> valueB = valueA.Clone();
        Rga<string> valueC = valueA.Clone();
        await using GarbageCollectionCoordinator coordinatorA = cluster.CreateCoordinator(A, valueA);
        await using GarbageCollectionCoordinator coordinatorB = cluster.CreateCoordinator(B, valueB);
        await using GarbageCollectionCoordinator coordinatorC = cluster.CreateCoordinator(C, valueC);
        int appliedA = 0;
        int appliedB = 0;
        int appliedC = 0;
        coordinatorA.WatermarkApplied += _ => appliedA++;
        coordinatorB.WatermarkApplied += _ => appliedB++;
        coordinatorC.WatermarkApplied += _ => appliedC++;

        await Cluster.StartAsync(coordinatorA, coordinatorB);
        await cluster.DrainAsync();

        await Assert.That(appliedA).IsEqualTo(0);
        await Assert.That(appliedB).IsEqualTo(0);
        await Assert.That(appliedC).IsEqualTo(0);
        await Assert.That(valueA.ToJson(CrdtValues.String).Contains("\"b\"")).IsTrue();
        await Assert.That(valueB.ToJson(CrdtValues.String).Contains("\"b\"")).IsTrue();
        await Assert.That(valueC.ToJson(CrdtValues.String).Contains("\"b\"")).IsTrue();
    }

    private static Rga<string> CreateDeletedConvergedValue()
    {
        var value = new Rga<string>();
        value.Append(A, "a");
        value.Append(B, "b");
        value.Append(C, "c");
        value.Delete(1);
        value.Delete(1);
        return value;
    }

    private sealed class Cluster : IAsyncDisposable
    {
        private readonly InMemoryNetwork _network = new();
        private readonly Dictionary<ReplicaId, ManualFailureDetector> _detectors = [];
        private readonly Dictionary<ReplicaId, DeterministicLeaderConsensus> _consensus = [];
        private readonly Dictionary<ReplicaId, InMemoryTransport> _transports = [];

        public Cluster(params ReplicaId[] members)
        {
            foreach (ReplicaId member in members)
            {
                var detector = new ManualFailureDetector(member, members);
                InMemoryTransport transport = _network.CreateTransport();
                var consensus = new DeterministicLeaderConsensus(new DeterministicLeaderConsensusOptions
                {
                    LocalReplicaId = member,
                    FailureDetector = detector,
                    DisposeFailureDetector = false,
                    Transport = new ConsensusTransportOptions
                    {
                        Transport = transport,
                        StartTransport = false,
                    },
                });

                _detectors.Add(member, detector);
                _consensus.Add(member, consensus);
                _transports.Add(member, transport);
            }
        }

        public GarbageCollectionCoordinator CreateCoordinator(ReplicaId replicaId, IGarbageCollectable value)
        {
            var coordinator = new GarbageCollectionCoordinator(new GarbageCollectionCoordinatorOptions
            {
                LocalReplicaId = replicaId,
                Consensus = _consensus[replicaId],
                FailureDetector = _detectors[replicaId],
                Transport = _transports[replicaId],
                ReportInterval = TimeSpan.FromMinutes(5),
                ReportStalenessTimeout = TimeSpan.FromMinutes(1),
            });
            coordinator.Register(value);
            return coordinator;
        }

        public static async Task StartAsync(params GarbageCollectionCoordinator[] coordinators)
        {
            foreach (GarbageCollectionCoordinator coordinator in coordinators)
            {
                await coordinator.StartAsync();
            }
        }

        public ValueTask DrainAsync()
        {
            return _network.DrainAsync();
        }

        public async ValueTask DisposeAsync()
        {
            foreach (DeterministicLeaderConsensus consensus in _consensus.Values)
            {
                await consensus.DisposeAsync();
            }

            foreach (ManualFailureDetector detector in _detectors.Values)
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
