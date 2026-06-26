// Copyright (c) marcschier. Licensed under the MIT License.

using Crdt.Tests.Testing;

namespace Crdt.Tests.Graphs;

public sealed class TwoPTwoPGraphTests
{
    [Test]
    public async Task Add_Vertices_And_Edges_Makes_Them_Visible()
    {
        var graph = new TwoPTwoPGraph<string>();
        graph.AddVertex("a");
        graph.AddVertex("b");
        graph.AddEdge("a", "b");

        await Assert.That(graph.ContainsVertex("a")).IsTrue();
        await Assert.That(graph.ContainsEdge("a", "b")).IsTrue();
        await Assert.That(graph.VertexCount).IsEqualTo(2);
        await Assert.That(graph.EdgeCount).IsEqualTo(1);
    }

    [Test]
    public async Task AddEdge_Throws_When_Endpoint_Is_Missing()
    {
        var graph = new TwoPTwoPGraph<string>();
        graph.AddVertex("a");

        await Assert.That(() => graph.AddEdge("a", "missing")).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task RemoveVertex_Hides_Incident_Edges()
    {
        var graph = new TwoPTwoPGraph<string>();
        graph.AddVertex("a");
        graph.AddVertex("b");
        graph.AddEdge("a", "b");
        graph.RemoveVertex("a");

        await Assert.That(graph.ContainsVertex("a")).IsFalse();
        await Assert.That(graph.ContainsEdge("a", "b")).IsFalse();
        await Assert.That(graph.EdgeCount).IsEqualTo(0);
    }

    [Test]
    public async Task RemoveEdge_Hides_Edge()
    {
        var graph = new TwoPTwoPGraph<string>();
        graph.AddVertex("a");
        graph.AddVertex("b");
        graph.AddEdge("a", "b");
        graph.RemoveEdge("a", "b");

        await Assert.That(graph.ContainsEdge("a", "b")).IsFalse();
        await Assert.That(graph.EdgeCount).IsEqualTo(0);
    }

    [Test]
    public async Task Remove_Is_Permanent_For_Vertices_And_Edges()
    {
        var graph = new TwoPTwoPGraph<string>();
        graph.AddVertex("a");
        graph.AddVertex("b");
        graph.AddEdge("a", "b");
        graph.RemoveEdge("a", "b");
        graph.AddEdge("a", "b");
        graph.RemoveVertex("a");
        graph.AddVertex("a");

        await Assert.That(graph.ContainsVertex("a")).IsFalse();
        await Assert.That(graph.ContainsEdge("a", "b")).IsFalse();
    }

    [Test]
    public async Task Merge_Converges_Vertices_Edges_And_Removals()
    {
        var left = new TwoPTwoPGraph<string>();
        left.AddVertex("a");
        left.AddVertex("b");
        left.AddEdge("a", "b");

        var right = new TwoPTwoPGraph<string>();
        right.AddVertex("a");
        right.AddVertex("b");
        right.AddVertex("c");
        right.AddEdge("b", "c");
        right.RemoveVertex("b");

        left.Merge(right);
        right.Merge(left);

        await Assert.That(left.Equals(right)).IsTrue();
        await Assert.That(left.ContainsVertex("a")).IsTrue();
        await Assert.That(left.ContainsVertex("b")).IsFalse();
        await Assert.That(left.ContainsEdge("a", "b")).IsFalse();
        await Assert.That(left.ContainsEdge("b", "c")).IsFalse();
    }

    [Test]
    public void Satisfies_Semilattice_Laws()
    {
        CrdtLaws.AssertSemilattice(SampleOne(), SampleTwo(), SampleThree());
    }

    [Test]
    public async Task Delta_Carries_Vertex_And_Edge_Changes()
    {
        var source = new TwoPTwoPGraph<string>();
        source.AddVertex("a");
        source.AddVertex("b");
        source.AddEdge("a", "b");
        source.RemoveEdge("a", "b");

        bool extracted = source.TryExtractDelta(out TwoPTwoPGraph<string>? delta);
        var target = new TwoPTwoPGraph<string>();
        target.MergeDelta(delta!);

        await Assert.That(extracted).IsTrue();
        await Assert.That(target.ContainsVertex("a")).IsTrue();
        await Assert.That(target.ContainsEdge("a", "b")).IsFalse();
        await Assert.That(target.Equals(source)).IsTrue();
    }

    [Test]
    public void Operations_Converge_Under_Reordering_And_Duplication()
    {
        var r0 = new TwoPTwoPGraph<string>();
        var r1 = new TwoPTwoPGraph<string>();
        var r2 = new TwoPTwoPGraph<string>();
        var sim = new OperationDeliverySimulator<TwoPTwoPGraph<string>, TwoPTwoPGraphOperation<string>>(r0, r1, r2);

        sim.Broadcast(0, r0.AddVertex("a"));
        sim.Broadcast(0, r0.AddVertex("b"));
        sim.Broadcast(0, r0.AddEdge("a", "b"));
        sim.Broadcast(0, r0.RemoveEdge("a", "b"));
        sim.Broadcast(1, r1.AddVertex("c"));
        sim.Broadcast(1, r1.AddVertex("d"));
        sim.Broadcast(1, r1.AddEdge("c", "d"));
        sim.Broadcast(1, r1.RemoveVertex("c"));

        sim.DeliverAll(seed: 29, duplicate: true);

        sim.AssertConverged(static (x, y) => x.Equals(y));
    }

    [Test]
    public async Task Binary_Roundtrips()
    {
        var graph = SampleOne();
        graph.RemoveEdge("a", "b");

        TwoPTwoPGraph<string> restored = TwoPTwoPGraph<string>.ReadFrom(
            graph.ToByteArray(CrdtValues.String),
            CrdtValues.String);

        await Assert.That(restored).IsEqualTo(graph);
    }

    [Test]
    public async Task Json_Roundtrips()
    {
        var graph = SampleTwo();
        graph.RemoveVertex("y");

        TwoPTwoPGraph<string> restored = TwoPTwoPGraph<string>.FromJson(
            graph.ToJson(CrdtValues.String),
            CrdtValues.String);

        await Assert.That(restored).IsEqualTo(graph);
    }

    private static TwoPTwoPGraph<string> SampleOne()
    {
        var graph = new TwoPTwoPGraph<string>();
        graph.AddVertex("a");
        graph.AddVertex("b");
        graph.AddEdge("a", "b");
        return graph;
    }

    private static TwoPTwoPGraph<string> SampleTwo()
    {
        var graph = new TwoPTwoPGraph<string>();
        graph.AddVertex("x");
        graph.AddVertex("y");
        graph.AddVertex("z");
        graph.AddEdge("x", "y");
        graph.AddEdge("y", "z");
        return graph;
    }

    private static TwoPTwoPGraph<string> SampleThree()
    {
        var graph = new TwoPTwoPGraph<string>();
        graph.AddVertex("m");
        graph.AddVertex("n");
        graph.AddEdge("m", "n");
        graph.RemoveEdge("m", "n");
        return graph;
    }
}
