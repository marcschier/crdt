// Copyright (c) marcschier. Licensed under the MIT License.

namespace Crdt.Transport;

/// <summary>Extracts a pending delta from a CRDT state value.</summary>
/// <typeparam name="TState">The state type that records local changes.</typeparam>
/// <typeparam name="TDelta">The delta type emitted by the state.</typeparam>
/// <param name="state">The state value to inspect.</param>
/// <param name="delta">The extracted delta when one is available.</param>
/// <returns><see langword="true"/> when a delta was extracted; otherwise <see langword="false"/>.</returns>
public delegate bool CrdtDeltaExtractor<TState, TDelta>(TState state, out TDelta delta);
