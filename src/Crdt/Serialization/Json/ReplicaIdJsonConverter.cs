// Copyright (c) marcschier. Licensed under the MIT License.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Crdt;

/// <summary>
/// A reflection-free, NativeAOT-safe <see cref="JsonConverter{T}"/> for <see cref="ReplicaId"/>.
/// Serializes a replica id as its canonical GUID string, both as a value and as a JSON
/// property name (so replica-keyed maps round-trip).
/// </summary>
public sealed class ReplicaIdJsonConverter : JsonConverter<ReplicaId>
{
    /// <inheritdoc/>
    public override ReplicaId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        string? text = reader.GetString();
        return text is null ? ReplicaId.Empty : ReplicaId.Parse(text);
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, ReplicaId value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Value);

    /// <inheritdoc/>
    public override ReplicaId ReadAsPropertyName(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options) => ReplicaId.Parse(reader.GetString()!);

    /// <inheritdoc/>
    public override void WriteAsPropertyName(
        Utf8JsonWriter writer,
        ReplicaId value,
        JsonSerializerOptions options) => writer.WritePropertyName(value.ToString());
}
