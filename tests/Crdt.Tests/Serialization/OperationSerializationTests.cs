// Copyright (c) marcschier. Licensed under the MIT License.

namespace Crdt.Tests.Serialization;

public sealed class OperationSerializationTests
{
    private static readonly ReplicaId A = ReplicaId.FromUInt64(1);

    [Test]
    public async Task GCounterOperation_Roundtrips()
    {
        var op = new GCounterOperation(A, 42);
        GCounterOperation restored = GCounterOperation.ReadFrom(op.ToByteArray());
        await Assert.That(restored).IsEqualTo(op);
    }

    [Test]
    public async Task PNCounterOperation_Roundtrips()
    {
        var op = new PNCounterOperation(A, 9, 4);
        PNCounterOperation restored = PNCounterOperation.ReadFrom(op.ToByteArray());
        await Assert.That(restored).IsEqualTo(op);
    }

    [Test]
    public async Task GSetOperation_Roundtrips()
    {
        var op = new GSetOperation<string>("element");
        byte[] bytes = ToBytes(op, CrdtValues.String);
        GSetOperation<string> restored = GSetOperation<string>.ReadFrom(bytes, CrdtValues.String);
        await Assert.That(restored.Element).IsEqualTo("element");
    }

    [Test]
    public async Task RgaInsertOperation_Roundtrips()
    {
        var op = RgaOperation<string>.Insert(new Dot(A, 1), default, "value");
        byte[] bytes = ToBytes(op, CrdtValues.String);
        RgaOperation<string> restored = RgaOperation<string>.ReadFrom(bytes, CrdtValues.String);

        await Assert.That(restored.Kind).IsEqualTo(RgaOperationKind.Insert);
        await Assert.That(restored.Id).IsEqualTo(op.Id);
        await Assert.That(restored.Value).IsEqualTo("value");
    }

    [Test]
    public async Task RgaDeleteOperation_Roundtrips()
    {
        var op = RgaOperation<string>.Delete(new Dot(A, 5));
        byte[] bytes = ToBytes(op, CrdtValues.String);
        RgaOperation<string> restored = RgaOperation<string>.ReadFrom(bytes, CrdtValues.String);

        await Assert.That(restored.Kind).IsEqualTo(RgaOperationKind.Delete);
        await Assert.That(restored.Id).IsEqualTo(op.Id);
    }

    private static byte[] ToBytes(GSetOperation<string> op, ICrdtValueSerializer<string> serializer)
    {
        using var buffer = new PooledBufferWriter();
        op.WriteTo(buffer, serializer);
        return buffer.WrittenSpan.ToArray();
    }

    private static byte[] ToBytes(RgaOperation<string> op, ICrdtValueSerializer<string> serializer)
    {
        using var buffer = new PooledBufferWriter();
        op.WriteTo(buffer, serializer);
        return buffer.WrittenSpan.ToArray();
    }
}
