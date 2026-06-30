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
