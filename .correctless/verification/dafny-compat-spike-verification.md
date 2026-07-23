# Verification: .NET 10 / Dafny 4.11.0 Package-Compatibility Spike (Phase 0.0, bullets 1–3)

- **Task**: dafny-compat-spike
- **Spec**: `.correctless/specs/dafny-compat-spike.md` (391 lines, status: approved)
- **Branch**: `feature/dafny-compat-spike` @ HEAD `e2a7028`
- **Effective intensity**: high (project floor `high` = feature `high`)
- **Verified**: 2026-07-22
- **Serena**: disabled (`mcp.serena=false`) — text-based tracing used.

## Evidence: full-suite green (canonical path)

The suite's canonical entry point is `scripts/run-spike.sh` (README / INV-014),
which mints a run, provisions Z3, does locked restore + build, publishes
`out/current`, then runs the `dotnet test` suite phase with `SPIKE_RUN_CONTEXT`
set, then aggregates. A **fresh canonical run was executed during this
verification**:

```
Passed!  - Failed: 0, Passed: 273, Skipped: 0, Total: 273, Duration: 5 m 27 s
route A verdict: COMPATIBLE
route B verdict: COMPATIBLE
run runid-fecd77bcc12f9332 complete (suite: success; A=0 B=0; aggregator exit 0; controller exit 0)
```

Fresh run-report: `final_suite_status = success`, both route states `COMPATIBLE`,
all 22 composite `(probe,route)` entries pass, git-binding `git_commit_id ==`
HEAD (`e2a7028`). Full-suite-green sentinel `.correctless/artifacts/test-success.sha`
written with HEAD content.

> Note on the configured `commands.test` — see Finding F-01. A *bare*
> `dotnet test spikes/dafny-compat` (the workflow-config command) is **not** an
> equivalent green gate; only the `run-spike.sh` canonical path is.

## Rule Coverage

All 14 invariants and 5 prohibitions are covered by substantive tests. Every
`[integration]`-tagged invariant is exercised through a real child-process
harness launch (`Launch.Harness` / `RunContext.Resolve` / `Launch.Script`), not
a unit stand-in — confirmed by launch-call counts per file and by the green
canonical run that actually drives Dafny + Boogie + provisioned Z3.

| Rule | Primary test(s) | Status | Notes |
|------|-----------------|--------|-------|
| INV-001 | Inv001ToolchainPinTests (10) | covered | Parses every `packages.lock.json`, asserts exact `[4.11.0]`/`[3.5.5]` family pins; NuGet.Config `<clear/>`+single source+mapping; global.json fields; CPM authoritative; **behavioral stale-lock negative restore** (NU1004-class) |
| INV-002 | Inv002SystemCommandLineTests (10) | covered | Lock-file `System.CommandLine` beta assert + expected-loaded-set equality by **file SHA-256** + net10-runtime proof (TFM/`Environment.Version.Major`/CoreLib) + report-mutation failure tests + namespace grep |
| INV-003 [integration] | Inv003SolverIdentityTests (7; 11 launches) | covered | Sentinel-solver nonce ledger, always-on decoys with zero-invocation assert, per-route z3-removal test, digest check, non-discoverable install |
| INV-004 [integration] | Inv004VerifyOkTests (3) | covered | Child-process harness; non-vacuous typed solver outcomes; AST-based fixture-hygiene test on ok.dfy |
| INV-005 [integration] | Inv005VerifyBadTests (4) | covered | Stage=verification refutation vs timeout/frontend; planted-location **containment** rule |
| INV-006 [integration] | Inv006VerdictTests (20; 7 launches) | covered | Per-manifest-entry deletion tests, route-deletion→INCOMPLETE, manifest-digest test constant, `final_suite_status` as verdict input, fail-closed. Spike's honesty mechanism — strongest coverage |
| INV-007 [integration] | Inv007AstRecoveryTests (9; 16 launches) | covered | Resolved-AST node table vs sidecar; **inferred-ghost mutation differential**; independent AST test (no sidecar); Option Manifest readback + normalization canary |
| INV-008 | Inv008SeamTests (8) | covered | Project-graph (no Dafny/Boogie PackageReference outside 2 seam projects); **two-phase negative-compile** (positive builds, forbidden fails at planted token); `MetadataLoadContext` API-surface scan; test-closure Dafny-free |
| INV-009 [integration] | Inv009EvidenceTests (15; 9 launches) | covered | Three-way field partition; schema-digest anchoring + append-only registry; anti-hardcode SHA recompute; hygiene grep; fresh-run-equality vs committed sample; git binding |
| INV-010 [integration] | Inv010DeterminismTests (3) | covered | Run-twice-and-diff on schema-derived deterministic projection |
| INV-011 [integration] | Inv011FrontendTests (4) | covered | stage=parse / stage=resolution typed diagnostics; zero sentinel invocations for the run nonce |
| INV-012 [integration] | Inv012ClosureTests (3) | covered | Closure `{root,leaf}` identical under both include-option values; verification-target-set delta asserted |
| INV-013 [integration] | Inv013AdjudicationTests (20; 6 launches) | covered | State machine + terminal transitions; **three-cell** net8/net10 control experiment; ADR linter on machine-readable fields; exit/report matrix; `verdict_reason` union payloads |
| INV-014 [integration] | Inv014OperatorSurfaceTests (9; 7 launches) | covered | README entry-point ref; provisioning 3-state + double-run idempotency; `.gitignore` covers `out/`; exact RID-fail message; terminal verdict summary |
| PRH-001 | Inv006VerdictTests, ProhibitionTests | covered | No COMPATIBLE on partial/failed/self-shrunken run; no error-path fallthrough |
| PRH-002 | Inv001ToolchainPinTests, ProhibitionTests | covered | Version-syntax scan over both csproj + authoritative `Directory.Packages.props` |
| PRH-003 | ProhibitionTests | covered | Spike-area path containment (advisory gate + suite backstop) |
| PRH-004 | ProhibitionTests (+4 files) | covered | `Process.Start` allowlist scan; no regex classification; no `dafny` CLI; two-layer launch allowlist; BASH_ENV/.curlrc poison tests |
| PRH-005 | ProhibitionTests, Inv009EvidenceTests | covered | Hygiene grep for host paths/username/hostname over committed artifacts |

**Uncovered rules: none. Weak tests: none identified. Wrong-level: none** — all
integration rules test through the real harness path.

Supporting rules also carry tests: EA-008 (`Ea008EnvironmentProfileTests`, env
profiles), EA-001/EA-002/EA-006, BND-002/BND-003/BND-004 (net8 control intake
exercised — control cells ran on pinned 8.0.29), and the probe-manifest/schema
SHA-256 constants in `SpecConstants.cs` match the committed files exactly
(`4956816b…` manifest, `c872c710…` schema).

## Dependencies

All packages are exact-pinned singleton ranges in the spike-local
`Directory.Packages.props`; every one is named in the spec (INV-001/INV-002).
No undeclared dependencies. `PackageReference` items are versionless (CPM);
Dafny/Boogie refs carry `PrivateAssets="compile"` and live only in the two
route-seam projects (INV-008).

- DafnyCore `[4.11.0]`, DafnyPipeline `[4.11.0]`, DafnyDriver `[4.11.0]`, Boogie.ExecutionEngine `[3.5.5]`
- System.CommandLine `[2.0.0-beta4.22272.1]` (transitive pin; no spike source uses it)
- Microsoft.NET.Test.Sdk `[17.11.1]`, xunit `[2.9.2]`, xunit.runner.visualstudio `[2.8.2]`, System.Reflection.MetadataLoadContext `[8.0.0]`

## Architecture Adherence

- TB-004 (Inbound toolchain supply chain): **valid** — registered in
  `.correctless/ARCHITECTURE.md` by this feature (Scope item 7 / RS-013),
  invariant/violated-when consistent with the implemented pins + digest checks.
- PAT-001..004, TB-001..003: **valid** — untouched; the spike prefigures PAT-001
  (DafnyAdapter boundary) via INV-008 without redefining it.
- ARCHITECTURE.md "Known Limitations": **stale (advisory)** — still asserts
  "Nothing is implemented … Phase 0.0 must still validate the .NET 10 / Dafny
  4.11 package-compatibility assumption." The spike has now shipped that
  validation infrastructure and produced ADR-0001 (both routes COMPATIBLE).
  `src/` is still empty (spike is non-production), so the wording is only
  partially outdated. → F-03, a /cdocs maintenance item, non-blocking.

### Drift Debt
`.correctless/meta/drift-debt.json` is absent (dormant — no prior open items).
No new DRIFT-NNN entries created; findings below are surfaced for disposition
rather than silently logged (this skill runs without human follow-up).

8 architecture entries checked, 1 stale (advisory), 0 path-missing.

## Deferred Obligations (registered, in-scope)
- DF-001 (DD-005): wire the committed CI stub `spikes/dafny-compat/ci/spike-ci.yml`
  as a permanent CI job — inherited by the CI feature. **open**, registered.
- DF-002 (DD-007): promote ADR-0001 provisional→accepted — inherited by the
  final Phase 0.0 feature. **open**, registered.
- DF-003..DF-012: additional MEDIUM/LOW deferred items recorded in
  `deferred-findings.json` (e.g. DF-003 exit/report-matrix cell). None gate this
  verification.

## QA / Mini-Audit Class Fixes Verified
`qa-findings-dafny-compat-spike.json`: 87 findings across QA round 1 + two
mini-audit rounds. **All 7 BLOCKING + 1 CRITICAL are `fixed`**; 76 fixed, 11
deferred (all LOW / NON-BLOCKING / upstream). Spot-checked class fixes are
present as structural tests:
- QA-001 (INV-009 evidence binding), QA-002/003 (INV-003 solver identity),
  QA-004 (INV-002 loaded-set), QA-005 (INV-013 adjudication), QA-006/007
  (INV-006/009 canonical-sample suite mask) — each has a corresponding test in
  `QaFixRound1Tests` / `QaFixRound2Tests` / probe-kill tests.

## Antipattern Scan
`antipattern-scan.sh main` → valid JSON, `errors: []`, 55 findings + 1 summary
(`+27 more in run-spike.sh`). All **low severity**:
- 34 `debug-logging` — `echo` progress statements in operator/provisioning shell
  scripts (`clean-runs.sh`, `provision-z3.sh`, `run-spike.sh`). These are the
  operator surface required by INV-014, not debug leaks.
- 21 `error-handling` — `|| true` / `|| :` in shell (deliberate fail-tolerance).

No blocking smells. No TODO/FIXME/HACK, no commented-out code, no hardcoded host
values (PRH-005 hygiene tests enforce this on committed artifacts). Two xUnit
analyzer **warnings** (not errors) in `Inv014OperatorSurfaceTests.cs`
(xUnit2009 `Assert.False` substring; CS8620 nullability) — cosmetic.

## Drift
- **F-01 (MEDIUM, non-blocking)** — Configured `commands.test`
  (`dotnet test spikes/dafny-compat -noAutoResponse`) is not a reliable
  standalone green gate. `spikes/dafny-compat/Directory.Build.props:52-53`
  auto-sets `RunSettingsFilePath` to `out/current/spike.runsettings` when it
  exists, so a bare `dotnet test` binds to whatever run last published
  `out/current`:
  - **Fresh clone** (`out/` is gitignored/absent): integration tests fail loudly
    ("no controller run context", `RunContext.RequirePath`) — by design they only
    run inside a controller run.
  - **Stale `out/current`** (built at an older commit): `Inv009EvidenceTests.
    GitCommitBinding_MatchesHeadAtGenerationTime` fails (report git_commit ==
    build-time commit ≠ current HEAD). Observed live: a bare run at HEAD `e2a7028`
    reused an `out/current` built at parent `237ce73` and failed this test.
  - **Concurrent controller run**: `QA021_OutCurrent_PointsAtCanonicalRunContext`
    races on `out/current` (observed when a `run-spike.sh` published mid-suite).

  The harness itself is spec-compliant and green via the canonical
  `run-spike.sh` path (273/273, both routes COMPATIBLE). This is a gap between
  EA-004's intent ("the Correctless workflow test command is configured to run
  the spike suite") and the configured command, which does not reproduce the
  canonical invocation. **Recommended disposition**: point `workflow-config.json`
  `commands.test` at `scripts/run-spike.sh` (or a thin wrapper), or explicitly
  document that the EA-004 green / cverify full-suite sentinel must come from
  `run-spike.sh`. Not blocking spec-compliance, but it affects Correctless's own
  test-gating and any future CI wiring (DF-001).

## Spec Updates
No `spec_updates` counter in the workflow state (field absent → 0). The spec
records QA-round-1 amendments in metadata (QA-006 committed-sample pair; QA-004
universe; oracle-edit ratifications) folded in before verification; these are
documented in the spec's Metadata block, not post-verify drift.

## Other advisory findings
- **F-02 (LOW)** — Committed evidence samples (`evidence/samples/*.json`) carry
  binding `git_commit_id = d28ed5d`, far behind HEAD `e2a7028`. This field is
  class-1 binding, **excluded from cross-run equality** by INV-009, and the
  fresh-run-equality test passes, so it is non-blocking; DD-006/DD-008 sample
  regeneration would normally refresh it. /cdocs / next toolchain-bump item.

## Overall: PASS — 0 blocking findings, 3 advisory (F-01 MEDIUM, F-02/F-03 LOW)

Every spec invariant and prohibition is covered by substantive tests that
exercise the real integration path; the canonical suite is fully green (273/273)
with both routes COMPATIBLE and `final_suite_status = success`; all deliverables
(ADR-0001, evidence sample pair, CI stub, probe manifest/schema/registry,
sealing files, fixtures, TB-004) are present; dependencies are exact-pinned and
declared; all BLOCKING QA findings are fixed. The three advisory findings are
maintenance/configuration items for /cdocs and the workflow config — none block
merge of the spike itself.
