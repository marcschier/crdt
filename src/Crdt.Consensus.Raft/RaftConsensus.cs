// Copyright (c) marcschier. Licensed under the MIT License.

using RaftConfChangeSingle = global::Raft.Configuration.ConfChangeSingle;
using RaftConfChangeType = global::Raft.Configuration.ConfChangeType;
using RaftConfChangeV2 = global::Raft.Configuration.ConfChangeV2;
using RaftConfState = global::Raft.Configuration.ConfState;
using RaftMemoryStorage = global::Raft.Storage.MemoryStorage;
using RaftNode = global::Raft.RaftNode;
using RaftNodeOptions = global::Raft.RaftNodeOptions;
using RaftRole = global::Raft.RaftRole;
using RaftStateChange = global::Raft.RaftStateChange;

namespace Crdt.Consensus.Raft;

/// <summary>Raft-backed implementation of <see cref="IConsensus"/> using RaftCs.</summary>
/// <remarks>
/// Role and leadership transitions are observed from <c>RaftNode.StateChanges</c>, and committed membership from
/// <c>RaftNode.CommittedConfigurations</c>, so this adapter reflects committed Raft state without polling.
/// </remarks>
public sealed class RaftConsensus : IConsensus
{
    private readonly RaftConsensusOptions _options;
    private readonly IFailureDetector _failureDetector;
    private readonly IReplicaIdRegistry _registry;
    private readonly CrdtRaftTransport _transport;
    private readonly RaftNode _node;
    private readonly object _gate = new();
    private readonly Dictionary<ulong, PendingProposal> _pending = new();
    private readonly HashSet<ulong> _configuredNodeIds;
    private readonly CancellationTokenSource _stop = new();
    private ReplicaId[] _members;
    private ReplicaId? _leaderId;
    private ConsensusRole _role;
    private Task? _commitPump;
    private Task? _stateChangesPump;
    private Task? _confPump;
    private long _nextSequence;
    private int _started;
    private int _disposed;
    private int _reconciling;

    /// <summary>Initializes a Raft consensus adapter.</summary>
    /// <param name="options">The Raft consensus options.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A required option is missing or invalid.</exception>
    public RaftConsensus(RaftConsensusOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _failureDetector = _options.FailureDetector
            ?? throw new ArgumentException("FailureDetector must be configured.", nameof(options));
        _registry = _options.ReplicaIdRegistry ?? new DefaultReplicaIdRegistry();
        ConsensusTransportOptions transportOptions = _options.Transport
            ?? throw new ArgumentException("Transport must be configured.", nameof(options));
        ValidateOptions(_options);

        RegisterKnownMembers();
        ulong localNodeId = _registry.GetNodeId(_options.LocalReplicaId);
        ulong[] cluster = BuildClusterNodeIds();
        _configuredNodeIds = new HashSet<ulong>(cluster);
        _members = BuildSortedMembers();
        _role = ConsensusRole.Follower;
        _transport = new CrdtRaftTransport(
            transportOptions,
            _registry,
            _options.LocalReplicaId,
            _options.MaxFrameLength);
        _node = new RaftNode(
            CreateRaftConfig(localNodeId),
            new RaftMemoryStorage(new RaftConfState(cluster)),
            _transport,
            new RaftNodeOptions { TickInterval = _options.TickInterval });
        _failureDetector.MembersChanged += OnFailureDetectorMembersChanged;
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
    public bool IsLeader => _node.IsLeader;

    /// <inheritdoc/>
    public IReadOnlyCollection<ReplicaId> Members => _members;

    /// <inheritdoc/>
    public async ValueTask StartAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        if (Interlocked.Exchange(ref _started, 1) == 1)
        {
            return;
        }

        await _failureDetector.StartAsync(ct).ConfigureAwait(false);
        await _node.StartAsync(ct).ConfigureAwait(false);
        _commitPump = PumpCommittedAsync(_stop.Token);
        _stateChangesPump = PumpStateChangesAsync(_stop.Token);
        _confPump = PumpConfigurationsAsync(_stop.Token);
    }

    /// <inheritdoc/>
    public async ValueTask<bool> ProposeAsync(ReadOnlyMemory<byte> entry, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        if (!IsLeader)
        {
            return false;
        }

        ulong sequence = unchecked((ulong)Interlocked.Increment(ref _nextSequence));
        var pending = new PendingProposal(sequence);
        lock (_gate)
        {
            _pending.Add(sequence, pending);
        }

        try
        {
            byte[] command = RaftProposalCodec.Encode(_node.Id, sequence, entry);
            await _node.ProposeAsync(command, ct).ConfigureAwait(false);
            return await WaitForResultAsync(pending.Completion.Task, ct).ConfigureAwait(false);
        }
        finally
        {
            lock (_gate)
            {
                _pending.Remove(sequence);
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

        _failureDetector.MembersChanged -= OnFailureDetectorMembersChanged;
        _stop.Cancel();
        await _node.DisposeAsync().ConfigureAwait(false);
        await AwaitLoopAsync(_commitPump).ConfigureAwait(false);
        await AwaitLoopAsync(_stateChangesPump).ConfigureAwait(false);
        await AwaitLoopAsync(_confPump).ConfigureAwait(false);
        CompletePending(false);
        if (_options.DisposeFailureDetector)
        {
            await _failureDetector.DisposeAsync().ConfigureAwait(false);
        }

        _stop.Dispose();
    }

    private static void ValidateOptions(RaftConsensusOptions options)
    {
        if (options.ElectionTick <= options.HeartbeatTick)
        {
            throw new ArgumentException("ElectionTick must be greater than HeartbeatTick.", nameof(options));
        }

        if (options.HeartbeatTick <= 0)
        {
            throw new ArgumentException("HeartbeatTick must be positive.", nameof(options));
        }

        if (options.MaxInflightMessages <= 0)
        {
            throw new ArgumentException("MaxInflightMessages must be positive.", nameof(options));
        }

        if (options.MaxInflightBytes == 0 || options.MaxUncommittedEntriesSize == 0)
        {
            throw new ArgumentException("Raft byte limits must be positive.", nameof(options));
        }

        if (options.TickInterval <= TimeSpan.Zero)
        {
            throw new ArgumentException("Raft timing intervals must be positive.", nameof(options));
        }

        if (options.MaxFrameLength <= 0)
        {
            throw new ArgumentException("MaxFrameLength must be positive.", nameof(options));
        }
    }

    private static ConsensusRole MapRole(RaftRole role)
    {
        return role switch
        {
            RaftRole.Leader => ConsensusRole.Leader,
            RaftRole.Candidate or RaftRole.PreCandidate => ConsensusRole.Candidate,
            _ => ConsensusRole.Follower,
        };
    }

    private global::Raft.RaftConfig CreateRaftConfig(ulong localNodeId)
    {
        return new global::Raft.RaftConfig
        {
            Id = localNodeId,
            ElectionTick = _options.ElectionTick,
            HeartbeatTick = _options.HeartbeatTick,
            PreVote = _options.PreVote,
            CheckQuorum = _options.CheckQuorum,
            MaxSizePerMessage = _options.MaxSizePerMessage,
            MaxInflightMessages = _options.MaxInflightMessages,
            MaxInflightBytes = _options.MaxInflightBytes,
            MaxUncommittedEntriesSize = _options.MaxUncommittedEntriesSize,
            DisableProposalForwarding = _options.DisableProposalForwarding,
            RandomizedElectionTimeout = _options.RandomizedElectionTimeout,
        };
    }

    private async Task PumpCommittedAsync(CancellationToken ct)
    {
        try
        {
            while (await _node.Committed.WaitToReadAsync(ct).ConfigureAwait(false))
            {
                while (_node.Committed.TryRead(out ReadOnlyMemory<byte> entry))
                {
                    CommitEntry(entry);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task PumpStateChangesAsync(CancellationToken ct)
    {
        try
        {
            while (await _node.StateChanges.WaitToReadAsync(ct).ConfigureAwait(false))
            {
                while (_node.StateChanges.TryRead(out RaftStateChange change))
                {
                    ApplyStateChange(change);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task PumpConfigurationsAsync(CancellationToken ct)
    {
        try
        {
            while (await _node.CommittedConfigurations.WaitToReadAsync(ct).ConfigureAwait(false))
            {
                while (_node.CommittedConfigurations.TryRead(out RaftConfState? confState) && confState is not null)
                {
                    ApplyCommittedConfiguration(confState);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void CommitEntry(ReadOnlyMemory<byte> entry)
    {
        if (!RaftProposalCodec.TryDecode(entry, out RaftProposal proposal))
        {
            EntryCommitted?.Invoke(entry);
            return;
        }

        if (proposal.OriginNodeId == _node.Id)
        {
            PendingProposal? pending;
            lock (_gate)
            {
                _pending.TryGetValue(proposal.Sequence, out pending);
            }

            pending?.Completion.TrySetResult(true);
        }

        EntryCommitted?.Invoke(proposal.Payload);
    }

    private void OnFailureDetectorMembersChanged()
    {
        RegisterKnownMembers();
        if (RefreshMembers())
        {
            MembersChanged?.Invoke();
        }

        if (IsLeader)
        {
            _ = ReconcileConfigurationAsync(CancellationToken.None);
        }
    }

    private bool RefreshMembers()
    {
        ReplicaId[] next = BuildSortedMembers();
        lock (_gate)
        {
            if (SameMembers(_members, next))
            {
                return false;
            }

            _members = next;
            return true;
        }
    }

    private void ApplyStateChange(RaftStateChange change)
    {
        ConsensusRole nextRole = MapRole(change.Role);
        ReplicaId? nextLeader = ResolveLeaderId(change.LeaderId);
        bool leaderChanged;
        lock (_gate)
        {
            leaderChanged = _leaderId != nextLeader;
            _role = nextRole;
            _leaderId = nextLeader;
        }

        if (leaderChanged)
        {
            LeadershipChanged?.Invoke(nextLeader);
        }

        if (nextRole == ConsensusRole.Leader)
        {
            _ = ReconcileConfigurationAsync(CancellationToken.None);
        }
    }

    private void ApplyCommittedConfiguration(RaftConfState confState)
    {
        lock (_gate)
        {
            _configuredNodeIds.Clear();
            foreach (ulong voter in confState.Voters)
            {
                _configuredNodeIds.Add(voter);
            }

            foreach (ulong voter in confState.VotersOutgoing)
            {
                _configuredNodeIds.Add(voter);
            }
        }

        if (IsLeader)
        {
            _ = ReconcileConfigurationAsync(CancellationToken.None);
        }
    }

    private ReplicaId? ResolveLeaderId(ulong leaderNodeId)
    {
        if (leaderNodeId == CrdtRaftTransport.BroadcastNodeId)
        {
            return null;
        }

        return _registry.TryGetReplicaId(leaderNodeId, out ReplicaId replicaId) ? replicaId : null;
    }

    private async Task ReconcileConfigurationAsync(CancellationToken ct)
    {
        if (Interlocked.Exchange(ref _reconciling, 1) == 1)
        {
            return;
        }

        try
        {
            if (!IsLeader || Volatile.Read(ref _disposed) == 1)
            {
                return;
            }

            ulong[] desired = BuildClusterNodeIds();
            List<RaftConfChangeSingle> changes = BuildConfigurationChanges(desired);
            if (changes.Count == 0)
            {
                return;
            }

            bool joint = changes.Count > 1;
            var change = new RaftConfChangeV2(changes, useJoint: joint, autoLeave: joint);
            await _node.ChangeConfigurationAsync(change, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        finally
        {
            Volatile.Write(ref _reconciling, 0);
        }
    }

    private List<RaftConfChangeSingle> BuildConfigurationChanges(ulong[] desired)
    {
        var changes = new List<RaftConfChangeSingle>();
        var desiredSet = new HashSet<ulong>(desired);
        lock (_gate)
        {
            foreach (ulong nodeId in desiredSet)
            {
                if (!_configuredNodeIds.Contains(nodeId))
                {
                    changes.Add(new RaftConfChangeSingle(RaftConfChangeType.AddNode, nodeId));
                }
            }

            foreach (ulong nodeId in _configuredNodeIds)
            {
                if (!desiredSet.Contains(nodeId))
                {
                    changes.Add(new RaftConfChangeSingle(RaftConfChangeType.RemoveNode, nodeId));
                }
            }
        }

        return changes;
    }

    private void RegisterKnownMembers()
    {
        _registry.GetNodeId(_options.LocalReplicaId);
        foreach (ReplicaId member in _failureDetector.Members)
        {
            _registry.GetNodeId(member);
        }
    }

    private ulong[] BuildClusterNodeIds()
    {
        ReplicaId[] members = BuildSortedMembers();
        var ids = new ulong[members.Length];
        for (int i = 0; i < members.Length; i++)
        {
            ids[i] = _registry.GetNodeId(members[i]);
        }

        Array.Sort(ids);
        return ids;
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

    private static bool SameMembers(ReplicaId[] left, ReplicaId[] right)
    {
        if (left.Length != right.Length)
        {
            return false;
        }

        for (int i = 0; i < left.Length; i++)
        {
            if (left[i] != right[i])
            {
                return false;
            }
        }

        return true;
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

    private static async Task AwaitLoopAsync(Task? loop)
    {
        if (loop is null)
        {
            return;
        }

        try
        {
            await loop.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void CompletePending(bool result)
    {
        List<PendingProposal> pending;
        lock (_gate)
        {
            pending = new List<PendingProposal>(_pending.Values);
            _pending.Clear();
        }

        foreach (PendingProposal proposal in pending)
        {
            proposal.Completion.TrySetResult(result);
        }
    }

    private void ThrowIfDisposed()
    {
#if NET8_0_OR_GREATER
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) == 1, this);
#else
        if (Volatile.Read(ref _disposed) == 1)
        {
            throw new ObjectDisposedException(nameof(RaftConsensus));
        }
#endif
    }

    private sealed class PendingProposal
    {
        public PendingProposal(ulong sequence)
        {
            Sequence = sequence;
            Completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public ulong Sequence { get; }

        public TaskCompletionSource<bool> Completion { get; }
    }
}
