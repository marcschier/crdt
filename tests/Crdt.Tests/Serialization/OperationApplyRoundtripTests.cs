// Copyright (c) marcschier. Licensed under the MIT License.

namespace Crdt.Tests.Serialization;

/// <summary>
/// Round-trips each CRDT's operation type through its binary serialization and re-applies it
/// on a fresh replica, exercising the operation serialization/apply paths across families.
/// </summary>
public sealed class OperationApplyRoundtripTests
{
    private static readonly ReplicaId A = ReplicaId.FromUInt64(1);

    [Test]
    public async Task GFlagOperation_Roundtrips_And_Applies()
    {
        var flag = new GFlag();
        GFlagOperation op = flag.Enable();
        GFlagOperation restored = GFlagOperation.ReadFrom(op.ToByteArray());

        var target = new GFlag();
        target.Apply(restored);
        await Assert.That(target.Value).IsTrue();
    }

    [Test]
    public async Task EnableWinsFlagOperation_Roundtrips_And_Applies()
    {
        var flag = new EnableWinsFlag();
        EnableWinsFlagOperation op = flag.Enable(A);
        EnableWinsFlagOperation restored = EnableWinsFlagOperation.ReadFrom(op.ToByteArray());

        var target = new EnableWinsFlag();
        target.Apply(restored);
        await Assert.That(target.Value).IsTrue();
    }

    [Test]
    public async Task DisableWinsFlagOperation_Roundtrips_And_Applies()
    {
        var flag = new DisableWinsFlag();
        DisableWinsFlagOperation op = flag.Disable(A);
        DisableWinsFlagOperation restored = DisableWinsFlagOperation.ReadFrom(op.ToByteArray());

        var target = new DisableWinsFlag();
        target.Apply(restored);
        await Assert.That(target.Value).IsFalse();
    }

    [Test]
    public async Task ORSetOperation_Roundtrips_And_Applies()
    {
        var set = new ORSet<string>();
        ORSetOperation<string> op = set.Add(A, "x");
        ORSetOperation<string> restored =
            ORSetOperation<string>.ReadFrom(ToBytes(b => op.WriteTo(b, CrdtValues.String)), CrdtValues.String);

        var target = new ORSet<string>();
        target.Apply(restored);
        await Assert.That(target.Contains("x")).IsTrue();
    }

    [Test]
    public async Task LogootOperation_Roundtrips_And_Applies()
    {
        var seq = new LogootSequence<string>();
        LogootOperation<string> op = seq.Append(A, "x");
        LogootOperation<string> restored =
            LogootOperation<string>.ReadFrom(ToBytes(b => op.WriteTo(b, CrdtValues.String)), CrdtValues.String);

        var target = new LogootSequence<string>();
        target.Apply(restored);
        await Assert.That(string.Join("", target.ToArray())).IsEqualTo("x");
    }

    [Test]
    public async Task LSeqOperation_Roundtrips_And_Applies()
    {
        var seq = new LSeqSequence<string>();
        LSeqOperation<string> op = seq.Append(A, "x");
        LSeqOperation<string> restored =
            LSeqOperation<string>.ReadFrom(ToBytes(b => op.WriteTo(b, CrdtValues.String)), CrdtValues.String);

        var target = new LSeqSequence<string>();
        target.Apply(restored);
        await Assert.That(string.Join("", target.ToArray())).IsEqualTo("x");
    }

    [Test]
    public async Task TreedocOperation_Roundtrips_And_Applies()
    {
        var seq = new TreedocSequence<string>();
        TreedocOperation<string> op = seq.Append(A, "x");
        TreedocOperation<string> restored =
            TreedocOperation<string>.ReadFrom(ToBytes(b => op.WriteTo(b, CrdtValues.String)), CrdtValues.String);

        var target = new TreedocSequence<string>();
        target.Apply(restored);
        await Assert.That(string.Join("", target.ToArray())).IsEqualTo("x");
    }

    [Test]
    public async Task YataOperation_Roundtrips_And_Applies()
    {
        var seq = new YataSequence<string>();
        YataOperation<string> op = seq.Append(A, "x");
        YataOperation<string> restored =
            YataOperation<string>.ReadFrom(ToBytes(b => op.WriteTo(b, CrdtValues.String)), CrdtValues.String);

        var target = new YataSequence<string>();
        target.Apply(restored);
        await Assert.That(string.Join("", target.ToArray())).IsEqualTo("x");
    }

    [Test]
    public async Task WootOperation_Roundtrips_And_Applies()
    {
        var seq = new WootSequence<string>();
        WootOperation<string> op = seq.Append(A, "x");
        WootOperation<string> restored =
            WootOperation<string>.ReadFrom(ToBytes(b => op.WriteTo(b, CrdtValues.String)), CrdtValues.String);

        var target = new WootSequence<string>();
        target.Apply(restored);
        await Assert.That(string.Join("", target.ToArray())).IsEqualTo("x");
    }

    private static byte[] ToBytes(Action<PooledBufferWriter> write)
    {
        using var buffer = new PooledBufferWriter();
        write(buffer);
        return buffer.WrittenSpan.ToArray();
    }
}
