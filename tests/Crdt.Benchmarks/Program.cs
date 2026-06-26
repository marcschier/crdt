// Copyright (c) marcschier. Licensed under the MIT License.

using BenchmarkDotNet.Running;

namespace Crdt.Benchmarks;

/// <summary>Entry point that dispatches to the benchmark classes in this assembly.</summary>
internal static class Program
{
    private static void Main(string[] args) =>
        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
}
