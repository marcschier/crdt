# Changelog

## 1.2.0

Dependency and tooling update with additive thread-safety guarantees for `GCounter`
and `PNCounter`. Public signatures, all five target frameworks, wire formats,
strong-name identity (assembly version 1.1.0.0), and the stable channel are unchanged.

- Update the published transport and consensus prerequisites: RaftCs 1.1.3,
  NanoMsgSharp 1.1.3, DtlsSharp 1.1.1, Pgm 0.10.2, and Mqtt.Client 1.1.5.
  Update the unused central RaftCs.Transport declaration to 1.1.3 as well;
  the CRDT Raft adapter consumes RaftCs directly, not RaftCs.Transport.
- Update the .NET 10 compatibility packages to 10.0.12, Nerdbank.GitVersioning
  to 3.10.94, Roslynator analyzers to 5.0.1, TUnit to 1.74.0, Rocks to 10.3.4,
  and the coverage extension to 18.12.0. Already-current packages are retained.
- Update the .NET SDK to 10.0.401 and GitHub Actions to their published stable
  releases without changing the .NET 8/9/10 test matrix or its gates.
- Select the coverage results directory explicitly for the upgraded testing
  platform, retaining the 85% floor and rejecting empty or nonfinite reports.
- Synchronize all counter reads, mutations, merges, delta extraction, comparison,
  cloning, and exports. PN operations observe a coherent positive/negative pair;
  opposing merges acquire instance gates in a stable order. Add focused counter
  concurrency regressions without changing existing assertions or timing.
- Read synchronized replica snapshots in the existing PGM OR-set convergence
  test under a user-authorized read-seam exception. Its assertions, expected
  elements, timing, fixtures, and skips remain unchanged; the other 100 original
  test files and the production OR-set concurrency contract are unchanged.
- Update Yjs to 13.6.33 and its lockfile, use reproducible `npm ci` in CI,
  replace end-of-life Node.js 20 with Node.js 24 LTS, and pin Mosquitto 2.1.2.
- Publish the existing nine CRDT packages to GitHub Packages first, then
  promote the same version to NuGet.org through the existing OIDC workflow.
