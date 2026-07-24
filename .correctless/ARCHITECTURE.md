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
| C# core worker | `src/Corrected.Core/` | Deterministic policy/acceptance core on .NET 10 LTS. Owns run state, intake/lock resolution, ownership classification, verification, acceptance evaluation, receipt emission. Uses pinned Dafny SDK packages (`DafnyCore`, `DafnyPipeline`, …). |
| DafnyAdapter | `src/Corrected.DafnyAdapter/` | The single Dafny SDK boundary (PAT-001) — the sole package that imports Dafny/Boogie assemblies and hosts the in-process solver. Core reaches Dafny only through it (INV-006/034/035). |
| `corrected` CLI | `src/Corrected.Cli/` | Reference acceptance implementation: `corrected init`, `corrected check`, `corrected certify`, `corrected explain`. Runnable with no model, Node.js, TypeScript, or Pi session. |
| Test/build-gate carrier (NOT shipped) | `gate/`, `test/` | Homes the readiness-gate checker (INV-001/002/036), the append-only schema-version *history* registry + meta-test (INV-044), and the from-clean / path-scoped gates. Kept OUT of the shipped core/CLI so the gate can enforce itself without tripping its own production-code ban (INV-036). |
| TypeScript Pi adapter | `adapters/pi/` (Phase 1) | Small integration package realizing the core-defined methodology inside the Pi agent runtime. Manages Pi lifecycle, cancellation, progress, proposal transport. Integration code only — not a second policy implementation. |
| Worker↔adapter protocol | Defined by core schemas (Phase 1) | Strict LF-delimited JSON over stdin/stdout; versioned commands/events/results; large artifacts by content-addressed descriptor. |

> **Planned paths, not artifacts.** `src/` is empty; the locations above are the
> design-stage layout the Phase 0.1 entrypoints bind to. The `DafnyPipeline`
> mention in the core-worker row is retained pending the DD-007 propagation, which
> is DF-002's obligation (checked by P1's component-table gate), not `/carchitect`'s.

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
  handler: "gate/Corrected.Gate/ReadinessGate.cs:Evaluate"
  test_via: "exec the gate project from a CLEAN checkout (git clone + `rm -rf out`) via `dotnet test gate/Corrected.Gate`, driving EvaluateReadiness over the committed SUPPLIED-block fixture table (BLOCKED-all-false -> Pass; READY+satisfied:false/evidence:null/refuted-probe -> Fail); also homes the INV-044 history-registry meta-test and the INV-036 production-surface path scan"
  scope:
    - "gate/**"
    - "test/**"
    - ".correctless/specs/phase-0-1-worker.md"
- name: "reference-ci-provenance"
  type: cli
  handler: ".github/workflows/phase-0-1-reference-ci.yml:verify-before-run"
  test_via: "run the reference-CI lane (or its extracted script) with the PINNED external SLSA/signature verifier + pinned Cosign identity against a tampered-artifact fixture (INV-031/032/033), plus the determinism lane that emits a counted ran/skipped outcome with observed cores + RID (INV-005 / P3)"
  scope:
    - ".github/workflows/**"
    - "gate/Corrected.Provenance/**"
```
<!-- correctless:entrypoints:end -->

### Entrypoint → invariant-group map (design-stage)

- **corrected-cli** — the operator surface and the AP-020 verbatim-invocation home; `corrected explain` renders receipts / INV-038 failure artifacts to human-actionable text (INV-040).
- **corrected-core** — the in-process certification pipeline; Entry/Through/Exit for the bulk of the `[integration]` invariants: intake/lock/identity (INV-007..013), ownership/protected-surface (INV-014..018), fragment gate + verification + resource plan + watchdog (INV-019..023), honesty/vacuity (INV-024..026), success-predicate/receipt/schemas (INV-027..030, INV-041/042/047), and INV-037/038/039/045/046/048. INV-044's **runtime** supported-version dispatch table ships here.
- **dafny-adapter** — the single Dafny boundary (PAT-001 / PROHIBIT-002); INV-006/034/035.
- **readiness-build-gate** — the test/build-gate carrier; INV-001/002/003/004/036/043 and INV-044's append-only **history** registry + meta-test. The readiness gate lives here so it can enforce itself without tripping its own production-code ban.
- **reference-ci-provenance** — the release-provenance / determinism lane (TB-003); INV-005/031/032/033.

### Production-surface partition (INV-036, deny-by-default)

INV-036 / PRH-008 need a deterministic partition so a path-scoped CI check can fail a PR that lands production code while `implementation_readiness.status = BLOCKED`:

- **Production surface** (deny-by-default — non-trivial content here while BLOCKED trips PRH-008): `src/Corrected.Core/**`, `src/Corrected.DafnyAdapter/**`, `src/Corrected.Cli/**`. Any NEW top-level `src/` package is production until explicitly listed as carrier.
- **Exempt carrier / test / CI surface** (may carry content while BLOCKED): `gate/**`, `test/**`, `.github/workflows/**`, and any `**/*.Tests/**`. Note the split from INV-044: its runtime dispatch table is production (`src/Corrected.Core/**`); only its append-only history registry + meta-test are exempt (`gate/**`).

### Design decisions (this `/carchitect` session, 2026-07-24)

- **Mode:** greenfield-additive — preserved the entire existing doc (frozen PAT-001..004, PROHIBIT-001/002, TB-001..004, Conventions, Known Limitations); added only the two component rows, the Entrypoints block, the invariant-group map, and the surface partition.
- **Planned .NET layout:** `src/Corrected.Core` (core worker) + `src/Corrected.DafnyAdapter` (sole Dafny boundary) + `src/Corrected.Cli` (`corrected`); non-shipped `gate/` (readiness + build gates) and `test/` (integration tests); `.github/workflows/` (reference CI). Paths are commitments, not artifacts.
- **Entrypoint granularity:** one per invariant-testing surface (CLI exec, in-process core API, adapter boundary, readiness/build gate, reference-CI lane) so every `[integration]` invariant has a concrete Entry/Through/Exit.
- **NOT touched:** the DD-007 component-set change (drop `DafnyPipeline`, add `DafnyLanguageServer`) remains DF-002's obligation and is verified by P1's component-table gate — not done here.

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
  roll-forward disabled — and evidence binds claims to the identities
  actually loaded/executed, never merely referenced. Intake failure is
  fail-closed: no verdict, never a silent fallback to ambient resolution.
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
  Recorded in provisional `docs/adr/ADR-0001-dafny-integration-boundary.md`.
  Route **A** is the selected boundary; formal ADR promotion
  (provisional → accepted) and the DD-007 component-table propagation remain
  pending as the final Phase 0.0 feature's obligation (DF-002). Later Phase 0.0
  gates (bullets 4–12) are still unstarted.
