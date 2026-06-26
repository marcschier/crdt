// Copyright (c) marcschier. Licensed under the MIT License.

namespace Crdt;

/// <summary>Identifies the kind of change described by a <see cref="TwoPTwoPGraphOperation{TVertex}"/>.</summary>
public enum TwoPTwoPGraphOperationKind
{
    /// <summary>Adds a vertex to the graph's vertex 2P-Set.</summary>
    AddVertex = 0,

    /// <summary>Removes a vertex from the graph's vertex 2P-Set.</summary>
    RemoveVertex = 1,

    /// <summary>Adds an edge to the graph's edge 2P-Set.</summary>
    AddEdge = 2,

    /// <summary>Removes an edge from the graph's edge 2P-Set.</summary>
    RemoveEdge = 3,
}

/// <summary>Describes an idempotent vertex or edge operation for a <see cref="TwoPTwoPGraph{TVertex}"/>.</summary>
/// <typeparam name="TVertex">The vertex type.</typeparam>
public readonly struct TwoPTwoPGraphOperation<TVertex>
    where TVertex : notnull
{
    private TwoPTwoPGraphOperation(
        TwoPTwoPGraphOperationKind kind,
        TVertex vertex,
        TVertex source,
        TVertex target)
    {
        Kind = kind;
        Vertex = vertex;
        Source = source;
        Target = target;
    }

    /// <summary>Gets the operation kind.</summary>
    public TwoPTwoPGraphOperationKind Kind { get; }

    /// <summary>Gets the vertex payload for vertex operations.</summary>
    public TVertex Vertex { get; }

    /// <summary>Gets the edge source payload for edge operations.</summary>
    public TVertex Source { get; }

    /// <summary>Gets the edge target payload for edge operations.</summary>
    public TVertex Target { get; }

    /// <summary>Creates an operation that adds <paramref name="vertex"/>.</summary>
    /// <param name="vertex">The vertex to add.</param>
    /// <returns>The add-vertex operation.</returns>
    public static TwoPTwoPGraphOperation<TVertex> AddVertex(TVertex vertex)
    {
        Throw.IfNull(vertex);
        return new TwoPTwoPGraphOperation<TVertex>(
            TwoPTwoPGraphOperationKind.AddVertex,
            vertex,
            default!,
            default!);
    }

    /// <summary>Creates an operation that removes <paramref name="vertex"/>.</summary>
    /// <param name="vertex">The vertex to remove.</param>
    /// <returns>The remove-vertex operation.</returns>
    public static TwoPTwoPGraphOperation<TVertex> RemoveVertex(TVertex vertex)
    {
        Throw.IfNull(vertex);
        return new TwoPTwoPGraphOperation<TVertex>(
            TwoPTwoPGraphOperationKind.RemoveVertex,
            vertex,
            default!,
            default!);
    }

    /// <summary>
    /// Creates an operation that adds the edge from <paramref name="source"/> to <paramref name="target"/>.
    /// </summary>
    /// <param name="source">The edge source vertex.</param>
    /// <param name="target">The edge target vertex.</param>
    /// <returns>The add-edge operation.</returns>
    public static TwoPTwoPGraphOperation<TVertex> AddEdge(TVertex source, TVertex target)
    {
        Throw.IfNull(source);
        Throw.IfNull(target);
        return new TwoPTwoPGraphOperation<TVertex>(
            TwoPTwoPGraphOperationKind.AddEdge,
            default!,
            source,
            target);
    }

    /// <summary>
    /// Creates an operation that removes the edge from <paramref name="source"/> to <paramref name="target"/>.
    /// </summary>
    /// <param name="source">The edge source vertex.</param>
    /// <param name="target">The edge target vertex.</param>
    /// <returns>The remove-edge operation.</returns>
    public static TwoPTwoPGraphOperation<TVertex> RemoveEdge(TVertex source, TVertex target)
    {
        Throw.IfNull(source);
        Throw.IfNull(target);
        return new TwoPTwoPGraphOperation<TVertex>(
            TwoPTwoPGraphOperationKind.RemoveEdge,
            default!,
            source,
            target);
    }
}
