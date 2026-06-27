// Copyright (c) marcschier. Licensed under the MIT License.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Crdt;

/// <summary>The JSON shape of a <see cref="HandoffCounter"/>.</summary>
internal sealed record HandoffCounterDto(
    ReplicaId Id,
    int Tier,
    ulong Val,
    HandoffCounterEntryDto[] Vec,
    HandoffCounterEntryDto[] Below,
    HandoffCounterSlotDto[] Slots,
    HandoffCounterTokenDto[] Tokens,
    ulong Sck,
    ulong Dck);

internal readonly record struct HandoffCounterEntryDto(ReplicaId Replica, ulong Value);

internal readonly record struct HandoffCounterSlotDto(
    ReplicaId Replica,
    int Tier,
    ulong SourceClock,
    ulong DestinationClock);

internal readonly record struct HandoffCounterTokenDto(
    ReplicaId Replica,
    int Tier,
    ulong Value,
    ulong SourceClock,
    ulong DestinationClock);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(HandoffCounterDto))]
internal sealed partial class CrdtHandoffCounterJson : JsonSerializerContext;

public sealed partial class HandoffCounter
{
    /// <summary>Serializes this counter to its canonical JSON representation.</summary>
    /// <returns>The JSON string.</returns>
    public string ToJson() => JsonSerializer.Serialize(ToDto(), CrdtHandoffCounterJson.Default.HandoffCounterDto);

    /// <summary>Deserializes a <see cref="HandoffCounter"/> from JSON.</summary>
    /// <param name="json">The JSON string.</param>
    /// <returns>The decoded counter.</returns>
    public static HandoffCounter FromJson(string json)
    {
        Throw.IfNull(json);
        HandoffCounterDto? dto = JsonSerializer.Deserialize(json, CrdtHandoffCounterJson.Default.HandoffCounterDto);
        if (dto is null)
        {
            Throw.InvalidData<HandoffCounter>("Handoff counter JSON payload was null.");
        }

        return FromDto(dto);
    }

    private HandoffCounterDto ToDto() =>
        new(Id, Tier, _val, ToEntries(_vec), ToEntries(_below), ToSlots(), ToTokens(), _sck, _dck);

    private static HandoffCounter FromDto(HandoffCounterDto dto) =>
        new(
            dto.Id,
            dto.Tier,
            dto.Val,
            FromEntries(dto.Vec),
            FromEntries(dto.Below),
            FromSlots(dto.Slots),
            FromTokens(dto.Tokens),
            dto.Sck,
            dto.Dck);

    private static HandoffCounterEntryDto[] ToEntries(Dictionary<ReplicaId, ulong> entries)
    {
        var result = new List<HandoffCounterEntryDto>();
        foreach (KeyValuePair<ReplicaId, ulong> entry in SortedEntries(entries))
        {
            result.Add(new HandoffCounterEntryDto(entry.Key, entry.Value));
        }

        return [.. result];
    }

    private HandoffCounterSlotDto[] ToSlots()
    {
        var result = new List<HandoffCounterSlotDto>();
        foreach (KeyValuePair<ReplicaId, HandoffSlot> entry in SortedSlots())
        {
            result.Add(
                new HandoffCounterSlotDto(
                    entry.Key,
                    entry.Value.Tier,
                    entry.Value.SourceClock,
                    entry.Value.DestinationClock));
        }

        return [.. result];
    }

    private HandoffCounterTokenDto[] ToTokens()
    {
        var result = new List<HandoffCounterTokenDto>();
        foreach (KeyValuePair<ReplicaId, HandoffToken> entry in SortedTokens())
        {
            result.Add(
                new HandoffCounterTokenDto(
                    entry.Key,
                    entry.Value.Tier,
                    entry.Value.Value,
                    entry.Value.SourceClock,
                    entry.Value.DestinationClock));
        }

        return [.. result];
    }

    private static Dictionary<ReplicaId, ulong> FromEntries(HandoffCounterEntryDto[]? entries)
    {
        var result = new Dictionary<ReplicaId, ulong>();
        if (entries is null)
        {
            return result;
        }

        foreach (HandoffCounterEntryDto entry in entries)
        {
            result[entry.Replica] = entry.Value;
        }

        return result;
    }

    private static Dictionary<ReplicaId, HandoffSlot> FromSlots(HandoffCounterSlotDto[]? slots)
    {
        var result = new Dictionary<ReplicaId, HandoffSlot>();
        if (slots is null)
        {
            return result;
        }

        foreach (HandoffCounterSlotDto slot in slots)
        {
            result[slot.Replica] = new HandoffSlot(slot.Tier, slot.SourceClock, slot.DestinationClock);
        }

        return result;
    }

    private static Dictionary<ReplicaId, HandoffToken> FromTokens(HandoffCounterTokenDto[]? tokens)
    {
        var result = new Dictionary<ReplicaId, HandoffToken>();
        if (tokens is null)
        {
            return result;
        }

        foreach (HandoffCounterTokenDto token in tokens)
        {
            result[token.Replica] =
                new HandoffToken(token.Tier, token.Value, token.SourceClock, token.DestinationClock);
        }

        return result;
    }
}
