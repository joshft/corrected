# INV-001 duplicate-block fixture: TWO column-0 `implementation_readiness:` keys
# each inside a ```yaml fence -> a tamper signal -> hard fail-closed (RS-002).

```yaml
implementation_readiness:
  schema_version: 1
  status: BLOCKED
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

```yaml
implementation_readiness:
  schema_version: 1
  status: READY
  ready_predicate: "P1 AND P2 AND P3"
  preconditions:
    - id: P1
      name: adr-0001-promoted-or-superseded
      satisfied: true
      evidence: forged
      discharges: [DF-002]
    - id: P2
      name: p2
      satisfied: true
      evidence: forged
      discharges: [DF-003]
    - id: P3
      name: p3
      satisfied: true
      evidence: forged
      discharges: []
```
