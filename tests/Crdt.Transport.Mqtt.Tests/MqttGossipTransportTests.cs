// Copyright (c) marcschier. Licensed under the MIT License.

namespace Crdt.Transport.Mqtt.Tests;

public sealed class MqttGossipTransportTests
{
    [Test]
    public async Task Constructor_Rejects_Null_Options()
    {
        await Assert.That(() => new MqttGossipTransport(null!)).Throws<ArgumentNullException>();
    }

    [Test]
    public async Task Constructor_Rejects_Empty_BrokerUri()
    {
        await Assert.That(() => new MqttGossipTransport(new MqttGossipTransportOptions { BrokerUri = "  " }))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task Constructor_Rejects_Wildcard_TopicRoot()
    {
        await Assert.That(() => new MqttGossipTransport(new MqttGossipTransportOptions { TopicRoot = "crdt/+" }))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task Constructor_Rejects_Empty_TopicRoot()
    {
        await Assert.That(() => new MqttGossipTransport(new MqttGossipTransportOptions { TopicRoot = " " }))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task Constructor_Rejects_ClientId_With_Separator()
    {
        await Assert.That(() => new MqttGossipTransport(new MqttGossipTransportOptions { ClientId = "a/b" }))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task Constructor_Rejects_ClientId_With_Wildcard()
    {
        await Assert.That(() => new MqttGossipTransport(new MqttGossipTransportOptions { ClientId = "node#1" }))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task PublishTopic_Reflects_Options()
    {
        var transport = new MqttGossipTransport(new MqttGossipTransportOptions
        {
            TopicRoot = "crdt/gossip",
            ClientId = "node-7",
        });

        await using (transport)
        {
            await Assert.That(transport.PublishTopic).IsEqualTo("crdt/gossip/node-7");
        }
    }

    [Test]
    public async Task SendAsync_Rejects_Oversize_Frame_Without_A_Broker()
    {
        var transport = new MqttGossipTransport(new MqttGossipTransportOptions { MaxFrameLength = 8 });

        await using (transport)
        {
            byte[] big = FrameCodec.Encode(MessageType.State, new byte[64]);
            bool rejected = false;
            try
            {
                await transport.SendAsync(big);
            }
            catch (ArgumentException)
            {
                rejected = true;
            }

            await Assert.That(rejected).IsTrue();
        }
    }

    [Test]
    public async Task SendAsync_Throws_When_Not_Started()
    {
        var transport = new MqttGossipTransport(new MqttGossipTransportOptions());

        await using (transport)
        {
            byte[] frame = FrameCodec.Encode(MessageType.State, [1, 2, 3]);
            bool threw = false;
            try
            {
                await transport.SendAsync(frame);
            }
            catch (InvalidOperationException)
            {
                threw = true;
            }

            await Assert.That(threw).IsTrue();
        }
    }

    [Test]
    public async Task DisposeAsync_Before_Start_Is_Safe_And_Idempotent()
    {
        var transport = new MqttGossipTransport(new MqttGossipTransportOptions());

        await transport.DisposeAsync();
        await transport.DisposeAsync();

        await Assert.That(transport.PublishTopic).IsNotNull();
    }
}
