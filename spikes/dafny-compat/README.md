# dafny-compat spike — .NET 10 / Dafny 4.11.0 package-compatibility harness

Permanent conformance infrastructure (DD-004/DD-005/DD-006) validating
DESIGN.md's Phase 0.0 bullets 1–3 assumption: Dafny 4.11.0's `net8.0` NuGet
assemblies work **in-process on a .NET 10 host** for parse, resolve, Z3-backed
verify, and resolved-AST recovery. Spec:
`.correctless/specs/dafny-compat-spike.md`. Deliverables are evidence +
`docs/adr/ADR-0001-dafny-integration-boundary.md` — nothing here is production
code, but the suite is retained permanently (the "spike" name refers to
non-production status, not disposability).

## Prerequisites

- Host RID: **linux-x64** — the only RID this spike proves (EA-002). Any other
  RID fails closed at provisioning with:
  `RID not supported by this spike; proven RIDs: linux-x64 (see ADR-0001)`
- .NET SDK **10.0.302** exactly, pinned by `global.json`
  (`rollForward: disable`, `allowPrerelease: false`). Install source:
  https://dotnet.microsoft.com/download/dotnet/10.0 (or `dotnet-install.sh`)
  into a user-local `DOTNET_ROOT`.
- Network access to nuget.org and github.com during the online phase
  (restore + provisioning) only (EA-003).

## Canonical entry point and phase ordering (INV-014)

The canonical entry point is **`scripts/run-spike.sh`** (the bootstrap
controller). Invocation contract (PRH-004):

```
env -i HOME="$HOME" bash -p scripts/run-spike.sh
```

Phase ordering: **provision → locked restore → build → test** (then
aggregation). The controller mints the `run_id`, creates `out/<run_id>/`,
holds the single `/proc/uptime` monotonic deadline
(`config/run-config.json: wall_clock_bound_seconds`), and emits nonce-bound
restore/build receipts. Run products (spike-local package cache, provisioned
z3, sentinel recordings, fresh evidence) live only under the untracked `out/`
area; committed samples live at `evidence/samples/`, regenerated ONLY via
`scripts/regen-sample.sh` (DD-008) and accepted via diff review.

Every human-facing run ends with a per-route terminal verdict summary
(verdict, failed probes with reasons, report path) — no JSON parsing needed.

## Exit-code contract (INV-006 / INV-013)

Test runner: pinned to **VSTest** (`Microsoft.NET.Test.Sdk [17.11.1]` +
`xunit.runner.visualstudio [2.8.2]`; committed in `config/run-config.json`):

| exit | meaning |
|------|---------|
| 0 | all tests passed |
| 1 | at least one test failed |
| other | infrastructure failure — never read as pass or refutation |

Route harness children (`Corrected.Spike.Contracts.ExitCodes`):

| exit | meaning |
|------|---------|
| 0 | route-probes-passed |
| 10 | probe-failure |
| 20 | INCOMPLETE (typed cause in the report) |
| unknown code / signal death | crash variant — never pass, never refutation |

The full exit/report consistency matrix (including "failure exit + all-pass
report ⇒ never COMPATIBLE") is committed in
`schema/evidence-schema.json → route_outcome_algebra.exit_report_matrix`.

A route verdict of COMPATIBLE additionally requires the final test-suite exit
to be success and the exit/report matrix to be consistent (codex R4-02); an
all-pass probe report accompanied by a failing `dotnet test` is not citable.

## Run-context contract (GREEN implements; tests already depend on it)

The suite's integration tests refuse to launch anything outside a controller
run (DD-008). The controller MUST:

1. Export **`SPIKE_RUN_CONTEXT`** = `<run-root>/run-context.json` before
   invoking `dotnet test`. Receipt shape:
   `{ "run_id", "run_root", "artifacts": { "<name>": { "path", "sha256" } } }`
   with at least `RouteAHarness`, `RouteBHarness`, `RouteAControl`,
   `RouteBControl`, `SpikeAggregator`, `control_dotnet_host`. Tests verify
   each artifact digest before launching it (TA-B13).
2. Pre-create the **sentinel nonce ledger** at `sentinel/ledger.json`
   (`{ "nonce", "entries": [ { "nonce", "argv" } ] }`, count zero) — tests
   read this FILE and mint their own nonces for test-owned run roots (TA-B1).
3. Write receipts under `receipts/`: `aggregation.json`
   (`consumed_report_paths` from performed launches only), `env-audit.json`
   (applied per-launch environments), `build-receipt.json`
   (`artifacts: [ { "path", "sha256" } ]`, all beneath the run root).
4. Construct every child environment from `config/env-profiles.json`
   (EA-008); children record their received environment in report binding
   identity as root-kind + relative-path entries.
5. Route every external command through the `run_cmd` allowlist dispatch in
   `scripts/run-spike.sh`; when invoked unhardened, re-exec under
   `env -i bash -p` or refuse fail-closed (TA-B9).
6. Generic operator phases (used by tests with test-owned faults):
   `--phase restore --project <csproj>`, `--phase preprocess-audit` (emits
   `IMPORT <path>` lines), `--phase negative-compile --consumer-fixture <f>
   --scratch <dir>`, plus `--config <file>`, `--solver <path>`,
   `--run-root <dir>` overrides recorded as variance in evidence.

## Running the test suite directly (EA-004)

```
dotnet test spikes/dafny-compat -noAutoResponse
```

`-noAutoResponse` is mandatory on every restore/build/test invocation: the
spike-local `Directory.Build.rsp` seals directory response files, but only
`-noAutoResponse` disables the SDK-adjacent `MSBuild.rsp` (codex R3-8).

## Layout

| path | purpose |
|------|---------|
| `contracts/SpikeContracts/` | Dafny-free contracts assembly + verdict aggregator logic (INV-008) |
| `adapters/SpikeDafnyAdapter.RouteA|B/` | route seam projects — sole owners of Dafny/Boogie packages + per-route lock (DD-001) |
| `harness/RouteA|BHarness/` | net10.0 route harness executables (child-process entry points) |
| `aggregator/SpikeAggregator/` | managed-aggregator host (INV-006) |
| `control/RouteA|BControl/` | net8.0 control executables (BND-004/INV-013 three-cell experiment) |
| `tests/SpikeTests/` | THE test project — references only the contracts assembly |
| `fixtures/` + `fixtures/expected/` | committed `.dfy` fixtures + expected-value sidecars (BND-003) |
| `manifest/` | spec-owned probe manifest, Option Manifest, expected-loaded sets |
| `schema/` | evidence schema + append-only version→digest registry (INV-009) |
| `config/` | run config, Z3 pin, net8 control-runtime pin |
| `scripts/` | bootstrap controller, provisioning, sample regeneration |
| `ci/spike-ci.yml` | stub CI workflow — handoff artifact for the CI feature (DD-005/DF-001) |
| `evidence/samples/` | committed evidence samples (hygiene-checked; no host details, PRH-005) |
| `out/` | untracked run products (gitignored) |

## Toolchain identity (DD-006)

Every pinned identity (Dafny/Boogie packages in
`Directory.Packages.props`, Z3 in `config/z3-pin.json`, SDK in `global.json`,
net8 control runtime in `config/net8-control-pin.json`) carries a breadcrumb:
changing it requires re-running this suite and committing the evidence diff
plus an ADR-0001 amendment. Prerequisite failures name their remediation step
(e.g. `run provisioning first: scripts/provision-z3.sh`).
