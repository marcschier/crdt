// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;

namespace Crdt;

/// <summary>
/// An add-only monotonic directed acyclic graph CRDT with vertices stored in a G-Set and edges
/// stored in a G-Set. Local edge additions are accepted only when they preserve acyclicity in
/// the current local graph.
/// </summary>
/// <typeparam name="TVertex">The vertex type; must be non-null and have value equality.</typeparam>
/// <remarks>
/// Because edges are grow-only and added independently per replica, merging two acyclic graphs can
/// introduce a cycle when replicas concurrently add edges that jointly form one. Correct usage
/// requires edges to respect a consistent global partial order, the standard monotonic-DAG
/// discipline. Use <see cref="HasCycle"/> and <see cref="TopologicalSort"/> to detect violations.
/// </remarks>
public sealed class AddOnlyDag<TVertex> :
    IConvergent<AddOnlyDag<TVertex>>,
    IDeltaConvergent<AddOnlyDag<TVertex>, AddOnlyDag<TVertex>>,
    IOperationConvergent<AddOnlyDagOperation<TVertex>>,
    IEquatable<AddOnlyDag<TVertex>>
    where TVertex : notnull
{
    private readonly GSet<TVertex> _vertices;
    private readonly GSet<Edge<TVertex>> _edges;

    /// <summary>Initializes an empty add-only DAG.</summary>
    public AddOnlyDag()
        : this(new GSet<TVertex>(), new GSet<Edge<TVertex>>())
    {
    }

    private AddOnlyDag(GSet<TVertex> vertices, GSet<Edge<TVertex>> edges)
    {
        _vertices = vertices;
        _edges = edges;
    }

    /// <summary>Gets the number of vertices currently present.</summary>
    public int VertexCount => _vertices.Count;

    /// <summary>Gets the number of edges whose endpoints are currently present.</summary>
    public int EdgeCount => Edges().Count;

    /// <summary>Determines whether <paramref name="vertex"/> is present.</summary>
    /// <param name="vertex">The vertex to test.</param>
    /// <returns><see langword="true"/> if the vertex is present.</returns>
    public bool ContainsVertex(TVertex vertex) => _vertices.Contains(vertex);

    /// <summary>
    /// Determines whether the directed edge from <paramref name="source"/> to <paramref name="target"/> is
    /// visible.
    /// </summary>
    /// <param name="source">The edge source vertex.</param>
    /// <param name="target">The edge target vertex.</param>
    /// <returns><see langword="true"/> if the edge and both endpoints are present.</returns>
    public bool ContainsEdge(TVertex source, TVertex target)
    {
        var edge = new Edge<TVertex>(source, target);
        return _edges.Contains(edge) && ContainsVertex(source) && ContainsVertex(target);
    }

    /// <summary>Gets the currently present vertices.</summary>
    /// <returns>The vertices.</returns>
    public IReadOnlyCollection<TVertex> Vertices() => _vertices.Elements;

    /// <summary>Gets the edges whose endpoints are currently present.</summary>
    /// <returns>The visible edges.</returns>
    public IReadOnlyCollection<Edge<TVertex>> Edges()
    {
        var edges = new List<Edge<TVertex>>();
        foreach (Edge<TVertex> edge in _edges.Elements)
        {
            if (ContainsVertex(edge.Source) && ContainsVertex(edge.Target))
            {
                edges.Add(edge);
            }
        }

        return edges;
    }

    /// <summary>Adds <paramref name="vertex"/> and returns the operation to broadcast.</summary>
    /// <param name="vertex">The vertex to add.</param>
    /// <returns>The add-vertex operation.</returns>
    public AddOnlyDagOperation<TVertex> AddVertex(TVertex vertex)
    {
        _vertices.Add(vertex);
        return AddOnlyDagOperation<TVertex>.AddVertex(vertex);
    }

    /// <summary>Adds an acyclic directed edge between two currently-present endpoints.</summary>
    /// <param name="source">The edge source vertex.</param>
    /// <param name="target">The edge target vertex.</param>
    /// <returns>The add-edge operation.</returns>
    /// <exception cref="InvalidOperationException">
    /// An endpoint is missing, or adding the edge would create a cycle.
    /// </exception>
    public AddOnlyDagOperation<TVertex> AddEdge(TVertex source, TVertex target)
    {
        if (!ContainsVertex(source) || !ContainsVertex(target))
        {
            throw new InvalidOperationException("Both endpoints must be present before adding an edge.");
        }

        if (EqualityComparer<TVertex>.Default.Equals(source, target) || CanReach(target, source))
        {
            throw new InvalidOperationException("Adding the edge would create a cycle.");
        }

        _edges.Add(new Edge<TVertex>(source, target));
        return AddOnlyDagOperation<TVertex>.AddEdge(source, target);
    }

    /// <summary>Determines whether the currently visible graph contains a directed cycle.</summary>
    /// <returns><see langword="true"/> if a cycle exists.</returns>
    public bool HasCycle()
    {
        Dictionary<TVertex, int> state = [];
        Dictionary<TVertex, List<TVertex>> adjacency = BuildAdjacency();
        foreach (TVertex vertex in _vertices.Elements)
        {
            if (Visit(vertex, adjacency, state))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Returns vertices in a valid topological order.</summary>
    /// <returns>The topologically sorted vertices.</returns>
    /// <exception cref="InvalidOperationException">The graph contains a cycle.</exception>
    public IReadOnlyList<TVertex> TopologicalSort()
    {
        Dictionary<TVertex, List<TVertex>> adjacency = BuildAdjacency();
        Dictionary<TVertex, int> indegrees = [];
        foreach (TVertex vertex in _vertices.Elements)
        {
            indegrees[vertex] = 0;
        }

        foreach (Edge<TVertex> edge in Edges())
        {
            indegrees[edge.Target]++;
        }

        var ready = new Queue<TVertex>();
        foreach (KeyValuePair<TVertex, int> pair in indegrees)
        {
            if (pair.Value == 0)
            {
                ready.Enqueue(pair.Key);
            }
        }

        var sorted = new List<TVertex>();
        while (ready.Count > 0)
        {
            TVertex vertex = ready.Dequeue();
            sorted.Add(vertex);
            if (!adjacency.TryGetValue(vertex, out List<TVertex>? targets))
            {
                continue;
            }

            foreach (TVertex target in targets)
            {
                indegrees[target]--;
                if (indegrees[target] == 0)
                {
                    ready.Enqueue(target);
                }
            }
        }

        if (sorted.Count != _vertices.Count)
        {
            throw new InvalidOperationException("The graph contains a cycle.");
        }

        return sorted;
    }

    /// <inheritdoc/>
    public void Merge(AddOnlyDag<TVertex> other)
    {
        Throw.IfNull(other);
        _vertices.Merge(other._vertices);
        _edges.Merge(other._edges);
    }

    /// <inheritdoc/>
    public CrdtOrder Compare(AddOnlyDag<TVertex> other)
    {
        Throw.IfNull(other);
        return CombineOrders(_vertices.Compare(other._vertices), _edges.Compare(other._edges));
    }

    /// <inheritdoc/>
    public AddOnlyDag<TVertex> Clone() => new(_vertices.Clone(), _edges.Clone());

    /// <inheritdoc/>
    public bool TryExtractDelta([MaybeNullWhen(false)] out AddOnlyDag<TVertex> delta)
    {
        bool hasVertexDelta = _vertices.TryExtractDelta(out GSet<TVertex>? vertexDelta);
        bool hasEdgeDelta = _edges.TryExtractDelta(out GSet<Edge<TVertex>>? edgeDelta);
        if (!hasVertexDelta && !hasEdgeDelta)
        {
            delta = null;
            return false;
        }

        delta = new AddOnlyDag<TVertex>(vertexDelta ?? new GSet<TVertex>(), edgeDelta ?? new GSet<Edge<TVertex>>());
        return true;
    }

    /// <inheritdoc/>
    public void MergeDelta(AddOnlyDag<TVertex> delta)
    {
        Throw.IfNull(delta);
        Merge(delta);
    }

    /// <inheritdoc/>
    public bool Apply(AddOnlyDagOperation<TVertex> operation)
    {
        return operation.Kind switch
        {
            AddOnlyDagOperationKind.AddVertex => _vertices.Apply(new GSetOperation<TVertex>(operation.Vertex)),
            AddOnlyDagOperationKind.AddEdge => _edges.Apply(
                new GSetOperation<Edge<TVertex>>(new Edge<TVertex>(operation.Source, operation.Target))),
            _ => false,
        };
    }

    /// <summary>Serializes the DAG to the binary format using <paramref name="vertexSerializer"/>.</summary>
    /// <param name="output">The destination buffer writer.</param>
    /// <param name="vertexSerializer">The vertex serializer.</param>
    public void WriteTo(IBufferWriter<byte> output, ICrdtValueSerializer<TVertex> vertexSerializer)
    {
        Throw.IfNull(output);
        Throw.IfNull(vertexSerializer);
        var writer = new CrdtWriter(output);
        var edgeSerializer = new EdgeCrdtValueSerializer<TVertex>(vertexSerializer);
        _vertices.Write(ref writer, vertexSerializer);
        _edges.Write(ref writer, edgeSerializer);
    }

    /// <summary>Serializes the DAG to a new byte array using <paramref name="vertexSerializer"/>.</summary>
    /// <param name="vertexSerializer">The vertex serializer.</param>
    /// <returns>The encoded bytes.</returns>
    public byte[] ToByteArray(ICrdtValueSerializer<TVertex> vertexSerializer)
    {
        using var buffer = new PooledBufferWriter();
        WriteTo(buffer, vertexSerializer);
        return buffer.WrittenSpan.ToArray();
    }

    /// <summary>Decodes a DAG from the binary format using <paramref name="vertexSerializer"/>.</summary>
    /// <param name="data">The encoded bytes.</param>
    /// <param name="vertexSerializer">The vertex serializer.</param>
    /// <param name="options">Optional decoding limits.</param>
    /// <returns>The decoded DAG.</returns>
    public static AddOnlyDag<TVertex> ReadFrom(
        ReadOnlySpan<byte> data,
        ICrdtValueSerializer<TVertex> vertexSerializer,
        CrdtReaderOptions? options = null)
    {
        Throw.IfNull(vertexSerializer);
        var reader = new CrdtReader(data, options);
        var edgeSerializer = new EdgeCrdtValueSerializer<TVertex>(vertexSerializer);
        return new AddOnlyDag<TVertex>(
            GSet<TVertex>.Read(ref reader, vertexSerializer),
            GSet<Edge<TVertex>>.Read(ref reader, edgeSerializer));
    }

    /// <summary>Serializes the DAG to JSON using <paramref name="vertexSerializer"/>.</summary>
    /// <param name="vertexSerializer">The vertex serializer.</param>
    /// <returns>The JSON string.</returns>
    public string ToJson(ICrdtValueSerializer<TVertex> vertexSerializer)
    {
        Throw.IfNull(vertexSerializer);
        var edgeSerializer = new EdgeCrdtValueSerializer<TVertex>(vertexSerializer);
        using var buffer = new PooledBufferWriter();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WritePropertyName("vertices");
            WriteJsonValue(writer, _vertices.ToJson(vertexSerializer));
            writer.WritePropertyName("edges");
            WriteJsonValue(writer, _edges.ToJson(edgeSerializer));
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    /// <summary>Deserializes a DAG from JSON using <paramref name="vertexSerializer"/>.</summary>
    /// <param name="json">The JSON string.</param>
    /// <param name="vertexSerializer">The vertex serializer.</param>
    /// <returns>The decoded DAG.</returns>
    public static AddOnlyDag<TVertex> FromJson(string json, ICrdtValueSerializer<TVertex> vertexSerializer)
    {
        Throw.IfNull(json);
        Throw.IfNull(vertexSerializer);
        var edgeSerializer = new EdgeCrdtValueSerializer<TVertex>(vertexSerializer);
        var vertices = new GSet<TVertex>();
        var edges = new GSet<Edge<TVertex>>();
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json));
        reader.Read();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            string? name = reader.GetString();
            reader.Read();
            using JsonDocument document = JsonDocument.ParseValue(ref reader);
            if (name == "vertices")
            {
                vertices = GSet<TVertex>.FromJson(document.RootElement.GetRawText(), vertexSerializer);
            }
            else if (name == "edges")
            {
                edges = GSet<Edge<TVertex>>.FromJson(document.RootElement.GetRawText(), edgeSerializer);
            }
        }

        return new AddOnlyDag<TVertex>(vertices, edges);
    }

    /// <inheritdoc/>
    public bool Equals(AddOnlyDag<TVertex>? other) =>
        other is not null && _vertices.Equals(other._vertices) && _edges.Equals(other._edges);

    /// <inheritdoc/>
    public override bool Equals([NotNullWhen(true)] object? obj) => Equals(obj as AddOnlyDag<TVertex>);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(_vertices, _edges);

    private bool CanReach(TVertex source, TVertex target)
    {
        var stack = new Stack<TVertex>();
        HashSet<TVertex> visited = [];
        stack.Push(source);
        while (stack.Count > 0)
        {
            TVertex current = stack.Pop();
            if (!visited.Add(current))
            {
                continue;
            }

            if (EqualityComparer<TVertex>.Default.Equals(current, target))
            {
                return true;
            }

            foreach (Edge<TVertex> edge in Edges())
            {
                if (EqualityComparer<TVertex>.Default.Equals(edge.Source, current))
                {
                    stack.Push(edge.Target);
                }
            }
        }

        return false;
    }

    private Dictionary<TVertex, List<TVertex>> BuildAdjacency()
    {
        Dictionary<TVertex, List<TVertex>> adjacency = [];
        foreach (TVertex vertex in _vertices.Elements)
        {
            adjacency[vertex] = [];
        }

        foreach (Edge<TVertex> edge in Edges())
        {
            adjacency[edge.Source].Add(edge.Target);
        }

        return adjacency;
    }

    private static bool Visit(
        TVertex vertex,
        Dictionary<TVertex, List<TVertex>> adjacency,
        Dictionary<TVertex, int> state)
    {
        if (state.TryGetValue(vertex, out int existing))
        {
            return existing == 1;
        }

        state[vertex] = 1;
        if (adjacency.TryGetValue(vertex, out List<TVertex>? targets))
        {
            foreach (TVertex target in targets)
            {
                if (Visit(target, adjacency, state))
                {
                    return true;
                }
            }
        }

        state[vertex] = 2;
        return false;
    }

    private static void WriteJsonValue(Utf8JsonWriter writer, string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        document.RootElement.WriteTo(writer);
    }

    private static CrdtOrder CombineOrders(CrdtOrder left, CrdtOrder right)
    {
        if (left == CrdtOrder.Equal)
        {
            return right;
        }

        if (right == CrdtOrder.Equal)
        {
            return left;
        }

        return left == right ? left : CrdtOrder.Concurrent;
    }
}
