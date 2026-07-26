# AP-031 real-producer fixture — the PRE-MIGRATION (Stage A) ADR adr_lint block.
# Source: docs/adr/ADR-0001-dafny-integration-boundary.md (lines 27-40), verbatim.
# Note: the REQUIRED tier (boundary_decision, selected_route, routes[]) is present
# and valid; the OPTIONAL acceptance schema (status / supersedes / superseded_by
# keys) is ABSENT — so INV-008 (a) step-2 must short-circuit to
# `evidence-schema-incomplete` (NOT `evidence-malformed`, NOT a throw). This block
# is what dead-reds a naive parser that treats absent-status as malformed.

```yaml
adr_lint:
  boundary_decision: in-process-selected   # pending | in-process-selected | rejected
  selected_route: A            # A | B | null
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
