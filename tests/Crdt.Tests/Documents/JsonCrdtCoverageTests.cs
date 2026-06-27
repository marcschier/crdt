// Copyright (c) marcschier. Licensed under the MIT License.

namespace Crdt.Tests.Documents;

public sealed class JsonCrdtCoverageTests
{
    private static readonly ReplicaId A = ReplicaId.FromUInt64(1);
    private static readonly ReplicaId B = ReplicaId.FromUInt64(2);
    private static readonly ReplicaId C = ReplicaId.FromUInt64(3);

    public static IEnumerable<JsonOperation> OperationCases()
    {
        JsonPathSegment[] path = [JsonPathSegment.MapKey("doc"), JsonPathSegment.ListElement(DotOf(9, 1))];
        Dot removedA = DotOf(7, 1);
        Dot removedB = DotOf(7, 2);

        yield return JsonOperation.SetKey(DotOf(1, 1), Ts(1, 1), path, "s", StringLiteral("text"));
        yield return JsonOperation.SetKey(DotOf(1, 2), Ts(2, 1), path, "n", NumberLiteral(1.25D));
        yield return JsonOperation.SetKey(DotOf(1, 3), Ts(3, 1), path, "t", BoolLiteral(true));
        yield return JsonOperation.SetKey(DotOf(1, 4), Ts(4, 1), path, "z", NullLiteral());
        yield return JsonOperation.SetKey(DotOf(1, 5), Ts(5, 1), path, "o", JsonLiteral.EmptyObject);
        yield return JsonOperation.SetKey(DotOf(1, 6), Ts(6, 1), path, "a", JsonLiteral.EmptyArray);
        yield return JsonOperation.SetKey(DotOf(1, 7), Ts(7, 1), path, "nested", NestedLiteral());
        yield return JsonOperation.InsertAfter(DotOf(2, 1), Ts(8, 2), path, DotOf(2, 0), StringLiteral("x"));
        yield return JsonOperation.InsertAfter(DotOf(2, 2), Ts(9, 2), path, DotOf(2, 1), NestedLiteral());
        yield return JsonOperation.DeleteKey(DotOf(3, 1), Ts(10, 3), path, "gone", [removedB, removedA]);
        yield return JsonOperation.DeleteIndex(DotOf(4, 1), Ts(11, 4), path, DotOf(2, 2));
        yield return JsonOperation.Assign(DotOf(5, 1), Ts(12, 5), path, JsonPrimitive.Boolean(false));
    }

    public static IEnumerable<JsonLiteral> LiteralCases()
    {
        yield return StringLiteral("hello");
        yield return NumberLiteral(42.5D);
        yield return BoolLiteral(true);
        yield return NullLiteral();
        yield return JsonLiteral.EmptyObject;
        yield return JsonLiteral.EmptyArray;
        yield return NestedLiteral();
    }

    [Test]
    [MethodDataSource(nameof(OperationCases))]
    public async Task Operation_Binary_Roundtrips_All_Kinds(JsonOperation operation)
    {
        JsonOperation restored = JsonOperation.ReadFrom(operation.ToByteArray());

        await Assert.That(restored).IsEqualTo(operation);
        await Assert.That(restored.GetHashCode()).IsEqualTo(operation.GetHashCode());
    }

    [Test]
    [MethodDataSource(nameof(LiteralCases))]
    public async Task Literal_Binary_Roundtrips_All_Kinds(JsonLiteral literal)
    {
        JsonLiteral restored = JsonLiteral.ReadFrom(literal.ToByteArray());

        await Assert.That(restored).IsEqualTo(literal);
        await Assert.That(restored.GetHashCode()).IsEqualTo(literal.GetHashCode());
    }

    [Test]
    public async Task Crdt_Binary_And_Json_Roundtrip_Nested_State()
    {
        JsonCrdt doc = BuildNestedDocument();

        JsonCrdt binary = JsonCrdt.ReadFrom(doc.ToByteArray());
        JsonCrdt json = JsonCrdt.FromJson(doc.ToJson());

        await Assert.That(binary).IsEqualTo(doc);
        await Assert.That(binary.ToJson()).IsEqualTo(doc.ToJson());
        await Assert.That(json.ToJson()).IsEqualTo(doc.ToJson());
    }

    [Test]
    public async Task Equality_Covers_Operations_Literals_Primitives_Path_And_Nodes()
    {
        JsonOperation set = JsonOperation.SetKey(DotOf(1, 1), Ts(1, 1), Path("p"), "k", StringLiteral("v"));
        JsonOperation setAgain = JsonOperation.ReadFrom(set.ToByteArray());
        JsonOperation differentDot = JsonOperation.SetKey(DotOf(1, 2), Ts(1, 1), Path("p"), "k", StringLiteral("v"));
        JsonOperation differentTimestamp =
            JsonOperation.SetKey(DotOf(1, 1), Ts(2, 1), Path("p"), "k", StringLiteral("v"));
        JsonOperation differentKey = JsonOperation.SetKey(DotOf(1, 1), Ts(1, 1), Path("p"), "x", StringLiteral("v"));
        JsonOperation differentPath = JsonOperation.SetKey(DotOf(1, 1), Ts(1, 1), Path("q"), "k", StringLiteral("v"));
        JsonOperation differentValue = JsonOperation.SetKey(DotOf(1, 1), Ts(1, 1), Path("p"), "k", NumberLiteral(1D));
        JsonOperation differentKind = JsonOperation.DeleteIndex(DotOf(1, 1), Ts(1, 1), Path("p"), DotOf(3, 1));

        await Assert.That(set.Equals(setAgain)).IsTrue();
        await Assert.That(set.Equals(null)).IsFalse();
        await Assert.That(object.Equals(set, "not an operation")).IsFalse();
        await Assert.That(object.Equals(set, differentDot)).IsFalse();
        await Assert.That(object.Equals(set, differentTimestamp)).IsFalse();
        await Assert.That(object.Equals(set, differentKey)).IsFalse();
        await Assert.That(object.Equals(set, differentPath)).IsFalse();
        await Assert.That(object.Equals(set, differentValue)).IsFalse();
        await Assert.That(object.Equals(set, differentKind)).IsFalse();

        await Assert.That(StringLiteral("a").Equals(StringLiteral("a"))).IsTrue();
        await Assert.That(StringLiteral("a").Equals(null)).IsFalse();
        await Assert.That(StringLiteral("a").Equals("not a literal")).IsFalse();
        await Assert.That(StringLiteral("a").Equals(StringLiteral("b"))).IsFalse();
        await Assert.That(JsonLiteral.Array([StringLiteral("a")]).Equals(JsonLiteral.Array([StringLiteral("b")])))
            .IsFalse();
        await Assert.That(JsonLiteral.Object([Pair("a", StringLiteral("a"))]).Equals(NestedLiteral())).IsFalse();

        await Assert.That(JsonPrimitive.String("a") == JsonPrimitive.String("a")).IsTrue();
        await Assert.That(JsonPrimitive.String("a") != JsonPrimitive.Number(1D)).IsTrue();
        await Assert.That(JsonPrimitive.Boolean(true).Equals(JsonPrimitive.Boolean(false))).IsFalse();
        await Assert.That(JsonPrimitive.Null.Equals("not primitive")).IsFalse();

        await Assert.That(JsonPathSegment.MapKey("a") == JsonPathSegment.MapKey("a")).IsTrue();
        await Assert.That(JsonPathSegment.MapKey("a") != JsonPathSegment.MapKey("b")).IsTrue();
        await Assert.That(JsonPathSegment.ListElement(DotOf(1, 1)).Equals(JsonPathSegment.ListElement(DotOf(1, 2))))
            .IsFalse();
        await Assert.That(JsonPathSegment.MapKey("a").Equals("not path")).IsFalse();

        JsonCrdt doc = BuildNestedDocument();
        JsonCrdt clone = doc.Clone();
        await Assert.That(doc.Root.Equals(clone.Root)).IsTrue();
        await Assert.That(doc.Root.GetHashCode()).IsEqualTo(clone.Root.GetHashCode());
        await Assert.That(doc.Root.Equals(null)).IsFalse();
        await Assert.That(object.Equals(doc.Root, "not a node")).IsFalse();
    }

    [Test]
    public async Task Clone_Is_Independent_And_Merge_Is_Commutative()
    {
        JsonCrdt original = BuildNestedDocument();
        JsonCrdt clone = original.Clone();

        clone.SetString(B, Ts(50, 2), "cloneOnly", "yes");

        await Assert.That(original.ToJson()).DoesNotContain("cloneOnly");
        await Assert.That(clone.ToJson()).Contains("cloneOnly");

        var left = new JsonCrdt();
        var right = new JsonCrdt();
        left.SetString(A, Ts(1, 1), "left", "a");
        right.SetNumber(B, Ts(1, 2), "right", 2D);

        JsonCrdt leftThenRight = left.Clone();
        JsonCrdt rightThenLeft = right.Clone();
        leftThenRight.Merge(right);
        rightThenLeft.Merge(left);

        await Assert.That(leftThenRight).IsEqualTo(rightThenLeft);
        await Assert.That(leftThenRight.ToJson()).IsEqualTo("""{"left":"a","right":2}""");
    }

    [Test]
    public async Task Missing_And_Wrong_Type_Paths_Are_NoOps()
    {
        var doc = new JsonCrdt();
        doc.SetNumber(A, Ts(1, 1), "number", 1D);

        JsonOperation missing = JsonOperation.SetKey(DotOf(9, 1), Ts(2, 1), Path("missing"), "x", StringLiteral("x"));
        JsonOperation wrongType = JsonOperation.SetKey(DotOf(9, 2), Ts(3, 1), Path("number"), "x", StringLiteral("x"));
        JsonOperation wrongListPath =
            JsonOperation.InsertAfter(DotOf(9, 3), Ts(4, 1), Path("number"), default, NullLiteral());
        JsonOperation wrongDelete = JsonOperation.DeleteIndex(DotOf(9, 4), Ts(5, 1), Path("number"), DotOf(9, 3));
        JsonOperation wrongAssign =
            JsonOperation.Assign(DotOf(9, 5), Ts(6, 1), Path("missing"), JsonPrimitive.Number(2D));

        await Assert.That(doc.Apply(missing)).IsFalse();
        await Assert.That(doc.Apply(wrongType)).IsFalse();
        await Assert.That(doc.Apply(wrongListPath)).IsFalse();
        await Assert.That(doc.Apply(wrongDelete)).IsFalse();
        await Assert.That(doc.Apply(wrongAssign)).IsFalse();
        await Assert.That(doc.ListElementIds(Path("number"))).IsEmpty();
        await Assert.That(doc.ToJson()).IsEqualTo("""{"number":1}""");
    }

    [Test]
    public async Task Guards_Throw_For_Null_Arguments_And_Invalid_Primitive_Access()
    {
        await Assert.That(() => JsonPathSegment.MapKey(null!)).Throws<ArgumentNullException>();
        await Assert.That(() => JsonPrimitive.String(null!)).Throws<ArgumentNullException>();
        await Assert.That(() => JsonLiteral.Object(null!)).Throws<ArgumentNullException>();
        await Assert.That(() => JsonLiteral.Array(null!)).Throws<ArgumentNullException>();
        await Assert.That(() => JsonOperation.SetKey(DotOf(1, 1), Ts(1, 1), null!, "k", StringLiteral("v")))
            .Throws<ArgumentNullException>();
        await Assert.That(() => JsonOperation.SetKey(DotOf(1, 1), Ts(1, 1), Path("p"), null!, StringLiteral("v")))
            .Throws<ArgumentNullException>();
        await Assert.That(() => JsonOperation.SetKey(DotOf(1, 1), Ts(1, 1), Path("p"), "k", null!))
            .Throws<ArgumentNullException>();
        await Assert.That(() => JsonOperation.InsertAfter(DotOf(1, 1), Ts(1, 1), null!, default, NullLiteral()))
            .Throws<ArgumentNullException>();
        await Assert.That(() => JsonOperation.InsertAfter(DotOf(1, 1), Ts(1, 1), Path("p"), default, null!))
            .Throws<ArgumentNullException>();
        await Assert.That(() => JsonOperation.DeleteKey(DotOf(1, 1), Ts(1, 1), null!, "k", []))
            .Throws<ArgumentNullException>();
        await Assert.That(() => JsonOperation.DeleteKey(DotOf(1, 1), Ts(1, 1), Path("p"), null!, []))
            .Throws<ArgumentNullException>();
        await Assert.That(() => JsonOperation.DeleteKey(DotOf(1, 1), Ts(1, 1), Path("p"), "k", null!))
            .Throws<ArgumentNullException>();
        await Assert.That(() => JsonOperation.DeleteIndex(DotOf(1, 1), Ts(1, 1), null!, DotOf(2, 1)))
            .Throws<ArgumentNullException>();
        await Assert.That(() => JsonOperation.Assign(DotOf(1, 1), Ts(1, 1), null!, JsonPrimitive.Null))
            .Throws<ArgumentNullException>();
        await Assert.That(() => JsonCrdt.FromJson(null!)).Throws<ArgumentNullException>();
        await Assert.That(() => JsonCrdt.FromJson("[]")).Throws<FormatException>();

        var doc = new JsonCrdt();
        await Assert.That(() => doc.Merge(null!)).Throws<ArgumentNullException>();
        await Assert.That(() => doc.Apply(null!)).Throws<ArgumentNullException>();
        await Assert.That(() => JsonPrimitive.Null.GetString()).Throws<InvalidOperationException>();
        await Assert.That(() => JsonPrimitive.String("x").GetNumber()).Throws<InvalidOperationException>();
        await Assert.That(() => JsonPrimitive.Number(1D).GetBoolean()).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Concurrent_SetKey_And_List_Insert_Delete_Converge()
    {
        var left = new JsonCrdt();
        var right = new JsonCrdt();
        left.SetString(A, Ts(1, 1), "same", "old");
        right.SetString(B, Ts(2, 2), "same", "new");
        left.SetArray(A, Ts(3, 1), Array.Empty<JsonPathSegment>(), "items");
        right.Apply(left.SetArray(A, Ts(3, 1), Array.Empty<JsonPathSegment>(), "items"));

        JsonOperation insert = left.Push(A, Ts(4, 1), Path("items"), StringLiteral("x"));
        right.Apply(insert);
        JsonOperation delete = right.DeleteIndex(B, Ts(5, 2), Path("items"), insert.ElementId);

        left.Apply(delete);
        left.Merge(right);
        right.Merge(left);

        await Assert.That(left.ToJson()).IsEqualTo("""{"items":[],"same":"new"}""");
        await Assert.That(right).IsEqualTo(left);
    }

    [Test]
    public async Task Compare_And_Operation_Duplicates_Use_All_Outcomes()
    {
        var empty = new JsonCrdt();
        var one = new JsonCrdt();
        one.SetString(A, Ts(1, 1), "a", "a");
        var other = new JsonCrdt();
        other.SetString(B, Ts(1, 2), "b", "b");

        await Assert.That(empty.Compare(empty.Clone())).IsEqualTo(CrdtOrder.Equal);
        await Assert.That(empty.Compare(one)).IsEqualTo(CrdtOrder.Less);
        await Assert.That(one.Compare(empty)).IsEqualTo(CrdtOrder.Greater);
        await Assert.That(one.Compare(other)).IsEqualTo(CrdtOrder.Concurrent);
        int oneHash = one.GetHashCode();
        int oneCloneHash = one.Clone().GetHashCode();
        await Assert.That(oneHash).IsEqualTo(oneCloneHash);
        JsonOperation op = one.SetString(A, Ts(2, 1), "duplicate", "x");
        await Assert.That(one.Apply(op)).IsFalse();
        await Assert.That(object.Equals(one, null)).IsFalse();
        await Assert.That(object.Equals(one, "not a crdt")).IsFalse();
    }

    private static JsonCrdt BuildNestedDocument()
    {
        var doc = new JsonCrdt();
        doc.SetObject(A, Ts(1, 1), Array.Empty<JsonPathSegment>(), "outer");
        doc.SetArray(A, Ts(2, 1), Path("outer"), "items");
        JsonOperation item = doc.PushObject(
            A,
            Ts(3, 1),
            [JsonPathSegment.MapKey("outer"), JsonPathSegment.MapKey("items")]);
        JsonPathSegment[] itemPath =
        [
            JsonPathSegment.MapKey("outer"),
            JsonPathSegment.MapKey("items"),
            JsonPathSegment.ListElement(item.ElementId),
        ];
        doc.SetString(A, Ts(4, 1), itemPath, "name", "a");
        doc.SetNumber(A, Ts(5, 1), itemPath, "score", 9D);
        doc.SetBoolean(A, Ts(6, 1), itemPath, "active", true);
        doc.SetNull(A, Ts(7, 1), itemPath, "none");
        return doc;
    }

    private static JsonLiteral NestedLiteral()
    {
        return JsonLiteral.Object(
        [
            Pair("name", StringLiteral("a")),
            Pair(
                "items",
                JsonLiteral.Array(
                [
                    NumberLiteral(1D),
                    JsonLiteral.Object([Pair("ok", BoolLiteral(true))]),
                ])),
        ]);
    }

    private static KeyValuePair<string, JsonLiteral> Pair(string key, JsonLiteral value) => new(key, value);

    private static JsonLiteral StringLiteral(string value) =>
        JsonLiteral.PrimitiveValue(JsonPrimitive.String(value));

    private static JsonLiteral NumberLiteral(double value) =>
        JsonLiteral.PrimitiveValue(JsonPrimitive.Number(value));

    private static JsonLiteral BoolLiteral(bool value) =>
        JsonLiteral.PrimitiveValue(JsonPrimitive.Boolean(value));

    private static JsonLiteral NullLiteral() => JsonLiteral.PrimitiveValue(JsonPrimitive.Null);

    private static JsonPathSegment[] Path(string key) => [JsonPathSegment.MapKey(key)];

    private static Dot DotOf(ulong replica, ulong sequence) => new(ReplicaId.FromUInt64(replica), sequence);

    private static Timestamp Ts(long wallClock, ulong replica) =>
        new(wallClock, 0UL, ReplicaId.FromUInt64(replica));
}
