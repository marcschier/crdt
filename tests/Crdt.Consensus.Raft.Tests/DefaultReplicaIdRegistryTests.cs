// Copyright (c) marcschier. Licensed under the MIT License.

namespace Crdt.Consensus.Raft.Tests;

public sealed class DefaultReplicaIdRegistryTests
{
    private static readonly ReplicaId A = ReplicaId.FromUInt64(1);
    private static readonly ReplicaId B = ReplicaId.FromUInt64(2);

    [Test]
    public async Task GetNodeId_Is_Stable_And_Resolves_Back()
    {
        var registry = new DefaultReplicaIdRegistry();

        ulong first = registry.GetNodeId(A);
        ulong second = registry.GetNodeId(A);

        await Assert.That(second).IsEqualTo(first);
        await Assert.That(first).IsNotEqualTo(0UL);
        await Assert.That(registry.TryGetReplicaId(first, out ReplicaId resolved)).IsTrue();
        await Assert.That(resolved).IsEqualTo(A);
    }

    [Test]
    public async Task Distinct_Replicas_Map_To_Distinct_NodeIds()
    {
        var registry = new DefaultReplicaIdRegistry();

        await Assert.That(registry.GetNodeId(A)).IsNotEqualTo(registry.GetNodeId(B));
    }

    [Test]
    public async Task TryGetReplicaId_Unknown_NodeId_ReturnsFalse()
    {
        var registry = new DefaultReplicaIdRegistry();

        await Assert.That(registry.TryGetReplicaId(123456789UL, out _)).IsFalse();
    }

    [Test]
    public async Task Register_Explicit_NodeId_Is_Used_By_GetNodeId()
    {
        var registry = new DefaultReplicaIdRegistry();

        registry.Register(A, 42);

        await Assert.That(registry.GetNodeId(A)).IsEqualTo(42UL);
        await Assert.That(registry.TryGetReplicaId(42, out ReplicaId resolved)).IsTrue();
        await Assert.That(resolved).IsEqualTo(A);
    }

    [Test]
    public async Task Register_Overrides_Previous_Hash_Mapping()
    {
        var registry = new DefaultReplicaIdRegistry();
        ulong hashed = registry.GetNodeId(A);

        registry.Register(A, 7);

        await Assert.That(registry.GetNodeId(A)).IsEqualTo(7UL);
        await Assert.That(registry.TryGetReplicaId(7, out _)).IsTrue();
        await Assert.That(registry.TryGetReplicaId(hashed, out _)).IsFalse();
    }

    [Test]
    public async Task Register_Rejects_Zero_NodeId()
    {
        var registry = new DefaultReplicaIdRegistry();

        await Assert.That(() => registry.Register(A, 0)).Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task Register_Rejects_NodeId_Already_Mapped_To_Other_Replica()
    {
        var registry = new DefaultReplicaIdRegistry();
        registry.Register(A, 5);

        await Assert.That(() => registry.Register(B, 5)).Throws<InvalidOperationException>();
    }
}
