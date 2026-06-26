// Copyright (c) marcschier. Licensed under the MIT License.

namespace Crdt.Tests;

/// <summary>
/// A deterministic <see cref="TimeProvider"/> test double whose UTC clock only moves when
/// explicitly advanced. Keeps clock-dependent tests (HLC, LWW) fully reproducible and
/// NativeAOT-safe (no reflection).
/// </summary>
internal sealed class FakeTimeProvider : TimeProvider
{
    private DateTimeOffset _utcNow;

    public FakeTimeProvider(DateTimeOffset start) => _utcNow = start;

    public FakeTimeProvider()
        : this(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero))
    {
    }

    public override DateTimeOffset GetUtcNow() => _utcNow;

    public void Advance(TimeSpan delta) => _utcNow = _utcNow.Add(delta);

    public void Set(DateTimeOffset value) => _utcNow = value;
}
