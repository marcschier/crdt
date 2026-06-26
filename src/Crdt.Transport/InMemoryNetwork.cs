// Copyright (c) marcschier. Licensed under the MIT License.

namespace Crdt.Transport;

/// <summary>Provides an in-process registry of <see cref="InMemoryTransport"/> peers.</summary>
public sealed class InMemoryNetwork : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly List<InMemoryTransport> _transports = [];

    /// <summary>Creates a transport attached to this network.</summary>
    /// <returns>The created transport.</returns>
    public InMemoryTransport CreateTransport() => new(this);

    /// <summary>Waits until all currently queued in-memory frames have been pumped.</summary>
    /// <param name="ct">A token that cancels the wait.</param>
    /// <returns>A task that completes once the network is idle.</returns>
    public async ValueTask DrainAsync(CancellationToken ct = default)
    {
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            InMemoryTransport[] transports = Snapshot();
            bool idle = true;
            foreach (InMemoryTransport transport in transports)
            {
                idle &= transport.PendingFrames == 0;
            }

            if (idle)
            {
                return;
            }

            await Task.Yield();
        }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        InMemoryTransport[] transports = Snapshot();
        foreach (InMemoryTransport transport in transports)
        {
            await transport.DisposeAsync().ConfigureAwait(false);
        }
    }

    internal void Register(InMemoryTransport transport)
    {
        lock (_gate)
        {
            if (!_transports.Contains(transport))
            {
                _transports.Add(transport);
            }
        }
    }

    internal void Unregister(InMemoryTransport transport)
    {
        lock (_gate)
        {
            _transports.Remove(transport);
        }
    }

    internal InMemoryTransport[] PeersExcept(InMemoryTransport sender)
    {
        lock (_gate)
        {
            var peers = new List<InMemoryTransport>(_transports.Count);
            foreach (InMemoryTransport transport in _transports)
            {
                if (!ReferenceEquals(sender, transport))
                {
                    peers.Add(transport);
                }
            }

            return [.. peers];
        }
    }

    private InMemoryTransport[] Snapshot()
    {
        lock (_gate)
        {
            return [.. _transports];
        }
    }
}
