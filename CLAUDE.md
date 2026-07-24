Read AGENTS.md before doing anything in this repo.

## Correctless Learnings
<!-- Auto-updated by Correctless workflow. Do not edit above this line. -->

### 2026-07-23 — Postmortem: documented root entry point (run-spike.sh from repo root) exited 127
- Test the documented command **verbatim** — same working directory and same relative/absolute `argv[0]` form the docs tell the operator to type. Asserting the README merely *mentions* the script (a keyword-presence check) and launching the entry point only through an absolute-path/fixed-cwd test helper both miss form-specific defects: a relative `BASH_SOURCE`/`$0`/argv path reused after a `cd` no longer resolves. Canonicalize any such path to absolute at capture. Operator-surface / entry-point invariants require an execution test, never a doc grep.
- Source: PMB-001 (see AP-020)

### 2026-07-23 — Postmortem: full suite red from a clean checkout — a suite test required a prior green run of the same suite (circular gate)
- Every suite/gate test must be provable **green from a single run of a clean checkout** (`rm -rf out` / fresh clone) with no accumulated state. A test that reads its subject from live `out/`, `out/current`, or on-disk prior-run receipts it did not produce this run is checking **self-produced state**: it passes only on leaked prior-run state (AP-010) and **deadlocks from clean** (a failing member of the suite prevents the very green receipt it demands). Bind any check of a run's OWN product to the **current** run's artifact via `RunContext`, never by enumerating prior run roots on disk (this is the spec's own RS-010 "never from reports found on disk" applied to tests). Wire the from-clean gate (DF-001) **now** — it is the shared missing net behind both PMB-001 and PMB-002; don't defer it.
- Source: PMB-002 (see AP-021)
