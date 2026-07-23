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
  into a user-local `DOTNET_ROOT`. The controller resolves the SDK from
  `HOME/.dotnet`, then the cached pointer (`out/cache/dotnet-root`).
  **System-wide installs (`/usr/share/dotnet`, `PATH`) are NOT auto-discovered**,
  and the clean-environment contract (MA-HI-1) strips an ambient `DOTNET_ROOT`
  env var so a version-spoofing wrapper cannot void the pinned SDK. A user whose
  SDK lives elsewhere names it **explicitly** as an argument (not an env var the
  re-exec would strip):

  ```
  env -i HOME="$HOME" bash -p scripts/run-spike.sh --dotnet-root <sdk-root>
  ```
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

Aggregator (`SpikeAggregator`) exit (QA-016 — fail-closed on any non-passing
AGGREGATION outcome, derived from the emitted run report): 0 only when every
per-probe entry of the 22-entry manifest view passes AND the exit/report
matrix is consistent; 10 when any probe failed; 20 when any entry is
incomplete (missing/malformed/forged-run_id route report, receipt mismatch,
P02/P04 failure) or the matrix is inconsistent, and on aggregator-internal
faults. A variance run whose probes all pass still exits 0 (its INCOMPLETE
route verdicts come from `final_suite_status=unknown`, the suite channel).

Controller (`scripts/run-spike.sh`) run-level exit (QA-008/QA-016 —
fail-closed for CI wiring, matching the report/summary):

| exit | meaning | report emitted? |
|------|---------|-----------------|
| 0 | all route children passed, aggregation passed, and (canonical runs) the suite passed | yes |
| 1 | a route child failed, aggregation detected a failure, or the suite failed | yes |
| 20 | INCOMPLETE — a per-run prerequisite/wall-clock/operator-cancel fault (synthetic report emitted) **OR** a PRE-RUN failure that emits NO report (MA-UX-4) | see note |
| 30 | refused: unhardened invocation, or a non-allowlisted variable present at canonical entry (use `env -i HOME=… bash -p`) | no |

**Exit 20 without a report (MA-UX-4):** exit 20 covers two classes. Once a run
root exists, prerequisite/wall-clock/operator-cancel faults emit a synthetic
INCOMPLETE report at the run-report path. But PRE-RUN failures that occur before
a run root is minted — an unknown/malformed argument, a missing `--project`/
`--consumer-fixture`, a missing `NuGet.Config`, a missing pinned SDK, a
corrupt/truncated `run-config` (PR-004), a `run_cmd` DENY — also exit 20 and
emit **no** report. A CI wrapper or operator that finds exit 20 with no
synthetic report at the expected path is looking at a pre-run failure (a typo or
missing prerequisite), distinct from a genuine INCOMPLETE run; the stderr line
names which.

Provisioning (`scripts/provision-z3.sh`) exit:

| exit | meaning |
|------|---------|
| 0 | provisioned (or already provisioned — idempotent) |
| 20 | usage error (missing `--run-root`, unknown argument, `run_cmd` DENY) — no report |
| 21 | unsupported host RID (fail-closed before any fetch) — no report |
| 22 | digest mismatch / no digest-valid archive (BND-002 fail-closed) — no report |

Sample regeneration (`scripts/regen-sample.sh`) exit:

| exit | meaning |
|------|---------|
| 0 | pair regenerated (review the diff before committing) |
| 20 | a run produced no/uncitable report, or the readout failed (AP-009) — no report |
| 30 | refused: unhardened invocation |
| 40 | refused: dirty tree (QA-001) — commit/stash first |

Run-root cleanup (`scripts/clean-runs.sh`) exit:

| exit | meaning |
|------|---------|
| 0 | pruned (or nothing to prune) — maintenance tool, no report |
| 20 | usage error / unhardened invocation |

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
cd spikes/dafny-compat && dotnet test DafnyCompatSpike.sln -noAutoResponse
```

**Run it from `spikes/dafny-compat/` (MA-UX-6).** The .NET muxer walks up from
the working directory for `global.json`; the repo root has none, so
`dotnet test spikes/dafny-compat` from the repo root runs the test host under
whatever newest SDK is installed, bypassing the 10.0.302 pin. `cd`-ing into the
spike directory first makes `spikes/dafny-compat/global.json` govern the direct
path. (The artifacts under test are controller-built and digest-attested and
QA-013 catches source drift, so only the test-host runtime is affected — but the
pin should govern uniformly.)

`-noAutoResponse` is mandatory on every restore/build/test invocation: the
spike-local `Directory.Build.rsp` seals directory response files, but only
`-noAutoResponse` disables the SDK-adjacent `MSBuild.rsp` (codex R3-8).

**A completed CANONICAL controller run must exist first.** Integration tests
launch only controller-built, digest-attested artifacts (DD-008/TA-B13). Each
completed **canonical** controller run atomically publishes
`out/current/spike.runsettings` — nested/variance runs (any `--run-root`/
`--out`/`--config`/`--solver` override) never touch `out/current` (QA-021:
only canonical operator runs may publish the shared pointer), which
`Directory.Build.props` feeds to VSTest so direct `dotnet test` invocations
receive `SPIKE_RUN_CONTEXT` pointing at the latest run's `run-context.json`.
On a fresh clone (no `out/current/`), the variable is unset and every
integration test fails loudly with the remediation message — run
`scripts/run-spike.sh` once. The controller also caches the resolved SDK
location (`out/cache/dotnet-root`) and the digest-verified provisioning
archives (`out/cache/`) so nested in-suite controller runs work under the
empty launch environments the tests construct.

**Suite-status semantics (codex R4-02/R3-5):** only a canonical operator run
(no `--run-root`/`--out`/`--config`/`--solver` overrides) executes the
`dotnet test` suite phase and records `final_suite_status` success/failure;
variance-mode runs — the form every in-suite nested launch uses, which also
prevents unbounded recursion — record `unknown`, so their route verdicts are
INCOMPLETE by construction and never citable as COMPATIBLE. The committed
evidence sample is a PAIR (QA-006 amendment): a **variance-mode** sample
(`evidence/samples/run-report.sample.json`, full class-2 equality) and a
**canonical-run** sample (`run-report.canonical.sample.json`, whose equality
masks only the schema-declared suite-status subtree —
`schema/evidence-schema.json → suite_status_mask`). ADR-0001's verdict
citations use the canonical sample. `scripts/regen-sample.sh` regenerates both
and refuses a dirty tree (QA-001).

**Solver identity digests (QA-002):** `config/z3-pin.json` pins the SHA-256 of
the official release *archive* (BND-002 intake check), recorded per run as the
`solver_archive_sha256` field. The separate `executed_solver_sha256` field is
the recomputed digest of the `bin/z3` binary at the option-manifest solver path
— the file actually executed — recomputed at emission and re-verified by P04
against the digest provisioning records in
`<run-root>/solver/z3-4.12.1/binary.sha256` (so a post-provisioning binary
substitution fails P04, not just P05–P07).

## Stopping a run (MA-UX-1/MA-RB-4)

A canonical run is long (~13–15 min). To stop it, send **Ctrl-C / SIGINT** (or
SIGTERM / close the terminal) to the controller. The outer watchdog traps the
signal, sweeps the entire inner session (the setsid'd inner controller plus the
active phase's own setsid session) with `SIGTERM`→`SIGKILL`, writes a synthetic
INCOMPLETE report with a typed `OperatorCancelled` cause, and exits nonzero. It
does **not** silently orphan the run: without the trap the detached inner would
keep provisioning/building/aggregating — and, for a canonical run, republish
`out/current` — minutes after you thought it was dead, racing an immediate
re-run. `out/current` is published only after the suite phase, which a cancelled
run never reaches, so the dev-loop pointer is left untouched.

## Disk usage, cleanup, and recovery

- **~1 GB per canonical run.** Each `out/<run_id>/` root holds a package-cache
  copy, the extracted net8 runtime, the z3 archive, and 5 projects' build
  output. Run roots are **safe to delete** — run products are regenerated by the
  next controller run. `rm -rf out` is a safe full reset (it also drops the
  shared `out/cache`, forcing a one-time cold restore/provision).
- **`out/test-scratch` is the largest consumer.** Every canonical run's suite
  phase populates `out/test-scratch/<token>/` via nested-controller fixture runs
  (several GB on a long-lived tree — more than `out/runid-*`+`out/regen-*`
  combined). Each suite invocation uses a fresh token dir (QA-021); older tokens
  accumulate until reclaimed.
- **Automatic prune.** Every *canonical* run prunes `out/runid-*` at start
  (under a coarse `out/`-level lock so concurrent runs never delete each other's
  live roots), keeping the newest 5 — never `out/current`'s target, never
  `out/cache`, never `out/regen-*`, never a run whose `.inner-pid` is live.
  Variance/nested runs never prune. The prune announces the count removed on
  stderr.
- **On-demand prune.** `env -i HOME="$HOME" bash -p scripts/clean-runs.sh
  [--keep N]` (default 3, `--keep 0` refused) prunes old `out/runid-*` and
  `out/regen-*` roots, **reclaims stale `out/test-scratch/<token>` dirs** (all
  but the newest) plus loose `out/preprocess.*.xml` / `out/*.log`, and leaves the
  `out/current` and `out/cache` directories — and the run root `out/current`
  points into — untouched. This is the way to reclaim `out/test-scratch` without
  the `rm -rf out` that also drops the shared `out/cache`.
- **Corrupt shared package cache (MA-UX-5/AP-016).** The shared cache
  (`out/cache/packages`) is trusted only when its `packages.complete` marker is
  present; a torn seed (deadline KILL / ENOSPC mid-populate) leaves a
  marker-less dir the controller ignores and rebuilds from restore. If restore
  still fails against a wedged cache, recover with `rm -rf out/cache` (the
  restore-failure message names this).

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
| `scripts/` | bootstrap controller, provisioning, sample regeneration, run-root cleanup (`clean-runs.sh`) |
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
