# TAMPER fixture (INV-008 masking guard, AP-014). A REQUIRED field
# (boundary_decision) is STRIPPED. This MUST map to `evidence-malformed`, NOT
# `evidence-schema-incomplete` — otherwise a malicious stripped decision field is
# masked as a benign "pre-migration" case (the R3-B1b masking hazard).

```yaml
adr_lint:
  selected_route: A
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
