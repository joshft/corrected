# ADR-0001: Dafny integration boundary (provisional)

- **Status**: provisional (DD-007; promotion to *accepted* is an inherited
  obligation of the final Phase 0.0 feature — DF-002)
- **Scope**: DESIGN.md Phase 0.0 **bullets 1–3 only**, pinned to DESIGN.md
  v1.13 (bullet numbering is not stable across design revisions). Deferred
  Phase 0.0 gates: bullets 4–12.
- **Spec**: `.correctless/specs/dafny-compat-spike.md`
- **Evidence**: pending — every claim below must cite a repo-relative
  committed evidence path, one per claim (INV-014).

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

## Decision (pending GREEN evidence)

Per-route verdicts, pinned identities, packages *actually loaded* per route
(P03 — never packages merely referenced), unsupported constructs, failure
behavior: to be recorded from the committed evidence sample. The selected
route's lock is the model for PAT-001's single production lock.

## Residuals (standing list — extended by GREEN)

- Sentinel and real runs are separate invocations; composition is inductive
  (INV-003).
- nuget.org upstream account compromise: accepted bootstrap-TCB residual
  (STRIDE/TB-004).
- Permanent availability dependency on nuget.org 4.11.0 packages, the
  Jan-2023 Z3 release asset, and the pinned .NET 8 runtime archive
  (vendoring/caching rejected for the spike, consequence accepted).
- EA-003 offline-verification expectation is unverified (dotnet
  telemetry/first-run behavior untested; EA-008 opts out).
- Unproven RIDs: osx-arm64, win-x64 (fail closed at provisioning).

## Propagation obligations (DD-007)

If the selected route's actually-loaded package set differs from what
DESIGN.md/ARCHITECTURE.md name (e.g. Route A selected ⇒ DafnyPipeline not
loaded), the required DESIGN.md + ARCHITECTURE.md component-table updates are
named here as explicit obligations.
