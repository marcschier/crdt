// Copyright (c) marcschier. Licensed under the MIT License.

namespace Crdt.Consensus.Tests;

public sealed class HeartbeatFailureDetectorTests
{
    [Test]
    public async Task AddRemoveMember_UpdatesMembershipViewAndRaisesEvents()
    {
        ReplicaId local = ReplicaId.FromUInt64(10);
        ReplicaId peer = ReplicaId.FromUInt64(11);
        ReplicaId observed = ReplicaId.FromUInt64(12);
        await using var detector = new HeartbeatFailureDetector(new HeartbeatFailureDetectorOptions
        {
            LocalReplicaId = local,
            Transport = NoopTransport(),
        });
        int changes = 0;
        detector.MembersChanged += () => changes++;

        detector.AddMember(peer);
        detector.ObserveHeartbeat(observed);
        bool removed = detector.RemoveMember(peer);

        await Assert.That(removed).IsTrue();
        await Assert.That(detector.Members.Order().ToArray()).IsEquivalentTo([local, observed]);
        await Assert.That(changes).IsEqualTo(3);
    }

    [Test]
    public async Task Constructor_Rejects_Invalid_Options()
    {
        ReplicaId local = ReplicaId.FromUInt64(10);

        await Assert.That(() => new HeartbeatFailureDetector(null!)).Throws<ArgumentNullException>();
        await Assert.That(() => new HeartbeatFailureDetector(new HeartbeatFailureDetectorOptions
        {
            LocalReplicaId = local,
        })).Throws<ArgumentException>();
        await Assert.That(() => new HeartbeatFailureDetector(new HeartbeatFailureDetectorOptions
        {
            LocalReplicaId = local,
            Transport = NoopTransport(),
            HeartbeatInterval = TimeSpan.Zero,
        })).Throws<ArgumentException>();
        await Assert.That(() => new HeartbeatFailureDetector(new HeartbeatFailureDetectorOptions
        {
            LocalReplicaId = local,
            Transport = NoopTransport(),
            Timeout = TimeSpan.Zero,
        })).Throws<ArgumentException>();
        await Assert.That(() => new HeartbeatFailureDetector(new HeartbeatFailureDetectorOptions
        {
            LocalReplicaId = local,
            Transport = NoopTransport(),
            MaxFrameLength = 0,
        })).Throws<ArgumentException>();
    }

    [Test]
    public async Task RemoveMember_DoesNotRemove_Local_Or_Unknown()
    {
        ReplicaId local = ReplicaId.FromUInt64(10);
        ReplicaId unknown = ReplicaId.FromUInt64(99);
        await using var detector = new HeartbeatFailureDetector(new HeartbeatFailureDetectorOptions
        {
            LocalReplicaId = local,
            Transport = NoopTransport(),
        });
        int changes = 0;
        detector.MembersChanged += () => changes++;

        bool removedLocal = detector.RemoveMember(local);
        bool removedUnknown = detector.RemoveMember(unknown);

        await Assert.That(removedLocal).IsFalse();
        await Assert.That(removedUnknown).IsFalse();
        await Assert.That(changes).IsEqualTo(0);
        await Assert.That(detector.Members.ToArray()).IsEquivalentTo([local]);
    }

    [Test]
    public async Task Constructor_Seeds_InitialMembers()
    {
        ReplicaId local = ReplicaId.FromUInt64(10);
        ReplicaId seedOne = ReplicaId.FromUInt64(11);
        ReplicaId seedTwo = ReplicaId.FromUInt64(12);
        var options = new HeartbeatFailureDetectorOptions
        {
            LocalReplicaId = local,
            Transport = NoopTransport(),
        };
        options.InitialMembers.Add(seedOne);
        options.InitialMembers.Add(seedTwo);
        await using var detector = new HeartbeatFailureDetector(options);

        await Assert.That(detector.Members.Order().ToArray()).IsEquivalentTo([local, seedOne, seedTwo]);
    }

    private static ConsensusTransportOptions NoopTransport() =>
        new()
        {
            SendAsync = static (_, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                return default;
            },
            RegisterReceiver = static _ => { },
            UnregisterReceiver = static _ => { },
        };
}
