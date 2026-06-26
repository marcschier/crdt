// Copyright (c) marcschier. Licensed under the MIT License.

namespace Crdt.Tests.Testing;

/// <summary>
/// Drives operation-based CRDT replicas through hostile delivery schedules — arbitrary
/// reordering and duplication of broadcast operations — and checks that every replica still
/// converges. This exercises the idempotent/commutative requirements of
/// <see cref="IOperationConvergent{TOperation}.Apply"/> beyond happy-path delivery.
/// </summary>
/// <typeparam name="TReplica">The replica CRDT type.</typeparam>
/// <typeparam name="TOperation">The operation type.</typeparam>
internal sealed class OperationDeliverySimulator<TReplica, TOperation>
    where TReplica : IOperationConvergent<TOperation>
{
    private readonly TReplica[] _replicas;
    private readonly List<(int Source, TOperation Operation)> _pending = [];

    public OperationDeliverySimulator(params TReplica[] replicas)
    {
        if (replicas.Length == 0)
        {
            throw new ArgumentException("At least one replica is required.", nameof(replicas));
        }

        _replicas = replicas;
    }

    public int Count => _replicas.Length;

    public TReplica this[int index] => _replicas[index];

    /// <summary>
    /// Records an operation that <paramref name="source"/> produced locally (and has already
    /// applied itself) so it can later be delivered to the other replicas.
    /// </summary>
    public void Broadcast(int source, TOperation operation) => _pending.Add((source, operation));

    /// <summary>
    /// Delivers every pending operation to all replicas other than its source, in a shuffled
    /// order and (optionally) duplicated, then clears the pending queue.
    /// </summary>
    /// <param name="seed">Seed for the deterministic shuffle.</param>
    /// <param name="duplicate">When true, every operation is delivered twice.</param>
    public void DeliverAll(int seed = 1, bool duplicate = false)
    {
        var deliveries = new List<(int Target, TOperation Operation)>();
        foreach ((int source, TOperation operation) in _pending)
        {
            for (int target = 0; target < _replicas.Length; target++)
            {
                if (target == source)
                {
                    continue;
                }

                deliveries.Add((target, operation));
                if (duplicate)
                {
                    deliveries.Add((target, operation));
                }
            }
        }

        Shuffle(deliveries, new Random(seed));

        foreach ((int target, TOperation operation) in deliveries)
        {
            _replicas[target].Apply(operation);
        }

        _pending.Clear();
    }

    /// <summary>Throws unless every replica is logically equal to replica 0.</summary>
    public void AssertConverged(Func<TReplica, TReplica, bool> areEqual)
    {
        for (int i = 1; i < _replicas.Length; i++)
        {
            if (!areEqual(_replicas[0], _replicas[i]))
            {
                throw new CrdtLawViolationException($"Replica 0 and replica {i} did not converge.");
            }
        }
    }

    private static void Shuffle<TItem>(List<TItem> items, Random random)
    {
        for (int i = items.Count - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            (items[i], items[j]) = (items[j], items[i]);
        }
    }
}
