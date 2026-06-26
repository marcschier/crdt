// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;

namespace Crdt;

/// <summary>
/// A two-phase/two-phase graph CRDT with vertices stored in a 2P-Set and directed edges stored
/// in a separate 2P-Set. Vertex and edge removals are permanent, and removed vertices hide their
/// incident edges.
/// </summary>
/// <typeparam name="TVertex">The vertex type; must be non-null and have value equality.</typeparam>
/// <remarks>Mutable and not thread-safe.</remarks>
public sealed class TwoPTwoPGraph<TVertex> :
    IConvergent<TwoPTwoPGraph<TVertex>>,
    IDeltaConvergent<TwoPTwoPGraph<TVertex>, TwoPTwoPGraph<TVertex>>,
    IOperationConvergent<TwoPTwoPGraphOperation<TVertex>>,
    IEquatable<TwoPTwoPGraph<TVertex>>
    where TVertex : notnull
{
    private readonly TwoPhaseSet<TVertex> _vertices;
    private readonly TwoPhaseSet<Edge<TVertex>> _edges;

    /// <summary>Initializes an empty 2P2P graph.</summary>
    public TwoPTwoPGraph()
        : this(new TwoPhaseSet<TVertex>(), new TwoPhaseSet<Edge<TVertex>>())
    {
    }

    private TwoPTwoPGraph(TwoPhaseSet<TVertex> vertices, TwoPhaseSet<Edge<TVertex>> edges)
    {
        _vertices = vertices;
        _edges = edges;
    }

    /// <summary>Gets the number of currently visible vertices.</summary>
    public int VertexCount => _vertices.Count;

    /// <summary>Gets the number of currently visible edges whose endpoints are currently present.</summary>
    public int EdgeCount => Edges().Count;

    /// <summary>Determines whether <paramref name="vertex"/> is currently present.</summary>
    /// <param name="vertex">The vertex to test.</param>
    /// <returns><see langword="true"/> if the vertex is present.</returns>
    public bool ContainsVertex(TVertex vertex) => _vertices.Contains(vertex);

    /// <summary>
    /// Determines whether the directed edge from <paramref name="source"/> to <paramref name="target"/> is
    /// visible.
    /// </summary>
    /// <param name="source">The edge source vertex.</param>
    /// <param name="target">The edge target vertex.</param>
    /// <returns><see langword="true"/> if the edge and both endpoints are currently present.</returns>
    public bool ContainsEdge(TVertex source, TVertex target)
    {
        var edge = new Edge<TVertex>(source, target);
        return _edges.Contains(edge) && ContainsVertex(source) && ContainsVertex(target);
    }

    /// <summary>Gets the currently visible vertices.</summary>
    /// <returns>The visible vertices.</returns>
    public IReadOnlyCollection<TVertex> Vertices() => _vertices.Elements;

    /// <summary>Gets the currently visible edges whose endpoints are currently present.</summary>
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
    public TwoPTwoPGraphOperation<TVertex> AddVertex(TVertex vertex)
    {
        _vertices.Add(vertex);
        return TwoPTwoPGraphOperation<TVertex>.AddVertex(vertex);
    }

    /// <summary>Removes <paramref name="vertex"/> permanently and returns the operation to broadcast.</summary>
    /// <param name="vertex">The currently-present vertex to remove.</param>
    /// <returns>The remove-vertex operation.</returns>
    public TwoPTwoPGraphOperation<TVertex> RemoveVertex(TVertex vertex)
    {
        _vertices.Remove(vertex);
        return TwoPTwoPGraphOperation<TVertex>.RemoveVertex(vertex);
    }

    /// <summary>Adds a directed edge between two currently-present endpoints.</summary>
    /// <param name="source">The edge source vertex.</param>
    /// <param name="target">The edge target vertex.</param>
    /// <returns>The add-edge operation.</returns>
    /// <exception cref="InvalidOperationException">An endpoint is not currently present.</exception>
    public TwoPTwoPGraphOperation<TVertex> AddEdge(TVertex source, TVertex target)
    {
        if (!ContainsVertex(source) || !ContainsVertex(target))
        {
            throw new InvalidOperationException("Both endpoints must be present before adding an edge.");
        }

        _edges.Add(new Edge<TVertex>(source, target));
        return TwoPTwoPGraphOperation<TVertex>.AddEdge(source, target);
    }

    /// <summary>Removes a directed edge permanently and returns the operation to broadcast.</summary>
    /// <param name="source">The edge source vertex.</param>
    /// <param name="target">The edge target vertex.</param>
    /// <returns>The remove-edge operation.</returns>
    public TwoPTwoPGraphOperation<TVertex> RemoveEdge(TVertex source, TVertex target)
    {
        _edges.Remove(new Edge<TVertex>(source, target));
        return TwoPTwoPGraphOperation<TVertex>.RemoveEdge(source, target);
    }

    /// <inheritdoc/>
    public void Merge(TwoPTwoPGraph<TVertex> other)
    {
        Throw.IfNull(other);
        _vertices.Merge(other._vertices);
        _edges.Merge(other._edges);
    }

    /// <inheritdoc/>
    public CrdtOrder Compare(TwoPTwoPGraph<TVertex> other)
    {
        Throw.IfNull(other);
        return CombineOrders(_vertices.Compare(other._vertices), _edges.Compare(other._edges));
    }

    /// <inheritdoc/>
    public TwoPTwoPGraph<TVertex> Clone() => new(_vertices.Clone(), _edges.Clone());

    /// <inheritdoc/>
    public bool TryExtractDelta([MaybeNullWhen(false)] out TwoPTwoPGraph<TVertex> delta)
    {
        bool hasVertexDelta = _vertices.TryExtractDelta(out TwoPhaseSet<TVertex>? vertexDelta);
        bool hasEdgeDelta = _edges.TryExtractDelta(out TwoPhaseSet<Edge<TVertex>>? edgeDelta);
        if (!hasVertexDelta && !hasEdgeDelta)
        {
            delta = null;
            return false;
        }

        delta = new TwoPTwoPGraph<TVertex>(
            vertexDelta ?? new TwoPhaseSet<TVertex>(),
            edgeDelta ?? new TwoPhaseSet<Edge<TVertex>>());
        return true;
    }

    /// <inheritdoc/>
    public void MergeDelta(TwoPTwoPGraph<TVertex> delta)
    {
        Throw.IfNull(delta);
        Merge(delta);
    }

    /// <inheritdoc/>
    public bool Apply(TwoPTwoPGraphOperation<TVertex> operation)
    {
        return operation.Kind switch
        {
            TwoPTwoPGraphOperationKind.AddVertex => _vertices.Apply(
                new TwoPhaseSetOperation<TVertex>(TwoPhaseSetOperationKind.Add, operation.Vertex)),
            TwoPTwoPGraphOperationKind.RemoveVertex => _vertices.Apply(
                new TwoPhaseSetOperation<TVertex>(TwoPhaseSetOperationKind.Remove, operation.Vertex)),
            TwoPTwoPGraphOperationKind.AddEdge => _edges.Apply(new TwoPhaseSetOperation<Edge<TVertex>>(
                TwoPhaseSetOperationKind.Add,
                new Edge<TVertex>(operation.Source, operation.Target))),
            TwoPTwoPGraphOperationKind.RemoveEdge => _edges.Apply(new TwoPhaseSetOperation<Edge<TVertex>>(
                TwoPhaseSetOperationKind.Remove,
                new Edge<TVertex>(operation.Source, operation.Target))),
            _ => false,
        };
    }

    /// <summary>Serializes the graph to the binary format using <paramref name="vertexSerializer"/>.</summary>
    /// <param name="output">The destination buffer writer.</param>
    /// <param name="vertexSerializer">The vertex serializer.</param>
    public void WriteTo(IBufferWriter<byte> output, ICrdtValueSerializer<TVertex> vertexSerializer)
    {
        Throw.IfNull(output);
        Throw.IfNull(vertexSerializer);
        var edgeSerializer = new EdgeCrdtValueSerializer<TVertex>(vertexSerializer);
        byte[] vertices = _vertices.ToByteArray(vertexSerializer);
        byte[] edges = _edges.ToByteArray(edgeSerializer);
        var writer = new CrdtWriter(output);
        WriteSection(ref writer, vertices);
        WriteSection(ref writer, edges);
    }

    /// <summary>Serializes the graph to a new byte array using <paramref name="vertexSerializer"/>.</summary>
    /// <param name="vertexSerializer">The vertex serializer.</param>
    /// <returns>The encoded bytes.</returns>
    public byte[] ToByteArray(ICrdtValueSerializer<TVertex> vertexSerializer)
    {
        using var buffer = new PooledBufferWriter();
        WriteTo(buffer, vertexSerializer);
        return buffer.WrittenSpan.ToArray();
    }

    /// <summary>Decodes a graph from the binary format using <paramref name="vertexSerializer"/>.</summary>
    /// <param name="data">The encoded bytes.</param>
    /// <param name="vertexSerializer">The vertex serializer.</param>
    /// <param name="options">Optional decoding limits.</param>
    /// <returns>The decoded graph.</returns>
    public static TwoPTwoPGraph<TVertex> ReadFrom(
        ReadOnlySpan<byte> data,
        ICrdtValueSerializer<TVertex> vertexSerializer,
        CrdtReaderOptions? options = null)
    {
        Throw.IfNull(vertexSerializer);
        var reader = new CrdtReader(data, options);
        ReadOnlySpan<byte> vertexBytes = ReadSection(ref reader);
        ReadOnlySpan<byte> edgeBytes = ReadSection(ref reader);
        var edgeSerializer = new EdgeCrdtValueSerializer<TVertex>(vertexSerializer);
        return new TwoPTwoPGraph<TVertex>(
            TwoPhaseSet<TVertex>.ReadFrom(vertexBytes, vertexSerializer, options),
            TwoPhaseSet<Edge<TVertex>>.ReadFrom(edgeBytes, edgeSerializer, options));
    }

    /// <summary>Serializes the graph to JSON using <paramref name="vertexSerializer"/>.</summary>
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

    /// <summary>Deserializes a graph from JSON using <paramref name="vertexSerializer"/>.</summary>
    /// <param name="json">The JSON string.</param>
    /// <param name="vertexSerializer">The vertex serializer.</param>
    /// <returns>The decoded graph.</returns>
    public static TwoPTwoPGraph<TVertex> FromJson(string json, ICrdtValueSerializer<TVertex> vertexSerializer)
    {
        Throw.IfNull(json);
        Throw.IfNull(vertexSerializer);
        var edgeSerializer = new EdgeCrdtValueSerializer<TVertex>(vertexSerializer);
        var vertices = new TwoPhaseSet<TVertex>();
        var edges = new TwoPhaseSet<Edge<TVertex>>();
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json));
        reader.Read();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            string? name = reader.GetString();
            reader.Read();
            using JsonDocument document = JsonDocument.ParseValue(ref reader);
            if (name == "vertices")
            {
                vertices = TwoPhaseSet<TVertex>.FromJson(document.RootElement.GetRawText(), vertexSerializer);
            }
            else if (name == "edges")
            {
                edges = TwoPhaseSet<Edge<TVertex>>.FromJson(document.RootElement.GetRawText(), edgeSerializer);
            }
        }

        return new TwoPTwoPGraph<TVertex>(vertices, edges);
    }

    /// <inheritdoc/>
    public bool Equals(TwoPTwoPGraph<TVertex>? other) =>
        other is not null && _vertices.Equals(other._vertices) && _edges.Equals(other._edges);

    /// <inheritdoc/>
    public override bool Equals([NotNullWhen(true)] object? obj) => Equals(obj as TwoPTwoPGraph<TVertex>);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(_vertices, _edges);

    private static void WriteSection(ref CrdtWriter writer, ReadOnlySpan<byte> bytes)
    {
        writer.WriteVarUInt64((ulong)bytes.Length);
        writer.WriteRaw(bytes);
    }

    private static ReadOnlySpan<byte> ReadSection(ref CrdtReader reader)
    {
        ulong length = reader.ReadVarUInt64();
        if (length > int.MaxValue)
        {
            Throw.InvalidData<int>("Encoded graph section exceeds the supported length.");
        }

        return reader.ReadRaw((int)length);
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
