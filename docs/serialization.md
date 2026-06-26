# Serialization

Every CRDT can be serialized to a compact **binary** format (the high-performance, NativeAOT-safe default) and to **JSON** (human-readable, interoperable). Both are reflection-free.

## Binary

Non-generic types expose `ToByteArray()` and a static `ReadFrom`:

```csharp
byte[] bytes = pnCounter.ToByteArray();
PNCounter restored = PNCounter.ReadFrom(bytes);
```

Generic types need a **value serializer** for their elements, keys, or values, because the library cannot know how to encode an arbitrary `T` without reflection:

```csharp
byte[] bytes = set.ToByteArray(CrdtValues.String);
GSet<string> restored = GSet<string>.ReadFrom(bytes, CrdtValues.String);
```

The format uses little-endian byte order and LEB128 variable-length integers, and identity-keyed collections are written in canonical (replica-sorted) order so equal states produce identical bytes.

To write into a buffer you already own, use the `IBufferWriter<byte>` overload:

```csharp
var buffer = new ArrayBufferWriter<byte>();
counter.WriteTo(buffer);            // non-generic
set.WriteTo(buffer, CrdtValues.String);  // generic
```

## JSON

JSON mirrors the binary API. Non-generic types expose `ToJson()`/`FromJson(string)`; generic types take the same value serializer:

```csharp
string json = counter.ToJson();
GCounter restored = GCounter.FromJson(json);

string setJson = set.ToJson(CrdtValues.Int64);
GSet<long> restoredSet = GSet<long>.FromJson(setJson, CrdtValues.Int64);
```

Non-generic types use `System.Text.Json` source generation, so JSON is AOT-safe with no runtime reflection. Generic types serialize their elements through the value serializer's `WriteJson`/`ReadJson` methods.

## Built-in value serializers

`CrdtValues` provides `ICrdtValueSerializer<T>` instances for the common element/value/key types:

| Property | Type |
| --- | --- |
| `CrdtValues.String` | `string` |
| `CrdtValues.Int64` | `long` |
| `CrdtValues.UInt64` | `ulong` |
| `CrdtValues.Int32` | `int` |
| `CrdtValues.Boolean` | `bool` |
| `CrdtValues.Guid` | `System.Guid` |
| `CrdtValues.Replica` | `ReplicaId` |

## Custom value types

Implement `ICrdtValueSerializer<T>` — four small methods that encode a single value in binary and JSON:

```csharp
public sealed class PointSerializer : ICrdtValueSerializer<Point>
{
    public void Write(ref CrdtWriter writer, Point value)
    {
        writer.WriteVarInt64(value.X);
        writer.WriteVarInt64(value.Y);
    }

    public Point Read(ref CrdtReader reader) =>
        new(checked((int)reader.ReadVarInt64()), checked((int)reader.ReadVarInt64()));

    public void WriteJson(Utf8JsonWriter writer, Point value)
    {
        writer.WriteStartObject();
        writer.WriteNumber("x", value.X);
        writer.WriteNumber("y", value.Y);
        writer.WriteEndObject();
    }

    public Point ReadJson(ref Utf8JsonReader reader)
    {
        // reader is positioned on the value's start token
        using var doc = JsonDocument.ParseValue(ref reader);
        return new(doc.RootElement.GetProperty("x").GetInt32(),
                   doc.RootElement.GetProperty("y").GetInt32());
    }
}
```

For CRDT-valued maps (`ORMap<TKey, TValue>` where the value is itself a CRDT), implement the richer `ICrdtValueOps<T>`, which extends `ICrdtValueSerializer<T>` with `CreateZero`, `Merge`, `Clone`, `AreEqual`, and `IsZero`.

## Decoding untrusted input

Deserialization is a trust boundary. Pass a `CrdtReaderOptions` to bound how much work a malformed or hostile payload can trigger:

```csharp
var options = new CrdtReaderOptions
{
    MaxCollectionCount = 10_000,
    MaxStringBytes = 64 * 1024,
    MaxDepth = 32,
};

GSet<string> set = GSet<string>.ReadFrom(bytes, CrdtValues.String, options);
```

Decoders fail fast with a `FormatException` when a limit is exceeded or the stream is truncated, rather than allocating unbounded memory.
