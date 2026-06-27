// Copyright (c) marcschier. Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace Crdt;

/// <summary>A single replica's bounded-counter slot, used for JSON serialization.</summary>
/// <param name="Replica">The replica.</param>
/// <param name="Value">The replica's accumulated value.</param>
internal readonly record struct BCounterEntryDto(ReplicaId Replica, ulong Value);

/// <summary>A rights-transfer slot in a bounded counter, used for JSON serialization.</summary>
/// <param name="From">The source replica.</param>
/// <param name="To">The destination replica.</param>
/// <param name="Value">The accumulated transfer value.</param>
internal readonly record struct BCounterTransferDto(ReplicaId From, ReplicaId To, ulong Value);

/// <summary>The JSON shape of a <see cref="BCounter"/>.</summary>
/// <param name="Min">The counter's lower bound.</param>
/// <param name="I">Per-replica increment totals.</param>
/// <param name="D">Per-replica decrement totals.</param>
/// <param name="T">Per-pair rights transfer totals.</param>
internal sealed record BCounterDto(
    long Min,
    BCounterEntryDto[] I,
    BCounterEntryDto[] D,
    BCounterTransferDto[] T);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(BCounterDto))]
internal sealed partial class CrdtBCounterJson : JsonSerializerContext;
