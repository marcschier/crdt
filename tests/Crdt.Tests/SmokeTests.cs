// Copyright (c) marcschier. Licensed under the MIT License.

namespace Crdt.Tests;

/// <summary>
/// Minimal toolchain smoke tests that validate the TUnit + Microsoft.Testing.Platform
/// setup (including the NativeAOT publish path) before the real CRDT test suites land.
/// </summary>
public sealed class SmokeTests
{
    /// <summary>Verifies the test host discovers and runs a trivial test.</summary>
    [Test]
    public async Task Toolchain_Is_Wired_Up()
    {
        int sum = Add(2, 3);
        await Assert.That(sum).IsEqualTo(5);
    }

    private static int Add(int a, int b) => a + b;
}
