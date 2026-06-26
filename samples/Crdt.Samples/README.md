# Crdt.Samples

A self-contained console walkthrough of core CRDT merge semantics — **no networking**. Three replicas are created in-process, mutated independently, and reconciled with `Merge`, showing that the result is the same regardless of the order or duplication of exchanges.

## What it demonstrates

- **PN-Counter cluster (state-based).** Three nodes each apply local increments/decrements, then exchange full state and converge to a single total.
- **OR-Set (add-wins).** A concurrent add and remove of the same element resolve to *add wins* — the element survives.
- **Collaborative text.** Two people edit the same `Text` document concurrently (an insertion and an append); their edits converge to one identical string on both replicas.

## How it works

Each scenario builds plain CRDT values (`PNCounter`, `ORSet<string>`, `Text`), mutates them on separate "replicas", and then merges. Replication is faked by a trivial in-process helper, `gossipState`, that merges every node's state into every other node — enough to show convergence without a transport. Serialization is shown along the way: binary via `ToByteArray`/`ReadFrom` (with a value serializer such as `CrdtValues.String`) and JSON via `ToJson`/`FromJson`.

There is no network and no persistence here; the goal is to make the algebra of merging visible.

## Run it

From the repository root:

```shell
dotnet run --project samples/Crdt.Samples -c Release
```

(Requires the .NET 10 SDK; the sample targets `net10.0`.)

### Expected output

```text
=== Crdt samples ===

-- PN-Counter cluster (state-based) --
  node value = 12
  node value = 12
  node value = 12
  converged value = 12 (expected 12)

-- OR-Set (add-wins over concurrent remove) --
  alice contains 'feature-x' = True
  bob   contains 'feature-x' = True
  (add wins — the element survives)

-- Collaborative text --
  alice sees: "the quick brown fox jumps"
  bob   sees: "the quick brown fox jumps"
  identical  = True

All replicas converged. ✓
```

## Further reading

- [Data types](../../docs/data-types.md) — every CRDT with usage examples.
- [Replication models](../../docs/replication-models.md) — state-based, delta-state, and operation-based.
- [Serialization](../../docs/serialization.md) — the binary and JSON formats.
- For a version that replicates over a real network transport, see [`Crdt.Samples.Gossip`](../Crdt.Samples.Gossip).
