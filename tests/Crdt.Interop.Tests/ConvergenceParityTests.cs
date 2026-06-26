// Copyright (c) marcschier. Licensed under the MIT License.

namespace Crdt.Interop.Tests;

public sealed class ConvergenceParityTests
{
    private static readonly ReplicaId BaseReplica = ReplicaId.FromUInt64(100);

    [Test]
    [Arguments("sequential-basic")]
    [Arguments("sequential-middle-edits")]
    [Arguments("sequential-tail-edits")]
    public async Task Tier1_Sequential_Edits_Match_Yjs_Exactly(string scenarioName)
    {
        CrdtScenario scenario = ScenarioSamples.Get(scenarioName);
        YjsResult yjs = await YjsHarness.RunAsync(scenario);
        string expected = yjs.Final["A"];

        await Assert.That(ApplyTextScenario(scenario)["A"]).IsEqualTo(expected);
        foreach (string algorithm in SequenceAlgorithms())
        {
            Dictionary<string, string> final = ApplySequenceScenario(scenario, algorithm);
            await Assert.That(final["A"]).IsEqualTo(expected);
        }
    }

    [Test]
    [Arguments("concurrent-same-index")]
    [Arguments("concurrent-different-indexes")]
    [Arguments("concurrent-multi-character")]
    public async Task Tier2_Concurrent_Edits_Converge_And_Match_Yjs_Multiset(string scenarioName)
    {
        CrdtScenario scenario = ScenarioSamples.Get(scenarioName);
        YjsResult yjs = await YjsHarness.RunAsync(scenario);
        string yjsA = yjs.Final["A"];
        string yjsB = yjs.Final["B"];

        await Assert.That(yjsA).IsEqualTo(yjsB);

        Dictionary<string, string> textFinal = ApplyTextScenario(scenario);
        await Assert.That(textFinal["A"]).IsEqualTo(textFinal["B"]);
        await Assert.That(textFinal["A"].Length).IsEqualTo(yjsA.Length);
        await Assert.That(SortedChars(textFinal["A"])).IsEqualTo(SortedChars(yjsA));

        foreach (string algorithm in SequenceAlgorithms())
        {
            Dictionary<string, string> final = ApplySequenceScenario(scenario, algorithm);
            await Assert.That(final["A"]).IsEqualTo(final["B"]);
            await Assert.That(final["A"].Length).IsEqualTo(yjsA.Length);
            await Assert.That(SortedChars(final["A"])).IsEqualTo(SortedChars(yjsA));
        }
    }

    private static Dictionary<string, string> ApplyTextScenario(CrdtScenario scenario)
    {
        var baseText = new Text();
        if (!string.IsNullOrEmpty(scenario.Initial))
        {
            baseText.Append(InitialReplicaFor(scenario), scenario.Initial);
        }

        Dictionary<string, Text> replicas = scenario.Replicas.ToDictionary(r => r.Id, _ => baseText.Clone());
        foreach (CrdtReplica replica in scenario.Replicas)
        {
            Text text = replicas[replica.Id];
            ReplicaId replicaId = ReplicaFor(replica.Id);
            foreach (CrdtOperation operation in replica.Ops)
            {
                ApplyTextOperation(text, replicaId, operation);
            }
        }

        foreach (string step in scenario.MergeSchedule)
        {
            (string target, string source) = ParseMerge(step);
            replicas[target].Merge(replicas[source]);
        }

        return replicas.ToDictionary(entry => entry.Key, entry => entry.Value.Value);
    }

    private static Dictionary<string, string> ApplySequenceScenario(CrdtScenario scenario, string algorithm)
    {
        return algorithm switch
        {
            "Rga" => ApplySequenceScenario(new RgaAdapter(), scenario),
            "Logoot" => ApplySequenceScenario(new LogootAdapter(), scenario),
            "LSeq" => ApplySequenceScenario(new LSeqAdapter(), scenario),
            "Treedoc" => ApplySequenceScenario(new TreedocAdapter(), scenario),
            "Yata" => ApplySequenceScenario(new YataAdapter(), scenario),
            "Woot" => ApplySequenceScenario(new WootAdapter(), scenario),
            _ => throw new ArgumentOutOfRangeException(nameof(algorithm), algorithm, "Unknown sequence algorithm.")
        };
    }

    private static Dictionary<string, string> ApplySequenceScenario<TSequence>(
        TSequence baseSequence,
        CrdtScenario scenario)
        where TSequence : ISequenceAdapter<TSequence>
    {
        if (!string.IsNullOrEmpty(scenario.Initial))
        {
            baseSequence.InsertText(InitialReplicaFor(scenario), 0, scenario.Initial);
        }

        Dictionary<string, TSequence> replicas =
            scenario.Replicas.ToDictionary(r => r.Id, _ => baseSequence.CloneSequence());
        foreach (CrdtReplica replica in scenario.Replicas)
        {
            TSequence sequence = replicas[replica.Id];
            ReplicaId replicaId = ReplicaFor(replica.Id);
            foreach (CrdtOperation operation in replica.Ops)
            {
                sequence.ApplyOperation(replicaId, operation);
            }
        }

        foreach (string step in scenario.MergeSchedule)
        {
            (string target, string source) = ParseMerge(step);
            replicas[target].MergeSequence(replicas[source]);
        }

        return replicas.ToDictionary(entry => entry.Key, entry => entry.Value.Render());
    }

    private static void ApplyTextOperation(Text text, ReplicaId replicaId, CrdtOperation operation)
    {
        if (operation.Op == "insert")
        {
            text.Insert(replicaId, operation.Index, operation.Text ?? string.Empty);
            return;
        }

        if (operation.Op == "delete")
        {
            text.Delete(operation.Index, operation.Length);
            return;
        }

        throw new InvalidOperationException($"Unsupported operation '{operation.Op}'.");
    }

    private static (string Target, string Source) ParseMerge(string step)
    {
        string[] parts = step.Split("<-", StringSplitOptions.None);
        if (parts.Length != 2)
        {
            throw new InvalidOperationException($"Invalid merge step '{step}'.");
        }

        return (parts[0], parts[1]);
    }

    private static ReplicaId ReplicaFor(string id) => id switch
    {
        "A" => ReplicaId.FromUInt64(1),
        "B" => ReplicaId.FromUInt64(2),
        _ => ReplicaId.FromUInt64((ulong)Math.Abs(StringComparer.Ordinal.GetHashCode(id)) + 3UL)
    };

    private static ReplicaId InitialReplicaFor(CrdtScenario scenario) =>
        scenario.Replicas.Count == 1 ? ReplicaFor(scenario.Replicas[0].Id) : BaseReplica;

    private static IEnumerable<string> SequenceAlgorithms()
    {
        yield return "Rga";
        yield return "Logoot";
        yield return "LSeq";
        yield return "Treedoc";
        yield return "Yata";
        yield return "Woot";
    }

    private static string SortedChars(string value) => string.Concat(value.OrderBy(static c => c));
}
