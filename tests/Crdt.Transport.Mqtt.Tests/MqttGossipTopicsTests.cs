// Copyright (c) marcschier. Licensed under the MIT License.

namespace Crdt.Transport.Mqtt.Tests;

public sealed class MqttGossipTopicsTests
{
    [Test]
    public async Task PublishTopic_Appends_ClientId_To_Root()
    {
        await Assert.That(MqttGossipTopics.PublishTopic("crdt/gossip", "node-1"))
            .IsEqualTo("crdt/gossip/node-1");
    }

    [Test]
    public async Task SubscriptionFilter_Uses_SingleLevel_Wildcard()
    {
        await Assert.That(MqttGossipTopics.SubscriptionFilter("crdt/gossip"))
            .IsEqualTo("crdt/gossip/+");
    }

    [Test]
    public async Task IsOwn_Is_True_For_Matching_Leaf()
    {
        await Assert.That(MqttGossipTopics.IsOwn("crdt/gossip/node-1", "node-1")).IsTrue();
    }

    [Test]
    public async Task IsOwn_Is_False_For_Other_Leaf()
    {
        await Assert.That(MqttGossipTopics.IsOwn("crdt/gossip/node-2", "node-1")).IsFalse();
    }

    [Test]
    public async Task IsOwn_Handles_MultiLevel_Root()
    {
        await Assert.That(MqttGossipTopics.IsOwn("a/b/c/node-1", "node-1")).IsTrue();
        await Assert.That(MqttGossipTopics.IsOwn("a/b/c/node-1", "node-9")).IsFalse();
    }

    [Test]
    public async Task IsOwn_Is_False_When_Leaf_Is_A_Prefix_Of_ClientId()
    {
        await Assert.That(MqttGossipTopics.IsOwn("crdt/gossip/node", "node-1")).IsFalse();
    }
}
