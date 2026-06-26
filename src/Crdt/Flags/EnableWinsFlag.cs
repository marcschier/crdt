// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace Crdt;

/// <summary>
/// An enable-wins observed flag: enabling adds a causal marker and disabling removes only
/// markers the replica has observed.
/// </summary>
/// <remarks>
/// A concurrent enable introduces a new unobserved marker, so it survives a concurrent
/// disable and the flag converges to enabled. Mutable and not thread-safe.
/// </remarks>
public sealed class EnableWinsFlag :
    IConvergent<EnableWinsFlag>,
    IDeltaConvergent<EnableWinsFlag, EnableWinsFlag>,
    IOperationConvergent<EnableWinsFlagOperation>,
    IBinaryWritable,
    IEquatable<EnableWinsFlag>
{
    private readonly DotKernel<bool> _kernel;
    private DotKernel<bool>? _delta;

    /// <summary>Initializes a disabled enable-wins flag.</summary>
    public EnableWinsFlag() => _kernel = new DotKernel<bool>();

    private EnableWinsFlag(DotKernel<bool> kernel) => _kernel = kernel;

    /// <summary>Gets a value indicating whether the flag is enabled.</summary>
    public bool Value => _kernel.Count > 0;

    /// <summary>Enables the flag on behalf of <paramref name="replica"/>.</summary>
    /// <param name="replica">The replica enabling the flag.</param>
    /// <returns>The enable operation to broadcast.</returns>
    public EnableWinsFlagOperation Enable(ReplicaId replica)
    {
        Dot dot = _kernel.Add(replica, true);
        _delta = ObservedFlagKernel.DeltaForAdded(dot);
        return EnableWinsFlagOperation.Enable(dot);
    }

    /// <summary>Disables the flag by removing all currently observed enable markers.</summary>
    /// <param name="replica">The replica disabling the flag.</param>
    /// <returns>The disable operation to broadcast.</returns>
    public EnableWinsFlagOperation Disable(ReplicaId replica)
    {
        _ = replica;
        Dot[] dots = ObservedFlagKernel.LiveDots(_kernel);
        foreach (Dot dot in dots)
        {
            _kernel.RemoveDot(dot);
        }

        if (dots.Length > 0)
        {
            _delta = ObservedFlagKernel.DeltaForRemoved(dots);
        }

        return EnableWinsFlagOperation.Disable(dots);
    }

    /// <inheritdoc/>
    public void Merge(EnableWinsFlag other)
    {
        Throw.IfNull(other);
        _kernel.Merge(other._kernel);
    }

    /// <inheritdoc/>
    public CrdtOrder Compare(EnableWinsFlag other)
    {
        Throw.IfNull(other);
        return ObservedFlagKernel.Compare(this, other);
    }

    /// <inheritdoc/>
    public EnableWinsFlag Clone() => new(_kernel.Clone());

    /// <inheritdoc/>
    public bool TryExtractDelta([MaybeNullWhen(false)] out EnableWinsFlag delta)
    {
        if (_delta is null)
        {
            delta = null;
            return false;
        }

        delta = new EnableWinsFlag(_delta.Clone());
        _delta = null;
        return true;
    }

    /// <inheritdoc/>
    public void MergeDelta(EnableWinsFlag delta)
    {
        Throw.IfNull(delta);
        Merge(delta);
    }

    /// <inheritdoc/>
    public bool Apply(EnableWinsFlagOperation operation)
    {
        if (operation.IsEnable)
        {
            return ApplyEnable(operation.Dot);
        }

        bool changed = false;
        foreach (Dot dot in operation.Dots)
        {
            if (!_kernel.Context.Contains(dot))
            {
                changed = true;
            }

            changed |= _kernel.RemoveDot(dot);
            _kernel.Context.Add(dot);
        }

        return changed;
    }

    /// <inheritdoc/>
    public void Write(ref CrdtWriter writer) => ObservedFlagKernel.Write(_kernel, ref writer);

    /// <summary>Decodes an <see cref="EnableWinsFlag"/> from its binary representation.</summary>
    /// <param name="data">The encoded bytes.</param>
    /// <param name="options">Optional decoding limits.</param>
    /// <returns>The decoded flag.</returns>
    public static EnableWinsFlag ReadFrom(ReadOnlySpan<byte> data, CrdtReaderOptions? options = null)
    {
        var reader = new CrdtReader(data, options);
        return new EnableWinsFlag(ObservedFlagKernel.Read(ref reader));
    }

    /// <summary>Serializes this flag to its canonical JSON representation.</summary>
    /// <returns>The JSON string.</returns>
    public string ToJson()
    {
        FlagKernelDto dto = ObservedFlagKernel.ToDto(_kernel);
        return JsonSerializer.Serialize(dto, CrdtFlagsJson.Default.FlagKernelDto);
    }

    /// <summary>Deserializes an <see cref="EnableWinsFlag"/> from JSON.</summary>
    /// <param name="json">The JSON string.</param>
    /// <returns>The decoded flag.</returns>
    public static EnableWinsFlag FromJson(string json)
    {
        Throw.IfNull(json);
        FlagKernelDto? dto = JsonSerializer.Deserialize(json, CrdtFlagsJson.Default.FlagKernelDto);
        return new EnableWinsFlag(dto is null ? new DotKernel<bool>() : ObservedFlagKernel.FromDto(dto));
    }

    /// <inheritdoc/>
    public bool Equals(EnableWinsFlag? other) =>
        other is not null && ObservedFlagKernel.KernelEquals(_kernel, other._kernel);

    /// <inheritdoc/>
    public override bool Equals([NotNullWhen(true)] object? obj) => Equals(obj as EnableWinsFlag);

    /// <inheritdoc/>
    public override int GetHashCode() => ObservedFlagKernel.KernelHashCode(_kernel);

    private bool ApplyEnable(Dot dot)
    {
        if (_kernel.Entries.ContainsKey(dot) || _kernel.Context.Contains(dot))
        {
            return false;
        }

        _kernel.Insert(dot, true);
        return true;
    }
}
