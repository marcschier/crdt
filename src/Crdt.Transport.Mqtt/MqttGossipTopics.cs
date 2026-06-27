// Copyright (c) marcschier. Licensed under the MIT License.

namespace Crdt.Transport.Mqtt;

/// <summary>Builds and matches the per-replica gossip topics used by <see cref="MqttGossipTransport"/>.</summary>
internal static class MqttGossipTopics
{
    /// <summary>Builds the topic a replica publishes its frames to: <c>{root}/{clientId}</c>.</summary>
    /// <param name="root">The shared topic root.</param>
    /// <param name="clientId">The replica's client id (the publish subtopic leaf).</param>
    /// <returns>The publish topic.</returns>
    public static string PublishTopic(string root, string clientId) => root + "/" + clientId;

    /// <summary>Builds the single-level wildcard filter a replica subscribes to: <c>{root}/+</c>.</summary>
    /// <param name="root">The shared topic root.</param>
    /// <returns>The subscription filter.</returns>
    public static string SubscriptionFilter(string root) => root + "/+";

    /// <summary>
    /// Determines whether <paramref name="topic"/> was published by the replica named
    /// <paramref name="clientId"/>, that is, whether the topic's last segment equals the client id.
    /// </summary>
    /// <param name="topic">The received message topic.</param>
    /// <param name="clientId">The local replica's client id.</param>
    /// <returns><see langword="true"/> when the topic is the local replica's own publish topic.</returns>
    public static bool IsOwn(string topic, string clientId)
    {
        int separator = topic.LastIndexOf('/');
        ReadOnlySpan<char> leaf = separator < 0 ? topic.AsSpan() : topic.AsSpan(separator + 1);
        return leaf.SequenceEqual(clientId.AsSpan());
    }
}
