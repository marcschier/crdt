// Copyright (c) marcschier. Licensed under the MIT License.

namespace Crdt.Tests.Causality;

public sealed class StableCutTests
{
    private static readonly ReplicaId A = ReplicaId.FromUInt64(1);
    private static readonly ReplicaId B = ReplicaId.FromUInt64(2);

    [Test]
    public async Task Meet_Takes_Pointwise_Minimum()
    {
        var first = new VersionVector();
        first.Observe(new Dot(A, 5));
        first.Observe(new Dot(B, 2));
        var second = new VersionVector();
        second.Observe(new Dot(A, 3));
        second.Observe(new Dot(B, 7));

        StableCut cut = StableCut.Meet([first, second]);

        await Assert.That(cut.Floor(A)).IsEqualTo(3UL);
        await Assert.That(cut.Floor(B)).IsEqualTo(2UL);
        await Assert.That(cut.IsStable(new Dot(A, 3))).IsTrue();
        await Assert.That(cut.IsStable(new Dot(A, 4))).IsFalse();
    }

    [Test]
    public async Task Meet_Treats_Missing_Replica_As_Zero()
    {
        var first = new VersionVector();
        first.Observe(new Dot(A, 2));
        var second = new VersionVector();
        second.Observe(new Dot(B, 2));

        StableCut cut = StableCut.Meet([first, second]);

        await Assert.That(cut.Floor(A)).IsEqualTo(0UL);
        await Assert.That(cut.Floor(B)).IsEqualTo(0UL);
        await Assert.That(cut.IsStable(new Dot(A, 1))).IsFalse();
    }

    [Test]
    public async Task Binary_Roundtrips()
    {
        var vector = new VersionVector();
        vector.Observe(new Dot(A, 5));
        StableCut cut = StableCut.Meet([vector]);

        StableCut restored = StableCut.ReadFrom(cut.ToByteArray());

        await Assert.That(restored).IsEqualTo(cut);
        await Assert.That(restored.IsStable(new Dot(A, 5))).IsTrue();
    }
}
