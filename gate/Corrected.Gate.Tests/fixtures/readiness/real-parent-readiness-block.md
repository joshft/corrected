# AP-031 real-producer fixture — the COMMITTED parent readiness block (Stage B).
# Source: .correctless/specs/phase-0-1-worker.md (lines 132-153), verbatim.
# INV-001-D: exactly ONE `implementation_readiness:` key at column 0 inside the one
# ```yaml fence. INV-001 asserts the current real parent parses to exactly one
# block; INV-002 asserts the values (schema_version 1, status BLOCKED, exactly
# {P1,P2,P3}; P1 satisfied:true + non-null evidence, P2/P3 satisfied:false +
# evidence:null). This is the Stage-B committed state (post P1 flip).

```yaml
implementation_readiness:
  schema_version: 1          # readiness-block schema version; INV-001 rejects an unrecognized version fail-closed (RS-001)
  status: BLOCKED            # BLOCKED | READY  (READY requires every precondition satisfied AND evidence non-null)
  ready_predicate: "P1 AND P2 AND P3"   # human-readable mirror; INV-001 asserts it equals the conjunction of precondition ids
  preconditions:
    - id: P1
      name: adr-0001-promoted-or-superseded
      satisfied: true
      evidence: Corrected.Gate.Tests.Inv008P1ProbeTests.Committed_tree_is_migrated_P1_satisfied   # registered gate test that re-derives P1's ADR-boundary discharge over the real tree; never prose
      discharges: [DF-002]
    - id: P2
      name: phase-0.1-entry-capability-gates-and-df-003-remediated
      satisfied: false
      evidence: null          # path to a committed Phase-0.0 completion manifest whose every named gate is green-from-clean
      discharges: [DF-003]     # plus the P0-* Phase-0.1-entry capability gates (DESIGN §13 v1.14)
    - id: P3
      name: inv010-ci-determinism-exercised-not-silently-skipped
      satisfied: false
      evidence: null          # CI lane / reworked test proving the cross-run determinism check actually runs on the CI path
      discharges: []
```
