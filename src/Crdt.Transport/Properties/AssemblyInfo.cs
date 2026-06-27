// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.CompilerServices;

// The DTLS transport reuses this assembly's netstandard polyfills (socket-async shims,
// SharedRandom, Throw, PeriodicTimer) instead of duplicating them.
[assembly: InternalsVisibleTo("Crdt.Transport.Dtls")]
