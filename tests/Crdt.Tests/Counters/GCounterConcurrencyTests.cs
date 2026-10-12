// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers;
using System.Collections.Concurrent;
using System.Text.Json;

namespace Crdt.Tests.Counters;

[Timeout(120_000)]
public sealed class GCounterConcurrencyTests
{
    private static readonly ReplicaId A = ReplicaId.FromUInt64(1);
    private static readonly ReplicaId B = ReplicaId.FromUInt64(2);
    private static readonly ReplicaId C = ReplicaId.FromUInt64(3);
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(10);
    private const string A7Hex = "010000000000000000000000000000000107";
    private const string A7Json = "[{\"replica\":\"00000000-0000-0000-0000-000000000001\",\"value\":7}]";
    private const string PairHex = "020000000000000000000000000000000107000000000000000000000000000000028001";
    private const string PairJson =
        "[{\"replica\":\"00000000-0000-0000-0000-000000000001\",\"value\":7}," +
        "{\"replica\":\"00000000-0000-0000-0000-000000000002\",\"value\":128}]";

    [Test]
    public async Task Concurrent_Increment_SameReplica_NoLostUpdates(CancellationToken cancellationToken)
    {
        var counter = new GCounter();
        var operations = new GCounterOperation[4, 64];
        RunRounds(64, cancellationToken, Enumerable.Range(0, 4).Select<int, Action<int>>(worker =>
            round => operations[worker, round] = counter.Increment(A, 3)).ToArray());

        await AssertState(counter, 768, 0, 768);
        await Assert.That(operations.Cast<GCounterOperation>().All(operation => operation.Replica == A)).IsTrue();
        await Assert.That(operations.Cast<GCounterOperation>().Select(operation => operation.Value).Order()
            .SequenceEqual(Enumerable.Range(1, 256).Select(i => (ulong)i * 3))).IsTrue();
    }

    [Test]
    public async Task Concurrent_Reads_Exports_And_Clones_AreCoherentAndIndependent(CancellationToken cancellationToken)
    {
        var counter = new GCounter();
        var retained = new List<Snapshot>();
        CaptureAll(counter, retained);
        GCounter[] updates = Enumerable.Range(1, 32).Select(round =>
            Seed((ulong)round * 3, (ulong)round * 5)).ToArray();
        ulong previous = 0;
        RunRounds(32, cancellationToken,
            round => counter.Merge(updates[round]),
            _ =>
            {
                ulong actual = counter.Value;
                Require(actual >= previous && actual <= 256 && actual % 8 == 0,
                    $"Live Value regressed or observed a torn merge: {actual}");
                previous = actual;
                CaptureAll(counter, retained);
            });
        CaptureAll(counter, retained);
        foreach (Snapshot snapshot in retained)
        {
            ValidateProgress(snapshot.Counter);
            // A single Merge advances BOTH slots. Unlike unrelated increments, a torn copy is observable.
            Require(snapshot.Counter[A] / 3 == snapshot.Counter[B] / 5,
                "Copy/export mixed the two sides of one multi-replica merge");
            NoDelta(snapshot.Counter);
        }

        foreach (Snapshot snapshot in retained.TakeLast(3))
        {
            await AssertState(snapshot.Counter, 96, 160, 256);
        }
        await AssertState(counter, 96, 160, 256);
        counter.Increment(A, 3);
        counter.Increment(B, 5);
        foreach (Snapshot snapshot in retained)
        {
            CheckStable(snapshot);
            snapshot.Counter.Increment(C, 11);
            Require(snapshot.Counter[C] == 11, "Retained copy did not own its C slot");
        }

        await AssertState(counter, 99, 165, 264);
        await Assert.That(counter[C]).IsEqualTo(0UL);
    }

    [Test]
    public async Task Concurrent_DeltaExtraction_RecoversTotals_AndTransfersOwnership(
        CancellationToken cancellationToken)
    {
        var source = new GCounter();
        var retained = new List<Snapshot>();
        Drain(source, retained); // False/null is a valid concurrent extraction partition.
        RunRounds(32, cancellationToken, _ => source.Increment(A, 3), _ => source.Increment(B, 5),
            _ => Drain(source, retained));
        Drain(source, retained);
        NoDelta(source);
        var receiver = new GCounter();
        foreach (Snapshot snapshot in retained)
        {
            ValidateProgress(snapshot.Counter);
            receiver.MergeDelta(snapshot.Counter);
        }

        await AssertState(receiver, 96, 160, 256);
        await AssertState(source, 96, 160, 256);
        NoDelta(receiver);
        source.Increment(A, 3);
        source.Increment(B, 5);
        foreach (Snapshot snapshot in retained)
        {
            CheckStable(snapshot);
            snapshot.Counter.Increment(C, 11);
        }

        await AssertState(source, 99, 165, 264);
        await Assert.That(source[C]).IsEqualTo(0UL);
        await Assert.That(receiver[C]).IsEqualTo(0UL);
    }

    [Test]
    public async Task Concurrent_MergeAndApply_KeepSlotsMonotonic_AndIdempotent(CancellationToken cancellationToken)
    {
        GCounter left = Seed(5, 2);
        GCounter right = Seed(3, 7);
        await Assert.That(left.Compare(right)).IsEqualTo(CrdtOrder.Concurrent);
        await Assert.That(Seed(3, 2).Compare(left)).IsEqualTo(CrdtOrder.Less);
        await Assert.That(left.Compare(Seed(3, 2))).IsEqualTo(CrdtOrder.Greater);
        GCounter maxLeft = left.Clone();
        GCounter maxRight = right.Clone();
        maxLeft.Merge(right);
        maxRight.Merge(left);
        await AssertState(maxLeft, 5, 7, 12);
        await AssertState(maxRight, 5, 7, 12);
        await Assert.That(maxLeft.Compare(maxRight)).IsEqualTo(CrdtOrder.Equal);
        await Assert.That(maxLeft.Apply(new GCounterOperation(A, 4))).IsFalse();
        await Assert.That(maxLeft.Apply(new GCounterOperation(A, 5))).IsFalse();

        ulong previousA = 0, previousB = 0, previousValue = 0;
        RunRounds(16, cancellationToken, _ => left.Merge(right), _ =>
        {
            left.MergeDelta(right);
            left.Apply(new GCounterOperation(A, 4));
            left.Apply(new GCounterOperation(A, 5));
            left.Apply(new GCounterOperation(A, 9));
        }, _ =>
        {
            // These are three separate observations, not a single atomic snapshot.
            ulong a = left[A], b = left[B], value = left.Value;
            Require(a >= previousA && a <= 9, $"A regressed/out of range: {a}");
            Require(b >= previousB && b <= 7, $"B regressed/out of range: {b}");
            Require(value >= previousValue && value <= 16, $"Value regressed/out of range: {value}");
            previousA = a;
            previousB = b;
            previousValue = value;
        });
        await AssertState(left, 9, 7, 16);
        await AssertState(right, 3, 7, 10);
        Snapshot stable = Record(left);
        left.Merge(right);
        left.MergeDelta(maxLeft);
        await Assert.That(left.Apply(new GCounterOperation(A, 4))).IsFalse();
        await Assert.That(left.Apply(new GCounterOperation(A, 9))).IsFalse();
        CheckStable(stable);
        await Assert.That(maxLeft.Apply(new GCounterOperation(A, 9))).IsTrue();
        await Assert.That(left.Compare(maxLeft)).IsEqualTo(CrdtOrder.Equal);
        NoDelta(left);
        NoDelta(right);
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
        var first = new GCounter();
        var second = new GCounter();
        GCounter left = reverseCreation ? second : first;
        GCounter right = reverseCreation ? first : second;
        left.Apply(new GCounterOperation(A, 3));
        left.Apply(new GCounterOperation(B, concurrent ? 5UL : 2UL));
        right.Apply(new GCounterOperation(A, 7));
        right.Apply(new GCounterOperation(B, concurrent ? 2UL : 5UL));
        Snapshot beforeLeft = Record(left), beforeRight = Record(right);
        CrdtOrder leftOrder = concurrent ? CrdtOrder.Concurrent : CrdtOrder.Less;
        CrdtOrder rightOrder = concurrent ? CrdtOrder.Concurrent : CrdtOrder.Greater;
        RunRounds(16, cancellationToken, _ => InvokePair(left, right, operation, leftOrder),
            _ => InvokePair(right, left, operation, rightOrder));
        if (operation == "Merge")
        {
            await AssertState(left, 7, 5, 12);
            await AssertState(right, 7, 5, 12);
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
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    public async Task SelfAlias_PairOperations_PreserveState(int shape, CancellationToken cancellationToken)
    {
        var counter = new GCounter();
        if (shape > 0)
        {
            counter.Increment(A, 7);
        }
        if (shape > 1)
        {
            counter.Increment(B, 5);
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
        await AssertState(counter, shape > 0 ? 7UL : 0, shape > 1 ? 5UL : 0,
            shape == 0 ? 0UL : shape == 1 ? 7UL : 12UL);
        if (shape == 0)
        {
            NoDelta(counter);
        }
        else
        {
            await Assert.That(counter.TryExtractDelta(out GCounter? delta)).IsTrue();
            await Assert.That(delta!.Equals(counter)).IsTrue();
            NoDelta(counter);
        }
    }

    [Test]
    public async Task BinaryWriter_Callback_MutatesOriginalOnOtherThread_AfterSnapshotCapture(
        CancellationToken cancellationToken)
    {
        GCounter counter = Seed(7, 2);
        byte[] bytes = ExportWithMutation(counter, () =>
        {
            counter.Increment(A, 5);
            counter.Increment(C, 3);
        }, cancellationToken);
        GCounter exported = GCounter.ReadFrom(bytes);
        await AssertState(exported, 7, 2, 9);
        await Assert.That(exported[C]).IsEqualTo(0UL);
        Require(Decode(bytes).SequenceEqual(new[] { (A, 7UL), (B, 2UL) }), "Pre-callback slots changed");
        await AssertState(counter, 12, 2, 17, 3);
        string retained = Convert.ToHexString(bytes);
        counter.Increment(B, 5);
        await Assert.That(Convert.ToHexString(bytes)).IsEqualTo(retained);
        await AssertState(exported, 7, 2, 9);
    }

    [Test]
    [Arguments(0, "00", "[]", 0UL)]
    [Arguments(1, A7Hex, A7Json, 7UL)]
    [Arguments(2, PairHex, PairJson, 135UL)]
    public async Task WireFormat_UsesLiteralSortedFixtures_AndPreservesReplicaIdentity(
        int shape, string hex, string json, ulong value, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var counter = new GCounter();
        if (shape == 2)
        {
            counter.Apply(new GCounterOperation(B, 128));
        }
        if (shape > 0)
        {
            counter.Apply(new GCounterOperation(A, 7));
        }
        await Assert.That(Convert.ToHexString(counter.ToByteArray())).IsEqualTo(hex);
        await Assert.That(counter.ToJson()).IsEqualTo(json);
        await Assert.That(counter.Value).IsEqualTo(value);
        (ReplicaId, ulong)[] expected = shape switch
        {
            0 => [],
            1 => [(A, 7)],
            _ => [(A, 7), (B, 128)],
        };
        Require(Decode(Convert.FromHexString(hex)).SequenceEqual(expected), "Literal binary identity/order mismatch");
        Require(DecodeJson(json).SequenceEqual(expected), "Literal JSON identity/order mismatch");
        await Assert.That(GCounter.ReadFrom(Convert.FromHexString(hex)).Equals(counter)).IsTrue();
        await Assert.That(GCounter.FromJson(json).Compare(counter)).IsEqualTo(CrdtOrder.Equal);
        const string operationHex = "000000000000000000000000000000018001";
        await Assert.That(Convert.ToHexString(new GCounterOperation(A, 128).ToByteArray())).IsEqualTo(operationHex);
        GCounterOperation operation = GCounterOperation.ReadFrom(Convert.FromHexString(operationHex));
        await Assert.That(operation.Replica).IsEqualTo(A);
        await Assert.That(operation.Value).IsEqualTo(128UL);
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
        GCounter counter = Seed(7, 5);
        Snapshot before = Record(counter);
        Action call = method switch
        {
            "Merge" => () => counter.Merge(null!),
            "MergeDelta" => () => counter.MergeDelta(null!),
            "Compare" => () => counter.Compare(null!),
            _ => () => GCounter.FromJson(null!),
        };
        await Assert.That(call).Throws<ArgumentNullException>();
        bool typedNull = counter.Equals((GCounter?)null);
        bool objectNull = counter!.Equals((object?)null);
        bool unrelated = counter!.Equals(new object());
        await Assert.That(typedNull).IsFalse();
        await Assert.That(objectNull).IsFalse();
        await Assert.That(unrelated).IsFalse();
        CheckStable(before);
    }

    [Test]
    public async Task EmptyAndSingleton_Copies_AndDeltas_PreserveSemantics(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var source = new GCounter();
        GCounter emptyClone = source.Clone();
        await AssertState(source, 0, 0, 0);
        NoDelta(source);
        NoDelta(emptyClone);
        GCounterOperation operation = source.Increment(A, 7);
        await Assert.That(operation.Replica).IsEqualTo(A);
        await Assert.That(operation.Value).IsEqualTo(7UL);
        GCounter copy = source.Clone();
        await AssertState(copy, 7, 0, 7);
        NoDelta(copy);
        await Assert.That(source.TryExtractDelta(out GCounter? delta)).IsTrue();
        await AssertState(delta!, 7, 0, 7);
        NoDelta(source);
        var applied = new GCounter();
        applied.Apply(operation);
        var merged = new GCounter();
        merged.Merge(delta!);
        await AssertState(applied, 7, 0, 7);
        await AssertState(merged, 7, 0, 7);
        NoDelta(applied);
        NoDelta(merged);
        source.Increment(A, 3);
        copy.Increment(C, 5);
        await AssertState(delta!, 7, 0, 7);
        await AssertState(source, 10, 0, 10);
        await Assert.That(source[C]).IsEqualTo(0UL);
        await AssertState(emptyClone, 0, 0, 0);
    }

    [Test]
    public async Task StoredZeroSlots_AreOrderEqual_ButNotRepresentationEqual(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        const string json = "[{\"replica\":\"00000000-0000-0000-0000-000000000001\",\"value\":0}]";
        const string hex = "010000000000000000000000000000000100";
        GCounter zero = GCounter.FromJson(json);
        var empty = new GCounter();
        await AssertState(zero, 0, 0, 0);
        await Assert.That(zero.Compare(empty)).IsEqualTo(CrdtOrder.Equal);
        await Assert.That(empty.Compare(zero)).IsEqualTo(CrdtOrder.Equal);
        await Assert.That(zero.Equals(empty)).IsFalse();
        await Assert.That(empty.Equals((object)zero)).IsFalse();
        await Assert.That(zero.ToJson()).IsEqualTo(json);
        await Assert.That(Convert.ToHexString(zero.ToByteArray())).IsEqualTo(hex);
        Require(Decode(zero.ToByteArray()).SequenceEqual(new[] { (A, 0UL) }), "Stored zero identity lost");
        GCounter clone = zero.Clone();
        await Assert.That(clone.Equals(zero)).IsTrue();
        await Assert.That(clone.GetHashCode()).IsEqualTo(zero.GetHashCode());
    }

    [Test]
    public async Task UncheckedArithmetic_PreservesSlotAndSumWraparound(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        GCounter counter = Seed(ulong.MaxValue - 1, 0);
        GCounterOperation atMax = counter.Increment(A);
        await Assert.That(atMax.Replica).IsEqualTo(A);
        await Assert.That(atMax.Value).IsEqualTo(ulong.MaxValue);
        await AssertState(counter, ulong.MaxValue, 0, ulong.MaxValue);
        GCounterOperation wrapped = counter.Increment(A);
        await Assert.That(wrapped.Value).IsEqualTo(0UL);
        await Assert.That(wrapped.Replica).IsEqualTo(A);
        Require(Decode(counter.ToByteArray()).SequenceEqual(new[] { (A, 0UL) }), "Wrapped slot lost");
        await AssertState(counter, 0, 0, 0);
        await AssertState(Seed(ulong.MaxValue, 1), ulong.MaxValue, 1, 0);
        await AssertState(Seed(ulong.MaxValue, ulong.MaxValue),
            ulong.MaxValue, ulong.MaxValue, ulong.MaxValue - 1);
    }

    private static GCounter Seed(ulong a, ulong b)
    {
        var counter = new GCounter();
        counter.Apply(new GCounterOperation(A, a));
        counter.Apply(new GCounterOperation(B, b));
        return counter;
    }

    private static async Task AssertState(GCounter counter, ulong a, ulong b, ulong value, ulong c = 0)
    {
        await Assert.That(counter[A]).IsEqualTo(a);
        await Assert.That(counter[B]).IsEqualTo(b);
        await Assert.That(counter[C]).IsEqualTo(c);
        await Assert.That(counter.Value).IsEqualTo(value);
        (ReplicaId Replica, ulong Value)[] slots = Decode(counter.ToByteArray());
        Require(slots.All(slot => slot.Replica == A || slot.Replica == B || slot.Replica == C),
            "Unexpected replica in state");
        Require(Slot(slots, A) == a && Slot(slots, B) == b && Slot(slots, C) == c,
            "Binary components differ from literal state");
        Require(DecodeJson(counter.ToJson()).SequenceEqual(slots), "JSON/binary components differ");
    }

    private static void InvokePair(GCounter left, GCounter right, string operation, CrdtOrder expected)
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

    private sealed record Snapshot(GCounter Counter, byte[] Bytes, string Hex, string Json, int Hash,
        ulong Value, (ReplicaId Replica, ulong Value)[] Slots);

    private static Snapshot Record(GCounter owned, byte[]? capturedBytes = null, string? capturedJson = null)
    {
        byte[] bytes = capturedBytes ?? owned.ToByteArray();
        return new Snapshot(owned, bytes, Convert.ToHexString(bytes), capturedJson ?? owned.ToJson(),
            owned.GetHashCode(), owned.Value, Decode(bytes));
    }

    private static void CaptureAll(GCounter source, List<Snapshot> retained)
    {
        GCounter clone = source.Clone();
        string json = source.ToJson();
        byte[] bytes = source.ToByteArray();
        Require(DecodeJson(json).SequenceEqual(Decode(GCounter.FromJson(json).ToByteArray())),
            "Captured JSON changed during decode");
        Decode(bytes);
        retained.Add(Record(clone));
        retained.Add(Record(GCounter.FromJson(json), capturedJson: json));
        retained.Add(Record(GCounter.ReadFrom(bytes), capturedBytes: bytes));
    }

    private static void CheckStable(Snapshot snapshot)
    {
        Require(Convert.ToHexString(snapshot.Bytes) == snapshot.Hex, "Retained export storage changed");
        Require(Convert.ToHexString(snapshot.Counter.ToByteArray()) == snapshot.Hex, "Retained copy bytes changed");
        Require(snapshot.Counter.ToJson() == snapshot.Json, "Retained copy JSON changed");
        Require(snapshot.Counter.GetHashCode() == snapshot.Hash, "Retained copy hash changed");
        Require(snapshot.Counter.Value == snapshot.Value, "Retained copy Value changed");
        Require(Decode(snapshot.Counter.ToByteArray()).SequenceEqual(snapshot.Slots), "Retained slots changed");
    }

    private static void ValidateProgress(GCounter owned)
    {
        (ReplicaId Replica, ulong Value)[] slots = Decode(owned.ToByteArray());
        ulong a = Slot(slots, A), b = Slot(slots, B);
        Require(slots.All(slot => slot.Replica == A || slot.Replica == B), "Unexpected progress identity");
        Require(a <= 96 && a % 3 == 0 && b <= 160 && b % 5 == 0, $"Invalid progress A={a}, B={b}");
        Require(owned[A] == a && owned[B] == b && owned.Value == a + b, "Owned snapshot is incoherent");
        Require(DecodeJson(owned.ToJson()).SequenceEqual(slots), "Owned JSON components disagree");
    }

    private static void Drain(GCounter source, List<Snapshot> retained)
    {
        bool extracted = source.TryExtractDelta(out GCounter? delta);
        Require(extracted == (delta is not null), "Delta result/null mismatch");
        if (extracted)
        {
            retained.Add(Record(delta!));
        }
    }

    private static void NoDelta(GCounter counter)
    {
        Require(!counter.TryExtractDelta(out GCounter? delta) && delta is null, "Unexpected local delta");
    }

    private static ulong Slot((ReplicaId Replica, ulong Value)[] slots, ReplicaId replica) =>
        slots.Where(slot => slot.Replica == replica).Select(slot => slot.Value).SingleOrDefault();

    private static (ReplicaId Replica, ulong Value)[] Decode(byte[] bytes)
    {
        var reader = new CrdtReader(bytes);
        var slots = new (ReplicaId, ulong)[reader.ReadCount()];
        for (int i = 0; i < slots.Length; i++)
        {
            slots[i] = (reader.ReadReplicaId(), reader.ReadVarUInt64());
        }
        Require(reader.End, "G binary snapshot has trailing bytes");
        return slots;
    }

    private static (ReplicaId Replica, ulong Value)[] DecodeJson(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return document.RootElement.EnumerateArray().Select(entry =>
            (ReplicaId.Parse(entry.GetProperty("replica").GetString()!), entry.GetProperty("value").GetUInt64()))
            .ToArray();
    }

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
                            // Launch participation, not a promise of simultaneous private-lock acquisition.
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
                { IsBackground = true, Name = "GCounter concurrency worker" };
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
            cancellation.Cancel(); // Releases cooperative barrier waits, not deadlocked Monitor acquisitions.
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
            throw new AggregateException(allJoined ? "Owned GCounter worker failed" :
                "Non-cancelable worker deadlock during bounded cleanup", errors);
        }
    }

    private static byte[] ExportWithMutation(GCounter original, Action mutation, CancellationToken testCancellation)
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
        { IsBackground = true, Name = "GCounter writer callback mutation" };
        worker.Start();
        try
        {
            var sink = new CallbackWriter(() =>
            {
                start.Set();
                // If Write holds the original gate, timeout unwinds Write before the finally join.
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
