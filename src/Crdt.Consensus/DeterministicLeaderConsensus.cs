// Copyright (c) marcschier. Licensed under the MIT License.

namespace Crdt.Consensus;

/// <summary>Consensus implementation that deterministically elects the lowest live replica id as leader.</summary>
public sealed class DeterministicLeaderConsensus : IConsensus
{
    private readonly DeterministicLeaderConsensusOptions _options;
    private readonly IFailureDetector _failureDetector;
    private readonly ConsensusTransportOptions _transport;
    private readonly object _gate = new();
    private readonly Dictionary<Guid, PendingProposal> _pending = [];
    private readonly HashSet<Guid> _committed = [];
    private ReplicaId[] _members = [];
    private ReplicaId? _leaderId;
    private ConsensusRole _role;
    private int _started;
    private int _disposed;

    /// <summary>Initializes deterministic leader consensus.</summary>
    /// <param name="options">The consensus options.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A required option is missing or invalid.</exception>
    public DeterministicLeaderConsensus(DeterministicLeaderConsensusOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _failureDetector = _options.FailureDetector
            ?? throw new ArgumentException("FailureDetector must be configured.", nameof(options));
        _transport = _options.Transport
            ?? throw new ArgumentException("Transport must be configured.", nameof(options));
        _transport.Validate();

        if (_options.MaxFrameLength <= 0)
        {
            throw new ArgumentException("MaxFrameLength must be positive.", nameof(options));
        }

        _failureDetector.MembersChanged += OnFailureDetectorMembersChanged;
        RefreshMembership();
    }

    /// <inheritdoc/>
    public event Action<ReplicaId?>? LeadershipChanged;

    /// <inheritdoc/>
    public event Action<ReadOnlyMemory<byte>>? EntryCommitted;

    /// <inheritdoc/>
    public event Action? MembersChanged;

    /// <inheritdoc/>
    public ConsensusRole Role => _role;

    /// <inheritdoc/>
    public ReplicaId? LeaderId => _leaderId;

    /// <inheritdoc/>
    public bool IsLeader => Role == ConsensusRole.Leader;

    /// <inheritdoc/>
    public IReadOnlyCollection<ReplicaId> Members => _members;

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
        await _failureDetector.StartAsync(ct).ConfigureAwait(false);
        RefreshMembership();
    }

    /// <inheritdoc/>
    public async ValueTask<bool> ProposeAsync(ReadOnlyMemory<byte> entry, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (!IsLeader || Volatile.Read(ref _disposed) == 1)
        {
            return false;
        }

        ReplicaId[] members = _members;
        byte[] entryBytes = entry.ToArray();
        var pending = new PendingProposal(Guid.NewGuid(), entryBytes, members, _options.LocalReplicaId);

        if (pending.WaitingFor.Count == 0)
        {
            CommitLocally(pending.MessageId, entryBytes);
            return true;
        }

        lock (_gate)
        {
            _pending.Add(pending.MessageId, pending);
        }

        try
        {
            await SendEnvelopeAsync(
                ConsensusEnvelopeKind.Proposal,
                null,
                pending.MessageId,
                entryBytes,
                ct).ConfigureAwait(false);

            bool committed = await WaitForResultAsync(pending.Completion.Task, ct).ConfigureAwait(false);
            if (!committed)
            {
                return false;
            }

            await SendEnvelopeAsync(
                ConsensusEnvelopeKind.Commit,
                null,
                pending.MessageId,
                entryBytes,
                ct).ConfigureAwait(false);
            CommitLocally(pending.MessageId, entryBytes);
            return true;
        }
        finally
        {
            lock (_gate)
            {
                _pending.Remove(pending.MessageId);
            }
        }
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

        _failureDetector.MembersChanged -= OnFailureDetectorMembersChanged;
        List<PendingProposal> pending;
        lock (_gate)
        {
            pending = [.. _pending.Values];
            _pending.Clear();
        }

        foreach (PendingProposal proposal in pending)
        {
            proposal.Completion.TrySetResult(false);
        }

        if (_options.DisposeFailureDetector)
        {
            await _failureDetector.DisposeAsync().ConfigureAwait(false);
        }
    }

    private void OnFailureDetectorMembersChanged()
    {
        bool leaderChanged = RefreshMembership();
        MembersChanged?.Invoke();
        if (leaderChanged)
        {
            LeadershipChanged?.Invoke(_leaderId);
        }
    }

    private bool RefreshMembership()
    {
        ReplicaId[] members = BuildSortedMembers();
        ReplicaId? leader = members.Length == 0 ? null : members[0];
        ConsensusRole role = leader.HasValue && leader.Value == _options.LocalReplicaId
            ? ConsensusRole.Leader
            : ConsensusRole.Follower;
        bool leaderChanged;

        lock (_gate)
        {
            leaderChanged = _leaderId != leader;
            _members = members;
            _leaderId = leader;
            _role = role;
        }

        return leaderChanged;
    }

    private ReplicaId[] BuildSortedMembers()
    {
        var unique = new SortedSet<ReplicaId>(_failureDetector.Members)
        {
            _options.LocalReplicaId,
        };
        var members = new ReplicaId[unique.Count];
        unique.CopyTo(members);
        return members;
    }

    private void OnFrameReceived(ReadOnlyMemory<byte> message)
    {
        if (!ConsensusEnvelopeCodec.TryDecode(message, out ConsensusEnvelope envelope, _options.MaxFrameLength))
        {
            return;
        }

        if (envelope.RecipientId.HasValue && envelope.RecipientId.Value != _options.LocalReplicaId)
        {
            return;
        }

        if (envelope.SenderId == _options.LocalReplicaId)
        {
            return;
        }

        switch (envelope.Kind)
        {
            case ConsensusEnvelopeKind.Proposal:
                HandleProposal(envelope);
                break;
            case ConsensusEnvelopeKind.Ack:
                HandleAck(envelope);
                break;
            case ConsensusEnvelopeKind.Commit:
                HandleCommit(envelope);
                break;
        }
    }

    private void HandleProposal(ConsensusEnvelope envelope)
    {
        if (_leaderId != envelope.SenderId)
        {
            return;
        }

        _ = SendAckAsync(envelope.SenderId, envelope.MessageId);
    }

    private async Task SendAckAsync(ReplicaId leaderId, Guid messageId)
    {
        try
        {
            await SendEnvelopeAsync(
                ConsensusEnvelopeKind.Ack,
                leaderId,
                messageId,
                ReadOnlyMemory<byte>.Empty,
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception) when (Volatile.Read(ref _disposed) == 1)
        {
        }
    }

    private void HandleAck(ConsensusEnvelope envelope)
    {
        PendingProposal? proposal;
        bool complete = false;
        lock (_gate)
        {
            if (!_pending.TryGetValue(envelope.MessageId, out proposal))
            {
                return;
            }

            if (proposal.WaitingFor.Remove(envelope.SenderId) && proposal.WaitingFor.Count == 0)
            {
                complete = true;
            }
        }

        if (complete)
        {
            proposal.Completion.TrySetResult(true);
        }
    }

    private void HandleCommit(ConsensusEnvelope envelope)
    {
        if (_leaderId != envelope.SenderId)
        {
            return;
        }

        CommitLocally(envelope.MessageId, envelope.Payload.ToArray());
    }

    private void CommitLocally(Guid messageId, byte[] entry)
    {
        bool added;
        lock (_gate)
        {
            added = _committed.Add(messageId);
        }

        if (added)
        {
            EntryCommitted?.Invoke(entry);
        }
    }

    private ValueTask SendEnvelopeAsync(
        ConsensusEnvelopeKind kind,
        ReplicaId? recipientId,
        Guid messageId,
        ReadOnlyMemory<byte> payload,
        CancellationToken ct)
    {
        byte[] message = ConsensusEnvelopeCodec.Encode(
            kind,
            _options.LocalReplicaId,
            recipientId,
            messageId,
            payload,
            _options.MaxFrameLength);

        return _transport.SendMessageAsync(message, ct);
    }

    private static async Task<bool> WaitForResultAsync(Task<bool> task, CancellationToken ct)
    {
        if (task.IsCompleted)
        {
            return await task.ConfigureAwait(false);
        }

        var canceled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using CancellationTokenRegistration registration = ct.Register(
            static state => ((TaskCompletionSource<bool>)state!).TrySetResult(true),
            canceled);
        Task completed = await Task.WhenAny(task, canceled.Task).ConfigureAwait(false);
        if (!ReferenceEquals(completed, task))
        {
            ct.ThrowIfCancellationRequested();
        }

        return await task.ConfigureAwait(false);
    }

    private sealed class PendingProposal
    {
        public PendingProposal(
            Guid messageId,
            byte[] entry,
            IReadOnlyCollection<ReplicaId> members,
            ReplicaId localReplicaId)
        {
            MessageId = messageId;
            Entry = entry;
            Completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            WaitingFor = [];

            foreach (ReplicaId member in members)
            {
                if (member != localReplicaId)
                {
                    WaitingFor.Add(member);
                }
            }
        }

        public Guid MessageId { get; }

        public byte[] Entry { get; }

        public HashSet<ReplicaId> WaitingFor { get; }

        public TaskCompletionSource<bool> Completion { get; }
    }
}
