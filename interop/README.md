# CRDT convergence-parity interop

These tests compare this library's text and sequence CRDTs with Yjs through a neutral JSON scenario protocol.
They intentionally do not assert byte-level wire compatibility. The goal is convergence parity:

- Tier 1: sequential single-editor scenarios must produce exactly the same final string as Yjs.
- Tier 2: concurrent fork/merge scenarios must converge within each implementation, and the final strings must contain
  the same multiset of characters. Exact cross-library order is not asserted because concurrent tie-breaks differ.

## Scenario protocol

A scenario has optional `initial` text, a `replicas` array, and a `mergeSchedule` array. The initial text is applied to a
base document and forked to every replica. Replica operation logs contain either:

```json
{ "op": "insert", "index": 0, "text": "abc" }
```

or:

```json
{ "op": "delete", "index": 0, "length": 1 }
```

Merge steps use `target<-source`, for example `A<-B` means target replica A receives source replica B's state.
See `interop/scenarios/schema.json` for the complete minimal schema.

## Yjs harness

Install dependencies once and run either a scenario file or the self-test:

```powershell
cd interop\yjs-harness
npm install
node index.js --self-test
node index.js ..\scenarios\concurrent-same-index.json
```

The harness prints `{ "final": { "A": "...", "B": "..." } }` to stdout. Rust/yrs interop is optional future work and is
not part of this test suite.

## .NET tests

```powershell
dotnet build tests\Crdt.Interop.Tests\Crdt.Interop.Tests.csproj -c Release
dotnet test tests\Crdt.Interop.Tests\Crdt.Interop.Tests.csproj -c Release
```

The TUnit helper runs `npm install` automatically when `interop\yjs-harness\node_modules` is missing.
