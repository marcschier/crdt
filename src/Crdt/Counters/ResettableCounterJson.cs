// Copyright (c) marcschier. Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace Crdt;

/// <summary>A dotted contribution in a <see cref="ResettableCounter"/> JSON payload.</summary>
/// <param name="Dot">The contribution dot.</param>
/// <param name="Amount">The signed contribution amount.</param>
internal readonly record struct ResettableCounterIncrementDto(ResettableCounterDotDto Dot, long Amount);

/// <summary>A dot in a <see cref="ResettableCounter"/> JSON payload.</summary>
/// <param name="Replica">The replica that produced the dot.</param>
/// <param name="Sequence">The dot's sequence number.</param>
internal readonly record struct ResettableCounterDotDto(ReplicaId Replica, ulong Sequence);

/// <summary>A compact context entry in a <see cref="ResettableCounter"/> JSON payload.</summary>
/// <param name="Replica">The replica represented by the compact prefix.</param>
/// <param name="Sequence">The highest contiguous sequence observed for the replica.</param>
internal readonly record struct ResettableCounterCompactDto(ReplicaId Replica, ulong Sequence);

/// <summary>The causal context in a <see cref="ResettableCounter"/> JSON payload.</summary>
/// <param name="Compact">The compact contiguous per-replica prefixes.</param>
/// <param name="Cloud">The out-of-order dot cloud.</param>
internal sealed record ResettableCounterContextDto(
    ResettableCounterCompactDto[] Compact,
    ResettableCounterDotDto[] Cloud);

/// <summary>The JSON shape of a <see cref="ResettableCounter"/>.</summary>
/// <param name="Increments">The live dotted contributions.</param>
/// <param name="Context">The causal context containing all observed dots.</param>
internal sealed record ResettableCounterDto(
    ResettableCounterIncrementDto[] Increments,
    ResettableCounterContextDto Context);

[JsonSerializable(typeof(ResettableCounterDto))]
internal sealed partial class CrdtResettableCounterJson : JsonSerializerContext;
