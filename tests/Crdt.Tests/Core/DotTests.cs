// Copyright (c) marcschier. Licensed under the MIT License.

namespace Crdt.Tests.Core;

public sealed class DotTests
{
    [Test]
    public async Task Equality_Considers_Replica_And_Sequence()
    {
        ReplicaId r = ReplicaId.FromUInt64(7);
        var a = new Dot(r, 3);
        var b = new Dot(r, 3);
        var c = new Dot(r, 4);

        await Assert.That(a).IsEqualTo(b);
        await Assert.That(a).IsNotEqualTo(c);
        await Assert.That(a == b).IsTrue();
        await Assert.That(a != c).IsTrue();
    }

    [Test]
    public async Task CompareTo_Orders_By_Replica_Then_Sequence()
    {
        ReplicaId r1 = ReplicaId.FromUInt64(1);
        ReplicaId r2 = ReplicaId.FromUInt64(2);

        await Assert.That(new Dot(r1, 5) < new Dot(r2, 1)).IsTrue();
        await Assert.That(new Dot(r1, 1) < new Dot(r1, 2)).IsTrue();
        await Assert.That(new Dot(r2, 1) > new Dot(r1, 9)).IsTrue();
        await Assert.That(new Dot(r1, 1) <= new Dot(r1, 1)).IsTrue();
        await Assert.That(new Dot(r1, 1) >= new Dot(r1, 1)).IsTrue();
    }

    [Test]
    public async Task ToString_Has_Replica_And_Sequence()
    {
        var dot = new Dot(ReplicaId.FromUInt64(1), 9);
        await Assert.That(dot.ToString()).Contains(":9");
    }

    [Test]
    public async Task GetHashCode_Is_Stable_For_Equal_Dots()
    {
        ReplicaId r = ReplicaId.FromUInt64(7);
        await Assert.That(new Dot(r, 3).GetHashCode()).IsEqualTo(new Dot(r, 3).GetHashCode());
    }
}
