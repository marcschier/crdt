// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
#if !NET8_0_OR_GREATER
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
#endif

namespace Crdt.Transport;

/// <summary>Shared pseudo-random helper that uses <c>Random.Shared</c> where available.</summary>
internal static class SharedRandom
{
#if !NET6_0_OR_GREATER
    private static readonly ThreadLocal<Random> Local = new(static () =>
        new Random(unchecked((Environment.TickCount * 397) ^ Environment.CurrentManagedThreadId)));
#endif

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Next(int maxValue) =>
#if NET6_0_OR_GREATER
        Random.Shared.Next(maxValue);
#else
        Local.Value!.Next(maxValue);
#endif
}

/// <summary>Argument-validation helpers that map to the BCL guard methods where available.</summary>
internal static class Throw
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void IfNull([NotNull] object? argument, string? paramName)
    {
#if NET6_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(argument, paramName);
#else
        if (argument is null)
        {
            throw new ArgumentNullException(paramName);
        }
#endif
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void IfNegativeOrZero(int value, string? paramName)
    {
#if NET8_0_OR_GREATER
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value, paramName);
#else
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(paramName, value, "Value must be positive.");
        }
#endif
    }
}

#if !NET8_0_OR_GREATER
/// <summary>
/// netstandard polyfills for the Memory-based socket async overloads and cancellation helpers that
/// exist in-box on net8+. Compiled only for netstandard; on net8+ the framework methods are used
/// directly, so those targets are entirely unaffected.
/// </summary>
internal static class NetstandardNetworkPolyfills
{
    public static ValueTask<int> SendToAsync(
        this Socket socket,
        ReadOnlyMemory<byte> buffer,
        SocketFlags socketFlags,
        EndPoint remoteEP,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArraySegment<byte> segment = AsSegment(buffer);
        return new ValueTask<int>(SocketTaskExtensions.SendToAsync(socket, segment, socketFlags, remoteEP));
    }

    public static async ValueTask<SocketReceiveFromResult> ReceiveFromAsync(
        this Socket socket,
        Memory<byte> buffer,
        SocketFlags socketFlags,
        EndPoint remoteEndPoint,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArraySegment<byte> segment = AsSegment(buffer);
        return await SocketTaskExtensions
            .ReceiveFromAsync(socket, segment, socketFlags, remoteEndPoint).ConfigureAwait(false);
    }

    public static async ValueTask<TcpClient> AcceptTcpClientAsync(
        this TcpListener listener,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await listener.AcceptTcpClientAsync().ConfigureAwait(false);
    }

    public static async ValueTask ConnectAsync(
        this TcpClient client,
        IPAddress address,
        int port,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await client.ConnectAsync(address, port).ConfigureAwait(false);
    }

    public static Task CancelAsync(this CancellationTokenSource source)
    {
        source.Cancel();
        return Task.CompletedTask;
    }

    internal static ArraySegment<byte> AsSegment(ReadOnlyMemory<byte> memory)
    {
        if (MemoryMarshal.TryGetArray(memory, out ArraySegment<byte> segment))
        {
            return segment;
        }

        return new ArraySegment<byte>(memory.ToArray());
    }
}

/// <summary>
/// netstandard polyfill for the compare-and-remove <see cref="ConcurrentDictionary{TKey, TValue}"/>
/// overload that exists in-box on net8+. Removes the entry only when its current value matches.
/// </summary>
internal static class NetstandardConcurrentPolyfills
{
    public static bool TryRemove<TKey, TValue>(
        this ConcurrentDictionary<TKey, TValue> dictionary,
        KeyValuePair<TKey, TValue> item) =>
        ((ICollection<KeyValuePair<TKey, TValue>>)dictionary).Remove(item);
}
#endif

#if NETSTANDARD2_0
/// <summary>netstandard2.0 polyfills for the Memory-based <see cref="System.IO.Stream"/> overloads.</summary>
internal static class NetstandardStreamPolyfills
{
    public static async ValueTask<int> ReadAsync(
        this System.IO.Stream stream,
        Memory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        if (MemoryMarshal.TryGetArray((ReadOnlyMemory<byte>)buffer, out ArraySegment<byte> segment))
        {
            return await stream.ReadAsync(segment.Array!, segment.Offset, segment.Count, cancellationToken)
                .ConfigureAwait(false);
        }

        byte[] temp = new byte[buffer.Length];
        int read = await stream.ReadAsync(temp, 0, temp.Length, cancellationToken).ConfigureAwait(false);
        new ReadOnlySpan<byte>(temp, 0, read).CopyTo(buffer.Span);
        return read;
    }

    public static async ValueTask WriteAsync(
        this System.IO.Stream stream,
        ReadOnlyMemory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        ArraySegment<byte> segment = NetstandardNetworkPolyfills.AsSegment(buffer);
        await stream.WriteAsync(segment.Array!, segment.Offset, segment.Count, cancellationToken)
            .ConfigureAwait(false);
    }
}
#endif
