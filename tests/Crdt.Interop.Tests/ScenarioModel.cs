// Copyright (c) marcschier. Licensed under the MIT License.

namespace Crdt.Interop.Tests;

internal sealed class CrdtScenario
{
    public string? Name { get; set; }

    public string? Initial { get; set; }

    public List<CrdtReplica> Replicas { get; set; } = [];

    public List<string> MergeSchedule { get; set; } = [];
}

internal sealed class CrdtReplica
{
    public string Id { get; set; } = string.Empty;

    public List<CrdtOperation> Ops { get; set; } = [];
}

internal sealed class CrdtOperation
{
    public string Op { get; set; } = string.Empty;

    public int Index { get; set; }

    public string? Text { get; set; }

    public int Length { get; set; }
}

internal sealed class YjsResult
{
    public Dictionary<string, string> Final { get; set; } = [];
}
