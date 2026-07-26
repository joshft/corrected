# Spec: Phase 0.1 Production Worker — Deterministic Acceptance Slice (Implementation Contract)

## Metadata
- **Created**: 2026-07-24T19:30:17Z
- **Status**: reviewed
- **Impacts**: dafny-compat-spike (consumes its Phase 0.0 outputs; must not fork them)
- **Branch**: feature/phase-0.1-worker-spec
- **Research**: null (no external-library research triggered; all inputs are DESIGN.md v1.13, ADR-0001, and the committed spike)
- **Recommended-intensity**: critical
- **Intensity**: high
- **Intensity reason**: keyword signals (`trust boundary`, `adversary`, `threat model`) → critical; project floor `high`; TB-003/TB-004/TB-002 referenced. Humility qualifier: 1 completed feature (<5) → low detection confidence.
- **Override**: lowered (critical → high; user decision 2026-07-24 — full 12-section template retained; behavioral detail re-derived in sub-specs at build time)

> **ID namespacing note.** `INV-nnn`, `PRH-nnn`, `BND-nnn`, `EA-nnn`, `OQ-nnn`
> below are local to THIS spec. References to *the spike's* INV-010 (the
> cross-run determinism check in `spikes/dafny-compat`) are always written as
> "the spike's INV-010" to avoid collision with this spec's `INV-0xx`.

> **Readiness, not lifecycle.** This spec's document `Status` (draft → reviewed →
> approved) is the /cspec pipeline lifecycle of the *artifact*. It is distinct
> from the feature's **Implementation-Readiness** (§Implementation-Readiness
> Gate), which is the thing the requester asked to hold BLOCKED until three
> Phase 0.0 preconditions carry executable evidence. A spec can be `approved`
> (reviewed, coherent, ready to build against) while implementation-readiness is
> still `BLOCKED`. The two never substitute for each other.

## Context

Phase 0.1 is the first production build of Corrected: the LLM-free, `CORE`
execution-mode deterministic acceptance stack for the narrow Dafny fragment
defined in DESIGN.md v1.13 §13 — exact-byte intake, resolved `corrected.lock`,
proof-completion ownership/protected-surface classification, complete
verification under a locked resource plan, mechanical honesty and vacuity
policy, canonical receipt/predicate emission, and content-addressed release
artifacts with SLSA Build Provenance verified by reference CI. It composes with
(never forks) the Phase 0.0 `spikes/dafny-compat/` conformance outputs: the
ADR-0001-selected Dafny integration boundary (Route A) and the TB-004
exact-pinned, digest-verified toolchain. This document is written as an
**implementation contract**: every buildable-without-an-LLM item in §13 is an
invariant with a named enforcement mechanism (PAT-004), and the whole is gated
BLOCKED behind three explicit Phase 0.0 dependencies.

## Scope

**In scope** (the DESIGN.md §13 "build without any LLM dependency" list, at
contract granularity):
- Exact-byte intake snapshot, deterministic file manifest, resolved lock, and
  canonical certification-subject manifest (§6 digest graph).
- Proof-completion-only ownership classification and a parser-based protected
  surface permitting only the four Phase 0.1 edit classes, enforcing the
  inherited ghost-erasure/executable-closure invariant.
- Single-file Phase 0.1 package/fragment intake gate (allowlist grammar; typed
  rejection of everything outside it).
- Complete verification-scope resolution and direct complete verification under
  the locked resource-unit plan for the `checked` and `verified-nonvacuous`
  profiles (the Phase 0.1 target profile per DESIGN §5 `target_profile` / §13; see
  INV-026).
- `dafny audit` plus supplemental rejection of the bypass classes reachable in
  the fragment; tri-state vacuity classification.
- Total analysis-status / environment / resource / search-outcome / evidence /
  disposition / profile-verdict schemas and the canonical receipt/predicate.
- Content-addressed Corrected release artifacts with an authenticated manifest
  and SLSA Build Provenance; a versioned Corrected predicate in an in-toto
  Statement; a reference-CI round trip authenticating with pinned Cosign and
  independently verifying the bundle.
- The `corrected` CLI reference implementation — `corrected init`, `corrected
  check`, `corrected certify`, and `corrected explain` (the human recovery
  renderer; brought in scope by spec-review RS-037 so INV-038's machine
  failure artifacts are human-readable).
- The **Implementation-Readiness Gate**: a machine-readable readiness block plus
  an enforcing test that holds the feature BLOCKED until the three Phase 0.0
  preconditions carry executable evidence.

**Explicitly NOT in scope** (deferred to sibling Phase 0.1 / later specs):
- The strict-LF JSONL worker↔adapter protocol seam (DESIGN §13 bullet 9 /
  PAT-003 / TB-001) — prototyped in the spike, productionized separately.
- The TypeScript Pi adapter and `MANAGED_PI` execution mode (`review_mode`,
  methodology record chain) — Phase 1.
- Any LLM/search proposal strategy (§7 portfolio) — this slice is `CORE`,
  `review_mode = NOT_REQUIRED`.
- Runtime evidence / `SEAM_TEST` beyond what the extern-free fragment can
  exercise (see OQ-003).
- Implementation-synthesis task mode (`replace_method_body`).

## Complexity Budget
- **Estimated LOC**: large (multi-thousand across intake, lock, adapter,
  classifier, verifier driver, honesty engine, receipt/predicate, provenance).
  This is an **umbrella contract**; implementation should decompose into
  sub-specs (see OQ-001) rather than land as one change.
- **Files touched**: new `src/` tree (currently empty) + a new production test
  project; new `.github/workflows` reference-CI lane; ADR/DESIGN propagation
  from DF-002.
- **New abstractions**: production `DafnyAdapter` (PAT-001 realization), lock
  resolver, ownership/protected-surface classifier + fingerprinter, honesty
  policy engine, vacuity classifier, canonical receipt/predicate emitter,
  release-provenance verifier; the readiness-gate checker and the append-only
  schema-version HISTORY registry + meta-test (INV-044), which live in the
  test/build-gate carrier, NOT the shipped core/CLI (INV-036) — while INV-044's
  runtime supported-version dispatch table ships WITH core, since certify needs it
  at runtime.
- **Trust boundaries touched**: TB-004 (inbound toolchain), TB-003 (release
  provenance / bootstrap TCB), TB-002 (search vs certification — degenerate here
  but the boundary principle still binds), and the intake boundary (BND-003,
  flagged for ARCHITECTURE.md registration as TB-005 — spec-review RS-016).
  TB-001 deferred with the protocol seam.
- **Risk surface delta**: **high**.

## Implementation-Readiness Gate

The feature is **BLOCKED** for implementation until all three preconditions
below carry executable evidence. This is enforced structurally
(INV-001/INV-002/INV-036), not by this prose — INV-002 forbids a false READY, and
INV-036 forbids production code landing while the flag is honestly BLOCKED.

**Canonical readiness-block schema (single source of truth — spec-review RS-001).**
The enforcing test (INV-001) parses THIS fenced block against exactly the key set
below and no other description. Any prose elsewhere in this document that
paraphrases the block is descriptive only; this table governs.

| Key | Level | Required | Type / vocabulary |
|-----|-------|----------|-------------------|
| `schema_version` | top | yes | integer; INV-001 rejects (fail-closed) an unrecognized version rather than under-parsing |
| `status` | top | yes | enum `{BLOCKED, READY}` |
| `ready_predicate` | top | yes | string; MUST equal the conjunction of the precondition `id`s (`"P1 AND P2 AND P3"`). INV-001 asserts this equality so `ready_predicate` cannot drift from the precondition set (RS-031); INV-002 evaluates readiness from the `preconditions` array, treating `ready_predicate` as a checked mirror, never as a second source of truth |
| `preconditions` | top | yes | array; exactly three, ids `{P1, P2, P3}` |
| `preconditions[].id` | item | yes | enum `{P1, P2, P3}` |
| `preconditions[].name` | item | yes | string |
| `preconditions[].satisfied` | item | yes | boolean |
| `preconditions[].evidence` | item | yes | nullable string (a test-id / gate / manifest path; never prose) |
| `preconditions[].discharges` | item | yes | array of finding ids |

```yaml
implementation_readiness:
  schema_version: 1          # readiness-block schema version; INV-001 rejects an unrecognized version fail-closed (RS-001)
  status: BLOCKED            # BLOCKED | READY  (READY requires every precondition satisfied AND evidence non-null)
  ready_predicate: "P1 AND P2 AND P3"   # human-readable mirror; INV-001 asserts it equals the conjunction of precondition ids
  preconditions:
    - id: P1
      name: adr-0001-promoted-or-superseded
      satisfied: false
      evidence: null          # test-id / gate that verifies the discharge; never prose
      discharges: [DF-002]
    - id: P2
      name: phase-0.0-gates-4-12-plus-open-medium-df-have-executable-evidence
      satisfied: false
      evidence: null          # path to a committed Phase-0.0 completion manifest whose every named gate is green-from-clean
      discharges: [DF-003]     # plus DESIGN.md §13 bullets 4–12
    - id: P3
      name: inv010-ci-determinism-exercised-not-silently-skipped
      satisfied: false
      evidence: null          # CI lane / reworked test proving the cross-run determinism check actually runs on the CI path
      discharges: []
```

**"Clean checkout" is defined operationally (spec-review RS-004).** Every
"green from a single clean-checkout run" claim below (INV-002, INV-004) means
`git clone` **plus `rm -rf` of the spike `out/` tree** — because the repository
currently commits `spikes/dafny-compat/out/` (run-id dirs, suite receipts), so a
bare clone is NOT `out`-clean and a probe reading on-disk `out/` could pass on
committed leaked prior-run state (AP-010/AP-021/PMB-002). Two obligations
follow: (a) every own-product probe binds to the CURRENT run's `RunContext`,
never to prior-run roots discovered on disk; (b) the operational fix — gitignore
`spikes/dafny-compat/out/` and `git rm --cached` it — must be validated against
the frozen-evidence constraint ([[dafny-spike-evidence-binding-fragile]]) before
landing, since it touches spike state; until then the `rm -rf spikes/dafny-compat/out/` step is
mandatory in every from-clean gate.

**Enforcement carrier (prerequisite).** The readiness gate and every invariant
below whose enforcement reads "CI test assertion" or "gate precondition" needs a
concrete home: the production test project + entrypoints contract that OQ-002
defers to `/carchitect`. That carrier must exist before the gate is meaningful —
its absence is itself a blocker on implementation-readiness approval (spec-review
finding 7 / RS-008). `/carchitect` must also **pin the production-package path
globs** (and the carrier/test glob) so INV-036's production-surface check is a
deterministic path partition, not a content heuristic. Ordering constraint
(RS-002):
<!-- correctless:readiness-current-state:start id="A2" -->
land the carrier and INV-002's positive READY-rejection fixture table before any real precondition discharges
<!-- correctless:readiness-current-state:end id="A2" -->
so the reject branch is proven before it first guards a real BLOCKED→READY
transition. These mechanisms are now homed in the `gate/` readiness-gate
carrier (the test/build-gate carrier).

## Invariants

### Group A — Readiness gate & Phase 0.0 dependency contract (the headline)

### INV-001: Readiness block is present, schema-valid, versioned, and closed-vocabulary
- **Type**: must
- **Category**: data-integrity
- **Statement**: this spec carries the `implementation_readiness` block above,
  parsed against the single canonical key table (§Implementation-Readiness Gate).
  `schema_version` is present and recognized (an unrecognized version fails
  closed, never under-parses — spec-review RS-001); `status ∈ {BLOCKED, READY}`;
  exactly the three preconditions P1/P2/P3 each with `id/name/satisfied/evidence/
  discharges`; and `ready_predicate` equals the conjunction of the precondition
  ids so it cannot silently drift from the precondition set (RS-031). Parsing is
  against the pinned key set, not prose detection, and rejects a file containing
  more than one `implementation_readiness:` block (duplicate = tamper — RS-002).
- **Violated when**: the block is missing or duplicated, `schema_version` is
  absent/unrecognized, `status` is outside the closed set, a precondition is
  added/removed, a required key is absent, or `ready_predicate` disagrees with
  the precondition ids.
- **Enforcement**: CI test assertion (a readiness-gate unit test parses the block
  from this file at its committed path against the canonical key table; the file
  path is a tested constant and its absence fails closed, not skips — RS-018/H).
- **Guards against**: AP-004 (claimed readiness exceeding its enforcement layer),
  AP-014 (format-pin defeated by an ambiguous key set).
- **Test approach**: unit
- **Risk**: medium

### INV-002: Readiness gate is fail-closed — READY is unreachable without executable evidence, and every branch is exercised
- **Type**: must
- **Category**: security
- **Statement**: the readiness gate is a pure decision function
  `EvaluateReadiness(blockText) → {Pass | Fail, offending_precondition}` over a
  SUPPLIED block string (not only the committed file — spec-review RS-002), so
  its rejecting branch is reachable in test. It FAILS the build if
  `status: READY` while any precondition has `satisfied: false` or
  `evidence: null`; and for each `satisfied: true` precondition it re-derives the
  discharge from the named `evidence` gate (below), never from the `satisfied`
  flag alone — an unresolvable/missing/nonexistent evidence reference is a hard
  fail-closed distinct from "probe ran and returned zero findings" (RS-002). Each
  evidence probe returns `satisfied=false` with a typed reason — **never throws or
  skips** — when its Phase-0.0 artifact is absent or `pending` (RS-003), so from a
  clean checkout (per the operational definition above) with all three false,
  `status: BLOCKED` is consistent and the test passes (no circular/bootstrap gate
  — AP-021). Every probe runs on every invocation regardless of `status`, and the
  independent probe verdict is cross-checked against the declared `satisfied`
  flag, so both BLOCKED-but-actually-satisfied and READY-but-actually-unsatisfied
  fail.
- **Violated when**: the flag is trusted without re-deriving evidence; READY is
  accepted with an unsatisfied/evidence-null precondition; a probe throws/skips on
  an absent artifact instead of returning `satisfied=false`; an unresolvable
  evidence reference re-derives vacuously; or the test only passes because a prior
  run left state on disk.
- **Enforcement**: gate precondition (CI test) driven by a committed fixture
  table of SUPPLIED blocks: (a) BLOCKED+all-false → Pass; (b) READY+one
  `satisfied:false` → Fail naming that precondition; (c) READY+one `evidence:null`
  → Fail; (d) READY+all-true but a probe REFUTES the evidence → Fail (proves
  re-derivation, not flag-trust); (e) READY+all-true+probes-confirm → Pass; plus
  an absent-artifact fixture per probe asserting `satisfied=false` (not throw).
<!-- correctless:readiness-current-state:start id="B5-currently-parses-blocked" -->
A SEPARATE test asserts the committed file currently parses to BLOCKED-all-false.
<!-- correctless:readiness-current-state:end id="B5-currently-parses-blocked" -->
- **Guards against**: AP-002 (guard defined but its rejecting branch never
  exercised), AP-005 (frozen gate with no legitimate reconverge path / routine
  override), AP-010 (pass on leaked state), AP-021 (non-bootstrappable gate).
- **Test approach**: integration
- **Integration contract**:
  Entry: the readiness-gate test invoked from a clean checkout (clone +
  `rm -rf spikes/dafny-compat/out/`);
<!-- correctless:readiness-current-state:start id="A6-no-entrypoint-yaml" -->
the entrypoint YAML exists at ARCHITECTURE.md:61 (since /carchitect 2026-07-24); the carrier is now homed
<!-- correctless:readiness-current-state:end id="A6-no-entrypoint-yaml" -->
  — see OQ-002 (built-carrier half closed) / `/carchitect`.
  Through: the actual P1/P2/P3 evidence probes (ADR linter + component-table gate,
  Phase-0.0 completion-manifest resolver, CI-determinism execution check) run on
  the real artifacts; none mocked.
  Exit: BLOCKED-with-any-false passes; READY-with-any-false-or-null (or a refuted
  probe, or an unresolvable evidence reference) fails with a message naming the
  offending precondition.

### INV-003: P1 evidence — ADR-0001 promoted or superseded, AND the component-table propagated
- **Type**: must
- **Category**: functional
- **Statement**: P1 is dischargeable only when EITHER ADR-0001 reads
  `Status: accepted` with its `adr_lint` block at
  `boundary_decision: in-process-selected` / `selected_route: A` /
  route-A `verdict: COMPATIBLE` backed by a schema-valid terminal adjudication
  record (the spike's `AdrLinter.Lint` returns zero findings for that positive
  selection) OR a later `accepted` ADR explicitly supersedes ADR-0001's boundary
  decision **AND** the superseding boundary is compatible with this spec's Route-A
  assumptions (INV-034's `DafnyDriver`/`CliCompilation` loaded set, the
  `DafnyLanguageServer` runtime dependency, and the DD-007 component-table shape).
  Because the ADR linter validates ONLY the `adr_lint` YAML block and cannot
  decide the loaded-set/component-table clause (spec-review RS-006), P1 has a
  SECOND, separate evidence probe: a mechanical component-table consistency gate
  that reads the DESIGN.md and ARCHITECTURE.md component tables and the committed
  `spikes/dafny-compat/manifest/expected-loaded/route-a.json` loaded-identity set,
  and asserts `DafnyLanguageServer` is present and `DafnyPipeline` is absent for
  the selected route (matching INV-034). Both probes must pass. A supersession
  that selects Route B, a subprocess/export boundary, or any other incompatible
  boundary does NOT satisfy P1 — it OBLIGATES a revision or supersession of THIS
  spec before it can go READY (see OQ-004). Prose intent (the current "Maintainer
  route selection" note) does NOT satisfy P1.
- **Violated when**: P1 is marked satisfied while the `adr_lint` block is still
  `pending`, backed only by prose, or by an adjudication record that fails the
  linter; OR the ADR is promoted but the component tables still name
  `DafnyPipeline`/omit `DafnyLanguageServer` (AP-004/AP-016 partial migration); or
  P1 is marked satisfied by a superseding ADR whose selected boundary is not
  Route-A-compatible while this spec still asserts Route-A invariants
  (INV-034/INV-035).
- **Enforcement**: (a) hash/record verification — reuse the spike's INV-013 ADR
  linter against the committed evidence, assert zero findings for the positive
  selection (this is exactly DF-002); (b) a component-table consistency gate over
  DESIGN.md/ARCHITECTURE.md + the route-A loaded-identity manifest. P1 fails
  closed until both are green.
- **Guards against**: AP-004, AP-016.
- **Test approach**: integration
- **Risk**: high
- **Cross-ref**: [[dafny-boundary-route-a-selected]], DF-002. The coupled DD-007
  propagation obligation (drop `DafnyPipeline`, add `DafnyLanguageServer` in
  DESIGN.md/ARCHITECTURE.md component tables) is part of discharging P1 and is now
  a *checked* part of the P1 gate, not a prose obligation. OQ-004 must be resolved
  so a superseding ADR is machine-recognizable.

### INV-004: P2 evidence — remaining Phase 0.0 gates have executable evidence; DF-003 is remediated, not just gated
- **Type**: must
- **Category**: functional
- **Statement**: P2 is dischargeable only when a committed **Phase-0.0
  completion manifest** — at a pinned committed path, with a versioned schema
  `{ bullet_id | finding_id → gate_id → gate_kind{test|ci-job} → green_run_id }`
  (spec-review RS-032) — enumerates DESIGN.md §13 bullets 4–12 and every open
  MEDIUM deferred finding (the set is derived from the deferred-findings ledger at
  run time, NOT hard-coded, so a future MEDIUM without a gate fails). Each entry
  maps to a NAMED executable gate green **from a single clean-checkout run** (per
  the operational definition). A prose checkmark, a keyword-presence check, or a
  gate that passes only on leaked/committed prior-run state does NOT count.
  Specifically for **DF-003** (child-exit-20 + all-pass report → COMPATIBLE/
  exit-0): P2 requires DF-003 **remediated** — a gate proving the offending
  exit/report matrix cell now maps to a fail-closed non-COMPATIBLE state — not
  merely "a named gate exists," because DF-003 is a live false-COMPATIBLE the
  spike currently carries and Phase 0.1 inherits via INV-006 composition. The
  DF-003 gate is FORWARD and additive (a new negative test in the exit/report
  totality suite) and does NOT regenerate the spike's ancestry-bound committed
  evidence samples (see OQ-005 [RESOLVED] for the sanctioned affordance).
- **Violated when**: any listed bullet/finding lacks a named gate; a named gate
  is a doc-grep rather than an execution; the manifest is satisfied by
  accumulated/committed `out/` state; the MEDIUM set is a stale hard-coded list;
  or DF-003's cell still resolves COMPATIBLE.
- **Enforcement**: gate precondition — the readiness test resolves the manifest at
  its pinned path, versions it (fail-closed on an unrecognized shape), and asserts
  each named gate exists and is EXERCISED from clean (bound to this run's
  RunContext), including the DF-003 remediation negative test.
- **Guards against**: AP-011 (keyword/routing), AP-013 (phantom integration),
  AP-018 (success without completeness), AP-021 (green-from-clean).
- **Test approach**: integration
- **Risk**: high
- **Cross-ref**: DF-003; OQ-005 [RESOLVED].

### INV-005: P3 evidence — the spike's INV-010 determinism check is actually EXECUTED in CI on a floor-capable, RID-matching runner
- **Type**: must
- **Category**: functional
- **Statement**: P3 is dischargeable only when the cross-run determinism check
  actually EXECUTES on the CI path and emits a machine-readable outcome
  `{ran-passed | ran-failed | skipped-resource-floor}` plus the observed
  `ProcessorCount` into this CI run's receipt; P3 asserts `outcome == ran-passed
  ∧ cores ≥ floor` for the current run. A workflow-YAML presence check ("a
  determinism lane exists") does NOT satisfy P3 (spec-review RS-007) — the
  presence-inspection disjunct is removed. The silent early-return-as-pass in the
  spike's `Inv010DeterminismTests.RunTwice…` (`coreFloor=8`, counted as a passed
  test on 4-vCPU) is exactly what P3 forbids; the reworked test emits a distinct
  counted `SkippableFact` skip, never a silent pass. The P3 (and INV-018)
  determinism runner's platform/RID MUST match EA-001's built RID, or the result
  is a differential-only, non-attesting observation recorded in the residual
  ledger and P3 is NOT discharged (spec-review RS-019).
- **Violated when**: P3 is marked satisfied while the determinism check
  early-returns a silent pass, is discharged by workflow presence-inspection, runs
  below the core floor, or runs on a platform/RID that does not match the built
  RID.
- **Enforcement**: CI test assertion — the reworked test emits a distinct
  skipped/failed/passed outcome + observed cores + runner RID into the run
  receipt; P3's probe asserts `ran-passed ∧ cores≥floor ∧ rid==built_rid`.
- **Guards against**: AP-013 (skipped-in-CI integration read as present), AP-010.
- **Test approach**: integration
- **Risk**: high
- **Cross-ref**: [[dafny-spike-harness-reliability-plan]]; the spike's
  `Inv010DeterminismTests.RunTwice_DeterministicProjectionsIdentical`; EA-001,
  EA-003, EA-011.

### INV-006: Composition with Phase 0.0 outputs — no fork, one adapter, one parser
- **Type**: must-not
- **Category**: functional
- **Statement**: Phase 0.1 production code consumes the Phase 0.0 outputs it
  depends on — the ADR-0001-selected route lock, the TB-004 pin set, the evidence
  schema — rather than re-deriving them, and reaches Dafny only through the single
  production `DafnyAdapter` boundary (PAT-001). No second parser or reconstructed
  Dafny semantics (PROHIBIT-002). The import-boundary scan is a SUPPLEMENTARY
  negative check; the load-bearing enforcement of "no reconstructed semantics" is
  INV-014's positive resolver-provenance fixture (every semantic classification
  traced to a pinned-resolver call), because a scan cannot decide the absence of a
  non-importing, non-regex semantic reconstruction (AP-004 residual — spec-review
  RS-030).
- **Violated when**: production code imports Dafny packages outside the adapter,
  hand-rolls a second parser/semantics, or copies/forks the spike's pinned
  identities instead of referencing the promoted boundary.
- **Enforcement**: CI test assertion — (primary) INV-014 resolver-provenance
  fixture; (supplementary) import-boundary scan (no Dafny package reference
  outside the adapter project) + a no-regex-fallback source scan. No-fork is made
  structural: the production route lock is digest-identical to the promoted route
  lock and no second lock with that content exists outside the adapter path.
- **Guards against**: PROHIBIT-002 restated as a test.
- **Test approach**: unit
- **Risk**: high
- **Residual (AP-004)**: the import/source scan cannot prove absence of a
  non-importing semantic reconstruction; the positive resolver-provenance fixture
  is the load-bearing check. "Agent tool-pinning" is NOT an enforcement mechanism
  for this production-code property and is not cited here.

### Group B — Intake, identity, and lock (DESIGN §6)

### INV-007: Exact-byte intake snapshot with a deterministic manifest, snapshot-before-validate
- **Type**: must
- **Category**: data-integrity
- **Statement**: intake hashes exact source bytes (SHA-256, lowercase hex) plus a
  deterministic manifest — intake-root-relative POSIX paths, entries sorted by
  full path bytes, encoded as a schema-versioned JCS object. To close the intake
  TOCTOU (spec-review RS-024), intake SNAPSHOTS the source into a content-addressed
  store FIRST via an `O_NOFOLLOW` lstat-walk that rejects non-regular/symlink at
  EVERY path component; all subsequent validation, hashing, and parsing operate on
  the immutable snapshot, never re-statting the live tree. The certification
  subject is the exact snapshot bytes Dafny is invoked on — no pre-parse
  normalization (RS-024/§Intake).
- **Violated when**: identity depends on parse order, wall-clock, or a
  non-canonical encoding; two byte-identical intakes produce different digests; or
  validation/hashing re-reads the live tree after the snapshot (a swap window).
- **Enforcement**: hash verification (recompute-and-compare on a fixture pair) + a
  snapshot-order test (a symlink/byte swap after the grammar check cannot change
  the certified bytes).
- **Guards against**: AP-010, AP-003.
- **Test approach**: unit
- **Risk**: high

### INV-008: Path grammar is fail-closed at intake
- **Type**: must-not
- **Category**: security
- **Statement**: intake rejects absolute paths, `.`/`..` components, symlinks,
  non-regular files, invalid UTF-8, and any path outside the Phase 0 grammar
  `[a-z0-9][a-z0-9._-]*` (separators `/`), with a typed reason (INV-041) — never a
  silent normalization. The grammar applies to the on-disk name as returned by the
  OS; a filename whose OS-returned bytes differ from the grammar is rejected
  (RS-018/A).
- **Violated when**: any such path is accepted, normalized, or followed.
- **Enforcement**: gate precondition (intake validator) — a PROPERTY test over
  random path strings asserting `accept ⇔ matches-grammar`, plus real-producer
  filesystem fixtures (an actual symlink/FIFO/non-regular file on disk, not a
  hand-written string) for the typed rejections (AP-014).
- **Guards against**: AP-008 (path/command injection class), AP-003 (substrate
  containment).
- **Test approach**: unit
- **Risk**: high

### INV-009: Source bytes are never rewritten at intake
- **Type**: must-not
- **Category**: data-integrity
- **Statement**: intake does not rewrite line endings, whitespace, or Unicode;
  `dafny format` is style-only and never defines semantic identity. Because a VCS
  checkout can rewrite bytes before intake (`core.autocrlf`, `.gitattributes eol`,
  smudge filters — RS-018/A), byte-fidelity of the working tree is an environment
  assumption (EA-016) and intake hashes the exact snapshot bytes.
- **Violated when**: identity is computed over reformatted bytes.
- **Enforcement**: hash verification (CRLF/whitespace-variant fixture yields a
  distinct digest; identity is computed by exactly one hashing function that sees
  raw snapshot bytes — a single-funnel structural assertion).
- **Test approach**: unit
- **Risk**: medium

### INV-010: Certification requires a present, schema-valid, non-stale, recomputed lock; dev locks are marked non-certifiable
- **Type**: must
- **Category**: config-lifecycle
- **Statement**: certification never runs from implicit defaults; it requires a
  `corrected.lock` that is present, schema-valid, non-stale, and whose every
  digest is recomputed at run start. "Stale" is defined operationally: the lock's
  recorded intake digest ≠ the recomputed intake digest, OR the lock's
  `policy_version`/`grammar_version` is superseded (INV-044). Development may begin
  from an ephemeral generated lock; certification may not — the ephemeral lock
  carries `certifiable: false` and certify fails closed on it with a typed reason
  (spec-review RS-039), and its lifecycle/cleanup is defined so it cannot be
  mistaken for a certification lock.
- **Violated when**: a missing/stale lock is tolerated, a lock digest is trusted
  without recomputation, or an ephemeral `certifiable:false` lock certifies.
- **Enforcement**: gate precondition (preflight lock validator) — fail-closed.
- **Guards against**: AP-001 (fail-open), AP-016 (partial/stale state), AP-017
  (coupled-artifact lifecycle).
- **Test approach**: integration
- **Integration contract**:
  Entry: `corrected certify` on a fixture with (i) no lock, (ii) a stale lock
  (recorded ≠ recomputed digest), (iii) a tampered-digest lock, (iv) a
  `certifiable:false` ephemeral lock; entrypoint YAML exists (ARCHITECTURE.md:61, `/carchitect` 2026-07-24).
  Through: the real preflight validator + digest recomputation; no mock of the
  lock resolver.
  Exit: each case fails closed with a typed reason; only the fresh, recomputed,
  schema-valid, `certifiable`-true lock proceeds.
- **Risk**: high

### INV-011: Canonical encodings obey RFC 8785 JCS and I-JSON
- **Type**: must
- **Category**: data-integrity
- **Statement**: the lock, certification-subject manifest, Corrected predicate,
  and receipt/evidence structures use RFC 8785 JCS and reject duplicate keys and
  non-finite numbers; integers outside `[-9007199254740991, 9007199254740991]`
  are decimal strings per schema; each structure omits its own digest field from
  the bytes it hashes. (Methodology-record-schema-bearing artifacts are
  `MANAGED_PI`/Phase 1 and out of this slice; the JCS rule binds them whenever
  they later exist — RS-041.)
- **Violated when**: a duplicate key is accepted, a self-digest is folded into its
  own hash, or a large integer is encoded as a JSON number.
- **Enforcement**: schema validator + hash verification (round-trip and negative
  fixtures; RFC 8785 published test vectors as producer-real fixtures).
- **Guards against**: AP-009 (all-or-nothing / malformed parse handling).
- **Test approach**: unit
- **Risk**: high

### INV-012: Digest graph is explicit and non-self-referential
- **Type**: must
- **Category**: data-integrity
- **Statement**: `verified_input_closure_digest`,
  `certification_subject_digest`, `certification_run_identity`, and
  `artifact_provenance` are computed per the §6 named-field JCS formulas (never
  raw string concatenation); every digest records its algorithm and
  canonicalization version; no structure hashes its own identity field.
- **Violated when**: a digest is a bare concatenation, omits its algorithm/version,
  or is self-referential.
- **Enforcement**: hash verification against committed golden vectors; every
  digest-bearing schema is enumerated from the schema file and asserted to have a
  self-exclusion golden vector, anchored to the schema digest (the spike's
  `EveryDeclaredReportKind_HasCrossRunEqualityConsumer` pattern), so adding a
  structure without a vector fails.
- **Test approach**: unit
- **Risk**: high

### INV-013: Intake identity vs authority are separated; verifier errors never downgrade
- **Type**: must
- **Category**: security
- **Statement**: intake mints the identity digest itself (no external blessing
  needed); an optional supplied attestation is recorded with status
  ∈ {absent, verified, unverified, invalid, error} and `invalid` is NEVER silently
  downgraded to `absent`. A verifier exception/crash maps to `error` (or `invalid`)
  — never to `absent`/`unverified` — and blocks the "approved by Y" claim
  (spec-review RS-023). "Approved by Y" is available only when the attestation is
  `verified`.
- **Violated when**: identity requires an attestation, an invalid attestation is
  treated as absent, or a verifier crash softens a would-be `invalid`.
- **Enforcement**: CI test assertion (attestation-status state table with a
  negative `invalid` case AND a verifier-exception/crash case).
- **Guards against**: AP-001.
- **Test approach**: unit
- **Risk**: medium

### Group C — Ownership and protected surface (DESIGN §6)

### INV-014: Ghostness comes from the pinned resolver, not keywords (behaviorally enforced)
- **Type**: must
- **Category**: functional
- **Statement**: ownership classification derives ghostness from the pinned Dafny
  resolver's classification of each AST node, never inferred from keywords or text.
- **Violated when**: any classification path branches on source keywords/regex.
- **Enforcement**: CI test assertion — the PRIMARY, behavioral check is a fixture
  where lexical appearance and resolver classification DISAGREE (an executable
  variable named `ghost_x`, a ghost variable named `x`), asserting the classifier
  follows the resolver (spec-review RS-026); the source scan for keyword inference
  is advisory/supplementary only (AP-004). This fixture is also INV-006's
  load-bearing "no reconstructed semantics" evidence.
- **Guards against**: PROHIBIT-002, AP-011.
- **Test approach**: unit
- **Risk**: high

### INV-015: Protected surface permits only the four Phase 0.1 edit classes (complement rejected by construction)
- **Type**: must
- **Category**: security
- **Statement**: the parser-based protected surface allows edits ONLY to loop
  `invariant`, loop/method `decreases`, `assert`, and `calc` inside existing
  method bodies. New declarations, lemmas, ghost helpers, ghost state, signatures,
  contracts, executable statements, and changes to existing function/predicate
  bodies are rejected. The classifier computes each edit's resolved-AST node class
  and asserts `class ∈ {loop invariant, decreases, assert, calc}`; any node whose
  class is OUTSIDE the allowlist rejects by construction — the enforcement is over
  the allowlist complement, not a hand-picked forbidden list (spec-review RS-005/
  §8.3 "enumeration cannot be the mechanism"). Whether `assert … by { … }` is in
  the Phase 0.1 fragment is stated explicitly and, if admitted, its nested proof
  block is inside the honesty scan surface (INV-024).
- **Violated when**: any edit whose resolved node class is outside the four
  resolves as accepted.
- **Enforcement**: gate precondition (protected-surface comparison against the
  lock) driven by generating edits across the full node-kind set the INV-020
  grammar admits (the complement), with a planted-mutation fixture per forbidden
  class as confirmation.
- **Guards against**: AP-002 (guard defined but not wired), AP-010.
- **Test approach**: integration
- **Integration contract**:
  Entry: `apply_proof_patch`-equivalent core path on a fixture per forbidden class
  AND a generated complement corpus.
  Through: real resolver + protected-surface comparator; no mock of the classifier.
  Exit: each forbidden mutation is rejected with a typed reason; each of the four
  allowed classes is accepted.
- **Risk**: high

### INV-016: Ghost-erasure and executable-closure invariant holds on every patch (soundly defined)
- **Type**: must
- **Category**: functional
- **Statement**: for every accepted proof node,
  `editable_proof(n) ⇒ resolver_classifies_ghost(n) ∧ compiler_erases(n) ∧ ¬authority_bearing(n)`,
  and `apply_proof_patch(before, after) ⇒ executable_semantic_closure(before) = executable_semantic_closure(after)`.
  `executable_semantic_closure` is defined operationally (spec-review RS-010) as
  the SHA-256 of the canonicalized compiled/erased output from the pinned Dafny
  compiler; equality is a digest compare, so "compiler erasure" is the operational
  definition. Compiler erasure is necessary but not sufficient — an erased
  assumption-producing construct (including an `assume` nested in an `assert … by`
  or `calc` hint — RS-005) is still rejected by the honesty policy (INV-024). A
  `decreases` edit must preserve the method/loop's total (terminating) proof
  obligation and never weaken it to partial correctness (INV-037).
- **Violated when**: a patch changes the executable semantic closure (digest), or
  an erased-but-assumption-producing construct is admitted.
- **Enforcement**: gate precondition (closure-digest equality post-patch) + honesty
  cross-check; fixtures for each Phase 0.1 `editable_proof` form PLUS a NEGATIVE
  fixture where an edit smuggles an executable statement past classification and
  the digest inequality FAILS the check (so the comparator is exercised on the case
  it must catch).
- **Guards against**: AP-001, AP-010, PROHIBIT-002.
- **Test approach**: integration
- **Risk**: high

### INV-017: Authority-bearing invariants/termination clauses are rejected, not reclassified
- **Type**: must-not
- **Category**: functional
- **Statement**: intake that designates a loop invariant or termination clause as
  specification authority FAILS intake with a typed reason; Phase 0.1 does not
  reclassify it as editable.
- **Violated when**: such an intake is silently reclassified as `editable_proof`.
- **Enforcement**: gate precondition (intake classifier) + negative fixture.
- **Test approach**: unit
- **Risk**: high

### INV-018: Protected-surface fingerprints are deterministic AND collision-resistant across repeated parses
- **Type**: must
- **Category**: data-integrity
- **Statement**: versioned resolved-node fingerprints for the protected surface are
  bit-identical across repeated parses of the same source AND collision-resistant
  (cryptographic over the resolved-node canonical form), so a forbidden edit cannot
  produce the same fingerprint as an allowed node (spec-review RS-025); constructs
  without a stable supported representation FAIL intake rather than producing an
  unstable fingerprint.
- **Violated when**: repeated parses differ, two semantically-distinct nodes share
  a fingerprint, or an unstable construct is admitted.
- **Enforcement**: CI test assertion — repeat-parse-and-diff over the INV-020
  accept CORPUS (not one fixture), with the process hash-seed PERTURBED between the
  two parses to surface iteration-order nondeterminism a same-process repeat can't
  (spec-review RS-034); plus a collision test (distinct nodes → distinct
  fingerprints). This is the production analog of the spike's INV-010 determinism
  check — the reason P3 must be real.
- **Guards against**: AP-010.
- **Test approach**: integration
- **Risk**: high

### Group D — Package and fragment gate (Phase 0.1 grammar)

### INV-019: Single-file package gate is fail-closed (accept predicate is the mechanism)
- **Type**: must-not
- **Category**: security
- **Statement**: intake accepts exactly one regular UTF-8 `.dfy` in the default
  module and rejects symlinks, includes, imports, Dafny project files, generated
  sources, linked libraries, preverified artifacts, abstract/replacement modules,
  and additional source roots — each with a typed reason.
- **Violated when**: any rejected package shape is accepted or partially processed.
- **Enforcement**: gate precondition (intake validator) — assert `accept ⇔
  (exactly-one ∧ regular ∧ utf8 ∧ default-module ∧ .dfy)`, so anything else
  rejects by construction; the per-shape negative fixtures are confirmations of the
  closed accept predicate, not the mechanism (spec-review RS-005).
- **Guards against**: AP-003, AP-008.
- **Test approach**: unit
- **Risk**: high

### INV-020: Program-fragment allowlist admits only the supported grammar (complement rejected over the resolved node-kind enum)
- **Type**: must
- **Category**: functional
- **Statement**: accepts total methods plus fully defined functions/predicates over
  `bool`/`int`/`nat`/immutable `seq`, local scalar/sequence variables, assignments,
  conditionals, and terminating `for`/`while` loops; spec expressions may use
  bounded quantification and finite sets/multisets over those values. Every
  rejected feature (heap references, arrays, objects, classes, traits, user
  datatypes, iterators, recursion, nondeterministic assignment, exceptions,
  externs, bodyless/opaque/replaceable/compilation-only declarations) fails intake
  with a typed reason. The validator computes over the resolved-AST node-kind enum
  from the PINNED resolver and rejects any node kind not in the positive allowlist
  (the complement), because DESIGN §8.3 states an enumerated blocklist "cannot be
  the certification mechanism" (spec-review RS-005). Contract expansion requires a
  new policy version with matching ownership/bypass/conformance fixtures, anchored
  to a policy-version constant so widening without a bump fails.
- **Violated when**: an unsupported construct verifies instead of failing intake,
  or the allowlist is widened without a policy-version bump + fixtures.
- **Enforcement**: gate precondition (fragment validator) with an accept corpus and
  a reject corpus generated as (node-kind-enum − allowlist), plus the policy-version
  anchor.
- **Guards against**: AP-005 (silent contract widening), AP-011.
- **Test approach**: integration
- **Risk**: high

### Group E — Complete verification (DESIGN §8.2)

### INV-021: Verification covers the complete closure with a keyed declaration→verdict map
- **Type**: must
- **Category**: functional
- **Statement**: `dafny verify` runs over the explicit complete program closure —
  for the Phase 0.1 single-file fragment, EVERY declaration in the one source file.
  Included-file verification (the general DESIGN §8.2 policy) is vacuous here
  because includes are rejected at intake (INV-019), and cross-file scope
  completeness defers to the first multi-file policy version. Unapproved
  `--library` exclusions and certification-mode symbol filters are disallowed; the
  exact effective options are recorded and applied consistently. Declaration↔verdict
  cardinality is enforced STRUCTURALLY via a keyed map declaration→verdict (not
  parallel lists), fail-closed on any declaration lacking a verdict, so a
  dropped/silently-filtered declaration is structurally impossible (spec-review
  RS-027 / AP-006). Partial verification is a development-only operation, never
  certification.
- **Violated when**: any declaration in the source file is left unverified, a
  symbol filter is honored in certification, effective options aren't recorded, or
  a declaration has no keyed verdict.
- **Enforcement**: gate precondition (verification-plan resolver + keyed map) + a
  **fragment-valid** scope negative fixture — a certification plan that omits a
  declaration present in the single source file (a within-file used-but-unverified
  symbol), NOT an include (includes fail at intake, INV-019).
- **Guards against**: AP-018 (partial reported as complete), AP-006 (paired
  declaration/verdict cardinality).
- **Test approach**: integration
- **Risk**: high

### INV-022: The locked resource plan (incl. pinned solver seed) is enforced; resource-limit exhaustion is inconclusive
- **Type**: must
- **Category**: resource-lifecycle
- **Statement**: the certification lock fixes `--resource-limit`, one verification
  worker, one solver thread, no solver time limit, AND the pinned solver random
  seed(s) — DESIGN pins random seeds as part of the plan (§13/§8.2/§6) and all
  determinism claims (INV-018/026/028) depend on a fixed seed, so it is a
  first-class, receipt-recorded element of the locked plan (spec-review RS-017). A
  `resource_limit_exhausted` result yields an `INCONCLUSIVE` verification analysis
  that can NEVER satisfy a verification-bearing profile, and the receipt records
  the applicable limit, consumed count, proof-batch identity, solver result, and
  seed. Resource units are treated as deterministic only for the same solver build
  + platform (EA-003).
- **Violated when**: exhaustion is mapped to pass/fail, a profile is satisfied
  under an unenforced/different resource plan, or the seed is unpinned/unrecorded.
- **Enforcement**: gate precondition + CI test assertion (planted-exhaustion
  fixture pinned to the pinned solver → INCONCLUSIVE, profile unsatisfied; seed
  presence asserted in the plan and receipt).
- **Guards against**: AP-001.
- **Test approach**: integration
- **Risk**: high

### INV-023: Operational watchdogs are never normalized into verification facts; watchdog dominates a co-firing resource limit
- **Type**: must-not
- **Category**: resource-lifecycle
- **Statement**: wall-clock, memory, and process watchdogs may abort unhealthy
  infrastructure, but on firing the affected gate is `INFRASTRUCTURE_INVALID`, the
  run cannot be `COMPLETE`, and no partial solver result becomes receipt-grade
  evidence. Certification never translates elapsed time into `verification_failure`
  or `resource_limit_exhausted`. Watchdogs use a MONOTONIC clock, not wall-clock
  (RS-018/B). Precedence is pinned (spec-review RS-028): if ANY watchdog fired
  during a gate, that gate is `INFRASTRUCTURE_INVALID` regardless of any
  co-occurring solver/resource-limit result, and the watchdog abort POISONS that
  gate's evidence so no partial solver output is readable downstream.
- **Violated when**: a watchdog abort is recorded as a verification/honesty/vacuity
  result; a run with a fired watchdog reports `COMPLETE`; or a co-firing resource
  limit is recorded as receipt-grade evidence.
- **Enforcement**: gate precondition + a planted-watchdog-abort fixture AND a
  planted watchdog+resource-limit co-fire fixture (watchdog must win).
- **Guards against**: AP-001, AP-018.
- **Test approach**: integration
- **Risk**: high

### Group F — Honesty and vacuity (DESIGN §8.3, §13)

### INV-024: Two-stage honesty policy; bypass set derived from the §8.3 closed list ∩ the fragment, scanned into nested proof blocks
- **Type**: must
- **Category**: security
- **Statement**: every candidate passes the fast structural honesty subset before a
  verifier invocation; the complete policy (`dafny audit` plus supplemental
  rejection of the fragment-reachable bypass classes) runs before any candidate
  becomes a checkpoint or certification input. Both stages run over the SAME
  digest-bound candidate snapshot (no swap window), and the full policy is a hard
  precondition with NO fallback to the fast-subset result on error (fail-closed —
  spec-review RS-005). The bypass set is DERIVED as (INV-020 allowlist ∩ the full
  DESIGN §8.3 class list) with an exhaustiveness argument over the closed fragment
  — not a hand-picked list — and covers at least `assume` (incl. assign-such-that
  `:|` and failure-update `:-` forms), `expect`, `{:assumption}`, `{:axiom}`,
  `{:verify false}`, `{:only}`, selective-checking/symbol-filter controls,
  bodyless loops/`forall` that contribute facts, unlicensed `{:extern}`,
  `{:compile false}`, contract mutation, module-level verification-semantics
  options, and termination-disabling controls (`decreases *`, disabling termination
  checking — pinned in INV-037). The scan RECURSES into every nested proof block
  (`assert … by { }`, `calc` hints, `forall` bodies), because those are reachable
  positions for an allowed edit class (INV-015) — e.g. `assert Post by { assume
  false; }` (spec-review RS-005). A certifiable candidate has
  `unapproved_logical_assumptions = ∅` and `prohibited_verification_controls = ∅`.
  `dafny audit` EXIT STATUS is never the signal — DESIGN §8.3 notes it exits 0 even
  with findings — the parsed finding SET is; an empty parsed set from an audit error
  is treated as INCOMPLETE (fail-closed), never "clean" (AP-009).
- **Violated when**: any bypass construct (top-level or nested) survives to
  certification; the full policy is skipped/falls-back before checkpoint/
  certification; the class list is a subset of §8.3; or `dafny audit` exit-0 is
  read as pass.
- **Enforcement**: gate precondition (honesty engine on the full resolved
  candidate) exercised through the real certification entry path (not the guard in
  isolation), with: a planted-bypass fixture per derived class INCLUDING a
  nested-in-`assert … by` and nested-in-`calc`-hint variant; a real-producer
  `dafny audit` output fixture captured verbatim at Dafny 4.11.0 where audit
  reports a finding at exit-0 (asserting the engine fails); and a positive test
  that a known-finding input yields a NON-empty parsed finding set. The derived
  class set is anchored to a policy-version constant so adding a bypass class
  without a wired reject fixture fails.
- **Guards against**: AP-002 (guard not wired), AP-004 (enumeration exceeding its
  layer), AP-009, AP-012 (always-succeed mock).
- **Test approach**: integration
- **Risk**: critical

### INV-025: Verification scope completeness (within-file, per entrypoint)
- **Type**: must
- **Category**: functional
- **Statement**: every declaration reachable from a certified entrypoint (INV-039)
  in the single source file is in the verification scope; a used-but-unverified
  declaration is a scope-completeness failure, not a pass. Cross-file/include scope
  completeness defers to the first multi-file policy version (INV-019 rejects
  includes at intake).
- **Violated when**: a within-file declaration reachable from an entrypoint is left
  unverified.
- **Enforcement**: gate precondition (scope-completeness check over the resolved
  single-file closure) + a within-file used-but-unverified-declaration fixture.
- **Guards against**: AP-018.
- **Test approach**: integration
- **Risk**: high

### INV-026: Tri-state vacuity is honest; the Phase 0.1 fragment requires a non-vacuity witness
- **Type**: must
- **Category**: functional
- **Statement**: vacuity is `PROVEN_VACUOUS` only with an actual proof of
  contradiction; otherwise it reports `WITNESSED_NONVACUOUS` or `VACUITY_UNKNOWN` —
  never an unproven vacuity claim. Phase 0.1's target certification profile is
  `verified-nonvacuous` (selected authoritatively by DESIGN §5 `target_profile` and
  the §13 rationale, per DESIGN §11's recommendation to use it "whenever the
  supported fragment can construct witnesses" — spec-review RS-022): because §8.4
  states witness construction is RELIABLE for exactly this fragment
  (`bool`/`int`/`nat`/immutable `seq`, bounded), a Phase 0.1 verification-bearing
  certification requires `WITNESSED_NONVACUOUS` per exported entrypoint, and a
  `VACUITY_UNKNOWN` result never satisfies the target profile. This SELECTS the
  stronger existing profile rather than redefining the generic `verified` profile —
  `verified` keeps its DESIGN §11 semantics (reports but permits `VACUITY_UNKNOWN`)
  and is simply not the Phase 0.1 target, so a receipt's profile name carries the
  same guarantee across phases. Where a witness genuinely cannot be constructed, the
  entrypoint yields `VACUITY_UNKNOWN`, which fails the `verified-nonvacuous`
  requirement and is surfaced with its residual — never a silent pass.
- **Violated when**: `PROVEN_VACUOUS` is emitted without a proof; an unknown is
  reported as a decided verdict; or a `verified-nonvacuous`-target certification
  passes on an entrypoint whose vacuity is `VACUITY_UNKNOWN`.
- **Enforcement**: CI test assertion (fixtures: contradiction → `PROVEN_VACUOUS`
  with proof; a `WITNESSED_NONVACUOUS` witness → satisfies the target; a
  subtle-contradiction fixture that yields `VACUITY_UNKNOWN` and must NOT satisfy the
  `verified-nonvacuous` target profile).
- **Guards against**: AP-004.
- **Test approach**: integration
- **Risk**: high

### Group G — Success predicate, receipt, and schemas (DESIGN §5, §6)

### INV-027: The mechanical success predicate is fixed after the lock is minted; empty-negatives require a positive attestation
- **Type**: must
- **Category**: security
- **Statement**: the profile verdict is the conjunction of the common predicate
  (`lock_integrity ∧ spec_integrity ∧ protected_surface_integrity ∧
  input_closure_complete ∧ honesty_policy_executed ∧
  unapproved_logical_assumptions = ∅ ∧ prohibited_verification_controls = ∅ ∧
  receipt_schema_valid`), the selected profile requirements, and the locked
  execution-mode overlay. Each empty-set conjunct (`unapproved_logical_assumptions
  = ∅`, `prohibited_verification_controls = ∅`) is satisfied ONLY when backed by a
  POSITIVE honesty attestation ("examined N constructs, found 0") AND
  `honesty_policy_executed = true` — an absent/unpopulated set is INCOMPLETE
  (fail-closed), never a satisfied empty-negative (spec-review RS-012). No skill,
  extension, or agent may redefine the predicate, overlay, or profile after the
  certification lock is minted; the predicate DEFINITION digest is bound into the
  lock so a post-lock mutation is detected.
- **Violated when**: any conjunct is skippable; an empty set from a never-run finder
  is read as pass; or the predicate is mutated post-lock.
- **Enforcement**: gate precondition (predicate evaluated as a single fixed
  conjunction) + a test that a dropped conjunct fails closed + a test that an
  unpopulated/absent honesty set yields INCOMPLETE, not pass + a post-lock
  predicate-digest mutation test.
- **Guards against**: AP-001, AP-005, AP-009, AP-010.
- **Test approach**: integration
- **Risk**: critical

### INV-028: Receipt-core reproducibility is an allowlist of normative fields with totally-ordered arrays
- **Type**: must
- **Category**: data-integrity
- **Statement**: `receipt_core_digest = SHA256(JCS(receipt core with
  receipt_core_digest omitted))`; the receipt core is a CLOSED SCHEMA of
  normative-tagged fields (an ALLOWLIST — spec-review RS-011), so any field present
  in the full receipt but not tagged normative is excluded by construction
  (deny-by-default) — non-normative metadata (timestamps, durations, invocation
  IDs, log locations, presentation text, out-of-Statement signature material) can
  never ride in, and a newly-added untagged field defaults to excluded (safe). Every
  array in a canonical structure (per-entrypoint results,
  `residual_obligation_fingerprints[]`, …) has a defined total order (sorted by a
  specified key); unordered-collection enumeration (HashSet/Dictionary/parallel
  LINQ) in core construction is banned, because JCS orders object keys but not array
  elements (RS-011). Two runs over the same identified candidate, lock, predicate
  schema, certification-environment identity, and required evidence identities
  produce an equal `receipt_core_digest`; a same-scope repeat that differs is a hard
  fail, never a retry-until-green flake.
- **Violated when**: a non-normative/untagged field enters the core; a core array is
  built from an unordered enumeration; or the same-scope repeat yields a different
  core digest.
- **Enforcement**: hash verification — a canonicalization test asserting every core
  array has a defined total order and that a new untagged schema field is excluded
  (an allowlist-membership test, not a metadata-perturbation blocklist);
  repeat-run equality within one pinned environment identity as confirmation.
- **Guards against**: AP-010, AP-014.
- **Test approach**: integration
- **Risk**: high

### INV-029: Total schemas — every analysis path has a typed terminal state; the exit/report matrix is exhaustive
- **Type**: must
- **Category**: data-integrity
- **Statement**: analysis-status, environment, resource, search-outcome, evidence,
  disposition, profile-verdict, AND the intake `RejectionReason` (INV-041) are
  total schemas; every gate outcome maps to a declared typed state (including
  `INFRASTRUCTURE_INVALID`, `INCONCLUSIVE`, `NOT_APPLICABLE`), and the exit/report
  matrix is an EXHAUSTIVE total function over the full exit-code × report-state
  cross-product — not a floor/count (the spike analog asserts a floor, and DF-003
  is exactly an uncovered cell a floor misses — spec-review RS-033). Any unmapped
  cell defaults fail-closed (`INFRASTRUCTURE_INVALID`), never pass.
- **Violated when**: any outcome has no typed state, an exit/report-matrix cell is
  unmapped (cf. DF-003), or the matrix is checked by a count rather than
  exhaustively.
- **Enforcement**: CI test assertion — enumerate the full exit × report
  cross-product; assert every cell maps to a declared typed state; a negative test
  per "error-exit + success-looking-report" pair (the DF-003 class).
- **Guards against**: AP-018, AP-009, DF-003 class.
- **Test approach**: unit
- **Risk**: high

### INV-030: A receipt is emittable only from a plan-complete run; the plan is lock-derived, never self-declared
- **Type**: must-not
- **Category**: functional
- **Statement**: the expected completed-phase set is DERIVED from the lock's
  profile/execution-mode (immutable, minted before the run — spec-review RS-013),
  never self-declared by the run; a receipt is emittable only when the recorded
  completed-phase set equals the lock-mandated plan, and a declared plan weaker
  than the lock-mandated plan is fail-closed (the AP-021 "derive from the manifest,
  never from disk/self" principle). Partial runs report partial, loudly — never a
  success receipt.
- **Violated when**: a receipt is produced from a truncated run, or from a
  self-declared plan weaker than the lock's.
- **Enforcement**: gate precondition (lock-derived completed-phase-set equality
  before receipt emission) + a truncated-run fixture + a minimal-self-declared-plan
  fixture (must not yield a receipt).
- **Guards against**: AP-018, AP-021.
- **Test approach**: integration
- **Risk**: high

### Group H — Release provenance, in-toto, SLSA (DESIGN §8.1, §13; TB-003)

### INV-031: Corrected release artifacts are content-addressed and provenance-verified before execution (binary-side gate)
- **Type**: must
- **Category**: security
- **Statement**: Corrected's own release artifacts are content-addressed with an
  authenticated manifest and SLSA Build Provenance; provenance is verified BEFORE
  executing Corrected. Verification is a fail-closed precondition IN THE BINARY
  (spec-review RS-021): `corrected certify` refuses to run unless it observes a
  signed bootstrap attestation from the independent, pinned external verifier
  (checked with a PINNED key/identity, non-recursively — INV-033), not merely a
  reference-CI YAML step that only applies inside that CI. Reference CI additionally
  verifies the release manifest, authentication bundle, artifact digest, and SLSA
  builder/source expectations.
- **Violated when**: Corrected executes without prior independent provenance
  verification (in any environment, including a local run), or verifies its own
  release recursively.
- **Enforcement**: gate precondition in the binary (verify-before-run) + the
  reference-CI lane + a tampered-artifact negative case run against the REAL verifier
  (an unavailable verifier goes to the residual ledger, not a skipped pass).
- **Guards against**: AP-004, AP-003, AP-013.
- **Test approach**: integration
- **Risk**: high

### INV-032: In-toto Statement round-trips through pinned Cosign with a pinned verification identity
- **Type**: must
- **Category**: security
- **Statement**: a versioned Corrected predicate is carried in an in-toto Statement;
  the reference-CI round trip authenticates the already-constructed Statement with
  pinned Cosign — pinning not only the tool version but the VERIFICATION IDENTITY
  (keyless issuer+subject, or the public key + trust root — spec-review RS-021), so
  a forged provenance from a compromised trust root is rejected; a mismatch is
  fail-closed. An unsigned local Statement and the authenticated CI Statement project
  to the SAME receipt core when their normative inputs and environment identities
  match — but receipt-core equality is a REPRODUCIBILITY property, never a standalone
  trust token: any consumer MUST verify the enclosing signed Statement, since
  INV-028 excludes signature material from the core so unsigned and signed project
  equal by construction.
- **Violated when**: local and CI Statements project to different receipt cores for
  matching normative inputs; verification uses unpinned tooling or an unpinned
  identity; or a consumer trusts the core digest without verifying the signed
  Statement.
- **Enforcement**: gate precondition (round-trip test with a pinned identity + an
  identity-mismatch negative) + hash verification (core projection equality) + a
  documented "core-equality ≠ authenticity" consumer rule.
- **Guards against**: AP-014, AP-015 (pin drift).
- **Test approach**: integration
- **Risk**: high

### INV-033: The provenance bootstrap TCB is verified by independently pinned tooling, never recursively
- **Type**: must-not
- **Category**: security
- **Statement**: the external signature/SLSA verifier and trusted builder (TB-003)
  are an explicit bootstrap TCB verified by independently pinned tooling before
  `corrected certify` runs; the Corrected binary never verifies its own release
  provenance recursively.
- **Violated when**: the binary self-verifies its provenance, or the bootstrap
  verifier is unpinned/ambient.
- **Enforcement**: gate precondition + CI configuration assertion (pinned external
  verifier) + a code-path-absence assertion (an import/call-graph scan proving the
  `certify` path never calls any self-provenance-verify function — spec-review
  RS-021), so the negative is not proven by a config presence check alone.
- **Guards against**: AP-004.
- **Test approach**: integration
- **Risk**: high

### Group I — Toolchain pinning (TB-004, inherited from the spike)

### INV-034: The production DafnyAdapter is the sole Dafny boundary on the P1-promoted route lock (static + runtime)
- **Type**: must
- **Category**: security
- **Statement**: all Dafny SDK calls sit behind one production `DafnyAdapter` and
  one exact package lock — the P1-promoted route lock (Route A per ADR-0001,
  **pending DF-002**; this spec conditions the Route-A assertion on P1 discharge
  rather than asserting it as settled fact — spec-review RS-014) as the single
  production lock (PAT-001). Neither source nor binary compatibility is assumed
  across Dafny versions.
- **Violated when**: Dafny packages are imported outside the adapter, a second
  route is loaded in production, or a Dafny assembly is loaded (statically or via
  reflection/`Assembly.LoadFrom`) outside the adapter's AssemblyLoadContext.
- **Enforcement**: CI test assertion — a STATIC import-boundary scan PLUS a RUNTIME
  loaded-assembly assertion (Dafny assemblies load only within the adapter's
  AssemblyLoadContext — spec-review RS-020), the production analog of the spike's
  loaded-identity gate. Static scan alone is insufficient (misses reflection).
- **Guards against**: PROHIBIT-002.
- **Test approach**: unit
- **Risk**: high

### INV-035: Toolchain intake is exact-pinned, digest-verified, fail-closed, and runs under a clean-environment re-exec
- **Type**: must
- **Category**: security
- **Statement**: every toolchain artifact (Dafny/Boogie NuGet graph, Z3 binary,
  .NET SDK) is exact-pinned and digest-verified before use — locked-mode NuGet
  restore under a `<clear/>`-scoped single-source config, SHA-256-pinned solver
  assets installed outside ambient discovery locations, exact SDK pin with
  roll-forward disabled — and evidence binds claims to the identities actually
  loaded/executed (recomputed from the reported concrete path, not a claimed
  field). The PRODUCTION `corrected certify` entry point re-execs under the same
  clean-environment allowlist the spike uses (`env -i` + a minimal allowlist —
  EA-013), because ADR-0001 names `DOTNET_ROOT`/`NUGET_PACKAGES`/`SSL_CERT_*`/`GIT_*`
  as the false-COMPATIBLE vector (spec-review RS-029); any RID-override-analog input
  is fail-closed. In-process Route A points at the pinned Z3 via an explicit
  config-based handoff (not ambient PATH/`Z3_EXE`), and no ambient solver can answer.
  Intake failure is fail-closed with no silent fallback to ambient resolution. A
  behavior-relevant toolchain bump reruns the conformance/fingerprint suites and
  re-locks (DD-006) AND re-captures the INV-020 fragment accept/reject corpus, the
  INV-012 golden vectors, and every verbatim-producer fixture, with a gate that
  fails if a fixture's captured tool version lags the locked one (spec-review
  RS-040 / AP-014/AP-015).
- **Violated when**: a floating version resolves; an ambient source/env/props/solver
  alters resolution or answers; a receipt cites artifacts not actually loaded; the
  production entry point runs without the clean-environment re-exec; or a bump
  re-locks while fixtures/golden vectors stay stale.
- **Enforcement**: gate precondition (locked restore + digest verification +
  clean-env re-exec + explicit pinned-solver handoff) + the negative restore /
  config-isolation / solver-identity tests carried forward from the spike (TB-004) +
  an AP-020 VERBATIM execution test of the documented `corrected certify`
  invocation (same argv[0] form + working directory the docs specify — a doc grep
  is not execution) + a fixture-version-lag gate.
- **Guards against**: AP-015, AP-020, AP-001.
- **Test approach**: integration
- **Risk**: critical
- **Cross-ref**: ARCHITECTURE.md TB-004; `spikes/dafny-compat` Inv001/Inv003.

### Group J — Additions from spec review (round 1–2, 2026-07-24)

### INV-036: While BLOCKED, no production implementation may land — with an actionable message
- **Type**: must-not
- **Category**: security
- **Statement**: while `implementation_readiness.status = BLOCKED`, the feature's
  production implementation surface is absent/empty — the C# core worker and
  `corrected` CLI packages (§Packages Affected) carry NO production implementation
  of these invariants. "Production surface" is an explicit path set (the core/CLI
  `src` globs pinned by `/carchitect`) MINUS an explicit carrier/test allowlist,
  and "non-empty" is a non-gameable predicate (any method body / any type
  implementing a policy interface in the shipped compilation closure — spec-review
  RS-008/RS-036), deny-by-default (any new top-level package is "production" until
  listed as carrier). Excepted while BLOCKED: test scaffolding, project skeletons,
  and the readiness-gate enforcement itself (the INV-001/INV-002/INV-036 checker +
  the INV-044 append-only history registry + meta-test — but NOT INV-044's runtime
  supported-version dispatch table, which ships in core), which MUST live in the
  test/build-gate carrier (OQ-002),
  never in the production core/CLI packages — so the gate can enforce itself
  without tripping its own production-code ban. The failing check emits an
  ACTIONABLE message mirroring INV-002 (spec-review RS-038): it names
  `status: BLOCKED`, lists the unsatisfied preconditions, and points to the
  readiness block and the OQ-002 carrier. Landing production core/CLI code requires
  `status = READY`, which INV-002 makes unreachable without P1/P2/P3 evidence.
- **Violated when**: a production implementation file for this feature exists with
  non-trivial content while `status = BLOCKED`; an implementation PR merges while
  `status ≠ READY`; or the check fires without an actionable message.
- **Enforcement**: gate precondition — a path-scoped CI check on the feature's
  production packages (globs pinned by `/carchitect`, deny-by-default) that fails
  the build/PR when `status = BLOCKED` and the production surface is non-empty,
  with a skeleton-only tree (must pass) AND a one-real-method tree (must fail) as
  fixtures. Complements INV-002: INV-002 stops a premature flip; INV-036 stops code
  landing under an honestly-BLOCKED flag.
- **Guards against**: AP-005 (a freeze with no teeth), AP-004, AP-002 (guard never
  exercised on the case it must catch).
- **Test approach**: integration
- **Risk**: high

### INV-037: `decreases` edits preserve totality; termination bypasses are prohibited (soundly checked)
- **Type**: must-not
- **Category**: security
- **Statement**: the Phase 0.1 fragment is total — methods and loops terminate
  (DESIGN §13). An accepted `decreases` edit (INV-015) must preserve the enclosing
  method/loop's terminating proof obligation; any edit that disables termination
  checking, introduces `decreases *`, or otherwise converts a total obligation into
  partial correctness is a prohibited bypass — rejected by the honesty policy
  (INV-024) and never certified. A source program that already contains such a
  construct fails intake (INV-020). "Not weaker" is operationalized (spec-review
  RS-010): re-verify the post-patch program with termination checking ENABLED and
  assert it discharges the pre-patch termination verification condition (derived
  from the pinned resolver/verifier per PAT-001 — NOT a reconstructed second
  semantics), so the check is a sound comparison, not a syntactic token match.
- **Violated when**: a `decreases *` / termination-disabling edit is accepted, or a
  certified candidate's totality obligation is weaker than the frozen program's
  (even without the literal `decreases *` token).
- **Enforcement**: gate precondition (honesty engine + a totality-preservation check
  comparing the emitted termination VC before/after each proof patch) with a planted
  `decreases *` / termination-weakening SYNTACTIC reject fixture AND a SEMANTIC
  negative fixture (weakens totality without the literal token).
- **Guards against**: AP-001 (the DESIGN §5 "disable termination checking"
  reward-hack), AP-010, PROHIBIT-002.
- **Test approach**: integration
- **Risk**: critical
- **Extends**: INV-015, INV-016, INV-020, INV-024.

### INV-038: INCOMPLETE / SPEC_ESCALATION carry the mandatory minimum failure artifact
- **Type**: must
- **Category**: data-integrity
- **Statement**: a non-success terminal state emits its mandatory evidence, not a
  bare status. `INCOMPLETE` carries a total `SearchOutcomeEvidence`
  (`termination_reason`, `budget_authorized`/`budget_consumed`,
  `residual_obligation_fingerprints[]`, `last_typed_diagnostics[]`,
  `potential_counterexample_descriptors[]`, `strategy_attempts_and_attribution[]`,
  optional `best_candidate`/`best_checkpoint`/`nearest_miss_patch` digests, and a
  `sidecar_manifest_digest`); `SPEC_ESCALATION` carries the concrete frozen-spec
  concern / `specification_escalation_witnesses[]` plus the proposed upstream
  obligation. Every fingerprint/sidecar is content-addressed and replayable.
  **CORE-mode specialization**: with no LLM/search loop, `strategy_attempts` and
  the search-budget fields are minimal or empty and `best_checkpoint` may be
  absent, but `termination_reason`, `residual_obligation_fingerprints`,
  `last_typed_diagnostics`, and `sidecar_manifest_digest` remain mandatory. Two
  distinct CORE causes are kept separate (never conflated): a **resource-limit
  exhaustion** is receipt-grade typed verifier evidence — it yields an INCONCLUSIVE
  verification analysis recorded with the solver result, limit, and consumed count
  (INV-022), and can never *satisfy* a profile though it IS a verification-analysis
  outcome; a **watchdog abort** is `INFRASTRUCTURE_INVALID`, is NOT verifier
  evidence, and never becomes any verification disposition (INV-023; watchdog
  dominates a co-firing limit). The failure artifact above accompanies whichever
  non-success terminal the run reaches, and is rendered human-readable by
  `corrected explain` (INV-040).
- **Violated when**: an `INCOMPLETE`/`SPEC_ESCALATION` receipt omits a mandatory
  field, or emits a non-replayable / non-content-addressed sidecar.
- **Enforcement**: schema validator (total `SearchOutcomeEvidence` schema) + a
  forced-INCOMPLETE and a forced-SPEC_ESCALATION fixture asserting the minimum
  artifact + a sidecar replay round-trip (re-derive the sidecar from its manifest
  digest — spec-review RS on replayability), not a presence check.
- **Guards against**: AP-018 (silent truncation), AP-009 (malformed evidence read
  as empty).
- **Test approach**: integration
- **Risk**: high
- **Cross-ref**: DESIGN.md §11 `SearchOutcomeEvidence`; §7 INCOMPLETE; §6
  `SPEC_ESCALATION` / legitimate update affordance.

### INV-039: Entrypoint set is resolved, identified, mapped per-entrypoint, and deterministically fingerprinted
- **Type**: must
- **Category**: data-integrity
- **Statement**: the lock's `entrypoints` list is non-empty, has no duplicate or
  ambiguous names, and each entry resolves to exactly one default-module symbol
  with a stable versioned fingerprint (the resolved-node identity, not the source
  name); obligations, verification results, and vacuity classification are mapped
  per entrypoint in the receipt so the certified public claim is unambiguous about
  WHAT was certified, not only over which source identity. The `entrypoints` lock
  field traces to the DESIGN §6 lock/digest-graph schema (or is flagged as a
  lock-schema addition — spec-review RS-041).
- **Violated when**: `entrypoints` is empty; a name is duplicated or resolves
  ambiguously (zero or many symbols); an entrypoint fingerprint is unstable across
  parses; or the receipt cannot attribute an obligation/vacuity verdict to a
  specific entrypoint.
- **Enforcement**: gate precondition (entrypoint resolver) with negative fixtures
  (empty list, duplicate name, ambiguous/unresolved symbol) + a per-entrypoint
  receipt-mapping assertion + fingerprint stability over the corpus with hash-seed
  perturbation (same rewrite as INV-018 — spec-review RS-034).
- **Guards against**: AP-006 (paired entrypoint↔verdict cardinality), AP-004.
- **Test approach**: integration
- **Risk**: high

### Group K — Additions from spec review round 3 (2026-07-24)

### INV-040: `corrected explain` renders any receipt or failure artifact to human-actionable text
- **Type**: must
- **Category**: functional
- **Statement**: `corrected explain` renders any receipt, disposition, or INV-038
  failure artifact (INCOMPLETE/SPEC_ESCALATION/INFRASTRUCTURE_INVALID/INCONCLUSIVE)
  to human-actionable text — the verdict, the per-entrypoint result, residual
  obligations, and the `remediation_class` (INV-042) — so the machine artifact
  (SHA-256 + `residual_obligation_fingerprints[]`) is never the only recovery
  surface (spec-review RS-037; DESIGN §14 `corrected explain`). `explain` is a pure
  renderer of already-produced artifacts; it is not an acceptance path and never
  re-derives a verdict.
- **Violated when**: a non-success terminal produces only a machine artifact with no
  `explain` rendering, or `explain` recomputes rather than renders.
- **Enforcement**: CI test assertion — for each terminal-state fixture, `explain`
  emits text naming the verdict, residual obligations, and remediation class; a test
  that `explain` never invokes an acceptance/verifier path.
- **Guards against**: AP-018 (persisted-but-unreadable), AP-004.
- **Test approach**: integration
- **Risk**: medium

### INV-041: Intake/lock rejections carry a total, closed-vocabulary typed reason
- **Type**: must
- **Category**: data-integrity
- **Statement**: every intake/lock rejection (INV-008/010/015/017/019/020, BND-003)
  carries a `RejectionReason = { code, human_message, offending_locus }` where
  `code` is a member of a total, closed vocabulary (added to INV-029's totality
  set), `human_message` is human-readable, and `offending_locus` identifies the
  offending input (a source span for span-bearing rejections like INV-020, a path
  component for path-bearing rejections like INV-008) — a bare enum code with no
  locus is insufficient (spec-review RS-035).
- **Violated when**: a rejection emits a bare code, omits the human message, or
  omits the offending locus where one exists; or the code is outside the closed
  vocabulary.
- **Enforcement**: schema validator (total closed `RejectionReason` vocabulary) + a
  negative fixture per reason asserting the message names the offending input.
- **Guards against**: AP-018, AP-004.
- **Test approach**: unit
- **Risk**: medium

### INV-042: Every non-success terminal carries a remediation class
- **Type**: must
- **Category**: data-integrity
- **Statement**: each non-success terminal state carries `remediation_class ∈
  {fix_infrastructure, raise_resource_limit_and_relock, fix_candidate,
  escalate_spec}` (spec-review RS-036), plus for `INFRASTRUCTURE_INVALID` the
  concrete observed-vs-locked mismatch and for `INCONCLUSIVE` the current limit +
  the re-lock remedy — so the typed taxonomy (disambiguated internally by round-2)
  is also ACTIONABLE externally (harness-bug vs my-proof vs my-infra vs
  spec-problem). Dispositions additionally carry a `benign` marker distinguishing
  "nothing to do" (e.g. NOT_APPLICABLE) from "you must act".
- **Violated when**: a non-success terminal omits `remediation_class`, or a
  disposition omits the benign/actionable marker.
- **Enforcement**: schema validator + a fixture per terminal state asserting the
  correct remediation class and mismatch/limit detail.
- **Guards against**: AP-018.
- **Test approach**: unit
- **Risk**: medium

### INV-043: The BLOCKED readiness state is self-explaining (rendered discharge checklist)
- **Type**: must
- **Category**: functional
- **Statement**: a readiness-status reporter (gate-side or `corrected`-side) renders
  each precondition → its `discharges` → current evidence state as a checklist, so
  an honest BLOCKED state is self-explaining rather than a silently-passing test
  (spec-review RS-038). This complements INV-002 (which only speaks on a false
  READY) and INV-036 (which speaks on a blocked PR).
- **Violated when**: the only feedback for a BLOCKED state is a silently-passing
  test with no rendered why-blocked/what-discharges summary.
- **Enforcement**: CI/CLI test assertion — the reporter renders all three
  preconditions with their discharge ids and satisfied/evidence state for the
  committed BLOCKED-all-false block.
- **Guards against**: AP-018 (self-enforcing but not self-explaining).
- **Test approach**: unit
- **Risk**: low

### INV-044: New versioned artifacts use an append-only, digest-pinned schema registry; certification dispatches on the lock's policy version
- **Type**: must
- **Category**: config-lifecycle
- **Statement**: this invariant has TWO homes and they must not be conflated
  (maintainer finding — a runtime dependency must not live only in the carrier).
  **(1) Runtime supported-version dispatch table — SHIPS WITH CORE**: the production
  core/CLI carries the table of supported `policy_version`/`schema_version` values
  bound to their schemas, so certification DISPATCHES strictly on the lock's pinned
  `policy_version` (an unknown/superseded version fails closed with a typed reason,
  never silently re-interpreted under the worker's own version) and the verification
  path selects the matching schema version to re-verify an existing receipt so old
  receipts stay verifiable. This is runtime acceptance behavior — core cannot
  certify a lock or re-verify a receipt without it — so it MUST ship in core, NOT
  the carrier. **(2) Append-only version→digest HISTORY registry + its meta-test —
  lives in the test/build-gate carrier** (INV-036): the append-only, digest-pinned
  registry (the spike's `schema-version-registry.json` model — spec-review RS-015)
  records every version→digest so a version may never be reused with a different
  digest and rows are never removed or altered; a bump is a reviewable appended row.
  A build-gate meta-test asserts the core's shipped supported-version set is a subset
  of, and digest-consistent with, the registry, so the two homes can never drift.
  Initial `schema_version`/`policy_version`/canonicalization-version values are
  format-pinned constants registered in the history registry.
- **Violated when**: a schema version is reused with a different digest; a registry
  row is mutated/removed; a new artifact lacks a registered version; certification
  applies its own policy to a lock minted under a different `policy_version`; an
  unknown version is silently upgraded rather than failing closed; the runtime
  supported-version dispatch table is absent from the shipped core (so core cannot
  certify/re-verify); or the core's supported set diverges from the history registry.
- **Enforcement**: gate precondition — (carrier) an append-only registry test (a
  version→digest mismatch or a mutated row fails) + (core) a policy-version-dispatch
  test exercised through the real core certify path (unknown/superseded lock version
  → fail-closed typed reason) + a subset/consistency meta-test between the shipped
  core supported-version table and the history registry + an old-receipt
  re-verification test.
- **Guards against**: AP-015, AP-005, AP-016.
- **Test approach**: integration
- **Risk**: high

### INV-045: An interrupted run leaves no partial artifact and re-invocation is idempotent
- **Type**: must
- **Category**: state-lifecycle
- **Statement**: after an interrupted certify (Ctrl-C, watchdog kill INV-023,
  crash) the run leaves NO partial receipt/sidecar behind, and re-invocation is
  idempotent from any interrupted state (clean/partial/full) with no manual cleanup
  (spec-review RS-039 / AP-016/AP-017). A receipt/sidecar is written atomically
  (temp + rename) so a half-written artifact is never observable.
- **Violated when**: an interrupted run leaves a partial receipt/sidecar, or a
  re-run requires manual cleanup or produces a different result.
- **Enforcement**: gate precondition + a kill-mid-run recovery fixture (interrupt at
  each phase; assert no partial artifact and an idempotent clean re-run).
- **Guards against**: AP-016, AP-017.
- **Test approach**: integration
- **Risk**: medium

### INV-046: Certification on a below-floor host fails closed as INFRASTRUCTURE_INVALID
- **Type**: must-not
- **Category**: resource-lifecycle
- **Statement**: `corrected certify` on a host below the certification resource
  floor fails closed as `INFRASTRUCTURE_INVALID` with a typed message naming the
  required floor and the observed capacity — never a silent degradation (spec-review
  RS-039). This is the production counterpart of INV-005's "visible and counted"
  determinism skip: the exact silent-skip failure mode (the spike's `coreFloor`
  early-return-as-pass) must not recur on the production path.
- **Violated when**: certify silently degrades, or produces a success/receipt-grade
  verdict, on a below-floor host.
- **Enforcement**: gate precondition + a below-floor-host fixture → INFRASTRUCTURE_INVALID
  naming the floor and observed capacity.
- **Guards against**: AP-013, AP-001.
- **Test approach**: integration
- **Risk**: medium

### INV-047: Acceptance verdicts are a closed sum type with no default-pass
- **Type**: must-not
- **Category**: security
- **Statement**: every acceptance-relevant verdict is a value of a closed sum type
  with NO default-pass branch; a path that returns a pass without the corresponding
  check having run is structurally impossible (spec-review RS-042). This makes
  PRH-002's "no fail-open" negative enforced by construction/type discipline rather
  than by enumerating today's acceptance functions — so a NEW acceptance path added
  later cannot fail open silently.
- **Violated when**: an acceptance verdict has a default/fallback pass, or a new
  acceptance path can return pass without its check running.
- **Enforcement**: gate precondition — a static analyzer/type-discipline check (or a
  registry + meta-test) flags any acceptance path that yields pass without the check
  running; exhaustiveness over the sum type is compiler-enforced.
- **Guards against**: AP-001.
- **Test approach**: unit
- **Risk**: high

### INV-048: Certification is environment-hermetic and its environment identity is pinned
- **Type**: must
- **Category**: security
- **Statement**: `corrected certify` is air-gappable and network-hermetic — it makes
  no ambient network calls (no NuGet HTTP, no .NET first-run/telemetry, no in-process
  `DafnyLanguageServer` background fetch); telemetry is opt-out-pinned; and the
  certification environment identity (RID/OS, immutable base image when used,
  pinned-invariant globalization, solver build+platform) is recorded per §6 and EA-*.
  Provisioning (a separate, non-certification phase) may reach the pinned upstream
  hosts; certification may not (spec-review RS-018).
- **Violated when**: certify makes an ambient network call, emits telemetry, or its
  environment identity is unrecorded/ambient.
- **Enforcement**: gate precondition — a NEGATIVE test that certify succeeds (or
  fails closed) with the network SEVERED, and that no ambient Z3/telemetry endpoint
  is contacted; environment-identity fields asserted present in the receipt.
- **Guards against**: AP-001, AP-015.
- **Test approach**: integration
- **Risk**: high

## Prohibitions

### PRH-001: No second Dafny parser or reconstructed semantics
- **Statement**: no non-adapter path parses Dafny or reconstructs its semantics
  (no regex/text matching as a fallback); any non-C# consumer uses the semantic
  worker or an upstream resolved-program export passing the identical conformance
  corpus.
- **Detection**: INV-014 resolver-provenance fixture (primary) + import-boundary
  scan + a "no regex fallback" source scan (supplementary — INV-006, INV-034).
- **Consequence**: a divergent second semantics silently accepts what Dafny would
  reject — the core failure the whole design forbids (PROHIBIT-002).

### PRH-002: No fail-open on any acceptance or enforcement path (structurally enforced)
- **Statement**: no acceptance-relevant function falls back to unenforced input; on
  any internal failure it returns reject/error with diagnostics. A "pass" verdict
  is producible only by the check actually running. Enforced by construction via
  the closed acceptance sum-type with no default-pass (INV-047), not by enumerating
  today's acceptance functions (spec-review RS-042).
- **Detection**: INV-047 analyzer/type-discipline + error-path tests per acceptance
  function (AP-001); no `|| pass` fallbacks.
- **Consequence**: unenforced input accepted as certified.

### PRH-003: No acceptance evidence from search-side or cached state
- **Statement**: search caches, LSP/persistent-verifier state, and prior-run
  artifacts are never acceptance evidence; only the fresh certification path's
  results count (PAT-002/TB-002).
- **Detection**: certification reads only current-run, lock-approved inputs; a test
  that a warm cache cannot change the certification verdict. (Degenerate in this
  LLM-free slice — no search side populates a cache — so until a search side exists
  this is a residual-ledger note plus a synthesized-cache-ignored test, not a
  vacuously-passing check — spec-review testability note.)
- **Consequence**: a brittle/cached pass is certified (AP-010).

### PRH-004: No elapsed-time-to-verification normalization
- **Statement**: a wall-clock/memory/process watchdog abort is never a verification,
  honesty, or vacuity fact (INV-023).
- **Detection**: planted-watchdog-abort fixture → `INFRASTRUCTURE_INVALID`, not
  `COMPLETE`.
- **Consequence**: infrastructure flakiness masquerades as a proof result.

### PRH-005: Implementation-readiness is never asserted by prose or a bare flag
- **Statement**: the feature is never treated as implementation-ready unless the
  readiness gate (INV-002) re-derives executable evidence for P1, P2, and P3 — with
  the reject branch itself exercised by the INV-002 fixture table (spec-review
  RS-002).
- **Detection**: the readiness-gate test (fails on READY-without-evidence, via
  supplied-block fixtures).
- **Consequence**: production build starts on an unpromoted boundary, unproven
  Phase 0.0 gates, or a determinism check that silently never runs (AP-004/AP-005).

### PRH-006: No partial-run success receipt
- **Statement**: a receipt is never emitted from a run whose completed-phase set is
  a strict subset of its LOCK-DERIVED declared plan (INV-030).
- **Detection**: truncated-run fixture + minimal-self-declared-plan fixture must not
  yield a receipt.
- **Consequence**: a silently-truncated run reads as certified (AP-018).

### PRH-007: No termination or totality bypass
- **Statement**: no accepted edit disables termination checking, introduces
  `decreases *`, or weakens a total obligation to partial correctness (INV-037),
  checked soundly by re-verifying with termination enabled — not by a token match.
- **Detection**: planted `decreases *` / termination-weakening syntactic fixture AND
  a semantic weakening fixture → rejected.
- **Consequence**: a nonterminating "proof" certifies as total (the DESIGN §5
  "disable termination checking" reward-hack).

### PRH-008: No implementation lands while readiness is BLOCKED
- **Statement**: production implementation of these invariants never lands while
  `implementation_readiness.status = BLOCKED` (INV-036); PRH-005 forbids a false
  READY flag, PRH-008 forbids building under an honest BLOCKED flag.
- **Detection**: path-scoped CI check on the feature's production packages
  (deny-by-default globs pinned by `/carchitect`), with an actionable message.
- **Consequence**: production code built on an unpromoted boundary / unproven
  Phase 0.0 gates — exactly what the readiness gate exists to prevent.

## Boundary Conditions

### BND-001: TB-004 — inbound toolchain supply chain
- **Boundary**: external package/asset sources → build and dev-host execution.
- **Input from**: nuget.org (Dafny/Boogie graph), the Z3 release asset, the .NET SDK.
- **Validation required**: exact-pin + digest verification before use; locked-mode
  restore; solver outside ambient discovery; identities bound to what actually loaded;
  clean-environment re-exec (INV-035).
- **Failure mode**: fail-closed (no verdict; never ambient fallback).

### BND-002: TB-003 — release provenance / bootstrap TCB
- **Boundary**: published-artifact provenance → execution.
- **Input from**: SLSA provenance, signature bundle, authenticated release manifest.
- **Validation required**: independently pinned external verifier + trusted builder +
  pinned verification IDENTITY checked BEFORE `corrected certify` (binary-side gate,
  INV-031); never recursive self-verification.
- **Failure mode**: fail-closed.

### BND-003: Intake — untrusted source bytes (flagged for ARCHITECTURE.md as TB-005)
- **Boundary**: arbitrary handed `.dfy` source → policy TCB.
- **Input from**: any upstream (source-agnostic intake).
- **Validation required**: snapshot-first (O_NOFOLLOW) + path grammar + UTF-8 +
  single-file/fragment gate + exact-byte snapshot before any processing (INV-007/008/
  019/020); typed `RejectionReason` (INV-041).
- **Failure mode**: fail-closed with a typed reason.
- **Note**: this boundary has no ARCHITECTURE.md entry; flagged for `/cupdate-arch`
  as **TB-005** (spec-review RS-016).

### BND-004: TB-002 — search vs certification
- **Boundary**: search-side state → certification.
- **Input from**: (degenerate in this LLM-free slice) any future cache/LSP/proposal.
- **Validation required**: certification consumes only fresh, lock-approved inputs;
  search state is never acceptance evidence.
- **Failure mode**: fail-closed.

## STRIDE Analysis

### STRIDE for TB-004: inbound toolchain supply chain
- **Spoofing**: a look-alike package/asset source answers a restore → defeated by
  `<clear/>`-scoped single-source locked-mode restore + SHA-256 pins.
- **Tampering**: a mutated Z3 binary or Dafny assembly → digest verification against
  the pin at load/execute; evidence binds to loaded identity.
- **Repudiation**: a verdict cites artifacts not actually loaded → identity binding
  records the executed digests (INV-035).
- **Info disclosure**: n/a (no secrets in the toolchain path).
- **DoS**: a hostile/huge asset stalls provisioning → operational watchdog →
  `INFRASTRUCTURE_INVALID`, never a verdict (INV-023); cf. the spike's QA-017
  sparse-file hardening.
- **Elevation**: an ambient env/props/source steers resolution to attacker code →
  `env -i` clean environment + config isolation + explicit pinned-solver handoff;
  fail-closed on any ambient influence (INV-035).

### STRIDE for TB-003: release provenance / bootstrap TCB
- **Spoofing**: a forged signature/SLSA statement → pinned Cosign + pinned
  verification identity + independent verifier; verify-before-run in the binary
  (INV-031/032/033).
- **Tampering**: a swapped release artifact after signing → content-addressed digest
  check in the reference-CI lane and the binary-side gate.
- **Repudiation**: ambiguous "which binary ran" → in-toto Statement binds the exact
  artifact + verified closure.
- **Info disclosure**: n/a.
- **DoS**: verifier unavailable → fail-closed (no certification), never skip.
- **Elevation**: recursive self-verification lets a compromised binary bless itself →
  explicitly forbidden (INV-033), enforced by a call-graph-absence assertion.

### STRIDE for TB-002: search vs certification (degenerate in this slice)
- **Tampering/Elevation**: search-side state influencing acceptance → certification
  reads only fresh lock-approved inputs (PRH-003). Included now so the boundary is
  enforced before the Phase 1 search side exists.

## Environment Assumptions

- **EA-001**: a self-contained .NET 10 Corrected host is built for ONE initial RID;
  other RIDs fail closed at provisioning. — refs EA-002 of the spike. — Consequence
  if wrong: false portability claims.
- **EA-002**: Dafny 4.11.0 `net8.0` packages run in-process on a .NET 10 host for the
  surfaces the spike actually exercised (parse/resolve/Z3-verify/AST-recovery/
  fingerprint-determinism/resource-limit). Production 0.1 NEWLY exercises in-process
  `dafny audit` parsing (INV-024), tri-state vacuity/contradiction analysis (INV-026),
  and per-entrypoint mapping (INV-039); these surfaces are NOT covered by the spike's
  COMPATIBLE verdict and are validated afresh by Phase 0.1 with real-producer
  fixtures (AP-014) — re-validated on any bump per DD-006 (spec-review RS-018/I). —
  Consequence if wrong: the boundary is invalid for the newly-exercised surface.
- **EA-003**: solver resource units AND fingerprints/unsat-cores/canonical projections
  are deterministic only for the same solver build + platform AND a fixed solver seed
  (INV-022); cross-platform agreement is a differential result, not receipt
  equivalence. Determinism additionally requires ordered/canonical collections in the
  classifier/emitter (no reliance on hash-code iteration order — spec-review RS-018/D).
  — Consequence if wrong: spurious resource/receipt verdicts.
- **EA-004**: the reference-CI lane (pinned Cosign + an independent SLSA/signature
  verifier) is a TO-BE-BUILT dependency tied to P3/OQ-002, not yet extant; the pinned
  bootstrap-verifier binaries must be published for the CI RID; offline/first-run
  telemetry behavior is unverified (cf. the spike's EA-003/EA-008 opt-out). —
  Consequence if wrong: provenance gate can't run.
- **EA-005**: the initial RID's OS family provides POSIX filesystem semantics —
  `lstat`-detectable symlinks, a distinguishable non-regular file, and case-sensitive
  path bytes — on which INV-008/019 depend; a case-insensitive or non-POSIX host
  (macOS APFS default, Windows junctions/`MAX_PATH`) is rejected at intake. —
  Consequence if wrong: intake fail-open or mis-reject.
- **EA-006**: certification/provenance requires a correct host wall-clock within
  signing-cert validity tolerance (Cosign cert `NotBefore/NotAfter`, Rekor SET) and
  for TLS; watchdogs use a MONOTONIC clock. — Consequence if wrong: signature/cert
  validity mis-decided, or hang protection defeated by a clock jump.
- **EA-007**: cold provisioning (a separate phase from certification) requires network
  reachability + DNS + TLS to nuget.org, the Z3 release-asset host, and the .NET SDK
  feed; an air-gapped/offline run requires a vendored cache. — Consequence if wrong:
  cannot provision.
- **EA-008**: the pinned NuGet versions and the pinned Z3 release asset remain
  available; to avoid an AP-005 supply-chain freeze-with-no-affordance, a mirrored/
  vendored copy of every pinned asset is held under Corrected's control. — Consequence
  if wrong: provisioning permanently fail-closed if an upstream artifact is deleted.
- **EA-009**: the test/build-gate carrier isolates concurrent workers — N≥2 concurrent
  `corrected certify`/readiness-probe invocations run in per-worker isolated
  workspaces/run-contexts (never a shared `out/`), or the certification/determinism
  tests are pinned to a non-parallel collection (AP-019); INV-022's one-worker/
  one-thread certification constraint must not be violated by a parallel xUnit
  carrier. — Consequence if wrong: determinism measured under contention (false
  nondeterminism), shared-state corruption.
- **EA-010**: the pinned Z3 native asset is published for the initial RID and links
  against a compatible OS libc/libstdc++ (glibc floor on Linux); a self-contained
  .NET host does NOT bundle Z3's native C runtime. — Consequence if wrong: in-process
  verify cannot load the solver.
- **EA-011**: P3 option (a) requires a ≥floor-core CI runner (standard GitHub-hosted
  runners are 2–4 vCPU); absent a larger/self-hosted runner or a dedicated determinism
  lane, only a visible-counted skip is reachable and INV-018 never executes in CI. —
  Consequence if wrong: the determinism check silently never runs (AP-013).
- **EA-012**: certification runs under a pinned immutable OS/container image (or the
  base OS is documented as an unpinned ambient dependency in the residual ledger), so
  the host's unbundled native deps (kernel, glibc, libstdc++) don't drift between the
  two INV-028 repeat runs. — Consequence if wrong: spurious receipt-core divergence.
- **EA-013**: `corrected certify` runs under an explicit MINIMAL environment allowlist
  (e.g. `HOME`, a writable `TMPDIR`, `DOTNET_ROOT`, `DOTNET_CLI_TELEMETRY_OPTOUT`,
  invariant-culture flags) atop `env -i` — enough for NuGet/Z3/Dafny, nothing ambient
  (INV-035). — Consequence if wrong: stripped `TMPDIR`/`HOME` breaks tool temp writes,
  or a leaked `DOTNET_*` steers resolution.
- **EA-014**: globalization is pinned invariant (`DOTNET_SYSTEM_GLOBALIZATION_INVARIANT`
  / `LC_ALL=C`) and recorded in the environment identity, so culture-sensitive
  number/string/diagnostic handling (INV-011/026/028, AP-014) does not drift by host
  locale. — Consequence if wrong: canonical-encoding/diagnostic drift.
- **EA-015**: a writable, adequately-sized scratch dir (inodes/space) is available for
  Dafny/Z3 temp files, and its path is non-identity-bearing (never enters INV-007/028
  digests); space exhaustion → watchdog → INFRASTRUCTURE_INVALID (cf. the spike's
  QA-017 DoS hardening). — Consequence if wrong: temp-exhaustion flakiness, or a temp
  path leaking into the core.
- **EA-016**: the working tree is checked out with byte-fidelity (`core.autocrlf=false`,
  no `eol`/smudge filters), and the git history reaching the pinned evidence commit
  is present (full-history clone, not a shallow `depth-1` checkout or a `.git`-stripped
  tarball) so the P1/P2 evidence binding and INV-007/009 hold. — Consequence if wrong:
  CRLF-normalized identity, or an evidence probe that cannot reach its bound commit.

## Packages Affected (monorepo)

- **C# core worker** (new package): owns intake, lock, the production
  `DafnyAdapter`, ownership/protected-surface, verification, honesty, vacuity,
  receipt/predicate. All policy logic lives here (PROHIBIT-001).
- **`corrected` CLI** (new, part of the core distribution): `corrected init`,
  `corrected check`, `corrected certify`, and `corrected explain` (the human
  recovery renderer — INV-040/RS-037) — runnable with no model/Node/TS/Pi.
  `corrected check` surfaces the same typed reasons a `certify` would hit (a faithful
  dry-run preview — spec-review UX).
- **Test/build-gate carrier** (new, NOT shipped): homes the readiness-gate checker
  (INV-001/002/036), the append-only schema-version HISTORY registry + meta-test
  (INV-044 — the runtime supported-version dispatch table ships in core, not here),
  and the from-clean gates — kept out of the shipped core/CLI so the gate can enforce itself
  (INV-036). Homed in the ARCHITECTURE.md component table (`/cupdate-arch`, readiness-build-gate).
- **TypeScript Pi adapter**: NOT affected by this slice (deferred with the protocol
  seam / MANAGED_PI).

## Open Questions

- **OQ-001 [RESOLVED 2026-07-24]**: Decomposition — **decided: umbrella + 6
  sub-specs**. This spec stays the parent implementation contract + readiness gate;
  once unblocked, implementation lands as 6 sibling sub-specs each TDD'd against a
  slice of these invariants: (a) intake+lock+identity [INV-007..013], (b)
  ownership+protected-surface [INV-014..018], (c) fragment gate +
  verification+resource-plan+watchdog [INV-019..023], (d) honesty+vacuity
  [INV-024..026], (e) success-predicate+receipt+schemas [INV-027..030, INV-040..047], (f)
  release+in-toto+SLSA [INV-031..033]; toolchain/adapter [INV-006, INV-034..035] and
  the schema registry [INV-044] are shared prerequisites of (a)–(f). Each sub-spec
  carries an input-version stamp (ADR-0001 status+selected_route, DESIGN.md version,
  evidence_schema_version, TB-004 pin-set digest) and a gate that fails a sub-spec
  whose stamped inputs don't match the current frozen state (spec-review RS-015).
  Rationale: one change of this size is un-reviewable and re-invites AP-018.
- **OQ-002 [APPROVAL-GATING — contract half DISCHARGED 2026-07-24; built-carrier
  half OPEN]**: Where do the production test harness and the readiness-gate test
  live? **Contract half (done):** `/carchitect` (2026-07-24) defined the
  `## Entrypoints` block in ARCHITECTURE.md — 5 entrypoints (`corrected-cli`,
  `corrected-core`, `dafny-adapter`, `readiness-build-gate`,
  `reference-ci-provenance`), the test/build-gate carrier component (`gate/` +
  `test/`), the planned `src/Corrected.*` layout, and the INV-036 deny-by-default
  production-surface partition — so every `[integration]` contract now has a
  concrete Entry/Through/Exit and INV-036 has a deterministic path partition.
  **Built-carrier half (still open, gates approval):** the `gate/`+`test/` projects
  and the reference-CI lane do not yet EXIST as code; ~22 invariants say "CI test
  assertion" / "gate precondition" with no built test package. Until the carrier is
  built, the readiness gate itself (INV-002/INV-036) is specified-but-unhomed. Land
  the built carrier + INV-002's positive reject-fixture table before any real
  precondition discharges (RS-002 ordering). Note the built carrier is test
  scaffolding (INV-036-exempt), so it MAY be built while `status = BLOCKED`.
- **OQ-003**: Is any runtime-evidence / `SEAM_TEST` obligation in scope for this
  slice? The Phase 0.1 fragment forbids externs and has minimal executable-contract
  seams, so runtime evidence may be vacuous here and belongs to an artifact-bearing
  profile spec. — Matters for the receipt profile set.
- **OQ-004**: For P1, does "superseded" require a specific ADR-supersession format,
  or is any later `accepted` ADR that re-decides the boundary sufficient? Must be
  resolved so INV-003 can machine-recognize the discharge. — Matters for how INV-003
  recognizes the discharge.
- **OQ-005 [RESOLVED 2026-07-24 — spec-review RS-009]**: The spike's committed
  evidence is frozen at commit `d28ed5d` and cannot reconverge after an
  ancestry-breaking rewrite ([[dafny-spike-evidence-binding-fragile]]).
  **Resolution (mechanism committed now):** P2's DF-003 evidence is a FORWARD,
  additive gate — a new negative test in the exit/report totality suite (INV-029)
  asserting the child-exit-20 + all-pass cell maps to a fail-closed non-COMPATIBLE
  state — that covers the matrix cell WITHOUT regenerating the ancestry-bound samples
  (which remain valid at `d28ed5d`). If any spike-evidence change is ever required,
  the SANCTIONED affordance is an APPEND-ONLY re-anchoring: a new evidence sample is
  added at a fresh registry row (never rewriting an existing sample or ancestor);
  QA-001's ancestor check binds to the new sample's own commit; QA-024's
  success-status form is satisfied by the new sample carrying `suite_exit==0` from its
  own clean run — never by mutating `d28ed5d`-bound history. INV-004 accordingly
  requires DF-003 REMEDIATED (the false-COMPATIBLE cell fail-closed), not merely "a
  named gate exists." This keeps P2 forward-dischargeable and closes the AP-005
  freeze-with-no-affordance risk.
- **OQ-006 [OPEN — spec-review RS-018/RS-021]**: Is the reference-CI Cosign
  verification KEYLESS (OIDC token + Fulcio/Rekor + correct clock + network) or
  KEY-BACKED (a pinned public key + trust root)? The environmental dependency chain
  (EA-004/EA-006/EA-007) and the pinned-identity requirement (INV-032) differ
  substantially. — Matters for the provenance group's environment assumptions.

## Notes for review (not invariants)

- **Spec-review round 1 (2026-07-24)** — 7 findings applied: (1) INV-036 + PRH-008
  BLOCKED-blocks-implementation; (2) INV-003 P1 supersession tightened to
  Route-A-compatibility; (3) INV-037 + PRH-007 termination/totality bypass pin
  (`decreases *`); (4) INV-038 INCOMPLETE/SPEC_ESCALATION minimum failure artifact
  (+ CORE-mode clause); (5) INV-021/INV-025 scope fixtures made fragment-valid
  (within-file, not include — resolving the conflict with INV-019); (6) INV-039
  entrypoint identity/resolution; (7) OQ-002 elevated to approval-gating
  (enforcement carrier must exist).
- **Spec-review round 2 (2026-07-24)** — 2 refinements: (1) INV-038 CORE clause
  split so resource-limit exhaustion (receipt-grade INCONCLUSIVE verifier evidence,
  INV-022) is no longer conflated with a watchdog abort (INFRASTRUCTURE_INVALID,
  non-evidence, INV-023); (2) INV-036 exemption + Complexity Budget clarified so the
  readiness-gate checker lives in the test/build-gate carrier — resolving the
  self-referential bootstrap where INV-036 forbade its own enforcer.
- **Spec-review round 3 (2026-07-24, `/creview-spec` multi-agent) — all 42 findings
  applied (RS-001..RS-042).** Highlights: canonical single readiness-block schema +
  `schema_version` (RS-001); INV-002 made a pure function over a SUPPLIED block with a
  reject-fixture table + absent-evidence-degrades-not-throws (RS-002/003); "clean
  checkout" defined as clone + `rm -rf out` because committed `out/` ships in every
  clone (RS-004); INV-024 honesty bypass set DERIVED as (allowlist ∩ §8.3), scanned
  into nested proof blocks, `dafny audit` exit-0 fail-open closed (RS-005); INV-003
  P1 component-table consistency gate (RS-006); INV-005 P3 presence-grep disjunct
  removed → execution artifact + RID binding (RS-007/019); OQ-005 RESOLVED with a
  forward DF-003 gate + append-only re-anchoring (RS-009); INV-016/037 sound
  comparators (RS-010); INV-028 receipt-core allowlist + ordered arrays (RS-011);
  INV-027 empty-negatives require a positive attestation (RS-012); INV-030 lock-derived
  plan (RS-013); INV-034 conditioned on P1 + runtime assembly assertion (RS-014/020);
  INV-022 pinned solver seed (RS-017); INV-026 verified-nonvacuous for the fragment
  (RS-022); INV-013 verifier-error row (RS-023); INV-007/008 snapshot-first TOCTOU
  close (RS-024); INV-018 collision-resistant + corpus/hash-seed (RS-025/034); INV-021
  keyed declaration→verdict map (RS-027); INV-023 watchdog precedence (RS-028); INV-035
  production clean-env re-exec + AP-020 verbatim test + fixture re-capture (RS-029/040);
  INV-006 scan demoted to supplementary, INV-014 primary (RS-030); INV-029 exhaustive
  matrix (RS-033); INV-031/032/033 binary-side gate + pinned trust anchor (RS-021).
  New Group K: INV-040 `corrected explain` (RS-037), INV-041 RejectionReason
  vocabulary (RS-035), INV-042 remediation_class (RS-036), INV-043 BLOCKED
  self-explaining (RS-038), INV-044 schema-version registry + policy dispatch (RS-015),
  INV-045 interrupted-run idempotency (RS-039), INV-046 below-floor host (RS-039),
  INV-047 closed acceptance sum type / no default-pass (RS-042), INV-048 certification
  hermeticity (RS-018). EAs EA-005..EA-016 added; EA-002/003/004 amended. OQ-005
  resolved, OQ-006 opened. Invariants now INV-001..048; prohibitions PRH-001..008
  (PRH-002 restated structurally via INV-047).
- **Architecture-registration flags (for `/cupdate-arch` — spec-review RS-016)**:
  register **TB-005** (intake / untrusted source bytes — BND-003); register **PAT-005**
  (readiness-gate-block-checked-by-a-test) with the canonical block schema, so
  downstream specs inherit one shape; add `Exercised at`/`Test` fields to **TB-003**
  (the new reference-CI lane + pinned Cosign path); add the **test/build-gate carrier**
  to the component table. (DF-002 separately owns the ARCHITECTURE.md line-20 /
  Known-Limitations DD-007 propagation — drop `DafnyPipeline`, add
  `DafnyLanguageServer`.)
- **New-pattern flag (Step 3a)**: the readiness-gate-as-machine-readable-block-
  checked-by-a-test is a convention not yet covered by PAT-001..004 (it mirrors
  ADR-0001's `adr_lint` linter). Registering it as PAT-005 NOW (not "if it recurs")
  is recommended so its schema is pinned architecture-wide and a future spec cannot
  define an incompatible block. No contradiction with existing PATs.
- **Antipattern promotion (Step 5b)**: AP-020 (1 feature) and AP-021 (2 findings / 1
  feature) remain below the 3-feature promotion threshold — no ARCHITECTURE.md
  promotion this run.
- **Allowed-tools cross-check (Step 5a)**: n/a — this spec instructs production code,
  not a Correctless skill; no skill `allowed-tools` frontmatter to amend.
- **Post-review reconciliation (2026-07-24, maintainer)**: two spec-vs-authority
  fixes after round 3. (1) **INV-044 split** — the RUNTIME supported-version dispatch
  table ships WITH core (certify/re-verify need it at runtime), while only the
  append-only version→digest HISTORY registry + its meta-test live in the
  test/build-gate carrier; a meta-test asserts the core table is a subset of and
  digest-consistent with the registry (fixes the contradiction of placing a runtime
  dependency outside shipped core). (2) **Verified-profile reconciliation** — Phase
  0.1's target profile is `verified-nonvacuous`, selected authoritatively in DESIGN
  §5 `target_profile` (changed from `verified`) + a §13 rationale; INV-026 no longer
  redefines the generic `verified` profile, it selects the stronger existing one, so
  a profile name means the same thing across phases. DESIGN.md was updated (the
  authoritative source) so this is a design commitment, not a spec-only deviation
  (Scope + INV-021 profile list updated to match).
- **External cross-model review**: SKIPPED this run — the configured codex `bin`
  resolves to an npm/nvm `node_modules/.../codex.js` launcher that the producer's
  RS-006 validator rejects (Correctless issue joshft/correctless#199). No repo context
  was egressed; the review is Claude-only (self-assessment + 6 adversarial agents).
