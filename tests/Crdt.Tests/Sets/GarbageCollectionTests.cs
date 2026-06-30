// Copyright (c) marcschier. Licensed under the MIT License.

namespace Crdt.Tests.Sets;

public sealed class GarbageCollectionTests
{
    [Test]
    public async Task TwoPhaseSet_CollectStable_Is_NoOp_Without_Value_Proof()
    {
        var set = new TwoPhaseSet<string>();
        set.Add("a");
        set.Remove("a");
        string before = set.ToJson(CrdtValues.String);

        set.CollectStable(StableCut.Meet([]));

        await Assert.That(set.ToJson(CrdtValues.String)).IsEqualTo(before);
        await Assert.That(set.Contains("a")).IsFalse();
    }

    [Test]
    public async Task TwoPhaseSet_CollectAllObserved_Drops_Stable_Removed_Value()
    {
        var set = new TwoPhaseSet<string>();
        set.Add("a");
        set.Remove("a");

        set.CollectAllObserved(["a"]);
        set.CollectAllObserved(["a"]);

        await Assert.That(set.ToJson(CrdtValues.String).Contains("\"a\"")).IsFalse();
        await Assert.That(set.Contains("a")).IsFalse();
    }

    [Test]
    public async Task CausalLengthSet_CollectStable_Is_NoOp_Without_Value_Proof()
    {
        var set = new CausalLengthSet<string>();
        set.Add("a");
        set.Remove("a");
        string before = set.ToJson(CrdtValues.String);

        set.CollectStable(StableCut.Meet([]));

        await Assert.That(set.ToJson(CrdtValues.String)).IsEqualTo(before);
        await Assert.That(set.Contains("a")).IsFalse();
    }

    [Test]
    public async Task CausalLengthSet_CollectAllObserved_Drops_Stable_Removed_Entry()
    {
        var set = new CausalLengthSet<string>();
        set.Add("a");
        set.Remove("a");
        set.Add("b");

        set.CollectAllObserved(["a"]);
        set.CollectAllObserved(["a"]);

        await Assert.That(set.ToJson(CrdtValues.String).Contains("\"a\"")).IsFalse();
        await Assert.That(set.Contains("a")).IsFalse();
        await Assert.That(set.Contains("b")).IsTrue();
    }
}
