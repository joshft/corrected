# INV-001-D decoy fixture: an INLINE PROSE mention of `implementation_readiness`
# that must be IGNORED (not counted as a block). Uses copies of the real parent's
# prose lines (RS-261) — a naive counter dead-reds the real parent. There is ZERO
# column-0 `implementation_readiness:` inside a ```yaml fence here, so extraction
# must find ZERO blocks -> hard fail-closed (not one).

The `implementation_readiness` block is the single source of truth. A backticked
`implementation_readiness:` mention and a `.status` reference in prose legitimately
exist in the parent (see lines ~196/1069/1443) and are ignored as prose.

```text
implementation_readiness:   # column 0 but a `text` info-string fence, NOT yaml — must NOT count
  status: BLOCKED
```
