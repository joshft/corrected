# Spec: Readiness-Gate Carrier (phase-0.1-worker enforcement home)

## Metadata
- **Created**: 2026-07-25T03:36:48Z
- **Status**: reviewed
- **Impacts**: `phase-0-1-worker` (this spec BUILDS the enforcement home for its
  INV-001/INV-002/INV-036/PRH-008, and — via the **stage-partitioned normative migration manifest** of
  DD-003 — flips `P1.satisfied` to `true` in its `implementation_readiness` block AND updates every parent
  current-state site. **The DD-003 manifest is the canonical parent-anchor site list**; this line and the
  Packages-Affected bullet reference it and do NOT re-enumerate — a meta-test asserts each holds ONLY the
  manifest reference (no local site list, EXT9-02/R3-I4/EXT4-05), not that "three lists agree". The separate
  ADR obligation is described in DD-003 and the ADR Packages-Affected bullet, not here.)
- **Branch**: feature/readiness-gate-carrier
- **Research**: `.correctless/artifacts/research/readiness-gate-carrier-research.md`
- **Review**: codex GPT-5.6-sol (xhigh) + `/creview-spec` 6-agent + self-assessment, **nine rounds**
  (round 1: 12 codex + ~70 Claude; round 2 on v3: 13 codex + ~40 Claude; round 3 FOCUSED on the v4
  P1/DD-003/scanner core: codex 6 BLOCKING + P1-red-team/DD-003/scanner agents; round 4 codex gate on
  v5: 6 BLOCKING + 2 IMPORTANT confirmed, EXT4-01..10; round 5 codex gate on v6: 2 BLOCKING +
  2 IMPORTANT confirmed, EXT5-01..04; round 6 maintainer-relayed impl-contract review on v7: 5 bounded issues
  EXT6-01..05; round 7 codex gate on v8: 4 BLOCKING + 2 IMPORTANT, EXT7-01..06; round 8 codex verification
  gate on v9: 4 BLOCKING + 2 IMPORTANT + 1 MINOR, EXT8-01..07; round 9 codex verification gate on v10:
  3 BLOCKING + 4 IMPORTANT + 1 MINOR confirmed, EXT9-01..08) — all in
  `.correctless/artifacts/review-spec-findings-readiness-gate-carrier.md`. **This is now v11.** Its v5 base incorporated the
  round-3 findings (Stage-A DTO required-vs-optional split so the pre-migration ADR parses to
  `evidence-schema-incomplete`; compiled `canonical_sample_sha256`/manifest-file anchors closing the residual
  coherent-tamper forge; duplicate-JSON rejection; the ADR **registry set-equality** replacing v4's
  "ignore out-of-allowlist" bypass; the supersession graph; the default-deny scanner predicate + real-build
  two-phase generator model + toolchain de-vestigialization; the DD-003 Stage-A/B re-partition + ~18-site
  enumeration + anchor grammar). **v6 (2026-07-25)** then applied the round-4 FINAL codex gate: closed-
  allowlist scanner predicate catching `extern`/positional-`record`/primary-ctor (EXT4-01); nullable
  supersession link-keys + "exactly one accepted TOTAL" terminal (EXT4-02/07); purely-structural
  example-vs-block ADR discovery (EXT4-03); the ARCHITECTURE reconcile — dropped `Microsoft.Build.*`,
  registry set-equality (EXT4-04); paired-marker anchor grammar + pinned manifest schema + reference-not-
  triple-equality meta-test (EXT4-05); the "does execute / caught by reference-rejection" wording correction
  (EXT4-06); the OQ-002 built-carrier-half split (EXT4-08); the exact `evidence-schema.json` path constant
  (EXT4-10). EXT4-09 downgraded (the parent path-scoped INV-036 already catches a standalone `src/` package). **v7
  (2026-07-25)** then applied the round-5 FINAL codex gate on v6: DD-003 manifest = SOLE digest authority
  (ID-only markers) + a `P1.satisfied`-derived single-stage selector applied uniformly + a mixed-stage
  negative fixture (EXT5-01); INV-011's "bounded/sandboxed" build-execution claim dropped — arbitrary
  build-time execution of committed generators recorded as an accepted CI-runner-scoped residual, the
  analysis-phase reference-rejection the named control (EXT5-02); the entrypoint current-state correction
  re-staged Stage-B→Stage-A + three stale literals enumerated in the finite scan (EXT5-03); the exact-
  `10.0.302` runtime assertions in INV-011/015/016 replaced with the `latestPatch` band-membership predicate
  (EXT5-04). The round-4 rewrite clusters (EXT4-01/02/03/04/07/09/10) were codex-verified sound — no regression. **v8
  (2026-07-25)** applied a round-6 maintainer-relayed implementation-contract review of v7 (5 bounded issues;
  the v7 core — P1-derived global stage, sole manifest digest authority, Stage-A correction, dropped fake
  sandbox — confirmed sound): the canonical operator/CI command unified onto the committed runnable gate
  script `<GATE-SCRIPT>` (runs `dotnet test`→TRX-validate→render→exit), reconciling INV-012/014/017 with a
  recursion guard (EXT6-01); INV-011's build-execution residual broadened to ALL committed build extensions
  (MSBuild `Target`/`UsingTask`/`Exec`/`Import`/build-events) with `Analyzer` items extracted+rejected and
  new Analyzer/BeforeBuild/Exec fixtures (EXT6-02); the `latestPatch` bound made deterministic — root
  `allowPrerelease:false`, a precise band predicate (major 10, minor 0, band 3xx, >= 10.0.302), and the
  "reproducibility held" overclaim replaced with an honest lockfile-pins-packages-not-SDK-patches statement
  (EXT6-03); DD-003's manifest contract reconciled with its schema — after-digest IS the replacement, stage
  never stored per row, ADR B13 excluded from the anchor scan (verified by INV-008) (EXT6-04); and INV-004's
  purity check made satisfiable+complete by isolating the kernel+DTOs in a tiny I/O-free
  `gate/Corrected.Gate.Kernel/` project + a nondeterminism forbidden-set (EXT6-05).
  **v9 (2026-07-25)** applied a round-7 FINAL codex xhigh gate on v8 (4 BLOCKING + 2 IMPORTANT, all
  re-verified against the tree; the v8 core held): the EXT6-01 command unification was completed at the
  sites that lagged — the ARCHITECTURE `test_via` and INV-006 Entry, which still named bare `dotnet test`
  as the documented command, rebound to `<GATE-SCRIPT>` (EXT7-01); the wrapper's combined exit-code
  contract (`final_rc == test_rc==0 && trx_rc==0 && render_rc==0`, renderer ALWAYS runs after `dotnet
  test`) and the sentinel set/check process semantics were pinned + given wrapper-level fixtures (EXT7-02);
  INV-011's presence policy was genuinely closed — `-noAutoResponse`, committed response-file / custom-`<Sdk>`
  / MSBuild-property-function / NuGet-`buildTransitive`-asset rejection, an exact SDK-default `Analyzer`
  allowlist, and the syntax scan bound to the build's actual `DefineConstants` + post-target items so an
  active `#if` branch is not invisible (EXT7-03); INV-004's kernel isolation given a structural project-graph
  assertion + an expanded nondeterminism/ambient-state forbidden set (EXT7-04); the new
  `Corrected.Gate.Kernel` project rippled through Scope / Files / Packages / aggregator membership /
  INV-015-16 restore-lock set / ARCHITECTURE handler + TB-006 paths (EXT7-05); and the "single canonical
  site list" wording corrected to "canonical parent-anchor list" with the ADR B13 edit modelled as a
  separately-named Stage-B obligation (EXT7-06).
  **v10 (2026-07-25)** applied a round-8 codex xhigh VERIFICATION gate on v9 (did EXT7 introduce new
  contradictions? — 4 BLOCKING + 2 IMPORTANT + 1 MINOR, all re-verified): a fifth wrapper fixture forcing the
  `render_rc` term (EXT8-01); the INV-004 denylist reframed as defense-in-depth behind a behavioral
  determinism check (mutated culture/clock/cwd) + reflection-load added + the static-field ban tightened to
  reject `static readonly` mutable collections (EXT8-02); **INV-003's factory made a `public static`
  validation-gated `TryCreate` so the `Corrected.Gate` parser can construct the Kernel-resident
  `ReadinessBlock` across the assembly boundary (EXT8-03 — the split had made it a private-factory
  contradiction)**; DD-003 gives every deterministic anchor a committed canonical after-span fixture (digest
  derived from it, recoverable preimage) + an atomic Stage-B flip, with B1's evidence id validated
  structurally (EXT8-04); the SDK-default `Analyzer` set computed dynamically from a baseline project instead
  of a committed const so `latestPatch` drift never false-fails (EXT8-05); the round-7 Packages-Affected
  A/B enumeration removed to honor DD-003's own "no local site list" meta-test (EXT8-06, a round-7 regression);
  and the four-project shorthand reconciled in Context/TB-004/lock references (EXT8-07).
  **v11 (2026-07-25)** applied a round-9 codex verification gate on v10 (6 of 8 findings traced to the
  round-8 fixes — a non-convergence signal; maintainer chose to fix all 8 then HOLD): the migration manifest
  schema became a closed **discriminated union** (digest rows vs one structural row for B1's GREEN-assigned
  evidence id, EXT9-01); the Metadata `Impacts` B13 carve-out removed for symmetry with the Packages-Affected
  "no local site list" meta-test (EXT9-02); INV-004's purity claim honestly SCOPED to accidental first-party
  I/O (a `[DllImport]` bypass is out of scope — not a sandbox boundary — + `extern`/`[DllImport]` added to the
  forbidden kernel-decl set, EXT9-03); "deeply immutable" made a RECURSIVE predicate (EXT9-04); the dynamic
  Analyzer baseline pinned to a committed `gate/` fixture under locked restore + a same-`NETCoreSdkVersion`
  assertion + skip-after-vacuous (EXT9-05); the migration atomicity guarantee redefined as **atomic accepted
  TREE STATE** (the mixed-set guard already delivers it; per-commit history is out of scope, EXT9-06); the
  vacuous `GetConstructors(Public)` reflection test corrected to `Instance | Public` (EXT9-07); and INV-003's
  three protected types (`ReadinessBlock`/`ProbeResult` in Kernel, `AdrLintBlock` in Gate) + the single
  `public static TryCreate` form pinned (EXT9-08). **HELD uncommitted at maintainer request — no advance, no
  further gates.**
- **Recommended-intensity**: high
- **Intensity**: high
- **Intensity reason**: fail-closed security gate parsing a tamper-checked trust boundary
  (TB-006); TB-004 supply chain; antipattern overlap (AP-002/004/005/006/010/011/014/015/016/020/021).
  Project floor + parent = high.
- **Override**: none

## Context

The `phase-0-1-worker` spec is deliberately `implementation_readiness.status: BLOCKED` behind
P1/P2/P3, and defines a readiness gate (INV-001/002), a production-code ban (INV-036/PRH-008),
and ~22 invariants whose enforcement reads "CI test assertion" / "gate precondition" with **no
built test package** (OQ-002 built-carrier half, RS-002). This spec builds that home: the
test/build-gate carrier at `gate/Corrected.Gate/` (+ the isolated I/O-free `gate/Corrected.Gate.Kernel/`,
`gate/Corrected.Gate.Tests/`, and the extracted `gate/Corrected.Gate.Lint/`, aggregated by
`gate/Corrected.Gate.slnx`), realizing the parent's INV-001 (parse), INV-002 (fail-closed
decision + fixtures + real probes), and INV-036/PRH-008 (production-surface scan + blocker). It
is the RS-002 unlock. The gate cross-checks each probe's independent verdict against the declared
`satisfied` flag; P1's probe (DF-002) resolves **true after the DD-003 migration** (not on today's
committed tree — the ADR must first gain its machine-readable acceptance/supersession fields, DD-003
Stage B), so wiring the real P1 probe requires flipping the parent block's `P1.satisfied` to `true`
— this carrier discharges P1's readiness-flip residual as an atomic byproduct. The gate lives OUTSIDE
the shipped core/CLI so it enforces the ban without tripping it (INV-036 self-enforcement, PAT-005).
**The readiness block AND the ADR / evidence the P1 probe reads are an untrusted, tamperable boundary
(TB-006): the gate treats every such input as adversarial and fails closed.** (TB-006 is the carrier's
readiness/ADR/evidence intake boundary; TB-005 is reserved by the parent for `.dfy` source-byte intake
— parent BND-003 — a distinct boundary. Round-2 EXT2-09.)

## Scope

**In scope.** A non-shipped .NET 10 project set under `gate/` (`gate/Corrected.Gate/` +
the isolated I/O-free `gate/Corrected.Gate.Kernel/` (pure kernel + DTOs, INV-004/EXT6-05) +
`gate/Corrected.Gate.Tests/` + the extracted Dafny-free linter lib `gate/Corrected.Gate.Lint/` +
`gate/Corrected.Gate.slnx` aggregating **exactly** those four projects), with its own `<clear/>`
`NuGet.Config`, CPM opt-out, committed
`packages.lock.json`, and a **repo-root** `global.json`; readiness-block extraction + AST-hardened
strict parse (INV-001/002/003); the fail-closed pure kernel + verdict table + real probe orchestration
(INV-004..007); the three probes with a **hardened** P1 (INV-008) and fail-closed-on-absent P2/P3
(INV-009/010); the shipped-closure production-surface ban + blocker (INV-011/012); bootstrap/from-clean,
the documented command with an out-of-suite executed-count guard, pinned+locked YamlDotNet + analysis
toolchain + test-host, the SDK pin, and an **executable from-clean gate CI job** (INV-013..018); and the
atomic P1 flip of `phase-0-1-worker.md` via the DD-003 migration manifest.

**Out of scope.** The full P2/P3 discharge logic beyond fail-closed-on-absent (P2 completion-manifest
schema-validation + DF-003 remediation; P3 reworked `Inv010` lane) — land with their discharges.
**INV-044's evidence-schema history registry + meta-test is homed in this carrier's `gate/` dir per
parent + ARCHITECTURE, but is a SEPARATE deliverable landing with Phase-0.1 certification runtime —
NOT built by this spec** (DD-005; ARCHITECTURE's `readiness-build-gate` `test_via` is amended to mark
it a deferred extension, EXT2-12). Any `src/Corrected.*` production code; the `corrected explain` CLI
(INV-043's CLI form — INV-012 is the gate-side self-explainer, DD-004); any BLOCKED→READY transition
(P2/P3 stay false).

## Complexity Budget
- **Estimated LOC**: ~1600–2100 gate/probe/parser/scanner + extracted-lint lib + ~1600 test LOC
  (fixtures, YAML-hardening, P1 evidence/tamper, closure/bypass, from-clean, guard-self-tests).
- **Files touched**: `gate/Corrected.Gate/**`, `gate/Corrected.Gate.Kernel/**` (isolated pure kernel + DTOs,
  EXT6-05), `gate/Corrected.Gate.Tests/**`, `gate/Corrected.Gate.Lint/**`,
  `gate/Corrected.Gate.slnx`, `gate/NuGet.Config`, `gate/Directory.Build.props` (+ CPM opt-out) +
  `packages.lock.json`, a **repo-root `global.json`**, a `.gitattributes` pinning the parsed
  specs/ADR to LF, a **`.gitignore`** rule for the gate's TRX/`TestResults`/local restore output, a gate
  CI workflow under `.github/workflows/` + its extracted runnable from-clean script, and the **atomic**
  changeset to `phase-0-1-worker.md` + `ADR-0001` (DD-003 migration manifest, applied at GREEN). No `src/` files.
- **New abstractions**: `ReadinessBlock` (validated immutable) + a private parse DTO; a hardened
  `ReadinessBlockParser`; a **distinct** `AdrLintBlock` DTO + parser sharing the same hardening machinery
  (INV-008a/RS-206); `ReadinessGate.EvaluateReadiness` (pure kernel, homed in the isolated I/O-free
  `Corrected.Gate.Kernel` project with its DTOs — INV-004/EXT6-05); `IEvidenceProbe` +
  `ProbeResult{satisfied, reason, referenceResolution}` + three probes + orchestrator;
  `ProductionSurfaceScanner` (out-of-process pinned-SDK build closure); the extracted `Corrected.Gate.Lint` API.
- **Trust boundaries touched**: TB-006 (readiness-block + ADR/evidence intake/tamper — the carrier's
  boundary, renumbered from the v3 TB-005 collision); TB-004 (YAML parser + analysis toolchain + SDK). 2.
- **Risk surface delta**: high (the P1 probe reads adversarial ADR/evidence; treated at high intensity).

## Invariants

### Group A — Extraction & AST-hardened parse (parent INV-001)

### INV-001: Exactly one bounded readiness block; file + block size-capped; encoding-normalized
- **Type**: must · **Category**: data-integrity
- **Statement**: extraction reads `phase-0-1-worker.md` at a **tested repo-relative path constant**
  resolved via a deterministic repo-root anchor — walk up from the test assembly to the **named committed
  sentinel** = the directory containing BOTH the repo-root `global.json` (INV-016) AND the `.correctless/`
  directory (NOT the `dotnet test` cwd; RS-A-04/RS-264). It normalizes line-endings to LF / UTF-8-no-BOM
  **first**, THEN bounds the whole file by `MaxFileBytes` (cap applied post-normalization so a CRLF-on-disk
  file is not dead-red before normalization; RS-264), then locates **exactly one** readiness block by the
  discriminator **INV-001-D**: a single `implementation_readiness:` key at **column 0 inside the one
  ` ```yaml … ``` ` fenced block**; every other occurrence of the string (inline prose, backticked
  mentions, `.status` references — which legitimately exist in the parent, e.g. lines ~196/1069/1443) is
  **ignored as prose, not counted**. It strips the fence delimiters before YAML parse, bounds the extracted
  block by `MaxBlockBytes`. Zero or ≥2 column-0-in-fence blocks, an over-cap file/block → hard fail-closed.
  (A `.gitattributes` pins the parsed specs/ADR to LF; RS-A-10.)
- **Boundary**: TB-006. · **Guards against**: AP-014, AP-031, AP-004.
- **Violated when**: the path anchors to cwd; an inline prose mention is counted as a block (would dead-red
  the real parent, RS-261); the file is read unbounded; caps are unnamed; the cap is applied before
  normalization; the sentinel is unnamed/ambiguous.
- **Enforcement**: CI test — fixtures {0, 1, 2 in-fence blocks, inline-prose-mention (must be IGNORED, using
  a copy of the real parent's prose lines), indented-in-fence decoy, over-`MaxFileBytes`,
  over-`MaxBlockBytes`, CRLF-normalized-under-cap} + a two-cwd anchor test + a test that the **current real
  parent** parses to exactly one block under INV-001-D. `MaxFileBytes`/`MaxBlockBytes` are tested `public const`.
- **Test approach**: unit · **Risk**: medium

### INV-002: AST pre-validation over the low-level Parser event API; closed-vocabulary into a private DTO
- **Type**: must · **Category**: security
- **Statement**: parsing uses **YamlDotNet 18.1.0's low-level `IParser` event stream** (NOT the
  `YamlStream`/DOM, which resolves tags and cannot discriminate explicit `!!str` from a plain scalar —
  RS-T-03). **Stage 1 AST pre-validation**, short-circuiting at the first breach (RS-RT-16), REJECTS
  every explicit tag (incl. built-in `!!str`/`!!int`/`!!bool`), every anchor/alias, any second
  document, any trailing content, and enforces `MaxScalarLength`/`MaxNodeCount`/`MaxAliasCount`
  (tested `const`s; RS-T-07) incrementally. **Stage 2** deserializes the single validated document
  into a **private DTO** record with `required` members: never `IgnoreUnmatchedProperties()`;
  `.WithDuplicateKeyChecking()` + `.WithEnforceRequiredMembers()`; never `.WithTagMapping()`; target
  the concrete DTO, never `object`/`dynamic`. Post-parse (in-box `System.Text.Json`/code, no
  `WithEnforceNullability`, no schema-validation library): `schema_version` recognized-or-fail-closed;
  `status ∈ {BLOCKED, READY}`; exactly `{P1,P2,P3}`; `evidence` is `string?`; `ready_predicate` ==
  conjunction of ids; exact per-id `name`/`discharges` match the pinned table.
- **Same hardening, distinct schemas + REQUIRED-vs-OPTIONAL split (RS-206 / R3-B1, CRITICAL)**: the ADR
  `adr_lint` block (INV-008) is parsed by the **same Stage-1/Stage-2 hardening machinery** but a
  **distinct `AdrLintBlock` DTO**. Its vocabulary is split into two tiers so the pre-migration ADR parses:
  - **REQUIRED (present today; under `.WithEnforceRequiredMembers()`)**: `boundary_decision`,
    `selected_route`, `routes[]{route, verdict, adjudication_record_id, evidence}`.
  - **OPTIONAL / absent-allowed (added by DD-003 Stage B; CARVED OUT of `EnforceRequiredMembers`)**:
    `status`, `supersedes`, `superseded_by`. Each is parsed with an **explicit presence bit** (a
    `bool HasStatus`, etc., distinguishing *key-absent* from an explicit `null` value — a `required
    string?` would still demand the KEY be present, which hard-rejects today's ADR; that conflation was
    the v4 bug). **Key-absence of `status`** is NOT a parse error — it is surfaced as a typed signal that
    INV-008(a‴) maps to `evidence-schema-incomplete`. **Link-key wire form (EXT4-02)**: `supersedes` /
    `superseded_by` are **nullable** — a canonical ADR id string, explicit `null`, or key-absent — where
    **`null` and key-absent BOTH denote "no edge"** (their presence bit is retained for diagnostics but does
    NOT change edge/terminal semantics; only `status`'s presence bit drives the schema-incomplete
    short-circuit). So the migrated ADR-0001 `{status: accepted, superseded_by: null}` is well-formed and IS
    the terminal (no non-null successor) — closing the v5 "explicit null vs absent" contradiction.
  Reusing the readiness DTO verbatim would reject the real ADR — "same parser" means same machinery,
  distinct DTO. The spike's permissive `ExtractLintBlock` line-scanner is **never** used for a trust decision.
- **AdrLintBlock parse-failure taxonomy (R3-B1b)**: a Stage-1/Stage-2 failure on the `adr_lint` block is
  caught by the probe and mapped to a **typed false**, never thrown (INV-006 "never throws"), with a
  taxonomy that SEPARATES: **`evidence-schema-incomplete`** = a REQUIRED field is present and valid but
  the OPTIONAL acceptance schema (`status` key) is absent (the benign pre-migration case) — determined by
  the presence bits, NOT by a required-member exception; vs **`evidence-malformed`** = a REQUIRED field is
  missing/duplicated, a tag/anchor/alias/2nd-block materializes, or the block is structurally malformed
  (the tamper case). This prevents a malicious stripped `boundary_decision`/`selected_route` from being
  masked as "pre-migration" (INV-012 renders the two categories distinctly).
- **Unparseable → indeterminate value (RS-262)**: a readiness block that fails Stage-1/Stage-2 yields a
  typed `status: indeterminate` **value** handed to the kernel (it does NOT abort the gate), so INV-011's
  deny-by-default "ban stays active while status ∈ {BLOCKED, indeterminate}" branch is reachable and testable.
- **Boundary**: TB-006, TB-004. · **Guards against**: AP-004, AP-014.
- **Violated when**: any tag/anchor/alias/multi-doc/trailing content materializes; the DOM API is
  used; an unknown/dup key or missing required field is accepted; caps counted post-drain; unrecognized
  `schema_version` under-parses; a parse failure aborts the gate instead of yielding `indeterminate`.
- **Enforcement**: CI test — reject fixtures {unknown key, dup key, missing field, `!!str`, `!!int`,
  custom tag, `&a`/`*a`, multi-doc, trailing content, each cap+1, unrecognized `schema_version`,
  `status: MAYBE`, `ready_predicate` mismatch, name-drift, discharges-drift (RS-T-08)} + a valid-block
  fixture asserting exact values + an unparseable→`indeterminate` fixture + **AdrLintBlock fixtures**: a
  **verbatim real-producer** pre-migration `adr_lint` fixture (status/supersedes/superseded_by keys ABSENT)
  asserting it PARSES and yields `evidence-schema-incomplete` (NOT `evidence-malformed`, NOT a throw); a
  migrated fixture (`status: accepted`, `superseded_by: null` explicit) asserting it parses valid; a
  stripped-`boundary_decision`/`selected_route` fixture asserting `evidence-malformed` (AP-014).
- **Test approach**: unit · **Risk**: high

### INV-003: Validated construction — private DTO → validate → immutable domain type
- **Type**: must · **Category**: data-integrity
- **Statement**: YAML materializes only into the private DTO; `ReadinessBlock`, `ProbeResult`, and
  `AdrLintBlock` are built only through a **`public static` validation-performing factory (`TryCreate`)**
  yielding an immutable value (**no public constructor / no `init`/`set`**, `enum status`, immutable
  collections). **Type homes are pinned (EXT9-08)**: `ReadinessBlock` + `ProbeResult` live in
  `Corrected.Gate.Kernel` (kernel inputs, EXT6-05); `AdrLintBlock` lives in `Corrected.Gate` (parsed +
  consumed by the P1 probe in the orchestrator, not a kernel input). Because the parser in `Corrected.Gate`
  constructs the Kernel-resident `ReadinessBlock`/`ProbeResult` **across the assembly boundary** (one-way
  Gate→Kernel, EXT7-04), the factory is `public static` — NOT a `private` factory a separate assembly cannot
  call (EXT8-03) — and the **single chosen form is `public static TryCreate` for ALL THREE types** (no
  `internal`+`InternalsVisibleTo` variant, so implementations cannot diverge; EXT9-08). A private
  *constructor* is still required so validation cannot be bypassed: a reflection test asserts
  **`GetConstructors(BindingFlags.Instance | BindingFlags.Public)` is empty** (NOT `Public` alone, which
  omits instance constructors and vacuously passes — EXT9-07) and no property is publicly settable, so no
  `with` reaches an invalid state (RS-T-17), while the SOLE public entry is the validating `TryCreate`.
  `EvaluateReadiness`/probes accept `ReadinessBlock`/typed `ProbeResult`, never raw text or the DTO.
- **Guards against**: AP-014. · **Enforcement**: CI test — invalid-input-cannot-construct + the
  reflection structural check. · **Test approach**: unit · **Risk**: medium

### Group B — Pure kernel, verdict table, real orchestration (parent INV-002)

### INV-004: `EvaluateReadiness` is a pure, I/O-free decision kernel over supplied inputs
- **Type**: must · **Category**: security
- **Statement**: `EvaluateReadiness(block: ReadinessBlock, probeResults: IReadOnlyDictionary<
  PreconditionId, ProbeResult>) → {Pass | Fail, offending_precondition}` takes **caller-supplied**
  inputs (not the live probes/file), so every branch is reachable with supplied results. It performs
  **no I/O and is deterministic**. **The kernel + its DTOs live in a tiny project
  `gate/Corrected.Gate.Kernel/` (EXT6-05)** that the parser/orchestrator reference **one-way** (Gate/Tests →
  Kernel, NEVER the reverse). A **project-graph meta-test asserts the Kernel project declares NO
  `ProjectReference` and NO `PackageReference`** (it references only the framework BCL) and that no gate
  project introduces a Kernel→I/O edge — so the "scan the Kernel project only" completeness does NOT rest on
  convention (EXT7-04: a symbol scan ALONE would miss a `ProjectReference` to the I/O-capable orchestrator
  that is never symbolically used). "I/O-free" is a property of the Kernel's *own code*, not its references —
  the BCL still exposes I/O and clock APIs (`System.IO.File` etc. live in `System.Private.CoreLib`), which is
  exactly why the control is a **symbol-usage scan**, not a reference scan. So the no-I/O check is a
  **Roslyn symbol-usage scan over the KERNEL PROJECT's whole
  compilation**, which is both **satisfiable** (the project holds ONLY the pure kernel + DTOs, so there is no
  legitimate I/O to false-positive on — a whole-`Corrected.Gate` scan would trip on the parser/orchestrator's
  real I/O) and **complete** (any private helper is in the same project, so I/O hidden behind a helper is
  still in scope — a scan of only the `EvaluateReadiness` method body would miss it; EXT6-05). It fails if any
  symbol under `System.IO.*` (`File`/`Directory`/`Path`/`Stream`/`FileStream`/…), `System.Console`,
  `System.Net.*`, `System.Diagnostics.{Process,Stopwatch}`,
  `System.Reflection.Assembly.{Location,Load,LoadFrom,LoadFile}`, `System.Runtime.Loader.AssemblyLoadContext`,
  `System.Threading.Thread.Sleep`, `System.Threading.Tasks.Task.Delay`, `System.GC`, or the
  **nondeterminism / ambient-state set** — `System.DateTime.{Now,UtcNow,Today}`,
  `System.DateTimeOffset.{Now,UtcNow}`, `System.TimeProvider`, `System.Environment` (incl.
  `TickCount`/`TickCount64`/`UserName`), `System.Random`,
  `System.Security.Cryptography.RandomNumberGenerator`, `System.Guid.NewGuid`,
  `System.Globalization.CultureInfo.{CurrentCulture,CurrentUICulture}` — is referenced, **or** if the Kernel
  declares any **mutable static state**: a static field that is NOT `const` AND NOT a `readonly` reference to a
  **deeply-immutable type**, where deeply-immutable is defined **RECURSIVELY (EXT9-04)** — a
  primitive/string/enum, OR an immutable `record`/`ImmutableArray<T>`/`FrozenDictionary<K,V>` **every one of
  whose generic arguments AND record fields/properties itself satisfies this predicate**. So `static readonly
  List<T>`/`Dictionary<K,V>` is BANNED (mutable contents), AND so are the nested-wrapper cases
  `ImmutableArray<List<int>>` (mutable element) and a `record` holding a `List<T>` (mutable field) — the
  readonly reference is immutable but the reachable state is not (EXT8-02/EXT9-04); EXT7-04. Kernel methods
  declared `extern`/`[DllImport]` are ALSO rejected (P/Invoke, EXT9-03 — mirroring INV-011's EXT4-01
  rejection). (An assembly-reference scan is INSUFFICIENT on .NET 10 — `System.IO.File`
  lives in `System.Private.CoreLib`/`System.Runtime`, so a reference scan passes even for a kernel calling
  `File.ReadAllText`; RS-260.) **The denylist is defense-in-depth, and the check's SCOPE is honestly bounded
  (EXT9-03)**: it catches ACCIDENTAL I/O/nondeterminism in first-party kernel code the maintainer authors —
  it does NOT claim to PROVE no-I/O against a determined bypass (a `[DllImport]` P/Invoke to libc reads a
  file with no `System.IO` symbol and could pass the symbol scan absent the extern rejection above); the
  kernel is first-party code, NOT an adversarial-input sandbox boundary, so syscall-level purity is out of
  scope. Backstopping the denylist: (i) the project-graph bound (BCL-only, no packages, no I/O-capable
  project refs — a small reachable surface) and (ii) a **behavioral determinism check** asserting the kernel
  touches no fixture file and returns byte-identical verdicts across repeated calls with identical inputs,
  **run under a mutated ambient culture and clock** (so an ambient-state read the denylist missed still fails
  determinism). A **meta-test enumerates the forbidden-symbol set** so a newly-relevant API is added
  deliberately. Verdict defined by INV-005.
- **Violated when**: the kernel reads the committed file / calls probes internally; it does I/O, reads the
  clock/culture/RNG, LOADS an assembly (`Assembly.LoadFrom`/`AssemblyLoadContext`), or holds mutable static
  state incl. a `static readonly` MUTABLE COLLECTION (caught by the expanded symbol scan / tightened
  static-field ban / behavioral determinism check); the Kernel declares a `ProjectReference`/`PackageReference`
  (caught by the project-graph meta-test); the failure omits the offending id. · **Enforcement**: gate
  precondition — INV-007 fixtures + the Roslyn no-I/O/nondeterminism symbol scan (incl. `extern`/`[DllImport]`
  rejection, EXT9-03) + the **behavioral determinism check** (mutated culture/clock) + the **project-graph
  meta-test** (Kernel has no `ProjectReference`/`PackageReference`; one-way Gate/Tests→Kernel) + the
  **recursively-checked static-field ban** (only `const`/deeply-immutable-readonly per the EXT9-04 recursive
  predicate) + one negative fixture per ambient-state/I/O family (clock, culture, RNG, console, process,
  reflection-load, `extern`/`[DllImport]`, `static readonly` mutable collection, AND the nested cases
  `ImmutableArray<List<int>>` + a record-containing-collection, EXT9-04). · **Test approach**: unit · **Risk**: high

### INV-005: The total verdict table — reference resolution + declared-vs-actual cross-check
- **Type**: must · **Category**: security
- **Statement**: each `ProbeResult` carries `{satisfied, reason, referenceResolution ∈
  {Resolved|Unresolvable|Malformed}}` (resolvability is populated by the orchestrator, INV-006 — the
  pure kernel cannot do the I/O to decide it; RS-T-06/RS-RT-05). Per precondition the kernel decides
  by this **total** table (evidence, declared, actual, referenceResolution):
  - `evidence==null ∧ declared false ∧ actual false` → **consistent** (no fail).
  - `evidence==null ∧ declared true` → **Fail** (a satisfied claim must cite evidence).
  - **`evidence==null ∧ declared false ∧ actual true` → Fail (BLOCKED-but-actually-satisfied)** — the
    cell that makes the P1 flip mandatory (RS-DC-02). **Deadlock note (RS-220/EXT2-02)**: this cell means
    today's committed block (P1 declared-false, actual-true-after-migration) Fails; the carrier is green
    only after the DD-003 Stage-B flip. The pre-flip **Stage A** commit is kept green because the real P1
    probe returns `false` on the *pre-migration* ADR (no machine `status:`/chain → typed
    `evidence-schema-incomplete` false), so `(null,false,false)` is consistent — see DD-003 staging.
  - `evidence!=null ∧ referenceResolution ∈ {Unresolvable, Malformed}` → **hard Fail regardless of
    status** (distinct from actual-false).
  - `evidence!=null ∧ Resolved` → cross-check declared vs actual; mismatch either direction → Fail.
  - `status: READY` legal **iff** every actual true ∧ every reference Resolved; else READY → Fail.
  - `status: indeterminate` (unparseable, INV-002) → **Fail**, and the INV-011 ban stays active.
  An **evidence-reference registry** (allowed test-ids/gate-names per precondition, a tested constant)
  defines resolvability independent of the probe verdict (RS-RT-05/RS-DC-05).
- **Violated when**: the `(null,false,true)` cell is undefined/consistent; a non-null unresolvable
  reference passes; resolvability is conflated with the probe verdict; READY passes with any
  false/unresolved; `indeterminate` passes.
- **Enforcement**: gate precondition — a fixture per table row incl. `(null,false,true)`, `(null,false,false)`
  (Stage-A current state), unresolvable under **both** declared values, `satisfied:true+probe-true+
  unregistered-evidence → Fail`, `BLOCKED+all-probes-true`, `READY+all-true+all-resolved`, `indeterminate → Fail`.
- **Test approach**: unit · **Risk**: high

### INV-006: Real probe orchestration; typed fail-closed reasons; current-state binding
- **Type**: must · **Category**: security
- **Statement**: an orchestrator runs the **real** P1/P2/P3 probes on the real committed artifacts
  (nothing mocked) and produces the `ProbeResult` map + `referenceResolution`. Each probe returns a
  **typed** `{satisfied:false, reason}` — never throws/skips — on an absent/`pending`/unreadable/
  **present-but-malformed** artifact, with a reason taxonomy distinguishing `evidence-absent`,
  `evidence-malformed`, `evidence-refutes`, `evidence-schema-incomplete` (the pre-migration ADR case), and
  `validator-deferred` (so a degraded env is distinguishable from a real regression; RS-UX-01/06).
  All structured-field reads use the **exact real producer JSON paths**, which are nested under the
  `deterministic.` envelope (`deterministic.route_verdicts[].state`, `deterministic.per_probe_results`,
  `deterministic.final_suite_status`, `deterministic.exit_report_matrix_outcome`) — NOT top-level (RS-207/EXT2).
  The current-state test asserts the **stage-current form ONLY** against the **real committed tree**
  (R3-M1 — "nothing mocked" forbids asserting a form that isn't the real state at that commit): pre-flip
  (Stage A) it asserts the real probe returns `P1=false (evidence-schema-incomplete)` — via the (a)
  status-key-absent short-circuit, a **typed false, never a throw** (so INV-006 "never throws" holds and
  Stage A is green) — with the committed block → `Pass`, `status: BLOCKED`; the Stage-B flip commit swaps
  the assertion to `P1=true, P2/P3=false (validator-deferred)` against the then-real migrated tree →
  migrated block `Pass`, `status: BLOCKED`. The two forms never coexist in one commit.
- **Violated when**: a probe throws/skips (incl. a hard parse-reject of the pre-migration ADR instead of a
  typed schema-incomplete false); the orchestrator mocks a probe; the gate passes on prior-run
  `out/`/`out/current` state; the current-state test is missing or asserts a non-stage-current form; a flat
  (non-`deterministic.`) path is read.
- **Enforcement**: gate precondition — absent + malformed + present-well-formed fixtures per probe;
  the stage-current-form current-state orchestration test; the from-clean gate (INV-013); a test asserting
  no check resolves its subject from `out/`.
- **Test approach**: integration
- **Integration contract**: Entry: the documented operator/CI command is the committed script
  `<GATE-SCRIPT>` (INV-014/EXT7-01), which runs `dotnet test <AGGREGATOR> --logger trx` from a clean checkout
  (clone + `rm -rf spikes/dafny-compat/out/`, INV-013/EXT2-11) — the `readiness-build-gate` entrypoint, where
  `<AGGREGATOR>` is the single pinned constant of INV-014; this INV-006 orchestration assertion runs INSIDE
  that `dotnet test <AGGREGATOR>` suite (it is a test in the discovered suite, NOT a separate shell-out to the
  script — that would recurse, INV-017/EXT7-02). Through: the real kernel + real
  orchestrator over the real committed spec/ADR/evidence; nothing mocked. Exit: post-migration result
  `P1=true, P2/P3=false` → migrated block `Pass`, `status: BLOCKED`.
- **Risk**: high

### INV-007: The fixture corpus exercises every kernel branch (supplied-input TP/FP), with a coverage meta-test
- **Type**: must · **Category**: security
- **Statement**: a committed `(SUPPLIED block, SUPPLIED probeResults)` corpus drives the kernel through
  every INV-005 row (see INV-005 enforcement), plus the INV-006 absent/malformed/present-well-formed
  per-probe cases, plus the committed-block current-state test. The **kernel-fixture portion is `unit`**
  (the kernel has no real dependency; RS-T-19); only the current-state case is the INV-006 integration
  binding. INV-007 partitions into **INV-007a** (the SUPPLIED-input kernel-branch reject corpus —
  flip-independent, green at any commit incl. pre-flip Stage A) and **INV-007b** (the committed-block
  current-state binding — Stage-A form green pre-flip, post-migration form green only in the Stage-B flip
  commit; RS-221/EXT2-02).
- **Guards against**: AP-002, AP-010, AP-014, AP-006.
- **Enforcement (RS-263)**: gate precondition — INV-005/006 fixtures **plus a corpus-coverage meta-test**
  that enumerates every INV-005 table-row id + every INV-006 per-probe case (a tested `const` set) and
  asserts each is present in the committed corpus (so dropping a fixture fails the meta-test, not silently
  greens). · **Test approach**: unit + one integration binding · **Risk**: high

### Group C — Evidence probes (parent INV-003/004/005)

### INV-008: P1 probe — HARDENED ADR promotion + component-table, resolving true after the DD-003 migration
- **Type**: must · **Category**: security
- **Statement**: P1 returns `true` only when ALL hold, each fail-closed to a typed `false` otherwise.
  **Global evaluation order (R3-B1 re-check)**: (a)'s schema-completeness short-circuit is a **global gate
  that precedes (a′)/(a″)/(a‴)** — if the `adr_lint` `status` key is absent the probe returns
  `evidence-schema-incomplete` immediately, before any evidence/recompute/supersession check runs (so a
  future stale `canonical_sample_sha256`/manifest const cannot dead-red the pre-migration Stage-A path via
  (a′)/(a″) being evaluated first). The clauses:
  - **(a) Hardened ADR parse + decision fields, with a SCHEMA-COMPLETENESS SHORT-CIRCUIT (RS-202/RS-206 /
    R3-B1b/R3-M2/R3-M3)** — the ADR `adr_lint` block is parsed by the **same hardening machinery** as
    INV-002 into the **distinct `AdrLintBlock` DTO** (reject ≥2 `adr_lint` blocks, ≥2 route claims per id,
    duplicate keys, tags/anchors). **Evaluation order is fixed** (R3-B1b/R3-M3): (1) the REQUIRED fields
    parse or → `evidence-malformed`; (2) **schema-completeness** — if the OPTIONAL `status` key is absent
    (presence bit false), short-circuit to `evidence-schema-incomplete` **before** any acceptance,
    prose↔machine, or terminal-rule check (so the benign pre-migration ADR is never misreported as a split
    or a tamper, and Stage A is green); (3) only when `status` is present do the remaining asserts run.
    The **authoritative** decision is made here (NOT delegated to the demoted spike linter): assert
    `boundary_decision` == the in-process-selected value, `selected_route == A`, and route-A's `verdict ==
    COMPATIBLE`. The spike's `AdrLinter.Lint` MAY additionally run as a **redundant cross-check** (empty
    `records` list — valid for an all-pass COMPATIBLE post-DF-002); the **differential fixture** asserts the
    two parsers agree **only on the shared decision fields** (`boundary_decision`/`selected_route`/
    `routes[].verdict`), NOT on overall pass/fail (they disagree by design pre-migration: hardened =
    schema-incomplete-false, spike-linter = zero-findings-pass — so an "overall verdict agree" fixture would
    dead-red Stage A; RS-205/R3-M3). The **prose↔machine status consistency** check (runs only in step 3,
    when `status` is present) extracts the **leading `{accepted|superseded|provisional}` token** from the
    ADR's line-3 `**Status**:` field (which is a multi-line parenthetical, e.g. `accepted (DF-002 …)` — a
    naive `==` dead-reds P1 at Stage B; R3-M2) and asserts it equals the machine `adr_lint.status`, blocking
    the human-review-evasion split (RS-208).
  - **(a′) Pinned canonical evidence + COMPILED content anchors (EXT2-05 / R3-B2, the residual-forge close)** —
    the cited evidence path must **equal a pinned committed path constant** = the **canonical** sample
    `spikes/dafny-compat/evidence/samples/run-report.canonical.sample.json` (NOT the variance
    `run-report.sample.json`, which is `INCOMPLETE`/`final_suite_status: unknown` and recomputes
    NOT-COMPATIBLE; the path is NOT taken from the tamperable ADR field, RS-RT-02/RS-210), and resolve to a
    repo-relative committed regular file (reject absolute/traversal/symlink). **THREE compiled content
    anchors** (`public const` SHA-256 in the gate), because the sample, the schema, AND the manifest are all
    frozen convergence artifacts (committed at/around `d28ed5d`) and the (a″) recompute only checks the
    sample's *internal* consistency — without a content pin a commit-access adversary (the TB-006 threat)
    coherently rewrites the frozen sample (flip every probe to pass / `final_suite_status: success` / route-A
    `COMPATIBLE`) keeping the referenced sha fields at their pinned values and forges P1=true (R3-B2):
    - `canonical_sample_sha256` == SHA-256(the canonical sample file) — **NEW, closes the residual forge**;
    - `evidence_schema_sha256` == SHA-256(the pinned schema file `spikes/dafny-compat/schema/evidence-schema.json`)
      == the sample's `evidence_schema_sha256` field;
    - `probe_manifest_sha256` == SHA-256(the probe-manifest file) == used by (a″) (R3-B2b).
    A coherent multi-file tamper of the *data files* still fails against these compiled constants. **TCB
    framing (R3-B2 re-check)**: this is NOT a cryptographic barrier against the TB-006 "anyone with commit
    access" adversary (who can edit the sample AND the `const` literal in one commit) — it **relocates** the
    trust root from a silent data-file edit to the **review-gated gate source** (a change to a security
    `sha256`/registry `const` is a conspicuous, review-flagged diff), and forces any forge onto that
    reviewable surface. The gate source (its SHA consts + the ADR registry const) IS the review-gated TCB
    root; the compiled pins are defense-in-depth (make the forge loud), not impossibility. A **Stage-A
    positive fixture** asserts `SHA256(the committed canonical sample) == canonical_sample_sha256` directly
    (so a stale const dead-reds at Stage A, not only at the Stage-B binding). `evidence_schema_version` is
    validated against a compiled **recognized set** (one element today: `2`; the set retains prior versions
    as added). Reason taxonomy (R3-L2): version/sha **older-or-mismatched** → typed `evidence-schema-mismatch`;
    a **newer-than-pinned** version → typed `evidence-schema-newer-than-pinned; bump the gate pin`
    (RS-209/RS-UC-03); neither is a generic malformed.
  - **(a″) Sound COMPATIBLE recompute — cardinality-guarded, duplicate-safe (RS-201/EXT2-04, CRITICAL;
    +R3-B3/R3-L1)** — recompute Route-A COMPATIBLE from the sample's **structured fields** (nested under
    `deterministic.`) via in-box `System.Text.Json`. **First, reject duplicate JSON property names
    recursively** before any field read (set `JsonDocumentOptions.AllowDuplicateProperties = false` on
    net10, or a `Utf8JsonReader` pre-pass) — the JSON path otherwise inherits `System.Text.Json`'s default
    last-wins duplicate handling, so `"status":"fail","status":"pass"` or two `deterministic` members would
    silently resolve to one value and defeat the guard (R3-B3). **Then**, re-derive the expected
    `(probe, route)` set from the **pinned probe manifest file** (`manifest/probe-manifest.json`, its file
    SHA-256 == the `probe_manifest_sha256` compiled pin of (a′) — so a tampered manifest FILE fails, not just
    a wrong sample field; R3-B2b) restricted to the **Route-A + shared partition** (matching
    `VerdictAggregator.ComputeRouteVerdict`'s non-veto partition — NOT the full 22-entry both-routes set,
    which would couple route-A's P1 to route-B completeness against the manifest's non-veto policy; R3-L1),
    and assert **count-aware multiset equality** (a `HashSet.SetEquals` silently dedups; mirror
    `ComputeRouteVerdict`'s `if(!seen.Add(key))` duplicate rejection; R3-B3b) against the Route-A+shared
    `deterministic.per_probe_results`: no missing, duplicate, or extra `(probe,route)` entries, and
    **exactly one** Route-A `route_verdicts` entry. Only then evaluate `route_verdicts[A].state ==
    COMPATIBLE ∧ deterministic.final_suite_status == success ∧ deterministic.exit_report_matrix_outcome ==
    consistent ∧ (∀ per_probe_results where route ∈ {A, shared}: status == pass)`. This closes the vacuous
    forge (stripping to route-B-only/empty fails the multiset equality). **Never** trust the declared
    `route_verdicts[].state` alone. Mutation fixtures: {flip one route-A probe to `fail`; `final_suite_status:
    unknown`; empty `per_probe_results`; route-B-only `per_probe_results`; duplicate route-A verdict;
    duplicate `(probe,route)` entry (multiset-count); **duplicate JSON property name** at root / route-verdict
    / per-probe; wrong `probe_manifest_sha256` (sample field); **tampered manifest FILE** (a `(probe,route)`
    removed from `manifest/probe-manifest.json`); **coherently-tampered canonical sample** (all fields flipped
    to pass — caught by (a′)'s `canonical_sample_sha256`)} each drive the probe `false`.
  - **(a‴) Acceptance + supersession — defined graph + terminal rule + registry set-equality (RS-203/
    EXT2-01 / R3-B1/R3-B4/R3-I6)** — the `adr_lint` DTO (INV-002/RS-206) carries an **optional** `status`
    (enum `{accepted, superseded, provisional}` with a presence bit) and **optional** `supersedes` /
    `superseded_by` (each a canonical ADR id string, explicit `null`, or absent — **null and absent both
    denote "no edge"**, EXT4-02). **`status`-key-absent → the (a) step-2
    short-circuit returns `evidence-schema-incomplete` typed-false** (the pre-migration case; these fields
    are added to ADR-0001 in the DD-003 Stage-B changeset, so P1 is schema-incomplete-false until Stage B).
    **Supersession graph (R3-I6)**: `supersedes`/`superseded_by` values are **canonical ADR ids** (e.g.
    `ADR-0001`) drawn from the registry id-space; the gate builds the directed graph over the registered
    ADRs and requires **reciprocal edges** (`X.superseded_by == Y` ⟺ `Y.supersedes == X`; a one-way link →
    fail closed), **reachability of every registered `adr_lint` node from the ADR-0001 root**, and **exactly
    one** `status==accepted` node whose `superseded_by` is **null-or-absent** (no non-null successor) whose
    boundary is Route-A = the terminal — **and exactly one `status==accepted` node TOTAL across the
    registry**: every non-terminal registered node MUST be `status: superseded` (EXT4-07).
    Fail closed on: `provisional`, a non-reciprocal/one-way edge, a dangling target, a cycle, two accepted
    terminals, **an `status==accepted` node that HAS a non-null successor (should be `superseded`, EXT4-07)**,
    a disconnected node, or a non-Route-A terminal.
    **Registry = authoritative set (R3-B4, closes the round-2 over-correction; supersedes RS-204)**: the ADR
    registry is a **compiled `const`** (R3-B4b) listing every ADR file that carries an `adr_lint` block, and
    the gate asserts **set-equality** between the registry and the ADRs actually carrying an `adr_lint:`
    block on disk. **Discovery is PURELY STRUCTURAL (EXT4-03)**: **every** `adr_lint:` key at **column 0
    inside a ` ```yaml … ``` ` fence COUNTS as a block** (backticked/inline/prose mentions do not — they are
    not a column-0 yaml-fenced block). A snippet that must NOT count may **not** rely on surrounding prose to
    be read as an "example" — it MUST use a **non-matching form** (a non-`yaml` info-string fence, non-column-0
    indentation, or the sentinel key `adr_lint_example:`); a real column-0 `adr_lint:` inside a `yaml` fence
    is ALWAYS counted → it must be registered or set-equality fails closed (this removes the v5 ambiguity
    where a fenced example satisfied both the discriminator and an "example" exemption — R3-B4-recheck
    superseded). Fixtures: a **decoy in non-matching form** asserts NOT counted; a **matching-form fenced
    example** asserts IT IS counted → registration required. An `adr_lint` block on disk that is **not
    registered** → **fail closed** ("register this ADR in the gate"), NOT ignored — because "ignore out-of-allowlist"
    (v4) let an attacker add an out-of-list `ADR-0002{status:accepted, supersedes:ADR-0001, Route B}` while
    ADR-0001 stayed the apparent terminal and P1 stayed true (R3-B4). (This restores DoS-resistance the
    right way: adding an ADR requires a coordinated gate-const edit, a loud fail, not a silent bypass.)
    **(a)/(a‴) interaction (R3-M/P1-RT)**: (a) asserts decision fields against ADR-0001 at a pinned path;
    once a legitimate Route-A successor supersedes ADR-0001, P1 fail-closes until the gate's pinned
    decision-path const + registry are re-pinned to the successor (a defensible fail-closed posture, stated
    here). Today only ADR-0001 exists, so the multi-ADR terminal cases are **fixture-driven over synthesized
    registries**, not the live single-entry one. **Live-binding stage (R3-B4-recheck)**: because (a‴) sits in
    step 3 and Stage A short-circuits at (a) step 2, the registry set-equality's FIRST live-tree run is the
    **Stage-B** current-state binding (pre-Stage-B it is exercised only by synthesized-registry fixtures) —
    self-consistent (P1 is false at Stage A regardless), but it means a stale registry const or an
    over-counting discovery would dead-red **Stage B**, so the Stage-B green-milestone check is where the
    live registry assertion is proven.
  - **(b) Component table — with propagation equality (EXT2-08)** — read the **structured `route-a.json`
    only** at its exact pinned path constant. The **"Dafny-family" predicate is defined explicitly** as an
    exact match of `assemblies[].simple_name` against the pinned expected set (NOT a `Dafny`-prefix
    substring, which would also match unrelated names, and NOT including `Boogie.*`; R3-L2). Assert **exact
    set-equality** of the Route-A Dafny-family loaded set against the pinned expected set
    `{DafnyCore, DafnyDriver, DafnyLanguageServer}` with `DafnyPipeline` **absent** — so removing
    `DafnyDriver`/`DafnyCore` (the manifest anchors) FAILS, not just "LanguageServer present / Pipeline
    absent" (note `DafnyLanguageServer` is in `assemblies`, not `anchors`). Do NOT substring-scan the ARCHITECTURE prose row, DESIGN.md, or `route-a.json`'s own
    `notes[]` (all contain "DafnyPipeline" in a negation/publication context → false-fail;
    RS-T-02/RS-A-08/RS-273). A **machine-readable production-assembly block** added to ARCHITECTURE
    (EXT2-08 amendment) is asserted equal to the route-a.json set, so P1 actually proves *propagation*;
    parent INV-003's "reads DESIGN.md and ARCHITECTURE.md tables" is reconciled in the DD-003 migration to
    "the ARCHITECTURE machine-readable production-assembly block is authoritative; route-a.json is the
    machine source; DESIGN prose is publication-scoped and excluded" (RS-DC-04).
  - **(c) Trust-root pin — extracted lib + append-only registry (RS-211/EXT2-10)** — the reused spike linter
    is **extracted** into the dedicated small `gate/Corrected.Gate.Lint/` lib (narrow `AdrLintBlock`-shaped
    API; INV-018/DD-001) rather than pinning the 1343-line churning `Components.cs`. Its source is pinned by
    an **append-only version/digest registry** `gate/Corrected.Gate/lint-source-registry.json` with a
    sanctioned bump procedure (edit lib + append a registry row in one commit, with a test they agree);
    the fixture pins the extracted lib, NOT `Components.cs`. The pinned `route-a.json`/canonical-sample are
    bound by exact path constants, never a glob (a leaked/gitignored `out/**` copy is never consulted; a
    fixture proves it by synthesizing its own decoy tree, since `rm -rf spikes/dafny-compat/out/` removes
    any committed-adjacent copies, RS-271).
  Because DF-002 is discharged, this resolves **true after DD-003 Stage B** (which adds the ADR
  acceptance/supersession fields). The committed parent block reads `P1.satisfied: true` /
  `P1.evidence: <this gate's registered test id>` **only in the Stage-B flip commit** (INV-005 cross-check);
  pre-Stage-B the probe is `evidence-schema-incomplete` false and the committed block stays consistent-BLOCKED.
- **Boundary**: TB-006. · **Guards against**: AP-004, AP-006, AP-014, AP-016, AP-010.
- **Enforcement**: integration — fixtures: pre-migration ADR (status/chain keys absent) → schema-incomplete
  (not malformed, not throw); migrated ADR (status:accepted, superseded_by:null explicit) → parses;
  stripped-`boundary_decision`/`selected_route` → malformed (NOT schema-incomplete — the masking guard);
  forged-2nd-route-claim, duplicate adr_lint block, `selected_route:B`/`boundary_decision:rejected`/route-A
  `verdict:INCOMPATIBLE` (decision-field tamper), prose↔machine status mismatch (multi-line prose token
  extraction), two-parser differential agree-on-decision-fields (Stage-B), nonexistent/traversal/symlink
  path, ADR-cited-path ≠ pinned canonical constant, variance-sample-recomputes-NOT-COMPATIBLE,
  **coherently-tampered canonical sample → fails on `canonical_sample_sha256`**, compiled-anchor
  schema-digest mismatch, coherent three-file schema tamper (still fails), **tampered manifest FILE**,
  **duplicate JSON property name (root/route-verdict/per-probe)**, evidence-schema-mismatch (older) vs
  evidence-schema-newer-than-pinned, the (a″) cardinality/recompute mutations (route-A+shared partition),
  supersession graph cases {accepted+null-or-absent-superseded_by→terminal-pass, migrated
  `superseded_by:null`→terminal-pass (EXT4-02), provisional→false, reciprocal
  superseded_by→ADR-0002(Route B)→false, one-way/non-reciprocal edge→fail-closed, dangling target→fail,
  cycle→fail, disconnected node→fail, two terminals→fail, **accepted-with-non-null-successor→fail (should
  be `superseded`, EXT4-07)**} over synthesized registries, **unregistered
  on-disk `adr_lint` block → fail-closed ("register this ADR")**, component-table set-equality (drop
  DafnyDriver → fail; Dafny-family exact-match not substring), `out/**` copy not consulted, extracted-lib digest change.
- **Test approach**: integration · **Risk**: critical · **Cross-ref**: DF-002,
  [[dafny-boundary-route-a-selected]], parent INV-003, DD-001.

### INV-009: P2 probe — fail-closed for absent AND present, until the validator lands
- **Type**: must · **Category**: functional
- **Statement**: the P2 probe resolves the Phase-0.0 completion manifest at the pinned constant
  `test/manifests/phase-0.0-completion.json` (DD-002). Until the full validator lands (out of scope),
  it returns `{satisfied:false, reason:"validator-deferred"}` **unconditionally for any input —
  absent, malformed, OR present-well-formed** — so a committed stub can never flip P2 (RS-RT-03). Its
  rendered reason (INV-012) carries a **discharge pointer** to where the P2 validator work is specified
  (the DF-003 remediation lane + the DD-002 manifest schema; RS-UX-09/UX-011).
- **Enforcement**: integration — absent, malformed, AND **present-well-formed** fixtures each assert
  `false` with the `validator-deferred` reason. · **Test approach**: integration · **Risk**: medium
  · **Cross-ref**: DF-003, parent INV-004, DD-002.

### INV-010: P3 probe — fail-closed; a durable, provenance-bound attestation (no bare committed claim)
- **Type**: must · **Category**: functional
- **Statement**: the P3 probe resolves a **durable committed determinism attestation** at
  `test/attestations/inv010-determinism.json` (DD-002), whose eventual shape is `{outcome, ProcessorCount,
  RID}` **carrying a provenance binding** (a signature / SLSA / a receipt digest chained to the
  reference-CI lane, TB-003) — a bare self-attested JSON is insufficient (anyone with commit access
  forges `ran-passed`; RS-RT-13). Until the full validator lands, it returns
  `{satisfied:false, reason:"validator-deferred"}` **unconditionally for any input** (absent, malformed,
  present-well-formed; RS-RT-03/RS-T-09), with a discharge pointer (RS-UX-09). Binding a persistent P3 flag
  to an **ephemeral** CI-workspace file is prohibited. The concrete provenance mechanism is OQ-A#3.
- **Enforcement**: integration — absent, malformed, present-well-formed → `false`/validator-deferred.
  · **Test approach**: integration · **Risk**: medium · **Cross-ref**: parent INV-005,
  [[dafny-spike-harness-reliability-plan]], DD-002.

### Group D — Production-surface ban over the shipped closure (parent INV-036/PRH-008)

### INV-011: Deny-by-default ban over the REAL shipped compilation closure (pinned-SDK, out-of-process)
- **Type**: must-not · **Category**: security
- **Statement**: while `status ∈ {BLOCKED, indeterminate}` (deny-by-default — an unreadable/unparseable
  status keeps the ban active, RS-RT-15/RS-262), the check operates over the **actual shipped compilation
  closure** of `src/Corrected.{Core,DafnyAdapter,Cli}`. **Scanner architecture (RS-230/RS-231/EXT2-07 —
  real out-of-process build, maintainer decision 2026-07-25)**, in two phases:
  - **Build phase** — an **out-of-process `dotnet build` bound to the pinned SDK** (INV-016's repo-root
    `global.json`; NOT an in-process `MSBuildLocator` resolving ambient machine MSBuild + inherited props,
    which TB-004 forbids). **The build is invoked with `--noAutoResponse` (EXT7-03)** so a committed
    `Directory.Build.rsp`/`MSBuild.rsp` cannot silently inject switches into the build; any such committed
    response file in the closure also FAILS closed under the presence policy (below). The build **does execute** the closure's source generators/analyzers (that is how
    generated sources come to exist) and emits them to disk via `EmitCompilerGeneratedFiles=true` →
    `CompilerGeneratedFilesOutputPath` (R3-B6 — v4's "do not execute generators" was incoherent with a real
    build; the ban never needed to *inspect a generator's body*, only its *emitted output* + *its reference*).
    The `Compile` and `Analyzer` item sets are extracted **after the build's evaluation+target run (a
    `-getItem:Compile,Analyzer` query bound to the SAME build so it captures target-added items — NOT an
    evaluation-only `-getItem` that misses target-injected analyzers/generated `Compile` items; EXT7-03)** —
    analyzers/source-generators ship as MSBuild `Analyzer` items, NOT necessarily ordinary assembly
    `Reference`s, so both feed the analysis-phase allowlist below (EXT6-02). **The .NET SDK enables built-in
    analyzers by default, so the SDK-default `Analyzer` set is computed DYNAMICALLY at gate time from a
    **committed baseline fixture project under `gate/Corrected.Gate.Tests/fixtures/analyzer-baseline/`** (its
    own `<clear/>` `gate/NuGet.Config` scope + committed `packages.lock.json` + locked restore — same
    discipline as the shipped-closure fixtures; EXT8-05/EXT9-05), built under the SAME resolved SDK: the
    closure's `Analyzer` set MINUS the baseline project's `Analyzer` set is the non-default set that must be
    empty/allowlisted. **The gate asserts the baseline build and the closure build report the SAME captured
    `NETCoreSdkVersion`** (so two `dotnet build` invocations in one run cannot straddle two `latestPatch`
    patches; EXT9-05), and **skips baseline construction entirely after the zero-project vacuous
    short-circuit** (no closure → no analyzer diff needed). This sidesteps `latestPatch` band drift (baseline
    resolves the same patch as the closure) and covers all permitted SDK identities uniformly (the baseline
    uses the closure project's own `<Sdk>` identity)**; anything outside that computed default set is non-allowlisted and rejected. (SDK ≥ 8, present on
    10.0.302 — a manifest that no `Microsoft.Build.*` in-process reference exists; R3-I1/EXT2-10). **RT-L1
    build-time-code-execution risk** is stated and SCOPED AS AN ACCEPTED RESIDUAL (EXT5-02): the build runs
    under the pinned SDK + the `<clear/>`-isolated locked restore, and while BLOCKED `src/` is empty/skeleton
    (nothing real to execute). The real out-of-process build **does execute** any committed source
    generator/analyzer BEFORE the analysis phase rejects its non-allowlisted reference; this gate specifies
    **no** build-time containment boundary (no scrubbed env / network restriction / timeout / process-tree
    kill) — arbitrary build-time execution of committed generators, analyzers, AND **MSBuild build extensions**
    (custom `<Target>`/`BeforeBuild`/`AfterBuild`, `<UsingTask>`, `<Exec>`, non-SDK `<Import>`, pre/post-build
    events — all of which run during `dotnet build` and evade the C#/reference scan) is an **accepted
    CI-runner-scoped residual**, with containment delegated to the CI runner's own sandbox and NOT claimed by
    this gate (the earlier "bounded/sandboxed" framing was dropped as an unbacked overclaim, EXT5-02). **But
    committed build extensions do NOT get a free pass on PRESENCE (EXT6-02/EXT7-03, closed policy)**: the
    shipped closure's `.csproj`/`.targets`/`.props`/`.rsp` must declare NONE of — a custom
    `<Target>`/`BeforeBuild`/`AfterBuild`/`<PreBuildEvent>`/`<PostBuildEvent>`, `<UsingTask>`, `<Exec>`, a
    non-SDK `<Import>`, a **non-default `<Sdk>`/`Sdk=` identity** (only `Microsoft.NET.Sdk`/`.Web`/`.Razor`
    permitted), a committed **response file** (`Directory.Build.rsp`/`MSBuild.rsp`), or an **MSBuild property
    function** (`$([Type]::Member(...))`) anywhere in a `PropertyGroup`/`ItemGroup` (property functions run at
    evaluation and can read time/env/culture/files) — only the closed SDK-default set is permitted (empty
    while BLOCKED); any present FAILS closed. **NuGet build assets are covered too (EXT7-03)**: the locked
    `packages.lock.json` is inspected and any restored package contributing `build`/`buildMultiTargeting`/
    `buildTransitive` MSBuild logic — which executes via restore-generated `obj/*.nuget.g.props/targets`
    WITHOUT emitting a `Reference`/`Analyzer` item or a literal `<Import>` — is rejected unless on the closed
    allowlist. Non-allowlisted `Analyzer` items are rejected by the analysis-phase allowlist alongside
    references. The named control is
    the **analysis-phase reference-rejection** (below): a shipped generator/analyzer is caught by its
    non-allowlisted *reference* AFTER it has already run (EXT4-06: the real build *does* run generators; the
    ban never needs to trust or pre-empt a generator's body, only to reject its non-allowlisted *reference*
    and inspect its emitted *output*) — so a malicious generator fails the gate on its *reference* regardless
    of what its body did at build time, but the gate does NOT prevent that body from executing.
  - **Analysis phase** — the **gate parses syntax-only** (its own in-process `Microsoft.CodeAnalysis.CSharp`,
    pinned+locked in INV-015; `CSharpSyntaxTree.ParseText`, no Workspaces/MSBuild, and **never re-hosts or
    re-executes** any generator/analyzer in its own compilation; R3-I1) over: the **post-target** `Compile`
    items + the emitted generated sources (read from `CompilerGeneratedFilesOutputPath`) + linked `<Compile
    Include>`. **The parse is bound to the build's ACTUAL preprocessor symbols (EXT7-03)**: `ParseText` uses a
    `CSharpParseOptions` seeded with the `DefineConstants` the real build passed to `Csc` (extracted from the
    same build), so an active `#if SYMBOL` branch the real build compiles is NOT dropped as inactive
    disabled-text trivia and thereby made invisible to the allowlist predicate — a default-symbol parse would
    leave executable content inside a live `#if` unscanned (the EXT7-03 parse-differential). It asserts (i) **no executable content** via a **CLOSED-ALLOWLIST Roslyn predicate**
    (default-deny by construction, EXT4-01): the ONLY permitted syntax nodes are namespace/type/member
    **declarations that carry no body, no initializer, and synthesize no members** — **any** other node fails
    closed. This rejects, non-exhaustively (the allowlist is the contract, not this list): `BlockSyntax`,
    `ArrowExpressionClauseSyntax` (expression-bodied member/property/indexer/ctor), `EqualsValueClauseSyntax`
    (field/property initializer), `GlobalStatementSyntax` (top-level statements), constructor /
    static-constructor / destructor / conversion-operator bodies, anonymous-function bodies, AND the
    body-free-but-executable forms the v5 4-node denylist MISSED (EXT4-01): `extern`/`[DllImport]` methods
    (execute native code with no C# body), **primary constructors** (`class C(int x)` / `struct` / positional
    `record R(int X)` parameter lists), and record positional-member synthesis. A **meta-test enumerates the
    allowed declaration-kind set** so a newly-added executable/synthesizing C# form fails closed by default
    rather than silently passing (NOT the v4 method-body denylist that missed these; R3-B7/EXT2-04-scanner/EXT4-01); the
    policy-interface base-list disjunct stays DROPPED until such interfaces exist (its return tracked by the
    DD-003 parent-INV-036 note); and (ii) the resolved non-framework reference set is an **injectable
    allowlist** (production binds the empty `const` while BLOCKED — so the allow-branch is exercised by a
    fixture binding a non-empty allowlist, R3-M-scanner; **"non-framework"** = references NOT under the
    resolved shared-framework pack dirs and not `FrameworkReference`/implicit-SDK, matched by **exact
    assembly identity**, not substring). A first-party binary DLL is production even behind a skeleton `.cs`.
  The **injectable closure-target set** (production binds the `src/Corrected.*` constant; tests bind a
  fixture path — mirroring INV-013) gives the fixtures somewhere to point. The shipped closure **overrides
  path exemption** (`**/*.Tests/**` exempt ONLY for an independent test project not referenced/linked by a
  shipped project). A new top-level `src/` package is caught as production by the **parent's path-scoped
  INV-036 CI check** (deny-by-default, `phase-0-1-worker.md` INV-036 detection) — INV-011 here is the
  **complementary closure scan** for linked/generated/binary content that evades path classification, not
  the path enforcer (EXT4-09).
  **Vacuous-vs-uncomputable discriminator (R3-I3/EA-004)**: the injected target set resolving to **zero
  project files** → the "no production surface (src/ empty)" **PASS** + a distinct stdout notice (INV-012);
  a resolved target whose restore / `dotnet build` / `-getItem` extraction returns **nonzero exit or
  unparseable output** → **closure-uncomputable → fail-closed** (a distinct state, never conflated with the
  vacuous pass). **Because `src/` is empty today the real closure is empty (AP-002 residual, RS-DC-06/SA-2)**;
  a **scaffold shipped fixture project** exercises the real path (scanned as a closure ONLY when injected,
  so it never trips the real ban). Fixture location: **under `gate/Corrected.Gate.Tests/fixtures/
  shipped-closure/**`** so it inherits `gate/NuGet.Config`'s `<clear/>` single source (a `test/fixtures/**`
  home would be outside the isolated config scope → ambient-source restore, the TB-004 hole; R3-I2), each
  fixture project with its **own committed `packages.lock.json`**. The binary case = a `<Reference Include>`
  + HintPath to a **first-party DLL that is BUILT from committed C# source by the same pinned out-of-process
  build** (or, if checked in, under a **SHA-256 pin + regeneration procedure + digest test** — a committed
  opaque binary is otherwise the very TB-004 hazard the scanner catches; R3-M-scanner); the generated-source
  case = a committed `IIncrementalGenerator` fixture project that sets `EmitCompilerGeneratedFiles=true`; the
  fixture asserts the generator's *reference is rejected* and its *emitted output is scanned* — NOT that its
  build-time execution was prevented (EXT5-02, accepted CI-runner-scoped residual).
- **Boundary**: production/carrier partition (ARCHITECTURE §Production-surface partition, amended).
- **Guards against**: AP-005, AP-004, AP-002, AP-015.
- **Enforcement**: gate precondition — skeleton-only passes; each of {one-real-method, **constructor body**,
  **static-ctor body**, **conversion-operator body**, **expression-bodied property/indexer**, **top-level
  statement**, **`extern`/`[DllImport]` method (EXT4-01)**, **primary constructor `class C(int x)` (EXT4-01)**,
  **positional `record R(int X)` (EXT4-01)**, nested `src/**/*.Tests` policy, linked `gate/**` source,
  field/property initializer, generated source (via the committed generator fixture's emitted output),
  binary first-party reference, **non-allowlisted `Analyzer` item (via the post-target `-getItem` query; EXT6-02/EXT7-03)**,
  **committed custom `<Target>` / `BeforeBuild` / `<Exec>` (EXT6-02)**, **committed `<UsingTask>` or non-SDK
  `<Import>` (EXT6-02)**, **committed response file `Directory.Build.rsp`/`MSBuild.rsp` (EXT7-03)**,
  **non-default `<Sdk>`/`Sdk=` identity (EXT7-03)**, **an MSBuild property function in a project file (EXT7-03)**,
  **a `build`/`buildTransitive` package asset in the locked `packages.lock.json` (EXT7-03)**, **executable
  content inside a live `#if` branch active under the build's `DefineConstants` (EXT7-03 parse-differential)**}
  FAILS; the **SDK-default `Analyzer` allowlist PASS fixture** (a vanilla skeleton project does NOT false-fail, EXT7-03);
  the allow-branch fixture (non-empty injected allowlist: allowed ref → pass, one-identity-char-off →
  fail); a vacuous-scan-visibility test (zero project files → pass + notice); a **closure-uncomputable →
  fail-closed** test (a fixture target whose locked restore is forced to fail / a malformed `.csproj`); a
  loaded-version assertion for the pinned in-process `Microsoft.CodeAnalysis.CSharp`, and a `dotnet msbuild
  -version` assertion binding the out-of-process SDK MSBuild to the pinned `10.0.302` feature-band — band-membership of the
  resolved version per INV-016's `latestPatch` predicate, NOT exact-only equality (`10.0.302` is the
  floor/requested; EXT5-04) — NOT a loaded-assembly check.
- **Test approach**: integration · **Risk**: high

### INV-012: Actionable, host-clean, valence-correct blocker/status message — visible on the GREEN path
- **Type**: must · **Category**: functional
- **Statement**: the gate-side message (the parent INV-043 self-explainer, DD-004) distinguishes
  **valence**: a **consistent BLOCKED is a PASS** — "PASS: readiness gate consistent; BLOCKED is the
  expected Phase-0.1 state (P2/P3 not yet dischargeable)" — vs a **violation is a FAIL** naming the
  offending precondition (RS-UX-05). **Green-path visibility (RS-290/UX-001, HIGH)**: because `dotnet test`
  swallows passing-test output, the banner + the INV-011 "no production surface (src/ empty)" notice + each
  unsatisfied precondition's rendered reason MUST be emitted to **stdout of the canonical gate command** — the committed runnable script `<GATE-SCRIPT>`
  (INV-014/INV-017, EXT6-01) that runs `dotnet test`, then renders the banner/notice/reasons and returns the
  final exit code — not merely as an xUnit assertion; a test asserts the banner text appears on the **script's**
  stdout, so an operator who sees green also sees the valence (EXT6-01: the renderer is a step in `<GATE-SCRIPT>`
  AFTER `dotnet test`, NOT inside the swallowed xUnit suite). It renders **each INV-006 reason-taxonomy category distinctly** (RS-291/
  UX-002): `validator-deferred` → "expected while BLOCKED; not yet dischargeable" (with the INV-009/010
  discharge pointer); `evidence-absent`/`evidence-malformed`/`evidence-schema-incomplete` → "degraded
  environment / pre-migration — NOT a code regression; restore the committed evidence / apply the DD-003
  migration and re-run"; `evidence-refutes` → "real regression — the evidence contradicts the claim". A
  first-run env-failure (no network per EA-005, wrong SDK) is mapped to "environment prerequisite unmet
  (EA-005) — not a gate verdict" where feasible (RS-295/UX-007). Emitted paths **match a repo-relative
  allowlist regex** (`^[\w./-]+$`, no leading `/`/drive) and contain no `Environment.UserName` (guarded for
  short/empty; RS-T-15/PRH-005).
- **Enforcement**: CI test — assert the PASS-BLOCKED banner AND the FAIL-violation text separately, **each
  on the documented command's stdout**; each unsatisfied id + rendered reason present; all reason-taxonomy
  renderings asserted separately; the allowlist-regex + username checks.
- **Test approach**: unit + integration (stdout of the documented command) · **Risk**: medium

### Group E — Bootstrap, wiring, operator surface, supply chain

### INV-013: Conditional green-from-clean, bound to this run; real-probe degraded-env test
- **Type**: must · **Category**: data-integrity
- **Statement**: the suite passes from a clean checkout (clone + **`rm -rf spikes/dafny-compat/out/`** —
  the correct path; there is no top-level `out/`, and that tree is gitignored/not-committed so a fresh
  clone is already out-clean, EXT2-11) with the committed block (Stage A: P1 `evidence-schema-incomplete`
  false, P2/P3 false → BLOCKED consistent; post-Stage-B: P1 true, P2/P3 false → BLOCKED), **conditional on
  required tracked evidence + pinned tooling being present** (committed ADR + canonical evidence sample +
  `route-a.json`; pinned SDK; YamlDotNet + analysis toolchain + test-host lock). In a degraded env where a
  declared-`true` precondition's evidence is unavailable, the probe returns typed `false` and the gate
  **hard-fails closed** — the correct outcome, not a false green (RS-UX-01/EXT-07). Every check binds to
  **this run's** inputs (never prior-run roots/`out/current`). The degraded-env test drives the **real**
  probe via an **injectable repo-root parameter** (structurally **test-only** — production binds the pinned
  constant, so the parameter is not a production substrate-swap seam, RS-271/AP-003) pointed at a temp tree
  copy with the evidence removed; the test asserts the resulting fail reason is exactly `evidence-absent`
  (not `schema-missing`, so the copy must faithfully include schema/registry/route-a.json/linter, RS-271/
  AP-010). The probe **holds no process-global state** (reads its root solely from the injected parameter;
  a test asserts it touches neither `Directory.GetCurrentDirectory()` nor ambient env), so it is
  parallel-safe with the INV-006 test — no serial collection is needed (dropping the v3 self-contradictory
  serialization, RS-270).
- **Guards against**: AP-021, AP-010, AP-019, AP-003.
- **Enforcement**: gate precondition — the from-clean run (correct `rm` path); a no-`out/`-subject test;
  the injectable-repo-root degraded-env test (real probe, temp copy, asserted `evidence-absent` reason,
  no-process-global-state assertion). · **Test approach**: integration · **Risk**: high

### INV-014: The documented command provably runs a non-zero test set (guard OUTSIDE the suite)
- **Type**: must · **Category**: functional
- **Statement**: the aggregator filename is a **single referenced constant** `<AGGREGATOR>` used by every
  site (INV-006 Entry, the gate script, INV-017 CI, ARCHITECTURE `test_via`, the README + AGENT_CONTEXT doc
  homes) — its value is `gate/Corrected.Gate.slnx` **iff** the INV-014 pre-flight proves `.slnx` builds on
  SDK 10.0.302, else the classic `.sln` fallback (OQ-A#2; resolving it updates the one constant, not ~6
  literals; RS-250/RS-A-02/RS-UC-08). **The canonical operator + CI command is the committed runnable script
  `<GATE-SCRIPT>` (`gate/run-readiness-gate.sh`, EXT6-01)** — a second single referenced constant — which
  runs `dotnet test <AGGREGATOR> --logger "trx;LogFileName=gate.trx"` from the repo root on a clean checkout
  (the logger is **baked in** so the counted-execution assertion sees the same argv the script runs), THEN
  validates the TRX (executed-count floor + named-fixture outcomes), renders the INV-012 status
  banner/notice/reasons to stdout, and **returns the final gate exit code**. **The exit-code contract is a
  defined state machine (EXT7-02)**: the script captures `test_rc` from `dotnet test` WITHOUT `set -e`
  short-circuiting (so a nonzero `dotnet test` does NOT skip the renderer), then computes `trx_rc` from the
  executed-count/named-fixture guard, **ALWAYS renders the INV-012 status** (the PASS-BLOCKED banner on green,
  the FAIL-violation text on any failure — INV-012's both-paths visibility depends on the renderer running
  regardless of `test_rc`), captures `render_rc`, and exits **`0` iff `test_rc==0 && trx_rc==0 &&
  render_rc==0`, else non-zero** — so a green `dotnet test` with a failed TRX guard (or a swallowed nonzero
  test) can NEVER produce a zero exit. The doc homes (README +
  AGENT_CONTEXT) document `<GATE-SCRIPT>` as the command to run, and the AP-020 verbatim test executes THAT
  script (not bare `dotnet test`, which swallows the banner); ARCHITECTURE's `test_via` is reconciled to
  `<GATE-SCRIPT>` (the v3 `--logger trx` mismatch, RS-251, corrected). This puts INV-012 (banner on the
  command's stdout), INV-014 (counted execution), and INV-017 (the runnable script) onto ONE canonical
  command, resolving the v7 three-way split (EXT6-01). A step **outside the discovered suite** (the
  reference-CI lane / a standalone runnable script) parses the TRX and FAILS on zero discovery or a
  below-floor executed count, and asserts the specific fixtures (INV-005 rows, INV-008 P1 cases, the
  partition/bypass, the committed-state test) each `Passed` — so dropping `Corrected.Gate.Tests` from the
  aggregator cannot silently green the run (RS-UX-02/RS-RT-10). **The guard's own fail-closed branch is
  tested (RS-252)**: committed synthetic TRX fixtures {zero-discovery → guard exits non-zero; below-floor →
  non-zero; happy → zero} plus a run of the guard against this run's real `gate.trx`. **Beyond the guard, the
  WRAPPER's combined-exit + always-render contract is tested at the script level (EXT7-02)** with **five**
  fixtures driving the outer script over a stubbed `dotnet test` + TRX: {nonzero-test + valid-TRX → script
  exits non-zero AND renders the FAIL text; zero-test + bad-TRX (zero-discovery/below-floor) → non-zero AND
  renders FAIL; missing-TRX → non-zero AND renders FAIL; **renderer-nonzero (zero-test + valid-TRX but the
  renderer step exits non-zero) → script exits non-zero AND a shell-owned fallback FAIL line is emitted even
  though the renderer could not (EXT8-01 — this forces the `render_rc` term, which the prior four fixtures
  left unenforced)**; happy (zero-test + valid-TRX + renderer zero) → zero AND renders the
  PASS-BLOCKED banner} — the self-tests exercise the enclosing script's combined result across ALL THREE of
  `test_rc`/`trx_rc`/`render_rc`, not only the guard in isolation. And a **meta-test**
  reconciles the expected-fixture-name `const` set against the enumerated INV-005 rows / INV-008 cases
  (count + names) so dropping a fixture from both silently is caught. The doc home is a **dedicated fenced
  `## Running the readiness gate` section** (mirroring the spike; a markdown table cell cannot hold a fenced
  block, RS-292/UX-003) in the named file(s), and the AP-020 verbatim test parses the fenced command from
  that section; the fenced command is `<GATE-SCRIPT>` (EXT6-01 — the canonical gate command, NOT bare `dotnet
  test`), and if two doc homes exist each is verbatim-tested byte-for-byte against `<GATE-SCRIPT>` (RS-292/
  UX-004). `<AGGREGATOR>` extension (the solution the script runs) is proven by the pre-flight restore/build
  test (RS-A-02/RS-UC-08).
- **Guards against**: AP-020, AP-013, AP-011, AP-018, AP-002.
- **Enforcement**: CI test / the reference-CI lane parsing TRX (executed-count floor + named-fixture
  outcomes) + the guard-self-tests + the fixture-name meta-test + the `<AGGREGATOR>` pre-flight + the
  documented-command verbatim test. · **Test approach**: integration · **Risk**: high

### INV-015: Pinned + locked YAML parser AND analysis toolchain AND test toolchain
- **Type**: must · **Category**: security
- **Statement**: `YamlDotNet` is pinned `18.1.0` (never `< 5.0.0`); the **in-process analysis toolchain**
  used by INV-011's syntax parse is `Microsoft.CodeAnalysis.CSharp` (+ its transitive
  `Microsoft.CodeAnalysis.Common`) pinned + locked with a **loaded-version assertion** — and this is the
  ONLY analysis package (R3-I1/EXT2-10): **no `Microsoft.Build.*` PackageReference exists**, because the
  closure build runs **out-of-process** on the pinned SDK's own MSBuild (a `Microsoft.Build.*` package is
  the in-process MSBuild API INV-011 forbids, and nothing from it would ever load into the gate process, so
  a "loaded-version assertion" for it is meaningless). The out-of-process MSBuild/SDK identity is asserted
  via a **process invocation** (`dotnet msbuild -version` / `dotnet --version` resolving to a member of the
  pinned `10.0.302` feature-band — band-membership per INV-016's `latestPatch` predicate, not exact-only
  equality; `10.0.302` is the floor/requested, EXT5-04), NOT a loaded-assembly reflection check. **Version-skew guard (R3-I2)**: the gate's own pinned Roslyn parses
  sources compiled by the SDK's (possibly newer) Roslyn; the pinned `Microsoft.CodeAnalysis.CSharp` feature
  level must be **≥ the SDK's bundled Roslyn** (bump-coupled to the SDK pin under DD-006), or the gate
  parses with `LanguageVersion.Latest` — with a fixture asserting a source using the SDK's newest supported
  C# feature parses without throwing (else a legitimate newer `src/` file spuriously fails closed). AND the
  gate's **test-host / xUnit / runner** are pinned + locked (VSTest — `Microsoft.NET.Test.Sdk` + `xunit` +
  `xunit.runner.visualstudio`, matching the spike; RS-A-06). All under `RestorePackagesWithLockFile` /
  `RestoreLockedMode` with a committed `packages.lock.json` (the gate's AND every shipped-closure fixture
  project's own lock; R3-I2); CI asserts the restored versions. **The `<AGGREGATOR>` restore/lock set is
  EXACTLY the four gate projects — `Corrected.Gate`, `Corrected.Gate.Kernel`, `Corrected.Gate.Tests`,
  `Corrected.Gate.Lint` (EXT7-05)**: a meta-test asserts solution membership equals that four-element set
  (so the isolated Kernel project is not accidentally omitted and depended on only transitively), and each
  has a committed lock restored in locked mode. A documented **bump affordance**: any
  YamlDotNet/Roslyn/SDK/test-host bump re-runs the AST-hardening + INV-011 + INV-014 fixtures under the
  spike's DD-006 procedure (RS-UC-05).
- **Guards against**: AP-015. · **Enforcement**: hash/lock verification — committed lockfile(s) +
  locked-mode + a loaded-version assertion for `Microsoft.CodeAnalysis.CSharp`/YamlDotNet/test-host + a
  `dotnet msbuild -version` process assertion for the out-of-process SDK MSBuild + the version-skew
  parse fixture. · **Test approach**: integration · **Risk**: medium

### INV-016: Repo-root SDK pin, semantically synced, isolated NuGet restore
- **Type**: must · **Category**: data-integrity
- **Statement**: a **repo-root `global.json`** pins the SDK `10.0.302` so the muxer (searching cwd→up)
  selects it for the root invocation. A test asserts the repo-root and `spikes/dafny-compat/global.json`
  are **semantically synced on the load-bearing `sdk.version` field** (parse both, compare the version
  string) — **NOT byte-identical** (the v3 byte-identical clause was unsatisfiable: the spike file is
  `rollForward: disable` with a `"//"` comment + `allowPrerelease`; RS-240/EXT2-06, maintainer decision
  2026-07-25 = semantic sync). `rollForward` and comments are explicitly allowed to
  differ per context, but the **repo-root `global.json` sets `allowPrerelease: false` explicitly** (EXT6-03 —
  the CLI defaults `allowPrerelease` to true outside Visual Studio, so an unqualified `latestPatch` would
  consider prerelease SDKs; the spike's `disable` file is unaffected); both files carry the DD-006 bump affordance. A committed **`gate/NuGet.Config` with
  `<clear/>` + a single pinned source** (mirroring the spike) makes the gate restore single-source-isolated
  (else NuGet config merges upward to machine/user sources — a TB-004 hole; RS-A-01/RS-UC-06), and a **CPM
  opt-out** (`gate/Directory.Packages.props` or `ManagePackageVersionsCentrally=false`, with a regression
  test that drops a dummy repo-root `Directory.Packages.props` into a temp copy and asserts the gate still
  restores its inline+locked versions; RS-UC-11) prevents a future repo-root `Directory.Packages.props`
  from capturing the gate's inline `Version=`. A build-time `NETCoreSdkVersion` band-membership assertion — the predicate is **exactly: major `10`,
  minor `0`, feature-band `3xx` (the hundreds digit of the patch == `3`), and resolved patch `>= 10.0.302`**
  (per the roll-forward clause below — NOT exact `== 10.0.302`; `10.0.302` is the floor/requested,
  EXT5-04/EXT6-03) + a from-repo-root `dotnet --version` check cover both the pin and the muxer-resolution
  clause.
- **Boundary**: TB-004.
- **Statement (roll-forward)**: `rollForward` is set to **`latestPatch`** at the repo root (maintainer
  decision 2026-07-25) with **`allowPrerelease: false`**, rather than `disable`, so a security patch is not
  blocked repo-wide (RS-UC-07). **Honest reproducibility claim (EXT6-03)**: the committed lockfile pins
  PACKAGE versions (a reproducible restore) but does NOT make two different SDK patches byte-identical —
  `latestPatch` selects the **highest installed qualifying patch** (Microsoft's global.json docs describe
  exactly this and recommend `disable` when strict SDK↔lockfile lockstep is required:
  https://learn.microsoft.com/en-us/dotnet/core/tools/global-json). This gate deliberately trades exact-SDK
  reproducibility for security-patch availability, **bounded** to feature-band 3xx `>= 10.0.302` with
  prerelease excluded; the build-time band assertion records the actual resolved SDK so the receipt cites
  what ran. ARCHITECTURE **TB-004 is amended** to record this `latestPatch` exception for the
  repo-root `global.json` (the spike's `disable` invariant is unchanged; RS-240/EXT2-06/DC-MED). The
  build-time assertion checks membership of the resolved `NETCoreSdkVersion` in the pinned patch-band, not
  exact-only equality, consistent with `latestPatch`.
- **Blast radius + ownership (RS-242/UC-MED/UX-008)**: adding a repo-root pin changes SDK resolution for
  the WHOLE repo; the spec requires a prominent install note (README + AGENT_CONTEXT: "the repo now
  requires SDK 10.0.302 / feature-band 3, pinned repo-wide") and an **ownership/removal note** per
  repo-global artifact (`global.json`, `.gitattributes`, the CI workflow): who owns it, what depends on it,
  what breaks on carrier removal or when `src/` production later inherits the pin.
- **Guards against**: AP-015, AP-016. · **Enforcement**: committed repo-root `global.json` + `gate/NuGet.Config`
  + CPM opt-out + the semantic-sync test + the CPM regression test + the build-time SDK assertion + a test
  that INV-001 fails **closed with a named reason** if the `.gitattributes` LF pin is absent. · **Test approach**: integration · **Risk**: high

### INV-017: The gate is WIRED to run from clean in CI via a runnable script (not a grep, not deferred)
- **Type**: must · **Category**: security
- **Statement**: an **executable CI job** runs the gate **from clean** (`rm -rf spikes/dafny-compat/out/`,
  EXT2-11) and gates PRs — landed as part of GREEN, NOT deferred. **The canonical operator + CI command is the
  committed runnable script `<GATE-SCRIPT>` (INV-014/INV-012, EXT6-01)**: it runs `dotnet test <AGGREGATOR>
  --logger trx` from clean, validates the TRX executed-count guard, renders the INV-012 status to stdout, and
  returns the final gate exit code. **CI executes `<GATE-SCRIPT>` DIRECTLY** — the primary from-clean
  execution and the real evidence, NOT merely `Assert.Contains(command, workflow.yml)` (a doc-grep, AP-011 /
  the exact PMB-001 trap; INV-017/RS-253/RT-H6/DC-MED). The **verbatim-behavior assertion** lives in the
  **out-of-suite reference-CI lane** (NOT inside `<AGGREGATOR>`'s discovered suite). **The re-entry sentinel
  `CORRECTED_GATE_INNER` has a defined set/check owner (EXT7-02)**: the OUTER script starts with the sentinel
  **unset** and **the script itself EXPORTS `CORRECTED_GATE_INNER=1` only for its child `dotnet test`
  invocation**; any gate-invoking helper that could run *inside* the discovered suite **checks** the sentinel
  and no-ops when it is set — so the inner `dotnet test <AGGREGATOR>` can never re-trigger the wrapper. The
  reference-CI lane runs the outer `<GATE-SCRIPT>` with the sentinel **unset** (so the outer script executes
  fully — the lane is NOT itself run under the sentinel, which would make the verbatim assertion a no-op);
  **no in-suite xUnit test ever executes `<GATE-SCRIPT>`** (an in-suite test invoking its own enclosing script
  would recurse, EXT6-01), and the doc-home AP-020 tests **compare bytes only**, never execute it. A
  built-but-grep-asserted gate is the PMB-001/PMB-002 deferred-net class. EA-005 notes this job assumes a
  network-connected CI (nuget.org restore); an air-gapped lane needs a committed/cached package store.
- **Guards against**: AP-002, AP-021, AP-011, AP-020. · **Enforcement**: a committed `.github/workflows` gate
  job + its extracted from-clean script, whose **execution** (not just presence) is asserted verbatim from a
  clean checkout (mirroring the spike's DF-001 charter/live pair, the LIVE half). · **Test approach**: integration · **Risk**: high

### INV-018: The gate build is insulated from the spike's build health (build-only; file deps enumerated)
- **Type**: must · **Category**: resource-lifecycle
- **Statement**: the P1 linter reuse (DD-001) does **not** couple the gate build to the spike's restore
  context or build health: the shared linter is **extracted into a Dafny-free, single-TFM (`net10.0`)
  shared library** (`gate/Corrected.Gate.Lint/`) the gate references without importing
  `spikes/dafny-compat/**`'s CPM/`<clear/>`/lock context or its `net8.0` targeting-pack dependency (a
  cross-tree `ProjectReference` would mix two locked-restore contexts + pull the net8 pack; RS-UC-09/
  RS-A-05/RS-DC-10). The extraction carves `AdrLinter` + its transitive type closure (`AdjudicationRecord`
  → `RouteState`/`IncompatibleClass`/`ThreeCellOutcome`/`ProbeStatus`, `RouteClaim`) into the new lib, NOT
  a whole-`Components.cs` reference (which also drags `ManagedLauncher`/`Process.Start` etc.; RS-211/EXT2-10).
  The extracted lib carries the INV-008(c) source-digest registry pin. **Insulation is build-only (RS-280)**:
  the gate retains a *data* dependency on committed spike files — it reads `route-a.json` + the canonical
  sample and pins the extracted-lib digest; these are enumerated as EA-002 pins, and relocating/pruning the
  spike tree fails the gate closed (a stated, not silent, coupling). A carrier test asserts the DD-003 ADR
  `status:` edit keeps the spike's own committed suite (`Inv013AdjudicationTests.cs`, which reads the real
  ADR) green (the edit is tolerated by `ExtractLintBlock`'s unknown-key skipping; RS-280).
- **Guards against**: AP-015, AP-017. · **Enforcement**: gate precondition — the gate restores/builds
  from clean with no dependency on the spike's build succeeding; an isolation test; the spike-suite-still-green
  test after the ADR edit; the enumerated file-dependency pins. · **Test approach**: integration · **Risk**: medium

## Prohibitions

### PRH-001: Never trust `satisfied` without the actual probe verdict AND reference resolution (INV-005).
Detection: fixtures (d)/(f)/unregistered-evidence → Fail. Consequence: a false READY / hidden satisfaction slips.

### PRH-002: No production policy code in the carrier; no `gate/**` source linked into a shipped project (INV-011/PAT-005).
Detection: import-boundary + linked-source scan; the shipped-closure partition. Consequence: the gate becomes production / launders policy into the exempt surface.

### PRH-003: The parser never materializes a tag, anchor, or alias, or an arbitrary type — for the readiness block OR the ADR block (INV-002/INV-008a).
Detection: tag/anchor/alias/multi-doc fixtures on both parses. Consequence: a gadget or parse-differential via a tampered block/ADR.

### PRH-004: The committed readiness block is never READY while any precondition is unmet (INV-006/007).
Detection: the committed-block current-state test. Consequence: production code becomes landable unproven.

### PRH-005: No hand-rolled general YAML parser, and the ADR trust decision never uses the spike's permissive line-scanner (INV-002/INV-008a). The permissive `AdrLinter.Lint` runs only as a redundant cross-check ANDed with the authoritative hardened decision, never as the sole trust source.
Detection: dependency pinned; no bespoke tokenizer; the authoritative decision-field assertions live in the hardened path; a differential fixture proves agreement. Consequence: under-parse of a tampered ADR/block.

### PRH-006: No committed `P1.satisfied:true` without a passing carrier re-deriving it (the inverse-partial guard).
Statement: a readiness block asserting `satisfied:true` for any precondition while the carrier's committed-state test is absent/failing is itself a blocking condition — humans/tools must never trust a flipped flag whose enforcement was reverted/not-merged (RS-RT-12). Detection: a cross-check that fails if the block claims a `satisfied:true` the carrier does not re-derive. Consequence: a standing unenforced satisfied claim.

### PRH-007: The recompute never passes a vacuous, plan-shrunk, duplicate-keyed, or content-tampered evidence sample (INV-008a′/a″).
Detection: the (a″) count-aware multiset equality + exactly-one-Route-A-verdict + duplicate-JSON-property rejection fixtures {empty, route-B-only, duplicate entry, duplicate JSON key, tampered manifest FILE, wrong manifest sha}, AND the (a′) compiled `canonical_sample_sha256` fixture {coherently-tampered sample → fails}. Consequence: a forged P1=true from a stripped `per_probe_results` (round-2 RS-201/EXT2-04) or a coherently-rewritten frozen sample (round-3 R3-B2).

## Boundary Conditions

### BND-001: The readiness block (committed, tamperable markdown) — TB-006.
Input from the committed `phase-0-1-worker.md`. Validation: INV-001/002/003/005. Failure mode: fail-closed.

### BND-002: The YAML parser + analysis toolchain + SDK — TB-004.
Input from the YamlDotNet + Roslyn/MSBuild + test-host packages + the .NET SDK. Validation: INV-015/016; AST pre-validation + caps (INV-002). Failure mode: fail-closed.

### BND-003: The ADR + evidence the P1 probe reads (committed, tamperable) — TB-006.
Input from `docs/adr/ADR-0001-*.md` (+ the compiled-`const` ADR registry), the pinned canonical evidence sample, `route-a.json`. Validation: INV-008 (hardened ADR parse incl. decision fields, pinned canonical path, compiled schema-integrity anchor, cardinality-guarded recompute, terminal-rule supersession over a **registry set-equality** — R3-B4, an unregistered on-disk `adr_lint` block fail-closes; supersedes RS-204, trust-root pin). Failure mode: fail-closed (P1 → typed false).

## STRIDE Analysis

### STRIDE for TB-004 (YamlDotNet + analysis toolchain + SDK)
- Spoofing/Tampering: pin+lock+CI-verify YamlDotNet + Roslyn/MSBuild + test-host (INV-015); repo-root SDK pin + semantic-sync + build-time assertion + `<clear/>` NuGet.Config (INV-016).
- Repudiation: restored versions in the lockfile/build evidence; loaded-version assertions.
- DoS: file + block byte caps (INV-001); incremental scalar/node/alias caps + recursion cap (INV-002).
- EoP: reject ALL tags/anchors/aliases in AST pre-validation, no `WithTagMapping`, no `object` target, never `<5.0.0` (INV-002/PRH-003); out-of-process pinned-SDK build (no ambient MSBuild resolution, INV-011).

### STRIDE for TB-006 (readiness block + ADR/evidence intake/tamper)
- Spoofing: duplicate readiness block / duplicate ADR route-claim → INV-001/INV-008a.
- Tampering: injected keys, flipped `satisfied`, unrecognized `schema_version`, tag injection, forged ADR claim, forged decision fields, repointed/forged evidence, stripped `per_probe_results`, relaxed evidence schema, prose↔machine status split → INV-002 + INV-005 cross-check + INV-008 (decision-field assertions, pinned canonical path, compiled schema-integrity anchor, cardinality-guarded recompute, prose↔machine check).
- Repudiation: the verdict is a pure function of the supplied block + probed artifacts.
- Information disclosure: host-clean message (INV-012 / PRH-005).
- DoS: bounded file + block (INV-001); the ADR **registry is a compiled-`const` authoritative set** matched by **set-equality** against on-disk `adr_lint` blocks (discovered via the INV-001-D column-0-in-fence discriminator). **R3-B4 SUPERSEDES RS-204**: an unregistered on-disk `adr_lint` block fail-closes (rather than being ignored) — this deliberately accepts an injected-ADR DoS as the price of closing the supersession bypass, and the DoS is bounded because it is a **loud, clearly-actionable fail** (remove the junk file or bump the registry const in a review-gated commit), not a silent block.
- Elevation of privilege: false READY / satisfied-without-resolved-evidence / forged P1=true (vacuous recompute or decision-field bypass) / missed supersession → INV-004/005/008 fail-closed; reject branches fixture-exercised (INV-007/INV-008/PRH-007).

## Environment Assumptions
- **EA-001**: SDK pinned `10.0.302` via a repo-root `global.json` (`rollForward: latestPatch` — maintainer
  decision 2026-07-25, recorded as the TB-004 exception; INV-016); gate targets `net10.0` under locked
  restore. — Wrong → drift (AP-015)/fail-closed restore.
- **EA-002**: the committed P1 evidence is present + readable (the extracted linter lib source, `ADR-0001`,
  the pinned **canonical** evidence sample, `route-a.json`) — a stated *data* dependency on the spike tree
  (INV-018 insulation is build-only; relocating the spike tree fails the gate closed). Full git *history* is
  NOT required by the carrier (the probe reads committed files by digest) — this **diverges from parent
  EA-016** (which requires a full-history clone for its P1/P2 binding); the DD-003 migration reconciles
  EA-016 to the carrier's file-digest model (RS-A / assumptions). Note: P1 resolves **true only
  post-Stage-B**; the pre-migration ADR lacks the machine `status:`/supersession fields, so P1 is
  `evidence-schema-incomplete` false until the flip commit. — Wrong → P1 typed false, gate hard-fails
  closed (INV-013).
- **EA-003**: runner RID `linux-x64` (spike EA-002) for any RID-bound P3 attestation. — Wrong → non-attesting.
- **EA-004**: the shipped `src/` packages are absent or skeleton-only and their build graph is inspectable
  for the closure scan; while `src/` is empty the scan is fixture-driven + a scaffold project (INV-011).
  The **vacuous (zero project files → pass + notice)** and **uncomputable (a resolved target whose
  restore/build/`-getItem` returns nonzero/unparseable → fail-closed)** states are distinct, discriminated
  operationally (INV-011/R3-I3), never conflated. — Wrong → closure uncomputable → fail-closed.
- **EA-005**: the first `dotnet test` on a clean clone performs an implicit restore requiring **network/DNS/
  TLS to nuget.org and a roughly-synced clock** (or a committed/cached package store); the gate verdict
  itself is clock-free (RS-A-03). ADR-0001 rejected vendoring, so nuget.org is a permanent dependency; the
  INV-017 wired-from-clean job assumes a network-connected CI. — Wrong → from-clean restore fails (not a false green).
- **EA-006**: the `.NET 8` targeting pack is NOT required (INV-018 extracts a single-TFM `net10.0` lib
  rather than referencing the spike's multi-TFM project; RS-A-05). (The v3 "spike is red-from-clean"
  justification is STRUCK as a false fact — the spike is green-from-clean 274/274; the extraction rationale
  rests on the net8-pack + lock/CPM-context grounds only, RS-A/assumptions.) — Wrong → build break.
- **EA-007**: the gate resolves repo-relative path constants via the named repo-root sentinel (the directory
  containing repo-root `global.json` + `.correctless/`), case-sensitively; a POSIX case-sensitive FS is
  assumed (RS-A-04/RS-A-12/RS-264). The from-clean *procedure* (`git clone`, `rm -rf`, `env -i bash`) assumes
  a POSIX/Linux host even though the `dotnet test` invocation is cross-platform. — Wrong → cwd/case-dependent
  fail-closed / non-runnable clean ritual on Windows.
- **EA-008**: `<AGGREGATOR>` `.slnx` works on `10.0.302` (proven by the INV-014 pre-flight) OR the `.sln`
  fallback is used (RS-A-02); the single constant flips atomically (OQ-A#2). — Wrong → documented command unrunnable.

## Design Decisions (resolved)
- **DD-001**: the P1 linter is reused by **extracting `AdrLinter` + its transitive type closure into the
  Dafny-free, single-TFM `gate/Corrected.Gate.Lint/` lib** (per INV-018) the gate references — NOT a
  cross-tree `ProjectReference` to the spike (which mixes lock/CPM contexts + net8 pack; RS-UC-09/RS-A-05)
  and NOT a whole-`Components.cs` pin (RS-211/EXT2-10). Source-digest pinned via an append-only registry.
- **DD-002**: pinned committed paths — P1 evidence = the **canonical** sample
  `spikes/dafny-compat/evidence/samples/run-report.canonical.sample.json` (asserted equal to the ADR-cited
  path; NEVER the variance `run-report.sample.json`, RS-210); P2 `test/manifests/phase-0.0-completion.json`;
  P3 `test/attestations/inv010-determinism.json` (durable + provenance-bound, INV-010).
- **DD-003 (the atomic P1 flip — a STAGE-PARTITIONED normative migration manifest)**: a machine-readable
  **migration manifest** committed in the carrier lists **every exact parent-spec anchor by `{id, file}`**;
  per EXT5-01 each **digest row** stores `stage_before_sha256` + `stage_after_sha256` (B1's evidence is a
  **structural row** instead — no after-digest, EXT9-01) where the **after-digest IS the
  replacement** (its content hash — no replacement string is stored) and the **stage is NEVER stored per row**
  (it is derived globally from committed `P1.satisfied`; EXT5-01/EXT6-04). **The manifest is a VALIDATOR /
  guard, and the exact after-bytes are made RECOVERABLE (EXT8-04)**: for every DETERMINISTIC parent-spec
  anchor, the exact post-migration span is committed AT STAGE A as a **canonical after-span fixture** under
  `gate/Corrected.Gate.Tests/manifests/after-spans/` (a Stage-A artifact, site A5), and `stage_after_sha256`
  is derived FROM that committed fixture — so a fresh Stage-A checkout carries the real preimage (recoverable),
  and the digest is NOT self-authored circularly at Stage B (a round-7 gap, EXT8-04: prose alone was not a
  byte-exact preimage). **The gate's guarantee is atomic accepted TREE STATE (EXT9-06)** — the P1-derived
  stage selector + the mixed-set fail-closed guard prove any GATED tree is all-before (Stage A) or all-after
  (Stage B), never mixed, so the `P1.satisfied` flip + every anchored edit + the ADR B13 edit are consistent
  in any tree the gate accepts. The gate checks TREE state, not commit history, so it does NOT claim the flip
  occupied literally one commit (a red-partial-then-green-complete PR is indistinguishable at HEAD; per-commit
  history-aware enforcement is out of scope, EXT9-06) — the honest guarantee is that an accepted tree is never
  partially-migrated. **B1's `P1.evidence` field is the sole exception to digest-pinning**: its value is the
  gate's OWN registered test id, assigned at GREEN and not knowable at Stage A, so B1 is validated STRUCTURALLY
  (`satisfied:true` AND `evidence` equals the id the gate registers — INV-005 cross-check) via a **structural
  manifest row** (`kind:"structural"`, EXT9-01), NOT a digest row. A gate enforces **set-equality**
  between the manifest and the current-state anchors it discovers **in `phase-0-1-worker.md`** (an omitted or
  stale site fails; RS-222/RS-223/EXT2-03/R3-I4). **The `docs/adr/ADR-0001` `adr_lint` change (B13) is
  EXPLICITLY OUT of this anchor scan (EXT6-04)** — it is not a current-state anchor; its Stage-B edit (adding
  `status`/`supersedes`/`superseded_by`) is verified separately by INV-008's own ADR parse (the terminal rule
  + compiled-registry set-equality), so the manifest's discovery scope is parent-spec anchors ONLY. **The manifest is the canonical
  parent-anchor site list** (the B13 ADR edit is a separately-named Stage-B obligation, NOT a manifest row
  — EXT7-06) — the Metadata `Impacts` line and the "Packages Affected → parent spec" bullet reference it
  and do NOT re-enumerate; a **meta-test asserts Metadata `Impacts` and Packages-Affected each contain the
  manifest REFERENCE and maintain NO local site list** (EXT4-05: there is ONE list — the manifest — not three
  to compare; v4's failure mode was Metadata + Packages-Affected listing INV-043 while the manifest omitted
  it, so the test asserts absence-of-local-list + presence-of-reference, not list-triple-equality).
  - **Stage-A sites (become true when the carrier LANDS — carrier-existence, not the P1 flip; R3-B5/EXT2-06)**:
    (A1) parent **OQ-002** — close ONLY its **built-carrier half** ("test/build-gate carrier is open" →
    closed); its **contract half** was already DISCHARGED 2026-07-24 (parent:~1633) and its **production-test-
    project / entrypoints / reference-provenance (P3) residual** (parent:~170) stays OPEN — A1 must NOT close
    OQ-002 wholesale (EXT4-08); the stale parent "no entrypoint YAML exists yet — see OQ-002" (parent:~247)
    is corrected in Stage A (site A6) and is in the finite literal scan below (EXT4-08/EXT5-03); (A2) parent **RS-002** ordering (phrased
    commit-level: "carrier + reject corpus proven green at/before the discharge commit", NOT "in a prior PR"
    — so the one-PR option OQ-A#4 keeps open is not retroactively forbidden; R3-M/DD-003-R3-6); (A3) the
    parent **enforcement-carrier prose** "these mechanisms are specified but unhomed / that carrier must
    exist before the gate is meaningful — its absence is itself a blocker" (parent:~168-179) → homed; (A4)
    the parent **clean-checkout RS-004 block** stating "the repository currently commits
    `spikes/dafny-compat/out/`" (parent:~155-166) → corrected to "gitignored / not committed" and the
    integration `rm -rf out` wording → `rm -rf spikes/dafny-compat/out/` (R3-I4/EXT2-11); (A5) placement of
    the **machine-readable current-state anchors** (below) — ID-only markers plus the committed manifest
    (whose per-anchor `stage_before_sha256` is the pre-flip digest); with `P1.satisfied` still `false` the
    gate resolves to **Stage A** and verifies every span against its `stage_before_sha256`, so Stage A is
    self-consistent (EXT5-01); (A6) the parent's **already-stale current-state literals** — the INV-002
    integration-contract line "no entrypoint YAML exists yet — see OQ-002" (parent:~247), the sibling
    "entrypoint YAML TBD (`/carchitect`)" (parent:~471), and "Flagged for the ARCHITECTURE.md component
    table" (parent:~1610) — corrected to match today's tree (entrypoint YAML exists at ARCHITECTURE.md:61
    since `/carchitect` 2026-07-24; the carrier is now homed). These are current-state truths **already true
    today, independent of the P1 flip**, so they are Stage A, NOT Stage B (EXT5-03); all three are also in the
    finite literal scan below.
  - **Stage-B sites (the P1 discharge — consistent in any gated tree; atomic TREE STATE, not per-commit, EXT9-06)**: (B1) the readiness block
    `P1.satisfied:false→true` + `P1.evidence:null→<registered id>` (the evidence field validated STRUCTURALLY,
    not digest-pinned — the id is assigned at GREEN; EXT8-04); (B2) the INV-002 kernel **signature** (single-arg `blockText` →
    two-arg `(ReadinessBlock, probeResults)`); (B3) the INV-002 **semantic** prose "the pure decision
    function re-derives the discharge / every probe runs" (carrier INV-004 forbids — kernel pure, orchestrator
    probes); (B4) **MOVED to Stage A (site A6, EXT5-03)** — the stale INV-002 "no entrypoint YAML exists yet" line is a current-state correction already true today (entrypoint YAML exists), NOT a P1-flip truth, so it is no longer a Stage-B site;
    (B5) the INV-002 enforcement **"a separate test asserts the committed file currently parses to
    BLOCKED-all-false"** (parent:~240) → post-flip P1=true (R3-I4 — distinct from B2/B3/B4); (B6) parent
    **INV-003** BOTH the Statement and Enforcement-(b) DESIGN/ARCHITECTURE-table clauses → "ARCHITECTURE
    machine-readable production-assembly block authoritative; route-a.json machine source; DESIGN
    publication-scoped/excluded"; (B7) the stale parent INV-003 "backed by a schema-valid terminal
    adjudication record" clause (DF-002 made `adjudication_record_id` optional; block carries `null`); (B8)
    parent **INV-034** "Route A pending DF-002" → discharged; (B9) parent **INV-036/PRH-008** path/policy-
    interface scan → INV-011 shipped-closure predicate (+ the policy-interface-disjunct-returns note) AND its
    **"OQ-002 carrier" actionable-message cross-references** (parent:~1081,1086) → the built carrier (R3-I4);
    (B10) parent **INV-043** enforcement "for the committed **BLOCKED-all-false** block" (parent:~1262) →
    the post-flip `P1=true, P2/P3=false` checklist (R3-I4/codex#7); (B11) parent **EA-016**
    full-history-required → reconciled to the carrier's file-digest model; (B12) parent **OQ-004**
    (supersession format) → closed to the INV-008(a‴) terminal rule; (B13) the **ADR-0001** `adr_lint`
    block gains `status:` + `supersedes`/`superseded_by` (optional keys, INV-002/a‴); (B14) with `P1.satisfied`
    now `true` the consistency gate resolves to **Stage B** and verifies every anchored span against its
    manifest `stage_after_sha256` (EXT5-01 — no inline anchor hashes exist to flip; the manifest is the sole
    digest authority and the stage is P1-derived).
  - **Consistency gate — anchors + finite literal scan (R3-I5/EXT2-08b; EXT5-01/EXT5-03)**: the gate does NOT
    claim to detect arbitrary unmarked paraphrase. It (i) reads **machine-readable anchors** placed in
    `phase-0-1-worker.md` as **PAIRED start/end markers** (EXT4-05 — a self-closing marker wraps nothing, so
    the hashed span was undefined) carrying **only an `id`** — **no inline hashes** (EXT5-01: the committed
    manifest is the SOLE digest authority, so a marker and the manifest can never drift out of sync):
    `<!-- correctless:readiness-current-state:start id="…" -->` … wrapped parent prose …
    `<!-- correctless:readiness-current-state:end id="…" -->`; the gate hashes the **UTF-8 / LF-normalized
    bytes strictly BETWEEN the paired markers, excluding the two marker lines**, and asserts that digest
    equals the manifest pin for that `id` under a **single repo-wide stage**. **The stage is derived
    mechanically from committed state (EXT5-01)** — **Stage A** iff the readiness block's `P1.satisfied` is
    `false` (evidence `null`); **Stage B** iff `P1.satisfied` is `true` (evidence = a registered id, the
    post-flip state) — and the SAME stage is applied to EVERY anchor: at Stage A every span must equal its
    `stage_before_sha256`, at Stage B every span must equal its `stage_after_sha256`. A **mixed set** (any
    span still at its before-digest while `P1.satisfied` is already `true`, or any span at its after-digest
    while `P1.satisfied` is still `false`) **FAILS CLOSED** — this is the partial-migration hazard, caught by
    a **mixed-before/after negative fixture** — naming each disagreeing site (`file:line`, expected vs found;
    RS-UX-08/UX-005). The **exact anchor-ID set, the manifest path
    `gate/Corrected.Gate.Tests/manifests/readiness-migration-manifest.json`, and its closed JSON schema**
    (a **discriminated union on `kind`**, EXT9-01: a **digest row** `{id, file, kind:"digest",
    stage_before_sha256, stage_after_sha256}` naming a canonical after-span fixture, OR a **structural row**
    `{id, file, kind:"structural", stage_predicate}` for B1's evidence field, which has no static after-digest
    since the id is GREEN-assigned — the manifest is the sole hash authority for digest rows) are pinned in that manifest — a Stage-A committed artifact with its own schema test — so the
    RED tests bind a concrete grammar, not a forward reference; and (ii) runs a **file-wide scan for the
    finite set of known stale literals/signatures** (`EvaluateReadiness(blockText)`, `BLOCKED-all-false`,
    "specified but unhomed", "pending DF-002", the wrong `rm -rf out`, and the three already-stale literals
    **"no entrypoint YAML exists yet"**, **"entrypoint YAML TBD"**, **"Flagged for the ARCHITECTURE.md
    component table"** — EXT5-03) so an un-anchored occurrence is still caught. Coverage = "every
    enumerated/anchored site + every known stale literal", NOT "any semantic drift" (R3-I5 downgrade). Placing
    the anchors is Stage-A site A5; the anchors are themselves migration targets (B14).
  - **Staging invariant**: each accepted TREE STATE — the Stage-A tree and the Stage-B tree — is independently
    **green-from-clean** (the guarantee is tree-state, not per-commit history; EXT9-06). Stage A keeps `P1.satisfied:false` so PRH-006 is never tripped and the real probe
    returns `evidence-schema-incomplete` false → `(null,false,false)` consistent-BLOCKED. Whether Stage A + B
    are one PR or two is OQ-A#4 (the partition holds either way). The inverse-partial hazard is guarded by PRH-006.
- **DD-004**: the parent INV-043 self-explaining-BLOCKED need is met **gate-side by INV-012** (now visible
  on the green path, RS-290); the `corrected explain` CLI form is deferred (it cannot live while BLOCKED).
  A doc-home note tells users "readiness explanation currently lives in the gate output; `corrected explain`
  is deferred until BLOCKED clears — DD-004" (RS-UX-09/UX-009).
- **DD-005**: INV-044's evidence-schema **history registry + meta-test is homed in this carrier's `gate/`
  dir** per parent INV-044/INV-036 + ARCHITECTURE — but it is a **separate deliverable** landing with
  Phase-0.1 certification runtime and is **NOT built by this spec**. ARCHITECTURE's `readiness-build-gate`
  `test_via` is amended to mark the INV-044 meta-test a **deferred extension**, not part of this carrier's
  required suite (so no from-clean/`test_via` completeness check demands a not-yet-built test; EXT2-12/
  RS-DC-03). When the gate registry is built it **seeds from / reconciles with the existing spike
  `schema-version-registry.json` rows** (v1, v2) rather than starting empty (RS-UC / assumptions
  dual-home). The *readiness-block* `schema_version` (v1) is validated by INV-002 (a recognized-SET that
  **retains** v1 when v2 is added, so a not-yet-migrated block still parses; RS-UC-04, AP-005).

## Open Questions
- **OQ-A**: bounded maintainer confirmations; none blocks the gate mechanism. (1) **RESOLVED 2026-07-25**:
  `rollForward: latestPatch` at the repo root with semantic (not byte-identical) sync + the TB-004
  exception. (2) `.slnx` vs a classic `.sln` `<AGGREGATOR>` — pending the INV-014 pre-flight on `10.0.302`;
  resolving it sets the one constant. (3) the concrete provenance mechanism for P3's durable attestation
  (signature / SLSA / reference-CI receipt digest — INV-010/TB-003). (4) whether the DD-003 Stage A / Stage
  B milestones land as one PR or two (the two-milestone structure holds either way; may relax parent RS-002).
- **OQ-B (NEW, EXT2-09 follow-through)**: the carrier's readiness/ADR boundary is **TB-006** (maintainer
  decision 2026-07-25); the parent's reserved **TB-005** (source-byte intake, BND-003) is registered
  separately by the parent's own `/cupdate-arch`. This spec + ARCHITECTURE use TB-006 throughout; confirm no
  other in-flight spec already claimed TB-006.

## Packages Affected (monorepo)
- **Test/build-gate carrier** (NEW, NOT shipped): `gate/Corrected.Gate/` + `gate/Corrected.Gate.Kernel/`
  (isolated pure kernel + DTOs, INV-004/EXT6-05) + `gate/Corrected.Gate.Tests/`
  + `gate/Corrected.Gate.Lint/` (extracted Dafny-free linter, INV-018) + `gate/Corrected.Gate.slnx`
  (aggregating **exactly** those four projects)
  + `gate/NuGet.Config` + CPM opt-out + a per-project `packages.lock.json` (one per gate project, INV-015) + `gate/Corrected.Gate/lint-source-registry.json`
  (INV-008c). Exempt surface (`gate/**`). INV-044's history registry is homed here but built later (DD-005).
- **Repo root**: new `global.json` (INV-016) + `.gitattributes` (INV-001) + a `.gitignore` rule for
  `gate.trx`/`TestResults`/local restore output (RS-254/UX-006) + a gate CI workflow + its extracted
  from-clean script under `.github/workflows/` (INV-017).
- **`src/Corrected.*`**: UNCHANGED — skeleton while BLOCKED (INV-011 verifies via the shipped closure).
- **`phase-0-1-worker` (parent spec)** — impacted via the **DD-003 stage-partitioned migration manifest**,
  which is the canonical parent-anchor site list. This bullet holds **ONLY that manifest reference and NO
  local site list** — the per-site A/B enumeration lives SOLELY in DD-003 (EXT8-06: the round-7 A/B listing
  here violated DD-003's own "no local site list" meta-test, which asserts Metadata `Impacts` and this bullet
  each contain only the manifest REFERENCE — R3-I4/EXT4-05). The ADR-0001 edit is described in the ADR bullet
  below (a separately-named Stage-B obligation verified by INV-008), not here. P2/P3 stay false → BLOCKED.
- **`docs/adr/ADR-0001-*.md`** — at Stage B (site B13): a machine-readable `status:` line + **optional**
  `supersedes` / `superseded_by` fields added inside the `adr_lint` block (INV-008a‴; parsed with presence
  bits, absent-allowed so the pre-migration block still parses to `evidence-schema-incomplete`, R3-B1), and
  the prose `Status:` kept consistent with the machine value (INV-008a).
- **`.correctless/ARCHITECTURE.md`** — **APPLIED this session** (not pending): TB-005→TB-006 renumber for the
  readiness boundary (dropped the wrong "Parent BND-003" label; TB-005 reserved for the parent source-byte
  boundary); TB-004 `latestPatch` exception + semantic-sync wording; `readiness-build-gate` `test_via` argv
  reconcile + `rm -rf spikes/dafny-compat/out/` + INV-044 deferred-extension annotation; the machine-readable
  `route-a-production-assemblies` block (EXT2-08 — already present, so INV-008(b) is testable now, not a
  future "add it" step; R3-L2).

## Prerequisite / cross-check notes
- **Allowed-tools (AP-008)**: ordinary TDD under `gate/` + the atomic Edits to `phase-0-1-worker.md`,
  `ADR-0001`, ARCHITECTURE, repo-root config — no Correctless skill writes a new path; no frontmatter change.
- **ARCHITECTURE (APPLIED this session, `/cupdate-arch`-class)**: the TB-006 renumber (+ the TB-005
  reserved stub); the TB-004 `latestPatch` exception + semantic-sync wording; the `test_via` argv reconcile
  + `rm`-path + INV-044 deferral; the machine-readable `route-a-production-assemblies` block. (The
  v3-applied amendments — handler/`test_via`, partition qualifier, PAT-005, TB-004 extension — remain.)
- **Format-pinning (AP-031)**: INV-001/002 pin the readiness-block format to `phase-0-1-worker.md`;
  INV-008 pins the `adr_lint` block (hardened parse, distinct DTO), the **canonical** evidence sample
  (structured `deterministic.` fields + compiled schema-digest anchor), the pinned probe manifest, and
  `route-a.json` (structured set-equality) — all authoritative producers.
