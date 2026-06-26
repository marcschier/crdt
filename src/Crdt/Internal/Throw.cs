// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Crdt;

/// <summary>
/// Centralised argument-validation helpers that behave identically on every target
/// framework (the BCL <c>ThrowIf*</c> helpers are not available on netstandard2.1).
/// </summary>
internal static class Throw
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void IfNull(
        [NotNull] object? argument,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null)
    {
        if (argument is null)
        {
            ThrowArgumentNull(paramName);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void IfNegative(
        long value,
        [CallerArgumentExpression(nameof(value))] string? paramName = null)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(paramName, value, "Value must be non-negative.");
        }
    }

    [DoesNotReturn]
    private static void ThrowArgumentNull(string? paramName) => throw new ArgumentNullException(paramName);

    [DoesNotReturn]
    public static void ArgumentOutOfRange(string? paramName, string message) =>
        throw new ArgumentOutOfRangeException(paramName, message);

    [DoesNotReturn]
    public static T InvalidData<T>(string message) => throw new FormatException(message);
}
