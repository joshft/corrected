# Review-Spec Findings: readiness-gate-carrier

Date: 2026-07-25
Spec: `.correctless/specs/readiness-gate-carrier.md`
External review: **codex (GPT-5.6-sol, reasoning effort xhigh)** via `codex exec`
(read-only sandbox, ephemeral). Raw output: `scratchpad/codex-review-out.md`.
Treated as advisory DATA, adjudicated by the maintainer (Claude relayed + assessed;
finding #5 independently verified against `Components.cs`). Claude's own /creview-spec
6-agent pass runs AFTER this incorporation and appends below.

## external-review status: ran(codex gpt-5.6-sol xhigh)
egress: full repo context sent to codex (OpenAI) — user-authorized (public OSS project)
cost: unavailable (direct CLI call, not the skill producer)

## Findings + dispositions (all accepted unless noted)

- **EXT-01 [BLOCKING] Entrypoint runs no tests** — `dotnet test gate/Corrected.Gate`
  targets the library dir; tests are in the sibling `.Tests` project → zero-discovery
  silent pass. Status: ACCEPTED → INV-014 targets a solution aggregator; zero-discovery
  fails; named fixtures asserted executed. ARCHITECTURE `test_via` correction flagged.
- **EXT-02 [BLOCKING] READY-all-true fixture impossible with skeleton P2/P3** — "real
  probes, nothing mocked" vs P2/P3 that only return false. Status: ACCEPTED → split pure
  kernel `EvaluateReadiness(block, probeResults)` from probe orchestration.
- **EXT-03 [BLOCKING] Evidence-reference semantics contradictory** — "unresolvable = hard
  fail" vs legitimate `evidence:null`. Status: ACCEPTED → explicit verdict table (INV-005)
  + fixtures.
- **EXT-04 [BLOCKING] INV-036 leaks via `**/*.Tests/**` inside a shipped project** —
  path exemption gameable within a shipped compile closure. Status: ACCEPTED →
  shipped-compilation-closure-based check (INV-011); exempt only independent test projects;
  bypass fixtures. ARCHITECTURE partition amendment flagged.
- **EXT-05 [BLOCKING] P1 linter proves neither evidence-existence nor ADR acceptance** —
  VERIFIED against `AdrLinter.Lint` (only `IsNullOrEmpty(Evidence)`; no `File.Exists`, no
  prose-status check). Status: ACCEPTED → INV-008 resolves + validates the cited sample and
  mechanically checks accepted/superseded status; add fixtures.
- **EXT-06 [IMPORTANT] Component-table producer not mechanically pinned** — DESIGN.md
  legitimately mentions `DafnyPipeline`. Status: ACCEPTED → ARCHITECTURE core-worker row is
  the authoritative structured source; validate route-a.json; no DESIGN grep.
- **EXT-07 [IMPORTANT] Env-local probe outcomes vs durable satisfaction** — degraded env →
  probe false → hard fail from clean; P3 ephemeral receipt breaks the local command.
  Status: ACCEPTED → green-from-clean is CONDITIONAL (INV-013); P3 uses a DURABLE committed
  attestation or a separated reference-CI gate (INV-010/OQ-002); soften EA-002.
- **EXT-08 [IMPORTANT] "Tags rejected" imprecise + no input limits** — YamlDotNet resolves
  built-in `!!str`/`!!int`; no byte/scalar/alias caps. Status: ACCEPTED → AST pre-validation
  rejects tags/anchors/aliases, one-doc/no-trailing, size caps (INV-002/PRH-003).
- **EXT-09 [IMPORTANT] SDK pin doesn't govern the root invocation** — muxer searches from
  cwd upward; spike-level global.json doesn't apply. Status: ACCEPTED → repo-root global.json
  (10.0.302, rollForward:disable) + build-time assertion (INV-016).
- **EXT-10 [IMPORTANT] P1 flip leaves parent stale** — parent INV-002/INV-043 still say
  "all-false". Status: ACCEPTED → atomic changeset updates the parent block + every
  current-state reference (INV-007/DD-003).
- **EXT-11 [IMPORTANT] OQs not safely disposed** — Status: MODIFIED-ACCEPT → OQ-001/002/003
  resolved now (DD-001/002/003); INV-043 gate-side need met by INV-012 (corrected-explain CLI
  deferred, can't live while BLOCKED) (DD-004); evidence-schema INV-044 explicitly scoped OUT
  with a parent/ARCHITECTURE note, readiness-block schema_version handled by INV-002 (DD-005).
- **EXT-12 [IMPORTANT] Direct deserialization into the domain record** — public record
  bypasses validation / `with`-mutable. Status: ACCEPTED → private DTO → validate → immutable
  domain type (INV-002/INV-003).

Codex-confirmed sound: duplicate-BLOCK vs duplicate-KEY separation; unknown-key/version/
status/cardinality fail-closed; `out/current` prohibition.

---
## Claude 6-agent adversarial pass (net-new; self-assessment brief = shared input)

### Self-assessment (highest residual risk)
- SA-1: INV-008 "recompute COMPATIBLE" undefined + entangled with out-of-scope DF-003 + red-from-clean spike.
- SA-2: INV-011 inert — src/ empty → empty shipped closure → only fixtures exercise it (AP-002).
- SA-3: INV-002 explicit-vs-implicit tag only works on the low-level Parser event API, not YamlStream DOM.
- SA-4: 6 ARCHITECTURE gaps — test_via, partition qualifier, TB-005 unregistered, kernel-split handler, INV-044 row, PAT-005 unwritten.
- SA-5: DD-001 cross-tree ProjectReference mixes CPM/lock contexts; DD-003 parent↔carrier signature drift.

### UX Auditor (10)
- RS-UX-01 [BLOCKING] INV-013/012/005: degraded-env P1 hard-fail indistinguishable from a real regression → probe reason taxonomy {evidence-absent vs refutes}; INV-012 renders degraded distinctly; assert on real pinned-path probe.
- RS-UX-02 [BLOCKING] INV-014: bare `dotnet test .slnx` can't self-detect zero-discovery; the guard is INSIDE the discovered set → move the zero-discovery/executed-count detector to an OUT-of-set wrapper that is the documented command.
- RS-UX-03 [IMPORTANT] INV-014: the documented gate command lives in no discoverable doc (README/AGENT_CONTEXT have no gate row) → name the doc home; bind the AP-020 verbatim test to the fenced command there.
- RS-UX-04 [IMPORTANT] ARCHITECTURE test_via still shows the buggy command; deferring the fix to OQ-A leaves a window where the doc lies → land the test_via fix in the same changeset (atomic).
- RS-UX-05 [IMPORTANT] INV-012 conflates valence: consistent-BLOCKED is a PASS but reads as failure → distinguish "PASS: BLOCKED is the expected Phase-0.1 state" from "FAIL: violation"; assert the pass-path banner.
- RS-UX-06 [IMPORTANT] INV-012 never asserts the typed probe reason renders to actionable human text → assert rendered reason string per unsatisfied precondition.
- RS-UX-07 [IMPORTANT] INV-011 vacuous pass is silent → report scanned closure size + a distinct "no production surface / src empty" state.
- RS-UX-08 [IMPORTANT] DD-003 atomic flip has no structural consistency gate on parent prose (AP-016) → add a check that parent prose current-state refs agree with the parsed block, or delete the prose duplications (block = single source).
- RS-UX-09 [IMPORTANT] INV-009/010: no discharge breadcrumb for the future P2/P3 maintainer → emit a discharge pointer in each skeleton reason; ship a skipped/pending placeholder naming the procedure.
- RS-UX-10 [MINOR] INV-016/EA-001: repo-root global.json has no precedence/removal story → document repo-root vs spike-level precedence + owned-by-gate removal note.

### Testability Auditor (4B/9I/5M)
- RS-T-01 [BLOCKING] INV-008(a′): "recompute COMPATIBLE" has no mechanical def; sample has no exit code → collapses to re-reading state:"COMPATIBLE" (circular AP-010). Pin an exact static predicate {state==COMPATIBLE ∧ final_suite_status==success ∧ exit_report_matrix_outcome==consistent ∧ ∀ route-A/shared per_probe_results==pass} + a mutation fixture that must drive false.
- RS-T-02 [BLOCKING] INV-008(b): ARCHITECTURE-row substring check false-fails (row contains "DafnyPipeline" in a negation) → check ONLY route-a.json structured (assemblies[].simple_name contains DafnyLanguageServer, not DafnyPipeline; LanguageServer is in assemblies not anchors), or add a machine-delimited closure block to ARCHITECTURE.
- RS-T-03 [BLOCKING] INV-002: explicit-vs-implicit !!str fixture unsatisfiable on YamlStream DOM → pin the low-level Parser event API; delete the YamlStream option from INV-002/PRH-003.
- RS-T-04 [BLOCKING] INV-011: real subject absent (src/ empty) → inert; bypass clauses (generated/linked/expression-body) need a real MSBuild+Roslyn build; "policy interface" disjunct unfalsifiable → real buildable fixture projects + concrete Roslyn predicate (any Body/ExpressionBody/initializer/base-list); drop policy-interface disjunct until interfaces exist.
- RS-T-05 [IMPORTANT] INV-008(a′) vs INV-015: JSON-Schema validation of the sample = a 2nd dependency → use in-box System.Text.Json field checks; state no schema-validation lib is added.
- RS-T-06 [IMPORTANT] INV-005 row 3: unresolvable-reference not decidable by the pure I/O-free kernel → add ReferenceResolution {Resolved|Unresolvable|Malformed} to the supplied ProbeResult (orchestrator populates), or a compiled-in token registry as a tested constant.
- RS-T-07 [IMPORTANT] INV-001/002: size caps undefined → pin MaxBlockBytes/MaxScalarLength/MaxNodeCount/MaxAliasCount as tested constants; boundary fixtures = constant+1.
- RS-T-08 [IMPORTANT] INV-002: no negative fixture for ready_predicate mismatch / name-drift / discharges-drift → add three reject fixtures.
- RS-T-09 [IMPORTANT] INV-010: only absent fixture → indistinguishable from a return-false stub → add present-but-malformed fixture with a distinct typed reason (parity with INV-009).
- RS-T-10 [IMPORTANT] INV-014: dotnet test exits 0 on zero discovery; .slnx support unproven → --logger trx + parse UnitTestResult, assert executed≥floor>0 and a–g/partition/committed-state names Passed; pre-flight .slnx restore/build test.
- RS-T-11 [IMPORTANT] INV-013: degraded-env only testable via proxy given pinned constants → give probes an injectable repo-root param (real code path); drive via a temp tree copy with evidence removed.
- RS-T-12 [IMPORTANT] INV-008(a″): accepted-status = free prose (AP-014) → add machine-readable status: line INSIDE the adr_lint block; validate that (or pin exact regex + captured fixture).
- RS-T-13 [IMPORTANT] DD-003: parent INV-002 single-arg EvaluateReadiness(blockText) vs carrier two-arg kernel → include the signature in the atomic changeset + a test the parent-cited symbol resolves to the kernel.
- RS-T-14..19 [MINOR] INV-006/007 exit sound once T09 lands; INV-012 denylist→allowlist regex; INV-004 no-I/O = structural (no System.IO ref); INV-003 reflection predicate (no public ctor/init); INV-001 anchored-header negative fixture (indented inline mention → 0-block reject); INV-007 mislabel integration→unit for the kernel corpus.

### Red Team (6B/7I/3M) — security/adversarial
- RS-RT-01 [BLOCKING] INV-008/INV-002: the ADR adr_lint block is a 2nd tamper-boundary input parsed by the NON-hardened spike line-scanner (ExtractLintBlock: last-key-wins, no dup rejection, comment truncation). Forge: add a 2nd route-A INCOMPATIBLE claim while keeping COMPATIBLE → routes.Any(COMPATIBLE) still matches → P1 stays true. Fix: run adr_lint through the SAME AST-hardened dup-rejecting closed-vocab parser (reject ≥2 route claims/id, ≥2 blocks, dup keys).
- RS-RT-02 [BLOCKING] INV-008(a′): P1 evidence PATH is read from the attacker-controlled ADR evidence: field, not pinned. Forge a sample that passes JSON-schema (schema permits status:"fail") but has a failing per_probe_result; if "recompute COMPATIBLE" reads declared route_verdicts[].state → false-true. Fix: pin the sample path as a tested constant, assert ADR's cited path EQUALS it; recompute from per_probe_results ∧ final_suite_status==success ∧ exit_report_matrix_outcome==consistent, never the declared state.
- RS-RT-03 [BLOCKING] INV-009/010: no fail-closed handler for a PRESENT well-formed artifact → a committed stub (e.g. {outcome:ran-passed,cores:64,RID:linux-x64}) flips P2/P3 before the real validator exists. Fix: skeleton returns {false, "validator-deferred"} UNCONDITIONALLY for any present input; add a present-well-formed→false fixture per probe.
- RS-RT-04 [BLOCKING] INV-011: source-only closure scan misses policy delivered as a binary <PackageReference>/<Reference> (a first-party pre-built policy DLL). Fix: assert the resolved non-framework referenced-assembly set is an allowlist (no first-party policy binaries) / scan referenced IL.
- RS-RT-05 [BLOCKING] INV-005/004: "unresolvable evidence reference → hard fail distinct from probe-false" has no defined resolver for readiness-block evidence strings → dead branch or conflated with probe verdict. Attack: P1.satisfied:true, evidence:"bogus-id" while probe genuinely true → bogus ref never rejected. Fix: define an evidence-reference registry (allowed ids/gate names per precondition) + a resolution step independent of the probe verdict; fixture: satisfied:true+probe-true+unregistered evidence → Fail.
- RS-RT-06 [BLOCKING] INV-008(a″): supersession claimed to fail-close but the probe reads ADR-0001 in ISOLATION — no discovery mechanism. Attack: commit ADR-0002 (Route B, marks 0001 superseded) but leave 0001 accepted/Route-A → P1 true, misses supersession. Fix: enumerate docs/adr/*.md, follow machine-readable supersedes/superseded_by chain to the terminal accepted ADR, validate ITS boundary; fail closed on ambiguity.
- RS-RT-07 [IMPORTANT] INV-008(b)/013: multiple committed route-a.json copies (pinned manifest + out/**/reports/route-a.json) → a glob resolves a leaked out/** copy. Fix: bind exact pinned path constant; fixture proving out/** copy NOT consulted (RS-010).
- RS-RT-08 [IMPORTANT] INV-008/DD-001/011: the P1 trust root (Components.cs in spikes/**) is neither in INV-011's partition nor integrity-pinned → a Components.cs change silently alters P1. Fix: source-digest pin / extract to a version-pinned shared lib; register where spikes/** sits in the partition.
- RS-RT-09 [IMPORTANT] INV-008(a′): the evidence SCHEMA file's integrity isn't re-anchored before validating the sample against it → relax the schema + craft a matching sample. Fix: assert schema file SHA-256 == registry-pinned digest == sample's evidence_schema_sha256; fail closed on mismatch.
- RS-RT-10 [IMPORTANT] INV-014: if the executed-count guard is a test INSIDE the .slnx suite, dropping Corrected.Gate.Tests disables both. Fix: put the TRX/executed-count assertion in a layer the .slnx edit can't remove (reference-CI / standalone harness).
- RS-RT-11 [IMPORTANT] INV-004/005/006: the READY-accept path is exercised ONLY with synthetic probeResults; the first real BLOCKED→READY runs an orchestrator→kernel accept path never executed. Fix: explicit residual gate — the discharge that first flips a precondition true must re-run the READY-accept branch through the real orchestrator.
- RS-RT-12 [IMPORTANT] DD-003: inverse partial (md-kept P1:true, gate reverted/not-merged) → the block permanently asserts P1 satisfied with NO gate re-deriving it. Fix: a committed P1:true with no passing carrier is itself blocking; cross-check fails if the block claims P1:true while the carrier committed-state test is absent.
- RS-RT-13 [IMPORTANT] INV-010/DD-002 (TB-003): P3's durable attestation is an UNSIGNED committed claim — anyone with commit access forges ran-passed. Fix: P3 attestation must carry a provenance binding (signature/SLSA/receipt digest chained to reference-CI), not bare committed JSON — else prose-strength (AP-004).
- RS-RT-14 [MINOR] INV-001: block extraction reads the whole (tamperable) md before the byte cap engages → multi-GB file OOMs. Fix: bound the file read itself.
- RS-RT-15 [MINOR] INV-011/001: keying the ban on status==BLOCKED means an indeterminate/unparseable status skips the ban → run the scan when BLOCKED OR indeterminate (deny-by-default).
- RS-RT-16 [MINOR] INV-002: enforce AST size caps INCREMENTALLY (abort at cap+1 during streaming), not after draining all events.

### Design Contract (3B/5I/4M)
- RS-DC-01 [BLOCKING] parent INV-002 kernel signature (phase-0-1-worker.md:213 single-arg blockText string) contradicted by carrier two-arg (ReadinessBlock, probeResults); NOT in the EXT-10/DD-003 edit set. Fix: add the signature line to the atomic changeset.
- RS-DC-02 [BLOCKING] INV-005 "total" table OMITS the (evidence==null ∧ declared false ∧ actual true) cell — exactly the current committed P1 state pre-flip; undefined → implementer may treat as "consistent" → un-flipped block passes → BLOCKED-but-actually-satisfied slips. Fix: add explicit row (null,false,true)→Fail; pin fixture (f)'s evidence value.
- RS-DC-03 [BLOCKING] DD-005 orphans INV-044's history registry+meta-test — parent INV-044/INV-036/Complexity-Budget AND ARCHITECTURE all home it in THIS carrier (the only exempt surface); "Phase-0.1 runtime" mislabel (runtime half = dispatch table, not registry). Fix: either home INV-044's registry+meta-test here, OR add the parent+ARCHITECTURE re-homing to the atomic changeset.
- RS-DC-04 [IMPORTANT] carrier INV-008 drops the DESIGN.md component-table check parent INV-003 mandates (parent: "reads DESIGN.md AND ARCHITECTURE.md tables"); un-propagated DESIGN.md would pass P1 (AP-016). Fix: reconcile parent INV-003 (ARCHITECTURE row authoritative; DESIGN prose publication-scoped) in the atomic set, or read DESIGN §12 core-worker TABLE (not a grep).
- RS-DC-05 [IMPORTANT] evidence-reference resolution undefined (dup of RS-RT-05/RS-T-06): probes bound by id, never by the evidence string; parent INV-002 "re-derive from the NAMED evidence gate" not realized. Fix: specify evidence-reference format + resolver; make P1's evidence string the resolved subject.
- RS-DC-06 [IMPORTANT] INV-036/PRH-008 self-enforcement is fixture-only/inert while src/ empty (AP-004 claim-exceeds-layer) — the invariant that's supposed to embody PAT-004 currently is prose-strength. Fix: real closure + fail-closed-when-uncomputable + a scaffold shipped project so ≥1 real closure is scanned, or record inertness in the residual ledger.
- RS-DC-07 [IMPORTANT] TB-005 referenced by INV-001/002/003/BND-001/STRIDE but registered nowhere (ARCHITECTURE has only TB-001..004). Fix: register TB-005 (readiness-block intake/tamper) with Invariant/Enforced-at/Test — OQ-A omits it.
- RS-DC-08 [IMPORTANT] ARCHITECTURE readiness-build-gate entrypoint describes a single Evaluate handler + "path scan"; carrier splits kernel+orchestrator+probes+closure-scanner. Fix (/cupdate-arch): update handler/decomposition + "path scan"→"shipped-closure scan".
- RS-DC-09..12 [MINOR] TB-004 scope doesn't cover YamlDotNet/gate lock/props/global.json; repo-root global.json + spikes ProjectReference fall outside every entrypoint scope; PAT-005 unwritten; DD-003 collapses RS-002's "reject fixtures proven BEFORE the discharge" ordering into one commit (prove INV-007 green before adding the flip line, or split commits).

### Upgrade Compat (3B/5I/2M)
- RS-UC-01 [BLOCKING] the gate is built but NEVER wired to run — reference-ci-provenance is out of scope, the only workflow runs the spike; INV-014 cites a reference-CI step this spec doesn't build → gate is repo-level inert (AP-002), the exact PMB-001/002 deferred-net trap. Fix: wire an executable from-clean `dotnet test gate/Corrected.Gate.slnx` CI job (or a gate step) as part of GREEN, not deferred.
- RS-UC-02 [BLOCKING] repo-root global.json is a 2nd SDK pin with no sync gate vs spikes/dafny-compat/global.json → edit one not the other = silent AP-015 drift. Fix: single source of truth (repo-root governs / spike references it) or a test asserting both sdk.version byte-identical; extend DD-006 bump procedure.
- RS-UC-03 [BLOCKING] P1 hard-binds the spike's evidence_schema_version:2 with no negotiation; the spike EXPECTS to bump (append-only registry) and DD-006 doesn't know about this reverse dep → a future v3 silently breaks P1. Fix: INV-008 pins the accepted evidence_schema_version + fail-closed "unrecognized evidence schema version" (distinct reason); register the gate as a downstream consumer in the spike's DD-006 checklist.
- RS-UC-04 [IMPORTANT] readiness-block schema_version has no recognized-set evolution affordance (AP-005) — a future v2 / P4 hard-breaks; no documented add-a-version path. Fix: document the legitimate-change affordance (recognized-set + table + DTO edited together with a test they agree).
- RS-UC-05 [IMPORTANT] YamlDotNet pin has no CVE/bump affordance; a major bump can break the low-level Parser hardening API under patch pressure. Fix: document the bump path (re-run AST-hardening fixtures on any bump; one named version constant); register under DD-006.
- RS-UC-06 [IMPORTANT] no gate NuGet.Config / CPM opt-out — NuGet config merges upward (restores YamlDotNet from ambient sources today, TB-004 gap); a future repo-root Directory.Packages.props flips the gate into CPM and its inline Version=18.1.0 errors. Fix: ship gate/NuGet.Config (<clear/> + single pinned source) + gate/Directory.Packages.props or ManagePackageVersionsCentrally=false.
- RS-UC-07 [IMPORTANT] rollForward:disable at repo scope blocks ALL SDK patch adoption repo-wide (security patches; old-band patches drop from installers) → whole repo frozen on 10.0.302; contributor on latest patch locked out at repo root. Fix: consider rollForward:latestPatch at repo root (reproducibility via lockfile + build-time NETCoreSdkVersion assertion), or attach DD-006 bump procedure.
- RS-UC-08 [IMPORTANT] .slnx coupled to exact SDK pin — unproven on 10.0.302, unstable across a bump, older/IDE dotnet may not grok .slnx. Fix: prove .slnx dotnet test on 10.0.302 before GREEN or fall back to classic .sln; treat as a pinned-SDK capability the bump procedure re-verifies.
- RS-UC-09 [MINOR] gate green-from-clean is downstream of the spike's build health (currently red-from-clean); DD-001 ProjectReference makes the gate COMPILE against the spike's build graph. Fix: take DD-001 fallback now (extract linter to a Dafny-free shared lib) so the gate build is insulated.
- RS-UC-10 [MINOR] ARCHITECTURE still assigns INV-044 registry+meta-test to the carrier, contradicting DD-005 → phantom-requirement confusion. Fix: the /cupdate-arch amendment strips it + records the evidence-schema registry lives spike-side.

### Assumptions Auditor (1B/9I/4M)
- RS-A-01 [BLOCKING] no <clear/>-scoped single-source NuGet.Config for the gate → restore walks up, never reaches the spike's, uses machine/user sources (ARCHITECTURE TB-004 requires <clear/> single-source); cross-tree SpikeContracts ref restored under ambient config defeats the spike's isolation. Fix: gate/NuGet.Config <clear/> + source mapping; EA single-source-isolated.
- RS-A-02 [IMPORTANT] .slnx unproven on 10.0.302 (repo is ALL classic .sln); rollForward:disable = no forgiveness; no .sln fallback. Fix: prove .slnx on 10.0.302 or specify .sln fallback; EA.
- RS-A-03 [IMPORTANT] from-clean silently needs network/DNS/TLS/clock to nuget.org (implicit restore of YamlDotNet + unpinned test-host/xUnit); INV-013 omits it; spike used a local NUGET_PACKAGES cache. Fix: EA naming network/cache + clock/TLS as from-clean preconditions.
- RS-A-04 [IMPORTANT] repo-root anchoring of the tested path constants unspecified (cwd via dotnet test vs AppContext.BaseDirectory→repo marker) → cwd-relative anchor fail-closes from a different dir (PMB-001/AP-020 class). Fix: deterministic repo-root anchor (walk up to a committed sentinel); test from ≥2 cwds.
- RS-A-05 [IMPORTANT] gate not self-contained: SpikeContracts multi-targets net10;net8 → the ProjectReference drags the net8 targeting pack + a healthy spike subtree into the gate restore. Fix: constrain to net10 only, or extract AdrLinter to a Dafny-free single-TFM shared lib before GREEN; EA net8 pack.
- RS-A-06 [IMPORTANT] gate test-host/xUnit/runner unpinned (spike pins Test.Sdk 17.11.1 + xunit 2.9.2 via its CPM) → INV-014 discovery/exit/.slnx/TRX semantics undefined (VSTest exits 0 on zero tests; MTP differs). Fix: pin+lock the gate's test-host/xunit/runner (extend INV-015); state the platform INV-014 assumes.
- RS-A-07 [IMPORTANT] INV-014's non-zero-executed assertion needs --logger trx/parse → a DIFFERENT argv than the verbatim documented command (AP-020 tension). Fix: bake the logger into the documented command so verbatim == counted invocation.
- RS-A-08 [IMPORTANT] ARCHITECTURE core-worker row present/absent is free prose (dup RS-T-02/RS-DC-*): a contains-check sees "DafnyPipeline" in the NOT-loaded clause → mis-flags present. Fix: machine-readable list / route-a.json sole authority.
- RS-A-09 [IMPORTANT] no isolation for xUnit default parallelism: the degraded-env fixture (touch/remove committed ADR/sample) races the INV-006 real-probe test reading the same files (AP-019). Fix: injectable path/temp-copy (never mutate the committed artifact) or a serial collection; isolation invariant.
- RS-A-10 [IMPORTANT] line-ending/encoding (no repo .gitattributes; git autocrlf) shifts the byte-cap, the ^implementation_readiness: anchor, and YamlDotNet's trailing-content view. Fix: .gitattributes LF for parsed specs/ADR; UTF-8-no-BOM EA; normalize newlines before the byte-cap.
- RS-A-11..14 [MINOR] dual global.json can silently diverge (assert both agree); "committed regular file" is OS/case-dependent + "committed" unverifiable without git (pure kernel forbids) → define operationally / orchestrator git-check + POSIX-FS EA; INV-011 also assumes native-output RID + existing policy interfaces (inert until src/ exists); fence-strip contract (strip ```yaml delimiters, anchor col-0) + a decoy-2nd-block reject test.

---
## Disposition (2026-07-25) — all findings ACCEPTED; incorporated into spec v3

Strategic decision (maintainer): **P1 kept in scope + hardened** (not de-scoped); **spec revised (v3)
+ ARCHITECTURE amended in the same pass.**

- codex EXT-01..12: incorporated in v2, carried into v3.
- Self-assessment SA-1..5 + UX RS-UX-01..10 + Testability RS-T-01..19 + Red-Team RS-RT-01..16 +
  Design-Contract RS-DC-01..12 + Upgrade RS-UC-01..10 + Assumptions RS-A-01..14: ACCEPTED, incorporated
  into readiness-gate-carrier.md v3.
- Key structural changes in v3: pure-kernel signature + no-I/O (INV-004); verdict table with the
  (null,false,true) cell + ReferenceResolution field + evidence-reference registry (INV-005); low-level
  Parser API + all-tags/anchors/aliases rejected + hardened ADR parse (INV-002/008a/PRH-003/PRH-005);
  P1 hardened — pinned evidence path + schema-integrity anchor + sound COMPATIBLE recompute + supersession
  chain + trust-root pin + route-a.json-only component check (INV-008); shipped-closure INV-036 + binary-ref
  allowlist + scaffold + vacuous-visibility (INV-011); out-of-suite TRX guard + pinned test-host + .slnx
  proof/.sln fallback + doc home (INV-014/015); repo-root global.json + sync + NuGet.Config <clear/> + CPM
  opt-out + rollForward:latestPatch (INV-016); **wired from-clean CI job (INV-017)**; extract-linter-to-
  shared-lib insulation (INV-018/DD-001); atomic parent flip incl. signature + prose consistency gate +
  inverse-partial PRH-006 (DD-003); INV-044 re-homing corrected (DD-005); TB-005/PAT-005/partition/test_via
  ARCHITECTURE amendments APPLIED.
- Residual OQ-A (maintainer confirmations, non-blocking): rollForward latestPatch vs disable; .slnx vs .sln;
  P3 provenance mechanism.

---
---

# Review-Spec Findings: readiness-gate-carrier — ROUND 2 (v3 re-review)

Date: 2026-07-25
Spec: .correctless/specs/readiness-gate-carrier.md (v3)
Agents: self-assessment + red-team + assumptions + testability + design-contract + upgrade-compat + ux (6/6 Claude complete)
External: codex GPT-5.6-sol xhigh (appended below if it completed; producer path skipped — invoked directly, joshft/correctless#199)
Intelligence brief: consumed (cross-feature-intel present; AP-002/010/014/016/020/021 dominant, corroborated by findings)

> Round-2 scope: v3 was a near-rewrite that folded in ~70 round-1 findings and had NOT itself been reviewed. These findings are NEW problems the v3 hardening introduced (contradictions, unsatisfiable-from-clean requirements, cross-spec drift) or round-1 issues the hardening did not fully close. Consensus = independently found by ≥2 agents (higher confidence). Round-2 ids are RS-2xx to avoid collision with round-1 RS-0xx.

## Headline

The parser/kernel core (INV-001..005) is well-grounded and mostly testable. **Escape risk is concentrated in the P1 probe (INV-008) and the DD-003 atomic changeset**, and the v3 hardening introduced (a) a **genuine forge-P1=true bypass** (RS-201), (b) at least **two internal contradictions that are red-from-clean as written** (RS-240 byte-identical sync; RS-220/RS-221 the atomic-flip deadlock), and (c) a P1 "resolves true today" claim that **depends on ADR fields and evidence-sample bindings that do not exist / actively refute it in the current tree** (RS-203, RS-210). Recommendation: **revise before advancing to TDD.**

---

## CRITICAL

### Finding RS-201: Vacuous COMPATIBLE recompute — forge P1=true (no cardinality floor)
**Source**: red-team (RT-CRIT-1)
**Category**: security · **INV**: INV-008(a″), PRH-001, TB-005 · **Confidence**: high (concrete bypass)
**Description**: INV-008(a″)'s recompute `… ∧ (∀ per_probe_results where route ∈ {A,shared}: status==pass)` has no cardinality/presence floor. A commit-access adversary edits the tamperable canonical sample to keep the three declared top-level fields (`route_verdicts[A].state=COMPATIBLE`, `final_suite_status=success`, `exit_report_matrix_outcome=consistent`) but strips `deterministic.per_probe_results` to route-B-only or empty; the `{A,shared}` filter yields ∅, the `∀` is vacuously true, and the recompute collapses to trusting the declared `route_verdicts[].state` — the exact thing (a″) says it will "never" do. The spike's real `VerdictAggregator.ComputeRouteVerdict` (Components.cs 134-180) guards this (manifest-cardinality equality + `relevant.Count==0→Incomplete` + plan-shrinkage denial); the hand-rolled reimplementation drops the guard. Mutation fixtures only test "flip one route-A probe to fail" — never empty/route-B-only.
**Proposed fix**: re-derive the expected probe set from the pinned manifest and assert set-equality (cardinality + identity) BEFORE the ∀; add empty-set + route-B-only + wrong-cardinality fixtures.
**Status**: pending

### Finding RS-203: Supersession sub-oracle (a‴) reads fields that exist nowhere; absent-chain undefined; == parent OQ-004 (open)
**Source**: red-team (RT-CRIT-2), self-assessment, testability (HIGH), assumptions (MED) · CONSENSUS (4)
**Category**: correctness/testability · **INV**: INV-008(a‴), TB-005 · **Confidence**: high
**Description**: (a‴) follows a machine-readable `supersedes/superseded_by` chain, but `docs/adr/` has exactly one file (ADR-0001) and NO `supersedes`/`superseded_by` fields anywhere; DD-003 adds only a `status:` line, not chain fields. The closed-vocabulary INV-002 parser (`WithEnforceRequiredMembers`, reject-unknown) would REJECT chain keys unless the ADR DTO vocab is explicitly extended — which the spec never states. The absent-chain terminal rule ("no `superseded_by` ⇒ terminal-self pass" vs "⇒ ambiguous fail-closed") is undefined. This is exactly parent **OQ-004** (phase-0-1-worker.md:1653), which is OPEN — the carrier assumes a resolution it doesn't state. If absent ⇒ ambiguous, P1=false → the atomic flip is dead-red and unlandable. Even under the benign reading, (a‴) provides zero security value today (one ADR) while adding a fail-closed branch (AP-004 strength > reality).
**Proposed fix**: add `supersedes`/`superseded_by` to the ADR `adr_lint` block AND the closed-vocab DTO (list in Packages Affected); define the terminal rule verbatim ("`status==accepted` + no `superseded_by` ⇒ terminal accepted; two terminals / cycle / dangling target ⇒ fail-closed"); enumerate a‴ fixtures against that rule; explicitly close/​reference parent OQ-004.
**Status**: pending

### Finding RS-220: The `(null,false,true)→Fail` cell makes the carrier greenable ONLY in the exact flip-commit with all six INV-008 sub-oracles true — no incremental landing path
**Source**: red-team (RT-CRIT-3), self-assessment, testability, assumptions · CONSENSUS (4)
**Category**: correctness/process · **INV**: INV-005, INV-006, INV-008, INV-013/017, DD-003, AP-021 · **Confidence**: high
**Description**: The currently committed block (`P1.satisfied:false`, `P1.evidence:null`) with a real probe returning P1=true is `evidence==null ∧ declared false ∧ actual true → Fail` — so the gate is red from a clean checkout of today's repo, and green ONLY after the atomic changeset flips P1 to `true` with a registered `evidence`. There is no incremental path: you cannot land the gate green with P1 unflipped (actual refutes declared), and you cannot flip P1 unless all six sub-oracles (a/a′/a″/a‴/b/c) robustly return true from clean on the first try (INV-013/INV-017 forbid deferral). Any single unsatisfiable-from-clean sub-clause (see RS-203, RS-210) makes the changeset unlandable and unrecoverable (AP-021 bootstrap deadlock, AP-016 all-or-nothing).
**Proposed fix**: this is inherent to the flip design, but it must be made explicitly satisfiable: resolve RS-203/RS-210 so all six sub-oracles are provably true-from-clean at the flip commit; state that the pre-flip commit and the flip commit are each independently green-from-clean; partition INV-007 (RS-221). Consider whether the `(null,false,true)→Fail` cell should instead be a distinct "block-understates-satisfaction — advisory, not gate-fail" state until the flip lands, to remove the deadlock shape.
**Status**: pending

---

## HIGH

### Finding RS-202: The authoritative hardened path never asserts the ADR's own decision fields
**Source**: red-team (RT-H1) · **Category**: security · **INV**: INV-008(a), PRH-005, TB-005
**Description**: INV-008(a) enumerates only what the hardened parser REJECTS; it never asserts `boundary_decision==<in-process-selected>`, `selected_route==A`, or route-A `verdict==COMPATIBLE`. Those positive-selection assertions live only in the spike's `AdrLinter.Lint` (Components.cs 1061-1071), which the spec DEMOTES to non-authoritative. So a tampered ADR with `selected_route:B` / `boundary_decision:rejected` passes the trust decision as long as the pinned sample recomputes COMPATIBLE and route-a.json matches (neither reads the ADR decision fields). Combined with RS-201 → forge P1=true from an ADR that does not even select Route A.
**Proposed fix**: move the decision-field assertions into the authoritative hardened path with explicit tamper fixtures (`selected_route:B → false`, `boundary_decision:rejected → false`, route-A `verdict:INCOMPATIBLE → false`).
**Status**: pending

### Finding RS-204: ADR `docs/adr/*.md` is an unpinned glob → fail-closed DoS/griefing on a gate that blocks all PRs
**Source**: red-team (RT-H2) · **Category**: security/availability · **INV**: INV-008(a‴), INV-017, TB-005
**Description**: (a‴) enumerates `docs/adr/*.md` (unpinned directory glob) while (c) pins route-a.json "by exact path constant, never a glob." Every ADR is a fail-closed trigger. Any committer adds `docs/adr/ADR-0002-*.md` with a `status:accepted` + a chain field creating ambiguity (two accepted terminals / cycle / Route-B terminal) → P1=false → the whole readiness gate Fails → every PR blocked (INV-017 gates all PRs) until the file is found and removed.
**Proposed fix**: pin the ADR set the same way as route-a.json (an explicit committed ADR-path allowlist / a single authoritative ADR chain root), not an open glob; define behavior for an unrecognized ADR file as ignore-with-log, not fail-closed.
**Status**: pending

### Finding RS-206: "Same hardened parser" for `adr_lint` is under-specified — the real block has a different schema; verbatim DTO reuse would REJECT the real ADR
**Source**: design-contract (MED), assumptions (MED), upgrade-compat (MED) · CONSENSUS (3) · **Category**: correctness/AP-014 · **INV**: INV-008(a), INV-002
**Description**: The committed `adr_lint` block (ADR-0001:28-40) is `{boundary_decision, selected_route, routes[{route, verdict, adjudication_record_id, evidence}]}` — a DIFFERENT schema from the readiness block, with none of INV-002's required members (`schema_version`/`status`/`ready_predicate`/`preconditions`). Reusing INV-002's DTO with `WithEnforceRequiredMembers()` + no `IgnoreUnmatchedProperties()` verbatim would reject the real ADR. "Same parser" must mean same hardening machinery, DISTINCT `AdrLintBlock` DTO; and INV-008 lacks a real-producer verbatim fixture of the committed block (AP-014).
**Proposed fix**: name a distinct `AdrLintBlock` DTO enumerating the full current `adr_lint` vocabulary (incl. `adjudication_record_id`, the `routes[]` shape, plus the new `status:`/chain keys); add a verbatim real-producer fixture; state "same machinery, distinct schema" explicitly.
**Status**: pending

### Finding RS-210: The committed evidence sample the gate might bind recomputes NOT-COMPATIBLE; the exact pinned constant is never named
**Source**: upgrade-compat (HIGH), red-team (RT-L3), testability, assumptions · CONSENSUS (4) · **Category**: correctness · **INV**: INV-008(a′)(a″), DD-002
**Description**: There are two committed samples: `run-report.sample.json` (`route_verdicts[A].state=INCOMPLETE`, `final_suite_status=unknown` → recomputes NOT-COMPATIBLE) and `run-report.canonical.sample.json` (COMPATIBLE, schema-digest `c872c710…` matching the registry v2 row). INV-008(a″)'s recompute only passes against the canonical one, but the spec never names the exact pinned P1-evidence path constant. If it binds (or the ADR cites) the variance sample, P1 dead-reds from clean with an `evidence-refutes` verdict that looks like a real regression.
**Proposed fix**: name the exact pinned constant = the canonical sample; add a fixture asserting the pinned committed sample recomputes COMPATIBLE (so a future producer regen that flips it fails loudly with `evidence-refutes`, distinguished from `evidence-absent`).
**Status**: pending

### Finding RS-221: The required commit ordering is self-contradictory (corpus-green-before-flip / CI-wired-not-deferred / current-state-Pass)
**Source**: red-team (RT-H4), testability (MED) · CONSENSUS (2) · **Category**: process · **INV**: RS-002/DD-003, INV-006, INV-017, INV-007, AP-021
**Description**: DD-003/RS-DC-12 requires the INV-007 reject corpus green BEFORE the flip; INV-017 requires the from-clean CI wired NOT deferred; INV-006's current-state test asserts `Pass` on the committed block. But at any pre-flip commit the committed block reads `P1.satisfied:false` while the real orchestrator returns true → INV-006 Fails → the wired CI is red at that commit. The three requirements have no satisfying commit ordering. INV-007 also bundles two greenability timelines (supplied-input reject fixtures = flip-independent; committed-block current-state = green only post-flip) and DD-003 is ambiguous about which half must be green "before."
**Proposed fix**: partition INV-007 explicitly — the SUPPLIED-input reject corpus (excluding the committed-block binding) green in the pre-flip commit; the committed-block current-state test introduced in the SAME atomic commit as the flip. State every commit must be green-from-clean.
**Status**: pending

### Finding RS-222: DD-003 "structural consistency gate" is ill-defined — compares prose only against block values, so it cannot catch the signature/semantic/entrypoint misses it exists for
**Source**: design-contract (H1), red-team (RT-H5) · CONSENSUS (2) · **Category**: correctness/AP-016 · **INV**: DD-003, TB-005
**Description**: DD-003's gate "fails if any parent current-state prose disagrees with the parsed block," but a missed kernel-signature / semantic ("it re-derives the discharge") / integration-contract edit is not a block VALUE, so the gate cannot detect it — a build faithful to parent INV-002's letter (probe-calling kernel) would fail carrier INV-004's no-I/O test. Additionally, the parent legitimately discusses `status=BLOCKED` as the current expected state in prose (lines 1069/1443/196) — a deterministic gate cannot distinguish legitimate BLOCKED prose from stale disagreeing prose without NLP, so it is either false-positive (dead-reds the flip) or too loose (AP-016 partial migration escapes).
**Proposed fix**: replace the prose-vs-block gate with machine-readable current-state anchors/markers in phase-0-1-worker.md (so an unmarked disagreeing mention is caught or structurally impossible); broaden the gate to diff the kernel-signature/orchestration prose; require its failure message to name each disagreeing site (`file:line`, expected vs found).
**Status**: pending

### Finding RS-223: DD-003 under-enumerates the parent sites the atomic flip must touch
**Source**: design-contract (×3), assumptions (×3), self-assessment · CONSENSUS (3) · **Category**: correctness/AP-016 · **INV**: DD-003
**Description**: Beyond the listed edits, the flip must also reconcile: (a) parent INV-002's semantic "the pure decision function re-derives the discharge / every probe runs" prose that conflates kernel+orchestrator (carrier INV-004 forbids); (b) the stale INV-002 integration-contract line "no entrypoint YAML exists yet — see OQ-002" (false now that ARCHITECTURE defines `readiness-build-gate`); (c) parent INV-003 appears TWICE (Statement:270 + Enforcement-(b):288) but DD-003 says "the clause" singular; (d) the stale parent INV-003 "backed by a schema-valid terminal adjudication record" clause (DF-002 made `adjudication_record_id` optional; committed block carries `null`); (e) parent EA-016 "full-history clone required for P1/P2 binding" vs carrier EA-002 "history NOT required" — two evidence-binding trust models; (f) parent INV-036's "type implementing a policy interface" disjunct that INV-011 drops (no OQ tracks its return); (g) parent OQ-004 supersession-format (RS-203).
**Proposed fix**: expand DD-003's enumeration to all of (a)–(g); add each as an explicit checklist assertion (not just the consistency gate).
**Status**: pending

### Finding RS-230: INV-011's real MSBuild/Roslyn closure scan is unbuildable/unpinnable/unlocatable as specified
**Source**: testability (H3), self-assessment, design-contract, assumptions, red-team · CONSENSUS (5) · **Category**: testability/supply-chain · **INV**: INV-011, INV-015, TB-004
**Description**: Four concrete blockers: (1) no injectable closure target (unlike INV-013's injectable repo-root) — the fixtures {one-real-method, linked gate/** source, generated source, binary first-party PackageReference} have nowhere to point; (2) the scaffold-fixture home is either production-under-`src/` (trips the ban) or under `gate/`/`test/` (not a shipped closure); (3) Roslyn/MSBuild deps (`Microsoft.CodeAnalysis.CSharp`, `Microsoft.Build`/MSBuildLocator) are unpinned under INV-015/TB-004 → AP-015 non-reproducible + a supply-chain hole; (4) the binary-first-party-PackageReference fixture is unbuildable under the single-source `<clear/>` locked restore (needs a second source), and the generated-source fixture needs an unspecified committed `IIncrementalGenerator`.
**Proposed fix**: make the scanner take an injectable closure-target set; name the scaffold home (e.g. `test/fixtures/shipped-closure/**`) scanned as a closure only when injected; add exact Roslyn/MSBuild versions to INV-015's pinned+locked set + lockfile; implement the binary case via `<Reference Include>` + HintPath to a checked-in first-party DLL and specify the generator fixture.
**Status**: pending

### Finding RS-231: INV-011 reintroduces ambient MSBuild resolution (TB-004 violation)
**Source**: red-team (RT-H3), design-contract · CONSENSUS (2) · **Category**: supply-chain · **INV**: INV-011, TB-004
**Description**: A real MSBuild/Roslyn build via MSBuildLocator resolves an ambient SDK MSBuild and pulls the repo-root `Directory.Build.props`, machine-level MSBuild SDK resolvers, and inherited props — precisely the ambient resolution TB-004 forbids. `gate/NuGet.Config` `<clear/>` isolates NuGet restore, NOT MSBuild evaluation of the scanned closure.
**Proposed fix**: drive the closure via the pinned-SDK `dotnet build`/`dotnet msbuild` out-of-process (bound by INV-016's global.json), not in-process ambient MSBuildLocator; pin the analysis toolchain (RS-230#3); add a loaded-identity assertion for the Roslyn/MSBuild versions actually used.
**Status**: pending

### Finding RS-240: INV-016's "byte-identical" dual-pin sync test is unsatisfiable (contradicts its own `rollForward: latestPatch`)
**Source**: design-contract (H2), upgrade-compat (H1), testability (H2), assumptions (HIGH) · CONSENSUS (4, unanimous) · **Category**: correctness · **INV**: INV-016, EA-001, TB-004
**Description**: INV-016 requires the repo-root global.json be byte-identical to `spikes/dafny-compat/global.json` AND set `rollForward: latestPatch`; but the spike file has `rollForward: "disable"`, a leading `"//"` comment key, and `allowPrerelease: false`. Byte-identical + differing rollForward is impossible; the only literal-satisfying path edits the frozen (TB-004, DD-006) spike file. ARCHITECTURE TB-004 repeats the wrong "byte-identical" wording.
**Proposed fix**: assert SEMANTIC sync of the load-bearing field only (`sdk.version == "10.0.302"`); explicitly allow `rollForward`/`allowPrerelease`/comments to differ; drop "byte-identical" in INV-016 and ARCHITECTURE TB-004.
**Status**: pending

### Finding RS-253: INV-017's "wired CI" assertion is itself a keyword-presence check — the exact PMB-001/AP-020 trap
**Source**: red-team (RT-H6), design-contract · CONSENSUS (2) · **Category**: correctness/AP-011/020 · **INV**: INV-017, AP-021
**Description**: INV-017 enforces via "a committed .github/workflows job whose presence + from-clean form + verbatim command are asserted." Parsing a YAML file for its command is a doc-grep, not execution — structurally identical to PMB-001 (INV-014 "verified" an entry point via `Assert.Contains(...)` while every behavioral test used a normalized proxy). The gate assembly cannot execute GitHub Actions.
**Proposed fix**: extract the from-clean invocation + TRX executed-count guard into a runnable script (ARCHITECTURE's `reference-ci-provenance` "or its extracted script"); a test executes that script verbatim from a clean checkout; bind INV-017's Enforcement to the live execution, not the static presence assertion.
**Status**: pending

### Finding RS-290: INV-012/INV-011 human-facing messages are invisible on the green path
**Source**: ux (UX-001, UX-009) · **Category**: ux · **INV**: INV-012, INV-011, DD-004
**Description**: The `readiness-build-gate` is declared `type: cli` but has no CLI binary — its only invocation is `dotnet test`, which swallows passing-test output. INV-012's valence banner, INV-011's "no production surface (src/ empty)" notice, and per-precondition reasons are specified only as test-asserted strings. An operator who runs the documented command and sees green sees NONE of it → cannot distinguish "green gate correctly reporting BLOCKED" from "gate did nothing," which is the precise confusion INV-012 exists to prevent; hollows out the DD-004 "self-explainer met gate-side" claim.
**Proposed fix**: require a human-visible surface on the green path (a small console renderer invoked by the documented command, or the out-of-suite reference-CI lane echoing the banner to the job log); assert the banner/vacuous-state text appears on STDOUT of the documented invocation, not merely in an xUnit assertion.
**Status**: pending

---

## MEDIUM

### Finding RS-205: Two-parser ADR design — dead code or a second dead-reddable permissive gate; zero coverage of (a‴)
**Source**: red-team (RT-M1), self-assessment, design-contract · CONSENSUS (3) · **INV**: INV-008(a), PRH-005, AP-002
**Description**: The gate runs BOTH the hardened parser and the spike's permissive `AdrLinter.Lint`/`ExtractLintBlock` on the same ADR. If the spike's zero-findings result is ANDed into P1, it is a second permissive line-scanner over a tamperable input that can dead-red P1 independently and diverge from the hardened parser (it stops at the first fence, cannot see a forged second block). If not ANDed, it is structurally-inert green coverage (AP-002). `ExtractLintBlock` ignores `status:`/chain keys → zero coverage of (a‴), making (a‴) a single-implementation un-cross-checked oracle.
**Proposed fix**: state precisely how the spike linter's result participates; add a differential fixture proving both parsers agree on the committed ADR; do not rely on the permissive scanner for any trust decision.
**Status**: pending

### Finding RS-207: INV-008(a″) recompute field paths are flat in the spec but nested under `deterministic.` in the real producer (AP-014)
**Source**: red-team (RT-M3), testability, self-check · CONSENSUS (3) · **INV**: INV-008(a″)
**Description**: The recompute names `route_verdicts[A].state`, `final_suite_status`, `exit_report_matrix_outcome`, `per_probe_results` as if top-level; in the real sample all four are nested under `deterministic` (lines 26/39/173/174). A literal implementer reads null → probe fails closed → P1 can't flip (deadlock); or a hand-written flat fixture passes while the nested sample fails (AP-014).
**Proposed fix**: pin the exact nested JSON paths; capture the fixture verbatim from the producer.
**Status**: pending

### Finding RS-208: Machine/prose ADR status split is unguarded (human-review evasion)
**Source**: red-team (RT-M8) · **INV**: INV-008(a‴), TB-005
**Description**: The gate reads the machine `status:` line but ignores the top-of-file prose `**Status**: accepted`. An adversary sets prose `Status: superseded` (misleading every human reviewer) while leaving machine `status: accepted` (passing the gate), or vice-versa.
**Proposed fix**: add a prose↔machine status consistency check (or state the machine field is authoritative and require the prose to be generated/checked against it).
**Status**: pending

### Finding RS-209: Digest triple-pin (schema) + source-digest pin have no synchronized cross-tree update affordance; the evidence schema already moved to v2
**Source**: upgrade-compat (HIGH), red-team (RT-M2), self-assessment · CONSENSUS (3) · **INV**: INV-008(a′)(c), AP-005/AP-016
**Description**: (a′) requires `schema-file SHA == registry-pinned digest == sample.evidence_schema_sha256`, and `evidence_schema_version ∈ pinned-accepted-set`. The evidence schema is ALREADY at v2 (schema-version-registry.json shows v1→v2 during a spike QA round); the spike bumps it routinely per DD-006. A v2→v3 bump rotates the sample digest + spike anchor but leaves the gate's pinned digest/version stale → P1 fail-closed with no in-gate signal that the cause is a legitimate upstream bump. "The registry-pinned digest" is ambiguous across three homes (existing spike registry / gate-local constant / future gate registry — DD-005 defers the gate registry). This is a THIRD compiled-in copy of `c872c710…`.
**Proposed fix**: name exactly which registry (a′) reads (recommend reusing the existing spike registry's append-only row); make `evidence_schema_version` a recognized-SET with a distinct "schema-newer-than-pinned; bump the gate pin" reason; document the lockstep-update affordance (how many pinned loci move per bump) as an AP-005 change path.
**Status**: pending

### Finding RS-211: INV-008(c) pins the churning `Components.cs`; DD-001 extraction understates the surgery and creates a drift-prone second copy
**Source**: self-assessment, assumptions, red-team, upgrade-compat · CONSENSUS (4) · **INV**: INV-008(c), DD-001, INV-018, AP-005
**Description**: (c) pins the SHA of the 1344-line `Components.cs`, actively churned by spike QA (holds `VerdictAggregator`, `EvidenceSchema`, `AdrLinter`, `AdjudicationStateMachine`, `ManagedLauncher` w/ `Process.Start`, …) — AP-005 no-change-affordance. DD-001's "extract the linter" understates: `AdrLinter.Lint(adr, IReadOnlyList<AdjudicationRecord>)` drags in `AdjudicationRecord`→`RouteState`/`IncompatibleClass`/`ThreeCellOutcome`→`ProbeStatus` + `RouteClaim` from ContractTypes.cs — a real slice, not one function. The extracted lib is a second copy of the permissive scanner that can drift from the spike; no legitimate-change affordance is stated for the extracted-lib digest.
**Proposed fix**: state the digest-update affordance (edit lib + digest constant in one commit with a test they agree); decide whether the extracted lib is the single source (spike references it too) or a tracked copy with a reconciliation check; enumerate the exact type closure to extract.
**Status**: pending

### Finding RS-242: Repo-root global.json blast radius + repo-global-artifact ownership/removal undocumented
**Source**: upgrade-compat (MED), ux (UX-008), self-assessment · CONSENSUS (3) · **INV**: INV-016, INV-001
**Description**: Adding a repo-root global.json changes SDK-muxer resolution for the WHOLE repo; a contributor whose only SDK is a newer feature band gets a hard muxer failure on every `dotnet` command BEFORE INV-016's friendly build-time assertion can emit. The carrier also introduces repo-global `.gitattributes` (load-bearing for INV-001's byte-count) and a repo CI workflow, all "owned" by `gate/`, with no ownership/removal story (offboard the carrier → the repo-root pin still governs the repo; a removed `.gitattributes` silently shifts INV-001's parse).
**Proposed fix**: require a prominent install note (README + AGENT_CONTEXT: "repo now requires SDK 10.0.302"); add an ownership/removal note per repo-global artifact; add a test that INV-001 fails CLOSED with a named reason if the `.gitattributes` LF pin is absent.
**Status**: pending

### Finding RS-250: `.slnx` on 10.0.302 is unverified; the fallback is not propagated across ~6 hardcoded sites
**Source**: testability, upgrade-compat, ux (UX-010), assumptions · CONSENSUS (4) · **INV**: INV-014, EA-008, OQ-A#2
**Description**: The working spike uses a classic `.sln`; `.slnx` restore/test on the pinned SDK is unproven (OQ-A#2). `gate/Corrected.Gate.slnx` is hardcoded in ≥6 sites (INV-006 Entry, INV-014 command, INV-017 CI, ARCHITECTURE test_via, README row, AGENT_CONTEXT row) + the AP-020 verbatim test. If the pre-flight forces `.sln`, all must flip in lockstep or the documented command file-not-found's (AP-020 form-mismatch, PMB-001 class).
**Proposed fix**: resolve OQ-A#2 (run the pre-flight) BEFORE freezing the strings; make the aggregator filename a single referenced constant; fold the resolution into the DD-003 atomic changeset's consistency gate.
**Status**: pending

### Finding RS-251: INV-014 vs ARCHITECTURE already disagree on the exact argv
**Source**: assumptions · **INV**: INV-014, AP-020
**Description**: Spec INV-014 documents `dotnet test gate/Corrected.Gate.slnx --logger "trx;LogFileName=gate.trx"`; ARCHITECTURE `readiness-build-gate` test_via documents `dotnet test gate/Corrected.Gate.slnx --logger trx`. The two authoritative docs diverge on the exact verbatim command AP-020 is meant to pin, before it even lands.
**Proposed fix**: make one the single source (a referenced constant); reconcile ARCHITECTURE test_via to the exact INV-014 argv; the AP-020 test parses that one string.
**Status**: pending

### Finding RS-252: The out-of-suite TRX guard's own fail-closed branch is never exercised; no expected-fixture-name meta-test
**Source**: testability · **INV**: INV-014, AP-002
**Description**: The guard lives outside the suite, so a from-clean `dotnet test` exercises the suite but never the guard's failure behavior — nothing feeds it a synthetic zero-discovery / below-floor TRX; the guard is itself AP-002-exposed. The guard asserts per-fixture `Passed` by name but no meta-test cross-checks the expected-fixture-name list against the enumerated INV-005 rows / INV-008 cases (dropping a fixture from both silently greens).
**Proposed fix**: add guard unit tests with committed synthetic TRX fixtures {zero-discovery→non-zero, below-floor→non-zero, happy→zero} + a run against this run's real gate.trx; add a meta-test reconciling the expected-fixture-name constant with the enumerated row/case sets.
**Status**: pending

### Finding RS-254: The documented command emits gate.trx/TestResults into the tree with no `.gitignore` → reopens the committed-`out/` hazard
**Source**: ux (UX-006) · **INV**: INV-014, AP-021
**Description**: `--logger trx` is baked into the documented command, so every local run writes `gate.trx`/`TestResults/` into the working tree, but Files-touched specifies no `.gitignore`. A contributor commits `TestResults/`, recreating the committed-prior-run-state hazard the whole `rm -rf out` apparatus exists to prevent.
**Proposed fix**: add a `.gitignore` for the gate's TRX/TestResults/local restore output to Files-touched; assert from-clean sees no committed gate run artifacts.
**Status**: pending

### Finding RS-260: INV-004's "kernel references no System.IO" is the wrong mechanism on .NET 10
**Source**: testability · **INV**: INV-004
**Description**: An assembly-reference scan for "System.IO" does not detect `System.IO.File`/`Directory`/`Path`/`Stream`, which live in `System.Private.CoreLib`/`System.Runtime` — the test passes even for a kernel calling `File.ReadAllText`.
**Proposed fix**: assert via a Roslyn symbol-usage scan over the kernel compilation (fail on any `System.IO.*`/`System.Diagnostics.Process` symbol use); keep the "touches no fixture file" behavioral check as a complement.
**Status**: pending

### Finding RS-261: INV-001's "inline decoy → hard fail-closed" contradicts the real parent and can dead-red it; the discriminator is unspecified
**Source**: red-team (RT-M4) · **INV**: INV-001, TB-005
**Description**: The parent legitimately contains inline `implementation_readiness:` prose mentions (phase-0-1-worker.md:196, and `.status` at 1069/1443). If the detector fails closed on any inline occurrence it dead-reds the parent; if it only counts column-0-in-fence occurrences, the "indented-decoy → hard fail-closed" fixture tests a rule the detector doesn't apply. The discriminator has both FP (nested example inside a fence) and FN (decoy outside a fence) modes.
**Proposed fix**: specify the exact discriminator (single `implementation_readiness:` at column 0 inside the one ```yaml fenced block; all other occurrences ignored as prose) and align the fixtures to it; verify the current parent parses to exactly one block under that rule.
**Status**: pending

### Finding RS-262: INV-002 fail-closed reject vs INV-011 "indeterminate status keeps the ban active" — unreconciled ordering seam
**Source**: red-team (RT-M5) · **INV**: INV-002, INV-011
**Description**: INV-002 rejects/throws on unparseable input, but INV-011 needs the parse-failure to arrive as a VALUE (`indeterminate`) so the deny-by-default scan still runs. If a parse failure aborts the gate before INV-011 executes, the "deny-by-default on unparseable" branch is unreachable/untestable.
**Proposed fix**: specify that an unparseable readiness block yields an `indeterminate` status value that keeps the ban active (INV-011 runs), rather than aborting the gate; add a fixture for unparseable → ban-active.
**Status**: pending

### Finding RS-263: INV-007 has no Enforcement field — completeness is prose-only/unenforced
**Source**: design-contract · **INV**: INV-007, PAT-004, AP-002
**Description**: INV-007 ("the corpus exercises EVERY kernel branch") has no `Enforcement:` line; existence of the INV-005/006 fixtures does not prove the corpus is complete.
**Proposed fix**: add an Enforcement field naming a corpus-coverage meta-test that enumerates every INV-005 table-row id + INV-006 per-probe case and asserts each is present.
**Status**: pending

### Finding RS-270: INV-013's "serialized against INV-006 to dodge xUnit parallelism" contradicts its injectable-repo-root isolation
**Source**: testability, red-team (RT-M6) · CONSENSUS (2) · **INV**: INV-013, AP-019
**Description**: If the degraded-env probe reads its root solely from an injected parameter, there is no shared mutable substrate with INV-006 → serialization is unnecessary; if serialization is genuinely required, the probe reads process-global state (cwd/env/static) contradicting "injectable parameter." Also xUnit serial collections don't serialize across assemblies if the runner parallelizes assemblies.
**Proposed fix**: drop the serialization and assert the probe holds no process-global state (touches neither `Directory.GetCurrentDirectory()` nor ambient env) → parallel-safe; OR name the concrete shared resource the `[Collection]` guards and justify it.
**Status**: pending

### Finding RS-271: INV-013 injectable repo-root is a substrate-swap seam; the degraded-env test can pass for the wrong reason
**Source**: red-team (RT-M6), assumptions (out/-decoy interaction) · CONSENSUS (2) · **INV**: INV-013, INV-008(c), AP-003/AP-010
**Description**: If the injectable repo-root is reachable in production (not structurally test-gated), a caller/misconfig can point the probe at a leaked `out/**` copy or forged tree (AP-003). The temp-copy test must faithfully copy schema/registry/route-a.json/linter; if it omits any, the probe fails closed for a DIFFERENT reason than "evidence removed" (AP-010 — passes green asserting nothing). Separately, INV-013's `rm -rf out` deletes the committed `out/**` decoys, so INV-008(c)'s "out/** copy not consulted" fixture must synthesize its own decoy tree, not depend on committed copies.
**Proposed fix**: structurally gate the injectable parameter to tests (production binds the pinned constant); assert the degraded-env test's fail reason is exactly `evidence-absent` (not schema-missing); have the (c) decoy fixture build its own decoy tree.
**Status**: pending

### Finding RS-280: INV-018 isolates the build, not the data — live cross-tree data coupling to the spike remains
**Source**: assumptions, red-team (RT-M7) · CONSENSUS (2) · **INV**: INV-018, DD-003, INV-008
**Description**: The gate retains a hard FILE dependency on the spike tree (reads route-a.json + canonical sample; pins Components.cs digest) — relocating/pruning the spike tree (a plausible post-Phase-0.1 cleanup) silently fails the gate closed despite INV-018's "insulated" claim. And the (a‴) ADR edit is consumed live by the spike's committed suite (`Inv013AdjudicationTests.cs` reads the real ADR); there is no carrier test asserting the ADR edit doesn't break the spike suite.
**Proposed fix**: state the insulation is build-only; enumerate the gate's file dependencies on the spike tree as EA/pins; add a carrier test that the ADR `status:` edit keeps the spike suite green (or note the coupling explicitly).
**Status**: pending

### Finding RS-291: Degraded-env hard-fail is indistinguishable from a real regression at the human surface
**Source**: ux (UX-002) · **INV**: INV-012, INV-006, INV-013
**Description**: INV-006 carries a four-way reason taxonomy internally, but INV-012 renders only two valences + a distinct reason for `validator-deferred`. It specifies no distinct rendering for a P1 `evidence-absent`/`evidence-malformed` degraded hard-fail and no recovery action → "safe but stuck" reads identically to a genuine `evidence-refutes` regression.
**Proposed fix**: require INV-012 to render each INV-006 taxonomy category distinctly; the `evidence-absent`/`-malformed` rendering carries an explicit "degraded environment — not a code regression; restore evidence/network and re-run" pointer; assert all four renderings separately.
**Status**: pending

### Finding RS-292: Named doc home is a markdown table, incompatible with the AP-020 fenced-command parser; two homes + one test → drift
**Source**: ux (UX-003, UX-004) · **INV**: INV-014, AP-020
**Description**: Both named doc homes (README "What exists today" + AGENT_CONTEXT Quick Reference) are markdown TABLES; a command in a table cell is inline code — tables cannot contain a triple-backtick fenced block, but the AP-020 detector parses FENCED commands. The spike precedent gets this right (a separate fenced "Running the compatibility spike" section). Also two homes with one verbatim test → the untested one silently drifts (PMB-001).
**Proposed fix**: host the command in a dedicated fenced "## Running the readiness gate" section mirroring the spike; keep the table row as a state pointer; make one home the single source (the other references it) or verbatim-test every home byte-for-byte.
**Status**: pending

### Finding RS-295: First-run env failures (no network per EA-005, wrong SDK) bypass all INV-012 messaging
**Source**: ux (UX-007), assumptions (EA-005) · CONSENSUS (2) · **INV**: EA-005, INV-012, INV-017
**Description**: On a fresh clone without network/DNS (EA-005 — ADR-0001 rejected vendoring; nuget.org is a permanent dependency) or without SDK 10.0.302, the from-clean gate fails closed with a raw NuGet locked-restore error before any gate code runs — none of INV-012's messaging applies. A new contributor can't tell "expected without network" from "gate broken." This also means INV-017's "wired not deferred" from-clean job cannot run in an air-gapped/hermetic CI.
**Proposed fix**: state first-run prerequisites (SDK 10.0.302 + network, EA-005) at the doc home; where feasible a preflight mapping a restore failure to "environment prerequisite unmet (EA-005) — not a gate verdict"; note INV-017 assumes a network-connected CI.
**Status**: pending

---

## LOW / ADVISORY

### Finding RS-232: INV-011 closure scan executes source generators/analyzers from the scanned surface (build-time code exec)
**Source**: red-team (RT-L1) · **INV**: INV-011, TB-004 · Vacuous today (src/ empty) but a code-exec boundary never registered as a TB.
**Proposed fix**: note the risk; prefer a syntax-only Roslyn parse (no generator/analyzer execution) for the ban predicate, or register the boundary.
**Status**: pending

### Finding RS-233: INV-011 is inert while src/ is empty (self-acknowledged AP-002); cost/benefit of the real-closure escalation is inverted today
**Source**: self-assessment, testability · Partly acknowledged in-spec (visible "src/ empty" state).
**Proposed fix**: consider whether a syntax/path scan meets INV-036 more cheaply while src/ is empty, deferring the full closure machinery until the first src/ package lands (with the OQ tracking it).
**Status**: pending

### Finding RS-264: INV-001 repo-root sentinel unnamed; cap-before-normalize CRLF interaction
**Source**: testability (LOW), red-team (RT-L2) · CONSENSUS (2) · **INV**: INV-001, EA-007
**Proposed fix**: name the sentinel file; apply `MaxFileBytes` after normalization (or account for CRLF) so a legitimately-CRLF-on-disk file isn't dead-red before normalization.
**Status**: pending

### Finding RS-300: Metadata `Status: reviewed` overstated what was reviewed (v3 unreviewed until this round)
**Source**: self-assessment · **Category**: process/AP-004-adjacent · Resolved BY this round.
**Proposed fix**: update metadata to record the round-2 review; keep the Review line honest about which version each round covered.
**Status**: pending

### Finding RS-272: parent INV-036 policy-interface disjunct dropped by INV-011 with no OQ tracking its return
**Source**: design-contract (LOW-MED) · folded into RS-223(f); standalone tracking for when the first policy interface lands.
**Status**: pending

### Finding RS-273: route-a.json's own `notes[]` contains "DafnyPipeline is NOT loaded" — hazard list should name it
**Source**: design-contract (LOW) · **INV**: INV-008(b) · No functional defect (structured `assemblies[].simple_name` read avoids it) but the enumerated false-fail hazard list omits the machine source's own `notes[]`.
**Proposed fix**: add route-a.json `notes[]` to the "do not substring-scan" hazard list.
**Status**: pending

---

## External model findings — codex (GPT-5.6-sol, xhigh) — ROUND 2

external-review status: ran (invoked directly; producer path skipped — nvm-launcher bin rejected, joshft/correctless#199)
  egress:  Sent spec + repo context to codex (OpenAI) — public OSS project, user-consented
  cost:    unavailable (direct invocation; ~xhigh reasoning)
  disable: set require_external_review:false in workflow-config.json to turn cross-model review off

> Verdict (codex): **"v3 needs another revision before TDD."** 7 BLOCKING, 5 IMPORTANT, 1 MINOR. Heavy corroboration of the Claude synthesis + 4 net-new findings (EXT2-05, EXT2-08, EXT2-09, EXT2-11) verified against the tree.

### Finding EXT2-01: [BLOCKING] P1 does not resolve true in the current checkout
**Source**: codex (external) · Dedup-merge: RS-203 / RS-220. `adr_lint` has no machine `status:` nor `supersedes`/`superseded_by`; prose `Status: accepted` is insufficient for the hardened path; no-link case undefined. Fix: define the ADR-link schema + terminal rule (nullable `supersedes`/`superseded_by`; `accepted` + both null ⇒ terminal); change "true today" → "becomes true in the DD-003 migration"; add all three fields to ADR-0001 atomically.
**Status**: pending

### Finding EXT2-02: [BLOCKING] RS-002 land-first ordering and the final current-state test cannot both be green
**Source**: codex (external) · Dedup-merge: RS-221. Adds: parent RS-002's "land the carrier FIRST" is STRONGER than DD-003's "proven green before the flip"; a preceding commit cannot contain the final hard-coded P1-true integration test. Fix: two explicit green milestones — Stage A lands the carrier asserting current-state P1=false/declared=false; Stage B atomically adds ADR status/link, flips P1/evidence, updates the parent, flips the current-state assertion to true — OR explicitly relax parent RS-002 to permit one atomic PR after a pre-flip unit-only proof.
**Status**: pending

### Finding EXT2-03: [BLOCKING] DD-003 omits multiple normative parent migration sites
**Source**: codex (external) · Dedup-merge: RS-223. Adds NEW missed sites beyond the Claude list: **parent INV-034 still says Route A is "pending DF-002" (lines 1012-1014)**; parent OQ-002 still says the built carrier is open; INV-036/PRH-008 retain the old path/policy-interface scan (lines 1069-1096, 1441-1446) vs INV-011's shipped-closure predicate. Fix: add a **normative migration manifest** listing every exact parent anchor + its replacement; require set-equality so an omitted/stale site fails.
**Status**: pending

### Finding EXT2-04: [BLOCKING] P1's COMPATIBLE recomputation passes an incomplete report vacuously
**Source**: codex (external) · Dedup-merge: RS-201 (independent same critical). Adds: duplicate Route-A verdicts / duplicate/missing probe keys are undefined; `System.Text.Json` field access alone doesn't give the duplicate-key + closed-shape guarantees the YAML path has. Fix: bind `probe_manifest_sha256` to the pinned manifest; require exact keyed-set equality for every expected `(probe,route)` (no missing/duplicate/extra); require exactly one Route-A verdict; add empty/deleted/duplicate-result + duplicate-JSON-key fixtures.
**Status**: pending

### Finding EXT2-05: [BLOCKING] The evidence-schema "re-anchor" has no INDEPENDENT trust anchor (NEW)
**Source**: codex (external) · Escalates RS-209. The `schema-file SHA == registry row == sample.evidence_schema_sha256` triple are ALL on-disk & tamperable together — an attacker modifies all three coherently and they still agree. INV-008 never requires comparing against the compiled `Components.cs` schema digest (line 78/85-86), and DD-001 extracts only the ADR linter (not the schema anchor), so the linter-source digest is unchanged during a coherent three-file attack → the "re-anchor" is circular, not an anchor. Fix: put the accepted `(schema_version, schema_sha256)` in a **compiled carrier constant** (or an independently-pinned append-only registry root) and cross-check the three on-disk values against THAT; define its append-only bump procedure.
**Status**: pending

### Finding EXT2-06: [BLOCKING] INV-016's SDK requirements are mutually unsatisfiable
**Source**: codex (external) · Dedup-merge: RS-240 (unanimous now: 4 Claude agents + codex). Adds: `latestPatch` cannot provide availability if the build rejects every `NETCoreSdkVersion` other than `10.0.302`. Fix: resolve OQ-A before TDD — minimal coherent option is root `rollForward: disable` byte-identical to spike; if `latestPatch` retained, drop byte-identity, define the permitted version relation, revise TB-004/parent/spike, replace exact build-version equality with the range policy.
**Status**: pending

### Finding EXT2-07: [BLOCKING] The real MSBuild/Roslyn scanner has an unpinned, undefined toolchain
**Source**: codex (external) · Dedup-merge: RS-230 / RS-231. Adds: the non-framework assembly allowlist is not enumerated (implementations may choose empty→allow-all). Fix: choose one reproducible scanner architecture now — pin+lock every MSBuild/Roslyn package with loaded-version assertions OR an SDK-CLI-only protocol bound to the pinned SDK identity; define the exact non-framework reference allowlist (or require it empty while BLOCKED) and test set-equality.
**Status**: pending

### Finding EXT2-08: [IMPORTANT] The component-table oracle does not prove component-table PROPAGATION (NEW)
**Source**: codex (external) · **Category**: correctness · INV-008(b)/DD-003, parent INV-003. Since v3 stopped reading ARCHITECTURE, reverting the ARCHITECTURE core-worker row would NOT affect P1 — so P1 no longer proves "the component table was propagated" (parent INV-003's actual point). The JSON predicate checks only "LanguageServer present, Pipeline absent"; removing `DafnyDriver`/`DafnyCore` (the manifest's Route-A anchors) still passes. Fix: add a machine-readable production-assembly block to ARCHITECTURE and require exact unique equality with the Dafny-family set in route-a.json (`DafnyCore`, `DafnyDriver`, `DafnyLanguageServer`, no `DafnyPipeline`).
**Status**: pending

### Finding EXT2-09: [IMPORTANT] TB-005 now denotes TWO different trust boundaries (NEW — CONFIRMED)
**Source**: codex (external) · **Category**: design-contract/naming collision · VERIFIED against tree. Parent `phase-0-1-worker.md` reserves **TB-005** for BND-003 = "Intake — untrusted **source bytes**" (arbitrary `.dfy` → policy TCB; lines 103-104, 1468-1476, 1724 "register TB-005 (intake / untrusted source bytes — BND-003)"). The v3 ARCHITECTURE amendment registered TB-005 as the readiness-block/ADR/evidence boundary and mislabeled it "(Parent BND-003.)" — factually wrong; parent BND-003 is the source-byte boundary. TB-005 is double-booked; downstream references can't tell which validation/failure-mode applies. Fix: keep TB-005 for the parent source-intake boundary; assign the readiness boundary a new ID (TB-006), updating all carrier + ARCHITECTURE references — or atomically renumber the parent boundary. (This must be decided BEFORE either ID is cemented in carrier tests.)
**Status**: pending

### Finding EXT2-10: [IMPORTANT] Linter extraction + digest pin have neither a bounded migration nor an update affordance
**Source**: codex (external) · Dedup-merge: RS-211. Adds: `Components.cs` = 1343 lines / 61,999 bytes of unrelated aggregators/schema/process-launch/evaluators — pinning the whole file makes unrelated maintenance fail P1; the shared-lib path is unnamed; INV-008's fixture still names `Components.cs`. Fix: extract a dedicated small linter file/package with a narrow DTO API; list all spike+gate project edits; pin that artifact in an append-only version/digest registry; add a sanctioned bump procedure with both spike + carrier fixtures.
**Status**: pending

### Finding EXT2-11: [IMPORTANT] The "from-clean" command removes the wrong `out` directory (NEW — CONFIRMED)
**Source**: codex (external) · **Category**: correctness/AP-021 · VERIFIED against tree. Carrier (INV-006/013/017) + ARCHITECTURE `test_via` run `rm -rf out` from repo root, but there is **no top-level tracked `out/`** (`git ls-files | grep -c /out/` = 0) — the intended target is `spikes/dafny-compat/out/`, so the cleanup removes nothing relevant and the CI job advertised as the AP-021 detector is not actually out-clean. Additional correction: `spikes/dafny-compat/out/` is **gitignored** (`spikes/dafny-compat/.gitignore:5`), so it is NOT committed — the parent's RS-004 premise ("the repository currently commits `spikes/dafny-compat/out/`", phase-0-1-worker.md:~155) is FALSE; a fresh clone is already out-clean, and the real staleness hazard is a local dev tree's untracked `out/` — which `rm -rf out` (wrong path) fails to clean. Fix: use the exact path `spikes/dafny-compat/out/` in the isolated CI checkout and assert absent before the gate; correct the parent's committed-vs-gitignored premise; synchronize carrier/parent/ARCHITECTURE/workflow text.
**Status**: pending

### Finding EXT2-12: [IMPORTANT] INV-044 is simultaneously part of and excluded from this entrypoint
**Source**: codex (external) · Dedup-merge: RS-223-adjacent / design-contract DC-low-med / UX-009. ARCHITECTURE says the entrypoint suite includes the INV-044 history-registry meta-test; DD-005 says it's not built by this spec. Omitting it violates the entrypoint contract; implementing it violates scope and risks an empty-subset pass. Fix: either include a non-vacuous pre-runtime contract now, or amend ARCHITECTURE's `test_via` to mark it a deferred extension, not part of this carrier's required suite.
**Status**: pending

### Finding EXT2-13: [MINOR] v3 incorrectly labels itself reviewed
**Source**: codex (external) · Dedup-merge: RS-300. Fix: set v3 to `draft`/`pending-review` until this round is adjudicated, then record this review.
**Status**: pending

---

## Round-2 disposition (2026-07-25)

**Verdict**: unanimous across 6 Claude lenses + self-assessment + codex GPT-5.6 — v3 needed a revision before TDD. **All round-2 findings ACCEPTED and incorporated into spec v4** (`.correctless/specs/readiness-gate-carrier.md`) + ARCHITECTURE reconciliations. Workflow held at `review-spec` (NOT advanced).

**Maintainer decisions (user, 2026-07-25):**
- **SDK pin (RS-240/EXT2-06)**: *Semantic sync + `latestPatch`* — INV-016 asserts `sdk.version` equality only (byte-identical dropped); repo-root `rollForward: latestPatch`; ARCHITECTURE TB-004 amended to record the exception. OQ-A#1 RESOLVED.
- **TB-005 collision (EXT2-09)**: *readiness boundary → TB-006* — the carrier's readiness/ADR/evidence boundary renumbered to TB-006 across spec + ARCHITECTURE; TB-005 left reserved for the parent's source-byte intake (BND-003). OQ-B added.
- **Direction**: *Revise to v4 now* (applied); recommended defaults taken for the un-asked calls (`.slnx` → single `<AGGREGATOR>` constant + INV-014 pre-flight decides, OQ-A#2; DD-003 two-milestone Stage A/Stage B staging, OQ-A#4; P3 provenance stays OQ-A#3).

**Key v4 changes by showstopper:**
- **Forge-P1 (RS-201/EXT2-04)**: INV-008(a″) now re-derives the expected `(probe,route)` set from the pinned probe manifest + asserts keyed-set equality (no missing/dup/extra) + exactly one Route-A verdict BEFORE the `∀`; PRH-007 added; 7 mutation fixtures. INV-008(a) adds authoritative ADR decision-field assertions (RS-202) + prose↔machine status check (RS-208).
- **P1-not-true-today + deadlock (RS-203/220/221/EXT2-01/02)**: INV-008(a‴) defines the ADR-link schema (nullable `supersedes`/`superseded_by`) + terminal rule; ADR fields added at DD-003 Stage B; "true today" → "true post-Stage-B"; INV-005 `(null,false,false)` Stage-A cell; INV-007 partitioned (a/b); DD-003 two-milestone staging (Stage A green pre-flip, Stage B atomic flip).
- **DD-003 migration (RS-222/223/EXT2-03)**: normative migration manifest with set-equality over 13 enumerated parent/ADR sites (incl. INV-034, INV-036, INV-003 ×2, EA-016, OQ-002/004); consistency gate diffs signature/semantic prose via machine-readable current-state anchors + names the disagreeing site.
- **INV-016 (RS-240/EXT2-06)**: semantic sync + latestPatch + blast-radius/ownership notes + CPM regression test.
- **Scanner (RS-230/231/EXT2-07)**: out-of-process pinned-SDK build (no ambient MSBuildLocator); Roslyn/MSBuild pinned+locked in INV-015; injectable closure target; enumerated allowlist (empty while BLOCKED); syntax-only parse.
- **Schema re-anchor (EXT2-05)**: compiled `(schema_version, sha256)` constant so a coherent 3-file tamper fails; recognized-set + distinct newer-than-pinned reason.
- **New-hole cluster**: ADR allowlist not glob (RS-204); component-table set-equality + ARCHITECTURE machine block (EXT2-08); `rm -rf spikes/dafny-compat/out/` (EXT2-11); INV-012 green-path stdout visibility (RS-290); INV-017 extracted runnable script executed verbatim (RS-253); INV-004 Roslyn symbol scan (RS-260); INV-013 no-serialization + test-only injectable root (RS-270/271); `.gitignore` for TRX (RS-254); fenced doc-home (RS-292); INV-018 build-only insulation + spike-suite-green test (RS-280).

All finding records above remain individually traceable (RS-2xx / EXT2-xx); statuses are "incorporated into v4" except where explicitly deferred to an OQ.

---

# Round-3 (FOCUSED) findings — v4 core: INV-008 P1 probe / DD-003 migration / INV-011 scanner

Date: 2026-07-25 · Reviewers: P1-probe red-team + DD-003 design-contract + INV-011 testability + codex GPT-5.6-sol (xhigh) · Verdict: **v4's P1/DD-003/scanner core NOT ready for TDD** (round-2 vacuous-recompute forge CONFIRMED closed; new/left blockers below). Ids R3-*.

## Confirmed sound (held up under re-review)
- The (a″) foundation is real: `manifest/probe-manifest.json` exists (22 unique (probe,route) keys); the canonical sample matches (22 keys, no dup, exactly one Route-A verdict, recomputes COMPATIBLE via 2 conjuncts); schema+manifest digests match committed anchors (schema v2). The cardinality/keyed-set guard closes the round-2 vacuous forge.
- (b) component-table propagation is correctly compiled-anchored (`{DafnyCore,DafnyDriver,DafnyLanguageServer}` == route-a.json assemblies[] == the ARCHITECTURE machine block; substring-scan of notes[]/DESIGN forbidden). Binary HintPath + generator fixtures need no 2nd package source. Empty allowlist coherent while BLOCKED.

## BLOCKING
- **R3-B1 [CRITICAL, 3-agent consensus: P1-RT + DD-003 + codex] — Stage A is RED, not green (the DTO required-member trap).** INV-002's `AdrLintBlock` under `.WithEnforceRequiredMembers()` lists `status`/`supersedes`/`superseded_by`; today's ADR has none. v4's "nullable" conflates value-null with key-absent — a `required string?` still needs the key present, so Stage-2 hard-rejects the pre-migration ADR → the promised `evidence-schema-incomplete` typed-false is unreachable (naturally maps to `evidence-malformed` or a throw) → INV-006/007b Stage-A current-state test RED → "every commit green-from-clean" fails (PMB-002 class). FIX: make the 3 acceptance/supersession fields OPTIONAL (absent-allowed, carved out of EnforceRequiredMembers) with explicit presence bits; map KEY-ABSENCE → `evidence-schema-incomplete` at a probe layer that runs BEFORE the generic malformed/reject path; add verbatim current-ADR + migrated-ADR fixtures asserting the exact reasons.
- **R3-B1b [BLOCKING, P1-RT + DD-003] — no AdrLintBlock parse-failure rule + check-ordering + taxonomy collision.** INV-002's "unparseable→indeterminate" is ONLY for the readiness block. On the ADR: if a required-member failure throws, INV-006 "never throws" is violated → gate aborts → Stage-A RED; if all such failures are caught and mapped to schema-incomplete, a MALICIOUS stripped `boundary_decision`/`selected_route` is indistinguishable from a benign missing `status` → INV-012 renders a real tamper as "pre-migration, not a regression" (masking) and the decision-field tamper fixtures (expecting evidence-refutes/malformed) collide. Also the prose↔machine status check is a flat conjunct with no precedence vs the status-absent short-circuit → same dead-red. FIX: add an explicit AdrLintBlock parse-failure→typed-false rule separating "acceptance-schema-absent" (schema-incomplete) from "structurally-malformed/tampered" (malformed); mandate schema-completeness is determined FIRST and short-circuits before prose↔machine and decision-field asserts.
- **R3-B2 [HIGH, NEW forge path, P1-RT] — the canonical sample's own content is NOT compiled-digest-pinned.** v4 compiled-pins the schema SHA and manifest SHA, but the sample is bound by PATH only (no `canonical_sample_sha256`); the (a″) recompute checks only the sample's INTERNAL consistency. A commit-access adversary (the TB-006 threat model) edits the frozen sample coherently — flip an INCOMPATIBLE run to all-pass/`success`/route-A `COMPATIBLE`, keeping the referenced schema/manifest sha fields at the pinned values — and it passes, because the gate never re-runs the spike and never checks the sample content against a trust anchor. This is the residual forge the cardinality guard does NOT close. FIX: add a compiled `canonical_sample_sha256` anchor (the sample is a frozen convergence artifact at d28ed5d — content-pinnable exactly like schema/manifest) + a tampered-sample fixture. Also add a manifest-FILE SHA==compiled-pin check + tampered-manifest-file fixture (R3-B2b, P1-RT: the (a″) list tests only `wrong probe_manifest_sha256` field, not a stripped manifest file).
- **R3-B3 [BLOCKING, NEW, codex] — duplicate JSON property names defeat the (a″) guard.** `System.Text.Json` permits duplicate properties by default (net10 `AllowDuplicateProperties`); `"status":"fail","status":"pass"` or two `deterministic` members resolve to one value. FIX: reject duplicate JSON names recursively before any field read (set `AllowDuplicateProperties=false` / a `Utf8JsonReader` pre-pass) — the JSON recompute path lacks the YAML path's duplicate-key checking. Also require a COUNT-AWARE multiset comparison for keyed-set-equality (R3-B3b, P1-RT: `ToHashSet().SetEquals` silently dedups → a duplicated (P03,A) passes; mirror `ComputeRouteVerdict`'s `if(!seen.Add(key))`).
- **R3-B4 [BLOCKING, NEW — my round-2 over-correction, codex] — the ADR allowlist "ignore out-of-allowlist" reopens the supersession bypass.** My RS-204 fix (glob→allowlist+ignore) traded a DoS for a bypass: add `ADR-0002` OUTSIDE the allowlist with `status:accepted, supersedes:ADR-0001, Route B` → v4 ignores it → ADR-0001 stays the apparent Route-A terminal → P1 stays true (the exact stale-supersession the rewrite meant to prevent). FIX: allowlist as an AUTHORITATIVE REGISTRY requiring SET-EQUALITY with every committed ADR carrying `adr_lint:` — an unregistered adr_lint block FAILS ("register this ADR"), not ignored; make the registry a COMPILED const (R3-B4b, P1-RT: "committed constant" is ambiguous compiled-const vs tamperable-file).
- **R3-B5 [BLOCKING, 2-agent: codex + DD-003 — Stage-A/B split is wrong.** DD-003 defers carrier-existence/OQ-002/RS-002/enforcement-home/path+command corrections to Stage B, but those become TRUE when the carrier lands (Stage A); between milestones the parent still says "the carrier doesn't exist / specified-but-unhomed." FIX: Stage A = carrier-existence + OQ-002 + RS-002 + enforcement-home + path/command corrections; Stage B = ADR schema fields + P1 flag/evidence + P1 current-state asserts + INV-034 + supersession closure.
- **R3-B6 [BLOCKING, 2-agent: codex + scanner] — "discover generated sources" contradicts "don't execute generators".** A real `dotnet build` executes generators/analyzers + arbitrary MSBuild targets before generated files exist; syntax-only parsing afterward doesn't undo execution, and `-getItem:Compile` doesn't even list generator output. FIX (recommended, while BLOCKED): DO NOT run a build — preflight the `.csproj` items and FAIL on the PRESENCE of any `Analyzer`/generator/custom-target/non-allowlisted binary reference, and syntax-scan the committed `Compile` sources; prove generator PRESENCE fails, don't execute to discover its body. (Alternative: sandboxed build + drop the "don't execute" claim.)
- **R3-B7 [BLOCKING, NEW, codex] — the INV-011 syntax predicate misses executable forms.** It names method/accessor/local-function/operator bodies + initializers but MISSES constructor/static-ctor/destructor bodies, conversion-operator bodies, expression-bodied properties/indexers, and top-level `GlobalStatementSyntax`. FIX: skeleton-allowlist OR exhaustive denylist — reject `BlockSyntax`/`ArrowExpressionClauseSyntax`/`EqualsValueClauseSyntax`/`GlobalStatementSyntax`/ctor/dtor/conversion bodies/anonymous-function bodies; one fixture per kind.

## IMPORTANT / HIGH
- **R3-I1 [HIGH, scanner + codex] — INV-015 toolchain incoherence.** "pin `Microsoft.Build.*` with a loaded-version assertion" is impossible for an out-of-process build (nothing from `Microsoft.Build.*` loads in-process; it's vestigial from the abandoned MSBuildLocator design). FIX: drop `Microsoft.Build.*`; pin only `Microsoft.CodeAnalysis.CSharp` (+ `.Common`) with a loaded-version assertion for the in-process syntax parse; assert the SDK's MSBuild via `dotnet msbuild -version`; specify closure extraction via `dotnet build -getItem:Compile`. (Largely moot if R3-B6's no-build-while-BLOCKED model is adopted.)
- **R3-I2 [HIGH, scanner] — fixtures under `test/fixtures/**` are outside `gate/`'s `<clear/>` scope** → their out-of-process restore merges ambient sources (the TB-004 hole) + each needs its own committed lock. FIX: relocate under `gate/Corrected.Gate.Tests/fixtures/**`; committed locks; from-clean locked-restore assertion.
- **R3-I3 [HIGH, scanner] — the `closure-uncomputable → fail-closed` trigger is undefined and collides with the vacuous "src/ empty → pass".** With src/ empty, is `dotnet build` with no project "empty" (pass) or "uncomputable" (fail)? Restore failure / skeleton compile error / missing `.csproj` all indistinguishable. FIX: operational discriminator (zero project files → pass+notice; a resolved target whose preflight/extraction returns nonzero/unparseable → fail-closed) + a concrete fail-closed stimulus fixture.
- **R3-I4 [IMPORTANT, 3-agent: DD-003 + codex — site enumeration incomplete + self-inconsistent.** Metadata + Packages-Affected list INV-043 but the DD-003 normative manifest does NOT; missed stale-after-flip sites: INV-043's "BLOCKED-all-false" render (parent:1262), INV-002's "committed file parses to BLOCKED-all-false" (parent:240), the enforcement-carrier "specified but unhomed / must exist before" prose (parent:168-179), INV-036's "OQ-002 carrier" refs (parent:1081,1086), and the parent's FALSE RS-004 "spikes/dafny-compat/out/ is committed" clean-checkout block (parent:155-166 — it's gitignored/untracked) + the wrong `rm -rf out` wording (carrier:246-247). FIX: → ~17-18 explicit numbered sites; reconcile Metadata/Packages-Affected/DD-003 to one canonical list + a meta-test they're identical.
- **R3-I5 [IMPORTANT, 2-agent: DD-003 + codex — the "machine-readable current-state anchors" don't exist and are unspecified.** No anchor grammar / ID set / before-after rule; adding them is an unenumerated parent edit; they're both the gate's oracle AND a migration target (bootstrapping). "Arbitrary unmarked prose structurally detectable" is unachievable. FIX: specify marker syntax + an anchor-ID set with `{file, id, before_sha256, after_sha256}`; add anchor-placement as explicit Stage-A sites; file-wide scans for the finite stale literals; downgrade the claim to "enumerated/anchored sites + literal scans are caught."
- **R3-I6 [IMPORTANT, codex + P1-RT] — supersession graph underdefined.** No target-ID grammar, reciprocal-edge rule, or reachability-from-ADR-0001 requirement (a one-way `superseded_by` with the target's `supersedes:null` is neither dangling nor cyclic yet broken); the promised cycle fixture is omitted. FIX: canonical ADR IDs, reciprocal links, reachability, exactly one accepted Route-A terminal; one-way/disconnected/cycle/mismatched-status fixtures.
- **R3-M1 [MODERATE, DD-003] — INV-006 "asserts BOTH Stage-A and post-migration forms, nothing mocked" contradicts the single-binding-that-swaps model** (post-migration form isn't the real committed state at Stage A). FIX: assert the STAGE-CURRENT form only.
- **R3-M2 [MEDIUM, P1-RT] — prose `Status:` extraction hazard.** ADR prose is `**Status**: accepted (DF-002 discharged … — promoted from provisional: …)` — a multi-line parenthetical; naive `prose==machine` dead-reds P1 at Stage B. FIX: extract the leading `{accepted|superseded|provisional}` token; pick the line-3 `Status:` as authoritative.
- **R3-M3 [MEDIUM-HIGH, P1-RT] — two-parser differential dead-reds Stage A.** On the pre-migration ADR the hardened path (evidence-schema-incomplete=false) and the demoted spike linter (zero findings=pass) disagree by design; if the differential fixture asserts "same overall verdict" it's RED at Stage A. FIX: scope "agree" to the shared decision fields (`boundary_decision`/`selected_route`/`routes[].verdict`), not the overall verdict; state Stage-A-green vs Stage-B-only.
- **R3-L1 [LOW-MED, P1-RT] — (a″) over-couples routes.** keyed-set-equality over the FULL per_probe_results vs the 22-entry manifest couples route-A's P1 to route-B completeness, contradicting the manifest's non-veto policy. FIX: scope the equality to the route-A+shared partition (matching `ComputeRouteVerdict`).
- **R3-L2 [LOW, P1-RT] — (a′) "recognized SET" with one element is confusing; the `newer-than-pinned` reason is wrong for older/same-version-wrong-sha (digest mismatch).** FIX: separate "older/mismatch" vs "newer" taxonomy. Define the "Dafny-family" predicate (b) explicitly (not substring). Stale changeset note: Packages-Affected lists the ARCHITECTURE production-assembly block as a NEW amendment "this pass" but it already exists (added earlier this session) — no-op.

## Overall
Every round-3 finding is concrete and BOUNDED — no fundamental redesign, a targeted v5 fix batch across the three areas. Highest-value new results: R3-B1 (Stage-A parse deadlock, 3-way), R3-B2 (sample-content not pinned — residual forge), R3-B4 (my ADR-allowlist over-correction reopened a bypass), R3-B6/B7 (scanner generator-execution + incomplete predicate). The (a″) cardinality foundation and (b) propagation anchor hold.

---

## Round-3 focused RE-CHECK (v5) — the 3 highest-risk closes

Reviewer: independent red-team re-check, grounded against the tree. **Verdict: all three closes HOLD at the mechanism level and are satisfiable-from-clean.**

- **CLOSE 1 — Stage-A green (R3-B1): CLOSED (clean).** The OPTIONAL carve-out lets YamlDotNet Stage-2 deserialize today's ADR (no missing-required-member); the schema-completeness short-circuit fires on the presence bit before the prose↔machine check; `evidence-schema-incomplete` is a typed false (not a throw, not malformed); INV-006 asserts the Stage-A form only. Follow-up applied: pinned (a)'s short-circuit as a **global gate preceding (a′)/(a″)/(a‴)**.
- **CLOSE 2 — sample forge (R3-B2): CLOSED against the data-file forge.** The whole-file `canonical_sample_sha256` covers every recompute-read field; duplicate-JSON rejection + count-aware multiset (route-A+shared partition, 12 entries verified) close the duplicate/strip defeats. Follow-up applied: corrected the **overclaim** — the compiled const relocates the trust root to the **review-gated gate source** (a commit-access adversary editing sample+const is caught by diff review), not a cryptographic barrier; added a Stage-A positive `SHA256(sample)==const` fixture.
- **CLOSE 3 — ADR-registry bypass (R3-B4): CLOSED against codex#2 + codex#9.** Set-equality + fail-on-unregistered closes the out-of-list-ADR bypass; reciprocal-edge + reachability closes the one-way-link hole; the injected-ADR DoS is stated + bounded. Follow-ups applied: fixed the **live self-contradiction** the v5 edit introduced (STRIDE §TB-006 DoS line + BND-003 still said "pinned allowlist, not an open glob" — now record **R3-B4 supersedes RS-204**); **hardened the on-disk discovery discriminator** to INV-001-D-grade (column-0-in-fence + decoy fixture) so a fenced `adr_lint:` doc example isn't counted; noted the registry's live binding is **Stage-B-first**.

**Net: v5's P1/DD-003/scanner core is coherent and satisfiable-from-clean; the re-check's bounded follow-ups are applied. Spec is TDD-ready.**

---

# Round 4 — FINAL codex gate on v5 (2026-07-25)

Reviewer: codex GPT-5.6-sol (xhigh), direct invocation (producer path skipped — nvm-launcher bin, joshft/correctless#199). Full-repo egress to OpenAI disclosed to the maintainer before the run. Prompt: `scratchpad/codex-v5-final-prompt.txt`; raw output: `scratchpad/codex-v5-final-out.md`. This is a go/no-go gate; each finding below carries the LEAD's independent verification verdict (grounded against the tree). **codex verdict: NO-GO.** Lead concurs: this SUPERSEDES the round-3 re-check's "TDD-ready" line above.

external-review status: ran (direct codex, xhigh) · egress: full repo incl. secrets/.env/git history sent to codex (OpenAI) · disable: n/a (direct invocation; producer would gate on require_external_review)

## EXT4-01 (codex#2) — INV-011 default-deny predicate is NOT exhaustive [BLOCKING — CONFIRMED]
**Source**: codex (external). **Location**: INV-011 `readiness-gate-carrier.md:491-496,523-526`.
The predicate is described as a "skeleton allowlist" but OPERATIONALIZED as a 4-node denylist (BlockSyntax / ArrowExpressionClause / EqualsValueClause / GlobalStatement + a few named bodies). Two executable forms carry NONE of those nodes and pass: (a) `[DllImport] extern` methods — body-free but execute native code; (b) positional records `record R(int X)` / primary constructors `class C(int x)` — synthesize executable ctor/Equals/members from a declaration with no explicit body. **Verified**: predicate text at :491-496 lists exactly those node types; neither extern nor record-synthesis is covered. Security invariant under-enforces. **Fix**: specify a CLOSED declaration allowlist (true skeleton grammar), reject `extern`, primary ctors, positional/record synthesis, and add one bypass fixture per kind + a meta-test over the allowed declaration-kind set. **Status**: pending.

## EXT4-02 (codex#5) — Stage-B supersession null/absence semantics are contradictory [BLOCKING — CONFIRMED]
**Source**: codex (external). **Location**: INV-002 `:138-143,167`; INV-008(a‴) `:361-362,368-369`; enforcement `:423`.
INV-002 DTO parses `superseded_by` with a presence bit distinguishing key-absent from explicit `null`, and the migrated fixture uses `superseded_by: null` EXPLICIT. But (a‴) grammar admits only "canonical ADR id string OR absent" (null not admitted) and the terminal rule requires `superseded_by` **absent**. So the migrated ADR-0001 (explicit null) is neither absent nor an id → malformed OR non-terminal → **Stage B goes RED right after `P1.satisfied:true`**. **Verified**: the three clauses conflict exactly as described. **Fix**: pick one wire form — make link keys nullable where `null` == "no edge", define terminal as "no non-null successor", pin the exact Stage-B ADR-0001 block. **Status**: pending.

## EXT4-03 (codex#6) — ADR discovery can't distinguish a fenced example from a real block [BLOCKING — CONFIRMED]
**Source**: codex (external). **Location**: INV-008(a‴) `:375-379`.
The v5 discriminator (single `adr_lint:` at column 0 inside the one ```yaml``` fence; inline/prose ignored) + the "documentation example is NOT counted" exception COLLIDE for a full fenced yaml example: a `docs/adr/ADR-0002.md` that SHOWS a column-0 `adr_lint:` inside a ```yaml``` fence satisfies BOTH "is a block" (→ set-equality counts it → unregistered → fail-closed = false-positive DoS) AND "is an example" (→ ignored → a real superseding Route-B block relabeled 'example' bypasses registry equality). The round-3 re-check's "column-0-in-fence + decoy fixture" hardening does NOT resolve this — a decoy that is a real fence is exactly the ambiguous case. **Verified**: discriminator (:375-377) vs example-exemption (:377-379) are not mechanically separable for a fenced example. **Fix**: count EVERY syntactically-matching fenced block; require examples to use a non-matching key/indentation OR authoritative start/end markers; never infer "example" from surrounding prose. **Status**: pending.

## EXT4-04 (codex#7) — ARCHITECTURE reconciliation is INCOMPLETE (spec↔ARCHITECTURE contradictions) [BLOCKING — CONFIRMED, mechanical]
**Source**: codex (external). **Location**: `ARCHITECTURE.md:256-263,300-301` vs carrier INV-015 / INV-008(a‴).
(a) TB-004 Phase-0.1 extension still lists `Microsoft.Build.*` "pinned + locked with **loaded-version assertions**" — directly contradicts INV-015 (drops all `Microsoft.Build.*`, asserts SDK MSBuild via `dotnet msbuild -version`); "loaded-version assertions" is impossible under the chosen out-of-process model. (b) TB-006 invariant still says supersession is "over a **pinned ADR allowlist** (not an open glob)" — contradicts v5's registry set-equality (unregistered block → fail, not ignored). Following ARCHITECTURE's authoritative text recreates the very bypass R3-B4 closed. **Verified directly**: both spans read verbatim as codex states; the round-3 re-check fixed only the STRIDE prose in the SPEC, not these two ARCHITECTURE spans. **Fix**: amend ARCHITECTURE before TDD — drop `Microsoft.Build.*`/loaded assertions → name SDK-MSBuild process assertion; replace the allowlist sentence with compiled-registry ↔ on-disk set-equality + unregistered-block fail. **Status**: pending.

## EXT4-05 (codex#4) — DD-003 anchor/digest protocol not unambiguously implementable [BLOCKING — CONFIRMED (2 of 3 sub-points)]
**Source**: codex (external). **Location**: DD-003 `:799-805,838-846`; Metadata/Packages `:6-10,803-804`.
(a) CONFIRMED: the anchor is a SELF-CLOSING marker `<!-- correctless:readiness-current-state id=… before_sha256=… after_sha256=… -->` yet the gate must hash "the parent prose spans they wrap" — a self-closing marker wraps nothing; no end marker / byte-range / UTF-8+LF normalization rule / manifest path+schema is defined at spec level ("specified in the manifest" forward-references a Stage-A artifact the RED tests need NOW). (b) CONFIRMED: the "meta-test asserts the three lists are identical" (:804) is incoherent — Metadata `Impacts` + Packages-Affected are deliberately REFERENCE-ONLY (:803), so there are not three lists to compare; the test must assert they reference the one manifest and hold NO local list. (c) codex MISREAD: "A5 pre-flip vs B14 flip while the marker holds both hashes" is the DESIGN, not a contradiction (one marker carries before+after; Stage A asserts `before`, Stage B asserts `after`) — not a defect. **Fix**: paired start/end markers; digest over UTF-8/LF bytes excluding markers; pinned manifest path + closed schema + exact anchor-ID set; restate the meta-test as reference-not-local-list. **Status**: pending.

## EXT4-06 (codex#3) — "generator caught pre-execution" contradicts "the build DOES execute generators" [BLOCKING — CONFIRMED as contradiction; fix-depth is a maintainer design choice]
**Source**: codex (external). **Location**: INV-011 `:478-486`.
Line 478 states the real `dotnet build` "**does execute** the closure's source generators/analyzers"; line 485 claims a shipped generator is "**independently caught pre-execution** as a non-allowlisted non-framework reference." The reference-rejection runs in the ANALYSIS phase, AFTER the build already executed the generator — so "pre-execution" is false. **Verified**: the two clauses contradict. NOTE: the maintainer explicitly chose "keep the out-of-process real-closure build" (round-3 decision), which ACCEPTS build-time generator execution — so the minimal fix is a WORDING correction ("caught by reference-rejection after a bounded, sandboxed build-time execution"), NOT necessarily codex's heavier "restore-only preflight that rejects non-allowlisted generators before the build." Maintainer decides depth. **Status**: pending.

## EXT4-07 (codex#8) — supersession graph permits an accepted NON-terminal predecessor [IMPORTANT — CONFIRMED]
**Source**: codex (external). **Location**: INV-008(a‴) `:359-371`.
Rule fails on "two accepted TERMINALS" but not on an accepted node that HAS a successor: ADR-0001 `{accepted, superseded_by: ADR-0002}` + ADR-0002 `{accepted, superseded_by absent, Route A}` → exactly one accepted terminal (ADR-0002) passes, yet ADR-0001 is left `accepted` though it was superseded. **Verified**: terminal counted by "accepted ∧ superseded_by absent", predecessor status unconstrained. **Fix**: require exactly ONE accepted node total (the terminal); every non-terminal must be `status: superseded`. (Clusters with EXT4-02/03 — the supersession-semantics rewrite.) **Status**: pending.

## EXT4-08 (codex#9) — Stage-A closes parent OQ-002 over-broadly [IMPORTANT — CONFIRMED]
**Source**: codex (external). **Location**: DD-003 A1 `:806-807`; parent OQ-002 `phase-0-1-worker.md:170,1633`.
A1 closes "parent OQ-002 'built carrier is open' → closed" WHOLESALE, but parent OQ-002 spans (i) a contract half already "DISCHARGED 2026-07-24" and (ii) "the production test project + entrypoints contract" (:170) — a production-harness concern this carrier does NOT build. Migration can pass while leaving the parent's production/provenance residual inconsistent. Also: stale parent phrases ("no entrypoint YAML exists yet" :247; carrier-still-flagged) are not in A1–A5 nor the finite literal scan. **Verified**: OQ-002 is multi-part at parent:170/1633. **Fix**: split A1 — close only the built-carrier portion here; enumerate the retained-open production/provenance parts; add exact Stage-A IDs (+ literal-scan entries) for the stale entrypoint-YAML/flagged phrases. **Status**: pending.

## EXT4-09 (codex#1) — INV-011 "any new top-level src/ package is production" mis-attributed [DOWNGRADED to MINOR/clarity — codex over-rated]
**Source**: codex (external). **Location**: INV-011 `:503-506`; ARCHITECTURE `:113`.
codex called it BLOCKING (a standalone `src/Evil/Evil.csproj` → INV-011 closure over `src/Corrected.*` resolves zero → vacuous PASS). **Lead DOWNGRADE**: the PARENT's INV-036 **path-scoped** CI check (`phase-0-1-worker.md:1091-1092,1445-1446`, deny-by-default, "any new top-level package is production") DOES catch a standalone `src/` package BY PATH; INV-011 is the COMPLEMENTARY closure scan for linked/generated/binary content that evades path classification. So the overall policy IS enforced — codex didn't credit the path-scoped sibling. Residual = a genuine CLARITY defect: INV-011 (:506) and ARCHITECTURE (:113) RESTATE the policy as if the closure scan enforces it. **Fix**: reword to attribute the "any new src/ package" catch to the parent path-scoped INV-036 detection; keep INV-011 scoped to the closure it actually computes. **Status**: pending.

## EXT4-10 (codex#10) — schema content-pin lacks an exact path constant [MINOR — CONFIRMED]
**Source**: codex (external). **Location**: INV-008(a′) `:307-320`.
`evidence_schema_sha256` references "the schema file" but names no path constant; the real producer `spikes/dafny-compat/schema/evidence-schema.json` EXISTS (verified). **Fix**: name that exact repo-relative constant alongside `canonical_sample_sha256` and `probe_manifest_sha256`. **Status**: pending.

## What HELD UP under the final gate (codex + lead-verified sound)
- Today's ADR parses to typed `evidence-schema-incomplete` (not a throw / not malformed) under the REQUIRED-vs-OPTIONAL presence-bit DTO — Stage-A green-from-clean at the DTO level holds.
- The canonical-sample pin targets the correct sample (schema v2, 22 probe results, 2 COMPATIBLE verdicts, `consistent`); variance sample stays INCOMPLETE. The three compiled pins + honest TCB framing close the DATA-ONLY coherent-rewrite forge.
- `probe-manifest.json` exists (22 composite keys, 12 Route-A/shared); count-aware equality + duplicate-JSON rejection mirror the upstream VerdictAggregator order.
- TB numbering is correct: readiness/ADR/evidence = TB-006; TB-005 remains the parent's `.dfy`-intake boundary; no mislabeling.
- The Route-A production-assembly machine block + readiness `test_via` are present with the corrected clean path.

**GATE VERDICT: NO-GO for /ctdd.** 6 BLOCKING (EXT4-01..06) + 2 IMPORTANT (07,08) confirmed; 1 downgraded (09), 1 minor (10). The defect mass is concentrated in the two v5 round-3 REWRITE areas — the supersession-semantics cluster (EXT4-02/03/07) and the scanner predicate (EXT4-01/06) — plus the ARCHITECTURE reconcile I left incomplete (EXT4-04-arch). Bounded → a v6 revision, not a redesign. The round-3 re-check's "TDD-ready" line is retracted.

---

## Round 4 dispositions — v6 applied (2026-07-25)

Maintainer decision: **Revise to v6 now (all confirmed findings)**; design sub-calls = **lightest correct fix** (EXT4-06 wording-only; EXT4-02 nullable link keys, terminal = "no non-null successor").

| Finding | Disposition | v6 change |
|---|---|---|
| EXT4-01 | **FIXED** | INV-011 predicate → CLOSED-allowlist (only body/init/synthesis-free declarations permitted; explicitly rejects `extern`/`[DllImport]`, primary ctors, positional records) + meta-test over allowed declaration-kind set + 3 new bypass fixtures |
| EXT4-02 | **FIXED** (lightest) | INV-002 + INV-008(a‴): `supersedes`/`superseded_by` nullable; **null and absent both == "no edge"**; terminal = "no non-null successor"; migrated `superseded_by:null` is well-formed & terminal |
| EXT4-03 | **FIXED** | INV-008(a‴) discovery is now PURELY STRUCTURAL: every column-0 `adr_lint:` in a `yaml` fence counts; a non-counting example MUST use a non-matching form (non-`yaml` fence / non-col-0 / `adr_lint_example:` sentinel); no prose-inferred "example" exemption; matching-form example fixture asserts IT IS counted |
| EXT4-04 | **FIXED** | ARCHITECTURE.md:258 dropped `Microsoft.Build.*` + loaded-version assertion → SDK-MSBuild `dotnet msbuild -version`; :300 "pinned ADR allowlist (not an open glob)" → compiled-registry set-equality + fail-on-unregistered. (Spec INV-015 was already correct; ARCHITECTURE was the lagging file.) |
| EXT4-05 | **FIXED** | DD-003 anchors → PAIRED start/end markers, digest over UTF-8/LF bytes between markers, pinned manifest path `gate/Corrected.Gate.Tests/manifests/readiness-migration-manifest.json` + closed JSON schema; meta-test restated as "each section holds ONLY the reference, no local list" (not "three lists identical") |
| EXT4-06 | **FIXED** (lightest) | INV-011: "independently caught pre-execution" → "caught by analysis-phase reference-rejection AFTER a bounded sandboxed build-time execution"; honors the maintainer's "keep the real build" decision |
| EXT4-07 | **FIXED** | INV-008(a‴): exactly ONE `status==accepted` node TOTAL; every non-terminal MUST be `status: superseded`; accepted-with-non-null-successor → fail-closed + fixture |
| EXT4-08 | **FIXED** | DD-003 A1 splits OQ-002 — close only the built-carrier half; contract half already discharged 2026-07-24; production-test-project/entrypoints/P3 residual stays open; stale "no entrypoint YAML" phrase added to finite literal scan |
| EXT4-09 | **NOTED / downgraded** | INV-011:506 reworded to attribute the "any new `src/` package is production" catch to the parent path-scoped INV-036 CI check; INV-011 scoped to the closure it computes. No behavior change (policy already enforced by the parent). |
| EXT4-10 | **FIXED** | INV-008(a′): `evidence_schema_sha256` names the exact constant `spikes/dafny-compat/schema/evidence-schema.json` |

Metadata bumped to **v6** (four review rounds). Post-edit verification sweep (grep for every straggler phrase — `Microsoft.Build` non-negated, "pinned ADR allowlist", "three lists identical", "pre-execution", old terminal phrasing, "This is **v5**") returned CLEAN. All EXT4-01..10 tags thread through the spec.

**v6 status: all confirmed round-4 findings applied. A focused re-check of the supersession-semantics cluster (EXT4-02/03/07) + the closed-allowlist predicate (EXT4-01) is the remaining prudent step before /ctdd.**

---

## Round 5 — FINAL codex xhigh gate on v6 (2026-07-25)

Full cross-model GO/NO-GO on v6 (GPT-5.6-sol, xhigh, read-only). **VERDICT: NO-GO** — 2 BLOCKING + 2 IMPORTANT, all CONFIRMED against the tree. Critically, the two round-4 rewrite clusters HELD UP: EXT4-01 (closed-allowlist predicate), EXT4-02/03/07 (supersession semantics), EXT4-04 (ARCHITECTURE reconcile), EXT4-09, EXT4-10 all verified sound. The new findings are in *different* areas — DD-003 consistency-gate mechanics (a NEW ambiguity created by EXT4-05's own fix), the build-execution honesty gap (side effect of EXT4-06's honest wording), an incompletely-wired EXT4-08, and a pre-existing SDK-pin contradiction earlier rounds missed.

### Finding EXT5-01 (BLOCKING → CONFIRMED): DD-003 has no mechanical global stage selector + a two-source digest ambiguity
**Source**: codex (external) · **Category**: testability/consistency-gate
**Location**: DD-003 consistency gate — readiness-gate-carrier.md:884,887,889–890 (marker `stage-before`/`stage-after` hashes AND manifest `{id,file,stage_before_sha256,stage_after_sha256}` — TWO hash sources, no stated authority/equality rule); "stage-selected" (line 887) is never mechanically defined.
**Scenario**: The paired-marker byte range (EXT4-05) is now clear, but the gate is told to compare each span's digest against `stage-before` at Stage A / `stage-after` at Stage B without any rule for (a) how it derives *which* stage it is in from committed state, applied uniformly to every anchor, or (b) which of the two hash sources (inline marker vs manifest) is authoritative when they disagree. A partial Stage-B migration (some spans migrated, some not) can therefore pass — each row matching *one* of its two pins — which defeats the exact inconsistency the gate exists to catch. RED tests cannot bind a deterministic expected value.
**Fix**: Make the manifest the SOLE digest authority (markers carry IDs only, drop inline hashes) OR assert exact marker↔manifest hash equality; define ONE structural stage selector derived from committed state (Stage A iff `P1.satisfied:false`/evidence null; Stage B iff `P1.satisfied:true`/evidence registered) applied uniformly to every anchor; add a mixed-before/after negative fixture. **Status**: pending

### Finding EXT5-02 (BLOCKING → CONFIRMED, severity light-to-medium): INV-011's "bounded/sandboxed" build-execution claim is unbacked
**Source**: codex (external) · **Category**: security/spec-honesty
**Location**: INV-011 build phase — readiness-gate-carrier.md:507–513 ("RT-L1 … is bounded and stated"; "AFTER a bounded build-time execution inside that sandboxed pinned-SDK build"); enforcement 559.
**Scenario**: EXT4-06's honest wording now admits the real build runs the closure's source generators FIRST, then analysis rejects the non-allowlisted reference. But the only named controls are the pinned SDK + `<clear/>`-isolated locked restore + "BLOCKED `src/` is empty (nothing to execute)" — the last is the very precondition the scanner exists to check, not an enforced boundary. A committed malicious generator can exfiltrate CI secrets / hit the network / loop indefinitely at build time; post-build reference rejection cannot undo the side effect. So "bounded"/"sandboxed" overclaims a containment the spec does not specify or test.
**Fix (lightest, honors "keep the real build")**: Drop "bounded/sandboxed" and record arbitrary build-time execution of committed generators as an ACCEPTED CI-runner-scoped residual (containment delegated to the CI runner; out of scope for the gate); keep reference-rejection as the actual named control; the malicious-generator fixture asserts the *reference is rejected*, not that execution is prevented. (Heavier alternative: specify+test a real boundary — scrubbed env, restricted network, read-only checkout+isolated output, timeout+process-tree kill.) **Status**: pending

### Finding EXT5-03 (IMPORTANT → CONFIRMED): EXT4-08 was claimed complete but not wired into the finite scan / staged wrong
**Source**: codex (external) · **Category**: migration-completeness
**Location**: DD-003 — readiness-gate-carrier.md:850 (A1 claims the parent "no entrypoint YAML exists yet" literal "is added to the finite literal scan below"), 865 (B4 stages that same INV-002 line under Stage B), 892–894 (the enumerated finite scan — which OMITS the "no entrypoint YAML" literal). Parent stale literals: phase-0-1-worker.md:247 ("no entrypoint YAML exists yet — see OQ-002"), :471 ("entrypoint YAML TBD (`/carchitect`)"), :1610–1614 ("Flagged for the ARCHITECTURE.md component table"). ARCHITECTURE.md:61 already HAS the entrypoint YAML (added by /carchitect 2026-07-24).
**Scenario**: (a) A1's claim that the literal was added to the finite scan is contradicted by the actual enumeration (892–894) — the round-4 disposition over-claimed. (b) Because entrypoint YAML already exists in ARCHITECTURE, correcting "no entrypoint YAML exists yet" is a current-state truth already true today → it belongs in Stage A, not Stage B (B4). (c) Two more stale entrypoint-YAML literals (parent:471) and a stale ARCHITECTURE-flag (parent:1610) are enumerated/anchored nowhere → Stage A can green while the parent stays inconsistent. The OQ-002 split direction (EXT4-08) is itself accurate (contract discharged; production-test/reference-provenance residual stays open at parent:1633) — only the wiring is incomplete.
**Fix**: Move the entrypoint-current-state correction from B4 into explicit Stage-A IDs; enumerate parent:247 + :471 + the :1610 ARCHITECTURE-flag literal in the finite scan; keep the production-test/reference-provenance residual explicitly open. **Status**: pending

### Finding EXT5-04 (IMPORTANT → CONFIRMED, understated): `latestPatch` conflicts with exact-`10.0.302` runtime assertions
**Source**: codex (external) · **Category**: internal-contradiction
**Location**: readiness-gate-carrier.md:568–569 (INV-011: `dotnet msbuild -version` bound to `10.0.302`), :659 (INV-015: `dotnet msbuild -version`/`dotnet --version` `== 10.0.302`), :690 (INV-016: `NETCoreSdkVersion == 10.0.302`) — all exact; vs :698–699 (INV-016: "membership in the pinned patch-band, not exact-only equality, consistent with `latestPatch`") and ARCHITECTURE.md:228–231 + :693–697 (roll-forward = `latestPatch` by maintainer decision, for repo-wide security-patch availability).
**Scenario**: On a clean runner with a later permitted `10.0.3xx` security patch installed, `latestPatch` (repo-root `global.json`) resolves that SDK; INV-016's band predicate accepts it, but the exact-`==10.0.302` assertions in INV-011, INV-015, and INV-016:690 FAIL → contradictory expected test results, and the exact assertions defeat the very patch-availability exception TB-004 records. The contradiction exists even *within* INV-016 (line 690 exact vs 698–699 band).
**Fix**: Replace the three exact-`10.0.302` runtime assertions with the same feature-band membership predicate INV-016:698–699 already defines (record the actual resolved SDK/MSBuild versions); keep `10.0.302` as the floor/requested version. **Status**: pending

**What held up (codex-verified sound in v6)**: EXT4-01 default-deny declaration contract (extern/PInvoke/primary-ctor/positional-record all rejected + allowed-kind meta-test); EXT4-02/03/07 supersession composition (null==absent==no-edge, single migrated ADR terminal, exactly-one-accepted-total, purely-structural discovery; today's ADR has one matching block lacking the optional keys); EXT4-04 ARCHITECTURE reconcile (no `Microsoft.Build.*`/in-process locator; registry↔disk set-equality); EXT4-09 parent-path-gate attribution; EXT4-10 real schema path + digest match; probe-manifest 22 keys/12 Route-A mirroring `ComputeRouteVerdict`; TB-005/TB-006 + Route-A machine block correctly separated.

**Round-5 disposition: RESOLVED in v7 (2026-07-25; user chose "revise to v7 now" + "EXT5-02 = accept as CI-runner residual"). All four applied (lightest-correct), EXT5-01..04 status → fixed:**
- **EXT5-01 → fixed**: DD-003 markers now carry ONLY an `id` (no inline hashes); the committed manifest is the SOLE digest authority; the gate derives ONE repo-wide stage from committed `P1.satisfied` (Stage A iff false/evidence null, Stage B iff true/evidence registered) and applies it uniformly — a mixed before/after set fails closed, exercised by a mixed-stage negative fixture. (readiness-gate-carrier.md consistency-gate block + A5/A6/B14)
- **EXT5-02 → fixed**: INV-011 dropped "bounded/sandboxed"; arbitrary build-time execution of committed generators recorded as an accepted CI-runner-scoped residual (containment delegated to the CI runner); the analysis-phase reference-rejection is the named control; the generated-source fixture asserts reference-rejection, not execution-prevention.
- **EXT5-03 → fixed**: the entrypoint current-state correction moved Stage-B (B4) → Stage-A (new site A6); the three already-stale literals ("no entrypoint YAML exists yet", "entrypoint YAML TBD", "Flagged for the ARCHITECTURE.md component table") enumerated in the finite literal scan; production-test/reference-provenance residual stays open.
- **EXT5-04 → fixed**: the exact-`10.0.302` runtime assertions in INV-011, INV-015, and INV-016 (incl. INV-016's internal 690-vs-698 contradiction) replaced with the `latestPatch` band-membership predicate; `10.0.302` kept as floor/requested.

Post-edit verification sweep (residual exact-302 forms, sandbox/bounded framing, inline marker hashes, old B14 phrasing, "three lists agree" residue, dangling B4, A1–A5 range) returned CLEAN. Spec is **v7**.
