// Copyright (c) marcschier. Licensed under the MIT License.

namespace Crdt.Tests.Serialization;

public sealed class CausalSerializationTests
{
    private static readonly ReplicaId A = ReplicaId.FromUInt64(1);
    private static readonly ReplicaId B = ReplicaId.FromUInt64(2);
    private static readonly ReplicaId C = ReplicaId.FromUInt64(3);

    [Test]
    public async Task VersionVector_Roundtrips()
    {
        var vv = new VersionVector();
        vv.Observe(new Dot(A, 3));
        vv.Observe(new Dot(B, 7));
        vv.Observe(new Dot(C, 1));

        byte[] bytes = vv.ToByteArray();
        VersionVector restored = VersionVector.ReadFrom(bytes);

        await Assert.That(restored).IsEqualTo(vv);
    }

    [Test]
    public async Task VersionVector_Serialization_Is_Canonical()
    {
        var left = new VersionVector();
        left.Observe(new Dot(A, 1));
        left.Observe(new Dot(B, 2));
        left.Observe(new Dot(C, 3));

        var right = new VersionVector();
        right.Observe(new Dot(C, 3));
        right.Observe(new Dot(A, 1));
        right.Observe(new Dot(B, 2));

        // Insertion order differs but canonical (replica-sorted) output must be identical.
        await Assert.That(Convert.ToBase64String(left.ToByteArray()))
            .IsEqualTo(Convert.ToBase64String(right.ToByteArray()));
    }

    [Test]
    public async Task Empty_VersionVector_Roundtrips()
    {
        var vv = new VersionVector();
        VersionVector restored = VersionVector.ReadFrom(vv.ToByteArray());
        await Assert.That(restored.IsEmpty).IsTrue();
    }
}
