// Copyright (c) marcschier. Licensed under the MIT License.

namespace Crdt;

internal static class ObservedFlagKernel
{
    public static Dot[] LiveDots(DotKernel<bool> kernel)
    {
        var dots = new List<Dot>(kernel.Count);
        foreach (KeyValuePair<Dot, bool> entry in kernel.SortedEntries())
        {
            dots.Add(entry.Key);
        }

        return [.. dots];
    }

    public static Dot[] SortDistinct(IEnumerable<Dot> dots)
    {
        Throw.IfNull(dots);
        var list = new List<Dot>(dots);
        list.Sort();
        int write = 0;
        for (int read = 0; read < list.Count; read++)
        {
            if (write == 0 || list[read] != list[write - 1])
            {
                list[write++] = list[read];
            }
        }

        if (write < list.Count)
        {
            list.RemoveRange(write, list.Count - write);
        }

        return [.. list];
    }

    public static bool SequenceEqual(IReadOnlyList<Dot> left, IReadOnlyList<Dot> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (int i = 0; i < left.Count; i++)
        {
            if (left[i] != right[i])
            {
                return false;
            }
        }

        return true;
    }

    public static void Write(DotKernel<bool> kernel, ref CrdtWriter writer)
    {
        writer.WriteVarUInt64((ulong)kernel.Count);
        foreach (KeyValuePair<Dot, bool> entry in kernel.SortedEntries())
        {
            writer.WriteDot(entry.Key);
        }

        kernel.Context.Write(ref writer);
    }

    public static DotKernel<bool> Read(ref CrdtReader reader)
    {
        int count = reader.ReadCount();
        var kernel = new DotKernel<bool>();
        for (int i = 0; i < count; i++)
        {
            kernel.Insert(reader.ReadDot(), true);
        }

        DotContext context = DotContext.Read(ref reader);
        kernel.Context.Merge(context);
        return kernel;
    }

    public static FlagKernelDto ToDto(DotKernel<bool> kernel) =>
        new(ToDto(LiveDots(kernel)), ToDto(ContextDots(kernel.Context)));

    public static DotKernel<bool> FromDto(FlagKernelDto dto)
    {
        var kernel = new DotKernel<bool>();
        foreach (FlagDotDto dot in dto.Entries)
        {
            kernel.Insert(FromDto(dot), true);
        }

        foreach (FlagDotDto dot in dto.Context)
        {
            kernel.Context.Add(FromDto(dot));
        }

        return kernel;
    }

    public static Dot[] ContextDots(DotContext context)
    {
        var dots = new List<Dot>();
        foreach (KeyValuePair<ReplicaId, ulong> entry in context.CompactEntries())
        {
            for (ulong sequence = 1; sequence <= entry.Value; sequence++)
            {
                dots.Add(new Dot(entry.Key, sequence));
            }
        }

        foreach (Dot dot in context.CloudDots())
        {
            dots.Add(dot);
        }

        dots.Sort();
        return [.. dots];
    }

    public static FlagDotDto[] ToDto(IReadOnlyList<Dot> dots)
    {
        var dto = new FlagDotDto[dots.Count];
        for (int i = 0; i < dots.Count; i++)
        {
            dto[i] = new FlagDotDto(dots[i].Replica, dots[i].Sequence);
        }

        return dto;
    }

    public static Dot FromDto(FlagDotDto dto) => new(dto.Replica, dto.Sequence);

    public static bool KernelEquals(DotKernel<bool> left, DotKernel<bool> right) =>
        SequenceEqual(LiveDots(left), LiveDots(right))
        && SequenceEqual(ContextDots(left.Context), ContextDots(right.Context));

    public static int KernelHashCode(DotKernel<bool> kernel)
    {
        var hash = new HashCode();
        foreach (Dot dot in LiveDots(kernel))
        {
            hash.Add(dot);
        }

        foreach (Dot dot in ContextDots(kernel.Context))
        {
            hash.Add(dot);
        }

        return hash.ToHashCode();
    }

    public static CrdtOrder Compare<T>(T left, T right)
        where T : IConvergent<T>, IEquatable<T>
    {
        T joined = left.Clone();
        joined.Merge(right);
        bool equalsLeft = joined.Equals(left);
        bool equalsRight = joined.Equals(right);
        return (equalsLeft, equalsRight) switch
        {
            (true, true) => CrdtOrder.Equal,
            (false, true) => CrdtOrder.Less,
            (true, false) => CrdtOrder.Greater,
            _ => CrdtOrder.Concurrent,
        };
    }

    public static DotKernel<bool> DeltaForAdded(Dot dot)
    {
        var delta = new DotKernel<bool>();
        delta.Insert(dot, true);
        return delta;
    }

    public static DotKernel<bool> DeltaForRemoved(IReadOnlyList<Dot> dots)
    {
        var delta = new DotKernel<bool>();
        foreach (Dot dot in dots)
        {
            delta.Context.Add(dot);
        }

        return delta;
    }
}
