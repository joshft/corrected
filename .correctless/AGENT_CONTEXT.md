# Agent Context — Corrected

> Last updated: 2026-07-20

## What This Project Does

Corrected is an open-source proof-directed verification worker and
certification toolchain for Dafny. Given a versioned Dafny program, frozen
formal obligations, and an explicit edit policy, it searches for an allowed
implementation or proof patch, rejects unapproved proof shortcuts, and emits
reproducible evidence (an assurance receipt). **Design-stage: no production
code yet** (`src/` is empty) — `DESIGN.md` (v1.13) at the repo root is the
authoritative design document; read it before speccing any feature. The first
build to land is the **Phase 0.0 package-compatibility spike** under
`spikes/dafny-compat/`: permanent, non-production conformance infrastructure
that validated Dafny 4.11.0 running in-process on a .NET 10 host (both
integration routes COMPATIBLE). See `docs/features/dafny-compat-spike.md` and
`docs/adr/ADR-0001-dafny-integration-boundary.md`.

## Detected Tooling

- Language: C# on .NET 10 (SDK pinned `10.0.302` via
  `spikes/dafny-compat/global.json`) exists **only in the spike** so far.
  Planned production: C# core worker (.NET 10 LTS) + TypeScript Pi adapter,
  organized as a monorepo (`is_monorepo: true`; per-package commands
  unconfigured until production packages exist).
- Test runner: xUnit + VSTest (`Microsoft.NET.Test.Sdk`), scoped to the spike.
  Linter: not yet chosen. Re-run `/csetup` once the first production package is
  scaffolded.

## Key Components

See `.correctless/ARCHITECTURE.md` for the intended component map (C# core
worker, `corrected` CLI, TypeScript Pi adapter, JSONL protocol seam), the
frozen design patterns (PAT-001..004), prohibitions, and trust boundaries
(TB-001..004 — TB-004 *inbound toolchain supply chain* was registered by the
spike). The one built artifact so far is the `spikes/dafny-compat/` harness —
see its `README.md` for the operator surface.

## Common Pitfalls

- **Treating DESIGN.md as aspirational**: its §12 delivery-model decisions
  (DafnyAdapter boundary, verifier split, protocol seam) are frozen
  commitments — specs must compose with them, not redesign them.
- **Putting policy logic in the TypeScript adapter**: the adapter is
  integration code only (PROHIBIT-001).
- **Green-gating the spike with a bare `dotnet test`**: the configured
  `commands.test` is *not* a reliable gate — integration tests only run inside
  a canonical `scripts/run-spike.sh` controller run (which publishes
  `out/current`) and fail loudly otherwise; a bare `dotnet test
  spikes/dafny-compat` from the repo root also bypasses the pinned SDK
  (MA-UX-6). Canonical green is
  `env -i HOME="$HOME" bash -p spikes/dafny-compat/scripts/run-spike.sh`.

## Quick Reference

| Need to... | Do this |
|------------|---------|
| Run the spike suite (canonical, only reliable gate) | `env -i HOME="$HOME" bash -p spikes/dafny-compat/scripts/run-spike.sh` (~13–15 min) |
| Run spike tests directly | `cd spikes/dafny-compat && dotnet test DafnyCompatSpike.sln -noAutoResponse` (needs a prior canonical run) |
| Build the spike | `dotnet build spikes/dafny-compat -noAutoResponse` |
| Lint | (not configured) |
| Read the design | `DESIGN.md` (repo root) |
| Find a spec | `.correctless/specs/{feature}.md` |
| Check architecture | `.correctless/ARCHITECTURE.md` |
| See known bugs | `.correctless/antipatterns.md` |
