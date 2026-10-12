// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers;
using System.Collections.Concurrent;
using System.Text.Json;

namespace Crdt.Tests.Counters;

[Timeout(120_000)]
public sealed class PNCounterConcurrencyTests
{
    private static readonly ReplicaId A = ReplicaId.FromUInt64(1);
    private static readonly ReplicaId B = ReplicaId.FromUInt64(2);
    private static readonly ReplicaId C = ReplicaId.FromUInt64(3);
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(10);
    private const string SingleHex = "010000000000000000000000000000000107010000000000000000000000000000000103";
    private const string SingleJson =
        "{\"p\":[{\"replica\":\"00000000-0000-0000-0000-000000000001\",\"value\":7}]," +
        "\"n\":[{\"replica\":\"00000000-0000-0000-0000-000000000001\",\"value\":3}]}";
    private const string PairHex =
        "020000000000000000000000000000000107000000000000000000000000000000028001" +
        "0200000000000000000000000000000001030000000000000000000000000000000201";
    private const string PairJson =
        "{\"p\":[{\"replica\":\"00000000-0000-0000-0000-000000000001\",\"value\":7}," +
        "{\"replica\":\"00000000-0000-0000-0000-000000000002\",\"value\":128}]," +
        "\"n\":[{\"replica\":\"00000000-0000-0000-0000-000000000001\",\"value\":3}," +
        "{\"replica\":\"00000000-0000-0000-0000-000000000002\",\"value\":1}]}";

    [Test]
    public async Task Concurrent_LocalIncrementDecrement_SameReplica_NoLostUpdates(CancellationToken cancellationToken)
    {
        var counter = new PNCounter();
        var positive = new PNCounterOperation[2, 64];
        var negative = new PNCounterOperation[2, 64];
        RunRounds(64, cancellationToken,
            round => positive[0, round] = counter.Increment(A, 3),
            round => positive[1, round] = counter.Increment(A, 3),
            round => negative[0, round] = counter.Decrement(A, 2),
            round => negative[1, round] = counter.Decrement(A, 2));
        await AssertState(counter, [(A, 384)], [(A, 256)], 128);
        await Assert.That(positive.Cast<PNCounterOperation>().All(operation =>
            operation.Replica == A && operation.Negative <= 256 && operation.Negative % 2 == 0)).IsTrue();
        await Assert.That(negative.Cast<PNCounterOperation>().All(operation =>
            operation.Replica == A && operation.Positive <= 384 && operation.Positive % 3 == 0)).IsTrue();
        await Assert.That(positive.Cast<PNCounterOperation>().Select(operation => operation.Positive).Order()
            .SequenceEqual(Enumerable.Range(1, 128).Select(i => (ulong)i * 3))).IsTrue();
        await Assert.That(negative.Cast<PNCounterOperation>().Select(operation => operation.Negative).Order()
            .SequenceEqual(Enumerable.Range(1, 128).Select(i => (ulong)i * 2))).IsTrue();
        // Every returned pair describes exactly one of the 256 serialized local mutation steps.
        // Returning zero/stale values for the OTHER child would duplicate or omit these exact ordinals.
        await Assert.That(positive.Cast<PNCounterOperation>().Concat(negative.Cast<PNCounterOperation>())
            .Select(operation => operation.Positive / 3 + operation.Negative / 2).Order()
            .SequenceEqual(Enumerable.Range(1, 256).Select(i => (ulong)i))).IsTrue();
        // Increment followed by Decrement is NOT a pair transaction; only final components are fixed here.
    }

    [Test]
    public async Task Concurrent_BalancedApply_ActualReadsAndSnapshots_AreExactlyZero(
        CancellationToken cancellationToken)
    {
        var counter = new PNCounter();
        var retained = new List<Snapshot>();
        CaptureBalanced(counter, retained, ValidateBalancedApply);
        RunRounds(64, cancellationToken,
            round => Require(counter.Apply(new PNCounterOperation(A, (ulong)round + 1, (ulong)round + 1)),
                $"Strictly newer balanced Apply failed in round {round}"),
            _ => CaptureBalanced(counter, retained, ValidateBalancedApply));
        CaptureBalanced(counter, retained, ValidateBalancedApply);
        foreach (Snapshot snapshot in retained.TakeLast(3))
        {
            await AssertState(snapshot.Counter, [(A, 64)], [(A, 64)], 0);
        }
        await AssertState(counter, [(A, 64)], [(A, 64)], 0);
        NoDelta(counter);
        counter.Apply(new PNCounterOperation(A, 65, 65));
        foreach (Snapshot snapshot in retained)
        {
            CheckStable(snapshot);
            NoDelta(snapshot.Counter);
            snapshot.Counter.Increment(C, 11);
            Require(Slot(Decode(snapshot.Counter.ToByteArray()).Positive, C) == 11,
                "Retained clone did not own C");
        }
        await AssertState(counter, [(A, 65)], [(A, 65)], 0);
    }

    [Test]
    public async Task Concurrent_DeltaExtraction_RecoversComponents_WithoutPairTransactionAssumption(
        CancellationToken cancellationToken)
    {
        var source = new PNCounter();
        var retained = new List<Snapshot>();
        Drain(source, retained);
        RunRounds(32, cancellationToken, _ =>
        {
            source.Increment(A, 3);
            source.Decrement(A, 2);
        }, _ => Drain(source, retained));
        Drain(source, retained);
        NoDelta(source);
        var receiver = new PNCounter();
        foreach (Snapshot snapshot in retained)
        {
            Components components = Decode(snapshot.Bytes);
            ulong positive = Slot(components.Positive, A), negative = Slot(components.Negative, A);
            Require(components.Positive.All(slot => slot.Replica == A) &&
                components.Negative.All(slot => slot.Replica == A), "Delta identity mismatch");
            Require(positive <= 96 && positive % 3 == 0 && negative <= 64 && negative % 2 == 0,
                $"Delta components out of range: P={positive}, N={negative}");
            Require(snapshot.Counter.Value == unchecked((long)positive - (long)negative),
                "Delta own Value does not match its own components");
            receiver.MergeDelta(snapshot.Counter);
        }
        await AssertState(source, [(A, 96)], [(A, 64)], 32);
        await AssertState(receiver, [(A, 96)], [(A, 64)], 32);
        NoDelta(receiver);
        source.Increment(A, 3);
        source.Decrement(A, 2);
        Require(source.TryExtractDelta(out PNCounter? checkpoint), "Quiescent pair delta missing");
        await AssertState(checkpoint!, [(A, 99)], [(A, 66)], 33);
        NoDelta(source.Clone());
        foreach (Snapshot snapshot in retained)
        {
            CheckStable(snapshot);
            snapshot.Counter.Decrement(C, 11);
        }
        await AssertState(source, [(A, 99)], [(A, 66)], 33);
        await AssertState(receiver, [(A, 96)], [(A, 64)], 32);
        NoDelta(source);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task DecodedState_InterfaceMerge_AndRawReads_PreserveCoherentSnapshots(
        bool useDelta, CancellationToken cancellationToken)
    {
        // Literal singleton pairs introduce new identities through REAL interface dispatch.
        ReadOnlyMemory<byte>[] fixtures =
        [
            Convert.FromHexString("01000000000000000000000000000000010A01000000000000000000000000000000010A"),
            Convert.FromHexString("010000000000000000000000000000000214010000000000000000000000000000000214"),
            Convert.FromHexString("01000000000000000000000000000000031E01000000000000000000000000000000031E"),
            Convert.FromHexString("010000000000000000000000000000000428010000000000000000000000000000000428"),
        ];
        (ReplicaId Replica, ulong Value)[] expected = [(A, 10), (B, 20), (C, 30), (ReplicaId.FromUInt64(4), 40)];
        var raw = new PNCounter();
        var retained = new List<Snapshot>();
        void validate(PNCounter owned)
        {
            Components components = Decode(owned.ToByteArray());
            Require(owned.Value == 0, "Decoded/interface snapshot Value was not zero");
            Require(components.Positive.SequenceEqual(components.Negative), "Interface merge pair is torn");
            Require(components.Positive.All(slot => expected.Contains(slot)),
                "Interface merge slot total/identity changed");
        }
        CaptureBalanced(raw, retained, validate);
        RunRounds(4, cancellationToken, round =>
        {
            PNCounter decoded = DecodeState(fixtures[round]);
            if (useDelta)
            {
                ((IDeltaConvergent<PNCounter, PNCounter>)raw).MergeDelta(decoded);
            }
            else
            {
                ((IConvergent<PNCounter>)raw).Merge(decoded);
            }
        }, _ => CaptureBalanced(raw, retained, validate));
        CaptureBalanced(raw, retained, validate);
        foreach (Snapshot snapshot in retained.TakeLast(3))
        {
            await AssertState(snapshot.Counter, expected, expected, 0);
        }
        await AssertState(raw, expected, expected, 0);
        Components final = Decode(raw.ToByteArray());
        await Assert.That(final.Positive.Sum(slot => (long)slot.Value)).IsEqualTo(100L);
        await Assert.That(final.Negative.Sum(slot => (long)slot.Value)).IsEqualTo(100L);
        var literalExpected = new PNCounter();
        foreach ((ReplicaId replica, ulong value) in expected)
        {
            literalExpected.Apply(new PNCounterOperation(replica, value, value));
        }
        await Assert.That(raw.Compare(literalExpected)).IsEqualTo(CrdtOrder.Equal);
        NoDelta(raw);
        raw.Apply(new PNCounterOperation(A, 11, 11));
        foreach (Snapshot snapshot in retained)
        {
            CheckStable(snapshot);
        }
    }

    [Test]
    [Arguments(false, "Merge", false)]
    [Arguments(false, "Merge", true)]
    [Arguments(true, "Merge", false)]
    [Arguments(true, "Merge", true)]
    [Arguments(false, "Compare", false)]
    [Arguments(false, "Compare", true)]
    [Arguments(true, "Compare", false)]
    [Arguments(true, "Compare", true)]
    [Arguments(false, "Equals", false)]
    [Arguments(false, "Equals", true)]
    [Arguments(true, "Equals", false)]
    [Arguments(true, "Equals", true)]
    public async Task Opposing_PairOperations_Complete_WithExactStateAndOrder(
        bool reverseCreation, string operation, bool concurrent, CancellationToken cancellationToken)
    {
        var first = new PNCounter();
        var second = new PNCounter();
        PNCounter left = reverseCreation ? second : first;
        PNCounter right = reverseCreation ? first : second;
        left.Apply(new PNCounterOperation(A, 3, concurrent ? 5UL : 2UL));
        right.Apply(new PNCounterOperation(A, 7, concurrent ? 2UL : 5UL));
        Snapshot beforeLeft = Record(left), beforeRight = Record(right);
        CrdtOrder leftOrder = concurrent ? CrdtOrder.Concurrent : CrdtOrder.Less;
        CrdtOrder rightOrder = concurrent ? CrdtOrder.Concurrent : CrdtOrder.Greater;
        RunRounds(16, cancellationToken, _ => InvokePair(left, right, operation, leftOrder),
            _ => InvokePair(right, left, operation, rightOrder));
        if (operation == "Merge")
        {
            await AssertState(left, [(A, 7)], [(A, 5)], 2);
            await AssertState(right, [(A, 7)], [(A, 5)], 2);
            await Assert.That(left.Compare(right)).IsEqualTo(CrdtOrder.Equal);
            await Assert.That(right.Compare(left)).IsEqualTo(CrdtOrder.Equal);
            await Assert.That(left.Equals(right)).IsTrue();
            await Assert.That(right.Equals((object)left)).IsTrue();
            Snapshot stable = Record(left);
            left.Merge(right);
            CheckStable(stable);
        }
        else
        {
            CheckStable(beforeLeft);
            CheckStable(beforeRight);
            await Assert.That(left.Compare(right)).IsEqualTo(leftOrder);
            await Assert.That(right.Compare(left)).IsEqualTo(rightOrder);
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Opposing_EqualPositivePairs_ObserveNegativeStateAndOrder(
        bool reverseCreation, CancellationToken cancellationToken)
    {
        var first = new PNCounter();
        var second = new PNCounter();
        PNCounter left = reverseCreation ? second : first;
        PNCounter right = reverseCreation ? first : second;
        left.Apply(new PNCounterOperation(A, 7, 3));
        right.Apply(new PNCounterOperation(A, 7, 5));
        RunRounds(16, cancellationToken, _ =>
        {
            InvokePair(left, right, "Compare", CrdtOrder.Less);
            InvokePair(left, right, "Equals", CrdtOrder.Less);
        }, _ =>
        {
            InvokePair(right, left, "Compare", CrdtOrder.Greater);
            InvokePair(right, left, "Equals", CrdtOrder.Greater);
        });
        await AssertState(left, [(A, 7)], [(A, 3)], 4);
        await AssertState(right, [(A, 7)], [(A, 5)], 2);
        RunRounds(16, cancellationToken, _ => left.Merge(right), _ => right.Merge(left));
        await AssertState(left, [(A, 7)], [(A, 5)], 2);
        await AssertState(right, [(A, 7)], [(A, 5)], 2);
        await Assert.That(left.Compare(right)).IsEqualTo(CrdtOrder.Equal);
        await Assert.That(right.Compare(left)).IsEqualTo(CrdtOrder.Equal);
        await Assert.That(left.Equals(right)).IsTrue();
        await Assert.That(right.Equals((object)left)).IsTrue();
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    public async Task SelfAlias_PairOperations_PreserveState(int shape, CancellationToken cancellationToken)
    {
        var counter = new PNCounter();
        if (shape > 0)
        {
            counter.Increment(A, 7);
            counter.Decrement(A, 3);
        }
        if (shape > 1)
        {
            counter.Increment(B, 5);
            counter.Decrement(B, 2);
        }
        Snapshot before = Record(counter);
        RunRounds(1, cancellationToken, _ =>
        {
            counter.Merge(counter);
            counter.MergeDelta(counter);
            Require(counter.Compare(counter) == CrdtOrder.Equal, "Self Compare was not Equal");
            Require(counter.Equals(counter) && counter.Equals((object)counter), "Self equality failed");
        });
        CheckStable(before);
        (ReplicaId, ulong)[] positive = shape == 0 ? [] : shape == 1 ? [(A, 7)] : [(A, 7), (B, 5)];
        (ReplicaId, ulong)[] negative = shape == 0 ? [] : shape == 1 ? [(A, 3)] : [(A, 3), (B, 2)];
        await AssertState(counter, positive, negative, shape == 0 ? 0 : shape == 1 ? 4 : 7);
        if (shape == 0)
        {
            NoDelta(counter);
        }
        else
        {
            await Assert.That(counter.TryExtractDelta(out PNCounter? delta)).IsTrue();
            await Assert.That(delta!.Equals(counter)).IsTrue();
            NoDelta(counter);
        }
    }

    [Test]
    public async Task BinaryWriter_Callback_ExportsPreCallbackPair_AndAllowsOtherThreadMutation(
        CancellationToken cancellationToken)
    {
        PNCounter counter = Seed(A, 7, 7);
        await Assert.That(counter.Value).IsEqualTo(0L);
        byte[] bytes = ExportWithMutation(counter, () =>
            Require(counter.Apply(new PNCounterOperation(A, 11, 13)), "Callback Apply did not change original"),
            cancellationToken);
        PNCounter exported = PNCounter.ReadFrom(bytes);
        Components components = Decode(bytes);
        Require(components.Positive.SequenceEqual(new[] { (A, 7UL) }) &&
            components.Negative.SequenceEqual(new[] { (A, 7UL) }), "Writer exported a post-callback/torn pair");
        await AssertState(exported, [(A, 7)], [(A, 7)], 0);
        await AssertState(counter, [(A, 11)], [(A, 13)], -2);
        string retained = Convert.ToHexString(bytes);
        counter.Apply(new PNCounterOperation(A, 17, 19));
        await Assert.That(Convert.ToHexString(bytes)).IsEqualTo(retained);
        await AssertState(exported, [(A, 7)], [(A, 7)], 0);
    }

    [Test]
    [Arguments(0, "0000", "{\"p\":[],\"n\":[]}", 0L)]
    [Arguments(1, SingleHex, SingleJson, 4L)]
    [Arguments(2, PairHex, PairJson, 131L)]
    public async Task WireFormat_UsesLiteralSortedFixtures_AndPreservesReplicaIdentity(
        int shape, string hex, string json, long value, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var counter = new PNCounter();
        if (shape == 2)
        {
            counter.Apply(new PNCounterOperation(B, 128, 1));
        }
        if (shape > 0)
        {
            counter.Apply(new PNCounterOperation(A, 7, 3));
        }
        await Assert.That(Convert.ToHexString(counter.ToByteArray())).IsEqualTo(hex);
        await Assert.That(counter.ToJson()).IsEqualTo(json);
        (ReplicaId, ulong)[] positive = shape == 0 ? [] : shape == 1 ? [(A, 7)] : [(A, 7), (B, 128)];
        (ReplicaId, ulong)[] negative = shape == 0 ? [] : shape == 1 ? [(A, 3)] : [(A, 3), (B, 1)];
        await AssertState(counter, positive, negative, value);
        PNCounter binary = PNCounter.ReadFrom(Convert.FromHexString(hex));
        PNCounter decodedJson = PNCounter.FromJson(json);
        await AssertState(binary, positive, negative, value);
        await AssertState(decodedJson, positive, negative, value);
        await Assert.That(binary.Compare(counter)).IsEqualTo(CrdtOrder.Equal);
        await Assert.That(decodedJson.Equals(counter)).IsTrue();
        Components literal = Decode(Convert.FromHexString(hex));
        Require(literal.Positive.SequenceEqual(positive) && literal.Negative.SequenceEqual(negative),
            "Literal binary components/identity/order changed");
        Components literalJson = DecodeJson(json);
        Require(literalJson.Positive.SequenceEqual(positive) && literalJson.Negative.SequenceEqual(negative),
            "Literal JSON components/identity/order changed");
        const string operationHex = "00000000000000000000000000000002800103";
        await Assert.That(Convert.ToHexString(new PNCounterOperation(B, 128, 3).ToByteArray()))
            .IsEqualTo(operationHex);
        PNCounterOperation operation = PNCounterOperation.ReadFrom(Convert.FromHexString(operationHex));
        await Assert.That(operation.Replica).IsEqualTo(B);
        await Assert.That(operation.Positive).IsEqualTo(128UL);
        await Assert.That(operation.Negative).IsEqualTo(3UL);
    }

    [Test]
    public async Task ComponentwiseMergeAndApply_PreserveMaxima_Idempotency_AndStateOrder(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        PNCounter left = Seed(A, 5, 2), right = Seed(B, 7, 3);
        await Assert.That(left.Compare(right)).IsEqualTo(CrdtOrder.Concurrent);
        await Assert.That(right.Compare(left)).IsEqualTo(CrdtOrder.Concurrent);
        Snapshot leftSeed = Record(left), rightSeed = Record(right);
        PNCounter maxLeft = left.Clone(), maxRight = right.Clone();
        maxLeft.Merge(right);
        maxRight.Merge(left);
        await AssertState(maxLeft, [(A, 5), (B, 7)], [(A, 2), (B, 3)], 7);
        await AssertState(maxRight, [(A, 5), (B, 7)], [(A, 2), (B, 3)], 7);
        await Assert.That(maxLeft.Compare(maxRight)).IsEqualTo(CrdtOrder.Equal);
        Snapshot stable = Record(maxLeft);
        maxLeft.Merge(left);
        maxLeft.Merge(right);
        maxLeft.MergeDelta(maxRight);
        CheckStable(stable);
        CheckStable(leftSeed);
        CheckStable(rightSeed);
        NoDelta(maxLeft);
        NoDelta(maxRight);

        var applied = new PNCounter();
        (ulong Positive, ulong Negative, bool Changed, ulong ExpectedP, ulong ExpectedN, long Value)[] rows =
        [
            (0, 0, false, 0, 0, 0),
            (7, 0, true, 7, 0, 7),
            (7, 5, true, 7, 5, 2),
            (6, 9, true, 7, 9, -2),
            (11, 8, true, 11, 9, 2),
            (11, 9, false, 11, 9, 2),
            (10, 8, false, 11, 9, 2),
        ];
        foreach (var row in rows)
        {
            PNCounter previous = applied.Clone();
            await Assert.That(applied.Apply(new PNCounterOperation(A, row.Positive, row.Negative)))
                .IsEqualTo(row.Changed);
            await AssertState(applied, row.ExpectedP == 0 ? [] : [(A, row.ExpectedP)],
                row.ExpectedN == 0 ? [] : [(A, row.ExpectedN)], row.Value);
            await Assert.That(applied.Compare(previous)).IsEqualTo(row.Changed ? CrdtOrder.Greater : CrdtOrder.Equal);
            await Assert.That(previous.Compare(applied)).IsEqualTo(row.Changed ? CrdtOrder.Less : CrdtOrder.Equal);
            NoDelta(applied);
        }
    }

    [Test]
    public async Task EqualSignedValues_DoNotImplyStateEqualityOrOrderEquality(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        PNCounter left = Seed(A, 5, 3), right = Seed(A, 7, 5);
        await Assert.That(left.Value).IsEqualTo(2L);
        await Assert.That(right.Value).IsEqualTo(2L);
        await Assert.That(left.Equals(right)).IsFalse();
        await Assert.That(right.Equals((object)left)).IsFalse();
        await Assert.That(left.Compare(right)).IsEqualTo(CrdtOrder.Less);
        await Assert.That(right.Compare(left)).IsEqualTo(CrdtOrder.Greater);
        const string zeroJson =
            "{\"p\":[{\"replica\":\"00000000-0000-0000-0000-000000000001\",\"value\":0}],\"n\":[]}";
        const string zeroHex = "01000000000000000000000000000000010000";
        PNCounter zero = PNCounter.FromJson(zeroJson);
        var empty = new PNCounter();
        await AssertState(zero, [(A, 0)], [], 0);
        await Assert.That(empty.Value).IsEqualTo(0L);
        await Assert.That(zero.Compare(empty)).IsEqualTo(CrdtOrder.Equal);
        await Assert.That(empty.Compare(zero)).IsEqualTo(CrdtOrder.Equal);
        await Assert.That(zero.Equals(empty)).IsFalse();
        await Assert.That(empty.Equals((object)zero)).IsFalse();
        await Assert.That(zero.ToJson()).IsEqualTo(zeroJson);
        await Assert.That(Convert.ToHexString(zero.ToByteArray())).IsEqualTo(zeroHex);
        await Assert.That(zero.Clone().Equals(zero)).IsTrue();
        await Assert.That(zero.Clone().GetHashCode()).IsEqualTo(zero.GetHashCode());
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task EmptyAndSingleton_Copies_AndDeltas_PreserveSemantics(
        bool decrement, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var source = new PNCounter();
        PNCounter emptyClone = source.Clone();
        await AssertState(source, [], [], 0);
        await Assert.That(Convert.ToHexString(source.ToByteArray())).IsEqualTo("0000");
        await Assert.That(source.ToJson()).IsEqualTo("{\"p\":[],\"n\":[]}");
        NoDelta(source);
        NoDelta(emptyClone);
        PNCounterOperation operation = decrement ? source.Decrement(A, 7) : source.Increment(A, 7);
        await Assert.That(operation.Replica).IsEqualTo(A);
        await Assert.That(operation.Positive).IsEqualTo(decrement ? 0UL : 7UL);
        await Assert.That(operation.Negative).IsEqualTo(decrement ? 7UL : 0UL);
        (ReplicaId, ulong)[] positive = decrement ? [] : [(A, 7)];
        (ReplicaId, ulong)[] negative = decrement ? [(A, 7)] : [];
        long value = decrement ? -7 : 7;
        PNCounter copy = source.Clone();
        await AssertState(copy, positive, negative, value);
        NoDelta(copy);
        await Assert.That(source.TryExtractDelta(out PNCounter? delta)).IsTrue();
        await AssertState(delta!, positive, negative, value);
        NoDelta(source);
        var applied = new PNCounter();
        var merged = new PNCounter();
        applied.Apply(operation);
        merged.Merge(delta!);
        await AssertState(applied, positive, negative, value);
        await AssertState(merged, positive, negative, value);
        NoDelta(applied);
        NoDelta(merged);
        source.Increment(B, 3);
        copy.Decrement(C, 5);
        await AssertState(delta!, positive, negative, value);
        await AssertState(source, decrement ? [(B, 3)] : [(A, 7), (B, 3)], negative, value + 3);
        await AssertState(emptyClone, [], [], 0);
    }

    [Test]
    [Arguments("Merge")]
    [Arguments("MergeDelta")]
    [Arguments("Compare")]
    [Arguments("FromJson")]
    public async Task NullOperands_AreRejected_AndNullEqualityIsFalse(
        string method, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        PNCounter counter = Seed(A, 7, 3);
        Snapshot before = Record(counter);
        Action call = method switch
        {
            "Merge" => () => counter.Merge(null!),
            "MergeDelta" => () => counter.MergeDelta(null!),
            "Compare" => () => counter.Compare(null!),
            _ => () => PNCounter.FromJson(null!),
        };
        await Assert.That(call).Throws<ArgumentNullException>();
        bool typedNull = counter.Equals((PNCounter?)null);
        bool objectNull = counter!.Equals((object?)null);
        bool unrelated = counter!.Equals(new object());
        await Assert.That(typedNull).IsFalse();
        await Assert.That(objectNull).IsFalse();
        await Assert.That(unrelated).IsFalse();
        CheckStable(before);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ZeroLocalAmounts_AreRejected_WithoutStateOrDeltaChanges(
        bool seeded, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var counter = new PNCounter();
        if (seeded)
        {
            counter.Increment(A, 7);
            counter.Decrement(A, 3);
            Require(counter.TryExtractDelta(out _), "Seed delta was not buffered");
        }
        Snapshot before = Record(counter);
        await Assert.That(() => counter.Increment(A, 0)).Throws<ArgumentOutOfRangeException>();
        CheckStable(before);
        NoDelta(counter);
        await Assert.That(() => counter.Decrement(A, 0)).Throws<ArgumentOutOfRangeException>();
        CheckStable(before);
        NoDelta(counter);
        await AssertState(counter, seeded ? [(A, 7)] : [], seeded ? [(A, 3)] : [], seeded ? 4 : 0);
    }

    [Test]
    public async Task UncheckedArithmetic_PreservesComponentAndSignedWraparound(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        PNCounter boundary = Seed(A, long.MaxValue, 0);
        await AssertState(boundary, [(A, long.MaxValue)], [], long.MaxValue);
        PNCounterOperation signed = boundary.Increment(A);
        await Assert.That(signed.Replica).IsEqualTo(A);
        await Assert.That(signed.Positive).IsEqualTo(1UL << 63);
        await Assert.That(signed.Negative).IsEqualTo(0UL);
        await AssertState(boundary, [(A, 1UL << 63)], [], long.MinValue);
        PNCounter positive = Seed(A, ulong.MaxValue, 0);
        await AssertState(positive, [(A, ulong.MaxValue)], [], -1);
        PNCounterOperation pWrap = positive.Increment(A);
        await Assert.That(pWrap.Replica).IsEqualTo(A);
        await Assert.That(pWrap.Positive).IsEqualTo(0UL);
        await Assert.That(pWrap.Negative).IsEqualTo(0UL);
        await AssertState(positive, [(A, 0)], [], 0);
        PNCounter negative = Seed(A, 0, ulong.MaxValue);
        await AssertState(negative, [], [(A, ulong.MaxValue)], 1);
        PNCounterOperation nWrap = negative.Decrement(A);
        await Assert.That(nWrap.Replica).IsEqualTo(A);
        await Assert.That(nWrap.Positive).IsEqualTo(0UL);
        await Assert.That(nWrap.Negative).IsEqualTo(0UL);
        await AssertState(negative, [], [(A, 0)], 0);
        await AssertState(Seed(A, 1UL << 63, 1), [(A, 1UL << 63)], [(A, 1)], long.MaxValue);
        PNCounter sum = Seed(A, ulong.MaxValue, 0);
        sum.Apply(new PNCounterOperation(B, 1, 0));
        await AssertState(sum, [(A, ulong.MaxValue), (B, 1)], [], 0);
    }

    private static PNCounter Seed(ReplicaId replica, ulong positive, ulong negative)
    {
        var counter = new PNCounter();
        counter.Apply(new PNCounterOperation(replica, positive, negative));
        return counter;
    }

    private static async Task AssertState(PNCounter counter, (ReplicaId, ulong)[] positive,
        (ReplicaId, ulong)[] negative, long value)
    {
        await Assert.That(counter.Value).IsEqualTo(value);
        Components binary = Decode(counter.ToByteArray());
        Components json = DecodeJson(counter.ToJson());
        Require(binary.Positive.SequenceEqual(positive), "Binary positive components/identity/order mismatch");
        Require(binary.Negative.SequenceEqual(negative), "Binary negative components/identity/order mismatch");
        Require(json.Positive.SequenceEqual(positive), "JSON positive components/identity/order mismatch");
        Require(json.Negative.SequenceEqual(negative), "JSON negative components/identity/order mismatch");
    }

    private static void InvokePair(PNCounter left, PNCounter right, string operation, CrdtOrder expected)
    {
        switch (operation)
        {
            case "Merge":
                left.Merge(right);
                break;
            case "Compare":
                Require(left.Compare(right) == expected, $"Opposing Compare expected {expected}");
                break;
            default:
                Require(!left.Equals(right) && !left.Equals((object)right), "Immutable unequal states became equal");
                break;
        }
    }

    private sealed record Components((ReplicaId Replica, ulong Value)[] Positive,
        (ReplicaId Replica, ulong Value)[] Negative);

    private sealed record Snapshot(PNCounter Counter, byte[] Bytes, string Hex, string Json, int Hash,
        long Value, Components Components);

    private static Snapshot Record(PNCounter owned, byte[]? capturedBytes = null, string? capturedJson = null)
    {
        byte[] bytes = capturedBytes ?? owned.ToByteArray();
        return new Snapshot(owned, bytes, Convert.ToHexString(bytes), capturedJson ?? owned.ToJson(),
            owned.GetHashCode(), owned.Value, Decode(bytes));
    }

    private static void CaptureBalanced(PNCounter raw, List<Snapshot> retained, Action<PNCounter> validate)
    {
        // No external lock: this is the raw Value API used while real decoded state merges are delivered.
        Require(raw.Value == 0, "Actual raw PN.Value was not exactly zero");
        PNCounter clone = raw.Clone();
        Require(clone.Value == 0, "Actual clone PN.Value was not exactly zero");
        validate(clone);
        retained.Add(Record(clone));
        string json = raw.ToJson();
        PNCounter fromJson = PNCounter.FromJson(json);
        Require(fromJson.Value == 0, "Actual JSON-decoded PN.Value was not exactly zero");
        validate(fromJson);
        Components jsonComponents = DecodeJson(json);
        Require(jsonComponents.Positive.SequenceEqual(jsonComponents.Negative), "Captured JSON pair is torn");
        retained.Add(Record(fromJson, capturedJson: json));
        byte[] bytes = raw.ToByteArray();
        PNCounter fromBinary = PNCounter.ReadFrom(bytes);
        Require(fromBinary.Value == 0, "Actual binary-decoded PN.Value was not exactly zero");
        validate(fromBinary);
        Components binaryComponents = Decode(bytes);
        Require(binaryComponents.Positive.SequenceEqual(binaryComponents.Negative), "Captured binary pair is torn");
        retained.Add(Record(fromBinary, capturedBytes: bytes));
    }

    private static void ValidateBalancedApply(PNCounter owned)
    {
        Components components = Decode(owned.ToByteArray());
        Require(components.Positive.SequenceEqual(components.Negative), "Balanced Apply components differ");
        Require(components.Positive.Length <= 1 && components.Positive.All(slot =>
            slot.Replica == A && slot.Value >= 1 && slot.Value <= 64), "Balanced Apply slots out of range");
        Require(owned.Value == 0, "Balanced Apply owned Value was not zero");
    }

    private static void CheckStable(Snapshot snapshot)
    {
        Require(Convert.ToHexString(snapshot.Bytes) == snapshot.Hex, "Retained export storage changed");
        Require(Convert.ToHexString(snapshot.Counter.ToByteArray()) == snapshot.Hex, "Retained copy bytes changed");
        Require(snapshot.Counter.ToJson() == snapshot.Json, "Retained copy JSON changed");
        Require(snapshot.Counter.GetHashCode() == snapshot.Hash, "Retained copy hash changed");
        Require(snapshot.Counter.Value == snapshot.Value, "Retained copy Value changed");
        Components components = Decode(snapshot.Counter.ToByteArray());
        Require(components.Positive.SequenceEqual(snapshot.Components.Positive) &&
            components.Negative.SequenceEqual(snapshot.Components.Negative), "Retained components changed");
    }

    private static void Drain(PNCounter source, List<Snapshot> retained)
    {
        bool extracted = source.TryExtractDelta(out PNCounter? delta);
        Require(extracted == (delta is not null), "Delta result/null mismatch");
        if (extracted)
        {
            retained.Add(Record(delta!));
        }
    }

    private static void NoDelta(PNCounter counter)
    {
        Require(!counter.TryExtractDelta(out PNCounter? delta) && delta is null, "Unexpected local delta");
    }

    private static ulong Slot((ReplicaId Replica, ulong Value)[] slots, ReplicaId replica) =>
        slots.Where(slot => slot.Replica == replica).Select(slot => slot.Value).SingleOrDefault();

    private static PNCounter DecodeState(ReadOnlyMemory<byte> bytes) => PNCounter.ReadFrom(bytes.Span);

    private static Components Decode(byte[] bytes)
    {
        var reader = new CrdtReader(bytes);
        (ReplicaId Replica, ulong Value)[] positive = ReadBlock(ref reader);
        (ReplicaId Replica, ulong Value)[] negative = ReadBlock(ref reader);
        Require(reader.End, "PN binary snapshot has trailing bytes");
        return new Components(positive, negative);
    }

    private static (ReplicaId Replica, ulong Value)[] ReadBlock(ref CrdtReader reader)
    {
        var slots = new (ReplicaId, ulong)[reader.ReadCount()];
        for (int i = 0; i < slots.Length; i++)
        {
            slots[i] = (reader.ReadReplicaId(), reader.ReadVarUInt64());
        }
        return slots;
    }

    private static Components DecodeJson(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return new Components(JsonBlock(document.RootElement.GetProperty("p")),
            JsonBlock(document.RootElement.GetProperty("n")));
    }

    private static (ReplicaId Replica, ulong Value)[] JsonBlock(JsonElement block) =>
        block.EnumerateArray().Select(entry =>
            (ReplicaId.Parse(entry.GetProperty("replica").GetString()!), entry.GetProperty("value").GetUInt64()))
            .ToArray();

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void RunRounds(int rounds, CancellationToken testCancellation, params Action<int>[] actions)
    {
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(testCancellation);
        cancellation.CancelAfter(TimeSpan.FromSeconds(60));
        var errors = new ConcurrentQueue<Exception>();
        var barrier = new Barrier(actions.Length);
        var workers = new List<Thread>();
        bool allJoined = true;
        try
        {
            foreach (Action<int> action in actions)
            {
                var worker = new Thread(() =>
                {
                    try
                    {
                        for (int round = 0; round < rounds; round++)
                        {
                            // Launch participation, not simultaneous private-lock acquisition.
                            Require(barrier.SignalAndWait(WaitLimit, cancellation.Token), "Round launch timed out");
                            action(round);
                            Require(barrier.SignalAndWait(WaitLimit, cancellation.Token), "Round finish timed out");
                        }
                    }
                    catch (Exception exception)
                    {
                        errors.Enqueue(exception);
                        cancellation.Cancel();
                    }
                })
                { IsBackground = true, Name = "PNCounter concurrency worker" };
                worker.Start();
                workers.Add(worker);
            }
            foreach (Thread worker in workers)
            {
                Require(worker.Join(WaitLimit), $"Worker completion timed out: {worker.Name}");
            }
        }
        catch (Exception exception)
        {
            errors.Enqueue(exception);
        }
        finally
        {
            cancellation.Cancel(); // Releases barrier waits, not non-cancelable Monitor acquisitions.
            foreach (Thread worker in workers)
            {
                if (!worker.Join(WaitLimit))
                {
                    allJoined = false;
                    errors.Enqueue(new TimeoutException($"Cleanup could not join {worker.Name}"));
                }
            }
            if (allJoined)
            {
                barrier.Dispose();
                cancellation.Dispose();
            }
        }
        if (!errors.IsEmpty)
        {
            throw new AggregateException(allJoined ? "Owned PNCounter worker failed" :
                "Non-cancelable worker deadlock during bounded cleanup", errors);
        }
    }

    private static byte[] ExportWithMutation(PNCounter original, Action mutation, CancellationToken testCancellation)
    {
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(testCancellation);
        cancellation.CancelAfter(TimeSpan.FromSeconds(60));
        var start = new ManualResetEventSlim();
        var done = new ManualResetEventSlim();
        Exception? error = null;
        int exporterThread = Environment.CurrentManagedThreadId;
        var worker = new Thread(() =>
        {
            try
            {
                Require(start.Wait(WaitLimit, cancellation.Token), "Callback mutation signal timed out");
                Require(Environment.CurrentManagedThreadId != exporterThread, "Callback reused exporter thread");
                cancellation.Token.ThrowIfCancellationRequested();
                mutation();
            }
            catch (Exception exception)
            {
                error = exception;
            }
            finally
            {
                done.Set();
            }
        })
        { IsBackground = true, Name = "PNCounter writer callback mutation" };
        worker.Start();
        try
        {
            var sink = new CallbackWriter(() =>
            {
                start.Set();
                // Timeout unwinds exporter-held locks before cleanup attempts the mutation worker join.
                Require(done.Wait(WaitLimit, cancellation.Token), "Other-thread callback mutation timed out");
                if (error is not null)
                {
                    throw new InvalidOperationException("Callback mutation failed", error);
                }
            });
            var writer = new CrdtWriter(sink);
            original.Write(ref writer);
            Require(worker.Join(WaitLimit), "Callback worker completion timed out");
            return sink.Bytes;
        }
        finally
        {
            cancellation.Cancel();
            start.Set();
            Require(worker.Join(WaitLimit), "Callback cleanup could not join mutation worker");
            start.Dispose();
            done.Dispose();
            cancellation.Dispose();
        }
    }

    private sealed class CallbackWriter(Action callback) : IBufferWriter<byte>
    {
        private readonly ArrayBufferWriter<byte> _buffer = new();
        private bool _called;
        public byte[] Bytes => _buffer.WrittenSpan.ToArray();
        public void Advance(int count) => _buffer.Advance(count);
        public Memory<byte> GetMemory(int sizeHint = 0)
        {
            Invoke();
            return _buffer.GetMemory(sizeHint);
        }
        public Span<byte> GetSpan(int sizeHint = 0)
        {
            Invoke();
            return _buffer.GetSpan(sizeHint);
        }
        private void Invoke()
        {
            if (!_called)
            {
                _called = true;
                callback();
            }
        }
    }
}
