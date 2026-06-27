// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;

namespace Crdt;

/// <summary>
/// An Interval Tree Clock stamp: a compact, anonymous causal clock whose identity can be
/// forked and joined without assigning globally unique replica identifiers.
/// </summary>
/// <remarks>
/// The stamp is a mutable CRDT shell around immutable interval-tree identity and event
/// components. Local operations return new stamps; <see cref="Merge"/> mutates this instance
/// by joining another stamp into it.
/// </remarks>
public sealed partial class IntervalTreeClock :
    IConvergent<IntervalTreeClock>,
    IEquatable<IntervalTreeClock>,
    IBinaryWritable
{
    private ItcId _id;
    private ItcEvent _event;

    private IntervalTreeClock(ItcId id, ItcEvent @event)
    {
        _id = id.Normalize();
        _event = @event.Normalize();
    }

    /// <summary>Creates the initial Interval Tree Clock stamp <c>(1, 0)</c>.</summary>
    /// <returns>The seed stamp.</returns>
    public static IntervalTreeClock Seed() => new(ItcId.One, ItcEvent.Zero);

    /// <summary>
    /// Splits this stamp's identity into two disjoint identities that share the same causal
    /// event history.
    /// </summary>
    /// <returns>The two forked stamps.</returns>
    public (IntervalTreeClock a, IntervalTreeClock b) Fork()
    {
        (ItcId left, ItcId right) = _id.Split();
        return (new IntervalTreeClock(left, _event), new IntervalTreeClock(right, _event));
    }

    /// <summary>
    /// Produces a new stamp by recording one local event at this stamp's identity.
    /// </summary>
    /// <returns>The advanced stamp.</returns>
    public IntervalTreeClock Event()
    {
        ItcEvent filled = _event.Fill(_id).Normalize();
        ItcEvent next = filled.Equals(_event) ? _event.Grow(_id).Normalize() : filled;
        return new IntervalTreeClock(_id, next);
    }

    /// <summary>
    /// Computes the least upper bound of this stamp and <paramref name="other"/>.
    /// </summary>
    /// <param name="other">The stamp to join with this stamp.</param>
    /// <returns>The joined stamp.</returns>
    public IntervalTreeClock Join(IntervalTreeClock other)
    {
        Throw.IfNull(other);
        return new IntervalTreeClock(_id.Sum(other._id), _event.Join(other._event));
    }

    /// <inheritdoc/>
    public void Merge(IntervalTreeClock other)
    {
        IntervalTreeClock joined = Join(other);
        _id = joined._id;
        _event = joined._event;
    }

    /// <summary>
    /// Determines whether this stamp's event history is less than or equal to
    /// <paramref name="other"/>'s event history.
    /// </summary>
    /// <param name="other">The stamp to compare with this stamp.</param>
    /// <returns><see langword="true"/> when this stamp happened before or equals <paramref name="other"/>.</returns>
    public bool Leq(IntervalTreeClock other)
    {
        Throw.IfNull(other);
        return _event.Leq(other._event);
    }

    /// <inheritdoc/>
    public CrdtOrder Compare(IntervalTreeClock other)
    {
        Throw.IfNull(other);
        bool lessOrEqual = Leq(other);
        bool greaterOrEqual = other.Leq(this);

        return (lessOrEqual, greaterOrEqual) switch
        {
            (true, true) => CrdtOrder.Equal,
            (true, false) => CrdtOrder.Less,
            (false, true) => CrdtOrder.Greater,
            _ => CrdtOrder.Concurrent,
        };
    }

    /// <inheritdoc/>
    public IntervalTreeClock Clone() => new(_id, _event);

    /// <inheritdoc/>
    public void Write(ref CrdtWriter writer)
    {
        _id.Write(ref writer);
        _event.Write(ref writer);
    }

    /// <summary>Decodes an Interval Tree Clock from its binary representation.</summary>
    /// <param name="data">The encoded bytes.</param>
    /// <param name="options">Optional decoding limits.</param>
    /// <returns>The decoded clock.</returns>
    public static IntervalTreeClock ReadFrom(ReadOnlySpan<byte> data, CrdtReaderOptions? options = null)
    {
        var reader = new CrdtReader(data, options);
        return Read(ref reader);
    }

    internal static IntervalTreeClock Read(ref CrdtReader reader)
    {
        ItcId id = ItcId.Read(ref reader);
        ItcEvent @event = ItcEvent.Read(ref reader);
        return new IntervalTreeClock(id, @event);
    }

    /// <inheritdoc/>
    public bool Equals(IntervalTreeClock? other) =>
        other is not null && _id.Equals(other._id) && _event.Equals(other._event);

    /// <inheritdoc/>
    public override bool Equals([NotNullWhen(true)] object? obj) => Equals(obj as IntervalTreeClock);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(_id, _event);
}

internal abstract class ItcId : IEquatable<ItcId>
{
    public static readonly ItcId Zero = new ItcIdLeaf(false);
    public static readonly ItcId One = new ItcIdLeaf(true);

    public abstract bool IsZero { get; }

    public abstract bool IsOne { get; }

    public abstract ItcId Normalize();

    public abstract (ItcId Left, ItcId Right) Split();

    public abstract ItcId Sum(ItcId other);

    public abstract void Write(ref CrdtWriter writer);

    public abstract bool Equals(ItcId? other);

    public override bool Equals([NotNullWhen(true)] object? obj) => Equals(obj as ItcId);

    public abstract override int GetHashCode();

    public static ItcId Node(ItcId left, ItcId right)
    {
        Throw.IfNull(left);
        Throw.IfNull(right);
        return new ItcIdNode(left, right).Normalize();
    }

    public static ItcId Read(ref CrdtReader reader)
    {
        byte tag = reader.ReadByte();
        if (tag == 0)
        {
            byte value = reader.ReadByte();
            return value switch
            {
                0 => Zero,
                1 => One,
                _ => Throw.InvalidData<ItcId>("Invalid Interval Tree Clock identity leaf."),
            };
        }

        if (tag == 1)
        {
            ItcId left = Read(ref reader);
            ItcId right = Read(ref reader);
            return Node(left, right);
        }

        return Throw.InvalidData<ItcId>("Invalid Interval Tree Clock identity node tag.");
    }
}

internal sealed class ItcIdLeaf : ItcId
{
    public ItcIdLeaf(bool value) => Value = value;

    public bool Value { get; }

    public override bool IsZero => !Value;

    public override bool IsOne => Value;

    public override ItcId Normalize() => Value ? One : Zero;

    public override (ItcId Left, ItcId Right) Split() =>
        Value ? (Node(One, Zero), Node(Zero, One)) : (Zero, Zero);

    public override ItcId Sum(ItcId other)
    {
        Throw.IfNull(other);
        if (IsZero)
        {
            return other;
        }

        return other.IsZero ? this : One;
    }

    public override void Write(ref CrdtWriter writer)
    {
        writer.WriteByte(0);
        writer.WriteByte(Value ? (byte)1 : (byte)0);
    }

    public override bool Equals(ItcId? other) => other is ItcIdLeaf leaf && leaf.Value == Value;

    public override int GetHashCode() => Value ? 1 : 0;
}

internal sealed class ItcIdNode : ItcId
{
    public ItcIdNode(ItcId left, ItcId right)
    {
        Throw.IfNull(left);
        Throw.IfNull(right);
        Left = left;
        Right = right;
    }

    public ItcId Left { get; }

    public ItcId Right { get; }

    public override bool IsZero => false;

    public override bool IsOne => false;

    public override ItcId Normalize()
    {
        ItcId left = Left.Normalize();
        ItcId right = Right.Normalize();
        if (left.IsZero && right.IsZero)
        {
            return Zero;
        }

        if (left.IsOne && right.IsOne)
        {
            return One;
        }

        return new ItcIdNode(left, right);
    }

    public override (ItcId Left, ItcId Right) Split()
    {
        if (!Left.IsZero)
        {
            (ItcId left, ItcId right) = Left.Split();
            return (Node(left, Right), Node(right, Zero));
        }

        (ItcId rightLeft, ItcId rightRight) = Right.Split();
        return (Node(Zero, rightLeft), Node(Zero, rightRight));
    }

    public override ItcId Sum(ItcId other)
    {
        Throw.IfNull(other);
        if (other.IsZero)
        {
            return this;
        }

        if (other is not ItcIdNode node)
        {
            return other.IsOne ? One : this;
        }

        return Node(Left.Sum(node.Left), Right.Sum(node.Right));
    }

    public override void Write(ref CrdtWriter writer)
    {
        writer.WriteByte(1);
        Left.Write(ref writer);
        Right.Write(ref writer);
    }

    public override bool Equals(ItcId? other) =>
        other is ItcIdNode node && Left.Equals(node.Left) && Right.Equals(node.Right);

    public override int GetHashCode() => HashCode.Combine(Left, Right);
}

internal abstract class ItcEvent : IEquatable<ItcEvent>
{
    public static readonly ItcEvent Zero = new ItcEventLeaf(0);

    public abstract uint Minimum { get; }

    public abstract uint Maximum { get; }

    public abstract int NodeCount { get; }

    public abstract ItcEvent Add(uint value);

    public abstract ItcEvent Normalize();

    public abstract ItcEvent Join(ItcEvent other);

    public abstract ItcEvent Fill(ItcId id);

    public abstract ItcEvent Grow(ItcId id);

    public abstract void Write(ref CrdtWriter writer);

    public abstract bool Equals(ItcEvent? other);

    public bool Leq(ItcEvent other)
    {
        Throw.IfNull(other);
        return Join(other).Equals(other.Normalize());
    }

    public override bool Equals([NotNullWhen(true)] object? obj) => Equals(obj as ItcEvent);

    public abstract override int GetHashCode();

    public static ItcEvent Leaf(uint value) => value == 0 ? Zero : new ItcEventLeaf(value);

    public static ItcEvent Node(uint value, ItcEvent left, ItcEvent right)
    {
        Throw.IfNull(left);
        Throw.IfNull(right);
        return new ItcEventNode(value, left, right).Normalize();
    }

    public static ItcEvent Read(ref CrdtReader reader)
    {
        byte tag = reader.ReadByte();
        if (tag == 0)
        {
            return Leaf(reader.ReadVarUInt32());
        }

        if (tag == 1)
        {
            uint value = reader.ReadVarUInt32();
            ItcEvent left = Read(ref reader);
            ItcEvent right = Read(ref reader);
            return Node(value, left, right);
        }

        return Throw.InvalidData<ItcEvent>("Invalid Interval Tree Clock event node tag.");
    }

    protected static ItcEvent JoinExpanded(ItcEvent left, ItcEvent right)
    {
        ItcEventNode leftNode = left.Expand();
        ItcEventNode rightNode = right.Expand();
        uint value = Math.Min(leftNode.Value, rightNode.Value);
        ItcEvent joinedLeft = leftNode.Left.Add(leftNode.Value - value).Join(
            rightNode.Left.Add(rightNode.Value - value));
        ItcEvent joinedRight = leftNode.Right.Add(leftNode.Value - value).Join(
            rightNode.Right.Add(rightNode.Value - value));

        return Node(value, joinedLeft, joinedRight);
    }

    protected abstract ItcEventNode Expand();
}

internal sealed class ItcEventLeaf : ItcEvent
{
    public ItcEventLeaf(uint value) => Value = value;

    public uint Value { get; }

    public override uint Minimum => Value;

    public override uint Maximum => Value;

    public override int NodeCount => 1;

    public override ItcEvent Add(uint value) => Leaf(checked(Value + value));

    public override ItcEvent Normalize() => Leaf(Value);

    public override ItcEvent Join(ItcEvent other)
    {
        Throw.IfNull(other);
        return other is ItcEventLeaf leaf ? Leaf(Math.Max(Value, leaf.Value)) : JoinExpanded(this, other);
    }

    public override ItcEvent Fill(ItcId id)
    {
        Throw.IfNull(id);
        return this;
    }

    public override ItcEvent Grow(ItcId id)
    {
        Throw.IfNull(id);
        if (id.IsZero)
        {
            return this;
        }

        if (id.IsOne)
        {
            return Leaf(checked(Value + 1));
        }

        var node = (ItcIdNode)id;
        if (node.Left.IsZero)
        {
            return Node(Value, Zero, Zero.Grow(node.Right));
        }

        if (node.Right.IsZero)
        {
            return Node(Value, Zero.Grow(node.Left), Zero);
        }

        return Node(Value, Zero.Grow(node.Left), Zero);
    }

    public override void Write(ref CrdtWriter writer)
    {
        writer.WriteByte(0);
        writer.WriteVarUInt32(Value);
    }

    public override bool Equals(ItcEvent? other) => other is ItcEventLeaf leaf && leaf.Value == Value;

    public override int GetHashCode() => HashCode.Combine(0, Value);

    protected override ItcEventNode Expand() => new(Value, Zero, Zero);
}

internal sealed class ItcEventNode : ItcEvent
{
    public ItcEventNode(uint value, ItcEvent left, ItcEvent right)
    {
        Throw.IfNull(left);
        Throw.IfNull(right);
        Value = value;
        Left = left;
        Right = right;
    }

    public uint Value { get; }

    public ItcEvent Left { get; }

    public ItcEvent Right { get; }

    public override uint Minimum => checked(Value + Math.Min(Left.Minimum, Right.Minimum));

    public override uint Maximum => checked(Value + Math.Max(Left.Maximum, Right.Maximum));

    public override int NodeCount => 1 + Left.NodeCount + Right.NodeCount;

    public override ItcEvent Add(uint value) => new ItcEventNode(checked(Value + value), Left, Right).Normalize();

    public override ItcEvent Normalize()
    {
        ItcEvent left = Left.Normalize();
        ItcEvent right = Right.Normalize();
        if (left is ItcEventLeaf leftLeaf && right is ItcEventLeaf rightLeaf && leftLeaf.Value == rightLeaf.Value)
        {
            return Leaf(checked(Value + leftLeaf.Value));
        }

        uint sink = Math.Min(left.Minimum, right.Minimum);
        if (sink != 0)
        {
            left = Subtract(left, sink);
            right = Subtract(right, sink);
            return new ItcEventNode(checked(Value + sink), left, right).Normalize();
        }

        return new ItcEventNode(Value, left, right);
    }

    public override ItcEvent Join(ItcEvent other)
    {
        Throw.IfNull(other);
        return JoinExpanded(this, other);
    }

    public override ItcEvent Fill(ItcId id)
    {
        Throw.IfNull(id);
        if (id.IsZero)
        {
            return this;
        }

        if (id.IsOne)
        {
            return Leaf(Maximum);
        }

        var node = (ItcIdNode)id;
        ItcEvent left = Left.Fill(node.Left);
        ItcEvent right = Right.Fill(node.Right);

        if (node.Left.IsOne)
        {
            left = Leaf(Math.Max(left.Maximum, right.Maximum));
        }

        if (node.Right.IsOne)
        {
            right = Leaf(Math.Max(left.Maximum, right.Maximum));
        }

        return Node(Value, left, right);
    }

    public override ItcEvent Grow(ItcId id)
    {
        Throw.IfNull(id);
        if (id.IsZero)
        {
            return this;
        }

        if (id.IsOne)
        {
            return Leaf(checked(Maximum + 1));
        }

        var node = (ItcIdNode)id;
        if (node.Left.IsZero)
        {
            return Node(Value, Left, Right.Grow(node.Right));
        }

        if (node.Right.IsZero)
        {
            return Node(Value, Left.Grow(node.Left), Right);
        }

        ItcEvent growLeft = Node(Value, Left.Grow(node.Left), Right);
        ItcEvent growRight = Node(Value, Left, Right.Grow(node.Right));
        return growLeft.NodeCount <= growRight.NodeCount ? growLeft : growRight;
    }

    public override void Write(ref CrdtWriter writer)
    {
        writer.WriteByte(1);
        writer.WriteVarUInt32(Value);
        Left.Write(ref writer);
        Right.Write(ref writer);
    }

    public override bool Equals(ItcEvent? other) =>
        other is ItcEventNode node
        && node.Value == Value
        && Left.Equals(node.Left)
        && Right.Equals(node.Right);

    public override int GetHashCode() => HashCode.Combine(Value, Left, Right);

    protected override ItcEventNode Expand() => this;

    private static ItcEvent Subtract(ItcEvent value, uint amount)
    {
        if (amount == 0)
        {
            return value;
        }

        if (value is ItcEventLeaf leaf)
        {
            return Leaf(leaf.Value - amount);
        }

        var node = (ItcEventNode)value;
        return new ItcEventNode(node.Value - amount, node.Left, node.Right).Normalize();
    }
}
