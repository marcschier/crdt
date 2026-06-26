// Copyright (c) marcschier. Licensed under the MIT License.

namespace Crdt.Tests.Core;

public sealed class TimestampTests
{
    private static readonly ReplicaId A = ReplicaId.FromUInt64(1);
    private static readonly ReplicaId B = ReplicaId.FromUInt64(2);

    [Test]
    public async Task CompareTo_Orders_By_Wall_Then_Counter_Then_Origin()
    {
        var earlyWall = new Timestamp(10, 5, B);
        var lateWall = new Timestamp(20, 0, A);
        await Assert.That(earlyWall < lateWall).IsTrue();

        var lowCounter = new Timestamp(10, 1, B);
        var highCounter = new Timestamp(10, 2, A);
        await Assert.That(lowCounter < highCounter).IsTrue();

        var originA = new Timestamp(10, 1, A);
        var originB = new Timestamp(10, 1, B);
        await Assert.That(originA < originB).IsTrue();
    }

    [Test]
    public async Task Equality_Considers_All_Components()
    {
        var a = new Timestamp(10, 1, A);
        var b = new Timestamp(10, 1, A);
        await Assert.That(a).IsEqualTo(b);
        await Assert.That(a == b).IsTrue();
        await Assert.That(a != new Timestamp(10, 2, A)).IsTrue();
    }

    [Test]
    public async Task MinValue_Is_Smallest()
    {
        await Assert.That(Timestamp.MinValue < new Timestamp(1, 0, A)).IsTrue();
    }
}
