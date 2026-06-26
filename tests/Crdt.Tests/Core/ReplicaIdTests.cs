// Copyright (c) marcschier. Licensed under the MIT License.

namespace Crdt.Tests.Core;

public sealed class ReplicaIdTests
{
    [Test]
    public async Task New_Produces_Distinct_Ids()
    {
        ReplicaId a = ReplicaId.New();
        ReplicaId b = ReplicaId.New();
        await Assert.That(a).IsNotEqualTo(b);
    }

    [Test]
    public async Task FromUInt64_Is_Deterministic()
    {
        ReplicaId a = ReplicaId.FromUInt64(42);
        ReplicaId b = ReplicaId.FromUInt64(42);
        await Assert.That(a).IsEqualTo(b);
        await Assert.That(a.GetHashCode()).IsEqualTo(b.GetHashCode());
    }

    [Test]
    public async Task FromUInt64_Distinct_Seeds_Differ()
    {
        await Assert.That(ReplicaId.FromUInt64(1)).IsNotEqualTo(ReplicaId.FromUInt64(2));
    }

    [Test]
    public async Task Parse_Roundtrips_ToString()
    {
        ReplicaId original = ReplicaId.New();
        ReplicaId parsed = ReplicaId.Parse(original.ToString());
        await Assert.That(parsed).IsEqualTo(original);
    }

    [Test]
    public async Task TryParse_Rejects_Garbage()
    {
        bool ok = ReplicaId.TryParse("not-a-guid", out ReplicaId result);
        await Assert.That(ok).IsFalse();
        await Assert.That(result).IsEqualTo(ReplicaId.Empty);
    }

    [Test]
    public async Task Comparison_Operators_Are_Consistent()
    {
        ReplicaId a = ReplicaId.FromUInt64(1);
        ReplicaId b = ReplicaId.FromUInt64(2);
        ReplicaId aAgain = ReplicaId.FromUInt64(1);

        // FromUInt64 writes the seed big-endian into the low 8 bytes, so order tracks seed.
        await Assert.That(a < b).IsTrue();
        await Assert.That(b > a).IsTrue();
        await Assert.That(a <= aAgain).IsTrue();
        await Assert.That(a >= aAgain).IsTrue();
        await Assert.That(a == aAgain).IsTrue();
        await Assert.That(a != b).IsTrue();
    }

    [Test]
    public async Task Empty_Is_Default()
    {
        await Assert.That(ReplicaId.Empty).IsEqualTo(default(ReplicaId));
    }
}
