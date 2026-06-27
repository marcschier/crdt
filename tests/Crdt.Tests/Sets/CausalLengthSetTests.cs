// Copyright (c) marcschier. Licensed under the MIT License.

using Crdt.Tests.Testing;

namespace Crdt.Tests.Sets;

public sealed class CausalLengthSetTests
{
    [Test]
    public async Task Add_Remove_Readd_Cycles_Increase_Lengths()
    {
        var set = new CausalLengthSet<string>();

        CausalLengthSetOperation<string> add = set.Add("a");
        CausalLengthSetOperation<string> duplicateAdd = set.Add("a");
        CausalLengthSetOperation<string> remove = set.Remove("a");
        CausalLengthSetOperation<string> duplicateRemove = set.Remove("a");
        CausalLengthSetOperation<string> readd = set.Add("a");

        await Assert.That(add.Length).IsEqualTo(1UL);
        await Assert.That(duplicateAdd.Length).IsEqualTo(1UL);
        await Assert.That(remove.Length).IsEqualTo(2UL);
        await Assert.That(duplicateRemove.Length).IsEqualTo(2UL);
        await Assert.That(readd.Length).IsEqualTo(3UL);
        await Assert.That(set.Contains("a")).IsTrue();
        await Assert.That(set.Count).IsEqualTo(1);
    }

    [Test]
    public async Task Concurrent_Add_Vs_Remove_Resolves_To_Higher_Length()
    {
        var original = new CausalLengthSet<string>();
        original.Add("a");

        CausalLengthSet<string> remover = original.Clone();
        CausalLengthSet<string> adder = original.Clone();
        CausalLengthSetOperation<string> remove = remover.Remove("a");
        CausalLengthSetOperation<string> add = adder.Add("a");

        adder.Merge(remover);
        remover.Merge(adder);

        await Assert.That(add.Length).IsEqualTo(1UL);
        await Assert.That(remove.Length).IsEqualTo(2UL);
        await Assert.That(adder.Contains("a")).IsFalse();
        await Assert.That(remover).IsEqualTo(adder);
    }

    [Test]
    public async Task Equal_Concurrent_Adds_Converge()
    {
        var left = new CausalLengthSet<string>();
        var right = new CausalLengthSet<string>();
        left.Add("a");
        right.Add("a");

        left.Merge(right);
        right.Merge(left);

        await Assert.That(left.Contains("a")).IsTrue();
        await Assert.That(left).IsEqualTo(right);
        await Assert.That(left.Compare(right)).IsEqualTo(CrdtOrder.Equal);
    }

    [Test]
    public void Merge_Is_Idempotent_Commutative_And_Associative()
    {
        CrdtLaws.AssertSemilattice(Sample("a", "b"), Sample("b", "c"), Sample("a", "d"));
    }

    [Test]
    public async Task Delta_Merge_Equals_Full_Merge()
    {
        var source = new CausalLengthSet<string>();
        source.Add("a");
        source.Add("b");
        source.Remove("a");

        var full = new CausalLengthSet<string>();
        full.Merge(source);

        bool extracted = source.TryExtractDelta(out CausalLengthSet<string>? delta);
        var deltaMerged = new CausalLengthSet<string>();
        deltaMerged.MergeDelta(delta!);

        await Assert.That(extracted).IsTrue();
        await Assert.That(deltaMerged).IsEqualTo(full);
        await Assert.That(deltaMerged.Contains("a")).IsFalse();
        await Assert.That(deltaMerged.Contains("b")).IsTrue();
    }

    [Test]
    public async Task Apply_Is_Idempotent()
    {
        var source = new CausalLengthSet<string>();
        CausalLengthSetOperation<string> add = source.Add("a");
        CausalLengthSetOperation<string> remove = source.Remove("a");
        var target = new CausalLengthSet<string>();

        bool firstAdd = target.Apply(add);
        bool secondAdd = target.Apply(add);
        bool firstRemove = target.Apply(remove);
        bool secondRemove = target.Apply(remove);

        await Assert.That(firstAdd).IsTrue();
        await Assert.That(secondAdd).IsFalse();
        await Assert.That(firstRemove).IsTrue();
        await Assert.That(secondRemove).IsFalse();
        await Assert.That(target.Contains("a")).IsFalse();
    }

    [Test]
    public async Task Binary_Roundtrips_With_String_Serializer()
    {
        var set = new CausalLengthSet<string>();
        set.Add("beta");
        set.Add("alpha");
        set.Remove("alpha");

        CausalLengthSet<string> restored = CausalLengthSet<string>.ReadFrom(
            set.ToByteArray(CrdtValues.String),
            CrdtValues.String);

        await Assert.That(restored).IsEqualTo(set);
        await Assert.That(restored.Contains("alpha")).IsFalse();
        await Assert.That(restored.Contains("beta")).IsTrue();
    }

    [Test]
    public async Task Json_Roundtrips_With_String_Serializer()
    {
        var set = new CausalLengthSet<string>();
        set.Add("beta");
        set.Add("alpha");
        set.Remove("alpha");

        CausalLengthSet<string> restored = CausalLengthSet<string>.FromJson(
            set.ToJson(CrdtValues.String),
            CrdtValues.String);

        await Assert.That(restored).IsEqualTo(set);
        await Assert.That(restored.Contains("alpha")).IsFalse();
        await Assert.That(restored.Contains("beta")).IsTrue();
    }

    [Test]
    public async Task Compare_Reflects_Per_Element_Length_Order()
    {
        var small = new CausalLengthSet<string>();
        small.Add("a");

        var large = small.Clone();
        large.Remove("a");

        var concurrent = small.Clone();
        concurrent.Add("b");

        await Assert.That(small.Compare(large)).IsEqualTo(CrdtOrder.Less);
        await Assert.That(large.Compare(small)).IsEqualTo(CrdtOrder.Greater);
        await Assert.That(large.Compare(concurrent)).IsEqualTo(CrdtOrder.Concurrent);
        await Assert.That(small.Compare(small.Clone())).IsEqualTo(CrdtOrder.Equal);
    }

    private static CausalLengthSet<string> Sample(string first, string second)
    {
        var set = new CausalLengthSet<string>();
        set.Add(first);
        set.Add(second);
        set.Remove(first);
        return set;
    }
}
