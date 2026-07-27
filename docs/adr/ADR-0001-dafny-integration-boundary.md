# ADR-0001: Dafny integration boundary (accepted)

- **Status**: accepted (DF-002 discharged 2026-07-24 — promoted from provisional:
  the machine-readable block below carries the in-process-selected / Route A /
  COMPATIBLE decision, anchored to the canonical committed sample and validated by
  the INV-013 ADR linter [zero findings]; the DD-007 component-table propagation
  into ARCHITECTURE.md is done)
- **Scope**: DESIGN.md Phase 0.0 **bullets 1–3 only** (the foundational
  boundary-establishment obligations), decided against DESIGN.md v1.13 bullet
  numbering — which, as flagged here, is not stable across design revisions. The
  remaining Phase-0.1-entry gates are now carried by DESIGN §13 (v1.14) under
  STABLE capability ids — `P0-ERASURE-BOUNDARY`, `P0-FINGERPRINT-DETERMINISM`,
  `P0-CLI-DIFFERENTIAL`, `P0-RESOURCE-SEMANTICS`, `P0-EDIT-CLASS-BOUNDARY`, plus
  `DF-003`; the former bullets 8–11 and the exhaustive conformance corpus are
  re-homed to Phase-1 / Phase-0.1-exit. These are the P2 precondition in
  `phase-0-1-worker.md` INV-004.
- **Spec**: `.correctless/specs/dafny-compat-spike.md`
- **Evidence**: committed sample PAIR (QA-006 amendment) —
  `spikes/dafny-compat/evidence/samples/run-report.sample.json` (variance-mode,
  full class-2 equality anchor) and
  `run-report.canonical.sample.json` (canonical run including the suite phase;
  equality masks only the schema-declared suite-status subtree). This ADR's
  verdict citations use the **canonical** sample. Both are regenerated only via
  `spikes/dafny-compat/scripts/regen-sample.sh` (which refuses a dirty tree,
  QA-001) per DD-008; the fresh-run-equality test binds them to reality.

## Machine-readable decision block (INV-013 ADR linter input)

The linter validates POSITIVE compatibility/selection claims as well as
rejection claims against schema-valid terminal adjudication records; a failed
mandatory P03 anchor can never be overridden by ADR prose (OQ-004 gate).

```yaml
adr_lint:
  boundary_decision: in-process-selected   # pending | in-process-selected | rejected
  selected_route: A            # A | B | null
  status: accepted             # OPTIONAL acceptance tier (DD-003 Stage B): pending | accepted | superseded
  supersedes: null             # nullable canonical ADR id | null | absent
  superseded_by: null          # explicit null == "no edge" == terminal (EXT4-02)
  routes:
    - route: A
      verdict: COMPATIBLE      # COMPATIBLE | INCOMPLETE | INCOMPATIBLE(...) | UPSTREAM_DEFECT | pending
      adjudication_record_id: null   # COMPATIBLE is an all-pass terminal state — no adjudication record (DF-002 linter-contract correction)
      evidence: spikes/dafny-compat/evidence/samples/run-report.canonical.sample.json
    - route: B
      verdict: COMPATIBLE
      adjudication_record_id: null
      evidence: spikes/dafny-compat/evidence/samples/run-report.canonical.sample.json
```

## Decision — Route A selected (in-process, `CliCompilation` via `DafnyDriver`)

The boundary decision is **in-process-selected / Route A**, promoted from
provisional on 2026-07-24 (DF-002). The `adr_lint` block above now carries the
selection and the COMPATIBLE verdicts, anchored to the canonical committed
sample; INV-013's ADR linter validates the positive selection with zero findings
(the linter's COMPATIBLE contract was corrected as part of this promotion — see
the linter-contract note below).

The committed evidence state (QA-020 correction; true since the convergence
pair landed): the **canonical** committed sample
(`spikes/dafny-compat/evidence/samples/run-report.canonical.sample.json`) is a
**suite-attested canonical run** — `final_suite_status=success`, exit/report
matrix consistent, and **both route verdicts COMPATIBLE** — produced by a
canonical operator run of `spikes/dafny-compat/scripts/run-spike.sh` including
the test-suite phase (codex R4-02/R3-5), and is the cited evidence for the
selection above. The **variance-mode** committed sample (`run-report.sample.json`,
`final_suite_status=unknown`, route verdicts INCOMPLETE by construction)
remains the full-equality reproducibility anchor for the fresh-run-equality
test. Route A is selected; no route is rejected (both are COMPATIBLE).

### Maintainer route selection (recorded 2026-07-22; promoted 2026-07-24)

The maintainer selected **Route A** (`CliCompilation` via `DafnyDriver`) as the
in-process boundary. Rationale — fidelity to the `dafny` CLI's own verification
path, and upgrade robustness: Route B's `DafnyPipeline` consumption (the OQ-004
standard-libraries `.doo` path) is load-bearing and would need re-validation on
every Dafny bump. Accepted cost: `DafnyLanguageServer` becomes a permanent,
un-trimmable part of the production closure (the driver's parser/resolver/
verifier are LanguageServer types), and `DafnyPipeline` is not loaded on Route A.

**Promotion (DF-002, 2026-07-24):** the selection above is now the formal
decision. The machine-readable block was set to in-process-selected / Route A /
COMPATIBLE, anchored to the canonical committed sample, and the DD-007
component-table propagation into `.correctless/ARCHITECTURE.md` was applied (drop
`DafnyPipeline`, add `DafnyDriver` + `DafnyLanguageServer` in the core-worker
production closure). DESIGN.md's "Dafny publishes …" statements name the full
published four-package set and describe the Phase 0.0 spike (which exercised
`DafnyPipeline` on Route B); neither is a production-closure claim, so DESIGN.md
is unchanged.

### Linter contract correction (DF-002 / codex R4-01)

Promotion required correcting one rule in the INV-013 ADR linter
(`spikes/dafny-compat/contracts/SpikeContracts/Components.cs`, `AdrLinter.Lint`).
The linter had required a `COMPATIBLE` route verdict to cite an
`adjudication_record_id` resolving to a schema-valid terminal adjudication record
(codex R4-01). But adjudication records are a **failure-path** artifact —
`AdjudicationStateMachine` produces them only for INCOMPATIBLE / UPSTREAM_DEFECT
terminal transitions — so an all-pass `COMPATIBLE` run produces none, and the
frozen canonical sample legitimately carries `adjudication_records: null` (spike
QA-006 catch-22: a COMPATIBLE-bearing sample cannot be committed with records
without breaking fresh-run equality, and regenerating the frozen sample cannot
reconverge). Requiring a record for `COMPATIBLE` therefore made the positive
selection **unsatisfiable** against the committed evidence — the exact "spec
decision" QA-006 flagged as needed.

The correction: a `COMPATIBLE` claim is anchored by its committed **evidence
path** (unchanged requirement); the `adjudication_record_id` is now **optional**
for `COMPATIBLE` and validated only when cited. **Rejection** claims
(INCOMPATIBLE / UPSTREAM_DEFECT) continue to require a schema-valid record. No
evidence sample was regenerated and no commit at or before the frozen convergence
point was rewritten.

### Capability observations (bullets 1–3; each backed by the committed pair)

Verdict-bearing claims cite the **canonical** sample
(`spikes/dafny-compat/evidence/samples/run-report.canonical.sample.json`,
`deterministic.per_probe_results` keyed by `(probe, route)`, with
`final_suite_status=success`); the variance sample records the identical
per-probe outcomes and anchors full deterministic-projection equality against
a fresh run:

- **In-process parse → resolve → Z3-backed verify on a .NET 10 host works on
  both routes**: P06 (ok.dfy, non-vacuous typed Valid outcomes) and P07
  (bad.dfy, completed typed refutation containing the planted line) pass for
  routes A and B.
- **Loaded-identity anchors hold** (P03): Route A loads DafnyDriver +
  DafnyCore (plus DafnyLanguageServer — see propagation note) and Route B
  loads DafnyCore + DafnyPipeline via the OQ-004 standard-libraries `.doo`
  consumption; identities are file-SHA-256s traced to the spike-local package
  assets (`spikes/dafny-compat/manifest/expected-loaded/route-a.json`,
  `route-b.json`).
- **Executed-solver identity is proven behaviorally** (P04/P05 plus the
  removal/decoy tests in the suite): the sentinel ledger records the run
  nonce; decoys record zero invocations.
- **Resolved-AST recovery, options readback with the OQ-003 canary, and
  closure recovery work on both routes** (P10, P11, P12), including the
  resolver-inferred ghost variable and the include-pair closure differential.

### Route observations feeding a future selection

- Route A (`CliCompilation` via DafnyDriver) additionally loads
  `DafnyLanguageServer.dll` at runtime — the driver's parser/resolver/verifier
  are LanguageServer types. DafnyPipeline is NOT loaded on Route A.
- Route B (hand-assembled `Compilation` from DafnyCore + DafnyPipeline +
  Boogie.ExecutionEngine) runs without DafnyDriver and without
  DafnyLanguageServer; DafnyPipeline loads only through the named
  standard-libraries consumption (OQ-004), proven to matter by the
  removal/differential tests.
- The selected route's lock is the model for PAT-001's single production lock
  (defined end for the plural-lock spike state).

## Residuals (standing list)

- Sentinel and real runs are separate invocations; composition is inductive
  (INV-003).
- nuget.org upstream account compromise: accepted bootstrap-TCB residual
  (STRIDE/TB-004).
- Permanent availability dependency on nuget.org 4.11.0 packages, the
  Jan-2023 Z3 release asset, and the pinned .NET 8 runtime archive
  (vendoring/caching rejected for the spike, consequence accepted; the
  untracked `out/cache/` area is a local convenience, not a vendored copy).
- EA-003 offline-verification expectation is unverified (dotnet
  telemetry/first-run behavior untested; EA-008 opts out).
- Unproven RIDs: osx-arm64, win-x64 (fail closed at provisioning).
- Solver identity (QA-002): `executed_solver_sha256` is the recomputed digest
  of the `bin/z3` binary at the option-manifest solver path — the file
  actually executed — and `solver_archive_sha256` separately records the
  BND-002 release-asset pin. P04 re-verifies the installed binary against the
  digest provisioning records (`<run-root>/solver/z3-4.12.1/binary.sha256`), so
  a post-provisioning binary substitution fails P04. The archive pin plus
  provisioning's digest-verified extraction is the chain of custody from pin to
  executed binary.
- `DafnyDriver.dll` and `DafnyPipeline.dll` ship without
  `AssemblyInformationalVersion` attributes; their identity is carried by file
  digest alone (RS-008 note in the expected-loaded sets).
- The committed **canonical** sample is a suite-attested run report with
  `final_suite_status=success` and both routes COMPATIBLE (QA-020 correction
  of the earlier variance-only wording); promotion (DF-002) cites it. The
  masked suite-status subtree is guarded in-suite by schema validation of both
  samples, a verdict recomputation from the sample's own per-probe results,
  and the success/pending consistency check (QA-019 — masked never means
  unvalidated).
- Startup-gate sanctioning (QA-015): a z3 in a `decoys/`-named directory is
  sanctioned by exact script digest only; the assembly-adjacent decoy location
  remains location-sanctioned with the zero-invocation decoy-log assertion as
  its behavioral backstop.
- Layer-1 allowlist `rm` widening (MA-XC-3, citing QA-014): spec PRH-004
  enumerates bash, dotnet, curl, sha256sum, tar, unzip, git, mkdir, mktemp, mv,
  chmod, setsid, kill, sleep; the implementation additionally allows `rm`,
  needed by the QA-014 provisioning/junk cleanup and the MA-UX-2/MA-RB-2 prune
  and cache-staging paths. Recorded here as the adjudicated deviation (the
  three-point-change discipline's spec point); `BootstrapAllowlist.Commands`
  carries the matching enforcement-copy comment.
- Clean-environment contract (MA-HI-1): the canonical entry re-execs under
  `env -i` so only an allowlist survives. Toolchain/resolution-steering
  variables (DOTNET_ROOT, DOTNET_HOST_PATH, NUGET_PACKAGES, SSL_CERT_*, GIT_*)
  are stripped — the false-COMPATIBLE vector. `SPIKE_PARENT_DEADLINE` (deadline
  inheritance) and `SPIKE_RID_OVERRIDE` (the provisioning fault-injection hook
  the committed QA-005 test drives) are PRESERVED as sanctioned inputs that can
  only ever fail CLOSED (a spurious INCOMPLETE, never a false COMPATIBLE).
  Accepted residual: `SPIKE_RID_OVERRIDE` remains honored from the environment
  (it is a test/operator fault hook, fail-closed only); the finding's proposed
  `DOTNET_ROOT` env pass-through for system-wide installs is replaced by the
  explicit `--dotnet-root <path>` argument (MA-UX-3), which survives the re-exec
  and cannot be ambiently inherited — a strictly stronger form.
- Suite-status receipt (MA-VI-6): the controller emits a nonce-bound
  `receipts/suite-receipt.json` after the test phase of a canonical run; the
  aggregator DERIVES `final_suite_status` from it and downgrades an unvalidated
  `--suite-status` to `unknown` when it is absent, so a COMPATIBLE verdict is
  only reachable for a suite-attested canonical run whose receipt binds to the
  run.
- P01 partition non-veto (QA-022(2) recorded disposition): R4-07's non-veto
  property holds at the P01 attestation/attribution level — a route-scoped
  lock fault fails only its own P01 partition in the receipt and the emitted
  report — but the controller still fails the whole run closed on any restore
  failure, so no route reaches a verdict in that run. Accepted residual: the
  fail-closed run-level stop is deliberate; attribution (not verdict
  computation under a broken restore) is the property preserved.

## Propagation obligations (DD-007) — DISCHARGED 2026-07-24

Route A's actually-loaded package set (see
`spikes/dafny-compat/manifest/expected-loaded/route-a.json`) is `DafnyDriver` +
`DafnyCore` + `DafnyLanguageServer`; `DafnyPipeline` is NOT loaded on Route A.
The production-closure component table in `.correctless/ARCHITECTURE.md` (the
"C# core worker" row, which had named `DafnyCore, DafnyPipeline, …`) was amended
to drop `DafnyPipeline` and add `DafnyDriver` + `DafnyLanguageServer`, and the
route-selection prose there was updated to reflect the accepted status. DESIGN.md
names only what Dafny **publishes** (the full four-package set) and describes the
Phase 0.0 spike (which exercised `DafnyPipeline` on Route B), neither of which is
a production-closure claim, so DESIGN.md is unchanged. The mechanical
component-table consistency gate that re-checks this partition against
`route-a.json` is INV-003 enforcement-(b) in the phase-0.1-worker spec, homed with
the readiness gate in the Phase 0.1 build-gate carrier (still to be built).
