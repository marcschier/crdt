// Copyright (c) marcschier. Licensed under the MIT License.

namespace Crdt;

/// <summary>
/// A dot store: the shared kernel behind observed-remove CRDTs (OR-Set, OR-Map,
/// multi-value register, enable/disable-wins flags). It maps each live value to the
/// <see cref="Dot"/> that introduced it and tracks a <see cref="DotContext"/> of all
/// observed dots. Causal merge keeps a value if and only if either replica still has its
/// dot, or neither has observed it — yielding add-wins, tombstone-free semantics.
/// </summary>
/// <typeparam name="TValue">The payload associated with each dot.</typeparam>
/// <remarks>Mutable and not thread-safe.</remarks>
internal sealed class DotKernel<TValue>
{
    private readonly Dictionary<Dot, TValue> _entries;

    public DotKernel()
    {
        _entries = [];
        Context = new DotContext();
    }

    private DotKernel(Dictionary<Dot, TValue> entries, DotContext context)
    {
        _entries = entries;
        Context = context;
    }

    public int Count => _entries.Count;

    public DotContext Context { get; }

    public IReadOnlyDictionary<Dot, TValue> Entries => _entries;

    public IEnumerable<TValue> Values => _entries.Values;

    /// <summary>Adds <paramref name="value"/> under a fresh dot from <paramref name="replica"/>.</summary>
    /// <returns>The dot that now identifies the value.</returns>
    public Dot Add(ReplicaId replica, TValue value)
    {
        Dot dot = Context.NextDot(replica);
        _entries[dot] = value;
        return dot;
    }

    /// <summary>Inserts an externally-created (dot, value) pair, recording the dot as observed.</summary>
    public void Insert(Dot dot, TValue value)
    {
        _entries[dot] = value;
        Context.Add(dot);
    }

    /// <summary>Removes the value identified by <paramref name="dot"/> (the dot stays observed).</summary>
    public bool RemoveDot(Dot dot) => _entries.Remove(dot);

    /// <summary>Removes every live value, leaving the causal context intact.</summary>
    public void Clear() => _entries.Clear();

    /// <summary>
    /// Causally joins <paramref name="other"/> into this kernel. Implements the ORSWOT merge:
    /// drop a local value the other side has causally removed; absorb a remote value we have
    /// not yet observed; keep values present on both sides; then merge causal contexts.
    /// </summary>
    public void Merge(DotKernel<TValue> other)
    {
        // 1. Local dots the other side has observed-and-removed must be dropped.
        var toRemove = new List<Dot>();
        foreach (KeyValuePair<Dot, TValue> entry in _entries)
        {
            if (!other._entries.ContainsKey(entry.Key) && other.Context.Contains(entry.Key))
            {
                toRemove.Add(entry.Key);
            }
        }

        foreach (Dot dot in toRemove)
        {
            _entries.Remove(dot);
        }

        // 2. Remote dots we have neither seen nor removed must be absorbed.
        foreach (KeyValuePair<Dot, TValue> entry in other._entries)
        {
            if (!_entries.ContainsKey(entry.Key) && !Context.Contains(entry.Key))
            {
                _entries[entry.Key] = entry.Value;
            }
        }

        // 3. Merge causal contexts.
        Context.Merge(other.Context);
    }

    public DotKernel<TValue> Clone() =>
        new(new Dictionary<Dot, TValue>(_entries), Context.Clone());

    /// <summary>The live entries in canonical (dot-sorted) order for deterministic serialization.</summary>
    public IEnumerable<KeyValuePair<Dot, TValue>> SortedEntries()
    {
        var list = new List<KeyValuePair<Dot, TValue>>(_entries);
        list.Sort(static (left, right) => left.Key.CompareTo(right.Key));
        return list;
    }
}
