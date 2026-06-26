// Copyright (c) marcschier. Licensed under the MIT License.

namespace Crdt.Tests.Core;

public sealed class VersionVectorTests
{
    private static readonly ReplicaId A = ReplicaId.FromUInt64(1);
    private static readonly ReplicaId B = ReplicaId.FromUInt64(2);

    [Test]
    public async Task Increment_Produces_OneBased_Monotonic_Dots()
    {
        var vv = new VersionVector();
        Dot first = vv.Increment(A);
        Dot second = vv.Increment(A);

        await Assert.That(first.Sequence).IsEqualTo(1UL);
        await Assert.That(second.Sequence).IsEqualTo(2UL);
        await Assert.That(vv[A]).IsEqualTo(2UL);
    }

    [Test]
    public async Task Observe_Sets_Max_And_Reports_Advancement()
    {
        var vv = new VersionVector();
        await Assert.That(vv.Observe(new Dot(A, 5))).IsTrue();
        await Assert.That(vv.Observe(new Dot(A, 3))).IsFalse();
        await Assert.That(vv[A]).IsEqualTo(5UL);
    }

    [Test]
    public async Task Contains_Reflects_Counter()
    {
        var vv = new VersionVector();
        vv.Observe(new Dot(A, 4));
        await Assert.That(vv.Contains(new Dot(A, 4))).IsTrue();
        await Assert.That(vv.Contains(new Dot(A, 5))).IsFalse();
        await Assert.That(vv.Contains(new Dot(B, 1))).IsFalse();
    }

    [Test]
    public async Task Merge_Takes_Per_Replica_Maximum()
    {
        var left = new VersionVector();
        left.Observe(new Dot(A, 3));
        left.Observe(new Dot(B, 1));

        var right = new VersionVector();
        right.Observe(new Dot(A, 1));
        right.Observe(new Dot(B, 5));

        left.Merge(right);

        await Assert.That(left[A]).IsEqualTo(3UL);
        await Assert.That(left[B]).IsEqualTo(5UL);
    }

    [Test]
    public async Task Compare_Classifies_Partial_Order()
    {
        var baseline = new VersionVector();
        baseline.Observe(new Dot(A, 1));

        var equal = new VersionVector();
        equal.Observe(new Dot(A, 1));

        var greater = new VersionVector();
        greater.Observe(new Dot(A, 2));

        var concurrent = new VersionVector();
        concurrent.Observe(new Dot(B, 1));

        await Assert.That(baseline.Compare(equal)).IsEqualTo(CrdtOrder.Equal);
        await Assert.That(baseline.Compare(greater)).IsEqualTo(CrdtOrder.Less);
        await Assert.That(greater.Compare(baseline)).IsEqualTo(CrdtOrder.Greater);
        await Assert.That(baseline.Compare(concurrent)).IsEqualTo(CrdtOrder.Concurrent);
    }

    [Test]
    public async Task Clone_Is_Independent()
    {
        var vv = new VersionVector();
        vv.Observe(new Dot(A, 1));
        VersionVector clone = vv.Clone();
        clone.Increment(A);

        await Assert.That(vv[A]).IsEqualTo(1UL);
        await Assert.That(clone[A]).IsEqualTo(2UL);
    }

    [Test]
    public async Task Equality_And_Hash_Are_Content_Based()
    {
        var left = new VersionVector();
        left.Observe(new Dot(A, 2));
        left.Observe(new Dot(B, 1));

        var right = new VersionVector();
        right.Observe(new Dot(B, 1));
        right.Observe(new Dot(A, 2));

        await Assert.That(left).IsEqualTo(right);
        await Assert.That(left.GetHashCode()).IsEqualTo(right.GetHashCode());
    }

    [Test]
    public async Task Empty_Vector_Reports_Empty()
    {
        var vv = new VersionVector();
        await Assert.That(vv.IsEmpty).IsTrue();
        vv.Increment(A);
        await Assert.That(vv.IsEmpty).IsFalse();
        await Assert.That(vv.Count).IsEqualTo(1);
    }
}
