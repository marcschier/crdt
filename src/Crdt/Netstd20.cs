// Copyright (c) marcschier. Licensed under the MIT License.

#if NETSTANDARD2_0
using System.Text;
#endif

namespace Crdt;

/// <summary>
/// Span-based parse helpers that use the framework's span overloads where available and fall back to
/// allocating string overloads on netstandard2.0 (which lacks them). Used on cold deserialization
/// paths, so the fallback allocation is negligible while modern targets keep the span fast path.
/// </summary>
internal static class SpanCompat
{
    public static Guid ParseGuidExactN(ReadOnlySpan<char> text) =>
#if NETSTANDARD2_0
        Guid.ParseExact(text.ToString(), "N");
#else
        Guid.ParseExact(text, "N");
#endif

    public static ulong ParseUInt64Invariant(ReadOnlySpan<char> text) =>
#if NETSTANDARD2_0
        ulong.Parse(text.ToString(), System.Globalization.CultureInfo.InvariantCulture);
#else
        ulong.Parse(text, provider: System.Globalization.CultureInfo.InvariantCulture);
#endif

    public static Guid CreateGuid(ReadOnlySpan<byte> bytes) =>
#if NETSTANDARD2_0
        new Guid(bytes.ToArray());
#else
        new Guid(bytes);
#endif

    public static void WriteGuidBytes(Guid value, Span<byte> destination)
    {
#if NETSTANDARD2_0
        value.ToByteArray().AsSpan().CopyTo(destination);
#else
        _ = value.TryWriteBytes(destination);
#endif
    }

    public static TEnum ParseEnum<TEnum>(string value)
        where TEnum : struct, Enum =>
#if NETSTANDARD2_0
        (TEnum)Enum.Parse(typeof(TEnum), value);
#else
        Enum.Parse<TEnum>(value);
#endif
}

#if NETSTANDARD2_0
/// <summary>
/// netstandard2.0 polyfills for span-based BCL overloads that exist in-box on netstandard2.1+. These
/// extension methods are compiled only for netstandard2.0; on newer targets the framework's instance
/// methods take precedence, so the call sites are identical across every target framework.
/// </summary>
internal static class Netstandard20Polyfills
{
    public static unsafe string GetString(this Encoding encoding, ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty)
        {
            return string.Empty;
        }

        fixed (byte* pointer = bytes)
        {
            return encoding.GetString(pointer, bytes.Length);
        }
    }

    public static unsafe int GetByteCount(this Encoding encoding, ReadOnlySpan<char> chars)
    {
        if (chars.IsEmpty)
        {
            return 0;
        }

        fixed (char* pointer = chars)
        {
            return encoding.GetByteCount(pointer, chars.Length);
        }
    }

    public static unsafe int GetBytes(this Encoding encoding, ReadOnlySpan<char> chars, Span<byte> bytes)
    {
        if (chars.IsEmpty)
        {
            return 0;
        }

        fixed (char* charPointer = chars)
        fixed (byte* bytePointer = bytes)
        {
            return encoding.GetBytes(charPointer, chars.Length, bytePointer, bytes.Length);
        }
    }

    public static bool TryAdd<TKey, TValue>(this Dictionary<TKey, TValue> dictionary, TKey key, TValue value)
    {
        if (dictionary.ContainsKey(key))
        {
            return false;
        }

        dictionary.Add(key, value);
        return true;
    }
}
#endif
