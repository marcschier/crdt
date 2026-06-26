// Copyright (c) marcschier. Licensed under the MIT License.

namespace Crdt.Tests.Testing;

/// <summary>Raised by <see cref="CrdtLaws"/> when a CRDT violates an algebraic law.</summary>
internal sealed class CrdtLawViolationException : Exception
{
    public CrdtLawViolationException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// Reusable, framework-agnostic verification of the join-semilattice laws every state-based
/// and delta-state CRDT must satisfy: idempotence, commutativity, and associativity of
/// <see cref="IConvergent{TSelf}.Merge"/>, plus order-independent convergence. Methods throw
/// <see cref="CrdtLawViolationException"/> on a violation, so they compose into any test.
/// </summary>
internal static class CrdtLaws
{
    /// <summary>Logical equality via the type's own partial-order comparison.</summary>
    public static bool DefaultEquals<T>(T left, T right)
        where T : IConvergent<T> => left.Compare(right) == CrdtOrder.Equal;

    /// <summary>Verifies <c>merge(a, a) == a</c>.</summary>
    public static void AssertIdempotent<T>(T value, Func<T, T, bool>? areEqual = null)
        where T : IConvergent<T>
    {
        areEqual ??= DefaultEquals;
        T merged = value.Clone();
        merged.Merge(value.Clone());
        if (!areEqual(merged, value))
        {
            throw new CrdtLawViolationException("Idempotence violated: merge(a, a) != a.");
        }
    }

    /// <summary>Verifies <c>merge(a, b) == merge(b, a)</c>.</summary>
    public static void AssertCommutative<T>(T a, T b, Func<T, T, bool>? areEqual = null)
        where T : IConvergent<T>
    {
        areEqual ??= DefaultEquals;
        T ab = a.Clone();
        ab.Merge(b.Clone());
        T ba = b.Clone();
        ba.Merge(a.Clone());
        if (!areEqual(ab, ba))
        {
            throw new CrdtLawViolationException("Commutativity violated: merge(a, b) != merge(b, a).");
        }
    }

    /// <summary>Verifies <c>merge(merge(a, b), c) == merge(a, merge(b, c))</c>.</summary>
    public static void AssertAssociative<T>(T a, T b, T c, Func<T, T, bool>? areEqual = null)
        where T : IConvergent<T>
    {
        areEqual ??= DefaultEquals;
        T left = a.Clone();
        left.Merge(b.Clone());
        left.Merge(c.Clone());

        T bc = b.Clone();
        bc.Merge(c.Clone());
        T right = a.Clone();
        right.Merge(bc);

        if (!areEqual(left, right))
        {
            throw new CrdtLawViolationException(
                "Associativity violated: merge(merge(a, b), c) != merge(a, merge(b, c)).");
        }
    }

    /// <summary>Runs the full semilattice law suite over three representative states.</summary>
    public static void AssertSemilattice<T>(T a, T b, T c, Func<T, T, bool>? areEqual = null)
        where T : IConvergent<T>
    {
        AssertIdempotent(a, areEqual);
        AssertIdempotent(b, areEqual);
        AssertIdempotent(c, areEqual);
        AssertCommutative(a, b, areEqual);
        AssertCommutative(a, c, areEqual);
        AssertCommutative(b, c, areEqual);
        AssertAssociative(a, b, c, areEqual);
    }

    /// <summary>
    /// Verifies that merging the given replica states in several different orders all yield the
    /// same converged state (a practical convergence check beyond the pairwise laws).
    /// </summary>
    public static void AssertConverges<T>(IReadOnlyList<T> replicas, Func<T, T, bool>? areEqual = null)
        where T : IConvergent<T>
    {
        areEqual ??= DefaultEquals;
        if (replicas.Count == 0)
        {
            return;
        }

        T forward = MergeAll(replicas, Enumerable.Range(0, replicas.Count));
        T reverse = MergeAll(replicas, Enumerable.Range(0, replicas.Count).Reverse());
        T rotated = MergeAll(replicas, Rotate(replicas.Count));

        if (!areEqual(forward, reverse) || !areEqual(forward, rotated))
        {
            throw new CrdtLawViolationException("Convergence violated: merge order affected the result.");
        }
    }

    private static T MergeAll<T>(IReadOnlyList<T> replicas, IEnumerable<int> order)
        where T : IConvergent<T>
    {
        T? acc = default;
        foreach (int index in order)
        {
            if (acc is null)
            {
                acc = replicas[index].Clone();
            }
            else
            {
                acc.Merge(replicas[index].Clone());
            }
        }

        return acc!;
    }

    private static IEnumerable<int> Rotate(int count)
    {
        for (int i = 0; i < count; i++)
        {
            yield return (i + count / 2) % count;
        }
    }
}
