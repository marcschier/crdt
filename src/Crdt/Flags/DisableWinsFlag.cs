// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace Crdt;

/// <summary>
/// A disable-wins observed flag: disabling adds a causal marker and enabling removes only
/// disable markers the replica has observed.
/// </summary>
/// <remarks>
/// A concurrent disable introduces a new unobserved marker, so it survives a concurrent
/// enable and the flag converges to disabled. Mutable and not thread-safe.
/// </remarks>
public sealed class DisableWinsFlag :
    IConvergent<DisableWinsFlag>,
    IDeltaConvergent<DisableWinsFlag, DisableWinsFlag>,
    IOperationConvergent<DisableWinsFlagOperation>,
    IBinaryWritable,
    IEquatable<DisableWinsFlag>
{
    private readonly DotKernel<bool> _kernel;
    private DotKernel<bool>? _delta;

    /// <summary>Initializes an enabled disable-wins flag.</summary>
    public DisableWinsFlag() => _kernel = new DotKernel<bool>();

    private DisableWinsFlag(DotKernel<bool> kernel) => _kernel = kernel;

    /// <summary>Gets a value indicating whether the flag is enabled.</summary>
    public bool Value => _kernel.Count == 0;

    /// <summary>Enables the flag by removing all currently observed disable markers.</summary>
    /// <param name="replica">The replica enabling the flag.</param>
    /// <returns>The enable operation to broadcast.</returns>
    public DisableWinsFlagOperation Enable(ReplicaId replica)
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

        return DisableWinsFlagOperation.Enable(dots);
    }

    /// <summary>Disables the flag on behalf of <paramref name="replica"/>.</summary>
    /// <param name="replica">The replica disabling the flag.</param>
    /// <returns>The disable operation to broadcast.</returns>
    public DisableWinsFlagOperation Disable(ReplicaId replica)
    {
        Dot dot = _kernel.Add(replica, true);
        _delta = ObservedFlagKernel.DeltaForAdded(dot);
        return DisableWinsFlagOperation.Disable(dot);
    }

    /// <inheritdoc/>
    public void Merge(DisableWinsFlag other)
    {
        Throw.IfNull(other);
        _kernel.Merge(other._kernel);
    }

    /// <inheritdoc/>
    public CrdtOrder Compare(DisableWinsFlag other)
    {
        Throw.IfNull(other);
        return ObservedFlagKernel.Compare(this, other);
    }

    /// <inheritdoc/>
    public DisableWinsFlag Clone() => new(_kernel.Clone());

    /// <inheritdoc/>
    public bool TryExtractDelta([MaybeNullWhen(false)] out DisableWinsFlag delta)
    {
        if (_delta is null)
        {
            delta = null;
            return false;
        }

        delta = new DisableWinsFlag(_delta.Clone());
        _delta = null;
        return true;
    }

    /// <inheritdoc/>
    public void MergeDelta(DisableWinsFlag delta)
    {
        Throw.IfNull(delta);
        Merge(delta);
    }

    /// <inheritdoc/>
    public bool Apply(DisableWinsFlagOperation operation)
    {
        if (!operation.IsEnable)
        {
            return ApplyDisable(operation.Dot);
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

    /// <summary>Decodes a <see cref="DisableWinsFlag"/> from its binary representation.</summary>
    /// <param name="data">The encoded bytes.</param>
    /// <param name="options">Optional decoding limits.</param>
    /// <returns>The decoded flag.</returns>
    public static DisableWinsFlag ReadFrom(ReadOnlySpan<byte> data, CrdtReaderOptions? options = null)
    {
        var reader = new CrdtReader(data, options);
        return new DisableWinsFlag(ObservedFlagKernel.Read(ref reader));
    }

    /// <summary>Serializes this flag to its canonical JSON representation.</summary>
    /// <returns>The JSON string.</returns>
    public string ToJson()
    {
        FlagKernelDto dto = ObservedFlagKernel.ToDto(_kernel);
        return JsonSerializer.Serialize(dto, CrdtFlagsJson.Default.FlagKernelDto);
    }

    /// <summary>Deserializes a <see cref="DisableWinsFlag"/> from JSON.</summary>
    /// <param name="json">The JSON string.</param>
    /// <returns>The decoded flag.</returns>
    public static DisableWinsFlag FromJson(string json)
    {
        Throw.IfNull(json);
        FlagKernelDto? dto = JsonSerializer.Deserialize(json, CrdtFlagsJson.Default.FlagKernelDto);
        return new DisableWinsFlag(dto is null ? new DotKernel<bool>() : ObservedFlagKernel.FromDto(dto));
    }

    /// <inheritdoc/>
    public bool Equals(DisableWinsFlag? other) =>
        other is not null && ObservedFlagKernel.KernelEquals(_kernel, other._kernel);

    /// <inheritdoc/>
    public override bool Equals([NotNullWhen(true)] object? obj) => Equals(obj as DisableWinsFlag);

    /// <inheritdoc/>
    public override int GetHashCode() => ObservedFlagKernel.KernelHashCode(_kernel);

    private bool ApplyDisable(Dot dot)
    {
        if (_kernel.Entries.ContainsKey(dot) || _kernel.Context.Contains(dot))
        {
            return false;
        }

        _kernel.Insert(dot, true);
        return true;
    }
}
