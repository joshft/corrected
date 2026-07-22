# ADR-0001: Dafny integration boundary (provisional)

- **Status**: provisional (DD-007; promotion to *accepted* is an inherited
  obligation of the final Phase 0.0 feature — DF-002)
- **Scope**: DESIGN.md Phase 0.0 **bullets 1–3 only**, pinned to DESIGN.md
  v1.13 (bullet numbering is not stable across design revisions). Deferred
  Phase 0.0 gates: bullets 4–12.
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

## Decision (pending — the route selection is a later, human decision)

The boundary decision stays **pending**: selecting a route (or rejecting the
in-process boundary) is a user decision to be taken later, and the `adr_lint`
block above deliberately carries `pending` verdicts until that promotion is
made with schema-valid adjudication records (INV-013/PAT-004).

The committed evidence state (QA-020 correction; true since the convergence
pair landed): the **canonical** committed sample
(`spikes/dafny-compat/evidence/samples/run-report.canonical.sample.json`) is a
**suite-attested canonical run** — `final_suite_status=success`, exit/report
matrix consistent, and **both route verdicts COMPATIBLE** — produced by a
canonical operator run of `spikes/dafny-compat/scripts/run-spike.sh` including
the test-suite phase (codex R4-02/R3-5), and is citable for verdict claims.
The **variance-mode** committed sample (`run-report.sample.json`,
`final_suite_status=unknown`, route verdicts INCOMPLETE by construction)
remains the full-equality reproducibility anchor for the fresh-run-equality
test. No selection claim is made here yet; no rejection claim is made either.

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

## Propagation obligations (DD-007)

If the selected route's actually-loaded package set differs from what
DESIGN.md/ARCHITECTURE.md name, the required DESIGN.md + ARCHITECTURE.md
component-table updates are named here as explicit obligations. Already
observable now: selecting Route A would mean DafnyPipeline is NOT loaded (the
component table naming `DafnyCore, DafnyPipeline, …` would need amending) and
that DafnyLanguageServer becomes a runtime dependency of the worker; selecting
Route B would mean DafnyDriver is not part of the production closure.
