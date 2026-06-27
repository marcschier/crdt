// Copyright (c) marcschier. Licensed under the MIT License.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Crdt;

/// <summary>The JSON identity-tree shape for an <see cref="IntervalTreeClock"/>.</summary>
/// <param name="Leaf">The leaf value, either 0 or 1.</param>
/// <param name="Left">The left identity subtree for an internal node.</param>
/// <param name="Right">The right identity subtree for an internal node.</param>
internal sealed record IntervalTreeClockIdDto(int? Leaf, IntervalTreeClockIdDto? Left, IntervalTreeClockIdDto? Right);

/// <summary>The JSON event-tree shape for an <see cref="IntervalTreeClock"/>.</summary>
/// <param name="Value">The leaf event value.</param>
/// <param name="Base">The internal node's base event value.</param>
/// <param name="Left">The left event subtree for an internal node.</param>
/// <param name="Right">The right event subtree for an internal node.</param>
internal sealed record IntervalTreeClockEventDto(
    uint? Value,
    uint? Base,
    IntervalTreeClockEventDto? Left,
    IntervalTreeClockEventDto? Right);

/// <summary>The JSON shape of an <see cref="IntervalTreeClock"/> stamp.</summary>
/// <param name="Id">The identity tree.</param>
/// <param name="Event">The event tree.</param>
internal sealed record IntervalTreeClockDto(IntervalTreeClockIdDto Id, IntervalTreeClockEventDto Event);

public sealed partial class IntervalTreeClock
{
    /// <summary>Serializes this clock to its canonical JSON representation.</summary>
    /// <returns>The JSON string.</returns>
    public string ToJson()
    {
        var dto = new IntervalTreeClockDto(ToDto(_id), ToDto(_event));
        return JsonSerializer.Serialize(dto, IntervalTreeClockJson.Default.IntervalTreeClockDto);
    }

    /// <summary>Deserializes an Interval Tree Clock from JSON.</summary>
    /// <param name="json">The JSON string.</param>
    /// <returns>The decoded clock.</returns>
    public static IntervalTreeClock FromJson(string json)
    {
        Throw.IfNull(json);
        IntervalTreeClockDto dto =
            JsonSerializer.Deserialize(json, IntervalTreeClockJson.Default.IntervalTreeClockDto)
            ?? Throw.InvalidData<IntervalTreeClockDto>("Missing Interval Tree Clock JSON value.");
        return new IntervalTreeClock(FromDto(dto.Id), FromDto(dto.Event));
    }

    private static IntervalTreeClockIdDto ToDto(ItcId id)
    {
        if (id is ItcIdLeaf leaf)
        {
            return new IntervalTreeClockIdDto(leaf.Value ? 1 : 0, null, null);
        }

        var node = (ItcIdNode)id;
        return new IntervalTreeClockIdDto(null, ToDto(node.Left), ToDto(node.Right));
    }

    private static IntervalTreeClockEventDto ToDto(ItcEvent @event)
    {
        if (@event is ItcEventLeaf leaf)
        {
            return new IntervalTreeClockEventDto(leaf.Value, null, null, null);
        }

        var node = (ItcEventNode)@event;
        return new IntervalTreeClockEventDto(null, node.Value, ToDto(node.Left), ToDto(node.Right));
    }

    private static ItcId FromDto(IntervalTreeClockIdDto dto)
    {
        Throw.IfNull(dto);
        if (dto.Leaf.HasValue)
        {
            return dto.Leaf.Value switch
            {
                0 => ItcId.Zero,
                1 => ItcId.One,
                _ => Throw.InvalidData<ItcId>("Invalid Interval Tree Clock identity JSON leaf."),
            };
        }

        if (dto.Left is null || dto.Right is null)
        {
            return Throw.InvalidData<ItcId>("Invalid Interval Tree Clock identity JSON node.");
        }

        return ItcId.Node(FromDto(dto.Left), FromDto(dto.Right));
    }

    private static ItcEvent FromDto(IntervalTreeClockEventDto dto)
    {
        Throw.IfNull(dto);
        if (dto.Value.HasValue)
        {
            return ItcEvent.Leaf(dto.Value.Value);
        }

        if (!dto.Base.HasValue || dto.Left is null || dto.Right is null)
        {
            return Throw.InvalidData<ItcEvent>("Invalid Interval Tree Clock event JSON node.");
        }

        return ItcEvent.Node(dto.Base.Value, FromDto(dto.Left), FromDto(dto.Right));
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(IntervalTreeClockDto))]
internal sealed partial class IntervalTreeClockJson : JsonSerializerContext;
