// Copyright (c) marcschier. Licensed under the MIT License.

namespace Crdt.Interop.Tests;

internal static class ScenarioSamples
{
    public static CrdtScenario Get(string name) => name switch
    {
        "sequential-basic" => SequentialBasic(),
        "sequential-middle-edits" => SequentialMiddleEdits(),
        "sequential-tail-edits" => SequentialTailEdits(),
        "concurrent-same-index" => ConcurrentSameIndex(),
        "concurrent-different-indexes" => ConcurrentDifferentIndexes(),
        "concurrent-multi-character" => ConcurrentMultiCharacter(),
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown scenario.")
    };

    private static CrdtScenario SequentialBasic() => new()
    {
        Name = "sequential-basic",
        Replicas =
        [
            new CrdtReplica
            {
                Id = "A",
                Ops =
                [
                    Insert(0, "Hello"),
                    Insert(5, " world"),
                    Delete(5, 1),
                    Insert(5, ",")
                ]
            }
        ]
    };

    private static CrdtScenario SequentialMiddleEdits() => new()
    {
        Name = "sequential-middle-edits",
        Initial = "abcdef",
        Replicas =
        [
            new CrdtReplica
            {
                Id = "A",
                Ops =
                [
                    Delete(2, 2),
                    Insert(2, "XY"),
                    Insert(0, "!")
                ]
            }
        ]
    };

    private static CrdtScenario SequentialTailEdits() => new()
    {
        Name = "sequential-tail-edits",
        Initial = "012345",
        Replicas =
        [
            new CrdtReplica
            {
                Id = "A",
                Ops =
                [
                    Delete(3, 3),
                    Insert(3, "abc"),
                    Insert(6, "Z")
                ]
            }
        ]
    };

    private static CrdtScenario ConcurrentSameIndex() => new()
    {
        Name = "concurrent-same-index",
        Initial = "ab",
        Replicas =
        [
            new CrdtReplica { Id = "A", Ops = [Insert(1, "X")] },
            new CrdtReplica { Id = "B", Ops = [Insert(1, "Y")] }
        ],
        MergeSchedule = ["A<-B", "B<-A"]
    };

    private static CrdtScenario ConcurrentDifferentIndexes() => new()
    {
        Name = "concurrent-different-indexes",
        Initial = "base",
        Replicas =
        [
            new CrdtReplica { Id = "A", Ops = [Insert(0, "L")] },
            new CrdtReplica { Id = "B", Ops = [Insert(4, "R")] }
        ],
        MergeSchedule = ["A<-B", "B<-A"]
    };

    private static CrdtScenario ConcurrentMultiCharacter() => new()
    {
        Name = "concurrent-multi-character",
        Initial = "cat",
        Replicas =
        [
            new CrdtReplica { Id = "A", Ops = [Insert(1, "12")] },
            new CrdtReplica { Id = "B", Ops = [Insert(2, "Z")] }
        ],
        MergeSchedule = ["A<-B", "B<-A"]
    };

    private static CrdtOperation Insert(int index, string text) => new()
    {
        Op = "insert",
        Index = index,
        Text = text
    };

    private static CrdtOperation Delete(int index, int length) => new()
    {
        Op = "delete",
        Index = index,
        Length = length
    };
}
