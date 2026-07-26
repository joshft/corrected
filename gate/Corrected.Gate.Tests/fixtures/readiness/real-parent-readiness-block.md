# AP-031 real-producer fixture — the COMMITTED parent readiness block (Stage A).
# Source: .correctless/specs/phase-0-1-worker.md (lines 132-153), verbatim.
# INV-001-D: exactly ONE `implementation_readiness:` key at column 0 inside the one
# ```yaml fence. INV-001 asserts the current real parent parses to exactly one
# block; INV-002 asserts the values (schema_version 1, status BLOCKED, exactly
# {P1,P2,P3}, each satisfied:false + evidence:null). This is the Stage-A committed
# state that must stay green (P1.satisfied:false).

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
