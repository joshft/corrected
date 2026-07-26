# Stage-B MIGRATED ADR adr_lint fixture (DD-003 site B13). Synthesized from the
# real pre-migration block by ADDING the optional acceptance schema keys the
# DD-003 Stage-B changeset introduces: `status: accepted` and an EXPLICIT
# `superseded_by: null` (null and key-absent both denote "no edge"; EXT4-02). This
# is the terminal (accepted, no non-null successor). INV-008 must parse it VALID
# (not schema-incomplete) and INV-002 must accept the explicit-null wire form.
# Base excerpt Source: docs/adr/ADR-0001-dafny-integration-boundary.md lines 27-40.

```yaml
adr_lint:
  boundary_decision: in-process-selected
  selected_route: A
  status: accepted            # OPTIONAL tier (DD-003 Stage B) — present here
  supersedes: null            # nullable canonical ADR id | null | absent
  superseded_by: null         # explicit null == "no edge" == terminal (EXT4-02)
  routes:
    - route: A
      verdict: COMPATIBLE
      adjudication_record_id: null
      evidence: spikes/dafny-compat/evidence/samples/run-report.canonical.sample.json
    - route: B
      verdict: COMPATIBLE
      adjudication_record_id: null
      evidence: spikes/dafny-compat/evidence/samples/run-report.canonical.sample.json
```
