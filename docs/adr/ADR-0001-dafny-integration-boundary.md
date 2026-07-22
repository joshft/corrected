# ADR-0001: Dafny integration boundary (provisional)

- **Status**: provisional (DD-007; promotion to *accepted* is an inherited
  obligation of the final Phase 0.0 feature — DF-002)
- **Scope**: DESIGN.md Phase 0.0 **bullets 1–3 only**, pinned to DESIGN.md
  v1.13 (bullet numbering is not stable across design revisions). Deferred
  Phase 0.0 gates: bullets 4–12.
- **Spec**: `.correctless/specs/dafny-compat-spike.md`
- **Evidence**: `spikes/dafny-compat/evidence/samples/run-report.sample.json`
  (committed sample, regenerated only via `spikes/dafny-compat/scripts/regen-sample.sh`
  per DD-008; the fresh-run-equality test binds it to reality on every suite run).

## Machine-readable decision block (INV-013 ADR linter input)

The linter validates POSITIVE compatibility/selection claims as well as
rejection claims against schema-valid terminal adjudication records; a failed
mandatory P03 anchor can never be overridden by ADR prose (OQ-004 gate).

```yaml
adr_lint:
  boundary_decision: pending   # pending | in-process-selected | rejected
  selected_route: null         # A | B | null
  routes:
    - route: A
      verdict: pending         # COMPATIBLE | INCOMPLETE | INCOMPATIBLE(...) | UPSTREAM_DEFECT | pending
      adjudication_record_id: null
      evidence: null
    - route: B
      verdict: pending
      adjudication_record_id: null
      evidence: null
```

## Decision (pending a canonical suite-attested run)

The boundary decision stays **pending**: a route verdict of COMPATIBLE
requires `final_suite_status=success` from a canonical operator run of
`spikes/dafny-compat/scripts/run-spike.sh` (codex R4-02/R3-5), and the
committed sample is deliberately produced in variance mode
(`final_suite_status=unknown`, route verdicts INCOMPLETE by construction) so
the fresh-run-equality comparison is like-for-like. No COMPATIBLE claim is
made here yet; no rejection claim is made either.

### Capability observations (bullets 1–3; each backed by the committed sample)

All claims cite `spikes/dafny-compat/evidence/samples/run-report.sample.json`
(`deterministic.per_probe_results`, keyed by `(probe, route)`), whose
deterministic projection a fresh run must reproduce:

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
- The BND-002 pin is the SHA-256 of the release *archive*; the digest of the
  extracted `bin/z3` binary actually executed is recorded per run
  (`executed_solver_sha256`) rather than pinned in config — the archive pin
  plus provisioning's extraction is the chain of custody.
- `DafnyDriver.dll` and `DafnyPipeline.dll` ship without
  `AssemblyInformationalVersion` attributes; their identity is carried by file
  digest alone (RS-008 note in the expected-loaded sets).
- A COMPATIBLE, suite-attested run report exists only under the untracked
  `out/<run_id>/` area of a canonical operator run; the committed sample is
  variance-mode by design. Promotion (DF-002) should cite a canonical run.

## Propagation obligations (DD-007)

If the selected route's actually-loaded package set differs from what
DESIGN.md/ARCHITECTURE.md name, the required DESIGN.md + ARCHITECTURE.md
component-table updates are named here as explicit obligations. Already
observable now: selecting Route A would mean DafnyPipeline is NOT loaded (the
component table naming `DafnyCore, DafnyPipeline, …` would need amending) and
that DafnyLanguageServer becomes a runtime dependency of the worker; selecting
Route B would mean DafnyDriver is not part of the production closure.
