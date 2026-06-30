// Copyright (c) marcschier. Licensed under the MIT License.

namespace Crdt.Transport;

/// <summary>Identifies the payload kind stored in a transport frame.</summary>
public enum MessageType : byte
{
    /// <summary>A complete CRDT state snapshot.</summary>
    State = 1,

    /// <summary>A delta-state CRDT update.</summary>
    Delta = 2,

    /// <summary>An operation-based CRDT update.</summary>
    Operation = 3,

    /// <summary>A digest exchanged before anti-entropy synchronization.</summary>
    Digest = 4,

    /// <summary>A peer greeting used by transports.</summary>
    Hello = 5,

    /// <summary>An acknowledgement used by transports.</summary>
    Ack = 6,

    /// <summary>A garbage-collection causal-frontier report.</summary>
    GcVersionReport = 7,

    /// <summary>A garbage-collection stable watermark.</summary>
    GcWatermark = 8,
}
