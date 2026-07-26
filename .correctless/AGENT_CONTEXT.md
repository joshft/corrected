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

A second non-production build has since landed: the **readiness-gate carrier**
under `gate/` — an isolated .NET 10 solution that homes the Phase-0.1 worker's
readiness gate (INV-001/002/036) and enforces the **Stage-A boundary** (no
production `src/` code while `implementation_readiness` is BLOCKED; the parent's
`P1.satisfied` stays `false` — the atomic flip is a separate Stage-B step). See
`docs/features/readiness-gate-carrier.md`.

## Detected Tooling

- Language: C# on .NET 10, SDK pinned `10.0.302`. The pin is now **repo-wide**
  via the repo-root `global.json` (`rollForward: latestPatch`,
  `allowPrerelease: false`), added by the readiness-gate carrier (INV-016 /
  TB-004); `spikes/dafny-compat/global.json` keeps its own exact pin. Planned
  production: C# core worker (.NET 10 LTS) + TypeScript Pi adapter, organized as
  a monorepo (`is_monorepo: true`; per-package commands unconfigured until
  production packages exist).
- Test runner: xUnit + VSTest (`Microsoft.NET.Test.Sdk`) in both the spike and
  the `gate/` carrier. The configured `commands.test` is now the carrier gate,
  `bash gate/run-readiness-gate.sh` (distinct from the spike's controller).
  Linter: not yet chosen. Re-run `/csetup` once the first production package is
  scaffolded.

## Key Components

See `.correctless/ARCHITECTURE.md` for the intended component map (C# core
worker, `corrected` CLI, TypeScript Pi adapter, JSONL protocol seam), the
frozen design patterns (PAT-001..004), prohibitions, and trust boundaries
(TB-001..004 — TB-004 *inbound toolchain supply chain* was registered by the
spike). The built artifacts so far are the `spikes/dafny-compat/` harness (see
its `README.md`) and the `gate/` readiness-gate carrier (see
`docs/features/readiness-gate-carrier.md`).

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
- **Green-gating the carrier with a bare `dotnet test`**: the readiness gate
  must run via `bash gate/run-readiness-gate.sh`. A bare `dotnet test` on the
  gate solution swallows the INV-012 status banner and runs no out-of-suite
  executed-count guard (INV-014), so a zero-discovery run reads as green.

## Quick Reference

| Need to... | Do this |
|------------|---------|
| Run the spike suite (canonical, only reliable gate) | `env -i HOME="$HOME" bash -p spikes/dafny-compat/scripts/run-spike.sh` (~13–15 min) |
| Run the readiness gate (carrier) | `bash gate/run-readiness-gate.sh` (from a clean checkout; `commands.test`) |
| Run spike tests directly | `cd spikes/dafny-compat && dotnet test DafnyCompatSpike.sln -noAutoResponse` (needs a prior canonical run) |
| Build the spike | `dotnet build spikes/dafny-compat -noAutoResponse` |
| Lint | (not configured) |
| Read the design | `DESIGN.md` (repo root) |
| Find a spec | `.correctless/specs/{feature}.md` |
| Check architecture | `.correctless/ARCHITECTURE.md` |
| See known bugs | `.correctless/antipatterns.md` |
