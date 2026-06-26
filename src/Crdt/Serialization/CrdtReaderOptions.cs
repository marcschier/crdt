// Copyright (c) marcschier. Licensed under the MIT License.

namespace Crdt;

/// <summary>
/// Bounds applied while decoding CRDT binary payloads, protecting against hostile or
/// corrupt input that could otherwise trigger excessive allocation or runaway work.
/// Decoders fail fast with <see cref="System.FormatException"/> when a bound is exceeded.
/// </summary>
public sealed class CrdtReaderOptions
{
    /// <summary>Gets the shared default options.</summary>
    public static CrdtReaderOptions Default { get; } = new();

    /// <summary>
    /// Gets the maximum number of elements any single length-prefixed collection may declare.
    /// Defaults to roughly 16 million.
    /// </summary>
    public int MaxCollectionCount { get; init; } = 1 << 24;

    /// <summary>Gets the maximum length, in bytes, of any single encoded string. Defaults to 1 MiB.</summary>
    public int MaxStringBytes { get; init; } = 1 << 20;

    /// <summary>Gets the maximum nesting depth permitted while decoding. Defaults to 64.</summary>
    public int MaxDepth { get; init; } = 64;
}
