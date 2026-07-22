# Spec: .NET 10 / Dafny 4.11.0 Package-Compatibility Spike (Phase 0.0, bullets 1–3)

## Metadata
- **Created**: 2026-07-20T22:57:00Z
- **Status**: approved
- **Impacts**: none (first feature; informs every later Phase 0.x spec)
- **Research**: .correctless/artifacts/research/dafny-compat-spike-research.md
- **External pre-review**: .correctless/artifacts/reviews/dafny-compat-spike-codex-prereview.md (codex gpt-5.6-sol xhigh, 16 findings, all applied 2026-07-20)
- **Review**: .correctless/artifacts/review-spec-findings-dafny-compat-spike.md (/creview-spec 2026-07-20: 6 adversarial agents; codex leg skipped — upstream correctless#199/#200/#268; findings RS-001..RS-020 all accepted and folded into v3)
- **Final external review**: .correctless/artifacts/reviews/dafny-compat-spike-codex-final-review.md (codex gpt-5.6-sol xhigh on v3, 2026-07-20: 15 findings — F1–F13 accepted, F14 re-budget-only, F15 containment rule — folded into v4)
- **Convergence rounds**: R2 (codex xhigh on v4, 2026-07-20; .correctless/artifacts/reviews/dafny-compat-spike-codex-r2.md): 3 blocking + 8 major + 1 minor, all accepted → v5. R3 (codex xhigh on v5, 2026-07-20; .correctless/artifacts/reviews/dafny-compat-spike-codex-r3.md): 5 blocking + 5 major + 2 minor, all accepted → v6. R4 decision round (codex xhigh on v6, 2026-07-20; .correctless/artifacts/reviews/dafny-compat-spike-codex-r4.md): NOT CONVERGED with an explicit minimal flip set R4-01..07 (+ non-gating R4-08..11) — all applied → this v7; convergence loop closed per user disposition
- **QA amendments** (round 1, dispositions 2026-07-21): QA-006 → committed-sample *pair* with schema-declared suite-status mask (see INV-009); QA-010 oracle-edit adjudications ratified post-hoc: `ok.sidecar.json` `min_task_count` 3→2 (AP-014 real-producer recapture, to be independently confirmed in fix round 1), `syntax-error.dfy` planted-token line normalization, expected-loaded universe note **reversed** by QA-004 (universe widened back to the codex-F8 definition). Fixture list fold-in (stdlib-anchor.dfy) recorded at BND-003 per OQ-004.
- **Recommended-intensity**: high
- **Intensity**: high
- **Intensity reason**: project floor `high`; TB-xxx reference signal (inbound toolchain supply-chain boundary, TB-004 proposed by this feature); antipattern-overlap signal (AP-010/013/014/015 all apply). Keyword scan matched "trust boundary" (a *critical*-tier keyword). Correction (RS-013): the earlier "false positive — the feature cites a trust boundary, it does not design one" assessment was wrong; this feature *does* design an inbound-toolchain trust boundary (BND-001/002 + STRIDE), now registered as TB-004. Intensity `high` retained (boundary is dev-time tooling, not the runtime policy TCB). Low-confidence qualifier: 0 completed features of project history.
- **Override**: none

## Context

The entire Corrected architecture (DESIGN.md §12) rests on one unvalidated
assumption: that Dafny 4.11.0's `net8.0` NuGet assemblies (`DafnyCore`,
`DafnyPipeline`) work **in-process on a .NET 10 host** for the real APIs the
worker needs — parse, resolve, verify via Boogie + native Z3, and resolved-AST
recovery. NuGet computes compatibility; nobody has proven it (research
finding, weak-sample: none of the three surveyed third-party tools embeds
these packages from NuGet — the ecosystem vendors source or shells out).
DESIGN.md gates all of Phase 0.1 on this spike and permits rejecting the
in-process boundary *only* with a reproduced integration failure. The spike
builds a tracked, non-production harness that produces **evidence and
ADR-0001**, not production code — but per DD-005/DD-006 the suite is
*permanent* conformance infrastructure, not a disposable prototype. Two
outcomes are harmful and both are guarded here: a **false COMPATIBLE** (a
spike that "passes" without exercising the surfaces that matter) and a
**misadjudicated INCOMPATIBLE** (a harness bug or environment fault read as a
reproduced integration failure, wrongly triggering the documented fallback —
pinned Dafny CLI or minimal C# semantic worker; see INV-013). A genuine
incompatibility discovered here is a *successful* spike outcome. The external
pre-review and the six-agent /creview-spec pass both attacked the
false-verdict failure modes; their findings are folded in below.

## Scope

**In scope** (DESIGN.md Phase 0.0 bullets 1–3, per brainstorm decision):

1. Route-isolated net10.0 C# harness projects + one test project under
   `spikes/dafny-compat/`, with exact package locks for the Dafny stack
   (`DafnyCore` 4.11.0, `DafnyPipeline` 4.11.0, `DafnyDriver` 4.11.0 where the
   route uses it, `Boogie.ExecutionEngine` 3.5.5) — one route-seam
   implementation project + lock per integration route
   (`SpikeDafnyAdapter.RouteA`/`.RouteB`, codex F2), **both routes mandatory
   to attempt** (DD-001), plus a Dafny-free contracts assembly for
   result/evidence types (INV-008).
2. Scripted, digest-verified provisioning of Z3 4.12.1 (idempotent,
   partial-state-safe), plus proof that the provisioned binary is the one the
   verifier actually executes (INV-003).
3. In-process parse → resolve → Z3-backed verify of committed `.dfy` fixtures:
   provable (`ok.dfy`), refutable (`bad.dfy`), classification
   (`classify.dfy`), malformed (`syntax-error.dfy`), unresolvable
   (`resolve-error.dfy`), a spike-only include pair
   (`incl-root.dfy`/`incl-leaf.dfy`) for closure recovery, and a spike-only
   Route B standard-library anchor (`stdlib-anchor.dfy`) for the OQ-004
   DafnyPipeline consumption removal/differential (added during RED per
   OQ-004's mandate; disposition 2026-07-21).
4. Resolved-AST recovery: symbol names, declaration kinds, proof-vs-executable
   node classification (including the four Phase 0.1 editable-proof forms),
   resolver-*inferred* ghostness, the effective-options manifest, and the
   parsed/resolved source closure.
5. A machine-readable, schema-versioned evidence report bound to the probe
   manifest and actual loaded identities via a committed three-way field
   partition (INV-009), a named cross-route verdict aggregator (INV-006), and
   a **provisional** `docs/adr/ADR-0001-dafny-integration-boundary.md`
   recording the per-route verdicts, selected boundary, pinned identities,
   unsupported constructs, failure behavior, and residuals, scope-qualified to
   bullets 1–3 (DD-007).
6. Operator surface (RS-017): a README/run doc under `spikes/dafny-compat/`
   naming the canonical entry point and phase ordering (provision → locked
   restore → build → test), an untracked `out/` area + `.gitignore` for run
   products, atomic report emission, an exit-code contract, per-route console
   verdict summaries, a committed-sample regeneration procedure (DD-008), and
   a stub CI workflow file the future CI feature must wire (DD-005).
7. Architecture propagation (RS-013): TB-004 "Inbound toolchain supply chain"
   added to `.correctless/ARCHITECTURE.md` with this feature; component-table
   and Conventions updates are named ADR-0001 propagation obligations (DD-007).

**Out of scope** (deferred to later Phase 0.0 features): editable-proof
erasure checks and planted-bypass rejection (bullet 4), protected-surface
fingerprint determinism (bullet 5), CLI-vs-adapter differential comparison
(bullet 6), Z3 resource-count determinism and watchdog semantics (bullet 7),
persistent search-verifier reuse (bullet 8), JSONL protocol prototype
(bullet 9), Pi adapter modes (bullet 10), release distribution (bullet 11),
full mutation corpus (bullet 12). Production `src/` stays empty; the
production `DafnyAdapter` is written fresh in Phase 0.1 informed by ADR-0001.
No CI pipeline exists yet; the spike runs locally until CI lands (DD-005).

## Complexity Budget
- **Estimated LOC**: ~3,000–5,000 C# (contracts + two route-seam implementations + harnesses + canonical runner/aggregator + net8 control + tests) + ~10 fixtures/sidecars + provisioning & run scripts + evidence-schema file. Revised upward twice (RS-012; codex F14 — disposition: re-budget without trimming machinery): the deletion/induced-failure matrix, route isolation, non-vacuity, solver identity, aggregator, and sentinel machinery are load-bearing and **non-trimmable against budget** — if the budget binds, scope negotiation happens via spec change, never by silently dropping INV-006's test armor.
- **Files touched**: ~30–42, all under `spikes/dafny-compat/`, `docs/adr/`, and `.correctless/` (plus the TB-004 ARCHITECTURE.md entry).
- **New abstractions**: 1 logical seam boundary realized as 3 assemblies — route-seam implementations `SpikeDafnyAdapter.RouteA`/`.RouteB` (each owning its route's Dafny/Boogie packages) + the shared Dafny-free contracts assembly (codex F2: a single package-owning seam project cannot serve two uncontaminated route graphs).
- **Trust boundaries touched**: 1 (TB-004: inbound toolchain supply chain — registered by this feature). TB-001/TB-002/TB-003 explicitly untouched.
- **Time-box** (RS-012, re-budgeted per codex F14; DESIGN.md "time-boxed spike"): 20 working days from TDD start. On expiry: unresolved probes stand as INCOMPLETE, the run is committed as-is, and the overrun escalates to an explicit scope decision — never quiet descoping of the test matrix (DD-009).
- **Risk surface delta**: low (isolated spike directory; nothing ships)

## Probe Manifest (spec-owned — INV-006 binds to this list)

The mandatory probe set AND the mandatory route plan are defined HERE, not by
the harness. The harness commits this manifest as a versioned file
(`manifest_version` field); the manifest's SHA-256 is **hard-coded as a
constant in the test suite** (RS-002) and asserted against both the committed
manifest file and every evidence report — so removing or altering a probe or
route requires a three-point change (spec, manifest, test constant), which is
auditable. Removing, renaming, or adding a probe or route is a spec change.

**Route plan (committed in the manifest)**: routes `A` and `B` are both
mandatory to attempt (DD-001). Probe instances are keyed by the composite
`(probeID, route)` (AP-006). **Ownership follows execution topology** (codex
F7, R2-4, R3-5): the **bootstrap controller** — the first thing that runs —
mints the `run_id`, creates the run root, holds the **single absolute
monotonic deadline through final aggregation**, and emits nonce-bound
restore/build receipts; the **managed aggregator** (built mid-run) *validates*
those receipts, it never claims to have performed them. P01 is
controller-attested and **partitioned by route** (codex R4-07 — "every
restored project" in both entries would let a Route B lock failure veto
Route A, contradicting the per-route verdict policy): P01(A) covers Route
A's seam, harness, and control projects **plus** the genuinely shared
contracts/aggregator/test projects; P01(B) analogously. A shared-project
restore failure may fail both P01 entries; a route-specific failure affects
only its route. Each entry binds its locks, the exact restore argv, exit
result, and generated assets to the run_id. P02/P04 are shared controller/aggregator-attested probes (P02
= build-time identity of every built artifact; per-route *runtime* identity
is P03's — codex R3-1); **route children** own P03 and P05–P12 only. Duplicate keys in the raw report array are rejected before
dictionary conversion. The expected completed set for a full run is exactly:
9 child probes × 2 routes + 2 controller-attested P01 instances + 2 shared
probes = **22 composite-keyed entries**. The final run report is published
only after the entire test suite has exited consistently — an all-pass probe
report accompanied by a failing `dotnet test` is not citable as COMPATIBLE
(codex R3-5). Every receipt carries the
run's `run_id` (codex F1); reports from another run_id are rejected. A run
missing any mandatory route's report is INCOMPLETE (INV-006).

| ID | Probe | Route key |
|----|-------|-----------|
| P01 | restore-identity — locked restore succeeded; resolved package set matches pins | per route (runner-attested) |
| P02 | sdk-build-identity — global.json-pinned SDK is the one that built every artifact (build-time records; runner-attested; per-route *runtime* identity lives in P03 — codex R3-1) | shared |
| P03 | loaded-assembly + runtime identity — runtime-loaded assemblies match the committed per-route expected-loaded set AND the lock (set equality, not subset — INV-002/RS-008); actual runtime module identity recorded per route (codex R3-1: route verdicts consume the shared build status plus **their own** runtime status only, so one route's runtime fault cannot contaminate the other) | per route |
| P04 | solver-identity — provisioned Z3 exists at its non-discoverable install location, digest matches pin, banner reports 4.12.1 | shared |
| P05 | solver-path-honored — sentinel-solver test with per-run nonce proves the solver-path option selects the executed binary | per route |
| P06 | verify-ok — ok.dfy verifies, non-vacuously (expected targets + typed solver-produced outcomes) | per route |
| P07 | verify-bad — bad.dfy refuted by a completed solver result at the planted location | per route |
| P08 | frontend-syntax — syntax-error.dfy yields stage=parse typed diagnostic; sentinel-configured run records zero solver invocations | per route |
| P09 | frontend-resolve — resolve-error.dfy yields stage=resolution typed diagnostic; sentinel-configured run records zero solver invocations | per route |
| P10 | ast-recovery — classify.dfy resolved-node table matches committed expected values (sidecar shape-tested independently, RS-009) | per route |
| P11 | options-manifest — post-verification readback matches the frozen Option Manifest file, with normalization canary | per route |
| P12 | closure-recovery — include pair's closure constant across both values of the included-files verification option; target set varies accordingly | per route |

## Invariants

### INV-001: Exact-pin, source-controlled restore of the whole toolchain
- **Type**: must
- **Category**: data-integrity
- **Statement**: Every `Dafny*` and `Boogie.*` package in any spike project's resolved graph is exact-pinned (DafnyCore 4.11.0, DafnyPipeline 4.11.0, DafnyDriver 4.11.0 where used, Boogie.ExecutionEngine 3.5.5), with a committed `packages.lock.json` per project, `RestorePackagesWithLockFile` enabled, and locked-mode restore. A spike-local `NuGet.Config` with `<clear/>`, nuget.org as the sole source, and package source mapping governs restore (`--configfile`); evidentiary restores use a spike-local packages folder. A committed `global.json` pins one exact .NET 10 SDK with `rollForward: disable` and `allowPrerelease: false`; the invocation working directory is pinned so this `global.json` is the one that resolves (RS-004d). Central Package Management is authoritative and correctly laid out (codex R2-6, certain per CPM docs): `ManagePackageVersionsCentrally=true`, `CentralPackageTransitivePinningEnabled=true`, **`CentralPackageVersionOverrideEnabled=false`** (the exact property — codex R3-8) with a scan rejecting any `VersionOverride` attribute; exact singleton-range constraints (`[4.11.0]`, `[3.5.5]`, `[2.0.0-beta4.22272.1]` — plain `Version="4.11.0"` is a minimum-inclusive range, codex F9) live on `PackageVersion` entries in the authoritative spike-local `Directory.Packages.props`, and `PackageReference` items are **versionless**; NuGet downgrade warnings are treated as errors. Spike-local **sealing files** terminate MSBuild's upward inheritance for props, targets, and response files (`Directory.Build.props`, `Directory.Build.targets`, `Directory.Build.rsp` — an empty props file alone does not block the others, codex F9 — plus `-noAutoResponse` on every restore/build/test invocation, since a local rsp does not disable the SDK-adjacent `MSBuild.rsp`, codex R3-8); the spike-local `Directory.Packages.props` is **non-empty and authoritative**: it holds the central transitive pins (including System.CommandLine) and seals inheritance (RS-014b, codex F9).
- **Boundary**: TB-004 (BND-001)
- **Violated when**: any Dafny/Boogie-family package floats or resolves off-pin; restore succeeds despite lock mismatch; a machine/user NuGet source or inherited MSBuild props leak into resolution; the SDK rolls forward silently.
- **Enforcement**: test assertion (local `dotnet test` until CI — EA-004/DD-005) — tests parse each `packages.lock.json` and assert exact versions for the whole Dafny/Boogie family (P01); a test asserts `NuGet.Config` has `<clear/>` + single source + mapping and `global.json` has the required fields (P02); a **behavioral negative test** first performs a successful control restore, then mutates only a lock-consistency field (bump DafnyCore to a nightly) and asserts restore fails **with the specific locked-mode mismatch diagnostic** (NU1004-class) — any-failure (network, missing package) must not satisfy the test (RS-014a, codex F9, AP-011); the exact restore argv is recorded in evidence via the allowlisted launcher; a test asserts the effective build inherits no props from above the spike root.
- **Guards against**: AP-015, AP-011
- **Test approach**: unit + integration (negative restore)
- **Risk**: high

### INV-002: System.CommandLine resolves to Dafny's required beta, structurally
- **Type**: must
- **Category**: data-integrity
- **Statement**: The resolved graph of every spike project contains `System.CommandLine` at exactly `2.0.0-beta4.22272.1`, enforced by an explicit central transitive pin (DafnyCore's own dependency is a `>=` lower bound, so an unpinned graph can silently lift it to the binary-incompatible GA 2.0.0). No spike code uses System.CommandLine APIs. Runtime identity discrimination is by **file SHA-256** (beta4 and GA can share assembly version 2.0.0.0 — RS-008); P03 asserts against the committed per-route expected-loaded-assembly set over a **defined universe** (codex F8): trust-relevant managed assets selected through the route's `.deps.json`, excluding shared-framework assemblies. P03's pass predicate additionally proves the route **ran as a net10.0 application on .NET 10** (codex R4-03 — recording identity is not gating on it): harness `TargetFrameworkAttribute` equals net10.0, the committed runtimeconfig's single framework reference and roll-forward policy match, `Environment.Version.Major == 10`, and the selected `hostfxr` + `System.Private.CoreLib` identities (location + digest) match expectations — runtime *patch* remains binding-only per EA-001; report-mutation tests (wrong TFM, wrong runtime major, wrong CoreLib location) assert P03 failure. Route anchors are mandatory — Route A: DafnyDriver + DafnyCore observed loaded; Route B: DafnyCore + a **named DafnyPipeline consumption on the verification path** (exact API/resource identified during RED per OQ-004, with a removal/differential test proving the consumption matters — a bare `Assembly.Load` does not satisfy the anchor, codex R2-11; if no real consumption exists, the ADR must state Route B proves DafnyCore only and must not claim DafnyPipeline was exercised) — and every loaded package assembly is traced through `.deps.json` to the isolated spike-local package asset and hash-compared, so a locally substituted binary fails even with a matching version string. Any loaded Dafny/Boogie-family assembly outside that mapping fails P03; an expected-but-never-loaded assembly is a failure, never a vacuous pass. If a route's committed expected set legitimately excludes System.CommandLine (never loaded on that route's paths), the runtime pin claim for that route explicitly downgrades to the lock-file assertion and the evidence says so.
- **Boundary**: TB-004 (BND-001)
- **Violated when**: the lock resolves System.CommandLine to any other version; spike source imports the namespace; the pin exists only as an unenforced comment; the runtime assert passes vacuously because the assembly never loaded.
- **Enforcement**: test assertion (local `dotnet test` until CI — EA-004/DD-005) — lock-file version assert (P01) + expected-loaded-set equality assert at runtime with SHA-256 identity (P03) + source grep for the namespace. The informational-version→package-version mapping rule (strip `+<sha>` build metadata before comparison) is stated in the evidence schema (RS-008).
- **Guards against**: AP-015, AP-010
- **Test approach**: unit
- **Risk**: critical (fails only at runtime, not restore)

### INV-003: The executed solver is the provisioned Z3 4.12.1 — proven, not presumed
- **Type**: must
- **Category**: security
- **Statement**: A provisioning script fetches the official Z3 4.12.1 release asset for the host RID, verifies its SHA-256 against a pinned digest committed in the repo, and installs it **outside every ambient discovery location** — in particular NOT at the assembly-adjacent path `{output}/z3/bin/z3-4.12.1` that Dafny's fallback discovery searches (RS-003a; a same-file collision would blind the removal test to broken option wiring). The harness passes the solver location explicitly and never relies on ambient discovery. Before verification probes run, the harness asserts the environment is sane: apart from the **sanctioned decoy fixtures** (below), no z3-named executable on PATH or under any route's output tree, and the typed Boogie prover-option list contains no duplicate or conflicting solver-path entries (RS-003e, EA-005; codex F6 — `PROVER_PATH` is a Boogie prover *option*, not a confirmed environment variable; the env-var rule applies only if source inspection confirms one exists). One shared, unit-tested function builds the solver-path option for every probe (sentinel and real runs alike), narrowing the sentinel→real-run composition gap (RS-003c). Execution identity is proven by: (a) the **sentinel-solver probe** (P05) pointing the solver-path option at a recording stub whose recording carries a **per-run nonce**, written to a **pre-created nonce-bound ledger initialized to count zero before the run** — so a zero-invocation assertion (INV-011) reads a file that exists, and a stale recording from a prior run cannot satisfy this run (RS-003d, codex F6); (b) **always-on decoys** (codex F6, resolving the v3 decoy-vs-startup-gate contradiction): differently-hashed, nonce-recording `z3-4.12.1` decoys at the assembly-adjacent path and first-on-PATH are *expected fixtures of every real verification run*, whitelisted by the startup gate, with every run asserting **zero decoy invocations**; (c) a **per-route removal test** that removes/renames the provisioned z3 and asserts **P06 and P07 individually record solver-unavailable failure** (not merely a non-COMPATIBLE overall verdict, which P04 alone would force — the wrong-reason pass of AP-010, RS-003b). The evidence report records the solver path passed and its digest. The residual — sentinel and real runs are separate invocations, so composition is inductive — is recorded explicitly in ADR-0001's residual list.
- **Boundary**: TB-004 (BND-002)
- **Violated when**: the sentinel probe passes on a stale recording; verification succeeds with the pinned z3 removed or with the decoy present; a z3 exists at a discoverable location; digest check is skipped or advisory.
- **Enforcement**: gate precondition in the harness (startup assert, fail-closed) + P04/P05 + per-route removal test + decoy test (both in the named end-to-end induced-failure subset, RS-012).
- **Guards against**: AP-003, AP-010, AP-015
- **Test approach**: integration
- **Risk**: high

### INV-004: Provable fixture verifies end-to-end, non-vacuously [integration]
- **Type**: must
- **Category**: functional
- **Statement**: The harness takes `fixtures/ok.dfy` through in-process parse → resolve → Boogie translation → Z3 verification with **zero parse/resolution errors**, and the probe asserts the *expected shape*, committed beside the fixture: the exact named verification targets (count and names), a task count ≥ the committed sidecar value > 0, and — the defined meaning of "solver invocation observed" (RS-004a) — **every task's outcome is a solver-produced outcome enum value** recovered from typed Boogie/Dafny result objects (per-task verification results reached via the typed compilation-event/result API), with `solver_resource_usage_observed=true` in the deterministic projection where the typed surface exposes usage — the **raw counts are a nondeterministic observation outside the equality domain** (resource-count determinism is expressly bullet 7; raw counts in the projection would make INV-010 silently require it — codex R4-09). No process-level observation is claimed; the INV-003 removal test is the behavioral backstop proving these outcomes are unobtainable without the pinned solver. **Fail-closed observability rule (RS-004, cross-cutting; refined per codex F3)**: if the typed surface cannot supply a required observable (counts, outcome enums, stage typing, options readback), the probe fails and enters INV-013's state machine as `UNADJUDICATED_IN_PROCESS_FAILURE` (absent-capability variant), reaching **OFFICIAL_API_CAPABILITY_GAP only through the source-verified terminal transition** (codex R4-01 — never a direct classification), a capability candidate for ADR-0001, not an environment fault and not a harness fault — and weakening the required observable set is a spec change, never an implementation decision, and never a downgrade to console scraping (PRH-004). `ok.dfy` contains bodied declarations only, with no attributes, `assume` statements, or verification-suppression constructs — enforced by a named **AST-based fixture-hygiene test** walking ok.dfy's resolved AST via the seam (RS-009).
- **Boundary**: none (core functional path)
- **Violated when**: zero obligations are produced and the probe still passes; an empty/filtered program passes; any frontend error is swallowed; outcomes are inferred from console text; the fixture gains a suppression construct undetected.
- **Enforcement**: test assertion (local `dotnet test` until CI — EA-004/DD-005) (P06, exact expected-values sidecar + fixture-hygiene AST test)
- **Guards against**: AP-010 (vacuous pass), AP-013 (real execution evidence)
- **Test approach**: integration
- **Integration contract**:
  Entry: launch the built route harness executable as a child process via the allowlisted launcher (its own `.deps.json`/`.runtimeconfig.json`, built with locked restore then `--no-restore`) — never in-test-host invocation, which has a different dependency graph
  Through: real DafnyCore parse/resolve, real Boogie ExecutionEngine, real provisioned Z3 process; none of DafnyCore, Boogie, or Z3 may be mocked, stubbed, or replaced
  Exit: emitted evidence shows P06 pass with expected target names/count, typed solver-produced outcomes for every task, zero frontend errors, and the executed-solver identity of INV-003

### INV-005: Failing fixture is refuted by a completed solver result [integration]
- **Type**: must
- **Category**: functional
- **Statement**: For `fixtures/bad.dfy` (exactly one false assertion), the harness reports verification failure whose *stage is verification*: a completed solver-produced refutation, not a frontend error, timeout, or infrastructure failure — with at least one typed diagnostic carrying the planted assertion's source location (file, line) and a non-empty message, recovered from typed API results, never console text. **If the typed outcome cannot distinguish completed refutation from timeout/undetermined, P07 fails** (RS-018c) — that inability is itself a spike finding for ADR-0001. Location granularity rule (relaxed per codex F15, disposition 2026-07-20): P07 passes if the typed diagnostic's reported range **contains** the planted assertion's line; the exact reported range is recorded in evidence as an observation, not gated. A range that does not contain the planted line fails P07.
- **Boundary**: none
- **Violated when**: bad.dfy is reported verified; the failure is a crash/timeout/frontend error misread as refutation; location doesn't match the planted line; diagnostics are scraped from stdout.
- **Enforcement**: test assertion (local `dotnet test` until CI — EA-004/DD-005) (P07, exact planted location)
- **Guards against**: AP-010
- **Test approach**: integration
- **Integration contract**:
  Entry: same child-process harness invocation as INV-004, with `fixtures/bad.dfy`
  Through: real DafnyCore/Boogie/Z3; diagnostic recovery through the typed compilation-event/result API
  Exit: P07 records stage=verification refutation distinguishable from non-completion, recovered range **containing** the planted assertion's line (exact range recorded observationally — codex R2-12), non-empty message

### INV-006: Verdict is fail-closed against the spec-owned probe manifest — per route, aggregated by a named component
- **Type**: must
- **Category**: functional
- **Statement**: **Verdicts are per-route; there is no bare overall COMPATIBLE** (RS-001, disposition 2026-07-20: both routes mandatory to attempt, per-route verdicts). A route's verdict COMPATIBLE is computable only when that route's completed probe set — keyed by composite `(probeID, route)` (AP-006, RS-010) — is non-empty and exactly equals the manifest's instantiation for that route plus the shared probes, with no duplicates or unknown keys, and every probe individually passed. A named **aggregator** component computes the run-level result: it derives its exact expected report set from the manifest's committed route plan (never from reports found on disk — the natural fail-open shape, RS-010), **owns the shared probes (P02, P04)** — route children report only their route probes (codex F7) — and **binds every consumed report to the current run** (codex F1/R3-5): the **bootstrap controller** mints an unpredictable `run_id` and unique run directory before provisioning (the managed aggregator does not exist yet at that point), expected report paths flow directly from the launches actually performed, and the aggregator rejects any report or receipt whose run_id mismatches or that was not produced by those launches, so a stale report from a crashed prior run can never be aggregated. A missing mandatory-route report yields overall INCOMPLETE. The run-level evidence enumerates each route's verdict; Phase 0.1 gating requires the ADR-selected route to be fully COMPATIBLE (DD-001/DD-007). One route failing while the other passes is a recorded per-route finding, not a veto and not grounds to redeclare the route plan (PRH-001). Verdict taxonomy per INV-013: environment/prerequisite faults yield INCOMPLETE with cause; in-process API failures enter INV-013's adjudication state machine as `UNADJUDICATED_IN_PROCESS_FAILURE` (codex R4-01 — no candidate shortcut). **A route verdict of COMPATIBLE additionally requires the final test-suite exit to be success and the exit/report matrix to be consistent** (codex R4-02 — `final_suite_status` is an input to the verdict formula, not a recorded footnote; probe outcomes and route verdicts are distinct fields): otherwise the route is INCOMPLETE(`SUITE_FAILURE` | `EXIT_REPORT_MISMATCH`). The .NET 10 test runner (VSTest vs Microsoft.Testing.Platform — different exit semantics) is pinned and its exit-code contract committed. The manifest is a committed, versioned file whose SHA-256 is a test-suite constant (RS-002) and appears in every evidence report; the harness cannot shrink its plan at probe **or route** granularity. Total wall-clock is bounded by a value in a committed config file (initial: 30 minutes for a full two-route run **including provisioning, restore, build, shared probes, and aggregation** — codex F12); the bound is one **absolute deadline on a true monotonic source** — `/proc/uptime` on the pinned linux-x64 RID (bash `SECONDS` is system-clock-derived and not monotonic, codex R4-05) — held by the bootstrap controller from run mint through final aggregation (codex R3-5). **Every phase runs in its own `setsid` process group under an asynchronous supervisor** performing TERM→KILL escalation (a controller blocked in a foreground `restore`/`build`/`test` cannot check any clock), and the **outer watchdog is the parent of the controller — not a test executed by it** — so hangs in provisioning, build, test, and aggregation are each independently killable and independently tested; expiry records the synthetic INCOMPLETE (RS-015; an in-process timer cannot kill a hung Boogie+Z3 chain, and full watchdog semantics are bullet 7).
- **Boundary**: none
- **Violated when**: verdict logic compares against a runtime-declared plan or route set instead of the committed manifest; equality is keyed by probe ID alone so one route's pass satisfies another's slot; an empty plan satisfies equality; the aggregator iterates reports found on disk; error paths fall through to pass; a dropped probe or route goes unnoticed; a timeout reports success.
- **Enforcement**: test assertion (local `dotnet test` until CI — EA-004/DD-005) — **deletion tests at the verdict-logic unit level** against synthetic completed-sets, one per manifest entry (fast; anchored by the hard-coded manifest digest constant), plus a **route-deletion test** (drop Route B's report, assert overall INCOMPLETE), plus a small **named end-to-end induced-failure subset**: absent z3 (per-route), decoy z3, unreadable fixture, one forced probe exception, induced timeout with the committed bound shrunk below a known-slow probe (RS-012, RS-015); a unit test asserts verdict logic reads the committed manifest, requires non-empty exact composite-key equality, and that the manifest file's digest equals the test constant.
- **Guards against**: AP-001, AP-018, AP-006
- **Test approach**: unit + integration
- **Risk**: critical (this is the spike's honesty mechanism)

### INV-007: Resolved-AST recovery demonstrates inference, proof-vs-executable forms, and options [integration]
- **Type**: must
- **Category**: functional
- **Statement**: From the *resolved* program of `fixtures/classify.dfy`, the harness recovers a node table asserted against a committed expected-values sidecar. The fixture must contain, and the table must correctly classify: (a) the four Phase 0.1 editable-proof forms — an `assert` statement, a `calc` statement, a loop `invariant`, and a `decreases` clause — as proof/ghost nodes; (b) executable statements (assignment, conditional, loop body) as compiled; (c) an `expect` statement as compiled (contrast case); (d) at least one variable with **no `ghost` keyword** whose ghostness is resolver-*inferred* (e.g. assigned from a ghost function), alongside explicitly ghost and compiled declarations; (e) per declaration: symbol name, declaration kind, ghost-vs-compiled. **Sidecar shape tests (RS-009)** run independently of fixture equality and assert the sidecar itself contains ≥1 entry per required proof form, ≥1 `inferred-ghost=true` entry, and exactly one `expect` entry classified compiled. Because a sidecar's own claims cannot prove the fixture lacks the keyword (codex F10), an **independent AST test** obtains explicit-modifier presence from the parser AST and ghostness from the resolved AST *without consulting the sidecar*, and a **mutation differential** adds `ghost` to a copy of the inferred declaration and asserts resolved ghostness stays true while `inferred-ghost` flips to false — so a frozen-first-run sidecar cannot self-certify. Additionally the harness recovers the effective options per the **Option Manifest** (P11): the frozen list lives as exact option-ID→value pairs in a committed manifest file (the file, not this prose, is the oracle — RS-018b), covering: verification enabled (no `--no-verify`), no symbol filter, the included-files verification setting (both values exercised by P12), library/project inputs (none), cores=1, explicit solver path **expressed via the `<run-root>` token — expanded for execution, canonical in evidence** (codex R2-1), **no solver time limit + explicit resource-limit setting per DESIGN.md's Phase 0.1 doctrine** (RS-018b), function-syntax version, random seed, and **Boogie prover options** (restored from pre-review finding 9). Options are read back post-verification — but naive readback of `Compilation.Options` is **input echo by API design** (codex F5, verified against Dafny v4.11.0 source: `Compilation.Options` aliases `Input.Options`). The **normalization canary** therefore requires a **concrete, source-verified transformation named per route during RED design**: the probe records the requested value, the exact options object used, its state after upstream option processing, and a behavioral differential proving the effective value — and the canary value must respect the repo-relative path rule (an absolute-path canary would conflict with INV-009). If source inspection finds no typed post-processing observation, that is an **OFFICIAL_API_CAPABILITY_GAP candidate** (INV-013) recorded in ADR-0001 — never an invented canary (RS-004c, codex F5). The complete source set is recovered per INV-012.
- **Boundary**: none
- **Violated when**: classification reads surface syntax (the literal `ghost` keyword) instead of resolver results; any required node form is missing/misclassified; the inferred-ghost variable is absent from the fixture or sidecar; the options report is a serialization of harness input rather than a post-verification readback; option membership or values live only in prose.
- **Enforcement**: test assertion (local `dotnet test` until CI — EA-004/DD-005) (P10, P11) with exact expected sidecar values + independent sidecar shape tests
- **Guards against**: AP-011, AP-014, AP-010
- **Test approach**: integration
- **Integration contract**:
  Entry: child-process harness invocation (as INV-004) with `fixtures/classify.dfy` in AST-recovery mode
  Through: real DafnyCore resolution; classification read from resolved-AST properties; no text/regex analysis of fixture source
  Exit: emitted node table equals the committed sidecar (name, kind, ghost/compiled, node form); Option Manifest readback with canary and source set present in evidence

### INV-008: The Dafny seam is a separate assembly, structurally enforced — with a Dafny-free contracts assembly
- **Type**: must
- **Category**: functional
- **Statement**: The seam is **one logical boundary realized as two route implementation projects** — `SpikeDafnyAdapter.RouteA` (DafnyDriver graph) and `SpikeDafnyAdapter.RouteB` (DafnyCore/DafnyPipeline graph) — each the sole owner of its route's Dafny/Boogie PackageReferences and lock (codex F2: a single package-owning seam project would contaminate Route B with Driver's closure and collide on one lock, defeating DD-001; and `PrivateAssets="all"` on those package refs would strip *runtime* assets from the harness executables — certain per NuGet asset-control docs, so v3's mechanism is replaced). Dafny **compile** assets are kept from consumers via the documented compile-only privacy pattern (runtime assets still flow), verified by a **negative compile test**: a consumer source file naming a `Microsoft.Dafny` type must fail to build (RS-019a). Result/evidence-schema types live in a **Dafny-free contracts assembly**; the test project references only the contracts assembly (and launches harnesses as child processes), so "tests never load Dafny" is project-graph-enforced rather than disciplined (RS-019c). Route harnesses reference their route's seam project; everything consumes contract result types only.
- **Boundary**: prefigures PAT-001 (DafnyAdapter boundary)
- **Violated when**: any non-seam csproj gains a Dafny/Boogie PackageReference; Dafny types leak through the seam's public signatures; non-seam source imports `Microsoft.Dafny`/`Microsoft.Boogie` (including via aliases or fully-qualified names); the test host's dependency closure contains Dafny assemblies.
- **Enforcement**: test assertion (local `dotnet test` until CI — EA-004/DD-005) — project-graph test (no project outside the two route-seam projects has a Dafny/Boogie PackageReference; the compile-only privacy metadata is present); the **two-phase negative compile test** (codex R4-10 — any-failure oracles prove nothing): an otherwise-identical positive consumer must first build successfully, then the forbidden-type variant must fail with the expected compiler diagnostic at the planted token and no unrelated errors; a **metadata-only API-surface test** (e.g. `MetadataLoadContext`, run from the Dafny-free test process or a separately launched inspector — never runtime `Assembly.Load` in the test host, which would contradict the Dafny-free-closure claim, codex R2-8) recursively inspecting every externally visible signature — methods, properties, events, fields, interfaces, delegates, base types, nested public types, and generic arguments/constraints — asserting none originate from Dafny/Boogie assemblies (RS-019b); negative compile cases covering property access, interface implementation, and generic-constraint usage in addition to direct type naming; source grep for the namespaces outside the seam projects (backstop; the hash-bound negative-compile fixture files are the sole whitelisted exception and a test asserts they are excluded from every shipping/build project — codex R3-11); a test asserting the test project's resolved closure contains no `Dafny*`/`Boogie*` assemblies.
- **Guards against**: AP-002
- **Test approach**: unit (project-graph + reflection + source scan)
- **Risk**: medium

### INV-009: Evidence binds the run to identities, tree state, and manifest — via a committed three-way field partition
- **Type**: must
- **Category**: data-integrity
- **Statement**: Each route harness run emits a machine-readable JSON evidence report; the aggregator emits the run-level report enumerating per-route verdicts with the discriminated `verdict_reason` union of INV-013 (codex R2-5). The evidence schema is a **committed, immutable, versioned schema file** carrying `evidence_schema_version` (RS-011e), whose **SHA-256 appears in every report AND is anchored beside the probe-manifest digest in the test-suite constant set** — the aggregator recomputes and validates the committed schema's digest **before** parsing or projecting any report and rejects mismatches; a schema version may never be reused with a different digest — enforced by an **append-only version→digest registry file** whose rows a test asserts are never removed or altered, with the aggregator carrying its trust anchor as a compiled-in constant independent of the on-disk schema (codex R3-6); schema-substitution + same-version-mutation tests assert rejection (codex F11/R2-9). Adjudication records, reproducer/control-asset digests, and the final suite status sit in the deterministic projection; path-bearing generated files (e.g. `project.assets.json`) contribute **normalized semantic digests** to class 2 with their raw digests in the binding class (codex R3-6). The schema is closed (`additionalProperties: false` recursively), projection construction **fails if any instance field lacks exactly one classification**, and the Option Manifest and other oracle files are digest-bound the same way (their digests sit in the deterministic projection). It partitions every field into three classes (RS-005): **(1) binding/environment identity** — `run_id` + run directory (codex F1: required for aggregation, excluded from equality), git commit id + dirty flag (asserted equal to HEAD/clean **at generation time** by a separate check), host RID, SDK + actual runtime versions, harness TFM, loaded-assembly identities' file paths, and the concrete (uncanonicalized) restore argv and solver/run-product paths (codex R2-1) — recorded and validated, but **excluded from cross-run equality**; **(2) deterministic outcome projection** — per-route verdicts, per-probe results, node table, Option Manifest readback, fixture/sidecar/lock/`.deps.json`/`NuGet.Config`/manifest digests, **evidence-schema and oracle digests** (codex R2-9), resolved package versions, loaded-assembly identities (simple name, mapped version, file SHA-256), executed-solver identity SHA-256 — with **every run-root-dependent path canonicalized to the fixed token `<run-root>`** before projection (codex R2-1: raw `out/<run_id>/` paths in restore argv, solver path, or option readback would make INV-010 structurally impossible), and a test asserting the run_id value appears **nowhere** in the projection — **the equality domain**; **(3) volatile** — timestamps, durations. The equality and determinism tests derive their comparison set from the schema file, so shrinking the projection is a reviewable diff, never a test edit; reclassifying any field to volatile is a spec change (RS-005). The report is emitted **atomically** (write-temp-then-rename); consumers treat a missing or malformed report as its own state, never as pass or refutation (AP-009, RS-017f). All paths are repo-relative; committed samples contain no absolute paths, usernames, or hostnames. Before ADR-0001 cites a committed sample, a test asserts the sample's deterministic projection equals a fresh run's — **scoped to the pinned RID (EA-002), with a loud "skipped: unproven RID" on other hosts** (RS-005). **Committed samples come as a pair (QA-006 amendment, disposition 2026-07-21)**: the *variance-mode* sample (full class-2 equality against an in-suite fresh run) and a *canonical-run* sample (produced by the full controller run including the suite phase) whose equality check masks **only** the suite-status-derived subtree (`final_suite_status`, route-verdict fields, and their `verdict_reasons`) — the mask is **declared in the evidence schema file itself** (additive-only, digest-anchored like everything else), never in test or projection code; every other class-2 field must be equal. ADR-0001's verdict citations use the canonical sample; the variance sample remains the full-equality reproducibility anchor.
- **Boundary**: TB-004 (the report is the reproducibility anchor for ADR-0001's claims)
- **Violated when**: identities are hardcoded strings rather than read from resolved/loaded state; the report omits an identity it claims tested; a stale/hand-edited committed sample backs the ADR; an absolute host path appears in committed evidence; the projection is narrowed in code without a schema-file diff.
- **Enforcement**: test assertion (local `dotnet test` until CI — EA-004/DD-005) — schema/required-fields test on a fresh run; fresh-run-equality test against the committed sample (RID-scoped); an **anti-hardcode test** recomputing each reported assembly SHA-256 from the file at its reported repo-relative path and asserting equality (RS-016); hygiene test grepping committed evidence for `/home/`, `/Users/`, `/tmp/`, `C:\\`, the runtime-derived username (`Environment.UserName` — never hardcoded, which would itself violate PRH-005) and runtime-derived hostname (RS-016). Known limit, stated per AP-004: the hygiene grep cannot catch a *different* generating machine's identifiers outside those patterns — human review per AGENTS.md is the backstop.
- **Guards against**: AP-004, AP-009, repo public-hygiene rule (AGENTS.md)
- **Test approach**: unit + integration
- **Risk**: medium

### INV-010: Probe verdicts are stable across repeated runs
- **Type**: must
- **Category**: functional
- **Statement**: Two consecutive harness runs on the same tree produce identical deterministic projections of the evidence report (class 2 of INV-009's committed partition); binding and volatile fields are excluded by construction. An observed flap is a blocking finding to investigate, never a retry-until-green (RS-020 discipline); moving a flapping field to volatile requires a spec change.
- **Boundary**: none (weak precursor of Phase 0.0's fingerprint-determinism bullet, which stays out of scope)
- **Violated when**: verdicts or classifications flap between runs; volatile and deterministic data are interleaved so the comparison is impossible; a flapping field is silently reclassified.
- **Enforcement**: test assertion (local `dotnet test` until CI — EA-004/DD-005) — run-twice-and-diff integration test on the schema-file-derived projection.
- **Guards against**: AP-010
- **Test approach**: integration
- **Risk**: medium

### INV-011: Frontend failures are staged, typed, and solver-free [integration]
- **Type**: must
- **Category**: functional
- **Statement**: `fixtures/syntax-error.dfy` yields a typed diagnostic with stage=parse and an exact source location; `fixtures/resolve-error.dfy` yields stage=resolution likewise; in both cases the harness reports zero verification targets and no solver launch. **"No solver launch" is defined observably (RS-004b)**: P08/P09 run with the solver-path option pointed at the recording sentinel stub (P05 machinery, per-run nonce), and the assertion is that the pre-created nonce-bound ledger (initialized to count zero before the run — codex F6) shows **zero invocations** for this run's nonce plus zero verification tasks in the typed results. The evidence for these probes records the stub path as the effective solver path, so the variance from the Option Manifest is explicit and reviewable. If stage typing must be derived (e.g. from which pipeline phase faulted), the derivation rule is committed beside the schema so the assertion is reviewable.
- **Boundary**: none
- **Violated when**: a parse error surfaces as an empty program that "verifies"; resolution failure is indistinguishable from refutation; the sentinel records an invocation despite frontend failure; diagnostics lack stage or location.
- **Enforcement**: test assertion (local `dotnet test` until CI — EA-004/DD-005) (P08, P09)
- **Guards against**: AP-010, AP-018
- **Test approach**: integration
- **Integration contract**:
  Entry: child-process harness invocation (as INV-004) with each frontend-failure fixture, sentinel-configured
  Through: real DafnyCore parse/resolution; typed diagnostic recovery
  Exit: stage-typed diagnostic with location; zero verification targets; zero sentinel invocations recorded for this run's nonce

### INV-012: Closure recovery reports reality, not the input argument [integration]
- **Type**: must
- **Category**: functional
- **Statement**: For the spike-only include pair (`incl-root.dfy` includes `incl-leaf.dfy`), the harness reports the parsed/resolved closure containing exactly both files, distinguished from the verification-target set. **P12 runs under both values of the included-files verification option** (restoring the pre-review differential that RS-018a found dropped): the closure must be identical {root, leaf} under both values while the verification-target set differs accordingly — this is what distinguishes real closure recovery from target-set conflation and from argument echo. Single-file fixtures must report a one-element closure derived from the resolver, not an echo of the CLI argument.
- **Boundary**: none
- **Violated when**: the "source set" is the input path echoed back; the include's file is missing from the closure; parsed closure and verification targets are conflated; only one option value is exercised.
- **Enforcement**: test assertion (local `dotnet test` until CI — EA-004/DD-005) (P12, both option values)
- **Guards against**: AP-011
- **Test approach**: integration
- **Integration contract**:
  Entry: child-process harness invocation (as INV-004) with `fixtures/incl-root.dfy`, once per included-files option value
  Through: real DafnyCore parse/resolution of the include closure
  Exit: reported closure = {incl-root.dfy, incl-leaf.dfy} (repo-relative) under both values; verification-target sets equal the **committed exact expected set per option value** (equality + non-emptiness where expected + the precise set delta — merely "the two sets differ" would accept fabricated sets, codex R3-10)
- **Note**: includes remain rejected by Phase 0.1 intake; this fixture exists solely to prove closure recovery is real (DESIGN.md bullet 3 "complete source set").

### INV-013: Verdict taxonomy — INCOMPATIBLE requires adjudication; harness bugs and environment faults are not integration failures
- **Type**: must
- **Category**: functional
- **Statement**: (RS-007; refined per codex F3/F4/F7) The verdict vocabulary distinguishes causes. **Missing prerequisite / environment / tooling faults** (absent z3, restore failure, unsupported RID, wall-clock kill) yield **INCOMPLETE with a typed cause**. In-process failures during a probe enter the **adjudication state machine** (codex R2-3 — the flat three-way split was neither exhaustive nor correctly typed) as **`UNADJUDICATED_IN_PROCESS_FAILURE`** with a variant payload: typed exception | unexpected typed result | absent required capability (a missing observable does not throw), plus probe ID, stage, and minimal inputs. Terminal transitions are **mechanical, each requiring a schema-valid adjudication record**: harness fault → **INCOMPLETE** (never an INCOMPATIBLE of any kind); source-verified absence of a required official surface → INCOMPATIBLE(`OFFICIAL_API_CAPABILITY_GAP`); three-cell control outcomes (below) → INCOMPATIBLE(`HOST_RUNTIME_INCOMPATIBILITY`) or INCOMPATIBLE(`TARGET_FRAMEWORK_INCOMPATIBILITY`) (codex R3-2); reproducible cross-runtime failure of an existing surface → **`UPSTREAM_DEFECT`** (recorded evidence, not a boundary rejection). The **complete route-outcome algebra is committed in the evidence schema** (codex R3-4/R4-01): route-state enum, reason union, transition table, exit/report matrix, a **precedence order for multi-match states** (signal death without a report resolves to the crash variant, never ambiguously), and `UPSTREAM_DEFECT`'s defined semantics — a non-COMPATIBLE, non-boundary-rejection route outcome with its own reason variant and probe-failure exit mapping. The **ADR linter operates on mandatory machine-readable fields** (`boundary_decision`, `route`, `adjudication_record_id` — a closed vocabulary, not prose detection) and validates **positive compatibility/selection claims as well as rejection claims** against schema-valid terminal adjudication records (codex R4-01/R4-02) — a mechanism that runs (PAT-004). Every non-COMPATIBLE run-level report carries **one discriminated `verdict_reason` union with variant-specific required fields** (codex R2-5/R3-4): probe-failure (probe selected by **manifest order**, never completion order), prerequisite-failure, missing-report, malformed-report, crash, **exit/report-mismatch**, **unadjudicated-failure**, and the terminal adjudication classes — missing/malformed reports carry their own variants (they do not collapse into crash) and have no "first failing probe". The committed **exit/report consistency table** is exhaustive: any mismatch — including a failure exit code accompanied by an all-pass report — is non-COMPATIBLE with its precise reason (codex R2-5/R3-4). Adjudication before ADR citation: the **net8.0 control** re-runs the failing probe from identical normalized source, resolved package graph, options, and fixtures rebuilt for net8.0 (a bit-identical assembly across TFMs is impossible — codex F4; **TFM-conditional harness logic is prohibited and control equivalence is proven by digest equality** of those inputs plus the control asset — codex R2-2), executed on the **BND-004-pinned net8 runtime** — "ran on 8.x" is insufficient; the *pinned* runtime must have run. Because retargeting changes **two variables** (target framework and runtime — reference packs, generated code, and asset selection differ even with identical normalized source, codex R3-2), adjudication uses a **three-cell experiment**: (i) the net10 asset on the pinned net10 host, (ii) the net8 control asset on the pinned net8 host, (iii) **the exact same net8 control bits on the pinned net10 host**. net8-pass + same-bits-fail-on-net10-host → INCOMPATIBLE(`HOST_RUNTIME_INCOMPATIBILITY`) **only when both net10-host failure cells exhibit the same minimized reproducer and an equivalent typed failure fingerprint** — unrelated failures across cells remain unadjudicated (codex R4-04); net8-bits-pass-on-net10 but net10-target-fail → INCOMPATIBLE(`TARGET_FRAMEWORK_INCOMPATIBILITY`), a distinct class. Control assets and locks are **per route** (each isolated route gets its own equivalent control graph); the control build has committed locks and a compiler-input comparison; conditional MSBuild/source logic is enforced against, not just prohibited. If the control runtime is unavailable, **only host/TFM adjudication is blocked** — an independently demonstrated capability gap remains citable (codex R2-2) — and nothing silently runs the control on the wrong host. ADR-citable transitions additionally require a minimized official-surface reproducer and a source/upstream cross-check. Every unadjudicated-failure record must embed the failing probe's typed diagnostic plus that run's P01–P04 identity evidence; an induced-failure test asserts this payload is present. The exit-code contract (consumed by the child-process integration tests now and CI later): route children exit with distinct codes for route-probes-passed / probe-failure / INCOMPLETE; **any unknown exit code or signal death maps to the crash variant** (a crashing process cannot guarantee a prescribed code — codex F7), absent/malformed reports carry their own reason variants (codex R3-4), and consumers never read any of these as pass or refutation (AP-003, AP-009).
- **Boundary**: none
- **Violated when**: a harness NullReferenceException surfaces as a citable "reproduced integration failure"; an environment fault is labeled INCOMPATIBLE; exit codes conflate crash with refutation; the ADR rejects the boundary without the net8.0 control.
- **Enforcement**: test assertion (local `dotnet test` until CI — EA-004/DD-005) — induced-failure tests assert the state-machine mapping and payload presence; the **ADR linter test** (codex R3-4) rejects boundary-rejection language lacking a schema-valid terminal adjudication record — a mechanism that runs, not an advisory gate.
- **Guards against**: AP-003, AP-004, AP-009
- **Test approach**: unit + integration
- **Risk**: high (a false INCOMPATIBLE triggers the expensive architecture fallback)

### INV-014: Operator surface — the spike is runnable, readable, and recoverable by a stranger
- **Type**: must
- **Category**: functional
- **Statement**: (RS-017) Deliverables include: a README under `spikes/dafny-compat/` naming the canonical entry-point script and the phase ordering (provision → locked restore → build → test) with the pinned SDK version and install source; prerequisite-failure messages that name the remediation step (e.g. "run provisioning first: <script>"); provisioning that is **idempotent and partial-state-safe** — tested from clean, partial (truncated/corrupt file present), and full states plus a double-run identical-state check (AP-016); **fail-closed unsupported-RID behavior** with the message "RID not supported by this spike; proven RIDs: linux-x64 (see ADR-0001)"; an untracked `out/` area for all run products (spike-local packages folder, provisioned z3, sentinel recordings, fresh evidence reports) with a required `.gitignore`, kept distinct from the committed-samples location — a public repo must never accidentally receive the z3 binary or package cache; a per-route **terminal verdict summary** (verdict, failed probes with reasons, report path) so a human has an entry point without parsing JSON; and ADR-0001 citing its evidence as repo-relative paths, one per claim.
- **Boundary**: none
- **Violated when**: a fresh clone has no documented way to run the spike; provisioning re-runs corrupt state; an unsupported RID produces an unexplained crash or a fail-open fetch; run products land in git; the only run output is a JSON blob.
- **Enforcement**: test assertion (local `dotnet test` until CI — EA-004/DD-005) — provisioning three-state + double-run tests; a test asserting `.gitignore` covers the `out/` area; README existence + entry-point reference check; summary-line presence asserted in the child-process integration tests.
- **Guards against**: AP-016, AP-005
- **Test approach**: unit + integration
- **Risk**: medium

## Prohibitions

### PRH-001: Never report COMPATIBLE on a partial, failed, or self-shrunken run
- **Statement**: No route verdict may be COMPATIBLE unless that route's completed composite-keyed probe set is non-empty and exactly equals the committed Probe Manifest's instantiation with every probe passing; no run-level result may omit a manifest-declared mandatory route (missing route ⇒ INCOMPLETE). No error path may fall back to the optimistic verdict; no runtime code path may redefine the plan at probe **or route** granularity.
- **Detection**: manifest deletion tests (unit-level) + route-deletion test + induced-failure subset (INV-006); manifest digest pinned as a test constant (RS-002); code review of verdict computation.
- **Consequence**: Phase 0.1 builds on an unproven toolchain; the spike's one job fails silently.

### PRH-002: No floating, range, or nightly package versions
- **Statement**: No Dafny/Boogie-family package may use a floating (`4.*`), **non-singleton** range (`>=`, open intervals), or nightly (`4.11.1-nightly-*`) version — exact singleton ranges `[x]` on `PackageVersion` entries are the only permitted form (codex R2-6); restore must never run outside locked mode in test/evidence contexts.
- **Detection**: lock-file assertion tests (INV-001) + version-syntax scan over **both** csproj files and the authoritative `Directory.Packages.props` (codex R2-6) + the INV-001 behavioral stale-lock negative test (the check that locked mode was actually in effect, RS-014a).
- **Consequence**: silent upgrade onto an untested AST/API surface (the `IToken`→`IOrigin` class of break lands in minors with no release-note warning).

### PRH-003: No spike code outside the spike area
- **Statement**: This feature writes only under `spikes/dafny-compat/`, `docs/adr/`, and `.correctless/` workflow artifacts (plus the TB-004 entry in ARCHITECTURE.md, named in Scope). `src/` stays empty; nothing in the spike may be presented or wired as production code.
- **Detection**: review-time `git diff --stat` path check — an **advisory human gate, labeled as such** (PAT-004/RS-020); optionally backstopped by a suite-level assertion that no tracked files outside the named areas changed on the branch.
- **Consequence**: the ADR-pending integration boundary hardens into de-facto production architecture before it's validated.

### PRH-004: No second-parser semantics; no unaudited process launches; no CLI fallback
- **Statement**: The spike must never derive semantic facts (ghostness, declaration kinds, verification results) from regex/text analysis of Dafny source or console output. Spike-initiated process launches are allowlisted in **two explicit layers** (RS-020o, codex F13/R2-7): (1) a minimal **bootstrap controller allowlist** with concrete executables — `bash`, `dotnet`, `curl`, `sha256sum`, `tar`, `unzip`, `git`, plus the filesystem/process/supervision utilities it actually invokes: `mkdir`, `mktemp`, `mv`, `chmod`, `setsid`, `kill`, `sleep` — each with a stated resolution rule, invoked with structured argv and the EA-008 per-launch environment profiles; a **static test compares the script's direct command set against this allowlist** (codex R3-9). The controller is invoked with a **clean environment and `bash -p`** (noninteractive bash executes `$BASH_ENV` before any script line can sanitize — codex R4-08), `curl` runs with `--disable` as its first option (default `.curlrc` reading), and poisoning tests cover `BASH_ENV` and `.curlrc`. The bootstrap controller owns the run mint and the single `/proc/uptime`-based monotonic deadline from provisioning through final aggregation, emitting the synthetic INCOMPLETE on expiry (codex R3-5/R4-05); (2) the **managed launcher** for post-build processes — the route harness executables, the net8 control host, the provisioned z3 identity check, and the sentinel stub. **SDK-managed descendants** of `dotnet` (MSBuild worker nodes, compiler servers, test hosts) are documented permitted children of layer 1, with persistent build servers disabled where practical (`MSBUILDDISABLENODEREUSE=1`, `UseSharedCompilation=false`) so the process tree stays bounded (codex R2-7). Boogie's internal solver spawn is the only permitted child process outside both layers. Launching a `dafny` CLI is prohibited — an in-process API failure is recorded as evidence (unadjudicated in-process failure per INV-013's state machine), never papered over by shelling out.
- **Detection**: code review + source scan: `Process.Start`/`ProcessStartInfo` usage outside the launcher component is a finding; no regex over fixture content in classification paths (INV-007's resolved-AST enforcement); no `dafny` binary invocation anywhere.
- **Consequence**: violates PROHIBIT-002/DESIGN.md ("no path may silently fall back to regex or text matching"); the spike would "pass" while measuring a different integration than the one Phase 0.1 will use.

### PRH-005: No host-system details in committed artifacts
- **Statement**: Committed evidence samples, fixtures, sidecars, ADR-0001, and scripts must contain no absolute local paths, OS usernames, hostnames, or machine details.
- **Detection**: hygiene grep test (INV-009) with runtime-derived username/hostname patterns and the widened path set (`/home/`, `/Users/`, `/tmp/`, `C:\`) + the standing pre-commit review rule in AGENTS.md. The grep's cross-machine limits are stated in INV-009 (AP-004 discipline).
- **Consequence**: public-repo leak of host-system information; cannot be undone after push.

## Boundary Conditions

### BND-001: NuGet package intake
- **Boundary**: TB-004 (inbound toolchain supply chain)
- **Input from**: nuget.org (untrusted network source)
- **Validation required**: exact family-wide pins; committed lock file per project; locked-mode restore (content hashes) proven in effect by the stale-lock negative test; spike-local `NuGet.Config` with `<clear/>` + single source + source mapping, applied via `--configfile`; spike-local packages folder for evidentiary restores (with `NUGET_PACKAGES` declared/sanitized per EA-008 — env can redirect the folder; machine-level `fallbackPackageFolders` are not cleared by `<clear/>`, residual bounded by locked-mode content hashes); spike-local sealing files (props/targets/rsp) terminating MSBuild inheritance, exact bracket version constraints, and downgrade-warnings-as-errors (INV-001, codex F9).
- **Failure mode**: fail-closed — restore/build fails on any mismatch; no "restore latest and continue".

### BND-002: Z3 binary intake
- **Boundary**: TB-004
- **Input from**: official Z3 GitHub release asset (untrusted network source)
- **Validation required**: pinned URL + pinned SHA-256 committed in repo; install location outside every ambient discovery location (INV-003); version banner check at harness startup; executable resolved by explicit path only; executed-solver identity proven per INV-003; unsupported host RID fails closed with the INV-014 message (never an unverified fetch).
- **Failure mode**: fail-closed — digest, version, or RID mismatch aborts provisioning/verdict (INCOMPLETE, never COMPATIBLE).

### BND-004: net8 control-runtime intake (codex R2-2)
- **Boundary**: TB-004
- **Input from**: official .NET 8 runtime archive (untrusted network source)
- **Validation required**: exact runtime patch + RID + URL + SHA-256 pinned in repo, installed inside the run root; launched via the **explicit private host argv** `<control-root>/dotnet exec --fx-version <exact-patch> …` with exactly one framework reference (codex R3-3, certain: `DOTNET_ROOT*` steers apphosts — it does not select the runtime when invoking a `dotnet` CLI; the matching private host must be invoked directly), and the net10 control host **pinned identically** — exact host/runtime with expected digests (or the SDK-bundled host declared as an explicit locked prerequisite with digests); EA-001's floating net10 runtime patch applies to the main run's binding record only, never to control cells (codex R4-04); digest + location identity of the selected `hostfxr` and `System.Private.CoreLib` recorded in control evidence ("ran on some 8.x" is insufficient — the *pinned* runtime must be proven to have run); control equivalence proven by digest equality of normalized source, resolved package graph (committed control locks), options, fixtures, compiler inputs, and the control asset; conditional MSBuild/source logic enforced against by scan.
- **Failure mode**: fail-closed — mismatch or unavailability blocks only host/TFM adjudication (an independently demonstrated capability gap remains citable); never a silently substituted runtime, never a control cell run on the wrong host.

### BND-003: Fixture files into the verifier
- **Boundary**: none (trusted, authored in-repo)
- **Input from**: `spikes/dafny-compat/fixtures/*.dfy` + expected-values sidecars, committed and reviewed; positive fixtures constrained to the Phase 0.1 supported fragment (bool/int/nat/seq, no heap/classes/recursion) so they remain reusable as Phase 0.1 fixtures — **by reference in place, not by copying** (RS-020l: copies would fork the SHA-256s the evidence binds to; any re-capture records digest continuity). The include pair, error fixtures, and the Route B standard-library anchor (`stdlib-anchor.dfy` + sidecar, added during RED per OQ-004's mandate) are spike-only and marked as such.
- **Validation required**: SHA-256 of each fixture and sidecar recorded in the evidence report so every claim binds to exact inputs; `ok.dfy` structural purity enforced by the named AST-based fixture-hygiene test (INV-004); sidecar shape tests independent of fixture equality (INV-007/RS-009).
- **Failure mode**: unreadable/missing fixture → probe INCOMPLETE (fail-closed per INV-006).

## STRIDE Analysis

### STRIDE for TB-004: inbound toolchain supply chain (NuGet packages + Z3 binary + control runtimes)
- **Spoofing**: a rogue package feed, look-alike package, or substituted runtime archive. Mitigated: spike-local `NuGet.Config` with `<clear/>`, nuget.org only, source mapping (INV-001); exact IDs+versions in lock files; BND-004's pinned URL + SHA-256 + explicit private-host launch for the net8 control runtime (codex R3-12). Residual: nuget.org account compromise of upstream — accepted for a spike (recorded in ADR as bootstrap-TCB residual, consistent with TB-003's explicit-TCB stance).
- **Tampering**: substituted package, assembly, Z3 asset, runtime archive, private host, `hostfxr`, or `System.Private.CoreLib` (codex R4-11). Mitigated: locked-mode restore (content hashes) + runtime loaded-assembly digest comparison against the expected-loaded set (INV-002/INV-009/P03); pinned SHA-256 + executed-solver proof + decoy test for Z3 (INV-003); MSBuild inheritance blocking (INV-001). Residual: trust in the initially recorded digests — one-time, reviewable.
- **Repudiation**: compat claims that can't be tied to what actually ran. Mitigated: evidence binds loaded-assembly identities, executed-solver identity, manifest/lock/fixture digests, git commit (validated at generation time), and per-probe results (INV-009); ADR cites the report by repo-relative path; committed sample must equal a fresh run (RID-scoped).
- **Information disclosure**: committed evidence/ADR leaking host paths/usernames. Mitigated: PRH-005 + widened hygiene grep (INV-009) + `out/` isolation (INV-014).
- **Denial of service**: hung Z3/solver stalls the spike indefinitely. Mitigated: committed wall-clock bound on a true monotonic source, per-phase `setsid` process groups under an asynchronous supervisor with TERM→KILL escalation, and an outer watchdog parenting the controller (INV-006/RS-015/R4-05). Full watchdog/resource-limit semantics are Phase 0.0 bullet 7, out of scope.
- **Elevation of privilege**: the spike inherently executes downloaded managed packages and a native solver on the dev machine. Accepted residual for local tooling (same class as any NuGet dependency); noted in ADR; no sandboxing in scope.
- **Availability over time** (RS-020h): DD-005/DD-006 make nuget.org's 4.11.0 packages, the Jan-2023 Z3 release asset, and BND-004's pinned .NET 8 runtime archive (codex R3-12) a *permanent* availability dependency. Position: vendoring/caching is **rejected for the spike, consequence accepted** — recorded in ADR-0001's residual list so the first unavailability event has a documented policy, not an improvised fallback.

## Environment Assumptions

- **EA-001**: One exact .NET 10 SDK, pinned in a committed `global.json` (`rollForward: disable`, `allowPrerelease: false`), is installed on the dev host — the exact version is selected at implementation time from the then-current supported 10.0.x line and recorded in the evidence report (binding class) along with actual runtime, host/fxr, architecture, and RID. **An SDK patch/feature-band bump is a DD-006 toolchain-identity change** with a defined lighter procedure: bump the pin, re-run the suite, re-capture the committed sample, commit the evidence diff (RS-011d). Framework-dependent publish means the *runtime* patch floats independently of the SDK pin; actual runtime identity is recorded as binding, not equality-compared (RS-020g). Consequence if wrong: build fails fast (by design) rather than silently building on a different SDK.
- **EA-002**: Initial dev host is linux-x64; that is the only RID the spike proves. Other RIDs (osx-arm64, win-x64) remain unproven, fail closed at provisioning (INV-014), and are listed as such in ADR-0001. The official Z3 4.12.1 linux-x64 asset's glibc floor is recorded (non-identifying) in evidence (RS-020c). DD-005's future CI runner is assumed linux-x64; a different CI RID requires new pinned digests + a re-captured sample (RS-020, RS-005).
- **EA-003**: Network access to nuget.org and github.com is required during the **online phase** (restore + provisioning); the **verification phase** is expected to run offline. These are explicitly two phases of one evidentiary run, not a no-hybrid claim (the v2 wording contradicted itself — RS-020f). The offline expectation is **unverified** (dotnet telemetry/first-run behavior is untested; EA-008 opts out) and is recorded as an ADR residual, not asserted as a guarantee. Consequence if wrong: restore/provisioning fails closed (BND-001/002).
- **EA-004**: No CI environment exists yet in this repo — every "test assertion" enforcement mechanism runs via the spike's test suite locally (`dotnet test`) until CI lands (DD-005). To make this the workflow's mechanism rather than developer discipline (RS-006), the Correctless workflow test command is configured to run the spike suite as part of this feature, and edit-time breadcrumb comments are placed in `global.json`, the central package pins, and the provisioning digest constants: "changing this requires re-running the spike suite and committing the evidence diff — see DD-006 / ADR-0001."
- **EA-005** (RS-003e, revised per codex F6): Solver environment hygiene — the typed Boogie prover-option list is inspected for duplicate/conflicting solver-path entries (`PROVER_PATH` is a Boogie prover *option*; an environment-variable form is unconfirmed, and the env rule applies only if source inspection confirms one); apart from the sanctioned nonce-recording decoys, no z3-named executable exists on PATH or under any route output tree (asserted by the harness startup gate).
- **EA-006** (RS-020b): Evidence serialization is culture-invariant with stable key/path ordering and a stated digest encoding; the canonical-serialization rule lives beside the evidence schema. Host locale must not affect the deterministic projection.
- **EA-007** (RS-020d): Provisioning/evidence host tooling exists: POSIX shell, a download tool, a sha256 utility, and `git` (for the commit-id binding field, invoked via the allowlisted launcher).
- **EA-008** (RS-020e, revised per codex F13/R2-7/R3-9): the evidentiary-run environment is **constructed from committed per-launch environment profiles** (exact keys and value templates per launch class), not sanitized by prohibition. Named keys at minimum: `PATH` (constructed, decoy-first per INV-003), `DOTNET_CLI_HOME`, `NUGET_PACKAGES` (spike-local), `TMPDIR` (run-root), `LC_ALL`/`LANG` (invariant per EA-006), `SSL_CERT_FILE`/`SSL_CERT_DIR` (passthrough), `DOTNET_CLI_TELEMETRY_OPTOUT=1`, `MSBUILDDISABLENODEREUSE=1`, `UseSharedCompilation=false` (the build-server disables must themselves be in the profiles); **documented SDK-injected exceptions** (e.g. `DOTNET_HOST_PATH`) are enumerated rather than contradicting the only-profile-keys rule. Recorded environment values in **committed** evidence are serialized as structured **root-kind + relative path** (`{root: run-root|home|system, path: …}`) rather than raw strings — token substitution alone is not exhaustive for `PATH` and certificate paths (codex R3-9/R4-08).

## Design Decisions

- **DD-001 — Route isolation, both routes mandatory, per-route verdicts** (revised per RS-001, disposition 2026-07-20): Each integration route is a separate minimal executable project with its own lock and `.deps.json`: Route A = `CliCompilation` via DafnyDriver 4.11.0 (closest to Dafny's reference driver); Route B = hand-assembled `Compilation` from DafnyCore/DafnyPipeline. A single mixed project cannot evaluate either route cleanly — Driver's transitive closure (LanguageServer, TestPlatform, Newtonsoft.Json) would contaminate the "fallback" graph, and per NuGet metadata DafnyDriver does not depend on DafnyPipeline, so a Driver-route pass may never load the package DESIGN.md names as the embedding candidate. **Both routes are committed in the manifest's route plan as mandatory to attempt; the run-level result enumerates each route's verdict; there is no bare overall COMPATIBLE.** One route failing while the other passes is a recorded per-route finding, not a veto (avoids the false-negative aggregation trap) and not grounds to redeclare the plan. Phase 0.1 gating requires the ADR-selected route to be fully COMPATIBLE; the ADR names the packages *actually loaded* per route (P03), never packages merely referenced, and states plainly which routes/packages remain unproven.
- **DD-002 — Z3 origin**: official microsoft/z3 GitHub release asset for 4.12.1 (not extraction from a Dafny release zip), pinned by SHA-256 — smallest, most citable provenance chain. Installed outside ambient discovery locations (INV-003).
- **DD-003 — Publish mode**: plain framework-dependent net10.0; `PublishTrimmed`, `PublishAot`, and single-file publishing are disabled (single-file breaks Dafny's assembly-adjacent solver discovery; trimming breaks its reflection/embedded resources). Integration tests exercise the built console artifact as a child process via the allowlisted launcher, not a test-host-loaded copy.
- **DD-004 — Spike isolation**: code under `spikes/dafny-compat/`, deliverables are evidence + ADR-0001; production `DafnyAdapter` is a Phase 0.1 rewrite informed by, not evolved from, this harness. The suite itself is **retained permanently** as conformance infrastructure (DD-005/DD-006) — the "spike" name refers to the code's non-production status, not to disposability (RS-020m); deleting `spikes/dafny-compat/` would strand ADR-0001's citations and break DD-006.
- **DD-005 — CI permanence with a structural carrier** (resolved OQ-001; hardened per RS-011a): when CI is introduced, the spike suite becomes a permanent CI job. Structural carriers, not prose: (1) a **stub CI workflow file committed under `spikes/dafny-compat/ci/`** that the CI feature must wire (its existence is the handoff artifact); (2) the Correctless workflow test command configured to run the suite now (EA-004); (3) this obligation registered NOW as **DF-001** in `.correctless/meta/deferred-findings.json` (codex F15/R2-12: registered at spec time, not promised for later), which the CI feature must discharge. Until CI lands, enforcement is local `dotnet test` (EA-004).
- **DD-006 — Standing conformance probe with a defined bump procedure** (resolved OQ-002; hardened per RS-011c): any change to a pinned toolchain identity (Dafny packages, Boogie, Z3, .NET major, **and SDK patch/feature-band per EA-001**) must re-run the spike. A bump change-set must contain: the pin diff, re-captured expected-value sidecars (AP-014 re-capture rule), the re-captured committed sample, the evidence diff, and an ADR-0001 amendment. Diff classes are adjudicated, not eyeballed: node-classification, target-set, option-readback, or loaded-assembly-set changes = reviewable compatibility signal requiring explicit acceptance; duration/timestamp changes = ignorable. **Scope honesty (RS-020i)**: this suite is *one component* of PAT-001's upgrade evidence, covering bullets 1–3 only — the ownership, closure, bypass, and fingerprint suites required by PAT-001 are owned by the bullets 4–5 features; re-running the spike alone does not satisfy PAT-001.
- **DD-007 — Provisional, scope-qualified ADR with an owned lifecycle** (external review F14; hardened per RS-011b, RS-020j/n): ADR-0001 is created with status **provisional**, records **per-route verdicts** naming the proven *capabilities* (in-process parse/resolve/Z3-verify, resolved-AST recovery, options readback, closure recovery) alongside the DESIGN.md bullet citation **pinned to the DESIGN.md version** (bullet numbering is not stable across design revisions), and explicitly lists the deferred Phase 0.0 gates (bullets 4–12). **Promotion to *accepted* is an inherited obligation of the final Phase 0.0 feature's spec**, registered NOW as **DF-002** in `.correctless/meta/deferred-findings.json` (codex F15/R2-12); a later bullet reproducing a boundary failure moves the ADR to *superseded* with the counter-evidence cited. The ADR states that the selected route's lock is the model for PAT-001's single production lock (defined end for the plural-lock spike state). **Doc propagation**: if the selected route's actually-loaded package set differs from what DESIGN.md/ARCHITECTURE.md name (e.g. Route A selected ⇒ DafnyPipeline not loaded), the ADR names the required DESIGN.md + ARCHITECTURE.md component-table updates as explicit obligations (per the Conventions propagation rule).
- **DD-008 — Evidence lifecycle and regeneration affordance** (RS-017d/e, codex F1/R4-06): the bootstrap controller creates a unique `out/<run_id>/` directory per run and all run products live under it — **structurally**, not by prose: every project sets `BaseOutputPath`, `BaseIntermediateOutputPath`, and `MSBuildProjectExtensionsPath` beneath the newly-empty run root, only artifacts from those paths are launched, build receipts bind output/`.deps.json`/runtimeconfig/generated-assets digests, and a test pre-seeds stale repo-local `bin`/`obj` artifacts and proves they are neither consumed nor launched (incremental outputs from another SDK must be unreachable); committed samples live at a distinct committed path. The **sanctioned regeneration procedure** (a named script/command) regenerates the committed sample; its output is accepted only via review of the resulting diff. The fresh-run-equality test's failure message names the procedure and distinguishes "regenerate and review" (after an intentional change) from "investigate" (unexplained divergence) — a frozen asset with no update affordance degenerates into routine overrides (AP-005).
- **DD-009 — Time-box** (RS-012, re-budgeted per codex F14; DESIGN.md "time-boxed spike"): 20 working days from TDD start. On expiry: unresolved probes stand as INCOMPLETE in the committed evidence, and continuation is an explicit scope decision recorded in the workflow — never a quiet trim of the deletion/induced-failure matrix.

## Packages Affected

Monorepo note: none of the eventual production packages exist or are touched.
The spike directory is intentionally outside the future package roots.

## Open Questions

OQ-001 (CI fate) and OQ-002 (toolchain-bump re-runs) were resolved during
spec review — see DD-005/DD-006. The route-verdict policy question (RS-001)
was resolved 2026-07-20: both routes mandatory, per-route verdicts, no bare
overall COMPATIBLE.

Two **bounded RED-phase adjudication tasks** remain open by design (codex
R2-10/R2-11 — declared here rather than hidden in prose; each has exactly
two permitted outcomes and no third):

- **OQ-003 — P11 canary concreteness**: during RED, name each route's exact
  canary contract (option ID, requested vs effective value, Dafny source
  citation for the transformation, behavioral differential) — or record
  `OFFICIAL_API_CAPABILITY_GAP` in ADR-0001. `Compilation.Options` aliasing
  `Input.Options` is established (v4.11.0 source); an ad-hoc observation
  does not satisfy this task.
- **OQ-004 — Route B DafnyPipeline consumption**: during RED, name the exact
  DafnyPipeline API/resource consumed on the verification path plus its
  removal/differential test — or, **before GREEN, amend the manifest and spec**
  (a spec change, not an ADR waiver — codex R3-7) to a DafnyCore-only Route B
  with matching references, expected-loaded set, and claim. OQ-004 completion
  is a **route-definition gate**: a failed mandatory P03 anchor can never be
  overridden by ADR prose.
