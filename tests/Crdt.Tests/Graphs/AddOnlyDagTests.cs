// Copyright (c) marcschier. Licensed under the MIT License.

using Crdt.Tests.Testing;

namespace Crdt.Tests.Graphs;

public sealed class AddOnlyDagTests
{
    [Test]
    public async Task Add_Vertices_And_Edges_Makes_Them_Visible()
    {
        var dag = new AddOnlyDag<string>();
        dag.AddVertex("a");
        dag.AddVertex("b");
        dag.AddEdge("a", "b");

        await Assert.That(dag.ContainsVertex("a")).IsTrue();
        await Assert.That(dag.ContainsEdge("a", "b")).IsTrue();
        await Assert.That(dag.VertexCount).IsEqualTo(2);
        await Assert.That(dag.EdgeCount).IsEqualTo(1);
    }

    [Test]
    public async Task AddEdge_Throws_When_Endpoint_Is_Missing()
    {
        var dag = new AddOnlyDag<string>();
        dag.AddVertex("a");

        await Assert.That(() => dag.AddEdge("a", "missing")).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task AddEdge_Throws_When_It_Would_Close_A_Cycle()
    {
        var dag = new AddOnlyDag<string>();
        dag.AddVertex("a");
        dag.AddVertex("b");
        dag.AddVertex("c");
        dag.AddEdge("a", "b");
        dag.AddEdge("b", "c");

        await Assert.That(() => dag.AddEdge("c", "a")).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task HasCycle_Returns_False_For_A_Dag()
    {
        var dag = SampleOne();

        await Assert.That(dag.HasCycle()).IsFalse();
    }

    [Test]
    public async Task TopologicalSort_Returns_Sources_Before_Targets()
    {
        var dag = SampleOne();

        IReadOnlyList<string> sorted = dag.TopologicalSort();
        bool valid = SourcePrecedesTarget(sorted, dag.Edges());

        await Assert.That(valid).IsTrue();
        await Assert.That(sorted.Count).IsEqualTo(dag.VertexCount);
    }

    [Test]
    public async Task Merge_Unions_Vertices_And_Edges()
    {
        var left = SampleOne();
        var right = new AddOnlyDag<string>();
        right.AddVertex("c");
        right.AddVertex("d");
        right.AddEdge("c", "d");

        left.Merge(right);
        right.Merge(left);

        await Assert.That(left.Equals(right)).IsTrue();
        await Assert.That(left.ContainsEdge("a", "b")).IsTrue();
        await Assert.That(left.ContainsEdge("c", "d")).IsTrue();
    }

    [Test]
    public void Satisfies_Semilattice_Laws()
    {
        CrdtLaws.AssertSemilattice(SampleOne(), SampleTwo(), SampleThree());
    }

    [Test]
    public async Task Delta_Carries_Vertex_And_Edge_Additions()
    {
        var source = SampleOne();

        bool extracted = source.TryExtractDelta(out AddOnlyDag<string>? delta);
        var target = new AddOnlyDag<string>();
        target.MergeDelta(delta!);

        await Assert.That(extracted).IsTrue();
        await Assert.That(target.ContainsVertex("a")).IsTrue();
        await Assert.That(target.ContainsEdge("a", "b")).IsTrue();
        await Assert.That(target.Equals(source)).IsTrue();
    }

    [Test]
    public void Operations_Converge_Under_Reordering_And_Duplication()
    {
        var r0 = new AddOnlyDag<string>();
        var r1 = new AddOnlyDag<string>();
        var r2 = new AddOnlyDag<string>();
        var sim = new OperationDeliverySimulator<AddOnlyDag<string>, AddOnlyDagOperation<string>>(r0, r1, r2);

        sim.Broadcast(0, r0.AddVertex("a"));
        sim.Broadcast(0, r0.AddVertex("b"));
        sim.Broadcast(0, r0.AddVertex("c"));
        sim.Broadcast(0, r0.AddEdge("a", "b"));
        sim.Broadcast(0, r0.AddEdge("b", "c"));
        sim.Broadcast(1, r1.AddVertex("x"));
        sim.Broadcast(1, r1.AddVertex("y"));
        sim.Broadcast(1, r1.AddEdge("x", "y"));

        sim.DeliverAll(seed: 41, duplicate: true);

        sim.AssertConverged(static (x, y) => x.Equals(y));
    }

    [Test]
    public async Task Binary_Roundtrips()
    {
        var dag = SampleTwo();

        AddOnlyDag<string> restored = AddOnlyDag<string>.ReadFrom(
            dag.ToByteArray(CrdtValues.String),
            CrdtValues.String);

        await Assert.That(restored).IsEqualTo(dag);
    }

    [Test]
    public async Task Json_Roundtrips()
    {
        var dag = SampleThree();

        AddOnlyDag<string> restored = AddOnlyDag<string>.FromJson(
            dag.ToJson(CrdtValues.String),
            CrdtValues.String);

        await Assert.That(restored).IsEqualTo(dag);
    }

    [Test]
    public async Task Concurrent_Acyclic_Edge_Additions_Can_Create_Cycle_After_Merge()
    {
        var left = new AddOnlyDag<string>();
        left.AddVertex("a");
        left.AddVertex("b");
        left.AddEdge("a", "b");

        var right = new AddOnlyDag<string>();
        right.AddVertex("a");
        right.AddVertex("b");
        right.AddEdge("b", "a");

        left.Merge(right);

        await Assert.That(left.HasCycle()).IsTrue();
        await Assert.That(() => left.TopologicalSort()).Throws<InvalidOperationException>();
    }

    private static AddOnlyDag<string> SampleOne()
    {
        var dag = new AddOnlyDag<string>();
        dag.AddVertex("a");
        dag.AddVertex("b");
        dag.AddVertex("c");
        dag.AddEdge("a", "b");
        dag.AddEdge("b", "c");
        return dag;
    }

    private static AddOnlyDag<string> SampleTwo()
    {
        var dag = new AddOnlyDag<string>();
        dag.AddVertex("m");
        dag.AddVertex("n");
        dag.AddVertex("o");
        dag.AddEdge("m", "n");
        dag.AddEdge("m", "o");
        return dag;
    }

    private static AddOnlyDag<string> SampleThree()
    {
        var dag = new AddOnlyDag<string>();
        dag.AddVertex("x");
        dag.AddVertex("y");
        dag.AddVertex("z");
        dag.AddEdge("x", "z");
        dag.AddEdge("y", "z");
        return dag;
    }

    private static bool SourcePrecedesTarget(
        IReadOnlyList<string> sorted,
        IReadOnlyCollection<Edge<string>> edges)
    {
        var positions = new Dictionary<string, int>();
        for (int i = 0; i < sorted.Count; i++)
        {
            positions[sorted[i]] = i;
        }

        foreach (Edge<string> edge in edges)
        {
            if (positions[edge.Source] >= positions[edge.Target])
            {
                return false;
            }
        }

        return true;
    }
}
