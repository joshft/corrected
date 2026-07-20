# Agent Context — Corrected

> Last updated: 2026-07-20

## What This Project Does

Corrected is an open-source proof-directed verification worker and
certification toolchain for Dafny. Given a versioned Dafny program, frozen
formal obligations, and an explicit edit policy, it searches for an allowed
implementation or proof patch, rejects unapproved proof shortcuts, and emits
reproducible evidence (an assurance receipt). **Design-stage: nothing is
built yet** — `DESIGN.md` (v1.13) at the repo root is the authoritative
design document. Read it before speccing any feature.

## Detected Tooling

- Language: none yet. Planned: C# core worker (.NET 10 LTS) + TypeScript Pi
  adapter, organized as a monorepo (`is_monorepo: true` in workflow config;
  per-package commands unconfigured until packages exist).
- Package manager / test runner / linter: not yet chosen — configure via
  `/csetup` re-run once the first package is scaffolded.

## Key Components

See `.correctless/ARCHITECTURE.md` for the intended component map (C# core
worker, `corrected` CLI, TypeScript Pi adapter, JSONL protocol seam) and the
frozen design patterns (PAT-001..003), prohibitions, and trust boundaries
(TB-001..003).

## Common Pitfalls

- **Treating DESIGN.md as aspirational**: its §12 delivery-model decisions
  (DafnyAdapter boundary, verifier split, protocol seam) are frozen
  commitments — specs must compose with them, not redesign them.
- **Putting policy logic in the TypeScript adapter**: the adapter is
  integration code only (PROHIBIT-001).

## Quick Reference

| Need to... | Do this |
|------------|---------|
| Run tests | (not configured) |
| Build | (not configured) |
| Lint | (not configured) |
| Read the design | `DESIGN.md` (repo root) |
| Find a spec | `.correctless/specs/{feature}.md` |
| Check architecture | `.correctless/ARCHITECTURE.md` |
| See known bugs | `.correctless/antipatterns.md` |
