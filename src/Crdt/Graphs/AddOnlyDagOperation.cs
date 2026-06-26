// Copyright (c) marcschier. Licensed under the MIT License.

namespace Crdt;

/// <summary>Identifies the kind of change described by an <see cref="AddOnlyDagOperation{TVertex}"/>.</summary>
public enum AddOnlyDagOperationKind
{
    /// <summary>Adds a vertex to the DAG's vertex G-Set.</summary>
    AddVertex = 0,

    /// <summary>Adds an edge to the DAG's edge G-Set.</summary>
    AddEdge = 1,
}

/// <summary>Describes an idempotent vertex or edge operation for an <see cref="AddOnlyDag{TVertex}"/>.</summary>
/// <typeparam name="TVertex">The vertex type.</typeparam>
public readonly struct AddOnlyDagOperation<TVertex>
    where TVertex : notnull
{
    private AddOnlyDagOperation(AddOnlyDagOperationKind kind, TVertex vertex, TVertex source, TVertex target)
    {
        Kind = kind;
        Vertex = vertex;
        Source = source;
        Target = target;
    }

    /// <summary>Gets the operation kind.</summary>
    public AddOnlyDagOperationKind Kind { get; }

    /// <summary>Gets the vertex payload for vertex operations.</summary>
    public TVertex Vertex { get; }

    /// <summary>Gets the edge source payload for edge operations.</summary>
    public TVertex Source { get; }

    /// <summary>Gets the edge target payload for edge operations.</summary>
    public TVertex Target { get; }

    /// <summary>Creates an operation that adds <paramref name="vertex"/>.</summary>
    /// <param name="vertex">The vertex to add.</param>
    /// <returns>The add-vertex operation.</returns>
    public static AddOnlyDagOperation<TVertex> AddVertex(TVertex vertex)
    {
        Throw.IfNull(vertex);
        return new AddOnlyDagOperation<TVertex>(AddOnlyDagOperationKind.AddVertex, vertex, default!, default!);
    }

    /// <summary>
    /// Creates an operation that adds the edge from <paramref name="source"/> to <paramref name="target"/>.
    /// </summary>
    /// <param name="source">The edge source vertex.</param>
    /// <param name="target">The edge target vertex.</param>
    /// <returns>The add-edge operation.</returns>
    public static AddOnlyDagOperation<TVertex> AddEdge(TVertex source, TVertex target)
    {
        Throw.IfNull(source);
        Throw.IfNull(target);
        return new AddOnlyDagOperation<TVertex>(AddOnlyDagOperationKind.AddEdge, default!, source, target);
    }
}
