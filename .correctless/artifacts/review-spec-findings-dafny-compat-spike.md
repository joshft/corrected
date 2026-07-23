# Review-Spec Findings: dafny-compat-spike
Date: 2026-07-20T23:59:00Z
Spec: .correctless/specs/dafny-compat-spike.md
Agents: self-assessment, red-team, assumptions, testability, design-contract, upgrade-compatibility, ux
Intelligence brief: dormant (present but 0 entries — all sections empty)

```
external-review status: skipped (config invalid or codex entry absent)
  egress:  (no call made — nothing sent to OpenAI this run)
  cost:    unavailable (run skipped)
  disable: set require_external_review:false in workflow-config.json to turn cross-model review off
  cause:   producer bin validation rejects npm-global codex installs (resolved path is
           .../node_modules/@openai/codex/bin/codex.js — fails basename+node_modules checks).
           Known upstream: correctless#199, correctless#200. Additionally the producer clamps
           timeout_seconds to 300s (EXTREV_TIMEOUT_MAX), below xhigh full-spec latency (~19 min):
           filed correctless#268. Note: spec v1 received a full codex (gpt-5.6-sol, xhigh)
           pre-review during /cspec (archived at
           .correctless/artifacts/reviews/dafny-compat-spike-codex-prereview.md); this review-spec
           run is Claude-only on v2.
```

Disposition (2026-07-20, human gate): all 20 findings **accepted** and incorporated into spec v3. Route policy decision (RS-001): both routes mandatory to attempt, per-route verdicts, no bare overall COMPATIBLE — Phase 0.1 gating requires the ADR-selected route fully COMPATIBLE. TB-004 added to ARCHITECTURE.md. No findings deferred or rejected.

Adjudicated out during synthesis (no finding): the self-assessment's "Route A `CliCompilation` may be internal" (research brief verified `CliCompilation.Create`/`VerifyAllLazily` public against v4.11.0 source) and "Boogie 3.5.5 may fight DafnyCore's declared dependency" (DafnyCore declares `>= 3.5.5`; NuGet lowest-applicable resolves exactly 3.5.5; any residual misread fails closed via locked restore + P01/P03). Both downgraded to ADR residual notes at most — red-team, design-contract, and upgrade-compatibility independently reached the same disposition.

## Finding RS-001: Route-set self-shrink — a rule-abiding Route-A-only run yields COMPATIBLE without ever loading DafnyPipeline
**Severity**: CRITICAL (blocking)
**Source**: red-team, design-contract, testability, assumptions, self-assessment (UX-006 adjacent)
**Category**: verdict-integrity
**Description**: The Probe Manifest freezes the 12 probe IDs but not the route set; DD-001 requires Route B only "whenever Route A fails" and INV-006 instantiates equality "per attempted route" — a runtime-declared set. DafnyDriver does not depend on DafnyPipeline, so a Route-A-only all-green run emits COMPATIBLE while never loading the package DESIGN.md bullet 1 explicitly names ("beginning with `DafnyCore` and `DafnyPipeline`") and the ARCHITECTURE.md component table commits to. Route-qualification is prose; Phase 0.1 gating consumes the word COMPATIBLE. This is the probe-level self-shrink INV-006 closes, reopened one level up. Fix: commit the required route set inside the probe manifest and extend INV-006's set equality to (probeID, route) composite keys with a route-deletion test (disable Route B → verdict ≠ COMPATIBLE). Decision needed: (a) both routes mandatory for COMPATIBLE, or (b) per-route verdicts where the overall claim enumerates each route's verdict and Phase 0.1 gating requires the ADR-selected route fully COMPATIBLE (red-team's mixed-outcome analysis favors (b): under (a), Route A failing + Route B passing forces a false-negative overall verdict even though DESIGN.md prefers any usable official-package surface).
**Status**: accepted

## Finding RS-002: Probe manifest has no independent digest anchor — one coordinated edit re-opens self-shrink
**Severity**: CRITICAL (blocking)
**Source**: red-team
**Category**: verdict-integrity
**Description**: Verdict logic reads the committed manifest from disk; an implementation (or agent under pressure) that edits the manifest file and the harness plan together still satisfies equality and emits COMPATIBLE. The manifest digest lands in evidence but is compared to nothing spec-pinned until the ADR-citation check, which only runs if someone runs it. The INV-006 deletion tests are ambiguous about where deletion happens; implemented as manifest edits they cannot work at all. Fix: the test suite hard-codes the expected manifest digest and asserts it against both the manifest file and every evidence report — removing a probe then requires a three-point change (spec, manifest, test constant), which is auditable.
**Status**: accepted

## Finding RS-003: Solver-identity mechanism is defeatable — assembly-adjacent same-file collision, weak removal-test oracle, stale sentinel recordings, PROVER_PATH
**Severity**: CRITICAL (blocking)
**Source**: red-team, assumptions, testability, design-contract
**Category**: solver-identity
**Description**: (a) Dafny's solver discovery stage 2 is assembly-adjacent (`{output}/z3/bin/z3-4.12.1`) and precedes PATH; the research brief's Recommended Patterns section suggests installing z3 at exactly that location. If provisioning does so, the explicit solver-path target and the fallback target are the same file: the removal test removes both at once and can never detect broken option wiring, and PATH sanitization is irrelevant. (b) The removal test's stated oracle ("verdict is not COMPATIBLE") passes for the wrong reason (AP-010): P04 fails first when z3 is absent, so the test passes even if another solver silently answered P06/P07. (c) The sentinel run (P05) and real runs (P06/P07) are separate invocations with separately constructed options — the composition is inductive; the revision substituted this for the pre-review's requested child-exec capture (a regression). (d) A stale sentinel recording from a prior run makes P05 pass while the option was ignored this run — no run-uniqueness is specified. (e) `PROVER_PATH` env var (first in discovery order) is never sanitized or declared. Fixes: provision z3 outside every ambient discovery location; pre-assert no other z3-named executable exists under each route output tree; add a decoy test (plant a differently-hashed `z3-4.12.1` at the assembly-adjacent path, assert the executed solver is the pinned one); removal test asserts per route that P06 and P07 individually record solver-unavailable failure; sentinel recording carries a per-run nonce; a single shared unit-enforced function builds the solver-path option for all probes; sanitize/declare `PROVER_PATH`; record the sentinel/real-run composition as an explicit ADR residual.
**Status**: accepted

## Finding RS-004: Four invariants rest on undefined observation channels the spike is supposed to be discovering
**Severity**: CRITICAL (blocking)
**Source**: testability, assumptions, red-team, design-contract, self-assessment
**Category**: observability
**Description**: (a) INV-004 "an observed solver invocation" and "positive solver-query/VC count" name no typed source; every candidate collides with a rule (console scraping banned; Boogie spawns z3 internally outside the allowlisted launcher; wrapping z3 in a shim changes the digest INV-003 records). (b) INV-011 "no solver launch" is unobservable with the real z3 configured — but is cleanly satisfiable by running P08/P09 with the solver path pointed at the recording sentinel and asserting zero invocations; the spec doesn't say so. (c) INV-007 "proven to be the options instance verification actually consumed" is unfalsifiable as written — reading `Compilation.Options` back is reference-equal to harness input, so the honest implementation is indistinguishable from the forbidden echo; the pre-review's stage-spanning formulation was diluted in revision. (d) P02 claims the pinned SDK "built/ran" the harness but enforcement is file-content only; `global.json` resolution follows process cwd, so `dotnet test` from repo root can ignore a spike-local global.json entirely while the test stays green. Fixes: define solver observation as typed per-task solver-produced outcomes (+ resource usage where exposed) with the RS-003 removal negative as the behavioral backstop, explicitly blessed in the spec; specify the sentinel mechanism for INV-011; for INV-007 require post-verification readback plus a normalization canary (at least one manifest option whose effective value is pipeline-normalized relative to input — e.g. resolved absolute solver path — so an input echo fails equality); for P02 record build-time SDK via the allowlisted launcher into evidence and assert runtime identity via typed APIs, and pin the invocation cwd/global.json location. Cross-cutting fail-closed rule: if the typed surface cannot supply required observability, the probe reports INCOMPLETE/INCOMPATIBLE-candidate and it becomes an ADR finding — never a silent downgrade to weaker evidence or scraping.
**Status**: accepted

## Finding RS-005: Deterministic projection is undefined and self-contradictory — the committed-sample equality test cannot work as written
**Severity**: CRITICAL (blocking)
**Source**: all six agents + self-assessment
**Category**: evidence-determinism
**Description**: INV-009 puts "git commit id + dirty flag", host RID, and SDK/runtime versions in the report and excludes only timestamps/durations from equality. A committed sample can never record the commit that contains it (and generating it dirties the tree), so if commit id is in the projection the equality test is permanently red — training developers to weaken it ad hoc (AP-005), which silently deletes the reproducibility anchor. RID/SDK coupling additionally guarantees flapping when DD-005 moves the suite to CI on a different environment. INV-010's parenthetical projection list differs from INV-009's. Fix: define a three-way field partition in a committed schema file whose digest appears in the report: (1) binding/environment identity — commit id, dirty flag, RID, SDK/runtime, loaded-assembly identities: recorded, validated (commit id asserted == HEAD at generation time by a separate check; pins validated where pinned), excluded from cross-run equality; (2) deterministic outcome projection — verdicts, probe results, node table, option manifest, input digests: the equality domain for the sample test and INV-010; (3) volatile — timestamps, durations. The equality test derives its comparison set from the schema file, so shrinking it is a reviewable diff; reclassifying any field to volatile requires a spec change. Scope the sample-equality test to the pinned RID with a loud "skipped: unproven RID" elsewhere.
**Status**: accepted

## Finding RS-006: Every "CI test assertion" enforcement label overstates its layer — all enforcement is voluntary until CI exists
**Severity**: HIGH
**Source**: design-contract, testability, assumptions, upgrade-compatibility, ux
**Category**: enforcement-layer (AP-004)
**Description**: Ten-plus invariants name "CI test assertion" while EA-004 admits no CI exists; the actual layer is voluntary local `dotnet test` — agent discipline, which PAT-004 says is not enforcement. Until DD-005, a false-positive COMPATIBLE ADR is producible by simply not running the suite (AP-013 shape). Fixes: (1) relabel enforcement fields "test assertion (local `dotnet test` until CI — EA-004; becomes CI assertion per DD-005)"; (2) the one structural mechanism available now: configure the Correctless workflow test command (currently "not configured") to run the spike suite so the workflow's verify gate executes it; (3) edit-time breadcrumb comments in `global.json`, the central package pins, and the provisioning digest constants: "changing this requires re-running the spike suite and committing the evidence diff — see DD-006/ADR-0001."
**Status**: accepted

## Finding RS-007: INCOMPATIBLE conflates reproduced integration failure with harness bug and environment fault — false-INCOMPATIBLE triggers the expensive fallback on a misread
**Severity**: HIGH
**Source**: ux, design-contract, red-team, testability, self-assessment
**Category**: verdict-taxonomy
**Description**: INV-006 maps "any exception, timeout, missing prerequisite, or skipped probe" to INCOMPATIBLE-or-INCOMPLETE with no cause taxonomy, but DESIGN.md permits rejecting the in-process boundary only with a reproduced integration failure on the official surfaces. A harness `NullReferenceException` from API misuse (a live risk — Route B's DI graph is hard to assemble) is observationally identical to a real net8-on-net10 failure and would improperly authorize the pinned-CLI fallback. Fixes: explicit verdict taxonomy — environment/prerequisite/tooling faults → INCOMPLETE with cause; in-process Dafny/Boogie API failures → INCOMPATIBLE-candidate carrying probe ID, stage, typed exception, and inputs, adjudicated in ADR-0001 before boundary rejection; top-level `verdict_reason` naming the first failing probe and stage; and red-team's control experiment: build the identical seam for net8.0 and re-run the failing probe — fails there too ⇒ harness bug, not a .NET 10 incompatibility. Require the control before any failure is citable as a reproduced integration failure. INCOMPATIBLE verdicts must embed the failing probe's typed diagnostic plus that run's P01–P04 identity evidence (an induced-failure test asserts the payload).
**Status**: accepted

## Finding RS-008: P03 loaded-assembly check is vacuous in both directions — expected-loaded set per route required
**Severity**: HIGH
**Source**: red-team, testability, assumptions
**Category**: identity-binding
**Description**: P03 verifies loaded ⊆ lock, not that expected assemblies loaded. .NET loads lazily: DafnyPipeline may never load even on Route B (it is largely resources + thin wrapper), and System.CommandLine — the spec's highest-rated runtime risk — may never load on a path that skips CLI parsing, satisfying INV-002's runtime assert vacuously. Also, beta4 and GA System.CommandLine can share assembly version 2.0.0.0, so only file SHA-256 discriminates; and no mapping rule exists from informational version (`4.11.0+sha`) to package version. Fix: committed per-route expected-loaded-assembly sidecar; P03 asserts set equality (expected-but-never-loaded = failure, not vacuous pass); identity comparison by file SHA-256 against package-sourced files; state the informational-version→package-version mapping rule; if a route legitimately never loads an assembly, the committed set says so and the runtime pin claim for that route downgrades explicitly to the lock-file assertion.
**Status**: accepted

## Finding RS-009: Expected-value sidecars self-certify first-run bugs — no independent shape tests
**Severity**: HIGH
**Source**: red-team, testability
**Category**: fixture-honesty
**Description**: Sidecars will in practice be generated by running the harness once and freezing output, so a recovery bug in the first run certifies itself forever (P06/P07/P10 inherit). INV-007's fixture-inventory requirements (four editable-proof forms, resolver-inferred ghost, expect contrast) have no enforcement: a fixture silently missing the inferred-ghost variable yields a sidecar missing that row, equality passes, and the subtlest recovery claim is never exercised. Fix: shape tests on the sidecar itself, independent of fixture equality — assert ≥1 entry per required proof form, ≥1 `inferred-ghost=true` entry with no `ghost` keyword at that declaration, exactly one `expect` entry classified compiled; plus a named AST-based fixture-hygiene test for ok.dfy's structural purity (no attributes/`assume`/suppression — currently mechanism-free prose).
**Status**: accepted

## Finding RS-010: Cross-route verdict aggregation is unowned and fail-open by natural implementation
**Severity**: HIGH
**Source**: red-team, ux
**Category**: verdict-integrity (AP-006, AP-001)
**Description**: Each route run emits its own evidence report; shared probes (P02, P04) belong to no route; no named component computes the overall verdict. The natural aggregator — iterate reports found on disk — is fail-open: a missing Route B report is simply not iterated (the mechanized RS-001). Keying by probe ID alone lets Route A's P06 satisfy Route B's slot (AP-006). The shared-vs-per-route expected count (24 vs 20+2) is ambiguous (UX-014). Fix: specify the aggregator component, derive its exact expected report set from the manifest's committed route list, use (probeID, route) composite keys, define the shared-probe instantiation exactly for one-route and two-route runs, and define mixed-outcome semantics per RS-001's decision.
**Status**: accepted

## Finding RS-011: Lifecycle obligations have no structural carriers — DD-005 CI permanence, DD-007 ADR promotion, DD-006 bump procedure
**Severity**: HIGH
**Source**: upgrade-compatibility, ux, design-contract
**Category**: lifecycle
**Description**: (a) DD-005's "the future CI spec must include this job" is prose with no machine-checkable carrier — commit a stub CI workflow under the spike dir that the CI feature must wire, or register the obligation in deferred-obligations tracking. (b) DD-007 names no promotion owner or trigger and no superseded/rejected transition; assign promotion to the final Phase 0.0 feature (DD-006's inheritance pattern) and define invalidation. (c) DD-006's bump procedure never says what gets re-captured (sidecars, committed sample, digests), which diff classes mean what, or who adjudicates — an unconstrained "update sidecars until green" bump reproduces the feared false positive at the first Dafny 4.12 bump. Require the bump change-set to contain pin diff + re-captured sidecars/sample + evidence diff + ADR amendment, with diff classes (node-classification/target-set changes = reviewable compat signal; durations = ignorable) and an adjudication step. (d) SDK-patch bumps are excluded from DD-006's trigger list but change P02 and recorded identities — extend DD-006 to any global.json/pin change with a defined (lighter) re-run procedure. (e) Add `evidence_schema_version` (and a manifest version field) to INV-009's required fields so bump diffs can distinguish schema evolution from behavioral drift.
**Status**: accepted

## Finding RS-012: The enforcement matrix collides with the LOC budget and local-only execution — descoping pressure lands on INV-006's armor
**Severity**: HIGH
**Source**: red-team, design-contract, assumptions, testability, self-assessment
**Category**: cost-honesty
**Description**: 12 probes × 2 routes at integration level + per-entry deletion tests + induced failures + run-twice determinism, each involving locked restore, build, child launch, and real Z3, will take tens of minutes locally — and local `dotnet test` is the only enforcement (AP-013 in practice: long suites stop being run). The ~900–1,400 LOC budget cannot absorb the matrix; overrun pressure will fall on the deletion/induced-failure tests that are the honesty mechanism. Fixes: deletion tests run at the verdict-logic unit level against synthetic completed-sets (fast, one per manifest entry, still anchored by RS-002's digest constant) plus a small named subset of true end-to-end induced failures (absent z3, unreadable fixture, one forced probe exception); state in-spec that deletion tests are non-trimmable against budget; revise the LOC budget honestly now; add the DESIGN.md-mandated calendar time-box with an on-expiry rule (unresolved = INCOMPLETE + scope decision, never quiet descoping).
**Status**: accepted

## Finding RS-013: "TB-003-adjacent" is invented — the spec designs a real trust boundary that needs a TB-004 entry, and ARCHITECTURE.md propagation obligations are unstated
**Severity**: MEDIUM
**Source**: all six agents
**Category**: architecture-registry
**Description**: TB-003 covers outbound release provenance; inbound dev-time toolchain intake (NuGet packages, Z3 assets, SDK) is a genuinely new boundary the spec fully designs (BND-001/002 + STRIDE) but hangs off a nonexistent label. The intensity block's claim that the trust-boundary keyword match was "a false positive: the feature cites a boundary, it does not design one" is wrong and should be corrected in the record. Fixes: add TB-004 "Inbound toolchain supply chain" to ARCHITECTURE.md seeded from BND-001/002 + STRIDE (DD-006 makes it a standing obligation future bump specs cite); name the ARCHITECTURE.md component-table update as an explicit ADR-0001 propagation obligation (a Route-A-selected ADR contradicts "uses DafnyCore, DafnyPipeline" as written — same for DESIGN.md per the Conventions section); propagate the new durable conventions (spikes/ directory class, docs/adr/, evidence-schema field partition, DD-005/DD-006 standing obligations) to ARCHITECTURE.md Conventions at feature completion.
**Status**: accepted

## Finding RS-014: Restore/build substrate under-hardened — locked-mode proven by grep, MSBuild inheritance unblocked, NUGET_PACKAGES unmodeled
**Severity**: MEDIUM
**Source**: testability, red-team, assumptions
**Category**: supply-chain
**Description**: (a) INV-001's locked-mode enforcement is script-content-shaped (keyword presence, AP-011); asserting NuGet.Config content doesn't prove it was applied. Fix: behavioral negative test — mutate a lock copy (bump DafnyCore to a nightly), run the evidentiary restore path, assert failure; record the exact restore argv in evidence via the allowlisted launcher. (b) A future repo-root `Directory.Build.props`/`Directory.Packages.props` silently applies to spike projects (MSBuild upward walk; NuGet `<clear/>` doesn't stop it) and can alter pins without touching `spikes/`. Fix: spike-local empty import-terminating props files + a test asserting the effective build inherits nothing from above the spike root. (c) `NUGET_PACKAGES` env can redirect the packages folder around the spike-local one; `<clear/>` doesn't clear machine-level `fallbackPackageFolders`. Declare/sanitize in the EA set; locked-mode content hashes bound the residue.
**Status**: accepted

## Finding RS-015: Wall-clock bound has no value, owner, or implementable mechanism at the claimed layer
**Severity**: MEDIUM
**Source**: testability, red-team, assumptions, design-contract
**Category**: failure-mode
**Description**: INV-006's "total wall-clock is bounded" names no bound; an in-process cooperative timer can't kill a hung Boogie+Z3 chain (process-tree management is deferred to bullet 7), so INCOMPLETE-on-timeout is unimplementable as stated (AP-004). Fix: commit the bound beside the manifest; assign enforcement to the test-harness layer — tests launch the route harness child with a timeout, kill the process tree on expiry, and record INCOMPLETE themselves; induced-timeout test shrinks the committed bound below a known-slow probe and asserts INCOMPLETE.
**Status**: accepted

## Finding RS-016: Hygiene enforcement is narrower than PRH-005's prohibition and can self-violate
**Severity**: MEDIUM
**Source**: red-team, testability, design-contract, ux
**Category**: hygiene
**Description**: The grep covers `/home/`, `C:\`, and "the local username pattern" but PRH-005 also bans hostnames and machine details (nothing greps them), and `/Users/`, `/tmp/` are uncovered. A test that hardcodes the local username commits the username (PRH-005 self-violation); it must derive username/hostname at runtime (`Environment.UserName`, `$(hostname)`). Cross-machine, the grep cannot catch the generating machine's leaked username — state that as a known limit of the layer (AP-004 discipline) with human review per AGENTS.md as backstop. Add an anti-hardcode test: recompute each reported assembly SHA-256 from the file at its repo-relative path and assert equality.
**Status**: accepted

## Finding RS-017: Operator surface unspecified — run procedure, artifact lifecycle, atomic reports, exit codes, human entry point
**Severity**: MEDIUM
**Source**: ux, upgrade-compatibility
**Category**: operator-experience
**Description**: (a) No deliverable documents how to run the spike (entry-point command, provision→restore→build→test ordering); prerequisite failures must name remediation. Add a README/run doc; name one canonical setup/run script. (b) Provisioning has no idempotency/partial-state contract — test from clean, partial (truncated file), and full states plus double-run identity (AP-016). (c) Unsupported-RID behavior undefined: provisioning must fail closed with "RID not supported; proven RIDs: linux-x64 (see ADR-0001)". (d) The committed sample has no regeneration affordance (AP-005): define the regeneration command, output-vs-committed paths, and review step; equality-failure output names it. (e) Run products (spike-local packages folder, z3 binary, sentinel artifacts, fresh reports) have no lifecycle: specify an untracked out/ dir, a distinct committed-samples location, and a required .gitignore — public repo, real risk of committing the z3 binary or package cache. (f) Interrupted runs can leave truncated JSON (AP-009): require atomic write-temp-then-rename emission and consumer treatment of missing/malformed reports as a distinct state, never pass or refutation. (g) No exit-code contract: distinct codes for COMPATIBLE / INCOMPATIBLE / INCOMPLETE / crashed-before-report (future CI + the child-process integration tests both need this). (h) No human entry point: require a terminal verdict summary per route (failed probes + report path) and ADR evidence citations as repo-relative paths, one per claim. (i) Update AGENT_CONTEXT.md's Quick Reference / workflow test command once the suite exists (composes with RS-006).
**Status**: accepted

## Finding RS-018: Content pins diluted or dropped in revision — INV-012 differential, Option Manifest values, INV-005 outcome granularity
**Severity**: MEDIUM
**Source**: red-team, testability, assumptions
**Category**: revision-regression
**Description**: (a) INV-012 asserts closure/target distinction only "under the Option Manifest's chosen setting", dropping the pre-review's both-values-of-`--verify-included-files` differential; if the chosen setting makes the sets coincide, discriminating power collapses to the echo check. Pin the setting to a value where the sets differ, or restore the both-values assertion for P12. (b) The frozen Option Manifest dropped "Boogie prover options" from the pre-review enumeration and leaves time/resource-limit values unpinned; DESIGN.md's Phase 0.1 doctrine is no solver time limit + resource limits as semantics — pin manifest values to that posture now or the spike proves compatibility under an option surface Phase 0.1 won't use. Option categories need exact ID→value pairs in the committed manifest file (the file, not spec prose, is the oracle). (c) INV-005: state that if the typed outcome cannot distinguish completed refutation from timeout/undetermined, P07 fails; define the legitimate location-granularity fallback (assertion token vs enclosing statement) instead of leaving loosening to the implementer.
**Status**: accepted

## Finding RS-019: Seam containment leaks — transitive package flow, public-API surface, contaminated test host
**Severity**: MEDIUM
**Source**: testability, design-contract, red-team
**Category**: seam-enforcement
**Description**: (a) ProjectReference flows the seam's Dafny package refs transitively: consumers can `using Microsoft.Dafny` and compile with no PackageReference of their own — the csproj scan passes; the grep is load-bearing, not a backstop. Add `PrivateAssets="all"` (or `DisableTransitiveProjectReferences`) to make the leak a build failure. (b) INV-008's violated-when includes Dafny types leaking through seam public signatures but nothing enforces it: add a reflection test enumerating the seam's public API (params, returns, fields, bases) asserting none originate from Dafny/Boogie assemblies. (c) The test project references the seam, recreating the contaminated graph DD-003 warns about inside the test host: split result/evidence-schema types into a third Dafny-free assembly; tests reference only that — "tests never load Dafny" becomes project-graph-enforced.
**Status**: accepted

## Finding RS-020: EA-set gaps and residual corrections (batch)
**Severity**: LOW / ADVISORY
**Source**: assumptions, upgrade-compatibility, design-contract, red-team, ux
**Category**: environment-assumptions
**Description**: Add/correct: (a) EA for solver env hygiene — `PROVER_PATH` unset, no z3 under route output trees (composes with RS-003). (b) EA for locale/canonical serialization — invariant culture, stable ordering/encoding rule for evidence. (c) EA for glibc floor of the official Z3 linux-x64 asset (pre-review item dropped in revision). (d) EA for provisioning host tooling (shell, downloader, sha256 util, git for the commit-id field). (e) EA for dotnet env vars (`DOTNET_ROOT`, `NUGET_PACKAGES`, telemetry opt-out). (f) Fix EA-003's self-contradiction: the evidentiary run includes P01 (restore), so the run is online-then-offline by phase — state the phase split; soften the unverified "verification runs offline" claim or list it as an ADR residual (no mechanism tests it). (g) Note framework-dependent publish means runtime patch identity floats independent of the SDK pin — record actual runtime in binding fields (RS-005 partition). (h) Artifact-availability residual: DD-005/006 make nuget.org 4.11.0 packages and the Jan-2023 Z3 asset a permanent availability dependency; record in ADR residuals with an explicit vendoring/caching position (even "rejected, consequence accepted"). (i) Reword DD-006's "concrete mechanism for PAT-001's upgrade-rerun rule" — the spike covers bullets 1–3 only, not ownership/closure/bypass/fingerprint suites (false-confidence overclaim). (j) ADR states the selected route's lock is the model for PAT-001's single production lock (plural-lock end state defined). (k) Cheap cross-assert: locked Boogie version equals DafnyCore's declared lower bound read from package metadata. (l) Fixture promotion mechanism: Phase 0.1 references fixtures in place (or re-captures with digest continuity recorded). (m) Drop/qualify "disposable" in Context (conflicts with DD-005/006 permanence; deletion of `spikes/dafny-compat/` would strand ADR citations). (n) ADR verdict names proven capabilities alongside the bullet citation, pinned to the DESIGN.md version (bullet renumbering hazard). (o) Extend PRH-004's allowlist enumeration to the test harness's child launches of the route executables and the git invocation for evidence (currently outside the allowlist as enumerated). (p) Correct the research-brief citation "no third-party tool embeds these packages" to weak-sample evidence (three tools observed).
**Status**: accepted

---

# Addendum: Final External Review (codex, direct invocation on v3)
Date: 2026-07-20 (post-/creview-spec, user-requested)
Reviewer: codex gpt-5.6-sol xhigh — full text at .correctless/artifacts/reviews/dafny-compat-spike-codex-final-review.md
Disposition (human gate 2026-07-20): EXT-101..113 accepted; EXT-114 accepted as re-budget-only (LOC ~3,000–5,000, time-box 20 working days, no machinery trimmed); EXT-115 accepted for the P07 containment rule + stable-ID deferred obligations. All folded into spec v4.

## EXT-101 (blocking): Stale-report replay — reports not bound to the current run → run_id + run dir + launch-bound aggregation. **Status**: accepted
## EXT-102 (blocking): Single-seam/two-route topology impossible; PrivateAssets="all" strips runtime assets → RouteA/RouteB seam projects + compile-only privacy + negative compile test. **Status**: accepted
## EXT-103 (blocking): net8 adjudication logically invalid → 3-way classification (HOST_RUNTIME_INCOMPATIBILITY / OFFICIAL_API_CAPABILITY_GAP / HARNESS_FAULT); control adjudicates only the first. **Status**: accepted
## EXT-104 (blocking): net8 control lacks a controlled net8 runtime; "identical assembly" impossible across TFMs → identical source/graph/options/fixtures on a pinned net8 runtime host with execution proof; else INCOMPLETE. **Status**: accepted
## EXT-105 (blocking): Normalization canary refuted by v4.11.0 source (Compilation.Options aliases Input.Options) → source-verified transformation named in RED, else capability-gap candidate. **Status**: accepted
## EXT-106 (major): Decoy test contradicts startup gate; no zero-invocation channel; PROVER_PATH is a prover option not an env var → always-on whitelisted nonce-recording decoys + pre-created zero-count ledger + typed prover-option inspection. **Status**: accepted
## EXT-107 (major): Shared-probe ownership vs 22-entry math vs exit codes → aggregator owns P02/P04; typed verdict_reason union; unknown exits/missing reports map to crashed-before-report. **Status**: accepted
## EXT-108 (major): Loaded-assembly equality universe undefined → .deps.json-derived universe, shared framework excluded, route anchors mandatory, package-asset hash tracing. **Status**: accepted
## EXT-109 (major): NuGet/MSBuild facts wrong (Version= is a range; "empty" packages.props contradiction; targets/rsp unsealed; any-failure lock test) → bracket pins, authoritative non-empty packages.props, full sealing set, NU1004-class-specific negative test, downgrade-as-error. **Status**: accepted
## EXT-110 (major): Sidecar still self-certifies inference → independent parser+resolved AST test + add-`ghost` mutation differential. **Status**: accepted
## EXT-111 (major): Schema bound only by version string → schema SHA-256 in every report, closed schemas, total classification, digest-bound oracles. **Status**: accepted
## EXT-112 (major): 30-min bound not total → single monotonic runner deadline covering all phases + outer watchdog. **Status**: accepted
## EXT-113 (major): One-launcher bootstrap cycle → two-layer allowlist (bootstrap script + managed launcher), allowlist-constructed env. **Status**: accepted
## EXT-114 (major): Budget not credible (realistic 3,000–5,000 LOC) → re-budget only: LOC raised, time-box 20 working days, machinery kept. **Status**: accepted (modified)
## EXT-115 (minor): Exact-line P07 too strict; stub files/prose aren't enforcement → containment rule; stable-ID deferred obligations for DD-005/DD-007. **Status**: accepted
