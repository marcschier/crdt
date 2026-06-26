// Copyright (c) marcschier. Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace Crdt;

/// <summary>A single replica's slot in a counter, used for JSON serialization.</summary>
/// <param name="Replica">The replica.</param>
/// <param name="Value">The replica's accumulated value.</param>
internal readonly record struct CounterEntryDto(ReplicaId Replica, ulong Value);

/// <summary>The JSON shape of a <see cref="PNCounter"/>: positive and negative slots.</summary>
/// <param name="P">Per-replica increment totals.</param>
/// <param name="N">Per-replica decrement totals.</param>
internal sealed record PNCounterDto(CounterEntryDto[] P, CounterEntryDto[] N);

[JsonSerializable(typeof(CounterEntryDto[]))]
[JsonSerializable(typeof(PNCounterDto))]
internal sealed partial class CrdtJson;
