# Strong naming

Every assembly shipped from this repository is **strong-named**. Strong naming gives each assembly a stable cryptographic identity (name + version + public key), which lets the packages be referenced by other strong-named assemblies, loaded into contexts that require a strong name, and unambiguously bound on the .NET Framework runtime.

## Identity

All assemblies are signed with a single repository key, so they share one public key and token:

| | Value |
| --- | --- |
| Public key token | `da3d093ebe8bf537` |
| Key file | [`crdt.snk`](../crdt.snk) (committed at the repository root) |

You can confirm the identity of any shipped assembly with the .NET Strong Name tool, or from PowerShell:

```powershell
[System.Reflection.AssemblyName]::GetAssemblyName("Crdt.dll").GetPublicKeyToken() |
    ForEach-Object { $_.ToString("x2") } | Join-String   # da3d093ebe8bf537
```

## The key is committed on purpose

`crdt.snk` contains a full key pair and is intentionally checked into source control. This is safe and expected: **strong naming is an identity mechanism, not a security boundary.** A strong name proves that two assemblies came from the same key, but it is not a defence against tampering — anyone can recompile against the public key, and .NET Core / .NET 5+ does not verify strong-name signatures at load time. Treat the key like a namespace, not like a credential.

Because the key is in the repository, signing happens during a normal build with no secrets and no extra steps. The .NET SDK performs the signing in managed code on Windows, Linux, and macOS, so CI requires no special configuration.

## How it is wired up

- `Directory.Build.props` sets `SignAssembly=true` and `AssemblyOriginatorKeyFile` for **all** projects (libraries, tests, samples, and benchmarks). Signing every project keeps `InternalsVisibleTo` working, since a signed assembly may only grant friend access to another signed assembly.
- The same file defines the `CrdtPublicKey` property (the full public key). Each `InternalsVisibleTo` grant is declared as an MSBuild `<InternalsVisibleTo Include="…" Key="$(CrdtPublicKey)" />` item so the public key is written exactly once.

## Caveat: extension packages that depend on not-yet-signed libraries

The core packages — `Crdt`, `Crdt.Transport`, `Crdt.Consensus`, and `Crdt.Gc` — depend only on signed (Microsoft) assemblies, so they are strong-named with no caveats.

The following extension packages reference upstream `marcschier` libraries that are **not yet strong-named** (`RaftCs`, `Pgm`, `NanoMsgSharp`, `DtlsSharp`, `Mqtt.Client`):

- `Crdt.Consensus.Raft`
- `Crdt.Transport.Pgm`
- `Crdt.Transport.NanoMsg`
- `Crdt.Transport.Dtls`
- `Crdt.Transport.Mqtt`

These packages are still strong-named, and they work normally on **.NET (Core) 5 and later**, where the old "a strong-named assembly may only reference strong-named assemblies" rule was relaxed. The C# compiler still emits `CS8002` for the unsigned reference; that warning is suppressed in exactly these five projects (and in the test/sample/benchmark projects that consume them transitively).

The one real limitation is **.NET Framework**: if one of these five packages is loaded on .NET Framework via its `netstandard` target, the unsigned upstream dependency will fail strong-name resolution at runtime. If you need these transports on .NET Framework, the fix is to strong-name the upstream library with the same key. Doing so upstream removes the caveat entirely and is the recommended long-term follow-up.

## Consumer impact

Adding a strong name changes assembly identity, which is a binary-breaking change: code compiled against an unsigned earlier build must be recompiled against the signed assemblies. This is why strong naming is introduced in a new minor release rather than a patch.
