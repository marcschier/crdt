// Copyright (c) marcschier. Licensed under the MIT License.

using Mqtt.Client;

namespace Crdt.Transport.Mqtt;

/// <summary>
/// A broker-based gossip transport that replicates CRDT frames over MQTT pub/sub. Each replica publishes its
/// frames to its own subtopic (<c>{TopicRoot}/{ClientId}</c>) and subscribes to a single-level wildcard over
/// the shared root (<c>{TopicRoot}/+</c>), ignoring messages it published itself. The broker fans frames out to
/// every other replica.
/// </summary>
/// <remarks>
/// Delivery reliability follows the configured <see cref="MqttGossipTransportOptions.Qos"/>; with
/// <see cref="MqttQoS.AtLeastOnce"/> the broker retries until acknowledged. When
/// <see cref="MqttGossipTransportOptions.RetainLastFrame"/> is set, each replica's most recent frame is retained
/// so late-joining replicas receive it immediately on subscribe. Convergence remains driven by the application's
/// broadcast cadence; this transport carries one complete <see cref="FrameCodec"/> frame per MQTT message.
/// </remarks>
public sealed class MqttGossipTransport : ITransport
{
    private readonly MqttGossipTransportOptions _options;
    private readonly string _publishTopic;
    private readonly string _subscriptionFilter;
    private readonly CancellationTokenSource _stop = new();
    private MqttClient? _client;
    private MqttSubscription? _subscription;
    private int _started;
    private int _disposed;

    /// <summary>Initializes an MQTT gossip transport.</summary>
    /// <param name="options">The transport options, including the broker URI and topic settings.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A required option is missing or a topic value is invalid.</exception>
    public MqttGossipTransport(MqttGossipTransportOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));

        if (string.IsNullOrWhiteSpace(_options.BrokerUri))
        {
            throw new ArgumentException("BrokerUri is required.", nameof(options));
        }

        ValidateTopicRoot(_options.TopicRoot, nameof(options));
        ValidateClientId(_options.ClientId, nameof(options));

        _publishTopic = MqttGossipTopics.PublishTopic(_options.TopicRoot, _options.ClientId);
        _subscriptionFilter = MqttGossipTopics.SubscriptionFilter(_options.TopicRoot);
    }

    /// <inheritdoc/>
    public event Action<ReadOnlyMemory<byte>>? FrameReceived;

    /// <summary>Gets the topic this replica publishes its frames to.</summary>
    public string PublishTopic => _publishTopic;

    /// <inheritdoc/>
    public async ValueTask StartAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (Interlocked.Exchange(ref _started, 1) == 1)
        {
            return;
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _stop.Token);

        MqttClientBuilder builder = MqttClient.CreateBuilder()
            .ConnectTo(_options.BrokerUri)
            .WithClientId(_options.ClientId);

        if (_options.Username is not null)
        {
            builder = builder.WithCredentials(_options.Username, _options.Password ?? string.Empty);
        }

        _options.ConfigureClient?.Invoke(builder);

        _client = builder.Build();
        await _client.ConnectAsync(linked.Token).ConfigureAwait(false);

        var subscriptionOptions = new MqttSubscriptionOptions { QoS = _options.Qos };
        _subscription = await _client
            .SubscribeAsync(_subscriptionFilter, OnMessageAsync, subscriptionOptions, linked.Token)
            .ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async ValueTask SendAsync(ReadOnlyMemory<byte> frame, CancellationToken ct = default)
    {
        if (frame.Length > _options.MaxFrameLength)
        {
            throw new ArgumentException(
                "The frame is larger than the maximum frame length.", nameof(frame));
        }

        // Validate the frame is a well-formed length-prefixed message before it leaves this node.
        FrameCodec.Decode(frame, _options.MaxFrameLength);

        MqttClient? client = _client;
        if (client is null)
        {
            throw new InvalidOperationException("The transport has not started.");
        }

        await client.PublishAsync(
            _publishTopic, frame, _options.Qos, _options.RetainLastFrame, properties: null, ct)
            .ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

#if NET8_0_OR_GREATER
        await _stop.CancelAsync().ConfigureAwait(false);
#else
        _stop.Cancel();
#endif

        if (_subscription is not null)
        {
            try
            {
                await _subscription.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex) when (IsExpectedFault(ex))
            {
            }
        }

        if (_client is not null)
        {
            try
            {
                await _client.DisconnectAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex) when (IsExpectedFault(ex))
            {
            }

            await _client.DisposeAsync().ConfigureAwait(false);
        }

        _stop.Dispose();
    }

    private ValueTask OnMessageAsync(MqttMessage message)
    {
        if (MqttGossipTopics.IsOwn(message.Topic, _options.ClientId))
        {
            return default;
        }

        // The inline payload is a transient slice valid only for the duration of this call, so copy it
        // before raising the event, which may hand the frame to asynchronous consumers.
        byte[] frame = message.PayloadMemory.ToArray();
        if (FrameCodec.TryDecode(frame, out _, _options.MaxFrameLength))
        {
            FrameReceived?.Invoke(frame);
        }

        return default;
    }

    private static void ValidateTopicRoot(string topicRoot, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(topicRoot))
        {
            throw new ArgumentException("TopicRoot is required.", parameterName);
        }

        foreach (char c in topicRoot)
        {
            if (c is '+' or '#')
            {
                throw new ArgumentException(
                    "TopicRoot must not contain the MQTT wildcards '+' or '#'.", parameterName);
            }
        }
    }

    private static void ValidateClientId(string clientId, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(clientId))
        {
            throw new ArgumentException("ClientId is required.", parameterName);
        }

        foreach (char c in clientId)
        {
            if (c is '/' or '+' or '#')
            {
                throw new ArgumentException(
                    "ClientId must be a single topic segment without '/', '+', or '#'.", parameterName);
            }
        }
    }

    private static bool IsExpectedFault(Exception ex) =>
        ex is IOException or ObjectDisposedException or OperationCanceledException
            or MqttConnectionException or MqttProtocolException;
}
