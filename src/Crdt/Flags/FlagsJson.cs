// Copyright (c) marcschier. Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace Crdt;

internal sealed class GFlagDto
{
}

internal readonly record struct FlagDotDto(ReplicaId Replica, ulong Sequence);

internal sealed record FlagKernelDto(FlagDotDto[] Entries, FlagDotDto[] Context);

[JsonSerializable(typeof(GFlagDto))]
[JsonSerializable(typeof(FlagKernelDto))]
internal sealed partial class CrdtFlagsJson : JsonSerializerContext;
