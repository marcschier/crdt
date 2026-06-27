// Copyright (c) marcschier. Licensed under the MIT License.

using Crdt.Tests.Testing;

namespace Crdt.Tests.Documents;

public sealed class JsonCrdtTests
{
    private static readonly ReplicaId A = ReplicaId.FromUInt64(1);
    private static readonly ReplicaId B = ReplicaId.FromUInt64(2);
    private static readonly ReplicaId C = ReplicaId.FromUInt64(3);

    [Test]
    public async Task Builder_Creates_Nested_Document()
    {
        var doc = new JsonCrdt();
        doc.SetNumber(A, Ts(1, 1), "n", 1D);
        doc.SetArray(A, Ts(2, 1), Array.Empty<JsonPathSegment>(), "users");
        JsonOperation user = doc.PushObject(A, Ts(3, 1), Path("users"));
        doc.SetString(A, Ts(4, 1), [JsonPathSegment.MapKey("users"), JsonPathSegment.ListElement(user.ElementId)], "name", "a");

        await Assert.That(doc.ToJson()).IsEqualTo("""{"n":1,"users":[{"name":"a"}]}""");
    }

    [Test]
    public async Task Merge_Concurrent_Edits_At_Different_Paths_Both_Survive()
    {
        var left = new JsonCrdt();
        var right = new JsonCrdt();

        left.SetNumber(A, Ts(1, 1), "n", 1D);
        right.SetString(B, Ts(1, 2), "name", "a");
        left.Merge(right);
        right.Merge(left);

        await Assert.That(left.ToJson()).IsEqualTo("""{"n":1,"name":"a"}""");
        await Assert.That(right.ToJson()).IsEqualTo(left.ToJson());
    }

    [Test]
    public async Task Merge_Concurrent_Assign_To_Same_Register_Uses_Lww_Timestamp()
    {
        var seed = new JsonCrdt();
        seed.SetNumber(A, Ts(1, 1), "n", 0D);
        JsonCrdt left = seed.Clone();
        JsonCrdt right = seed.Clone();

        left.Assign(A, Ts(2, 1), Path("n"), JsonPrimitive.Number(1D));
        right.Assign(B, Ts(3, 2), Path("n"), JsonPrimitive.Number(2D));
        left.Merge(right);
        right.Merge(left);

        await Assert.That(left.ToJson()).IsEqualTo("""{"n":2}""");
        await Assert.That(right.ToJson()).IsEqualTo(left.ToJson());
    }

    [Test]
    public async Task Merge_Array_Insert_And_Delete_Converge()
    {
        var left = new JsonCrdt();
        left.SetArray(A, Ts(1, 1), Array.Empty<JsonPathSegment>(), "items");
        JsonCrdt right = left.Clone();

        JsonOperation insert = left.Push(A, Ts(2, 1), Path("items"), JsonLiteral.PrimitiveValue(JsonPrimitive.String("x")));
        right.Apply(insert);
        right.DeleteIndex(B, Ts(3, 2), Path("items"), insert.ElementId);
        left.Merge(right);
        right.Merge(left);

        await Assert.That(left.ToJson()).IsEqualTo("""{"items":[]}""");
        await Assert.That(right.ToJson()).IsEqualTo(left.ToJson());
    }

    [Test]
    public async Task Operations_Converge_Under_Reordering_And_Duplication()
    {
        var r0 = new JsonCrdt();
        var r1 = new JsonCrdt();
        var r2 = new JsonCrdt();
        var sim = new OperationDeliverySimulator<JsonCrdt, JsonOperation>(r0, r1, r2);

        JsonOperation setA = r0.SetString(A, Ts(1, 1), "a", "old");
        sim.Broadcast(0, setA);
        sim.Broadcast(0, r0.DeleteKey(A, Ts(2, 1), Array.Empty<JsonPathSegment>(), "a"));
        sim.Broadcast(1, r1.SetNumber(B, Ts(1, 2), "b", 2D));
        sim.Broadcast(2, r2.SetBoolean(C, Ts(1, 3), Array.Empty<JsonPathSegment>(), "c", true));

        sim.DeliverAll(seed: 29, duplicate: true);

        sim.AssertConverged(static (left, right) => left.Equals(right));
        await Assert.That(r0.ToJson()).IsEqualTo("""{"b":2,"c":true}""");
    }

    [Test]
    public async Task Json_Roundtrips_As_Fresh_Seed_Document()
    {
        JsonCrdt doc = JsonCrdt.FromJson("""{"n":1,"users":[{"name":"a"}]}""");
        JsonCrdt restored = JsonCrdt.FromJson(doc.ToJson());

        await Assert.That(restored.ToJson()).IsEqualTo(doc.ToJson());
    }

    [Test]
    public async Task Binary_Roundtrips_Document_State()
    {
        var doc = new JsonCrdt();
        doc.SetString(A, Ts(1, 1), "name", "a");
        doc.SetArray(A, Ts(2, 1), Array.Empty<JsonPathSegment>(), "items");
        doc.Push(A, Ts(3, 1), Path("items"), JsonLiteral.PrimitiveValue(JsonPrimitive.Number(1D)));

        JsonCrdt restored = JsonCrdt.ReadFrom(doc.ToByteArray());

        await Assert.That(restored).IsEqualTo(doc);
        await Assert.That(restored.ToJson()).IsEqualTo(doc.ToJson());
    }

    private static JsonPathSegment[] Path(string key) => [JsonPathSegment.MapKey(key)];

    private static Timestamp Ts(long wallClock, ulong replica) => new(wallClock, 0UL, ReplicaId.FromUInt64(replica));
}
