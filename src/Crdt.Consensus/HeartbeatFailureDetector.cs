// Copyright (c) marcschier. Licensed under the MIT License.

namespace Crdt.Consensus;

/// <summary>Failure detector that exchanges periodic heartbeats and expires silent members.</summary>
public sealed class HeartbeatFailureDetector : IFailureDetector
{
    private readonly HeartbeatFailureDetectorOptions _options;
    private readonly ConsensusTransportOptions _transport;
    private readonly object _gate = new();
    private readonly Dictionary<ReplicaId, DateTimeOffset> _members = [];
    private readonly CancellationTokenSource _stop = new();
    private ReplicaId[] _snapshot = [];
    private Task? _loop;
    private int _started;
    private int _disposed;

    /// <summary>Initializes a heartbeat failure detector.</summary>
    /// <param name="options">The detector options.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A required option is missing or invalid.</exception>
    public HeartbeatFailureDetector(HeartbeatFailureDetectorOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _transport = _options.Transport
            ?? throw new ArgumentException("Transport must be configured.", nameof(options));
        _transport.Validate();

        if (_options.HeartbeatInterval <= TimeSpan.Zero)
        {
            throw new ArgumentException("HeartbeatInterval must be positive.", nameof(options));
        }

        if (_options.Timeout <= TimeSpan.Zero)
        {
            throw new ArgumentException("Timeout must be positive.", nameof(options));
        }

        if (_options.MaxFrameLength <= 0)
        {
            throw new ArgumentException("MaxFrameLength must be positive.", nameof(options));
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        _members[_options.LocalReplicaId] = now;
        foreach (ReplicaId member in _options.InitialMembers)
        {
            _members[member] = now;
        }

        UpdateSnapshotLocked();
    }

    /// <inheritdoc/>
    public event Action? MembersChanged;

    /// <inheritdoc/>
    public ReplicaId LocalReplicaId => _options.LocalReplicaId;

    /// <inheritdoc/>
    public IReadOnlyCollection<ReplicaId> Members => _snapshot;

    /// <inheritdoc/>
    public async ValueTask StartAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (Interlocked.Exchange(ref _started, 1) == 1)
        {
            return;
        }

        _transport.Register(OnFrameReceived);
        await _transport.StartAsync(ct).ConfigureAwait(false);
        _loop = RunAsync(_stop.Token);
    }

    /// <inheritdoc/>
    public void AddMember(ReplicaId replicaId)
    {
        bool changed;
        lock (_gate)
        {
            changed = !_members.ContainsKey(replicaId);
            _members[replicaId] = DateTimeOffset.UtcNow;
            if (changed)
            {
                UpdateSnapshotLocked();
            }
        }

        if (changed)
        {
            MembersChanged?.Invoke();
        }
    }

    /// <inheritdoc/>
    public bool RemoveMember(ReplicaId replicaId)
    {
        if (replicaId == _options.LocalReplicaId)
        {
            return false;
        }

        bool changed;
        lock (_gate)
        {
            changed = _members.Remove(replicaId);
            if (changed)
            {
                UpdateSnapshotLocked();
            }
        }

        if (changed)
        {
            MembersChanged?.Invoke();
        }

        return changed;
    }

    /// <inheritdoc/>
    public void ObserveHeartbeat(ReplicaId replicaId)
    {
        AddMember(replicaId);
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        if (Volatile.Read(ref _started) == 1)
        {
            _transport.Unregister(OnFrameReceived);
        }

        _stop.Cancel();
        if (_loop is not null)
        {
            try
            {
                await _loop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        _stop.Dispose();
    }

    private async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await SendHeartbeatAsync(ct).ConfigureAwait(false);
            PruneExpired(DateTimeOffset.UtcNow);
            await Task.Delay(_options.HeartbeatInterval, ct).ConfigureAwait(false);
        }
    }

    private async ValueTask SendHeartbeatAsync(CancellationToken ct)
    {
        byte[] message = ConsensusEnvelopeCodec.Encode(
            ConsensusEnvelopeKind.Heartbeat,
            _options.LocalReplicaId,
            null,
            Guid.Empty,
            ReadOnlyMemory<byte>.Empty,
            _options.MaxFrameLength);

        await _transport.SendMessageAsync(message, ct).ConfigureAwait(false);
    }

    private void OnFrameReceived(ReadOnlyMemory<byte> message)
    {
        if (!ConsensusEnvelopeCodec.TryDecode(message, out ConsensusEnvelope envelope, _options.MaxFrameLength))
        {
            return;
        }

        if (envelope.Kind != ConsensusEnvelopeKind.Heartbeat)
        {
            return;
        }

        if (envelope.RecipientId.HasValue && envelope.RecipientId.Value != _options.LocalReplicaId)
        {
            return;
        }

        if (envelope.SenderId != _options.LocalReplicaId)
        {
            ObserveHeartbeat(envelope.SenderId);
        }
    }

    private void PruneExpired(DateTimeOffset now)
    {
        List<ReplicaId> expired = [];
        lock (_gate)
        {
            foreach (KeyValuePair<ReplicaId, DateTimeOffset> member in _members)
            {
                if (member.Key != _options.LocalReplicaId && now - member.Value > _options.Timeout)
                {
                    expired.Add(member.Key);
                }
            }

            foreach (ReplicaId member in expired)
            {
                _members.Remove(member);
            }

            if (expired.Count > 0)
            {
                UpdateSnapshotLocked();
            }
        }

        if (expired.Count > 0)
        {
            MembersChanged?.Invoke();
        }
    }

    private void UpdateSnapshotLocked()
    {
        var members = new ReplicaId[_members.Count];
        _members.Keys.CopyTo(members, 0);
        Array.Sort(members);
        _snapshot = members;
    }
}
