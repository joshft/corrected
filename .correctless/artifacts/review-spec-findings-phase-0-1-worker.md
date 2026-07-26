# Review-Spec Findings: phase-0-1-worker
Date: 2026-07-24
Spec: .correctless/specs/phase-0-1-worker.md
Agents: self-assessment, red-team, assumptions, testability, design-contract, upgrade-compat, ux
Intelligence brief: dormant (present but all 19 entries below the occurrences>=3 threshold)

## external-review status: skipped (config invalid or codex entry absent — precise cause: configured `bin` resolves through an npm/nvm launcher symlink to a `node_modules/@openai/codex/bin/codex.js` target, which `_validate_invocation` rejects [RS-006: resolved target under node_modules; resolved basename `codex.js` != `codex`])
  egress:  NOT sent — the producer skipped before any codex invocation; no repo context left the machine
  cost:    unavailable (no invocation)
  disable: set require_external_review:false in workflow-config.json to turn cross-model review off
  note:    likely a Correctless tooling flaw (validator rejects the standard global npm/nvm codex install); redacted issue to be filed to joshft/correctless

---

Severity legend: CRITICAL > HIGH > MEDIUM > LOW. "Consensus" = independently surfaced by ≥2 agents (higher confidence).

**DISPOSITION (2026-07-24): all 42 findings ACCEPTED and incorporated into the spec (spec-review round 3).** Two carried explicit design decisions from the maintainer: RS-009/OQ-005 → resolve the reconverge mechanism now (forward additive DF-003 gate + append-only re-anchoring, no sample regen); RS-037 → bring `corrected explain` into scope (INV-040). Spec now carries INV-001..048, PRH-001..008, EA-001..016, OQ-001..006. Architecture-registration items (TB-005, PAT-005, TB-003 fields, carrier) are flagged for `/cupdate-arch`.

---

## Finding RS-001: Readiness block schema is described three inconsistent ways
**Source**: self-assessment, red-team, testability, design-contract, upgrade-compat (consensus x5)
**Category**: data-integrity / format-pin (AP-014)
**Severity**: CRITICAL
**Description**: The `implementation_readiness` block has three conflicting key-set descriptions in one spec: the format-pin parenthetical (line 106) `status | preconditions[].{id,satisfied,evidence,discharges}` (drops `ready_predicate`, `name`); the YAML block (lines 108-128) carries `ready_predicate` + `preconditions[].name`; INV-001 prose (lines 144-149) requires only `status` + `satisfied` + `evidence` (drops `name`, `discharges`, `ready_predicate`). The enforcing test's "pinned key set" is ambiguous — INV-001 could pass on a block missing `ready_predicate`. AP-014/AP-031 format-pinning defeated on the spec's own headline artifact.
**Proposed fix**: Declare ONE canonical key table (per key: required?/type/vocabulary); make INV-001 + line 106 reference it verbatim; delete the other two descriptions. Add a `readiness_schema_version` and have INV-001 reject an unrecognized version rather than under-read (ties to RS-015).
**Status**: accepted — incorporated into spec (round 3, 2026-07-24)

---

## Finding RS-002: INV-002 fail-closed READY-rejection branch is dead code as specified
**Source**: self-assessment, red-team, testability (consensus x3)
**Category**: security / testability (AP-002)
**Severity**: CRITICAL
**Description**: INV-001 pins "parse the block from THIS file at its committed path"; the committed block is `BLOCKED` with all three preconditions `satisfied:false/evidence:null`. So the only exercised path is the trivially-passing BLOCKED-all-false case. The invariant's teeth — fail the build on `READY` with a false/null precondition, and re-derive each discharge from the named evidence probe "never from the `satisfied` flag alone" — can never fire because no READY block and no `satisfied:true` precondition exists in the file to drive them. A suite could delete the entire reject/re-derivation logic and stay green (textbook AP-002). This is the spec's headline guarantee and its rejecting branch is untested.
**Proposed fix**: Make the gate a pure function `EvaluateReadiness(blockText) -> {Pass|Fail, offendingPrecondition}` over a SUPPLIED string, driven by a committed fixture table: (a) BLOCKED+all-false→Pass; (b) READY+one satisfied:false→Fail naming it; (c) READY+one evidence:null→Fail; (d) READY+all-true but a probe REFUTES the evidence→Fail (proves re-derivation, not flag-trust); (e) READY+all-true+probes-confirm→Pass. Keep a separate test that the committed file parses to BLOCKED-all-false. Run every probe on every invocation and cross-check the independent verdict against the declared flag. Land the positive reject test BEFORE any real precondition discharges.
**Status**: accepted — incorporated into spec (round 3, 2026-07-24)

---

## Finding RS-003: Bootstrap-from-clean assumes probes degrade to satisfied=false (never throw) on absent artifacts — unpinned
**Source**: self-assessment, red-team, assumptions, testability (consensus x4)
**Category**: security / correctness (AP-021)
**Severity**: HIGH
**Description**: INV-002's "bootstrappable from clean … BLOCKED-all-false passes" holds only if every P1/P2/P3 probe returns `satisfied=false` with a typed reason when its Phase-0.0 artifact is ABSENT — but the P2 completion manifest and P3 determinism CI lane do not exist yet. If a probe throws `FileNotFound`/parse-error on the missing manifest (or the still-`pending` ADR block), the gate errors from clean — the exact PMB-002/AP-021 deadlock it claims to avoid. Never pinned.
**Proposed fix**: Add an invariant clause + test: "every evidence probe returns satisfied=false (never throws/skips) when its target artifact is absent or `pending`," exercised from a fresh clone with no accumulated state, bound to the current run via RunContext (never enumerating prior run roots on disk).
**Status**: accepted — incorporated into spec (round 3, 2026-07-24)

---

## Finding RS-004: "Clean checkout" is not clean — committed spikes/dafny-compat/out/ ships in every git clone
**Source**: assumptions (+ red-team green-from-clean theme)
**Category**: security / test-honesty (AP-010/AP-021/PMB-002)
**Severity**: HIGH
**Description**: `.gitignore` does NOT ignore `spikes/dafny-compat/out/`, so committed prior-run `out/` (run-id dirs, suite receipts) ships in every `git clone`. A clone is therefore NOT `out`-clean, and any P2 probe that reads on-disk `out/`/receipts can pass on committed leaked state — exactly the AP-010/AP-021 class the CLAUDE.md learnings (PMB-002) record. INV-002/INV-004 "green from a single clean-checkout run" silently equates clone-clean with run-clean; the repo layout violates it.
**Proposed fix**: Define "clean checkout" as `git clone` PLUS `rm -rf out` (and stop committing `out/`); bind every own-product probe to the current RunContext, never on-disk prior-run roots.
**Status**: accepted — incorporated into spec (round 3, 2026-07-24)

---

## Finding RS-005: INV-024 honesty policy is enumerated, but DESIGN §8.3 says enumeration cannot be the certification mechanism — multiple concrete bypasses
**Source**: red-team, testability (grounded in DESIGN §8.3 lines 1197-1255)
**Category**: security / reward-hack (AP-004/AP-011)
**Severity**: CRITICAL
**Description**: DESIGN §8.3 explicitly states a lexical/enumerated scan "cannot be the certification mechanism … comments, alternate grammar forms, attributes, module options, and desugaring make lexical completeness too fragile." INV-024's enforcement is "one planted fixture per class." Concrete holes: (a) **class list is a strict subset of §8.3** — missing at least `{:verify false}`, `{:assumption}`, assign-such-that (`:|`) and failure-update (`:-`) assume-forms, bodyless `forall`/loop fact-contributors, and module-level verification-semantics options; (b) **nested-assume bypass** — `assert Post by { assume false; }` or an `assume` inside a `calc` hint uses an ALLOWED edit class (INV-015: `assert`/`calc`) to discharge a frozen postcondition; executable closure is unchanged so INV-016 passes; the per-class fixture is almost certainly a top-level `assume`, not one nested in an assert-`by`/calc hint; (c) **soundness depends on INV-020 being a true closure**, which is never proven — a construct in the allowlist that produces a proposition INV-024 doesn't scan bypasses; (d) **`dafny audit` exits 0 even with findings** (§8.3 L1205) — an exit-status-only check is fail-open, and in-process audit returning an empty finding-set on error is indistinguishable from "clean" (AP-009).
**Proposed fix**: Derive the bypass set as (INV-020 allowlist ∩ full §8.3 class list) and prove exhaustiveness over the closed fragment; recurse the honesty scan into every nested proof block (assert-`by`, calc hints, forall bodies); every bypass fixture includes a nested-in-assert-`by` and nested-in-calc variant; state explicitly whether `assert … by { }` is in the Phase 0.1 fragment; add a real-producer `dafny audit` fixture (Dafny 4.11.0) that reports a finding at exit-0 and assert the engine fails; a positive test that a known-finding input yields a NON-empty parsed finding set.
**Status**: accepted — incorporated into spec (round 3, 2026-07-24)

---

## Finding RS-006: INV-003 P1 enforcement (ADR linter) cannot decide the Route-A-compatibility / DD-007 component-table clause
**Source**: self-assessment, red-team, testability, design-contract, upgrade-compat (consensus x5)
**Category**: security / claim-exceeds-layer (AP-004/AP-016)
**Severity**: HIGH
**Description**: INV-003 requires P1 be compatible with INV-034's loaded set, the `DafnyLanguageServer` runtime dependency, and the DD-007 component-table shape — but its named enforcement is only the spike's `AdrLinter.Lint`, which validates the ADR `adr_lint` YAML block (decision vocab, per-route verdict, adjudication_record_id, evidence-path) and does NOT read any component table. So P1 can be marked satisfied (ADR flipped to Route A/COMPATIBLE) while ARCHITECTURE.md line 20 still names `DafnyPipeline` and omits `DafnyLanguageServer` — a silent spec↔arch contradiction (AP-004 + AP-016 partial migration). The string "DD-007" appears only in a linter finding message, not in any check.
**Proposed fix**: Split enforcement: (a) `AdrLinter.Lint` against the promoted block = zero findings (= DF-002); (b) a SEPARATE mechanical component-table consistency gate reading DESIGN.md/ARCHITECTURE.md tables + the committed `manifest/expected-loaded/route-a.json` loaded-identity set, asserting `DafnyLanguageServer` present and `DafnyPipeline` absent (matching INV-034). P1 fails closed until (b) is green. Resolve OQ-004 (supersession-recognition format) so a test can recognize a superseding ADR at all.
**Status**: accepted — incorporated into spec (round 3, 2026-07-24)

---

## Finding RS-007: INV-005 P3 "or a determinism lane exists in the workflow" disjunct is satisfiable by a presence-grep
**Source**: self-assessment, red-team, assumptions, testability, upgrade-compat (consensus x5)
**Category**: security / phantom-integration (AP-013/AP-011)
**Severity**: HIGH
**Description**: INV-005's enforcement offers "assert the reworked test emits a distinct skipped/failed outcome, OR that a determinism lane exists in the workflow." The second disjunct is a workflow-YAML grep that does not prove the check ran on a core-count-capable runner. The exact silent-skip defect P3 exists to fix is LIVE in the spike: `Inv010DeterminismTests.cs` L51-60 does `const int coreFloor=8; if (ProcessorCount<coreFloor){ …; return; }` — a silent early-return counted as a PASSED test on the 4-vCPU public runner. If P3 accepts "a lane exists," the silent skip survives and P3 goes green while the check never executes (AP-013).
**Proposed fix**: Delete the presence-inspection disjunct. Require the probe to observe an EXECUTION artifact proving the determinism check ran on a runner at/above the floor (real durations/output + observed ProcessorCount into this CI run's receipt), OR a distinct counted `SkippableFact` outcome `{ran-passed|ran-failed|skipped-resource-floor}`. Never a workflow-text match. Assert `outcome==ran-passed ∧ cores>=floor` for the current run.
**Status**: accepted — incorporated into spec (round 3, 2026-07-24)

---

## Finding RS-008: ~22 [integration] invariants are unhomed — no entrypoints YAML, empty src/, no test carrier (OQ-002)
**Source**: ALL (self-assessment, red-team, assumptions, testability, design-contract, upgrade-compat) (consensus x6)
**Category**: enforcement-carrier absence (AP-013/AP-004/PAT-004)
**Severity**: HIGH (approval-gating blocker; correctly disclosed)
**Description**: INV-002, 010, 015, 016, 020, 021, 022, 023, 024, 025, 027, 028, 030, 031, 032, 033, 035, 036, 037, 038, 039 name "gate precondition"/"CI test assertion" whose Integration contract Entry reads "entrypoint YAML TBD (`/carchitect`)"; `src/` is empty and no production test project exists. Per PAT-004, an enforcement mechanism that cannot run enforces nothing — ~2/3 of the spec's fail-closed properties are currently prose. The spec handles this honestly (OQ-002 elevated to approval-gating; INV-002/036 gated BLOCKED; enforcement-carrier prerequisite at lines 130-135), so it is a correctly-disclosed blocker, not a hidden defect.
**Proposed fix**: `/carchitect` must produce the entrypoints contract + production test carrier BEFORE READY. Keep BLOCKED. Pin the production-package path globs (and the carrier glob) so INV-036 is a deterministic path partition, not a content heuristic (ties to RS-002 ordering: land the carrier + the INV-002 positive reject test before any real discharge).
**Status**: accepted — incorporated into spec (round 3, 2026-07-24)

---

## Finding RS-009: OQ-005 — P2/DF-003 may be structurally undischargeable (frozen spike evidence); DF-003 is a live false-COMPATIBLE inherited via composition
**Source**: self-assessment, red-team, assumptions, upgrade-compat (consensus x4)
**Category**: freeze-with-no-reconverge / migration deadlock (AP-005/AP-021)
**Severity**: HIGH (blocking migration-path gap; currently only an open question)
**Description**: INV-004 makes DF-003 (child-exit-20 + all-pass report → COMPATIBLE/exit-0) an in-scope P2 gate that must be green from a single clean-checkout run. Covering DF-003 likely changes the spike's matrix/probe output, forcing a regen of committed evidence samples — but the spike's evidence is frozen at commit `d28ed5d` and cannot reconverge after any ancestry-breaking rewrite ([[dafny-spike-evidence-binding-fragile]]; QA-001+QA-024 already deadlocked). So P2 may be permanently BLOCKED with only a `git reset --hard` escape — AP-005 (a freeze with no legitimate reconverge affordance), which degenerates into a routine override. Separately, DF-003 is a LIVE false-COMPATIBLE in the spike, and INV-006 has Phase 0.1 COMPOSE with the spike's outputs, so Phase 0.1 inherits the fail-open until DF-003 is REMEDIATED, not merely gated.
**Proposed fix**: Resolve OQ-005 before READY-eligibility. P2's evidence must be a FORWARD dischargeable gate (not the frozen d28ed5d sample), with a sanctioned append-only reconverge affordance that respects QA-001/QA-024 (or a DF-003 gate that covers the matrix cell without regenerating ancestry-bound samples). P2 must require DF-003 REMEDIATED (proof the false-COMPATIBLE no longer occurs), not "a named gate exists."
**Status**: accepted — incorporated into spec (round 3, 2026-07-24)

---

## Finding RS-010: INV-016 / INV-037 rely on SOUND comparators left entirely to implementation
**Source**: red-team, testability (consensus x2)
**Category**: correctness / undefined-oracle (AP-004/AP-010)
**Severity**: HIGH
**Description**: INV-016 asserts `executable_semantic_closure(before)=…(after)` and INV-037 asserts a certified candidate's totality obligation is "not weaker" than the frozen program's — but "executable semantic closure" and "obligation-class weaker-than" are undefined. An undefined comparator has no computable oracle (untestable), and a trivial "return equal for proof-only edits" comparator passes for the wrong reason and is indistinguishable from a correct one unless a real inequality is exercised. INV-037's semantic half also risks being a second Dafny semantics (PROHIBIT-002) if it reconstructs "terminating obligation."
**Proposed fix**: Define `executable_semantic_closure` operationally as SHA-256 of the canonicalized compiled/erased output from the pinned Dafny compiler (equality = digest compare); add a negative fixture where an edit DOES change the executable closure and assert it FAILS. Operationalize INV-037 "not weaker" as: re-verify the post-patch program with termination checking ENABLED and assert it discharges the pre-patch termination VC (derived from the pinned resolver/verifier, per PAT-001); add a SEMANTIC negative fixture (weakens totality without the literal `decreases *` token).
**Status**: accepted — incorporated into spec (round 3, 2026-07-24)

---

## Finding RS-011: INV-028 receipt-core metadata exclusion is a blocklist; unordered collections break reproducibility
**Source**: self-assessment, red-team, assumptions, testability (consensus x4)
**Category**: data-integrity / determinism (AP-010/AP-014)
**Severity**: HIGH
**Description**: INV-028 excludes non-normative metadata by ENUMERATING fields to perturb; a later-added stable-but-metadata field is never perturbed and rides into the core, silently breaking repeat-run equality. Separately, JCS sorts object keys but NOT array element order — any core array (per-entrypoint results, `residual_obligation_fingerprints[]`) built from a HashSet/Dictionary enumeration or parallel LINQ flips order between runs → different `receipt_core_digest`. Two-run sampling cannot falsify the universal (two runs can agree by luck).
**Proposed fix**: Invert to an ALLOWLIST — `receipt_core` = exactly the normative-tagged fields of a closed schema; any field not tagged normative is excluded by construction (deny-by-default); assert a new untagged field fails the test. Pin a defined total order on every core array (sorted by a specified key); ban unordered-collection enumeration in core construction, enforced by a canonicalization test. Then repeat-run equality is confirmation, not the mechanism.
**Status**: accepted — incorporated into spec (round 3, 2026-07-24)

---

## Finding RS-012: INV-027 empty-set-means-pass is a fail-open default
**Source**: red-team
**Category**: security / fail-open (AP-001/AP-009/AP-010)
**Severity**: CRITICAL
**Description**: The predicate conjunct `unapproved_logical_assumptions = ∅` is TRUE when the set is empty — but "empty" is indistinguishable from "the assumption-finder never ran and left the set empty." If the honesty phase is skipped or errors, the set stays ∅ (pass); if `honesty_policy_executed` gets set on any other path, the whole predicate passes with no honesty check. Same for `prohibited_verification_controls = ∅`.
**Proposed fix**: `= ∅` must be gated on a POSITIVE honesty attestation ("examined N constructs, found 0") AND `honesty_policy_executed = true`; an absent/unpopulated set is INCOMPLETE (fail-closed), never a satisfied empty-negative.
**Status**: accepted — incorporated into spec (round 3, 2026-07-24)

---

## Finding RS-013: INV-030 self-declared completed-phase plan lets a truncated run certify
**Source**: red-team
**Category**: security / completeness (AP-018/AP-021)
**Severity**: HIGH
**Description**: INV-030 emits a receipt only when the completed-phase set equals the DECLARED plan. If the run declares its own plan at start, an attacker declares a minimal plan (e.g. "intake only"), completes it, and gets a receipt for a run that never verified.
**Proposed fix**: Derive the expected-phase set from the LOCK's profile/execution-mode (immutable, minted before the run), never self-declared; a declared plan weaker than the lock-mandated plan is fail-closed (the AP-021 "derive from the manifest, never from disk/self" principle).
**Status**: accepted — incorporated into spec (round 3, 2026-07-24)

---

## Finding RS-014: DD-007 active spec↔arch disagreement; INV-034 asserts Route A as settled while P1 holds it pending
**Source**: design-contract, upgrade-compat (consensus x2)
**Category**: drift / internal-tension
**Severity**: HIGH
**Description**: INV-034/018/035 assert Route A (`DafnyDriver`/`CliCompilation` + `DafnyLanguageServer`, no `DafnyPipeline`) as the production baseline, but ARCHITECTURE.md line 20 still names `DafnyPipeline`/omits `DafnyLanguageServer`, and ADR-0001's `adr_lint` block is still `pending`. Internal tension: INV-034 states Route A as settled fact ("the ADR-0001-selected route (Route A) as the single production lock") while INV-003/P1 treats that same boundary as not-yet-promoted (pending DF-002).
**Proposed fix**: Condition INV-034/018/035's Route-A assertions on P1 discharge (e.g. "the P1-promoted route lock — Route A per ADR-0001, pending DF-002"). DF-002 owns the ARCHITECTURE.md line-20 + Known-Limitations propagation (no arch edit belongs in this spec).
**Status**: accepted — incorporated into spec (round 3, 2026-07-24)

---

## Finding RS-015: No schema-version registry for the 6 new versioned artifacts; policy-version back-compat unspecified
**Source**: design-contract, upgrade-compat (consensus x2)
**Category**: versioning / back-compat (AP-015)
**Severity**: MEDIUM (HIGH for the policy-dispatch sub-point)
**Description**: The spec introduces six versioned artifacts (lock, certification-subject manifest, Corrected predicate, receipt-core, readiness block, fragment policy) but adopts none of the spike's already-proven append-only, digest-pinned, test-enforced schema-version registry (`spikes/dafny-compat/schema/schema-version-registry.json`). No invariant pins the initial `schema_version`/`policy_version` as a format-checked constant, records a migration row on a bump, or defines old-consumer behavior. Critically, there is no rule that certification DISPATCHES on the lock's `policy_version` and FAILS CLOSED on an unknown/superseded version — a v0.2 worker meeting a v0.1 lock could silently apply v0.2 semantics (mis-certification).
**Proposed fix**: Adopt the append-only digest-pinned registry for every new schema family; pin initial version constants (mirroring the spike's `SpecConstants`); version the readiness block; require certification to dispatch strictly on the lock's `policy_version`, unknown → fail-closed typed reason, known-but-different → never silently upgraded (INV-010 staleness discipline on the policy axis); require the verification path to select the matching schema version when re-verifying an existing receipt so old receipts stay verifiable.
**Status**: accepted — incorporated into spec (round 3, 2026-07-24)

---

## Finding RS-016: Unregistered architecture elements — TB-005 (intake), PAT-005 (readiness gate), TB-003 CI-lane fields, test/build-gate carrier
**Source**: design-contract
**Category**: architecture-registration (for /cupdate-arch)
**Severity**: MEDIUM
**Description**: (a) BND-003 "Intake — untrusted source bytes → policy TCB" is a first-class, heavily-enforced trust boundary (INV-007/008/019/020) with NO entry in ARCHITECTURE.md's TB-001..004 — the most significant unregistered abstraction. (b) The readiness-gate-block-checked-by-a-test is the spec's headline mechanism with no PAT home; nothing in ARCHITECTURE.md pins its schema, so a future spec could define an incompatible block; the spec defers it to "if it recurs" — should register NOW. (c) TB-003 is described abstractly with no `Exercised at`/`Test` fields (contrast fully-populated TB-004); this spec's reference-CI lane + pinned Cosign would populate them. (d) INV-036's "test/build-gate carrier" is an undocumented component.
**Proposed fix**: For `/cupdate-arch`: register TB-005 (intake/untrusted-source-bytes); register PAT-005 (readiness-gate block-checked-by-test) with the canonical block schema; add `Exercised at`/`Test` to TB-003 (reference-CI lane + pinned Cosign path); add the test/build-gate carrier to the component table.
**Status**: accepted — incorporated into spec (round 3, 2026-07-24)

---

## Finding RS-017: INV-022 omits the pinned solver seed from the locked plan
**Source**: assumptions (+ red-team determinism theme)
**Category**: determinism / config-completeness
**Severity**: HIGH
**Description**: DESIGN pins "random seeds" as part of the certification plan (§13 L2146, §8.2 L1168, §6 L530), but INV-022 enumerates only `--resource-limit`, one worker, one thread, no time limit — it OMITS the seed. All determinism claims (INV-018/026/028) silently assume a fixed solver seed that no invariant enforces or records.
**Proposed fix**: Amend INV-022 to include the pinned solver seed(s) as a fixed, enforced, receipt-recorded element of the locked plan.
**Status**: accepted — incorporated into spec (round 3, 2026-07-24)

---

## Finding RS-018: Missing environment assumptions (network, clock, filesystem, locale, RID/OS, provisioning, hermeticity)
**Source**: assumptions (+ upgrade-compat, red-team)
**Category**: environment-assumptions (grouped)
**Severity**: MEDIUM (individual items HIGH where noted)
**Description**: EA-001..004 leave many load-bearing assumptions unstated: (EA-005) OS/filesystem semantics of the pinned RID — POSIX symlink/`lstat`/regular-file/case-sensitivity that INV-008/019 depend on; RID's OS family is undeclared. (git byte-fidelity) `core.autocrlf`/`.gitattributes` can rewrite bytes before intake, silently binding a CRLF-normalized copy (INV-007/009). (EA-006) correct + monotonic host clock for Cosign cert-validity/Rekor timestamps and for watchdogs (a backward clock jump defeats hang protection). (EA-007) network reachability/DNS/TLS to nuget.org + Z3 release host + SDK feed for cold provisioning — no offline/vendored mode stated. (EA-008) upstream artifact durability — a deleted NuGet package/GitHub asset fail-closes provisioning permanently with no mirror (AP-005 supply-chain). (hermeticity) certify air-gappability — .NET/NuGet telemetry + first-run writes + in-process DafnyLanguageServer background fetches are never asserted absent (INV-028 reproducibility). (Cosign mode) keyless-vs-key-backed unspecified (very different env chain) + pinned TRUST ANCHOR (issuer/subject/key), not just tool version. (EA-010) pinned Z3 native asset published for the initial RID + OS libc/libstdc++ floor. (EA-012) immutable OS/base image for reproducibility (unbundled native deps must not drift between the two runs). (EA-013) minimal env allowlist vs `env -i` (certify needs HOME/TMPDIR/DOTNET_*). (EA-014) invariant globalization/locale (`DOTNET_SYSTEM_GLOBALIZATION_INVARIANT`/`LC_ALL=C`). (EA-015) writable, adequately-sized, non-identity-bearing scratch. (in-process solver handoff) how in-process Dafny points at the pinned Z3 without ambient PATH/`Z3_EXE` — an unstated mechanism risks an ambient Z3 answering (the INV-035 violation).
**Proposed fix**: Add the EA-xxx entries above (or a consolidated environment-identity/hermeticity invariant); add negative tests where feasible (certify succeeds with network severed; no ambient Z3 can answer; below-floor host fails closed).
**Status**: accepted — incorporated into spec (round 3, 2026-07-24)

---

## Finding RS-019: EA-001 vs EA-003 vs P3 platform tension — determinism attested on the wrong platform
**Source**: self-assessment, assumptions, red-team, upgrade-compat (consensus x4)
**Category**: portability / false-confidence
**Severity**: HIGH
**Description**: EA-001 builds for ONE initial RID; EA-003 scopes determinism to "same solver build + platform"; but INV-005 permits discharging P3 on "a capable runner whose core count meets the floor," which may be a DIFFERENT platform/RID than the shipped one. Determinism "exercised" on platform X does not attest determinism for RID Y that Corrected ships — silent false-confidence; resource units (EA-003) can also shift.
**Proposed fix**: Bind the P3/INV-018 determinism runner's RID/platform to EA-001's built RID; or explicitly scope it as a differential (non-receipt) result in the residual-trust ledger and FORBID P3 discharge on a non-matching platform.
**Status**: accepted — incorporated into spec (round 3, 2026-07-24)

---

## Finding RS-020: INV-034 static import scan misses dynamic/reflection loads
**Source**: red-team
**Category**: security / boundary-enforcement (AP-011)
**Severity**: MEDIUM
**Description**: INV-034's import-boundary scan is static (references/`using`). `Assembly.LoadFrom`/reflection of a Dafny DLL outside the adapter evades it.
**Proposed fix**: Complement the static scan with a RUNTIME loaded-assembly assertion — Dafny assemblies load only within the adapter's AssemblyLoadContext (the "production analog of the spike's loaded-identity gate" INV-034 references but leaves as a static scan).
**Status**: accepted — incorporated into spec (round 3, 2026-07-24)

---

## Finding RS-021: Provenance gate lives only in reference-CI YAML — local `corrected certify` has none; pinned Cosign version ≠ pinned trust anchor
**Source**: red-team (+ assumptions Cosign-mode)
**Category**: security / trust-boundary (TB-003, AP-004)
**Severity**: HIGH
**Description**: INV-033's enforcement is a "CI configuration assertion (pinned external verifier)" — the weakest layer: it checks the YAML SAYS to verify, not that verification gates execution, and it doesn't apply when Corrected runs outside the reference CI. An attacker runs the compromised binary directly. Separately, EA-004/INV-032 pin the Cosign TOOL VERSION but not the verification IDENTITY (keyless issuer/subject, or public key) — whoever controls the trust root forges provenance a version-pinned Cosign happily verifies. Also: core-equality is reproducibility, not authenticity (unsigned local == signed CI at the core by construction) — a consumer trusting the core digest alone treats unsigned as signed.
**Proposed fix**: Make provenance a fail-closed precondition IN THE BINARY — refuse `certify` unless it observes a signed bootstrap attestation from the independent verifier checked with a PINNED key (non-recursive). Pin the verification identity (issuer+subject+key) alongside the tool version; mismatch → fail-closed; record the trust-root residual. Pin that the receipt core digest is never a trust token on its own; document core-equality as a reproducibility (not authenticity) property.
**Status**: accepted — incorporated into spec (round 3, 2026-07-24)

---

## Finding RS-022: INV-026 vacuity — `verified` profile permits VACUITY_UNKNOWN, so a vacuous proof certifies
**Source**: red-team (grounded in DESIGN §8.4 L1257-1300)
**Category**: security / assurance-gap
**Severity**: HIGH
**Description**: DESIGN §8.4 makes the generic `verified` default "report but permit unknown vacuity"; only `verified-nonvacuous` requires a witness. Phase 0.1 ships `checked`/`verified` (INV-021). An attacker crafts a precondition contradiction subtle enough that Dafny's (incomplete) vacuity analysis returns `VACUITY_UNKNOWN`, and the vacuous proof certifies as `verified`. §8.4 says witness construction is RELIABLE for exactly this fragment (bool/int/nat/seq, bounded).
**Proposed fix**: For the Phase 0.1 fragment, require/default `verified-nonvacuous` where witness construction is reliable; at minimum, INV-026 must surface that `verified` + `VACUITY_UNKNOWN` is a residual pass-with-unproven-non-vacuity in the receipt.
**Status**: accepted — incorporated into spec (round 3, 2026-07-24)

---

## Finding RS-023: INV-013 attestation state table omits a verifier-error/crash input
**Source**: red-team
**Category**: security / fail-closed-completeness
**Severity**: MEDIUM
**Description**: INV-013 pins "`invalid` is NEVER silently downgraded to `absent`," but the state table enumerates only {absent, verified, unverified, invalid} and omits a verifier-error/crash input. A malformed attestation that makes the verifier throw could be mapped to `unverified`/`absent`, softening a would-be `invalid`.
**Proposed fix**: Add a verifier-exception row → treated as `invalid` (or a distinct `error` that blocks the "approved by Y" claim and never downgrades).
**Status**: accepted — incorporated into spec (round 3, 2026-07-24)

---

## Finding RS-024: Intake TOCTOU — validate-then-hash ordering unpinned (symlink/byte swap)
**Source**: red-team (+ assumptions)
**Category**: security / TOCTOU (AP-003)
**Severity**: MEDIUM
**Description**: INV-008 rejects symlinks/non-regular files and INV-007 hashes exact bytes, but the ordering (validate-then-snapshot vs snapshot-then-validate) is not pinned. On a live tree an attacker swaps a regular file for a symlink between the lstat check and the hash read, or swaps bytes after the grammar check. Enumerated ("one fixture per class") can't cover filesystem-semantics negatives.
**Proposed fix**: Snapshot into a content-addressed store FIRST via an O_NOFOLLOW lstat-walk that rejects non-regular/symlink at EVERY path component; all validation and hashing operate on the immutable snapshot, never re-statting the live tree. Add a property/fuzz test over path bytes.
**Status**: accepted — incorporated into spec (round 3, 2026-07-24)

---

## Finding RS-025: INV-015/018 fingerprints deterministic but not collision-resistant
**Source**: red-team
**Category**: security / protected-surface
**Severity**: MEDIUM
**Description**: The protected-surface comparator identifies changed nodes via versioned resolved-node fingerprints (INV-018), required deterministic but NOT collision-resistant. An attacker who makes a forbidden edit produce the same fingerprint as an allowed node has a same-fingerprint check treat it as unchanged.
**Proposed fix**: Require fingerprints to be collision-resistant (cryptographic over the resolved-node canonical form), with a test that two semantically-distinct nodes never share a fingerprint.
**Status**: accepted — incorporated into spec (round 3, 2026-07-24)

---

## Finding RS-026: INV-014 "no keyword-based ghost inference" enforced by a source scan is itself lexical/evadable
**Source**: red-team, testability (consensus x2)
**Category**: enforcement / claim-exceeds-layer (AP-011/AP-004)
**Severity**: MEDIUM
**Description**: INV-014's enforcement is a SOURCE SCAN of the classifier for keyword-based inference — a lexical check itself evadable (runtime-built strings, lookup tables, helper in another file).
**Proposed fix**: Make the enforcement BEHAVIORAL — a fixture where keyword inference and resolver classification DISAGREE (an executable variable named `ghost_x`, a ghost variable named `x`), asserting the classifier follows the resolver. Treat the source scan as advisory only.
**Status**: accepted — incorporated into spec (round 3, 2026-07-24)

---

## Finding RS-027: INV-021/025 declaration↔verdict cardinality only fixture-tested (AP-006)
**Source**: red-team
**Category**: data-integrity / paired-collections (AP-006)
**Severity**: MEDIUM
**Description**: INV-021 requires "every declaration verified," enforced by a single negative fixture (omit one declaration). A single fixture doesn't make the class impossible.
**Proposed fix**: Enforce cardinality structurally — a keyed map declaration→verdict (not parallel lists), fail-closed on any declaration lacking a verdict, so a dropped/silently-filtered declaration cannot occur.
**Status**: accepted — incorporated into spec (round 3, 2026-07-24)

---

## Finding RS-028: Watchdog vs resource-limit precedence race (INV-022/023/038)
**Source**: red-team
**Category**: resource-lifecycle / determinism
**Severity**: MEDIUM
**Description**: INV-038 (round 2) split resource-limit-exhausted (receipt-grade INCONCLUSIVE) from watchdog-abort (INFRASTRUCTURE_INVALID). But when a wall-clock watchdog and a resource-limit fire near-simultaneously, WHICH label wins is unspecified — a race decides whether an infra abort becomes receipt-grade verifier evidence.
**Proposed fix**: Pin deterministic precedence — if any watchdog fired during a gate, the gate is INFRASTRUCTURE_INVALID regardless of any solver result (watchdog dominates), and the watchdog abort POISONS that gate's evidence so no partial solver output is readable downstream.
**Status**: accepted — incorporated into spec (round 3, 2026-07-24)

---

## Finding RS-029: INV-035 doesn't restate clean-environment re-exec for production; documented certify needs a verbatim execution test
**Source**: red-team (+ assumptions env-allowlist)
**Category**: security / operator-surface (AP-020, ADR-0001 residuals)
**Severity**: MEDIUM
**Description**: ADR-0001 residuals name `DOTNET_ROOT`/`NUGET_PACKAGES`/`SSL_CERT_*`/`GIT_*` as "the false-COMPATIBLE vector"; the spike enforces `env -i` clean re-exec, but INV-035 inherits TB-004 without restating the clean-environment re-exec for production `corrected certify`. Also, per AP-020 (the run-spike.sh exit-127 postmortem), the DOCUMENTED `corrected certify` invocation must have a verbatim execution test — a doc grep is not execution.
**Proposed fix**: INV-035 must require the production entry point to re-exec under the same clean-environment allowlist (with any RID-override-analog input fail-closed). Add an AP-020 verbatim-invocation execution test for the documented certify command.
**Status**: accepted — incorporated into spec (round 3, 2026-07-24)

---

## Finding RS-030: INV-006 "agent tool-pinning at build time" is a prompt-level mechanism for a production-code property; import scan can't decide "no reconstructed semantics"
**Source**: design-contract, testability (consensus x2)
**Category**: enforcement / claim-exceeds-layer (AP-004/PAT-004)
**Severity**: MEDIUM
**Description**: INV-006/034/PRH-001 enforce "no reconstructed Dafny semantics" via an import-boundary + no-regex source scan, but a hand-rolled AST-walk that re-derives ghostness without importing a Dafny package and without regex passes every scan (AP-004). INV-006 additionally cites "agent tool-pinning at build time" — a Correctless-skill (PAT-018) mechanism enforcing a PRODUCTION-CODE property (category mismatch).
**Proposed fix**: Demote the import/source scan to a supplementary negative check; name INV-014's resolver-provenance fixture (every semantic classification traced to a resolver call) as the PRIMARY enforcement of "no reconstructed semantics"; add an AP-004 residual note (what the scan cannot stop). Drop/relabel "agent tool-pinning" as advisory.
**Status**: accepted — incorporated into spec (round 3, 2026-07-24)

---

## Finding RS-031: `ready_predicate` is a decorative second source of truth
**Source**: design-contract
**Category**: drift / single-source (AP-005 shape)
**Severity**: MEDIUM
**Description**: `ready_predicate: "P1 AND P2 AND P3"` (line 111) is decorative — INV-002 evaluates the conjunction directly, not via `ready_predicate`. Two sources of truth for the same rule can drift.
**Proposed fix**: Either make `ready_predicate` authoritative (INV-002 evaluates it) or drop it.
**Status**: accepted — incorporated into spec (round 3, 2026-07-24)

---

## Finding RS-032: INV-004 completion manifest has no schema/path/producer; MEDIUM-finding set is a drifting hardcoded "DF-003"
**Source**: testability (+ upgrade-compat degradation)
**Category**: testability / vague-invariant (AP-011/AP-013)
**Severity**: MEDIUM
**Description**: INV-004 resolves a "Phase-0.0 completion manifest" but there is no schema, no path, no producer, and no enumeration of §13 bullets 4-12 as gate ids; "every open MEDIUM finding (currently DF-003)" is a drifting set. "Asserts each named gate exists" is satisfiable by a string-presence check that never runs the gate.
**Proposed fix**: Pin a manifest schema `{bullet_id|finding_id → gate_id → gate_kind{test|ci-job} → green_run_id}`; the enforcement EXECUTES each named gate in the from-clean job and binds each green outcome to this run's RunContext; derive the MEDIUM-finding set from the deferred-findings ledger at runtime (not a hardcoded DF-003). Version the manifest schema and fail closed on an unrecognized shape (degradation).
**Status**: accepted — incorporated into spec (round 3, 2026-07-24)

---

## Finding RS-033: INV-029 exit/report matrix asserts a floor, not exhaustiveness — DF-003 is exactly an uncovered cell
**Source**: testability
**Category**: totality / completeness (AP-018, DF-003 class)
**Severity**: MEDIUM
**Description**: The spike analog (`RouteOutcomeAlgebra…`) asserts `exit_report_matrix >= 9` — a FLOOR, not exhaustiveness. DF-003 (child-exit-20 + all-pass → COMPATIBLE) is precisely an uncovered matrix cell a floor check misses (AP-009 sibling: truncated report read as all-pass).
**Proposed fix**: Enumerate the full exit-code × report-state cross-product; assert every cell maps to a declared typed state; any unmapped cell defaults fail-closed (INFRASTRUCTURE_INVALID), never pass. Add a negative test per "error-exit + success-looking-report" pair.
**Status**: accepted — incorporated into spec (round 3, 2026-07-24)

---

## Finding RS-034: INV-018/INV-039 fingerprint determinism tested by sampling; under-sampled on the same undersized runner P3 targets
**Source**: testability (+ assumptions)
**Category**: determinism / test-strength (AP-010/AP-013)
**Severity**: MEDIUM
**Description**: "Repeat-parse-and-diff" over one fixture twice cannot falsify a universal ("bit-identical across repeated parses" for ALL sources) and is the exact analog of the spike's INV-010 that itself silently skips under resource pressure. Map/set iteration order and hash-seed nondeterminism may not surface in N repeats. INV-039's "stable fingerprint across parses" reintroduces the same problem.
**Proposed fix**: Run over the INV-020 accept CORPUS (not one fixture); perturb the process hash-seed between the two parses to surface iteration-order nondeterminism a same-process repeat can't; prefer structural determinism (canonical serialization of resolved-node identity, ordered collections, no wall-clock leakage); emit a counted skip (never silent) if only observable under load.
**Status**: accepted — incorporated into spec (round 3, 2026-07-24)

---

## Finding RS-035: "Typed reason" under-specified across intake/lock rejections; not in INV-029 totality set
**Source**: ux
**Category**: UX / actionability (advisory)
**Severity**: MEDIUM (advisory)
**Description**: "with a typed reason" is the entire user-facing contract for INV-008/010/015/017/019/020 + BND-003, but nothing pins what a typed reason IS (closed vocabulary? human message? offending input?). INV-029's totality set does NOT include the intake-rejection reason enum, so the reason taxonomy has neither a totality nor an actionability guarantee. A first-time user can get a bare `PATH_GRAMMAR_VIOLATION` with no indication of which path or what the grammar is.
**Proposed fix**: Add an invariant that `RejectionReason` is a total, closed-vocabulary schema `{code, human_message, offending_locus}`, added to INV-029's totality set, with a negative fixture per reason asserting the message names the offending input. (`offending_locus` mandatory for span-bearing (INV-020) and path-bearing (INV-008) rejections.)
**Status**: accepted — incorporated into spec (round 3, 2026-07-24)

---

## Finding RS-036: No `remediation_class` on non-success terminals — taxonomy disambiguated internally but not actionable
**Source**: ux
**Category**: UX / recovery (advisory)
**Severity**: MEDIUM (advisory)
**Description**: Round 2 correctly separated the terminal states, but each demands a different actor/action: INFRASTRUCTURE_INVALID → operator fixes host; INCONCLUSIVE → raise `--resource-limit` and re-lock; verification_failure/INCOMPLETE → the developer's proof is wrong; SPEC_ESCALATION → the frozen spec needs an upstream change. No invariant maps the terminal state to a "who acts / what to do" class — the taxonomy is disambiguated internally but not actionable externally.
**Proposed fix**: Require each non-success terminal to carry `remediation_class ∈ {fix_infrastructure, raise_resource_limit_and_relock, fix_candidate, escalate_spec}`, plus the concrete observed-vs-locked mismatch for INFRASTRUCTURE_INVALID and the current limit + re-lock remedy for INCONCLUSIVE.
**Status**: accepted — incorporated into spec (round 3, 2026-07-24)

---

## Finding RS-037: `corrected explain` (human recovery renderer) omitted from scope
**Source**: ux
**Category**: UX / scope-decision (advisory)
**Severity**: MEDIUM (advisory; scope decision worth an explicit accept/defer)
**Description**: DESIGN.md L1983 defines `corrected explain` as THE human recovery surface ("render a receipt or residual proof state for humans"), but Packages Affected lists only `init`/`check`/`certify`. INV-038 produces a content-addressed, fingerprint/sidecar-digest failure artifact — machine-readable but with no human rendering. The recovery path a developer sees is "here is a SHA-256 and a residual_obligation_fingerprint[]." (PMB-008 class: persisted but not readable.)
**Proposed fix**: Bring `corrected explain` into scope with an INV that it renders any receipt/failure artifact to human-actionable text, OR explicitly defer it with the recovery-readability gap logged in the residual-trust ledger. Worth an explicit accept-or-defer decision, not silent exclusion.
**Status**: accepted — incorporated into spec (round 3, 2026-07-24)

---

## Finding RS-038: INV-036 BLOCKED gate has no actionable message; honest-BLOCKED state passes silently
**Source**: ux
**Category**: UX / offboarding (advisory)
**Severity**: MEDIUM (advisory)
**Description**: INV-036/PRH-008 fail a PR when production code lands under BLOCKED, but unlike INV-002 ("message naming the offending precondition"), INV-036 specifies no actionable message — a developer who scaffolds core/CLI code gets a bare "production surface non-empty while BLOCKED" with no pointer to the readiness block, the preconditions, or the OQ-002 carrier. Separately, an honest-BLOCKED state passes INV-002 SILENTLY (the gate only speaks on a false READY), so the most common developer experience ("this spec is BLOCKED — what do I do?") gets the least feedback.
**Proposed fix**: Give INV-036 an actionable-message clause mirroring INV-002 (name `status: BLOCKED`, list unsatisfied preconditions, point to the readiness block). Add a readiness-status reporter (gate-side or `corrected`-side) that renders each precondition → discharge → current-evidence-state as a checklist, so BLOCKED is self-explaining, not just self-enforcing.
**Status**: accepted — incorporated into spec (round 3, 2026-07-24)

---

## Finding RS-039: UX recovery gaps — ephemeral dev lock lifecycle, interrupted-run idempotency, undersized-host certify
**Source**: ux
**Category**: UX / recovery + lifecycle (advisory)
**Severity**: MEDIUM/LOW (advisory)
**Description**: (a) INV-010's "ephemeral generated lock" has no lifecycle, no cleanup, and no `certifiable:false` marker so it can't be mistaken for a certification lock (AP-017 UX frame). (b) After an interrupted certify (Ctrl-C, watchdog kill INV-023, crash) there is no stated resume path, no "leaves no partial receipt/sidecar" guarantee, and no idempotency contract (AP-016/AP-017). (c) An undersized-host certify should fail closed as INFRASTRUCTURE_INVALID naming the required floor and observed capacity — the production counterpart of INV-005's "visible and counted" skip — not a silent degradation. (d) NOT_APPLICABLE/PROVEN_VACUOUS dispositions lack a benign-vs-actionable marker.
**Proposed fix**: `certifiable:false` marker + cleanup/lifecycle contract on the ephemeral lock; an interrupted-run idempotency/no-partial-artifact invariant with a kill-mid-run recovery fixture; a below-floor-host INFRASTRUCTURE_INVALID invariant naming the floor; a `severity`/`benign` marker on the disposition schema (complements RS-036).
**Status**: accepted — incorporated into spec (round 3, 2026-07-24)

---

## Finding RS-040: Toolchain bump reruns conformance but doesn't force fixture / golden-vector re-capture
**Source**: upgrade-compat
**Category**: versioning / fixture-drift (AP-014/AP-015)
**Severity**: MEDIUM
**Description**: INV-035/DD-006 says a behavior-relevant toolchain bump "reruns the conformance/fingerprint suites and re-locks," but does NOT require re-capturing (i) the INV-020 fragment accept/reject corpus, (ii) the INV-012 golden digest vectors, or (iii) verbatim producer fixtures (AP-014: "re-capture on every version bump"). A Dafny bump can re-lock while stale fixtures keep passing against changed tool behavior.
**Proposed fix**: Extend INV-035/DD-006 (and INV-020's migration clause) to require re-capturing the allowlist corpus, INV-012 golden vectors, and every verbatim-producer fixture on a behavior-relevant bump, with a gate that fails if a fixture's captured tool version lags the locked one.
**Status**: accepted — incorporated into spec (round 3, 2026-07-24)

---

## Finding RS-041: Minor drift — INV-011 out-of-slice reference; INV-039 `entrypoints` lock field provenance; plural→single lock migration
**Source**: design-contract, upgrade-compat
**Category**: minor / traceability
**Severity**: LOW
**Description**: (a) INV-011 lists "methodology-record-schema-bearing artifacts" (MANAGED_PI/Phase 1, out of scope) among JCS structures — not wrong but out-of-slice phrasing. (b) INV-039 introduces a lock `entrypoints` field (round-1 addition) that should trace to DESIGN §6's digest-graph/lock schema or be flagged as a lock-schema addition. (c) Route A promotion removes `DafnyPipeline` while the spike retains it on Route B; no migration handoff states which route lock is authoritative post-promotion or that Route-B coupled artifacts must not be carried into the production adapter (AP-017).
**Proposed fix**: Trim INV-011 out-of-slice phrasing; trace/flag INV-039 `entrypoints`; state the plural-lock → single-Route-A-lock migration (production lock derives from the promoted Route-A identity set only; a gate rejects any production lock/expected-loaded set still referencing the Route-B/`DafnyPipeline` closure).
**Status**: accepted — incorporated into spec (round 3, 2026-07-24)

---

## Finding RS-042: PRH-002 universal negative enforced per-function; a new acceptance path fails open silently
**Source**: red-team, testability (consensus x2)
**Category**: security / structural-enforcement (AP-001)
**Severity**: MEDIUM
**Description**: PRH-002's detection is "error-path tests per acceptance function" — a new acceptance function added later without such a test fails open, and "per acceptance function" is an unbounded universal.
**Proposed fix**: Make it structural — acceptance verdicts a closed sum type with NO default-pass; an analyzer/type discipline (or a registry + meta-test) flags any path that returns pass without the check having run, so the negative is enforced by construction, not by enumerating today's functions.
**Status**: accepted — incorporated into spec (round 3, 2026-07-24)

---
