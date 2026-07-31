# Architecture — Corrected

> **Status: design-stage.** No code exists yet. `DESIGN.md` (v1.13) at the repo
> root is the authoritative design document; this file tracks the architectural
> commitments already frozen there so specs and reviews can enforce them from
> the first feature. As implementation lands, entries here shift from
> "intended" to "as built."

Corrected is an open-source proof-directed verification worker and
certification toolchain for Dafny: given a versioned Dafny program, frozen
formal obligations, and an explicit edit policy, it searches for an allowed
implementation or proof patch, rejects unapproved proof shortcuts, and emits
reproducible evidence (an assurance receipt) of what was proved, built,
tested, assumed, and still trusted.

## Key Components (intended — nothing built yet)

| Component | Planned location | Purpose |
|-----------|------------------|---------|
| C# core worker | `src/Corrected.Core/` | Deterministic policy/acceptance core on .NET 10 LTS. Owns run state, intake/lock resolution, ownership classification, verification, acceptance evaluation, receipt emission. Uses pinned Dafny SDK packages for the selected Route A (`DafnyCore`, `DafnyDriver`, `DafnyLanguageServer`, …; `DafnyPipeline` is NOT loaded on Route A — ADR-0001/DD-007). |
| DafnyAdapter | `src/Corrected.DafnyAdapter/` | The single Dafny SDK boundary (PAT-001) — the sole package that imports Dafny/Boogie assemblies and hosts the in-process solver. Core reaches Dafny only through it (INV-006/034/035). |
| `corrected` CLI | `src/Corrected.Cli/` | Reference acceptance implementation: `corrected init`, `corrected check`, `corrected certify`, `corrected explain`. Runnable with no model, Node.js, TypeScript, or Pi session. |
| Test/build-gate carrier (NOT shipped) | `gate/`, `test/` | Homes the readiness-gate checker (INV-001/002/036), the append-only schema-version *history* registry + meta-test (INV-044), and the from-clean / path-scoped gates. Kept OUT of the shipped core/CLI so the gate can enforce itself without tripping its own production-code ban (INV-036). |
| TypeScript Pi adapter | `adapters/pi/` (Phase 1) | Small integration package realizing the core-defined methodology inside the Pi agent runtime. Manages Pi lifecycle, cancellation, progress, proposal transport. Integration code only — not a second policy implementation. |
| Worker↔adapter protocol | Defined by core schemas (Phase 1) | Strict LF-delimited JSON over stdin/stdout; versioned commands/events/results; large artifacts by content-addressed descriptor. |

> **Planned paths, not artifacts.** `src/` is empty; the locations above are the
> design-stage layout the Phase 0.1 entrypoints bind to. The core-worker Dafny SDK
> package set reflects the DD-007 propagation for the selected Route A (`DafnyDriver`
> + `DafnyCore` + `DafnyLanguageServer`; `DafnyPipeline` not loaded), discharged with
> ADR-0001's promotion to accepted (DF-002, 2026-07-24); P1's component-table gate
> (INV-003 enforcement-(b), in the build-gate carrier) re-checks it against
> `route-a.json`.

**Route-A production-assembly set (machine-readable — authoritative for P1's
component-table propagation check, readiness-gate-carrier INV-008(b)/EXT2-08).** P1
asserts **exact set-equality** of `route-a.json`'s loaded Route-A Dafny-family set
against this pinned set (so reverting this block, or dropping an anchor, FAILS P1 —
the check proves *propagation*, not merely "LanguageServer present / Pipeline absent"):

<!-- correctless:route-a-production-assemblies:start -->
```yaml
route: A
dafny_family_loaded:
  - DafnyCore        # anchor
  - DafnyDriver      # anchor
  - DafnyLanguageServer  # in route-a.json assemblies[], NOT anchors
dafny_family_absent:
  - DafnyPipeline    # NOT loaded on Route A (ADR-0001/DD-007)
```
<!-- correctless:route-a-production-assemblies:end -->

## Entrypoints

> **Design-stage (discharges OQ-002 of the Phase 0.1 worker spec).** `src/` is
> empty; the handlers/scopes below are the **planned** contract that the Phase 0.1
> spec's `[integration]` invariants bind their Entry/Through/Exit to. No code exists
> yet — these are commitments, not artifacts. Defined greenfield-additively by
> `/carchitect` (2026-07-24) without disturbing the frozen PAT/PROHIBIT/TB entries.

<!-- correctless:entrypoints:start -->
```yaml
- name: "corrected-cli"
  type: cli
  handler: "src/Corrected.Cli/Program.cs:Main"
  test_via: "exec the PUBLISHED self-contained `corrected` binary VERBATIM — same argv[0] form and working directory the docs specify (AP-020; never a normalized/absolute-path proxy) — asserting init/check/certify/explain behavior"
  scope:
    - "src/Corrected.Cli/**"
- name: "corrected-core"
  type: library
  handler: "src/Corrected.Core/CertificationRun.cs:Execute"
  test_via: "public API import from the integration test project — drive the in-process certification pipeline (intake -> lock -> ownership/protected-surface -> verify -> honesty -> vacuity -> predicate -> receipt) over a fixture lock + subject; assert the fixture fails BEFORE the guard runs (AP-010)"
  scope:
    - "src/Corrected.Core/**"
- name: "dafny-adapter"
  type: library
  handler: "src/Corrected.DafnyAdapter/DafnyAdapter.cs:Resolve"
  test_via: "public API import within the adapter's AssemblyLoadContext, plus a static import-boundary scan and a runtime loaded-assembly assertion (INV-006/034) and the solver-identity / locked-restore / config-isolation tests carried from the spike (INV-035)"
  scope:
    - "src/Corrected.DafnyAdapter/**"
- name: "readiness-build-gate"
  type: cli
  handler: "gate/Corrected.Gate.Kernel/ReadinessGate.cs:EvaluateReadiness (pure kernel + DTOs in the isolated I/O-free Corrected.Gate.Kernel project, EXT6-05: (ReadinessBlock, probeResults) -> {Pass|Fail, offending}) + a separate probe orchestrator (IEvidenceProbe/ProbeResult) and a shipped-closure ProductionSurfaceScanner in gate/Corrected.Gate/"
  test_via: "from a CLEAN checkout (git clone + `rm -rf spikes/dafny-compat/out/` — the correct path; there is no top-level out/, and that tree is gitignored) run the DOCUMENTED command — the committed runnable gate script `gate/run-readiness-gate.sh` (<GATE-SCRIPT>, EXT6-01/EXT7-01; NOT bare `dotnet test`, which swallows the banner and runs no executed-count guard), which internally runs `dotnet test <AGGREGATOR> --logger \"trx;LogFileName=gate.trx\"`, then validates the TRX, renders the INV-012 status to stdout, and returns the final gate exit code — where <AGGREGATOR> is the single pinned constant `gate/Corrected.Gate.slnx` iff an INV-014 pre-flight proves `.slnx` on SDK 10.0.302, else the classic `.sln` fallback (a `.slnx`/`.sln` aggregator over gate/Corrected.Gate + gate/Corrected.Gate.Kernel + gate/Corrected.Gate.Tests + gate/Corrected.Gate.Lint + gate/Corrected.Provenance). Parse the TRX so zero-discovery / a below-floor executed count FAILS — the executed-count guard lives OUTSIDE the discovered suite, in an EXTRACTED RUNNABLE SCRIPT executed verbatim from clean (never a YAML/README grep — the PMB-001 trap). It drives the pure kernel over the committed SUPPLIED-(block, probeResults) fixture table (BLOCKED-all-false -> Pass; READY+satisfied:false/evidence:null/refuted-probe/unresolvable-reference -> Fail; indeterminate -> Fail) AND the real probe orchestrator (Stage A pre-migration: P1 evidence-schema-incomplete false; Stage B post-migration: P1 true; P2/P3 false-on-absent -> BLOCKED either way). It also homes the INV-036 production-surface SHIPPED-CLOSURE scan (out-of-process pinned-SDK build, not a path scan). NOTE: the INV-044 history-registry meta-test is a DEFERRED extension of this entrypoint (lands with Phase-0.1 certification runtime, readiness-gate-carrier DD-005) — NOT part of this carrier's required suite"
  scope:
    - "gate/**"
    - "test/**"
    - "global.json"
    - "spikes/dafny-compat/**"
    - ".correctless/specs/phase-0-1-worker.md"
- name: "reference-ci-provenance"
  type: cli
  handler: ".github/workflows/phase-0-1-reference-ci.yml:verify-before-run"
  test_via: "run the reference-CI lane (or its extracted script) with the PINNED external SLSA/signature verifier + pinned Cosign identity against a tampered-artifact fixture (INV-031/032/033), plus the determinism lane (PR1-built: .github/workflows/p3-determinism-lane.yml -> scripts/determinism-lane.sh) that drives two nested runs and emits a RunReceipt binding the observed platform identity (ProcessorCount / RID / arch / pinned OS label / kernel / SDK) to a derived closed-table (execution × comparison) status pair — comparison_status=equal iff every per-role deterministic projection agrees across the two runs, exit non-zero on different (INV-001/002/003/005 / P3). This entry was reconciled under INV-023 — the P3/determinism ownership re-homed to readiness-build-gate/TB-007 (TB-007 registered in the Trust Boundaries section below); gate/Corrected.Provenance/** stays in scope but is reused only by reimplementation of the generic in-toto/DSSE contracts (PRH-010), not as a shipped dependency"
  scope:
    - ".github/workflows/**"
    - "gate/Corrected.Provenance/**"
```
<!-- correctless:entrypoints:end -->

### Entrypoint → invariant-group map (design-stage)

- **corrected-cli** — the operator surface and the AP-020 verbatim-invocation home; `corrected explain` renders receipts / INV-038 failure artifacts to human-actionable text (INV-040).
- **corrected-core** — the in-process certification pipeline; Entry/Through/Exit for the bulk of the `[integration]` invariants: intake/lock/identity (INV-007..013), ownership/protected-surface (INV-014..018), fragment gate + verification + resource plan + watchdog (INV-019..023), honesty/vacuity (INV-024..026), success-predicate/receipt/schemas (INV-027..030, INV-041/042/047), and INV-037/038/039/045/046/048. INV-044's **runtime** supported-version dispatch table ships here.
- **dafny-adapter** — the single Dafny boundary (PAT-001 / PROHIBIT-002); INV-006/034/035.
- **readiness-build-gate** — the test/build-gate carrier; INV-001/002/003/004/036/043. INV-044's append-only **history** registry + meta-test is *homed* here but is a **deferred extension** built with Phase-0.1 certification runtime (readiness-gate-carrier DD-005), NOT part of the carrier's initial required suite. The readiness gate lives here so it can enforce itself without tripping its own production-code ban. The P3/**determinism** attestation claim (INV-005) re-homes here under INV-022/023 and is homed at the TB-007 trusted-CI evidence signing/verification boundary.
- **reference-ci-provenance** — the release-provenance lane (TB-003); INV-031/032/033. The P3/determinism claim re-homes to readiness-build-gate/TB-007 (INV-022/023); gate/Corrected.Provenance/** stays in this lane's scope but is reused only by reimplementation of the generic in-toto/DSSE contracts (PRH-010).

### Production-surface partition (INV-036, deny-by-default)

INV-036 / PRH-008 need a deterministic partition so a path-scoped CI check can fail a PR that lands production code while `effective_lifecycle != ENTERED`:

- **Production surface** (deny-by-default — non-trivial content here while `effective_lifecycle != ENTERED` trips PRH-008): `src/Corrected.Core/**`, `src/Corrected.DafnyAdapter/**`, `src/Corrected.Cli/**`. Any NEW top-level `src/` package is production until explicitly listed as carrier.
- **Exempt carrier / test / CI surface** (may carry content while BLOCKED): `gate/**`, `test/**`, `.github/workflows/**`, and `**/*.Tests/**` — but the **shipped compilation closure overrides path exemption** (spec-review EXT-04/RS-RT-04): `**/*.Tests/**` is exempt ONLY for an *independent top-level test project* neither referenced nor linked by any shipped `src/Corrected.*` project. A `*.Tests` directory, a linked `<Compile Include>`, a generated/analyzer-emitted source, or a first-party binary `<PackageReference>`/`<Reference>` that lands *inside* a shipped project's built closure is **production**, not exempt — INV-011 enforces over the real MSBuild/Roslyn closure (Compile items + project/assembly references + generated sources), not a path/content scan. Note the split from INV-044: its runtime dispatch table is production (`src/Corrected.Core/**`); only its append-only history registry + meta-test are exempt (`gate/**`).

### Design decisions (this `/carchitect` session, 2026-07-24)

- **Mode:** greenfield-additive — preserved the entire existing doc (frozen PAT-001..004, PROHIBIT-001/002, TB-001..004, Conventions, Known Limitations); added only the two component rows, the Entrypoints block, the invariant-group map, and the surface partition.
- **Planned .NET layout:** `src/Corrected.Core` (core worker) + `src/Corrected.DafnyAdapter` (sole Dafny boundary) + `src/Corrected.Cli` (`corrected`); non-shipped `gate/` (readiness + build gates) and `test/` (integration tests); `.github/workflows/` (reference CI). Paths are commitments, not artifacts.
- **Entrypoint granularity:** one per invariant-testing surface (CLI exec, in-process core API, adapter boundary, readiness/build gate, reference-CI lane) so every `[integration]` invariant has a concrete Entry/Through/Exit.
- **DD-007 (applied later, not in this session):** the component-set change (drop `DafnyPipeline`, add `DafnyDriver` + `DafnyLanguageServer` in the core-worker row) was applied on 2026-07-24 when ADR-0001 was promoted to accepted (DF-002); P1's component-table gate (INV-003 enforcement-(b)) re-verifies it in the build-gate carrier.

## Design Patterns

### PAT-001: DafnyAdapter boundary
- All Dafny SDK calls sit behind one `DafnyAdapter` boundary and one exact
  package lock; the design assumes neither source nor binary compatibility
  across Dafny versions.
- A toolchain upgrade recompiles the adapter and reruns the ownership,
  closure, bypass, and fingerprint suites.
- Violates it: importing Dafny packages outside the adapter; reconstructing
  Dafny semantics from a second parser (see PROHIBIT-002).

### PAT-002: Split search/certification verifier paths
- The **search verifier** may use in-process pipelines, a persistent Language
  Server, and caches to amortize setup during proof search.
- The **certification verifier** starts from the materialized
  certification-subject in a fresh process, loads only lock-approved
  resources, uses no search-session cache, and verifies the complete closure.
- Both paths share the verification-plan schema and are differentially
  tested; on any disagreement the fresh certification path wins. Search
  caches and Language Server state are never receipt evidence.
- Violates it: certification reading search-session state; treating search
  verifier output as acceptance evidence.

### PAT-003: Fail-closed JSONL protocol seam
- C# worker and TypeScript adapter communicate via strict LF-delimited JSON
  over stdin/stdout: stdout carries protocol records only, diagnostics go to
  stderr, records have explicit size limits, large artifacts travel by
  content-addressed descriptor.
- Messages carry request IDs, candidate and lock digests, state versions,
  typed results, and schema versions. Malformed, stale, reordered, duplicate,
  or cross-candidate responses fail closed.
- Generated TypeScript types/validators are convenience, not authority: the
  C# endpoint validates every request.
- Violates it: adapter-side trust in its own validation; protocol records on
  stderr or diagnostics on stdout; inline large payloads.

### PAT-004: Structural enforcement over prose-level instruction
- Adopted from the Correctless project's PAT-018 (public repo:
  joshft/correctless), where it emerged from repeated incidents of
  prose-described constraints silently not holding. It is also Corrected's own
  thesis applied to itself: the deterministic acceptance layer exists because
  agent discipline is not enforcement.
- Any invariant that matters must be enforced by a mechanism that runs —
  a verifier check, a schema validator, a digest comparison, a gate that
  fails closed, a test that exercises the real path. An invariant stated
  only in documentation, spec prose, or agent instructions is unenforced.
- Review rule: every spec invariant names its enforcement mechanism (see
  antipatterns AP-004); "the agent/developer will make sure" is not one.
- Violates it: prose-only tool restrictions, comment-documented protocol
  constraints with no validator, review checklists with no gate.

### PAT-005: Readiness-gate block checked by test; the exempt carrier enforces itself
- A machine-readable `implementation_readiness` block (in a committed spec) is
  the single source of truth for whether production implementation may land; a
  fail-closed gate test re-derives executable evidence for each precondition and
  refuses READY without it (never trusting the declared flag). The gate + the
  production-surface ban live in the **non-shipped exempt carrier** (`gate/`,
  `test/`) so the enforcement can run without tripping its own production-code
  ban (INV-036 self-enforcement). Realizes PAT-004 for the readiness gate.
- A precondition's `satisfied:true` flip is legitimate only when bound to a
  passing gate; the block is never edited to READY/`satisfied:true` ahead of the
  evidence the gate re-derives (spec-review parent-carrier-atomicity lens).
- Violates it: trusting the `satisfied` flag without re-deriving evidence; homing
  the gate in a shipped `src/` package; a committed `satisfied:true` with no
  passing carrier test re-deriving it.
- Registered by the readiness-gate-carrier feature
  (`.correctless/specs/readiness-gate-carrier.md`, INV-002/004/005/011 + PRH-002).

## Prohibitions

- **PROHIBIT-001**: The TypeScript adapter never owns run state, parses
  Dafny, recomputes identities, classifies ownership, ranks checkpoints, or
  evaluates acceptance. Those live only in the C# core.
- **PROHIBIT-002**: No second-language reimplementation of Dafny semantics.
  Any non-C# implementation must use the semantic worker or an upstream
  resolved-program export passing the identical conformance corpus — never a
  second parser.

## Trust Boundaries

### TB-001: Worker↔adapter process seam
- Everything crossing the stdin/stdout JSONL protocol is untrusted until the
  C# endpoint validates it (PAT-003). The adapter is outside the policy TCB.

### TB-002: Search vs certification
- Search-side state (caches, LSP sessions, agent proposals) is untrusted
  input to certification. Only the fresh-process certification verifier's
  results are acceptance evidence (PAT-002).

### TB-003: Release provenance / bootstrap TCB
- Published executables are content-addressed, signed, and carry SLSA Build
  Provenance. The external signature/SLSA verifier and trusted builder are an
  explicit bootstrap TCB — verified by independently pinned tooling before
  `corrected certify` runs, never recursively by the Corrected binary itself.

### TB-004: Inbound toolchain supply chain
- Dev-time intake of third-party toolchain artifacts (Dafny/Boogie-family
  NuGet packages and their transitive graph, the native Z3 solver binary, the
  .NET SDK) is an untrusted-input boundary distinct from TB-003's *outbound*
  release provenance. Crosses: external package/asset sources → build and
  dev-host execution.
- Invariant: every toolchain artifact is exact-pinned and digest-verified
  before use — locked-mode NuGet restore (content hashes) under a
  `<clear/>`-scoped single-source config, SHA-256-pinned solver assets
  installed outside ambient discovery locations, exact SDK pin with
  roll-forward disabled (the repo-root Phase-0.1 `global.json` documents a
  `rollForward: latestPatch` **exception** with `allowPrerelease: false` for
  repo-wide security-patch availability — the committed lockfile pins PACKAGE
  versions but does NOT make different SDK patches identical; `latestPatch` picks
  the highest installed qualifying patch, bounded to feature-band 3xx >= 10.0.302,
  and a build-time band assertion records the resolved SDK; see the Phase-0.1
  extension below and Microsoft's global.json docs, EXT6-03) — and evidence binds
  claims to the identities actually loaded/executed, never merely referenced.
  Intake failure is fail-closed: no verdict, never a silent fallback to ambient resolution.
- Violated when: a floating/range version resolves; a machine-level source,
  environment variable, or inherited MSBuild props alters resolution; an
  ambient solver answers instead of the pinned one; a verdict or receipt
  cites artifacts that were not the ones loaded.
- Exercised at (first concrete enforcement — the non-production
  `spikes/dafny-compat/` harness; the production `DafnyAdapter` lands in
  Phase 0.1): `spikes/dafny-compat/Directory.Packages.props` + per-project
  `packages.lock.json` + `NuGet.Config` (exact-pin locked-mode restore);
  `spikes/dafny-compat/global.json` (exact SDK pin, roll-forward disabled);
  `spikes/dafny-compat/config/z3-pin.json` + `config/net8-control-pin.json`
  (SHA-256 asset pins); `spikes/dafny-compat/scripts/provision-z3.sh`
  (digest-verified solver install outside ambient discovery locations);
  `spikes/dafny-compat/scripts/run-spike.sh` (`env -i` clean-environment
  re-exec + locked restore + fail-closed intake).
- Test: `spikes/dafny-compat/tests/SpikeTests/Inv001ToolchainPinTests.cs`
  (exact pins, locked-mode negative restore, config/props isolation) and
  `spikes/dafny-compat/tests/SpikeTests/Inv003SolverIdentityTests.cs`
  (solver digest, executed-solver identity, per-route removal test).
- Registered by the dafny-compat-spike feature (BND-001/BND-002 + STRIDE in
  `.correctless/specs/dafny-compat-spike.md`, review finding RS-013); DD-006
  there makes this boundary a standing obligation for every future
  toolchain-bump spec.
- **Phase 0.1 extension (readiness-gate carrier):** the gate adds a third-party
  YAML parser (`YamlDotNet`, exact-pinned + locked), the **analysis toolchain**
  (`Microsoft.CodeAnalysis.CSharp` ONLY, pinned + locked — **no `Microsoft.Build.*`
  PackageReference** (EXT4-04); INV-011 runs the shipped-closure scan **out-of-process
  against the pinned SDK's MSBuild**, asserted via `dotnet msbuild -version`, never an
  in-process `Microsoft.Build`/`MSBuildLocator` resolving ambient machine MSBuild), and a
  **repo-root** `global.json` (exact SDK pin, `rollForward: latestPatch` per the
  documented exception above) to this boundary — the general "any dev-time
  third-party NuGet/toolchain artifact" case beyond the Dafny/Z3/SDK set above.
  Exercised at: `gate/NuGet.Config` (`<clear/>` single-source), a per-project `packages.lock.json`
  (one per gate project — Gate/Kernel/Tests/Lint; INV-015),
  `gate/Directory.Build.props`, the repo-root `global.json` (its `sdk.version` kept
  **semantically synced** — NOT byte-identical — with `spikes/dafny-compat/global.json`
  by a version-field sync test; `rollForward`/comments legitimately differ). See
  `.correctless/specs/readiness-gate-carrier.md` INV-011/INV-015/INV-016/BND-002.

### TB-005: (reserved) Source-byte intake — untrusted `.dfy` source → policy TCB
- **Reserved by the parent** `phase-0-1-worker.md` (BND-003) for arbitrary handed
  `.dfy` source-byte intake into the policy TCB; to be registered here by the
  parent's own `/cupdate-arch`. It is a **distinct boundary** from the readiness/ADR
  intake boundary (TB-006 below). The readiness-gate carrier does NOT define TB-005;
  the v3 carrier amendment mistakenly registered the readiness boundary as TB-005 and
  mislabeled it "Parent BND-003" — corrected 2026-07-25 to TB-006 (readiness-gate
  round-2 EXT2-09). Parent BND-003 anchors: snapshot-first (O_NOFOLLOW) + path grammar
  + UTF-8 + single-file/fragment gate; typed `RejectionReason`; fail-closed.

### TB-006: Readiness-block / ADR / evidence intake / tamper boundary
- The committed `implementation_readiness` block (in
  `.correctless/specs/phase-0-1-worker.md`) and the committed ADR/evidence the P1
  probe reads (`docs/adr/ADR-0001-*.md`, the pinned canonical evidence sample,
  `route-a.json`) are **untrusted, tamperable input** to the readiness gate:
  anyone with commit access can duplicate the block, inject keys, flip a
  `satisfied` flag, forge an ADR claim or its decision fields, strip an evidence
  sample's probe results, or repoint an evidence path. Crosses:
  committed markdown/JSON → the gate's parse + verdict decision. (Distinct from the
  parent's TB-005 source-byte boundary; the parent flagged its own BND-003 for TB-005.)
- Invariant: exactly one bounded readiness block; an AST-hardened,
  closed-vocabulary, tag/anchor/alias-rejecting strict parse into a validated
  immutable domain type; the **same hardening machinery** applied to the ADR
  `adr_lint` block via a **distinct `AdrLintBlock` DTO** (no reuse of the spike's
  permissive line-scanner for a trust-boundary decision); the ADR's decision fields
  (`selected_route == A`, `verdict == COMPATIBLE`) asserted in the authoritative path;
  each precondition's evidence re-derived and cross-checked against the declared flag;
  the P1 evidence path pinned to the **canonical** sample (not taken from the tamperable
  ADR field) and the evidence-schema integrity anchored to a **compiled** constant; the
  COMPATIBLE recompute guarded by **keyed-set cardinality equality** against the pinned
  probe manifest (no vacuous `∀`); supersession discovered by a **terminal rule** over a
  **compiled ADR registry** asserted **set-equal** to the ADRs carrying an `adr_lint` block on
  disk (an unregistered on-disk block fails closed — "register this ADR" — never ignored; the
  round-2 "pinned allowlist that ignores out-of-list ADRs" was itself the bypass, reversed by
  R3-B4/EXT4-04). Intake failure is fail-closed
  (reject / BLOCKED), never a silent pass.
- Violated when: a duplicate/oversize block or a tag/anchor/alias parses; a
  forged second ADR route-claim / forged decision fields / a superseding ADR is missed;
  a stripped/plan-shrunk evidence sample passes the recompute; a P1 evidence
  path is read from the ADR field or resolved by glob (a leaked `out/**` copy);
  the ADR block is parsed by the non-hardened spike scanner.
- Exercised at (Phase 0.1, readiness-gate carrier): `gate/Corrected.Gate/**`
  (parser + probes + scanner) + `gate/Corrected.Gate.Kernel/**` (the isolated pure kernel + DTOs, EXT6-05)
  + `gate/Corrected.Gate.Lint/**` and its `*.Tests` fixture corpus.
- Test: `.correctless/specs/readiness-gate-carrier.md` INV-001/002/003/005/008 +
  BND-001/BND-003 + the STRIDE-for-TB-006 section. Registered by that feature.

### TB-007: trusted-CI evidence signing/verification
- The determinism-attestation lane's durable claim crosses a trust boundary
  distinct from TB-003's *outbound* release provenance: a trusted-CI run executes
  the two nested determinism runs, then signs the emitted RunReceipt so a later
  consumer can verify the recorded claim was produced by the trusted-CI lane and
  not altered after the fact. Crosses: trusted-CI execution → a durable,
  provenance-bound determinism claim, established by signing/verification of the
  receipt. This boundary reuses TB-004 (inbound cosign intake) and TB-006
  (committed evidence / tamper) as-defined — it adds only the trusted-CI →
  provenance-bound-determinism crossing and does not redefine them.
- Invariant: a determinism claim is acceptance evidence only when it is signed in
  the trusted-CI lane, its signer identity verifies against the pinned Cosign
  identity (TB-004, as-defined), and the signed receipt is bound to committed
  evidence (TB-006, as-defined). An unsigned, unverifiable, or non-committed
  determinism receipt is not evidence; intake failure is fail-closed.
- Violated when: a determinism claim is trusted without a trusted-CI signature;
  the signing/verification step is skipped or self-verified by the produced
  binary; or this boundary is silently folded into / relabeled as TB-003 (release
  provenance) instead of registered as its own boundary.
- Homed by the `readiness-build-gate` entrypoint — the P3/determinism ownership
  (INV-005) re-homed here under INV-022/023. Registered by the
  p3-determinism-attestation feature
  (`.correctless/specs/p3-determinism-attestation.md`, INV-022/INV-023).

## Conventions

- `DESIGN.md` is the single authoritative design source; changes to frozen
  design commitments happen there first, then propagate here.

## Known Limitations

- No production code is implemented; every component above is a design
  commitment, not shipped behavior (`src/` is empty).
- Phase 0.0's foundational assumption — that Dafny 4.11.0's `net8.0` packages
  run in-process on a .NET 10 host for parse / resolve / Z3-backed verify /
  resolved-AST recovery — **has now been validated** by the permanent
  `spikes/dafny-compat/` conformance harness. Both integration routes are
  **COMPATIBLE** (suite-attested, 274/274): Route A (`DafnyDriver` /
  `CliCompilation`, additionally loading `DafnyLanguageServer`) and Route B
  (hand-assembled `DafnyCore` + `DafnyPipeline` + `Boogie.ExecutionEngine`).
  Recorded in `docs/adr/ADR-0001-dafny-integration-boundary.md`, promoted to
  **accepted** on 2026-07-24 (DF-002). Route **A** is the selected boundary; the
  DD-007 component-table propagation (drop `DafnyPipeline`, add `DafnyDriver` +
  `DafnyLanguageServer` in the core-worker row above) is applied. The later
  Phase-0.1-entry gates (the `P0-*` capability set + `DF-003`, DESIGN §13 v1.14;
  former bullets 8–11 re-homed to Phase-1 / Phase-0.1-exit) are still unstarted.
