Read AGENTS.md before doing anything in this repo.

## Output Language

Write all assistant output in this project in ASD-STE100 Simplified Technical English (STE). Obey the STE writing rules: use approved words only, keep sentences short (procedures 20 words or fewer, descriptions 25 words or fewer), write in the active voice, give one instruction per sentence, and use simple tenses. Do not use unapproved or ambiguous words. This rule applies to all prose output, but not to code, file paths, identifiers, commit messages, or quoted spec text.

## Correctless Learnings
<!-- Auto-updated by Correctless workflow. Do not edit above this line. -->

### 2026-07-23 — Postmortem: documented root entry point (run-spike.sh from repo root) exited 127
- Test the documented command **verbatim** — same working directory and same relative/absolute `argv[0]` form the docs tell the operator to type. Asserting the README merely *mentions* the script (a keyword-presence check) and launching the entry point only through an absolute-path/fixed-cwd test helper both miss form-specific defects: a relative `BASH_SOURCE`/`$0`/argv path reused after a `cd` no longer resolves. Canonicalize any such path to absolute at capture. Operator-surface / entry-point invariants require an execution test, never a doc grep.
- Source: PMB-001 (see AP-020)

### 2026-07-23 — Postmortem: full suite red from a clean checkout — a suite test required a prior green run of the same suite (circular gate)
- Every suite/gate test must be provable **green from a single run of a clean checkout** (`rm -rf out` / fresh clone) with no accumulated state. A test that reads its subject from live `out/`, `out/current`, or on-disk prior-run receipts it did not produce this run is checking **self-produced state**: it passes only on leaked prior-run state (AP-010) and **deadlocks from clean** (a failing member of the suite prevents the very green receipt it demands). Bind any check of a run's OWN product to the **current** run's artifact via `RunContext`, never by enumerating prior run roots on disk (this is the spec's own RS-010 "never from reports found on disk" applied to tests). Wire the from-clean gate (DF-001) **now** — it is the shared missing net behind both PMB-001 and PMB-002; don't defer it.
- Source: PMB-002 (see AP-021)

### 2026-07-27 — Postmortem: an "exhaustive" exit/report matrix was certified green while one cell (exit-20 + all-pass report) failed open to COMPATIBLE
- When a spec, schema, or `switch` claims a decision table / matrix / enum-mapping is **"exhaustive" or "complete"**, its test must be a **cross-product enumeration** over the declared input dimensions (`{dim₁} × {dim₂} × …`), asserting for every cell: a defined outcome (no silent `default` fallthrough), a match to exactly one committed table row (derive the cell set **from the committed schema**, not from test literals), and the **safety-direction invariant** the table guards (here: no failure-signalling exit + all-pass report may ever be COMPATIBLE). A **row-count** (`length >= N`), a **presence** assert, or a handful of **representative** per-cell tests is a proxy that cannot detect an *absent* cell — it is AP-011 (presence/count standing in for behavior) applied to an AP-018 completeness claim, and a `default` branch that assumes input content it never inspects is a fail-open on the accept side (AP-001). When a fix touches one cell of such a table, close the **whole cluster** with the cross-product test — don't patch one instance and defer known sibling cells.
- Source: PMB-003 (see AP-022)
