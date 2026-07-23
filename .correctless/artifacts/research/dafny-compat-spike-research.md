# Research Brief: Dafny 4.11.0 NuGet packages (DafnyCore, DafnyPipeline) — current state and .NET 10 in-process compatibility
# Searched: 2026-07-20
# Producer: correctless:cspec-research agent (WebSearch/WebFetch), for spec dafny-compat-spike

## Current State

As of today, **Dafny 4.11.0 is still the latest stable release** (GitHub releases page shows it as "Latest", with only nightlies after it — most recent `nightly-2026-07-07-368adac`; nightly package versions are `4.11.1-nightly-*`, so no 4.12 line has begun). 4.11.0 was published to NuGet on **2025-08-25**; DafnyCore 4.11.0, DafnyPipeline 4.11.0, and DafnyDriver 4.11.0 are all **listed, MIT-licensed, and not deprecated or unlisted**. All three ship **net8.0** assemblies, and NuGet marks them computed-compatible with net9.0 and net10.0. The pinned-4.11.0 assumption in the plan is correct and safe. Note the stable-release cadence has slowed: no stable release in ~11 months (Aug 2025 → Jul 2026), though nightly publishing through 2026-07-07 shows the repo is actively developed.

The packages **do not bundle Z3**. `DafnyOptions` (public, `Microsoft.Dafny` namespace) hardcodes `DefaultZ3Version = "4.12.1"` and resolves the solver via (1) `--solver-path`/`PROVER_PATH` prover option, (2) `{dir of executing assembly}/z3/bin/z3-4.12.1[.exe]`, (3) a `z3`/`z3.exe` found on PATH. The harness must provision Z3 4.12.1 itself. The in-process API surface exists and is public — `Microsoft.Dafny.Compilation` (in DafnyCore, `Source/DafnyCore/Pipeline/`) with `Start()`, `Task<Program?> ParsedProgram`, `Task<ResolutionResult?> Resolution`, `IObservable<ICompilationEvent> Updates`, and `VerifyLocation`/`VerifyCanVerify`; plus `CliCompilation.Create(DafnyOptions)` / `VerifyAllLazily()` in the DafnyDriver package — but Dafny's own docs explicitly warn the exposed AST "is constantly evolving" and must be recompiled against the exact binary version. Release notes carry **no C# API changelog**, and real churn between minors is confirmed (4.10.0 renamed `IToken` → `IOrigin` and moved net6.0 → net8.0).

The single biggest .NET-10-era runtime risk found is **System.CommandLine**: DafnyCore (including current master) pins `2.0.0-beta4.22272.1`, but Microsoft shipped stable System.CommandLine 2.0.0 in **November 2025 alongside .NET 10** with sweeping breaking changes vs beta4 (`SetHandler`→`SetAction`, `InvocationContext` removed, etc.). NuGet treats stable 2.0.0 > 2.0.0-beta4, so any harness dependency that transitively pulls the GA package will silently upgrade Dafny's dependency and cause `MissingMethodException`/`TypeLoadException` at runtime. No .NET 10 breaking change in Microsoft's official list looks likely to break DafnyCore's managed code on a net10.0 host (Z3 is an external process, not P/Invoke), with one caveat around single-file publishing.

## Key Findings

### 1. 4.11.0 is the latest stable and remains pinnable; no 4.12 exists
- **Source**: https://github.com/dafny-lang/dafny/releases (accessed 2026-07-20); https://www.nuget.org/packages/DafnyCore/ ; https://www.nuget.org/packages/DafnyPipeline/ (both published 2025-08-25); nightly `4.11.1-nightly-2026-01-04` and `nightly-2026-07-07` confirm no stable successor
- **Relevance**: Confirms the spike's pin. Nightlies exist as prereleases of 4.11.1 on the same NuGet feed.
- **Implication for rules**: Pin exactly `4.11.0` (not a floating `4.*`), and require prerelease-excluded restore so `4.11.1-nightly-*` can never be selected.

### 2. Packages ship net8.0 only; NuGet computes net9.0/net10.0 compatibility
- **Source**: https://www.nuget.org/packages/DafnyCore/ and https://www.nuget.org/packages/DafnyPipeline/ (framework tables, accessed 2026-07-20)
- **Relevance**: A net10.0 harness will restore and load these fine at the NuGet level; actual runtime behavior is exactly what the spike must prove.
- **Implication for rules**: The spec should treat "restores and loads" as necessary-but-insufficient; the pass criterion must be end-to-end verify of both fixtures.

### 3. NuGet packages do NOT bundle Z3; Dafny 4.11.0 expects Z3 4.12.1 by a specific name/path
- **Source**: `Source/DafnyCore/DafnyOptions.cs` at tag v4.11.0: `public static readonly string DefaultZ3Version = "4.12.1";`, `SetZ3ExecutablePath(...)` search order: prover-path option → `Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "z3", "bin", "z3-4.12.1[.exe]")` → PATH scan for `z3`/`z3.exe`. `DafnyCore.csproj` and `DafnyPipeline.csproj` at v4.11.0 contain no z3 payload (they do embed `DafnyPrelude.bpl` and runtime/stdlib resources). The dotnet-tool install docs likewise say to install Z3 4.12.1 separately and put it on PATH (https://github.com/dafny-lang/dafny/wiki/INSTALL).
- **Relevance**: The harness must provision a pinned Z3 4.12.1 binary (from Z3's GitHub releases or copied out of a Dafny release zip) and either drop it at `{output}/z3/bin/z3-4.12.1` next to DafnyCore.dll or point `--solver-path` at it.
- **Implication for rules**: Spec must include explicit Z3 provisioning (exact version 4.12.1, exact expected filename `z3-4.12.1[.exe]`), and should prefer the explicit solver-path option over PATH ambient discovery for reproducibility. Note: `Assembly.GetExecutingAssembly().Location` returns `""` under single-file publish, which would break discovery stage 2 — avoid single-file publish for the spike (inference from documented .NET single-file behavior).
- Maintainers have never formally documented supported Z3 version ranges — issue #5470 "Guidance on z3 versions?" (opened 2024-05-21) shows the gap: https://github.com/dafny-lang/dafny/issues/5470. Z3 4.8.5–4.12.1 is the tested range per the Dafny 4 release blog (2023-03-03): https://dafny.org/blog/2023/03/03/dafny-4-released/

### 4. The in-process pipeline API is public and complete, but officially unstable
- **Source**: v4.11.0 source — `Source/DafnyCore/Pipeline/` contains `Compilation.cs`, `CompilationInput.cs`, `IDafnyParser.cs`, `ISymbolResolver.cs`, `IProgramVerifier.cs`, `ITextDocumentLoader.cs`, `ResolutionResult.cs`, `Events/`. `Microsoft.Dafny.Compilation` is public: ctor `Compilation(ILogger<Compilation>, IFileSystem, ITextDocumentLoader, IProgramVerifier, ExecutionEngine boogieEngine, CompilationInput)`; members `Start()`, `Task<Program?> ParsedProgram`, `Task<ResolutionResult?> Resolution`, `DafnyOptions Options`, `IObservable<ICompilationEvent> Updates`, `VerifyLocation(FilePosition, ...)`, `VerifyCanVerify(ICanVerify, ...)`, `HasErrors`. The DafnyDriver package (a normal library on NuGet, 4.11.0, 2025-08-25) exposes public `CliCompilation.Create(DafnyOptions)` → `Start()` → `IAsyncEnumerable<CanVerifyResult> VerifyAllLazily(int? randomSeed = null)`, with events `FinishedParsing`, `FinishedResolution`, `NewDiagnostic`, `BoogieUpdate`.
- **Official stance**: FAQ "Is Dafny available as a library?" answers "Yes... The DafnyPipeline library on NuGet is the most likely candidate for inclusion in a third-party program" (https://dafny.org/dafny/HowToFAQ/FAQDafnyAsLibrary). The plugin documentation at v4.11.0 (`docs/DafnyRef/Plugins.md`) warns: "The plugin API directly exposes the Dafny AST, which is constantly evolving. Hence, always recompile your plugin against the binary of Dafny that will be importing your plugin."
- **Relevance**: Both the low-level `Compilation` route (DafnyCore only) and the higher-level `CliCompilation` route (via the DafnyDriver package) are viable in-process. `CliCompilation` is the closest thing to a reference driver, but DafnyDriver drags in DafnyLanguageServer, DafnyTestGeneration, Microsoft.TestPlatform ≥ 17.9.0, and Newtonsoft.Json ≥ 13.0.3 as dependencies.
- **Implication for rules**: The spec should (a) pin the exact package version and treat the C# API as version-locked with no compatibility guarantee across even minor upgrades; (b) decide explicitly between DafnyPipeline+DafnyCore (lighter, must hand-assemble the `Compilation` DI graph) vs adding DafnyDriver (turnkey `CliCompilation`, heavier transitive closure) — both are facts the spike can compare; (c) not assume release notes will announce API breaks — they don't (verified against RELEASE_NOTES.md for 4.8.0–4.11.0).

### 5. Third-party tools mostly do NOT embed the NuGet packages — they vendor source, use the plugin hook, or shell out
- **Sources**: dafny-sketcher pins Dafny as a git submodule and builds against source, net8.0 (https://github.com/namin/dafny-sketcher); MutDafny (paper Nov 2025, arXiv:2511.15403) vendors a Dafny submodule and runs as a `--plugin` loaded by the Dafny CLI (https://github.com/MutDafny/mutdafny); dafny-annotator drives the `dafny` CLI from Python (https://dafny.org/blog/2025/06/21/dafny-annotator/).
- **Relevance**: There is no widely-copied exemplar of "reference DafnyPipeline 4.11.0 from NuGet and verify in-process" in the ecosystem; the ecosystem consensus is to pin an exact Dafny build. This raises the value of the spike (it's genuinely unproven territory) and validates the pin-exact-version approach.
- **Implication for rules**: The spec should not assume community-proven patterns exist for the NuGet-embedding route; the spike's acceptance evidence needs to be self-contained.

### 6. API/AST churn between 4.x minors is real and undocumented in release notes
- **Source**: Direct source comparison — v4.9.0 `DafnyOptions.cs` uses `IToken` (`SetZ3ExecutablePath(ErrorReporter, IToken)`), v4.10.0/v4.11.0 use `IOrigin`. DafnyCore 4.9.0 (2024-10-31) targets **net6.0** with Boogie ≥ 3.3.3; 4.10.0 (2025-02-04) targets **net8.0** with Boogie ≥ 3.4.3; 4.11.0 requires Boogie ≥ 3.5.5. RELEASE_NOTES.md for 4.8.0–4.11.0 contains zero entries about any of these C# API/TFM changes.
- **Relevance**: The core token/AST abstraction was renamed in a minor release (4.9→4.10) with no release-note mention. Any AST-walking code the spike writes (symbols, declaration kinds, ghost classification) is version-locked.
- **Implication for rules**: Spec rules should forbid loose version ranges, require the AST-walking layer to live behind the project's DafnyAdapter boundary, and treat any Dafny upgrade as a breaking-change event requiring recompile + retest.

### 7. System.CommandLine is the sharpest .NET-10-era conflict risk
- **Source**: DafnyCore 4.11.0 and **current master** both pin `System.CommandLine 2.0.0-beta4.22272.1`. System.CommandLine 2.0.0 went GA/stable around .NET 10's release (Nov 2025) with major breaking changes vs beta4: `SetHandler`→`SetAction`, `ICommandHandler`/`InvocationContext`/`CommandLineBuilder` removed, etc. (https://github.com/dotnet/command-line-api/issues/2576; https://learn.microsoft.com/en-us/dotnet/standard/commandline/migration-guide-2.0.0-beta5).
- **Relevance**: NuGet version ordering puts stable `2.0.0` above `2.0.0-beta4.22272.1`, and DafnyCore's constraint is `>=`. If the harness (or any transitive dependency) references System.CommandLine 2.0.0, the resolved graph upgrades Dafny's copy and DafnyCore/DafnyDriver will fail at runtime with missing-member exceptions. Dafny upstream has NOT migrated even on master as of July 2026.
- **Implication for rules**: The harness project must not reference System.CommandLine directly, and restore must be verified to resolve System.CommandLine to exactly `2.0.0-beta4.22272.1` (assert in the spike; lock file). Flag any future dependency addition as a re-check trigger.

### 8. .NET 10 breaking changes: nothing that clearly breaks DafnyCore in-process; two adjacent items
- **Source**: Microsoft's official ".NET 10 breaking changes" index (page updated 2026-07-08): https://learn.microsoft.com/en-us/dotnet/core/compatibility/10.0
- **Relevance**: No BinaryFormatter-class removal issues apply (that removal shipped in .NET 9, and no evidence was found that Dafny/Boogie 3.5.x use it). Relevant-adjacent entries: (a) "Single-file apps no longer look for native libraries in executable directory" and "DllImportSearchPath.AssemblyDirectory only searches the assembly directory" — interop behavioral changes that don't affect Z3 (spawned as an external process, not P/Invoked), but reinforce avoiding single-file publish; (b) trimming remains opt-in for console apps, and Dafny's heavy reflection/embedded-resource usage means `PublishTrimmed` must stay off.
- **Implication for rules**: Mandate a plain framework-dependent (non-single-file, non-trimmed, non-AOT) net10.0 console app.
- **Negative finding, explicitly reported**: Searches for Dafny-specific .NET 9/10 runtime failures returned no Dafny-related reports. Absence of reports is weak evidence of compatibility, not proof — which is exactly what the spike is for.

### 9. Boogie dependency is healthy; resolves to 3.5.5 by default
- **Source**: https://www.nuget.org/packages/Boogie.ExecutionEngine — latest 3.5.6 (2025-12-10), 3.5.5 (2025-08-07), both net8.0, active release cadence, no deprecations. DafnyCore 4.11.0 requires `>= 3.5.5`; NuGet's lowest-applicable rule resolves 3.5.5 unless the harness pins higher.
- **Relevance**: `Compilation`'s constructor takes Boogie's `ExecutionEngine` directly, so the harness will touch Boogie API; letting it float to 3.5.6 is an untested combination.
- **Implication for rules**: Pin Boogie.ExecutionEngine explicitly to 3.5.5 (the version Dafny 4.11.0 was built and tested against) rather than accepting float.

## Recommended Patterns

- **Pin exact versions of the whole verified stack**: DafnyPipeline 4.11.0 + DafnyCore 4.11.0 (+ DafnyDriver 4.11.0 if using `CliCompilation`), Boogie.ExecutionEngine 3.5.5, System.CommandLine 2.0.0-beta4.22272.1 (transitive), Z3 4.12.1. This mirrors what every observed downstream tool does (submodule/commit pinning).
- **Drive verification the way Dafny's own CLI does**: `CliCompilation.Create(DafnyOptions)` → `Start()` → `await foreach` over `VerifyAllLazily()` and/or subscribe to `Compilation.Updates` for `FinishedParsing`/`FinishedResolution`/`BoogieUpdate` events; recover the resolved AST via `Compilation.Resolution` / `ParsedProgram` and options via `Compilation.Options`.
- **Provision Z3 deterministically**: ship `z3-4.12.1` at `{AppContext.BaseDirectory}/z3/bin/` or pass an explicit solver path; verify at startup that the resolved solver is the pinned one.
- **Publish plainly**: framework-dependent net10.0, no PublishTrimmed/PublishAot/single-file.

## Things to Avoid

- **Floating or range versions of any Dafny package** — AST renames (`IToken`→`IOrigin`, 4.10.0) and TFM jumps (net6→net8, 4.10.0) land in minor releases with no release-note warning.
- **Referencing System.CommandLine ≥ 2.0.0-beta5/GA anywhere in the harness's dependency graph** — silently upgrades Dafny's pinned beta4 and breaks it at runtime.
- **Assuming Z3 arrives via NuGet** — it does not; only the GitHub release zips bundle z3.
- **Single-file publish** — breaks `Assembly.GetExecutingAssembly().Location`-based z3 discovery, and .NET 10 further changed single-file native library search.
- **Nightly packages** (`4.11.1-nightly-*`) — explicitly labeled "may not be as stable," no release notes.
- **Treating the LSP `DocumentManager`/language-server internals as a stabler alternative** — the LSP-facing types moved into DafnyCore's Pipeline over the 4.x series and carry the same "constantly evolving" caveat as everything else.

## Version Pins

| Component | Pin | Rationale |
|---|---|---|
| DafnyCore | 4.11.0 (NuGet, 2025-08-25) | Latest stable; listed; net8.0; the API the spike targets |
| DafnyPipeline | 4.11.0 | Thin wrapper over DafnyCore + embedded runtime/stdlib resources; the FAQ-blessed embedding package |
| DafnyDriver (optional) | 4.11.0 | Only if using `CliCompilation`; brings DafnyLanguageServer, DafnyTestGeneration, Microsoft.TestPlatform ≥ 17.9.0, Newtonsoft.Json ≥ 13.0.3 |
| Boogie.ExecutionEngine | 3.5.5 (2025-08-07) | Exact version DafnyCore 4.11.0 builds against; don't float to 3.5.6 |
| System.CommandLine | 2.0.0-beta4.22272.1 (transitive — do not reference directly) | Dafny master is still on beta4 as of 2026-07; GA 2.0.0 is binary-incompatible |
| Z3 | 4.12.1 (exact filename `z3-4.12.1[.exe]`) | Hardcoded `DefaultZ3Version` in 4.11.0; tested range is 4.8.5–4.12.1 |
| Harness TFM | net10.0, no trimming/AOT/single-file | Current LTS; no applicable .NET 10 breaking change found for this load pattern |

## Dependency Health

| Dependency | Version | Status | Last Release | Notes |
|------------|---------|--------|--------------|-------|
| DafnyCore / DafnyPipeline / DafnyDriver | 4.11.0 | Active (listed, not deprecated) | 2025-08-25 stable; nightlies through 2026-07-07 | ~11 months without a stable release — cadence slowdown worth noting |
| Boogie.ExecutionEngine | 3.5.5 (Dafny's pin) | Active | 3.5.6 on 2025-12-10 | net8.0; healthy |
| System.CommandLine (transitive) | 2.0.0-beta4.22272.1 | Frozen legacy prerelease | beta4: 2022; GA 2.0.0: Nov 2025 | Dafny has not migrated even on master; conflict hazard |
| Z3 | 4.12.1 | Old but mandated | Jan 2023 | Newer Z3 (4.13+) untested/unsupported by Dafny per open issue #5470 |
| OmniSharp.Extensions.LanguageServer (transitive) | 0.19.5 | Not verified this session | — | Pulled in by DafnyCore itself; worth a health check if the spike graduates |
| .NET 10 (host) | 10.0.x LTS | Active LTS | Nov 2025 | Official breaking-changes list reviewed; nothing directly applicable |

## Open Questions

1. **No direct evidence of anyone running DafnyCore 4.11.0 in-process on a .NET 10 host** — searches found no success or failure reports. This is exactly the unknown the spike resolves.
2. **PATH-based z3 discovery details**: the assembly-adjacent lookup requires the versioned name `z3-4.12.1`, while the PATH fallback accepts plain `z3`; exactly how a version mismatch on a PATH-found z3 is surfaced (warning vs error) was not verified. Using an explicit solver path sidesteps this.
3. **OmniSharp.Extensions.LanguageServer 0.19.5 transitive graph on net10.0** was not individually audited for conflicts with a modern net10.0 app graph.
4. **Whether Dafny 4.12/5.0 is imminent** — no roadmap statement found; the 4.11.1 nightly branch suggests the next release will be a patch, but that's inference from version strings only.
5. The dafny-lang FAQ answer recommending DafnyPipeline predates the 4.x pipeline refactor; whether maintainers today would recommend DafnyDriver's `CliCompilation` instead is not documented anywhere found.

## Sources

- Dafny releases: https://github.com/dafny-lang/dafny/releases ; v4.11.0 tag; RELEASE_NOTES.md
- NuGet: DafnyCore, DafnyPipeline, DafnyDriver, Boogie.ExecutionEngine package pages (accessed 2026-07-20)
- v4.11.0 source: DafnyOptions.cs, Source/DafnyCore/Pipeline/, Compilation.cs, CliCompilation.cs, DafnyCore.csproj, DafnyPipeline.csproj, docs/DafnyRef/Plugins.md
- Dafny FAQ (Dafny as a library), INSTALL wiki, Dafny 4 release blog, issue #5470
- System.CommandLine: dotnet/command-line-api#2576, Microsoft migration guide (beta5/GA)
- Microsoft .NET 10 breaking changes index (updated 2026-07-08)
- Third-party embedders: dafny-sketcher, MutDafny (arXiv:2511.15403), dafny-annotator
