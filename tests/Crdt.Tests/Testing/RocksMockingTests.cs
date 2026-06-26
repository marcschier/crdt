// Copyright (c) marcschier. Licensed under the MIT License.

#if NET10_0_OR_GREATER
using Rocks;

[assembly: Rock(typeof(Crdt.IClock), BuildType.Create)]

namespace Crdt.Tests.Testing;

/// <summary>
/// Validates the source-generated (Rocks) mocking path. These tests only build on .NET 10
/// because Rocks 10 emits code that targets net10; they run under both the JIT suite and the
/// NativeAOT gate, proving the mocking infrastructure is AOT-safe.
/// </summary>
public sealed class RocksMockingTests
{
    [Test]
    public async Task Mocked_Clock_Returns_Configured_Timestamp()
    {
        var expectations = new IClockCreateExpectations();
        var stamped = new Timestamp(100, 3, ReplicaId.FromUInt64(1));
        expectations.Setups.Now().ReturnValue(stamped);

        IClock clock = expectations.Instance();
        Timestamp result = clock.Now();

        await Assert.That(result).IsEqualTo(stamped);
        expectations.Verify();
    }
}
#endif
