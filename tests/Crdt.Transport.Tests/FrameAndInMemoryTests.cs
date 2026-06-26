// Copyright (c) marcschier. Licensed under the MIT License.

namespace Crdt.Transport.Tests;

public sealed class FrameCodecTests
{
    [Test]
    public async Task Encode_And_Decode_Roundtrips_All_Message_Types()
    {
        byte[] payload = [1, 2, 3, 4, 5];
        foreach (MessageType messageType in Enum.GetValues<MessageType>())
        {
            byte[] frame = FrameCodec.Encode(messageType, payload);
            DecodedFrame decoded = FrameCodec.Decode(frame);

            await Assert.That(decoded.MessageType).IsEqualTo(messageType);
            await Assert.That(decoded.Payload.ToArray()).IsEquivalentTo(payload);
        }
    }

    [Test]
    public async Task Decode_Rejects_Truncated_Length_Prefix()
    {
        byte[] frame = [0x80];

        await Assert.That(() => FrameCodec.Decode(frame)).Throws<InvalidDataException>();
        await Assert.That(FrameCodec.TryDecode(frame, out _)).IsFalse();
    }

    [Test]
    public async Task Decode_Rejects_Truncated_Body()
    {
        byte[] frame = [3, (byte)MessageType.State, 1];

        await Assert.That(() => FrameCodec.Decode(frame)).Throws<InvalidDataException>();
        await Assert.That(FrameCodec.TryDecode(frame, out _)).IsFalse();
    }

    [Test]
    public async Task Decode_Rejects_Unknown_Message_Type()
    {
        byte[] frame = [1, 99];

        await Assert.That(() => FrameCodec.Decode(frame)).Throws<InvalidDataException>();
    }

    [Test]
    public async Task Decode_Rejects_Too_Large_Frame()
    {
        byte[] frame = FrameCodec.Encode(MessageType.State, [1, 2, 3]);

        await Assert.That(() => FrameCodec.Decode(frame, maxFrameLength: 2)).Throws<InvalidDataException>();
    }
}

public sealed class InMemoryTransportTests
{
    [Test]
    public async Task State_Gossip_Converges_PNCounters()
    {
        await using var network = new InMemoryNetwork();
        List<ReplicationEngine<PNCounter>> engines = [];
        try
        {
            for (int i = 0; i < 5; i++)
            {
                var counter = new PNCounter();
                var replica = new CrdtReplica<PNCounter>(counter, static c => c.ToByteArray(), ReadPNCounter);
                var engine = new ReplicationEngine<PNCounter>(replica, network.CreateTransport());
                await engine.StartAsync();
                engines.Add(engine);
            }

            var random = new Random(1234);
            for (int i = 0; i < engines.Count; i++)
            {
                ReplicaId replicaId = ReplicaId.FromUInt64((ulong)i + 1UL);
                for (int j = 0; j < 25; j++)
                {
                    ulong amount = (ulong)random.Next(1, 5);
                    if (random.Next(2) == 0)
                    {
                        engines[i].Replica.Value.Increment(replicaId, amount);
                    }
                    else
                    {
                        engines[i].Replica.Value.Decrement(replicaId, amount);
                    }
                }
            }

            foreach (ReplicationEngine<PNCounter> engine in engines)
            {
                await engine.BroadcastStateAsync();
            }

            await network.DrainAsync();
            long value = engines[0].Replica.Value.Value;
            foreach (ReplicationEngine<PNCounter> engine in engines)
            {
                await Assert.That(engine.Replica.Value.Value).IsEqualTo(value);
            }
        }
        finally
        {
            await DisposeAllAsync(engines);
        }
    }

    [Test]
    public async Task State_Gossip_Converges_ORSets()
    {
        await using var network = new InMemoryNetwork();
        List<ReplicationEngine<ORSet<long>>> engines = [];
        try
        {
            for (int i = 0; i < 5; i++)
            {
                var set = new ORSet<long>();
                var replica = new CrdtReplica<ORSet<long>>(
                    set,
                    static s => s.ToByteArray(CrdtValues.Int64),
                    static b => ORSet<long>.ReadFrom(b.Span, CrdtValues.Int64));
                var engine = new ReplicationEngine<ORSet<long>>(replica, network.CreateTransport());
                await engine.StartAsync();
                engines.Add(engine);
            }

            var random = new Random(42);
            for (int i = 0; i < engines.Count; i++)
            {
                ReplicaId replicaId = ReplicaId.FromUInt64((ulong)i + 10UL);
                for (int j = 0; j < 20; j++)
                {
                    engines[i].Replica.Value.Add(replicaId, random.Next(0, 50));
                }
            }

            foreach (ReplicationEngine<ORSet<long>> engine in engines)
            {
                await engine.BroadcastStateAsync();
            }

            await network.DrainAsync();
            long[] expected = [.. engines[0].Replica.Value.Elements.Order()];
            foreach (ReplicationEngine<ORSet<long>> engine in engines)
            {
                await Assert.That(engine.Replica.Value.Elements.Order().ToArray()).IsEquivalentTo(expected);
            }
        }
        finally
        {
            await DisposeAllAsync(engines);
        }
    }

    [Test]
    public async Task State_Gossip_Converges_Text()
    {
        await using var network = new InMemoryNetwork();
        List<ReplicationEngine<Text>> engines = [];
        try
        {
            for (int i = 0; i < 5; i++)
            {
                var text = new Text();
                var replica = new CrdtReplica<Text>(text, static t => t.ToByteArray(), ReadText);
                var engine = new ReplicationEngine<Text>(replica, network.CreateTransport());
                await engine.StartAsync();
                engines.Add(engine);
            }

            for (int i = 0; i < engines.Count; i++)
            {
                ReplicaId replicaId = ReplicaId.FromUInt64((ulong)i + 20UL);
                engines[i].Replica.Value.Append(replicaId, $"node-{i};");
            }

            foreach (ReplicationEngine<Text> engine in engines)
            {
                await engine.BroadcastStateAsync();
            }

            await network.DrainAsync();
            string value = engines[0].Replica.Value.Value;
            foreach (ReplicationEngine<Text> engine in engines)
            {
                await Assert.That(engine.Replica.Value.Value).IsEqualTo(value);
            }
        }
        finally
        {
            await DisposeAllAsync(engines);
        }
    }

    private static PNCounter ReadPNCounter(ReadOnlyMemory<byte> bytes) => PNCounter.ReadFrom(bytes.Span);

    private static Text ReadText(ReadOnlyMemory<byte> bytes) => Text.ReadFrom(bytes.Span);

    private static async ValueTask DisposeAllAsync<TState>(IEnumerable<ReplicationEngine<TState>> engines)
        where TState : IConvergent<TState>
    {
        foreach (ReplicationEngine<TState> engine in engines)
        {
            await engine.DisposeAsync();
        }
    }
}
