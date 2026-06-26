// Copyright (c) marcschier. Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace Crdt;

/// <summary>
/// The shared <see cref="JsonSerializerContext"/> for the CRDT JSON format. It is a partial
/// type: each CRDT family contributes its data-transfer types via additional
/// <see cref="JsonSerializableAttribute"/> declarations on <c>partial class CrdtJson</c> in
/// its own file. Source generation keeps the JSON path reflection-free and NativeAOT-safe.
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class CrdtJson : JsonSerializerContext;
