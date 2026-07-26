# INV-002 EoP fixture: an explicit YAML tag (!!str) injected into the readiness
# block. Stage-1 AST pre-validation over the low-level IParser event stream MUST
# reject every explicit tag (incl. built-in !!str/!!int/!!bool) -> the block yields
# `indeterminate`, never a materialized gadget (PRH-003).

```yaml
implementation_readiness:
  schema_version: !!int 1
  status: !!str BLOCKED
  ready_predicate: "P1 AND P2 AND P3"
  preconditions:
    - id: P1
      name: adr-0001-promoted-or-superseded
      satisfied: false
      evidence: null
      discharges: [DF-002]
    - id: P2
      name: p2
      satisfied: false
      evidence: null
      discharges: [DF-003]
    - id: P3
      name: p3
      satisfied: false
      evidence: null
      discharges: []
```
