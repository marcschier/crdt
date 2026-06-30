// Copyright (c) marcschier. Licensed under the MIT License.

namespace Crdt.Tests.Causality;

public sealed class StableCutCoverageTests
{
    private static readonly ReplicaId A = ReplicaId.FromUInt64(1);
    private static readonly ReplicaId B = ReplicaId.FromUInt64(2);

    [Test]
    public async Task Meet_Of_No_Vectors_Is_Empty()
    {
        StableCut cut = StableCut.Meet([]);

        await Assert.That(cut.IsEmpty).IsTrue();
        await Assert.That(cut.Count).IsEqualTo(0);
        await Assert.That(cut.Replicas.Count).IsEqualTo(0);
        await Assert.That(cut.Floor(A)).IsEqualTo(0UL);
        await Assert.That(cut.IsStable(new Dot(A, 1))).IsFalse();
    }

    [Test]
    public async Task Replicas_And_Count_Reflect_NonZero_Floors()
    {
        StableCut cut = StableCut.Meet([Vector((A, 5), (B, 2))]);

        await Assert.That(cut.Count).IsEqualTo(2);
        await Assert.That(cut.IsEmpty).IsFalse();

        ReplicaId[] replicas = cut.Replicas.Order().ToArray();
        await Assert.That(replicas.Length).IsEqualTo(2);
        await Assert.That(replicas[0]).IsEqualTo(A);
        await Assert.That(replicas[1]).IsEqualTo(B);
    }

    [Test]
    public async Task Equality_And_HashCode_Are_Content_Based()
    {
        StableCut left = StableCut.Meet([Vector((A, 5), (B, 2))]);
        StableCut sameDifferentOrder = StableCut.Meet([Vector((B, 2), (A, 5))]);
        StableCut differentFloor = StableCut.Meet([Vector((A, 3), (B, 2))]);
        StableCut differentCount = StableCut.Meet([Vector((A, 5))]);

        await Assert.That(left.Equals(sameDifferentOrder)).IsTrue();
        await Assert.That(left.GetHashCode()).IsEqualTo(sameDifferentOrder.GetHashCode());
        await Assert.That(left.Equals(differentFloor)).IsFalse();
        await Assert.That(left.Equals(differentCount)).IsFalse();
    }

    [Test]
    public async Task Equals_Object_Overload_Handles_Null_And_Boxed_Cut()
    {
        object boxedCut = StableCut.Meet([Vector((A, 5))]);

        bool equalsNull = StableCut.Meet([Vector((A, 5))]).Equals((object?)null);
        bool equalsBoxedCut = StableCut.Meet([Vector((A, 5))]).Equals(boxedCut);

        await Assert.That(equalsNull).IsFalse();
        await Assert.That(equalsBoxedCut).IsTrue();
    }

    [Test]
    public async Task Empty_Cut_Binary_Roundtrips()
    {
        StableCut empty = StableCut.Meet([]);

        StableCut restored = StableCut.ReadFrom(empty.ToByteArray());

        await Assert.That(restored.IsEmpty).IsTrue();
        await Assert.That(restored).IsEqualTo(empty);
    }

    private static VersionVector Vector(params (ReplicaId Replica, ulong Sequence)[] entries)
    {
        var vector = new VersionVector();
        foreach ((ReplicaId replica, ulong sequence) in entries)
        {
            vector.Observe(new Dot(replica, sequence));
        }

        return vector;
    }
}
