# Copyright (c) marcschier. Licensed under the MIT License.
# Runs the BenchmarkDotNet suite with pass-through arguments.
dotnet run -c Release --project tests\Crdt.Benchmarks\Crdt.Benchmarks.csproj -- @args
