// Copyright (c) marcschier. Licensed under the MIT License.

using Crdt.Samples;

Console.WriteLine("=== Crdt data-type tour ===");
Console.WriteLine("Each line shows independent replicas mutating, then merging to a converged value.");
Console.WriteLine();

CounterSamples.Run();
SetSamples.Run();
RegisterMapFlagSamples.Run();
GraphSequenceSamples.Run();
AdvancedSamples.Run();

Console.WriteLine("All replicas converged. ✓");
