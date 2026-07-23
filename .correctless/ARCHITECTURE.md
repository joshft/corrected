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
| C# core worker | TBD (monorepo package) | Deterministic policy/acceptance core on .NET 10 LTS. Owns run state, intake/lock resolution, ownership classification, verification, acceptance evaluation, receipt emission. Uses pinned Dafny SDK packages (`DafnyCore`, `DafnyPipeline`, …). |
| `corrected` CLI | TBD (part of core distribution) | Reference acceptance implementation: `corrected init`, `corrected check`, `corrected certify`, etc. Runnable with no model, Node.js, TypeScript, or Pi session. |
| TypeScript Pi adapter | TBD (monorepo package) | Small integration package realizing the core-defined methodology inside the Pi agent runtime. Manages Pi lifecycle, cancellation, progress, proposal transport. Integration code only — not a second policy implementation. |
| Worker↔adapter protocol | Defined by core schemas | Strict LF-delimited JSON over stdin/stdout; versioned commands/events/results; large artifacts by content-addressed descriptor. |

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
  **COMPATIBLE** (suite-attested, 273/273): Route A (`DafnyDriver` /
  `CliCompilation`, additionally loading `DafnyLanguageServer`) and Route B
  (hand-assembled `DafnyCore` + `DafnyPipeline` + `Boogie.ExecutionEngine`).
  Recorded in provisional `docs/adr/ADR-0001-dafny-integration-boundary.md`.
  Route **A** is the selected boundary; formal ADR promotion
  (provisional → accepted) and the DD-007 component-table propagation remain
  pending as the final Phase 0.0 feature's obligation (DF-002). Later Phase 0.0
  gates (bullets 4–12) are still unstarted.
