Read AGENTS.md before doing anything in this repo.

## Correctless Learnings
<!-- Auto-updated by Correctless workflow. Do not edit above this line. -->

### 2026-07-23 — Postmortem: documented root entry point (run-spike.sh from repo root) exited 127
- Test the documented command **verbatim** — same working directory and same relative/absolute `argv[0]` form the docs tell the operator to type. Asserting the README merely *mentions* the script (a keyword-presence check) and launching the entry point only through an absolute-path/fixed-cwd test helper both miss form-specific defects: a relative `BASH_SOURCE`/`$0`/argv path reused after a `cd` no longer resolves. Canonicalize any such path to absolute at capture. Operator-surface / entry-point invariants require an execution test, never a doc grep.
- Source: PMB-001 (see AP-020)
