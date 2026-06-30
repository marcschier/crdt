// Copyright (c) marcschier. Licensed under the MIT License.

using Crdt.Consensus;
using Crdt.Transport;

namespace Crdt.Gc;

/// <summary>
/// Coordinates causal-stability garbage collection for local CRDT values using pluggable consensus.
/// </summary>
/// <remarks>
/// The coordinator periodically broadcasts the union of all registered values'
/// <see cref="IGarbageCollectable.ObservedVersion"/> frontiers as the local report. The union is the
/// conservative local frontier because a replica has observed every dot observed by any registered
/// value. The leader advances only when every live member has a fresh report; it commits the
/// pointwise meet of those reports through <see cref="IConsensus"/> before broadcasting the watermark.
/// </remarks>
public sealed class GarbageCollectionCoordinator : IAsyncDisposable
{
    private readonly GarbageCollectionCoordinatorOptions _options;
    private readonly IConsensus _consensus;
    private readonly IFailureDetector? _failureDetector;
    private readonly TransportEndpoint _transport;
    private readonly object _gate = new();
    private readonly List<IGarbageCollectable> _values = [];
    private readonly Dictionary<ReplicaId, VersionReport> _reports = [];
    private readonly CancellationTokenSource _stop = new();
    private readonly SemaphoreSlim _advance = new(1, 1);
    private Task? _loop;
    private StableCut? _lastWatermark;
    private int _started;
    private int _disposed;

    /// <summary>Initializes a garbage-collection coordinator.</summary>
    /// <param name="options">The coordinator options.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A required option is missing or invalid.</exception>
    public GarbageCollectionCoordinator(GarbageCollectionCoordinatorOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _consensus = _options.Consensus
            ?? throw new ArgumentException("Consensus must be configured.", nameof(options));
        _failureDetector = _options.FailureDetector;
        _transport = TransportEndpoint.Create(_options);

        if (_options.LocalReplicaId == ReplicaId.Empty)
        {
            throw new ArgumentException("LocalReplicaId must be configured.", nameof(options));
        }

        if (_options.ReportInterval <= TimeSpan.Zero)
        {
            throw new ArgumentException("ReportInterval must be positive.", nameof(options));
        }

        if (_options.ReportStalenessTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentException("ReportStalenessTimeout must be positive.", nameof(options));
        }

        if (_options.MaxFrameLength <= 0)
        {
            throw new ArgumentException("MaxFrameLength must be positive.", nameof(options));
        }
    }

    /// <summary>Raised after a committed or broadcast stable watermark is applied locally.</summary>
    public event Action<StableCut>? WatermarkApplied;

    /// <summary>Gets the local replica id.</summary>
    public ReplicaId LocalReplicaId => _options.LocalReplicaId;

    /// <summary>Registers a local CRDT value for stable garbage collection.</summary>
    /// <param name="value">The value whose observed frontier contributes to local reports.</param>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    public void Register(IGarbageCollectable value)
    {
        ArgumentNull.ThrowIfNull(value, nameof(value));

        lock (_gate)
        {
            if (!_values.Contains(value))
            {
                _values.Add(value);
            }
        }
    }

    /// <summary>Unregisters a local CRDT value from stable garbage collection.</summary>
    /// <param name="value">The value to unregister.</param>
    /// <returns><see langword="true"/> when the value was registered; otherwise <see langword="false"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    public bool Unregister(IGarbageCollectable value)
    {
        ArgumentNull.ThrowIfNull(value, nameof(value));

        lock (_gate)
        {
            return _values.Remove(value);
        }
    }

    /// <summary>Computes the union of all registered values' observed frontiers.</summary>
    /// <returns>The local causal frontier to report to peers.</returns>
    public VersionVector GetObservedVersion()
    {
        IGarbageCollectable[] values;
        lock (_gate)
        {
            values = [.. _values];
        }

        var observed = new VersionVector();
        foreach (IGarbageCollectable value in values)
        {
            observed.Merge(value.ObservedVersion);
        }

        return observed;
    }

    /// <summary>Starts transport subscriptions, consensus, and periodic report broadcasts.</summary>
    /// <param name="ct">A token that cancels the start operation.</param>
    /// <returns>A task-like value that completes when the coordinator has started.</returns>
    public async ValueTask StartAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (Interlocked.Exchange(ref _started, 1) == 1)
        {
            return;
        }

        _transport.Register(OnFrameReceived);
        _consensus.EntryCommitted += OnEntryCommitted;
        _consensus.MembersChanged += OnMembersChanged;
        _consensus.LeadershipChanged += OnLeadershipChanged;
        if (_failureDetector is not null)
        {
            _failureDetector.MembersChanged += OnMembersChanged;
        }

        await _transport.StartAsync(ct).ConfigureAwait(false);
        await _consensus.StartAsync(ct).ConfigureAwait(false);
        if (_failureDetector is not null)
        {
            await _failureDetector.StartAsync(ct).ConfigureAwait(false);
        }

        await BroadcastReportAsync(ct).ConfigureAwait(false);
        _loop = RunAsync(_stop.Token);
        QueueAdvance();
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
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

        if (Volatile.Read(ref _started) == 1)
        {
            _transport.Unregister(OnFrameReceived);
            _consensus.EntryCommitted -= OnEntryCommitted;
            _consensus.MembersChanged -= OnMembersChanged;
            _consensus.LeadershipChanged -= OnLeadershipChanged;
            if (_failureDetector is not null)
            {
                _failureDetector.MembersChanged -= OnMembersChanged;
            }
        }

        if (_options.DisposeTransport)
        {
            await _transport.DisposeAsync().ConfigureAwait(false);
        }

        if (_options.DisposeConsensus)
        {
            await _consensus.DisposeAsync().ConfigureAwait(false);
        }

        if (_options.DisposeFailureDetector && _failureDetector is not null)
        {
            await _failureDetector.DisposeAsync().ConfigureAwait(false);
        }

        _advance.Dispose();
        _stop.Dispose();
    }

    private async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(_options.ReportInterval, ct).ConfigureAwait(false);
            await BroadcastReportAsync(ct).ConfigureAwait(false);
            await TryAdvanceAsync(ct).ConfigureAwait(false);
        }
    }

    private async ValueTask BroadcastReportAsync(CancellationToken ct)
    {
        VersionVector observed = GetObservedVersion();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        lock (_gate)
        {
            _reports[_options.LocalReplicaId] = new VersionReport(observed.Clone(), now);
        }

        byte[] payload = GcFrameCodec.EncodeVersionReport(_options.LocalReplicaId, observed, _options.MaxFrameLength);
        byte[] frame = FrameCodec.Encode(MessageType.GcVersionReport, payload, _options.MaxFrameLength);
        await _transport.SendAsync(frame, ct).ConfigureAwait(false);
    }

    private void OnFrameReceived(ReadOnlyMemory<byte> frame)
    {
        if (!FrameCodec.TryDecode(frame, out DecodedFrame decoded, _options.MaxFrameLength))
        {
            return;
        }

        switch (decoded.MessageType)
        {
            case MessageType.GcVersionReport:
                HandleVersionReport(decoded.Payload);
                break;
            case MessageType.GcWatermark:
                HandleWatermark(decoded.Payload);
                break;
        }
    }

    private void HandleVersionReport(ReadOnlyMemory<byte> payload)
    {
        if (!GcFrameCodec.TryDecodeVersionReport(payload, out ReplicaId replicaId, out VersionVector? observed)
            || observed is null)
        {
            return;
        }

        if (replicaId == _options.LocalReplicaId)
        {
            return;
        }

        lock (_gate)
        {
            _reports[replicaId] = new VersionReport(observed, DateTimeOffset.UtcNow);
        }

        QueueAdvance();
    }

    private void HandleWatermark(ReadOnlyMemory<byte> payload)
    {
        if (GcFrameCodec.TryDecodeWatermark(payload, out StableCut? cut) && cut is not null)
        {
            ApplyWatermark(cut);
        }
    }

    private void OnEntryCommitted(ReadOnlyMemory<byte> entry)
    {
        if (GcFrameCodec.TryDecodeWatermark(entry, out StableCut? cut) && cut is not null)
        {
            ApplyWatermark(cut);
        }
    }

    private void OnMembersChanged()
    {
        PruneReports();
        QueueAdvance();
    }

    private void OnLeadershipChanged(ReplicaId? leaderId)
    {
        _ = leaderId;
        QueueAdvance();
    }

    private void QueueAdvance()
    {
        if (Volatile.Read(ref _disposed) == 1)
        {
            return;
        }

        _ = TryAdvanceIgnoringCancellationAsync();
    }

    private async Task TryAdvanceIgnoringCancellationAsync()
    {
        try
        {
            await TryAdvanceAsync(_stop.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task TryAdvanceAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!_consensus.IsLeader || Volatile.Read(ref _disposed) == 1)
        {
            return;
        }

        if (!await _advance.WaitAsync(0, ct).ConfigureAwait(false))
        {
            return;
        }

        try
        {
            StableCut? cut = TryBuildStableCut(DateTimeOffset.UtcNow);
            if (cut is null || cut.IsEmpty || !IsAdvancing(cut))
            {
                return;
            }

            byte[] payload = GcFrameCodec.EncodeWatermark(cut, _options.MaxFrameLength);
            bool committed = await _consensus.ProposeAsync(payload, ct).ConfigureAwait(false);
            if (!committed)
            {
                return;
            }

            ApplyWatermark(cut);
            byte[] frame = FrameCodec.Encode(MessageType.GcWatermark, payload, _options.MaxFrameLength);
            await _transport.SendAsync(frame, ct).ConfigureAwait(false);
        }
        finally
        {
            _advance.Release();
        }
    }

    private StableCut? TryBuildStableCut(DateTimeOffset now)
    {
        VersionVector local = GetObservedVersion();
        ReplicaId[] members = GetLiveMembers();
        var reports = new List<VersionVector>(members.Length);

        lock (_gate)
        {
            _reports[_options.LocalReplicaId] = new VersionReport(local.Clone(), now);
            foreach (ReplicaId member in members)
            {
                if (!_reports.TryGetValue(member, out VersionReport report)
                    || now - report.ObservedAt > _options.ReportStalenessTimeout)
                {
                    return null;
                }

                reports.Add(report.Observed.Clone());
            }
        }

        return StableCut.Meet(reports);
    }

    private ReplicaId[] GetLiveMembers()
    {
        IReadOnlyCollection<ReplicaId> source = _failureDetector?.Members ?? _consensus.Members;
        var members = new SortedSet<ReplicaId>(source)
        {
            _options.LocalReplicaId,
        };
        var result = new ReplicaId[members.Count];
        members.CopyTo(result);
        return result;
    }

    private void PruneReports()
    {
        var members = new HashSet<ReplicaId>(GetLiveMembers());
        lock (_gate)
        {
            List<ReplicaId> removed = [];
            foreach (ReplicaId replicaId in _reports.Keys)
            {
                if (!members.Contains(replicaId))
                {
                    removed.Add(replicaId);
                }
            }

            foreach (ReplicaId replicaId in removed)
            {
                _reports.Remove(replicaId);
            }
        }
    }

    private bool IsAdvancing(StableCut cut)
    {
        lock (_gate)
        {
            return IsAdvancingLocked(cut);
        }
    }

    private void ApplyWatermark(StableCut cut)
    {
        IGarbageCollectable[] values;
        bool apply;
        lock (_gate)
        {
            apply = IsAdvancingLocked(cut);
            if (apply)
            {
                _lastWatermark = cut;
            }

            values = [.. _values];
        }

        if (!apply)
        {
            return;
        }

        foreach (IGarbageCollectable value in values)
        {
            value.CollectStable(cut);
        }

        WatermarkApplied?.Invoke(cut);
    }

    private bool IsAdvancingLocked(StableCut cut)
    {
        if (_lastWatermark is null)
        {
            return !cut.IsEmpty;
        }

        bool greater = false;
        foreach (ReplicaId replicaId in _lastWatermark.Replicas)
        {
            ulong previous = _lastWatermark.Floor(replicaId);
            ulong next = cut.Floor(replicaId);
            if (next < previous)
            {
                return false;
            }

            greater |= next > previous;
        }

        foreach (ReplicaId replicaId in cut.Replicas)
        {
            greater |= cut.Floor(replicaId) > _lastWatermark.Floor(replicaId);
        }

        return greater;
    }

    private readonly struct VersionReport
    {
        public VersionReport(VersionVector observed, DateTimeOffset observedAt)
        {
            Observed = observed;
            ObservedAt = observedAt;
        }

        public VersionVector Observed { get; }

        public DateTimeOffset ObservedAt { get; }
    }
}
