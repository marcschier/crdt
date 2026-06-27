// Copyright (c) marcschier. Licensed under the MIT License.

namespace Crdt.Tests.Counters;

public sealed class ResettableCounterCoverageTests
{
    private static readonly ReplicaId A = ReplicaId.FromUInt64(1);
    private static readonly ReplicaId B = ReplicaId.FromUInt64(2);
    private static readonly ReplicaId C = ReplicaId.FromUInt64(3);

    [Test]
    public async Task Operation_Binary_Roundtrips_Each_Kind()
    {
        ResettableCounterOperation increment = new ResettableCounter().Increment(A, 7);
        ResettableCounterOperation decrement = new ResettableCounter().Decrement(B, 3);
        ResettableCounterOperation reset = MakeObservedReset();

        await AssertOperationBinaryRoundtrip(increment);
        await AssertOperationBinaryRoundtrip(decrement);
        await AssertOperationBinaryRoundtrip(reset);
    }

    [Test]
    public async Task Operation_Json_Roundtrips_Each_Kind()
    {
        ResettableCounterOperation increment = new ResettableCounter().Increment(A, 7);
        ResettableCounterOperation decrement = new ResettableCounter().Decrement(B, 3);
        ResettableCounterOperation reset = MakeObservedReset();

        await AssertOperationJsonRoundtrip(increment);
        await AssertOperationJsonRoundtrip(decrement);
        await AssertOperationJsonRoundtrip(reset);
    }

    [Test]
    public async Task Operation_Equality_And_Invalid_Binary_Are_Covered()
    {
        ResettableCounterOperation left = new ResettableCounter().Increment(A, 4);
        ResettableCounterOperation same = ResettableCounterOperation.ReadFrom(left.ToByteArray());
        ResettableCounterOperation different = new ResettableCounter().Increment(B, 4);

        await Assert.That(left == same).IsTrue();
        await Assert.That(left != different).IsTrue();
        await Assert.That(left.Equals((object)same)).IsTrue();
        await Assert.That(left.Equals((object?)null)).IsFalse();
        await Assert.That(left.Equals("not an operation")).IsFalse();
        await Assert.That(left.GetHashCode()).IsEqualTo(same.GetHashCode());
        await Assert.That(left.GetHashCode()).IsNotEqualTo(different.GetHashCode());
        await Assert.That(() => ResettableCounterOperation.ReadFrom([(byte)99])).Throws<FormatException>();
    }

    [Test]
    public async Task State_Binary_And_Json_Roundtrip_Nontrivial_History()
    {
        ResettableCounter counter = MakeCounterWithResetHistory();

        ResettableCounter binary = ResettableCounter.ReadFrom(counter.ToByteArray());
        ResettableCounter json = ResettableCounter.FromJson(counter.ToJson());

        await Assert.That(binary).IsEqualTo(counter);
        await Assert.That(binary.Value).IsEqualTo(counter.Value);
        await Assert.That(json).IsEqualTo(counter);
        await Assert.That(json.Value).IsEqualTo(counter.Value);
    }

    [Test]
    public async Task Observed_Reset_Removes_Exactly_Observed_Contributions()
    {
        var left = new ResettableCounter();
        ResettableCounterOperation observed = left.Increment(A, 10);

        var right = new ResettableCounter();
        right.Apply(observed);
        ResettableCounterOperation reset = left.Reset();
        ResettableCounterOperation concurrent = right.Increment(B, 4);

        await Assert.That(right.Apply(reset)).IsTrue();
        await Assert.That(right.Value).IsEqualTo(4L);

        left.Apply(concurrent);
        left.Merge(right);
        right.Merge(left);

        await Assert.That(left.Value).IsEqualTo(4L);
        await Assert.That(right.Value).IsEqualTo(4L);
        await Assert.That(left).IsEqualTo(right);
    }

    [Test]
    public async Task Compare_Returns_All_Crdt_Orders()
    {
        var baseline = new ResettableCounter();
        baseline.Increment(A, 1);

        ResettableCounter equal = baseline.Clone();
        ResettableCounter greater = baseline.Clone();
        greater.Increment(A, 2);

        var concurrent = new ResettableCounter();
        concurrent.Increment(B, 1);

        await Assert.That(baseline.Compare(equal)).IsEqualTo(CrdtOrder.Equal);
        await Assert.That(baseline.Compare(greater)).IsEqualTo(CrdtOrder.Less);
        await Assert.That(greater.Compare(baseline)).IsEqualTo(CrdtOrder.Greater);
        await Assert.That(baseline.Compare(concurrent)).IsEqualTo(CrdtOrder.Concurrent);
    }

    [Test]
    public async Task Clone_Is_Independent_And_Merge_Is_Idempotent()
    {
        ResettableCounter original = MakeCounterWithResetHistory();
        ResettableCounter clone = original.Clone();

        clone.Increment(C, 11);

        await Assert.That(original.Value).IsEqualTo(1L);
        await Assert.That(clone.Value).IsEqualTo(12L);
        await Assert.That(original).IsNotEqualTo(clone);

        ResettableCounter merged = original.Clone();
        merged.Merge(clone);
        ResettableCounter once = merged.Clone();
        merged.Merge(clone);

        await Assert.That(merged).IsEqualTo(once);
    }

    [Test]
    public async Task Merge_Is_Commutative_And_Associative_For_Three_Replicas()
    {
        ResettableCounter x = OneIncrement(A, 1);
        ResettableCounter y = OneIncrement(B, 2);
        ResettableCounter z = OneIncrement(C, 3);

        ResettableCounter xy = x.Clone();
        xy.Merge(y);
        ResettableCounter yx = y.Clone();
        yx.Merge(x);

        ResettableCounter leftAssociated = x.Clone();
        ResettableCounter yz = y.Clone();
        yz.Merge(z);
        leftAssociated.Merge(yz);

        ResettableCounter rightAssociated = x.Clone();
        rightAssociated.Merge(y);
        rightAssociated.Merge(z);

        await Assert.That(xy).IsEqualTo(yx);
        await Assert.That(leftAssociated).IsEqualTo(rightAssociated);
        await Assert.That(rightAssociated.Value).IsEqualTo(6L);
    }

    [Test]
    public async Task Apply_Is_Idempotent_And_Empty_Reset_Is_Noop()
    {
        var source = new ResettableCounter();
        ResettableCounterOperation emptyReset = source.Reset();
        ResettableCounterOperation increment = source.Increment(A, 5);
        ResettableCounterOperation reset = source.Reset();

        var target = new ResettableCounter();

        await Assert.That(target.Apply(emptyReset)).IsFalse();
        await Assert.That(target.Apply(increment)).IsTrue();
        await Assert.That(target.Apply(increment)).IsFalse();
        await Assert.That(target.Apply(reset)).IsTrue();
        await Assert.That(target.Apply(reset)).IsFalse();
        await Assert.That(target.Value).IsEqualTo(0L);
    }

    [Test]
    public async Task Equals_GetHashCode_Null_And_Different_Type_Are_Covered()
    {
        ResettableCounter left = MakeCounterWithResetHistory();
        ResettableCounter same = ResettableCounter.FromJson(left.ToJson());
        ResettableCounter different = left.Clone();
        different.Increment(C, 2);
        object sameObject = same;
        object? nullObject = null;
        object differentType = "not a counter";
        bool equalsSameObject = left.Equals(sameObject);
        bool equalsNullObject = left.Equals(nullObject);
        bool equalsDifferentType = left.Equals(differentType);
        int leftHash = left.GetHashCode();
        int sameHash = same.GetHashCode();
        int differentHash = different.GetHashCode();

        await Assert.That(left.Equals(same)).IsTrue();
        await Assert.That(left.Equals(different)).IsFalse();
        await Assert.That(left.Equals((ResettableCounter?)null)).IsFalse();
        await Assert.That(equalsSameObject).IsTrue();
        await Assert.That(equalsNullObject).IsFalse();
        await Assert.That(equalsDifferentType).IsFalse();
        await Assert.That(leftHash).IsEqualTo(sameHash);
        await Assert.That(leftHash).IsNotEqualTo(differentHash);
    }

    [Test]
    public async Task Argument_Guards_Throw()
    {
        var counter = new ResettableCounter();

        await Assert.That(() => counter.Increment(A, 0)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => counter.Increment(A, -1)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => counter.Decrement(A, 0)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => counter.Decrement(A, -1)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => counter.Merge(null!)).Throws<ArgumentNullException>();
        await Assert.That(() => counter.Compare(null!)).Throws<ArgumentNullException>();
        await Assert.That(() => ResettableCounter.FromJson(null!)).Throws<ArgumentNullException>();
    }

    private static ResettableCounterOperation MakeObservedReset()
    {
        var counter = new ResettableCounter();
        counter.Increment(A, 2);
        counter.Decrement(B, 1);
        return counter.Reset();
    }

    private static ResettableCounter MakeCounterWithResetHistory()
    {
        var counter = new ResettableCounter();
        counter.Increment(A, 9);
        counter.Decrement(B, 4);
        counter.Reset();
        counter.Increment(A, 3);
        counter.Decrement(C, 2);
        return counter;
    }

    private static ResettableCounter OneIncrement(ReplicaId replica, long amount)
    {
        var counter = new ResettableCounter();
        counter.Increment(replica, amount);
        return counter;
    }

    private static async Task AssertOperationBinaryRoundtrip(ResettableCounterOperation operation)
    {
        ResettableCounterOperation restored = ResettableCounterOperation.ReadFrom(operation.ToByteArray());

        await Assert.That(restored).IsEqualTo(operation);
        await Assert.That(restored.Kind).IsEqualTo(operation.Kind);
        await Assert.That(restored.GetHashCode()).IsEqualTo(operation.GetHashCode());
    }

    private static async Task AssertOperationJsonRoundtrip(ResettableCounterOperation operation)
    {
        string json = OperationToJson(operation);
        ResettableCounterOperation restored = OperationFromJson(json);

        await Assert.That(restored).IsEqualTo(operation);
        await Assert.That(restored.Kind).IsEqualTo(operation.Kind);
    }

    private static string OperationToJson(ResettableCounterOperation operation)
    {
        string encoded = Convert.ToBase64String(operation.ToByteArray());
        return "{\"binary\":\"" + encoded + "\"}";
    }

    private static ResettableCounterOperation OperationFromJson(string json)
    {
        const string prefix = "{\"binary\":\"";
        const string suffix = "\"}";
        string encoded = json.Substring(prefix.Length, json.Length - prefix.Length - suffix.Length);
        byte[] bytes = Convert.FromBase64String(encoded);
        return ResettableCounterOperation.ReadFrom(bytes);
    }
}
