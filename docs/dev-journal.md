# Dev Journal

## 2026-07-22 — .NET 10 / Dafny 4.11.0 Package-Compatibility Spike

**Why this exists.** The whole Corrected architecture (DESIGN.md §12) is gated on one
assumption: that Dafny 4.11.0's `net8.0` NuGet assemblies run *in-process on a .NET 10
host* for the APIs the worker actually needs — parse, resolve, Boogie+Z3 verification, and
resolved-AST recovery. NuGet reports that compatibility, but no one had exercised it (the
surveyed ecosystem vendors Dafny source or shells out to the CLI). This spike proves it
with a permanent, tracked-but-non-production harness under `spikes/dafny-compat/`. Two
failure modes were treated as equally harmful and guarded against throughout: a *false
COMPATIBLE* (passing without exercising the surfaces that matter) and a *misadjudicated
INCOMPATIBLE* (a harness or environment fault misread as a real integration failure). A
genuine incompatibility found here would have been a *successful* spike outcome.

**What was built.** ~27k lines across 128 files, all under `spikes/dafny-compat/`,
`docs/adr/`, and `.correctless/`. The C# side is deliberately split into a Dafny-free
`SpikeContracts` assembly (result/evidence types + verdict aggregation), two route-seam
adapter projects (`SpikeDafnyAdapter.RouteA` uses `DafnyDriver`/`CliCompilation`;
`.RouteB` hand-assembles `Compilation` from `DafnyCore` + `DafnyPipeline` +
`Boogie.ExecutionEngine`) that are the *sole* owners of Dafny/Boogie package references and
per-route locks, `net10.0` route harness executables, a managed `SpikeAggregator`, `net8.0`
control executables for the three-cell net8-vs-net10 experiment, and the test project
(`tests/SpikeTests/`) which references *only* the contracts assembly and launches harnesses
as child processes. The shell side is the bootstrap controller `scripts/run-spike.sh`
(~1500 lines), `provision-z3.sh`, `regen-sample.sh`, and `clean-runs.sh`. Fixtures, expected
sidecars, the spec-owned probe manifest, the versioned evidence schema + append-only
digest registry, and the pin files round it out.

**How it works.** `run-spike.sh` is the first thing that runs and owns everything the
aggregator later only *validates*: it mints an unpredictable `run_id` and run directory,
holds the single absolute monotonic deadline (`/proc/uptime`) from mint through final
aggregation, and drives the phase ordering provision → locked restore → build → publish
`out/current` → `dotnet test` → aggregate. Each phase runs in its own `setsid` process
group under an async supervisor doing TERM→KILL escalation, with the outer watchdog as the
*parent* of the controller (not a test executed by it) so a hung Boogie+Z3 chain is
independently killable. The integration tests refuse to launch anything outside a
controller run context and digest-verify every artifact against a run-context receipt
before launching it. Verdicts are per-route and fail-closed: a route is COMPATIBLE only
when its completed probe set exactly equals the manifest's instantiation (composite
`(probeID, route)` keys, no duplicates/unknowns), every probe passed, *and* the final test
suite exited success — the aggregator derives its expected set from the committed manifest,
never from reports found on disk, and rejects any report whose `run_id` mismatches.

**Patterns and non-obvious decisions.** The spike prefigures **PAT-001** (the DafnyAdapter
boundary) via INV-008's structurally-enforced seam, and is a direct application of
**PAT-004** (structural enforcement over prose): every invariant is locked by a mechanism
that runs — a digest comparison, a schema validator, a fail-closed gate, or a behavioral
test through the real harness path. It registered **TB-004** (inbound toolchain supply
chain) in ARCHITECTURE.md. Several decisions are worth remembering: solver identity is
proven *behaviorally* (a per-run nonce sentinel ledger + always-on decoys asserting zero
invocations + a per-route z3-removal test), not by digest alone, because a digest check
alone can pass for the wrong reason. Evidence uses a three-way field partition (binding /
deterministic-projection / volatile) so determinism (INV-010) is a clean run-twice-and-diff
and the equality domain shrinks only via a reviewable schema-file diff, never a test edit.
The canonical entry re-execs under `env -i` (MA-HI-1) so toolchain-steering env vars
(`DOTNET_ROOT`, `NUGET_PACKAGES`, `SSL_CERT_*`, `GIT_*`) can't create a false COMPATIBLE;
system SDKs are named via an explicit `--dotnet-root` argument that survives the re-exec.
The route decision itself (Route A selected) lives in ADR-0001 and is intentionally *not*
yet promoted — that promotion, with a schema-valid adjudication record and the DD-007
component-table propagation, is the final Phase 0.0 feature's obligation (DF-002).
