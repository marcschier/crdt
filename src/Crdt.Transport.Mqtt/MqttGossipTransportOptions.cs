// Copyright (c) marcschier. Licensed under the MIT License.

using Mqtt.Client;

namespace Crdt.Transport.Mqtt;

/// <summary>Configures a <see cref="MqttGossipTransport"/> instance.</summary>
public sealed class MqttGossipTransportOptions
{
    /// <summary>The default shared topic root that replicas publish under and subscribe to.</summary>
    public const string DefaultTopicRoot = "crdt/gossip";

    /// <summary>The default broker URI (plaintext MQTT on loopback).</summary>
    public const string DefaultBrokerUri = "mqtt://localhost:1883";

    /// <summary>
    /// Gets or sets the broker URI, for example <c>mqtt://localhost:1883</c>, <c>mqtts://host:8883</c>,
    /// <c>ws://host/mqtt</c>, or <c>wss://host/mqtt</c>.
    /// </summary>
    public string BrokerUri { get; set; } = DefaultBrokerUri;

    /// <summary>
    /// Gets or sets the shared topic root. Each replica publishes to <c>{TopicRoot}/{ClientId}</c> and
    /// subscribes to <c>{TopicRoot}/+</c>, ignoring its own messages. Must not contain the MQTT wildcards
    /// <c>+</c> or <c>#</c>.
    /// </summary>
    public string TopicRoot { get; set; } = DefaultTopicRoot;

    /// <summary>
    /// Gets or sets the MQTT client id, which also names this replica's publish subtopic. Must be a single
    /// topic segment (no <c>/</c>, <c>+</c>, or <c>#</c>) and unique per broker connection.
    /// </summary>
    public string ClientId { get; set; } = "crdt-" + Guid.NewGuid().ToString("N");

    /// <summary>Gets or sets the quality of service used to publish and subscribe. Defaults to at-least-once.</summary>
    public MqttQoS Qos { get; set; } = MqttQoS.AtLeastOnce;

    /// <summary>Gets or sets an optional user name for broker authentication.</summary>
    public string? Username { get; set; }

    /// <summary>Gets or sets an optional password for broker authentication.</summary>
    public string? Password { get; set; }

    /// <summary>Gets or sets the maximum accepted frame body length, in bytes.</summary>
    public int MaxFrameLength { get; set; } = FrameCodec.DefaultMaxFrameLength;

    /// <summary>
    /// Gets or sets whether each published frame is retained by the broker. Retaining the last frame lets a
    /// late-joining replica immediately receive every peer's most recent state on subscribe.
    /// </summary>
    public bool RetainLastFrame { get; set; } = true;

    /// <summary>
    /// Gets or sets an optional hook to configure the underlying <see cref="MqttClientBuilder"/> directly, for
    /// example to enable TLS, a SOCKS5 proxy, a reconnect policy, a keep-alive interval, or logging. The broker
    /// URI, client id, and credentials configured on this options object are applied before the hook runs.
    /// </summary>
    public Action<MqttClientBuilder>? ConfigureClient { get; set; }
}
