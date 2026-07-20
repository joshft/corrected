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

## Conventions

- `DESIGN.md` is the single authoritative design source; changes to frozen
  design commitments happen there first, then propagate here.

## Known Limitations

- Nothing is implemented; every component above is a design commitment, not
  shipped behavior. Phase 0.0 must still validate the .NET 10 / Dafny 4.11
  package-compatibility assumption with a reproduced integration spike.
