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

## 2026-07-26 — Readiness-Gate Carrier (phase-0.1-worker enforcement home)

**Why this exists.** The Phase-0.1 worker must not land production code until a
machine-readable `implementation_readiness` block in `phase-0-1-worker.md` says its
preconditions (P1 ADR-boundary, P2 validator, P3 determinism) are genuinely
dischargeable. The hazard is *trusting that block*: it is committed markdown that anyone
with write access can tamper with — flip a `satisfied` flag, forge an ADR route-claim,
strip an evidence sample, or land a `src/` package early. This feature builds the
fail-closed gate that **re-derives** the evidence instead of trusting the flags, and the
production-surface ban that keeps `src/` empty while readiness is `BLOCKED`. It is the
RS-002 unlock the parent worker's own invariants (INV-001/002/036) are homed against.
Critically it lands as **Stage A**: the enforcement home is built and green, but the
readiness flip itself (`P1.satisfied → true`) is deliberately *not* performed — that
atomic Stage-B migration is a separate later step, and the gate exists precisely so the
flip can never be a bare edit ahead of evidence.

**What was built.** ~13k lines under `gate/`, plus DD-003 anchors in the parent spec, a
repo-root `global.json`, `.gitattributes`, and a `.github/workflows/readiness-gate.yml`
CI lane. The solution is four projects behind `gate/Corrected.Gate.slnx`: a pure,
I/O-free `Corrected.Gate.Kernel` (`ReadinessGate.EvaluateReadiness` + closed DTOs); the
impure `Corrected.Gate` edge (AST-hardened YAML/ADR parsers, the P1/P2/P3 evidence
probes, `MigrationManifest.CheckConsistency` for DD-003, the `ClosureBuildRunner` +
`ProductionSurfaceScanner` for INV-011, and the `StatusRenderer`); the `Corrected.Gate.Tests`
xUnit suite with one `Inv0NN*Tests.cs` per invariant plus a SUPPLIED-fixture kernel corpus;
and a Dafny-free `Corrected.Gate.Lint` (extracted so the gate build never pulls Dafny
assemblies). The operator/CI surface is the single runnable script
`gate/run-readiness-gate.sh` (restore-locked → `dotnet test … --logger trx` →
out-of-suite TRX executed-count guard → always-render INV-012 banner → combined exit),
with the combined-exit state machine single-sourced in `gate/tools/combined-exit.sh`.

**How it works.** The gate never trusts the declared `satisfied` flags. The block and the
ADR/evidence are strict-parsed into closed, validated domain types (tags/anchors/aliases,
duplicate and oversize blocks rejected); the P1/P2/P3 probes re-derive each precondition
from executable evidence; and the **pure kernel** renders the verdict — `status: READY` is
legal iff every precondition is actually `satisfied` AND every reference is `Resolved`,
else the verdict is `Fail`. At Stage A, ADR-0001 carries its decision fields but the
machine `status:` keys are absent, so P1 short-circuits to `evidence-schema-incomplete →
false`, giving a *consistent BLOCKED* — the intended green path. INV-011 is the sharpest
edge: rather than a static path/text scan, `ClosureBuildRunner` shells out to a real
out-of-process `dotnet build -t:Rebuild` on the pinned SDK, runs generators
(`EmitCompilerGeneratedFiles`), extracts the resolved `-getItem:Compile`/`-getItem:Analyzer`
item sets, and diffs an analyzer baseline — so a generated or linked source that lands
inside a shipped project's built closure is caught, not just a bare `src/` directory. The
DD-003 gate hashes the bytes between paired current-state anchors in the parent spec
against real SHA-256 digests in `readiness-migration-manifest.json`, failing closed on a
missing/duplicate anchor, a missing/invalid manifest, or an injected appendix marker. A
`CORRECTED_GATE_INNER` recursion sentinel makes any gate-invoking helper inside the
discovered suite a no-op, and the whole thing is proven green **from a clean checkout**
(226/226, `GATE_EXIT=0`).

**Patterns and non-obvious decisions.** The carrier registers **PAT-005** (readiness-gate
block checked by test; the exempt carrier enforces itself) and **TB-006** (readiness/ADR/
evidence intake as a tamperable boundary), and is a direct application of **PAT-004**
(structural enforcement over prose) — every invariant is locked by a mechanism that runs.
The single most important structural choice is *carrier exemption*: the gate lives under
`gate/**`, outside the shipped compilation closure, so it can enforce its own
production-code ban (INV-036) without tripping it. Three decisions came out of adversarial
review and are worth remembering. (1) INV-011 had to be a *real build*, not a scan — the
QA-002 finding was that a static scan can't see generated/linked closure members; the
`IIncrementalGenerator` fixture is the centerpiece proving the build actually runs
generators. (2) The kernel was isolated into its own I/O-free project (EXT6-05) specifically
so its purity invariant (INV-004) is both satisfiable and checkable via a project-graph
BCL-only bound plus a behavioral determinism check. (3) The DD-003 stale-literal scan is
scoped to the spec's *normative body* (text before `## Notes for review`) so the changelog's
historical `rm -rf out` literals don't falsify it. A mini-audit later caught a genuine
kernel bug the corpus had masked — `EvaluateReadiness` accepted a `READY` block with a
precondition declared `satisfied:false` (a forged-READY fail-open) because the
`ready-with-all-true-all-resolved-pass` fixture was mis-built; the fix added a global READY
check and a negative corpus row. Three accepted residuals were logged as drift-debt
(DRIFT-001 token-scan purity control, DRIFT-002 `StartsWith("Dafny")` family detection,
DRIFT-003 dict-parsed ADR block) — all fail-safe and dormant while readiness is BLOCKED,
resolvable when the Stage-B flip lands.

## 2026-07-26 — Stage-B P1 flip (readiness gate crosses its own boundary)

**Why this exists.** The readiness-gate carrier landed as Stage A: the enforcement home
was built and green, but the readiness flip itself was deliberately withheld so it could
never be a bare edit ahead of evidence. Stage B is that sanctioned flip — promoting
`P1.satisfied` to `true` by arming ADR-0001's machine acceptance schema and migrating the
DD-003 manifest to its after-digests, atomically, under a passing gate. P2/P3 remain
false, so overall readiness stays consistently `BLOCKED`; this crosses the P1 boundary
only, not the whole gate.

**What changed.** Two live artifacts. (1) `docs/adr/ADR-0001…` gained the optional
acceptance schema in its `adr_lint` block — `status: accepted`, plus explicit
`supersedes: null` / `superseded_by: null` marking the terminal node (the prose
`**Status**: accepted` had been present since DF-002, so the probe's prose↔machine
consistency check now passes). (2) `.correctless/specs/phase-0-1-worker.md` flipped the
P1 precondition to `satisfied: true` with a non-null `evidence` pointer, swapped the
A2/B5 current-state anchor spans to their committed after-content, and corrected three
now-stale normative-body literals that the DD-003 Stage-B stale-literal scan requires
absent (`EvaluateReadiness(blockText)` → `(block, probeResults)`, `pending DF-002` →
`DF-002 discharged`, `BLOCKED-all-false` → the post-flip phrasing).

**How it was verified — test-first.** The gate was built to verify exactly this flip, so
the change was driven test-first against the existing gate rather than through a fresh TDD
cycle. Several gate self-tests were hard-pinned to the live repo's Stage-A state and had
to be inverted or re-homed: `Inv008.PreMigration_adr_is_schema_incomplete` and
`Inv006.StageA_real_probe_…` became a live-repo Stage-B positive plus a re-homed
synthesized-tree copy — a new `P1Mutation.PreMigrationStatusAbsent` keeps the Stage-A
schema-incomplete branch covered stage-independently; `Dd003.Stage_…_StageA_today` and the
`Inv002`/`Inv013` live-repo assertions flipped to Stage-B expectations. All four
Stage-B-asserting tests were confirmed RED against the still-Stage-A artifacts, then green
after the flip. Renaming `StageA_committed_block_is_Pass_BLOCKED` → `StageB_…` also
required updating the INV-014 trx-guard's required-fixture list and the committed
`happy.trx` fixture (the real test name, the guard list, and the fixture must stay
consistent). `gate/run-readiness-gate.sh` is green-from-clean at 227/227, `GATE_EXIT=0`,
banner `PASS … BLOCKED`; the spike's ADR consumers (INV-013 linter + `Contains`
assertions, 24/24) stay green because the linter ignores unknown keys and the prose was
preserved.

**Notes.** The three drift-debt residuals (DRIFT-001/002/003) were dormant at Stage A
because P1 short-circuited; they now execute but remain fail-safe accepted debt (a
residual can only produce a false-FAIL, never a forged READY). `P1.evidence` is not
mechanically pinned to a specific string (the kernel checks non-null + probe agreement,
not the string content), so it names the registered gate test that re-derives P1's
discharge, per INV-002 ("test-id / gate / manifest path; never prose").

## 2026-07-29 — P3 Determinism Attestation (PR1, Group A)

PR1 is the first of three PRs that turn the Phase-0.0 determinism check (Inv010, "one
spike run's evidence projects deterministically") into a tamper-evident, CI-attested
**capability baseline** the readiness gate will eventually carry as a real fail-closed
**P3** probe (replacing the `validator-deferred` stub). PR1 is spike-side and **signs
nothing**: it builds the determinism status model, the serial CI lane, and a
measurement-campaign scaffold, and it flips no readiness precondition — P3 stays `false`,
readiness stays BLOCKED. PR2 adds the frozen `Corrected.Provenance` (cosign/DSSE)
mechanism; PR3 activates P3 on evidence only.

The core lives in `contracts/SpikeContracts/DeterminismStatusModel.cs` (the total
classifier, `DeterminismComparison`, the campaign ancestry check via real
`git merge-base --is-ancestor`, the receipt privacy scan, and `RunReceiptCodec`) and
`DeterminismReceiptWriter.cs` (`RunCli`, which emits a `RunReceipt` fail-closed). The
shipped receipt-status mapping in `Build` is single-sourced through
`DeterminismClassifier.Classify` (via `MapComparisonToReceiptStatus`) so the AP-022
exhaustiveness/totality tests constrain the *shipped* path, not a dead surrogate
(mini-audit MA-CC-001). `Compare` runs six ordered checks over committed registries —
role uniqueness, role set-equality, kind set-equality, role→kind totality, the
projection-policy pin (both the projection **impl** digest and `Sha256File` of the
evidence schema), then projection equality — so `comparison_status = equal` only when
every one of the five roles projects identically across the two runs. The lane is an
**extracted** script (`scripts/determinism-lane.sh`) the CI job runs verbatim
(AP-020/PMB-001, never inline YAML a grep can't exercise): it drives two nested
`run-spike.sh` runs into `<root>/r1` and `<root>/r2`, then reuses the already-built
aggregator host (`SpikeAggregator --emit-determinism-receipt`, no new project — MA-UC-4)
to emit the receipt and exit non-zero on `different` (INV-003).

Two conventions dominate. **CI separation:** the heavyweight lane tests carry
`[Trait("Category","determinism-lane")]`; the 4-vCPU general gate opts into
`run-spike.sh --exclude-category determinism-lane` (an *argument*, because the hardened
`env -i` invocation strips env vars), and a dedicated ≥8-core lane runs them for real.
The QA-001→013 saga hardened this to an *execution* proof: `build_inner_args` and
`build_suite_cmd` single-source the outer→inner argv forward and the actual `dotnet test`
command, and a `--print-inner-filter` dry-run test is bound to those same constructors —
so a mutation of the real line reds the test (the PMB-001/AP-020 parallel-path trap).
**In-tree run root (TB-004 / PRH-005):** the `/cverify` BLOCKING-1 fix. `SpikeRunRootRel`
is contractually SPIKE-relative (`Directory.Build.props`), so an out-of-tree run root
(the CI lane's original `$RUNNER_TEMP`) made MSBuild write build outputs in-tree while the
DD-008 completeness check resolved the true absolute root — a silent INCOMPLETE — and
would have leaked an absolute host path into the recorded build argv. `run-spike.sh`
(`require_in_tree_run_root`, both `RUN_DIR_REL` sites) and `determinism-lane.sh` now refuse
an out-of-tree `--run-root` fail-closed before any build; the CI lane uses an in-tree
`out/determinism-lane`.

A few decisions aren't obvious from the code. The measurement-campaign `run_id`s are
deliberate `PENDING-CI-NETWORK-ASSOCIATION` placeholders (spec-sanctioned RS-016
CI-network deferral, QA-003) — real floor-capable-lane run_ids are a **hard pre-landing**
requirement, not deferrable to PR2. BLOCKING-1 was fixed as **Option A** (in-tree +
fail-closed) rather than "make out-of-tree work," because an out-of-tree root fundamentally
violates the SPIKE-relative / PRH-005 argv contract. And a subtle test bug surfaced only
under the *full* from-clean suite: inside a controller run `TMPDIR` is redirected under the
in-tree run root, so `Path.GetTempPath()` returned an in-tree path and the lane legitimately
accepted it, masking the defect — the boundary test now anchors at a TMPDIR-independent
`/tmp` with a self-check that it is genuinely out-of-tree.
