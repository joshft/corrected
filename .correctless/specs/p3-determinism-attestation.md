# Spec: P3 Determinism Attestation (capability-baseline; carrier INV-010 real; OQ-A#3 discharge)

## Metadata
- **Created**: 2026-07-27T21:36:00Z
- **Status**: reviewed (revising) — after six external single-reviewer rounds (15+4+5+6+5+1 blocking triaged), a **7th multi-agent `/creview-spec` pass (6 Claude adversarial lenses + gpt-5.6-sol @ xhigh, reading the actual repo code) returned REVISE with 40 findings (5 BLOCKING, ~20 HIGH)** the prose rounds missed — forged-ENTERED bypass, INV-012 fail-open default, pre-entry P3-refresh deadlock, incomplete INV-036 cross-doc amendment (READY+BLOCKED fail-open), projection-policy substitution, + a large cross-doc-migration-completeness cluster. **All BLOCKING + HIGH + clear MEDIUM incorporated 2026-07-27** (findings: `.correctless/artifacts/review-spec-findings-p3-determinism-attestation.md`; RS-001..RS-040). A **user diff re-check (2026-07-28) then caught 2 residual contradictions in the revision + 6 final edits — all fixed:** PRH-007 was self-blocking (forbade its own pre-entry invalidation + failed PR2/rotation/upgrade closed) → restructured to typed invalidation mode + a mechanism-change class (mutually exclusive with evidence) + mode-via-trusted-PR-label; the 2nd PROD-ARGV negative was impossible (cosign rejects on identity first) → two honest negatives (identity vs a fixture-accepting SHA-cross-check); true-neutral CI → an ordinary non-required advisory job w/ typed summary/artifact (fork-token `checks:write` unavailable); PR1 unsigned-artifact fix; pointer-schema + filename + signing-diagnostic + duplicate-sentence + RS-040-ledger fixes. A **second user re-check (round 8) closed the last blocking gap — no legal v1→v2 entry transition** (phase-entry permitted `lifecycle:BLOCKED→ENTERED` but not the `schema_version:1→2` bump; mechanism-change forbade lifecycle/pointer) → **phase entry is now ONE ATOMIC transition** permitting `schema_version:1→2` + add-`lifecycle:ENTERED` + set-pointer + entry evidence (both v1→v2 and v2-BLOCKED→v2-ENTERED shapes, fixture-tested); the **v2 wire format now RETAINS `ready_predicate`** (was dropped) with an exact per-version field table (v2 `lifecycle` **required**, pointer required iff ENTERED — presence bits = version-aware, NOT optional); + 3 small fixes ("retry signer"→new-attempt-1-commit, signing-diagnostic wording, pointer moved-not-appended). A **round-9 user re-check found a further BLOCKING classifier gap — the total `satisfied` classifier had no P2-activation class, so the eventual P2-landing PR (`P2.satisfied:true` + completion manifest + `status:BLOCKED→READY`) matched no class and failed closed, so P2 could never land and phase entry could never reach its required `status=READY` commit** → **FIXED: added the `Evidence: P2-activation` class** (the classification contract only; the completion manifest + P2 validator stay the P2 feature's out-of-scope deliverables) + stated the delegation + the landing/upgrade sequence; + 4 consistency edits (INV-029 names both atomic entry shapes; OQ-004 tuple retains `ready_predicate`; EA-010↔INV-005 OS-label reconciled; this readiness note walked back per the user). A **round-10 user re-check found the inverse edge-state deadlock — once `status=READY` (post-P2), recovery was unreachable**: the pre-entry-invalidation could flip `P3.satisfied:true→false` but not `status:READY→BLOCKED` (so the carrier rejected the still-`READY`+precondition-false block), and the re-mint could not move `status:BLOCKED→READY` back → **FIXED by GENERALIZING** the invalidate/re-mint modes into two typed **precondition-invalidation / precondition-reactivation** classes that move `status` in lockstep with the re-derived preconditions (`status`-follows-preconditions), subsuming the P3-initial/P3-re-mint/P2-activation rows; + a `{status:BLOCKED,READY}×{P2,P3}×{invalidate,restore}` cross-product test; + the explicit 4-step delegated P2 landing sequence (P2 **validator lands under mechanism-change first** → P2 evidence activation → entry receipt → phase entry). User reaffirmed **keeping Group G in-feature** (DD-012). A **round-11 user re-check found two bounded implementation gaps in the generalized classes — both fixed:** (a) the classes named only "evidence pointer," conflating the readiness-block `Pk.evidence` field with the external active-reference file, but the carrier hard-Fails a **non-null-but-`Unresolvable`** reference regardless of the declared `satisfied` (`ReadinessGate.cs:110`), so deleting only the file while leaving `Pk.evidence` non-null would make the invalidation PR self-fail → **both layers now move in the SAME PR** (reactivation: `Pk.evidence: null→<ref>` **and** create the file; invalidation: `Pk.evidence: <ref>→null` **and** delete the file; historical versioned evidence preserved, only the active reference retired), with a **negative cross-product cell** asserting delete-file-but-keep-field is REJECTED; (b) the classes claimed `k∈{1,2,3}` but only P2/P3 behavior was defined and the cross-product covered only `{P2,P3}` → **scoped to k∈{2,3} (P2, P3) ONLY; P1 is governed by its own already-landed Stage-B migration contract (`phase-0-1-worker.md`), never a pre-entry evidence class**, so `{P2,P3}` is the complete controlled set. Structural sweep clean (30 INV/10 PRH/5 class rows/1474 lines/`git diff --check` clean). **(The round-7..11 state was committed `95a0982`, "commit and hold".)** A **round-12 user re-check found one more cross-feature ordering blocker — fixed:** the delegated P2 landing sequence led with the P2 mechanism PR, but that PR makes `P2Probe` real by editing `gate/Corrected.Gate/Probes.cs` — the SHARED file that also holds `P3Probe`, a P3 subject-manifest verifier-surface input (INV-018) — so with P3 still declared true it STALES the committed P3 baseline (`CellFails` declared≠actual), and `mechanism-change` may not re-mint P3 → the PR is rejected. **Fixed: P3 is INVALIDATED before the staling mechanism PR** — the sequence is now 7 steps (P3-invalidation k=3 → P2 mechanism → new P3 observation → P3-reactivation k=3 → P2 evidence activation k=2, `status→READY` → entry receipt → phase entry); and the **P2 completion-manifest + active-reference paths are named EXACT INV-018 exclusions** (so step 5's P2 evidence activation cannot itself stale P3, and the exactly-one-precondition rule need not restore P3 and activate P2 in one PR). Root cause noted (`P2Probe`/`P3Probe` share one file; a future P2 feature MAY split them to drop the invalidate-first steps). Sweep clean (30 INV/10 PRH/5 class rows/1495 lines/`git diff --check` clean). **This round-12 fix is UNCOMMITTED on top of `95a0982`. Readiness reassessment is the user's to give; workflow still HELD at review-spec pending the user's explicit go.**
- **Impacts**: phase-0-1-worker (INV-005 P3 "current run" → capability-baseline + full orthogonal-outcome remap; **INV-036 amended — production-code ban scoped to pre-ENTERED**; OQ-006 informed), readiness-gate-carrier (INV-010 real; OQ-A#3 resolved; DD-002 P3 path; exact-five; **the readiness kernel gains a BLOCKED→ENTERED phase-latch, COMPLETE reserved**), dafny-compat-spike (INV-010 runner → three-artifact status model + serial lane), ARCHITECTURE (TB-007 new; exact-four → exact-five gate projects; Corrected.Provenance non-shipped shared substrate)
- **Branch**: feature/p3-determinism-attestation
- **Research**: .correctless/artifacts/research/p3-determinism-attestation-research.md
- **Recommended-intensity**: critical
- **Intensity**: critical
- **Intensity reason**: three trust boundaries (TB-004 inbound cosign toolchain, TB-006 tamperable committed attestation, TB-007 trusted-CI evidence signing) + a bounded commit-access adversary + keyless-signing cryptography. User confirmed.
- **Override**: none

## Context

The determinism guarantee INV-010 (spike `RunTwice_DeterministicProjectionsIdentical`) currently
(a) **never executes in public CI** — it early-`return`s silently whenever `Environment.ProcessorCount < 8`,
and GitHub public runners are 4-vCPU — and (b) even if it ran, the carrier P3 probe consumes it as an
**unconditional `validator-deferred` stub** (`gate/Corrected.Gate/Probes.cs:520`). The carrier's own INV-010
states a bare committed `ran-passed` JSON is insufficient — "anyone with commit access forges `ran-passed`"
(RS-RT-13). This feature makes the determinism check **actually run** in an isolated CI lane, emit **typed,
derived evidence**, mint a **provenance-bound attestation**, and gives the carrier a **real fail-closed P3
probe** that cryptographically verifies it — resolving carrier **OQ-A#3** to a **keyless-OIDC Cosign-signed
in-toto DSSE attestation** verified by a digest-pinned `cosign` CLI. P3 is redefined (with a bounded parent
amendment) as a **committed capability baseline** bound to a versioned determinism-subject manifest, kept
honest by a **live determinism CI job** on every determinism-relevant change. A passing result means **"the
declared deterministic projections agreed in this two-run observation under the recorded environment"** — not
that determinism is universally proved. The catastrophic failure this prevents is a **forged/mis-verified
attestation flipping P3 → true and unblocking production `src/` on unproven code** — under the honest
(narrowed) threat model that a reviewed, protected verifier/workflow/pin/trust-policy is not itself the thing
being subverted.

## Scope

**In scope** (one coherent mechanism, landed as **THREE PRs** — DD-003):
- **PR1 — counted runner + serial lane + measurement campaign** (P3 stays false): replace the silent
  early-return with an **orthogonal typed status model**; a **per-role/per-kind** determinism receipt; a
  dedicated **isolated serial CI lane** recording `ProcessorCount` + RID + the pinned OS + actual runner
  image; a **campaign-plan commit** that predates every included run. No signing.
- **PR2 — frozen provenance mechanism** (P3 stays false): the `gate/Corrected.Provenance` **non-shipped shared
  substrate** (in-toto Statement builder + predicate schema + cosign verify wrapper), the **two-job
  keyless-OIDC signing** design, the exact **single-version pinned cosign + per-RID digest + frozen argv from a
  real sign→offline-verify transcript spike**, the versioned append-only **trust root**, the real fail-closed
  **P3 probe** with typed reasons, the **3-layer test architecture** (positive under a **fixture identity**),
  and the **versioned determinism-subject manifest**. All mechanism artifacts **frozen** here.
- **PR3 — evidence-only activation** (P3 flips true): commit the real **production-identity** signed
  `{receipt, bundle}` from the merged-main run + the readiness flip via a **parsed-span** diff; **no**
  mechanism/schema/root/verifier/workflow changes; P3 flips only after clean re-verification.
- **Readiness phase-entry lifecycle** (Group G, built in-feature — DD-011): the carrier readiness kernel gains a
  monotonic **BLOCKED → ENTERED** latch (**COMPLETE reserved**, schema v2); at first `P1∧P2∧P3` on a main commit
  `X`, **trusted main CI signs** an immutable provenance-bound **Phase-0.1-entry receipt** for `X` and a
  constrained activation PR transitions to **ENTERED**, which **the gate verifies** (the pure kernel only
  *proposes*; it never mints/persists — INV-026). Parent **INV-036** is amended so the production-code ban is
  scoped by **`effective_lifecycle != ENTERED`**; post-entry, determinism is a **non-blocking health check** with
  a **refresh protocol** (never a re-BLOCK of existing `src/`; a transient verifier outage never re-bans it).
  Built + fixture-tested here; the live entry transition cannot fire until P2 lands.
- **Bounded cross-doc edits** (the edit set is **completely enumerated** — the review's dominant escaped-bug class
  is the *incomplete* cross-doc migration, AP-016 / RS-004/018/020/021/022/023/025): amend parent **INV-005** (full
  orthogonal-outcome remap) and **INV-036** — the latter requires rewriting **every** `status`-keyed clause (parent
  INV-036 Statement/Violated-when/Enforcement, **`ARCHITECTURE.md:111`+`:113`**, and the **kernel ban-predicate
  `ReadinessGate.cs`**), not just the child (RS-004); register **TB-007** + migrate **exact-four → exact-five** gate
  projects across **all itemized committed literals** (the `Inv015PinnedToolchainTests.cs` array + `Assert.Equal(4,…)`
  + method name + comments, the new `Corrected.Provenance/packages.lock.json`, `ARCHITECTURE:84`, carrier
  `133/882/1216`, `docs/features/readiness-gate-carrier.md:24` + its role table — RS-020); reconcile the
  **`reference-ci-provenance` entrypoint** (Corrected.Provenance dual-home + the new determinism workflow file +
  `ARCHITECTURE:94`'s stale outcome prose — RS-023); update the **ARCHITECTURE `readiness-build-gate` handler YAML
  `:83`** + `docs/features/readiness-gate-carrier.md:29` for the NEW (added-alongside) 3-arg transition evaluator
  (RS-022); migrate **`ReadinessBlock.cs`** for schema v2 (`RecognizedSchemaVersion` int→recognized-set `{1,2}`;
  **`ready_predicate` RETAINED** in v2 — never dropped, round-8; **version-aware presence bits** so `lifecycle`
  and `entry_evidence_pointer` are **absent-by-design in v1 but REQUIRED in v2** (`lifecycle` always; the pointer
  iff `ENTERED`) — presence bits make parsing version-aware, they do NOT make v2's required fields semantically
  optional; the *default-BLOCKED* reading applies only to a v1 block where `lifecycle` is absent, and the closed
  DTO must not reject a valid v2 block — cf. the v4/v5 AdrLintBlock "`required string?` still demands the key" bug
  the carrier already hit) + carrier **INV-002**'s "single recognized version" contract (RS-021); resolve carrier
  **OQ-A#3** + make carrier **INV-010** real (**re-label TB-003→TB-007 + three-artifact shape**, RS-033) + amend the
  carrier **declared-vs-actual cross-check (readiness-gate-carrier INV-005)** so a post-entry stale P3 is neutral
  not hard-fail (RS-018) + add the readiness-kernel phase-latch. All committed anchors are **re-pinned by
  symbol/exact-literal, not line number** (RS-026), and the DD-003 stale-literal scan is re-run before PR2/PR3.

**NOT in scope**:
- **Release/binary provenance** — parent **INV-031/032/033**. Corrected.Provenance is **designed** as the
  reusable schema/predicate/Statement **contract** they will reuse (INV-022), but it is **non-shipped** and
  **never referenced by a shipped `src/Corrected.*` binary** (PRH-010, preserving the parent's non-recursive
  bootstrap, INV-033).
- **Closing parent OQ-006 wholesale** — informed, not closed.
- **The P2 completion manifest / DD-002 P2 validator**, DF-003's schema-v3 doc row, DRIFT-001/002/003.

## Complexity Budget
- **Estimated LOC**: ~4200–6000 across 3 PRs (**revised up from ~3200–4800 after the round-7 additions**, RS-*: the
  three-artifact receipt model, the pinned subject classifier + role/kind→projection-policy map, the two-job signer
  isolation + offline harness + provisioning wiring + the separate advisory health job, the extracted lane script +
  sync test, the parsed activation diff with a validated merge-base + total mode classifier, the four PROD-ARGV
  reason-specific negatives + forged-ENTERED + dangling-pointer fixtures + the several cross-product totality tests
  derived from committed enums, and the **full readiness lifecycle protocol** (schema v2 + v1→v2 `ReadinessBlock.cs`
  presence-bit migration + kernel/orchestrator split + two-step sign→activate + entry-receipt provenance + the
  transition_context×entry_integrity×health state model) + the exact-five carrier migration). The exact-five
  migration alone touches **~12 propagation sites** (slnx + comment, INV-014, the INV-015 array **+ count guard +
  method name + comments**, `Corrected.Provenance/packages.lock.json`, BND-002, carrier ×3, ARCHITECTURE:84,
  `docs/features/readiness-gate-carrier.md:24`+role-table, `StatusRenderer.cs`).
- **Files touched**: ~42–58
- **New abstractions**: 7 (three-artifact determinism status model; the `Corrected.Provenance` non-shipped shared substrate; the versioned determinism-subject manifest + single classifier; the two-job signing lane; **readiness schema v2 + the pure-kernel/impure-orchestrator lifecycle protocol + the two-step entry activation**; the Phase-0.1-entry receipt + its own provenance identity contract; the post-entry health-check + append-only refresh protocol). To be registered as **PAT-006** in ARCHITECTURE via `/cupdate-arch` (DD-013).
- **Trust boundaries touched**: 3 (TB-004, TB-006, TB-007-new)
- **Risk surface delta**: high. **Note (RS-011, accepted):** Group G's entry-signing pipeline (INV-029/030) remains a second, frozen, fixture-only-until-P2 cryptographic surface (AP-002/AP-013) — kept in-feature per DD-012, with the entry-verifier PROD-ARGV discipline + a residual-ledger entry as the compensating controls.

## The evidence layering (referenced by INV-001/006/012 — resolves the causal loop AND the totality contradiction)

**Three distinct artifacts** — a "receipt for every invocation" is impossible (a receipt-write failure cannot
emit a receipt of its own failure; an abrupt process death emits nothing; PR1 signs nothing; disagreements/skips
are deliberately not signed). So:
- **RunnerInvocationOutcome** — the controller/workflow observation, **always classified if the controller
  survives**. It carries `execution_status`. A **missing** RunReceipt is classified **externally, here** as
  `infrastructure_invalid` — the fault cannot be represented inside the receipt that was never written.
- **RunReceipt** — emitted for **ordinary terminal runs** (usually **unsigned**): `execution_status`,
  `comparison_status`, the per-role/kind evidence, the recorded platform identity, `attested_commit`, and the
  **subject-manifest digest + policy version**. No attestation/verification status inside it.
- **AttestedRunReceipt** — only a **`completed ∧ equal`** RunReceipt that the signer authenticates; it is the
  in-toto **subject**. Nothing else is ever signed.

**Legal `(execution_status, comparison_status)` table** (orthogonal ≠ all nine combos valid — every other
combination is **schema-invalid**):

| execution_status | comparison_status |
|---|---|
| `completed` | `equal` |
| `completed` | `different` |
| `resource_floor_skipped` | `not_evaluated` |
| `infrastructure_invalid` | `not_evaluated` |

**Signing outcome** (a workflow fact, outside every receipt): `{not_attempted | minted | failed}` — the
`not_attempted` vs `failed` distinction is **not** inferable from bundle absence, so when it matters the workflow
emits a **separate unsigned diagnostic**. **Probe result** (computed by the consumer at gate time):
`{verified | rejected | unavailable}` with a typed reason. **`ran-passed`** is **derived by the P3 probe** from a
signed `completed ∧ equal` AttestedRunReceipt **plus** a successful current verification **plus** a non-stale
manifest **plus** `attested_commit` ancestor-of-HEAD — never a stored field in the signed subject.

## Invariants

### Group A — Counted runner + typed evidence (PR1, spike-side)

### INV-001: A three-artifact status model with a closed legal-status table replaces the silent early-return [integration]
- **Type**: must
- **Category**: functional
- **Statement**: the determinism check uses the **three-artifact model** ("The evidence layering") — a
  **RunnerInvocationOutcome** (always classified if the controller survives; classifies a **missing** RunReceipt
  externally as `infrastructure_invalid`), a **RunReceipt** (`execution_status ∈ {completed,
  resource_floor_skipped, infrastructure_invalid}` × `comparison_status ∈ {equal, different, not_evaluated}`),
  and an **AttestedRunReceipt** (only a signed `completed ∧ equal` receipt) — and the silent
  `Console.Error.WriteLine(...) + return` path (`Inv010DeterminismTests.cs:52–60`) is removed. The
  `(execution_status, comparison_status)` pair is constrained to the **closed legal-status table** (only the four
  rows in "The evidence layering"; every other combination is **schema-invalid**). Any infrastructure fault maps
  to `infrastructure_invalid`/`not_evaluated`, **never** `comparison_status=different`. Signing outcome + probe
  result live **outside** every receipt; `ran-passed` is **probe-derived**.
- **Violated when**: a "receipt for every invocation" is claimed (an unwritable/absent receipt cannot self-report);
  an infrastructure fault is recorded as `comparison_status=different`; a status pair outside the legal table
  parses; or any attestation/verification status is written into the signed subject.
- **Enforcement**: CI test assertion — a **pure total classifier** over controlled observations covers every
  legal combination and rejects every illegal one (INV-013 layer 1); an integration test proves the real runner
  feeds genuine observations; a schema test enforces the closed legal-status table + no attestation field in the subject; a missing-receipt fixture classifies as `infrastructure_invalid` externally.
- **Guards against**: AP-018 (success without completeness), AP-022 (non-total enum)
- **Test approach**: integration
- **Integration contract**:
  Entry: the serial determinism lane / its extracted runnable script, invoked verbatim
  Through: the real spike controller two-nested-run path feeding a pure classifier; NOT a mocked outcome/ProcessorCount
  Exit: the signed receipt carries `execution_status` + `comparison_status` (no attestation/verification field), matching the observed run

### INV-002: Comparison is derived over 3 schema kinds and 5 artifact roles, each set-equal to its registry [integration]
- **Type**: must
- **Category**: data-integrity
- **Statement**: the determinism corpus is **3 schema report KINDS** — `run-report`, `route-report`,
  `control-report` (the schema enum at `Inv010DeterminismTests.cs:112`) — and **5 artifact ROLES** —
  `run`, `route-a`, `route-b`, `control-a`, `control-b` — with an explicit **role→kind map**
  (`run→run-report`; `route-a,route-b→route-report`; `control-a,control-b→control-report`). Two runs × five
  roles = **ten artifacts** and **five comparisons**. The receipt carries, per run × per role: repo-relative
  name, **raw artifact SHA-256**, **deterministic-projection SHA-256**, projection schema/version + digest,
  canonicalization version, and a per-role equality verdict; plus an aggregate. `comparison_status=equal` is
  derived **only** when the **kind set is set-equal to the schema report-kind registry**, the **role set is
  set-equal to a committed role registry**, every role maps to a declared kind, each role appears in both runs
  exactly once, every per-role **projection** digest matches, **AND** (RS-005) every per-role **recorded
  projection-policy identity** (projection schema/version + digest) is **set-equal to the manifest-pinned
  projection policy** for that role/kind via a **closed role/kind→projection-policy map**, **AND
  (QA-005-tightened, 2026-07-29) every per-role recorded projection-IMPLEMENTATION digest — computed by running
  the real projection over a committed self-test vector (`projection_impl_digest =
  SHA256(DeterministicProjection(vector))`, the pin committed alongside the map) — is set-equal to the
  manifest-pinned `projection_impl_digest`**. A degenerate/no-op
  projection that records an **off-manifest** policy identity is rejected (`projection-policy-mismatch`,
  INV-012) — so `equal` cannot be minted from projection hashes produced by a projection other than the
  manifest-pinned one; and because the impl-digest is the projection's actual output over the pinned vector, a
  producer that runs a **different** projection (even one that stamps the pinned schema/version identity string)
  yields a different impl-digest and is rejected on a **REAL** receipt, not only a hand-forged fixture. (Raw digests are expected to differ; equality is a projection property.) The schema-kind
  and role registries **and** the role/kind→projection-policy map are **committed artifacts** the set-equalities
  derive from — **never in-test literals** (RS-020 — a set-equality against a hand-written `Dictionary` in the
  test cannot detect a registry that silently shrank; AP-022). **Residual-trust note (RS-005, honest per
  PAT-004/AP-004):** verification reconstructs the Statement from the committed receipt (INV-010) and cannot
  re-derive the projection from the *volatile raw reports* (INV-006 does not commit them), so the recorded
  projection *facts* are trusted **because the producer runs the reviewed, EA-006-protected projection code** on a
  protected-`main` push (INV-007/025); the policy cross-check **plus the projection-impl-digest cross-check
  (QA-005-tightened: the pin is a genuine product of the reviewed projection over a committed self-test vector, so
  a changed projection is caught on a real receipt — no longer a fixture-only guard)** + protecting
  `spikes/dafny-compat/**` (EA-006) close the *off-manifest-policy* variant. The **narrowed** residual is now only
  a projection engineered to reproduce the pinned output on the fixed self-test vector while diverging on real
  inputs (a targeted-evasion residual) — for which the trusted, EA-006-protected producer remains the backstop;
  recorded in the ledger, not claimed as cryptographic closure.
- **Violated when**: kinds and roles are conflated (e.g., "five schema-declared kinds"); equality is over raw
  bytes; a role/kind is missing/duplicated without → `not_evaluated`; either registry set-equality is skipped; the
  role/kind→projection-policy cross-check is skipped (a recorded off-manifest projection policy mints `equal`); or
  a registry/map is a test literal rather than a committed artifact.
- **Enforcement**: hash verification + a schema test pinning the per-role receipt shape + set-equality asserts
  against both the schema kind registry and the committed role registry **and** the committed
  role/kind→projection-policy map (a fixture recording an off-manifest projection policy → `projection-policy-mismatch`);
  a verbatim-captured fixture (AP-014).
- **Guards against**: AP-006 (paired data without cardinality/coverage), AP-014/AP-031
- **Test approach**: integration
- **Integration contract**:
  Entry: the gate reading the committed receipt + the serial lane producing it
  Through: the real receipt writer + real projection code over all 5 roles; NOT a hand-written fixture as sole input
  Exit: comparison_status=equal iff all 5 role projections agree, kinds set-equal to the schema registry, roles set-equal to the role registry, role→kind map total

### INV-003: A projection disagreement hard-fails the live lane, mints nothing, is never retried [integration]
- **Type**: must
- **Category**: correctness
- **Statement**: when `comparison_status=different`, the live serial lane exits **non-zero**, the signing
  outcome is `not_attempted`, and the disagreement is reported as **"the declared deterministic projections
  differed in this observation under the recorded environment"** — strong evidence, **not** a proof of the
  underlying cause, and **not** a universal-determinism claim (RS-020: investigate, never retry into green).
- **Violated when**: the lane exits 0 on `different`; an attestation is attempted for a `different` run; or the
  message overclaims "proven nondeterminism" / universal determinism.
- **Enforcement**: the **pure classifier** proves `different` ⇒ no mint + non-zero disposition; an integration
  test routes a genuine disagreement through it (no production-accessible "force pass/flap" switch; INV-013).
- **Guards against**: AP-001 (fail-open on accept side)
- **Test approach**: integration

### INV-004: The resource floor is set by a predeclared, commit-anchored measurement campaign [integration]
- **Type**: must
- **Category**: functional
- **Statement**: the core floor is a **single-sourced constant** determined by a **campaign whose plan is
  committed BEFORE any included run** (a plan commit that **predates** every campaign run — so the plan cannot
  be chosen after seeing results). The committed plan pins: exact **N**, candidate **runner labels/classes**,
  the **success + floor-selection rule**, the **infrastructure metrics** collected, the **handling of every
  possible result** (agreement / disagreement / infrastructure-invalid), and the **retained run rows**. **Each
  campaign row records `{run_id, run_attempt, head_sha, plan_commit, plan_digest}`** — git cannot compare a
  commit to a GitHub run ID directly, so the assertion is **`plan_commit` is an ancestor of the row's
  `head_sha`** AND the recorded `run_id`/`run_attempt` was associated with that `head_sha`. All included runs are
  **attempt-1**, all retained, no cherry-picking. Below the floor →
  `execution_status=resource_floor_skipped` (valid non-attesting).
  **Completeness of the eligible run universe, not just per-row validity (RS-016):** per-row ancestry + count==N
  cannot detect an **omitted** disagreeing run (retain 5 agreeing of 6 attempt-1 runs → every retained row passes,
  count==N, the disagreement is silently cherry-picked). The plan therefore **predefines the eligible run
  SEQUENCE** — e.g. *the first N qualifying attempt-1 run IDs after `plan_commit`* — and the campaign is verified
  **set-equal to an authoritative complete run listing** for that sequence (no eligible run may be absent). The
  `run_id↔head_sha` **association is a live-GitHub-Actions-API fact**, not offline/from-clean verifiable — it is
  scoped as a **CI-network-only** check (the from-clean gate asserts only ancestry + attempt-1 + single-source),
  and each run's identity is bound to a **retained UNSIGNED workflow artifact verified against authoritative GitHub
  Actions run metadata** (F2 / round-7 re-review: **PR1 signs nothing** — DD-003 — so the earlier "signed/attested
  run artifact" was self-contradictory; the retained per-run artifact is validated against the Actions
  `run_id`/`run_attempt`/`head_sha` metadata via the CI-network check, not a signature). If a signed campaign
  binding is ever wanted, it moves to PR2 (which has the signer); PR1 stays unsigned.
  **The 4-vCPU-vs-floor-8 reality (RS-009):** the removed early-return existed because ≤4 cores flap the
  two-nested-run controller, so if the campaign confirms the floor **> 4**, PR1 **commits the standing determinism
  lane to a specific pinned runner CLASS that empirically reaches `completed`** (recorded in EA-005) — the
  "pinned larger runner" is then the **sanctioned steady-state lane**, not an escape hatch; a CI assertion
  requires the lane's **default outcome on the pinned runner to be `completed`, not `resource_floor_skipped`**, and
  a **persistent all-skip across the campaign window surfaces loudly** (AP-005 frequency monitoring) rather than
  reading as benign. Escalation *beyond* the committed class is justified for **infrastructure exhaustion only**,
  never to erase a projection disagreement; never a retry.
- **Violated when**: the plan is not committed ahead of the runs; a row asserts a commit-vs-run-ID ancestry; the
  floor is duplicated or set from one run; reached via retry; a disagreement is "fixed" by a bigger runner;
  campaign results are cherry-picked; **the eligible run sequence is not predefined / not verified set-equal to an
  authoritative listing** (RS-016); or the committed lane's default outcome on the pinned runner is
  `resource_floor_skipped` (P3 unmintable on the standing lane, RS-009).
- **Enforcement**: per-row `plan_commit`-ancestor-of-`head_sha` + a **predefined-eligible-sequence set-equality
  against the authoritative run listing** (CI-network scope) + `run_id/attempt`↔`head_sha` association + a
  single-definition test + the retained rows committed as the basis + a CI assertion that the pinned-runner default
  outcome is `completed` + a persistent-all-skip alarm.
- **Guards against**: AP-013 (skip as coverage), AP-005 (post-hoc plan selection)
- **Test approach**: integration

### INV-005: The determinism run is isolated in a dedicated serial job recording a pinned platform identity [integration]
- **Type**: must
- **Category**: functional
- **Statement**: the two nested runs execute in a **dedicated CI job** with **no competing parallel suite**; the
  job records the observed `ProcessorCount`, RID, a **pinned OS label** (not floating `ubuntu-latest`), and the
  **actual runner image/version, kernel, architecture, and resolved SDK** into the receipt. (A digest-pinned
  container is used iff exact platform reproducibility is later required.)
- **Violated when**: the run shares a job with the parallel suite; the platform identity is absent/synthesized;
  or only a floating `ubuntu-latest` with no recorded image.
- **Enforcement**: CI test assertion + a workflow-structure assertion (distinct job) + AP-020 verbatim execution.
- **Guards against**: AP-019, AP-020, AP-015 (platform drift)
- **Test approach**: integration
- **Integration contract**:
  Entry: the serial lane workflow / extracted script executed verbatim
  Through: the real isolated job; NOT the parallel-suite job
  Exit: the receipt carries observed ProcessorCount + RID + pinned OS + recorded runner image/kernel/arch/SDK

### Group B — Attestation object model + minting (PR2)

### INV-006: The signed subject is the run receipt; the manifest is bound into it; the artifact graph is exact [integration]
- **Type**: must
- **Category**: data-integrity
- **Boundary**: TB-007
- **Statement**: the signed object is an exact graph — the **run receipt bytes** (pinned schema, carrying
  `execution_status`, `comparison_status`, the per-role/kind evidence, the platform identity, `attested_commit`,
  **and the determinism-subject-manifest digest + policy version**) → **SHA-256 subject digest** over those exact
  bytes → a **versioned Corrected determinism predicate** (pinned **predicate-type URI**) → an in-toto
  **`Statement/v1`** (pinned `_type`, exactly one subject with a canonical **name** + **sha256**) → **DSSE
  payload** → **Sigstore bundle**. The predicate **references** the receipt digest and **embeds** the typed
  per-role projection facts (never the volatile raw reports). Binding the manifest digest + policy version into
  the signed receipt is what makes P3 provably "bound to the manifest" (INV-018), not merely asserted.
- **Violated when**: `_type`, predicate-type URI, subject name/algorithm, or the embed/reference decision is
  unpinned; the manifest digest + policy version are absent from the signed receipt; or the Statement is not a
  valid `Statement/v1`.
- **Enforcement**: a Statement-schema test + a hash test binding the subject digest to the receipt bytes + an
  assertion that the receipt carries the manifest digest + policy version.
- **Guards against**: AP-004, AP-014
- **Test approach**: integration

### INV-007: Signing is isolated in a minimal signer job; the determinism producer holds no OIDC privilege [integration]
- **Type**: must
- **Category**: security
- **Boundary**: TB-007
- **Statement**: **two jobs** — an **unprivileged determinism producer** (runs the spike, emits the receipt +
  Corrected-built Statement as workflow artifacts with a known digest, **no `id-token: write`**) and a **minimal
  signer**. The signer's permissions are **exactly `id-token: write` + `contents: read`** (it cannot inspect the
  commit without read access — the earlier "only id-token" was self-contradictory). **The two-permission set is
  sufficient ONLY because producer and signer are two jobs in the SAME workflow run using the same-run
  `@actions/artifact` (v4) runtime-token transfer (RS-032, EA-010) — no Artifacts REST / cross-run `run-id` /
  `gh run download`, which would need `actions: read` (forbidden here).** Splitting producer/signer across
  workflows or reaching for the cross-run API silently breaks the minimal set (403). The producer records exactly
  `${{ github.sha }}` (checks out the pinned trigger SHA, never a branch ref) so the cert source-repo-digest OID
  equals `attested_commit` (INV-011, EA-010). It **checks out at the exact `attested_commit` SHA with credentials
  NOT persisted, no
  submodules, no LFS, and Git hooks disabled**, re-checks the producer artifacts' digest/schema/producing-job
  result/commit/run-id/attempt **and the subject-manifest at `attested_commit`**, executes **only one frozen,
  reviewed signer-validation surface** (no producer/test/build/restore/package-hook/arbitrary repository code),
  signs, and publishes the bundle. Third-party Actions pinned by **commit SHA**; signs **only** a protected-main
  `push` (never `pull_request` / `pull_request_target`). The PR2 **transcript proves the actual granted
  permissions**, not merely asserts them.
- **Violated when**: the producer has `id-token: write`; the signer has broader permissions than
  `id-token:write`+`contents:read`, persists credentials, enables hooks/submodules/LFS, or executes any
  repository code beyond the frozen surface; **the artifact hand-off uses a cross-run/REST path that needs
  `actions: read`** (RS-032) or producer+signer are not the same run; an action is tag-pinned; signing runs on a PR
  event; or the signer does not re-check the manifest at `attested_commit`.
- **Enforcement**: a workflow-config assertion (exact per-job permissions, checkout options, event guard,
  SHA-pinned actions) + the signer's re-check exercised against a mismatched-artifact fixture + the permissions
  transcript.
- **Guards against**: AP-002, AP-004
- **Test approach**: integration
- **Integration contract**:
  Entry: the two-job signing workflow / extracted scripts
  Through: the real artifact hand-off + the signer's re-check (incl. manifest-at-attested_commit); NOT a single privileged job
  Exit: the producer has no id-token privilege; the signer signs only after re-validating the producer's binding + manifest

### INV-008: The signer refuses to sign a rerun — GITHUB_RUN_ATTEMPT must be 1 [integration]
- **Type**: must
- **Category**: security
- **Boundary**: TB-007
- **Statement**: the signer records `GITHUB_RUN_ATTEMPT` and **refuses to sign unless it is `1`**; a "Re-run
  failed/all jobs" is **diagnostic only** and mints nothing; a **new reviewed commit** is required after a flap
  before a new attestation can be minted.
- **Violated when**: the signer signs `run_attempt > 1`; or the attempt is not recorded.
- **Enforcement**: a signer `run_attempt==1` guard + the attempt recorded + a negative test (attempt=2 → no signature).
- **Guards against**: AP-001, AP-005
- **Test approach**: integration

### INV-009: Keyless-OIDC signing with a single pinned cosign; the bundle is time-anchored; argv frozen by transcript [integration]
- **Type**: must
- **Category**: security
- **Boundary**: TB-007, TB-004
- **Statement**: the signer signs with the **exact single-version pinned** `cosign attest-blob --statement`
  (Corrected **owns the Statement semantics**; cosign constructs/signs the DSSE envelope + obtains/logs the
  Fulcio cert as **transport**) using keyless GitHub OIDC. The exact cosign version, per-RID binary digest,
  bundle format, and **frozen signing argv** are established by a **real `attest-blob` → network-disabled
  `verify-blob-attestation` transcript spike** (a PR2 *specification* deliverable — not a GREEN-time question),
  and the bundle carries a Rekor **tlog inclusion proof + signed timestamp** (verify-later sound after the
  ~10-min Fulcio cert expires). **Two crypto facts this invariant ASSERTS are unproven until the transcript spike
  and are therefore HARD PR2 LANDING GATES (RS-007/RS-008) — if the spike disproves either, PR2 cannot freeze and
  this invariant is amended, not waived:** (a) **the keyless GitHub-OIDC bundle actually carries an RFC3161 signed
  timestamp** so `verify-blob-attestation --use-signed-timestamps` is sound **offline after the Fulcio cert
  expires** — EA-001 already grants TSA network at signing time and cosign v3 + the public-good instance require
  signed timestamps, but the transcript spike must **demonstrate a committed fixture bundle offline-verifying
  *after* cert expiry** (add `--timestamp-server-url`/a TSA to the signer if the demonstration shows one is not
  attached by default); and (b) **offline verification of the pinned bundle FORMAT works** — the research brief's
  own Open Question #1 flags that offline verify of the *new protobuf* bundle format "was not fully landed" and is
  UNCONFIRMED at v3.1.x; the spike must resolve this and, if new-format offline is unconfirmed at the pin, **sign
  with `--new-bundle-format=false`** (old format embeds the Rekor SET for offline verify) — this contingency is a
  **pinned decision carried in DD-002**, not left only in OQ-001.
- **Violated when**: cosign is unpinned/ranged; the bundle lacks a tlog proof or signed timestamp; the argv is not
  the transcript-frozen one; **the committed fixture's only time anchor requires a network Rekor lookup** (rather
  than an embedded signed timestamp) so from-clean offline verify after cert expiry is impossible (RS-007); or the
  pinned bundle format cannot be offline-verified at the pinned version without the `--new-bundle-format=false`
  contingency being taken (RS-008).
- **Enforcement**: CI config assertion (pinned version+digest+frozen argv) + a bundle-content assertion + **a
  from-clean meta-test that FAILS if the committed fixture bundle's only time anchor requires network Rekor** (it
  must offline-verify after cert expiry) + a transcript-spike landing check that records which bundle-format
  contingency (new-format-offline vs `--new-bundle-format=false`) was taken.
- **Guards against**: AP-015, AP-014
- **Test approach**: integration

### Group C — Verification (PR2; security core)

### INV-010: The probe verifies the SIGNED DSSE payload equals the Corrected-constructed Statement — not an input-file hash [integration]
- **Type**: must
- **Category**: security
- **Boundary**: TB-006, TB-007
- **Statement**: the P3 probe (a) runs the pinned `cosign verify-blob-attestation` with `--check-claims=true`,
  exact `--certificate-identity` + exact `--certificate-oidc-issuer https://token.actions.githubusercontent.com`,
  the GitHub workflow cert constraints (INV-011), `--use-signed-timestamps`, and the pinned `--trusted-root`
  (frozen argv from the transcript spike); then (b) **decodes the Statement from the signed DSSE payload** and
  requires it **byte-equal** the Statement Corrected reconstructs from the committed receipt (subject sha256 ==
  SHA-256 of the committed receipt bytes; predicate-type URI + subject name match) — **not** a pre/post
  input-file hash. Verification is **offline** (no verify-time network; no Rekor content-search — rely on the
  embedded inclusion proof). **The real-cosign path is actually WIRED into the documented gate command (RS-014 —
  else it is a phantom, AP-013):** `gate/run-readiness-gate.sh` (the project's `commands.test`) **invokes the
  online provisioning phase** (`provision-cosign.sh`, INV-017 — fetch + digest-validate the per-RID cosign binary
  + pinned trust root, EA-008) **as a documented pre-step, and the from-clean CI job does the same, before** the
  offline verify — so a fresh clone does not simply hit `verifier-unavailable` for lack of a provisioned binary.
  The real-cosign path is **`linux-x64`-only** (EA-003); off-RID it records an honest typed `rid-platform-mismatch`
  and does **not** silently skip.
- **Violated when**: verification uses a pre/post file hash as the semantic check; any pinned flag is missing or
  a regexp/insecure variant (PRH-001); the decoded-payload equality is skipped; verify-time network is required;
  **the documented gate command / from-clean CI job does not invoke provisioning before the offline verify** (the
  real cosign path never runs on a fresh clone, RS-014); or an off-RID host **silently skips** the real-cosign path
  rather than recording a typed `rid-platform-mismatch` (RS-015).
- **Enforcement**: gate precondition (real `gate/run-readiness-gate.sh` path) + real cosign verify against a
  genuine signed fixture + the decoded-payload byte-equality assertion (INV-013 layer 2) + **a from-clean
  assertion that the real cosign subprocess ACTUALLY executed** (provisioning ran; not a skipped/stubbed path).
- **Guards against**: AP-002, AP-008, AP-011/AP-013, AP-004
- **Test approach**: integration
- **Integration contract**:
  Entry: from a CLEAN checkout run `gate/run-readiness-gate.sh` (which invokes provisioning, then the offline verify)
  Through: the real Corrected.Provenance verify wrapper → real pinned cosign; NOT a stub or always-pass double (AP-012)
  Exit: a genuine signed fixture verifies AND its decoded DSSE Statement equals the reconstructed Statement; no verify-time network; the from-clean run proves the real cosign path executed (not skipped for lack of provisioning / off-RID)

### INV-011: The certificate is bound to the attested commit — workflow-SHA cross-checked, all constraints exact [integration]
- **Type**: must
- **Category**: security
- **Boundary**: TB-007
- **Statement**: verification pins the exact GitHub cert constraints — `--certificate-github-workflow-sha`,
  `-repository`, `-ref`, `-trigger`, `-name` — and **cross-checks the cert's workflow-SHA equals the receipt's
  `attested_commit`**. An exact SAN at `refs/heads/main` alone is insufficient (mutable branch); identity +
  issuer pinned **exact** (never regexp). The signing workflow uses a **direct** invocation (DD-008) so the
  cert's `workflow_sha` claim is the run's commit; if a reusable workflow is ever used, the PR2 transcript must
  demonstrate exactly which claim (`workflow_sha` vs reusable-only `job_workflow_sha`) each flag checks. The
  signing workflow digest is part of the determinism-subject manifest (INV-018).
- **Violated when**: the cert SHA is not cross-checked to `attested_commit`; any constraint is a regexp; only
  the SAN path is pinned; or a reusable workflow is used without the claim-mapping transcript.
- **Enforcement**: the frozen verifier argv includes the constraints + a probe assertion cross-checking
  cert-SHA == receipt.attested_commit + a negative fixture (wrong workflow-SHA → reject).
- **Guards against**: AP-004
- **Test approach**: integration

### INV-012: Fail-closed, typed probe result; ran-passed is derived, scoped to this observation [integration]
- **Type**: must
- **Category**: security
- **Boundary**: TB-006
- **Statement**: the probe computes a result `{verified | rejected | unavailable}` and returns `satisfied:true`
  (equivalently, derives `ran-passed`) **only** when the bundle cryptographically verifies (INV-010/011), the
  decoded receipt's `comparison_status==equal ∧ execution_status==completed`, the RID/platform match the pinned
  expectations, and the determinism-subject manifest is **valid (non-stale)** on HEAD (INV-018/019) with
  `attested_commit` an **ancestor of HEAD**. This asserts only **"the projections agreed in this two-run
  observation under the recorded environment"**, never universal determinism. The probe uses an **internal typed
  result value** (an enum/value type, NOT a free string — the carrier `ProbeResult` reason accepts any nonempty
  string, so P3 computes a typed internal result and maps it to a carrier `ProbeReasons` token at the boundary;
  the internal→carrier-token map is itself **total with no free-string fallthrough**, RS-010), and the mapping
  from **every** orchestration/crypto/policy failure to `{rejected | unavailable}` is **total AND fail-closed by
  default (RS-002)**:
  - **`unavailable`** is reserved for a **CLOSED, positively-enumerated set of transient tool/environment faults**
    only: `{verifier-unavailable` (cosign binary absent / online-provisioning not completed, EA-008),
    `trust-root-or-tool-unreadable` (the pinned root/binary file is present-but-unreadable — an I/O fault, EA-009)`}`.
  - **`rejected`** covers every policy/crypto/staleness/ancestry failure **and is the DEFAULT for anything not
    positively identified as one of the two transient faults above**: `{evidence-absent, p3-not-yet-activated`
    (RS-035 — the expected pre-PR3 zero-state; rendered distinctly by INV-021 but classified fail-closed)`,
    malformed-receipt, malformed-bundle, signature-invalid, identity-mismatch, predicate-type-mismatch,
    subject-digest-mismatch, projection-policy-mismatch` (RS-005)`, stale-subject-manifest,
    attested-commit-not-ancestor, ancestry-uncomputable` (RS-013 — a shallow-clone/absent-`X` ancestry that cannot
    be computed is `rejected`, **never** `unavailable`)`, rid-platform-mismatch, non-pass-outcome,
    trust-root-or-pin-mismatch` (a root/binary **digest MISMATCH** — distinct from the *unreadable* fault above)`,
    unclassified-verifier-fault}`. **`unclassified-verifier-fault` is the pinned DEFAULT branch**: any cosign
    crash / SIGSEGV / unknown non-zero exit / timeout / output the INV-014 exit-code taxonomy does not positively
    match → **`rejected`** (fail-closed). Treating an unclassified cosign fault as `unavailable` (the earlier broad
    "verifier/tool faults → unavailable") is the fail-open seam that **armed the RS-001 forged-ENTERED bypass** (a
    crafted malformed payload → panic → `unavailable` → non-failing verdict); it is now closed. Any internal error
    → `false` (never pass-through).
- **Violated when**: `true`/`ran-passed` for any non-conforming input; `attested_commit` not an ancestor of HEAD
  (→ `attested-commit-not-ancestor`) or ancestry uncomputable mapped to anything but `rejected`; a reason is a raw
  stderr string rather than a typed value, or the internal→carrier-token map has a free-string fallthrough; the
  DEFAULT/unclassified branch maps to `unavailable` (or anything non-`rejected`); the
  failure→`{rejected|unavailable}` mapping is not total; or an internal exception yields anything but `false`.
- **Enforcement**: gate precondition + the 3-layer test architecture (INV-013) exercising **every** typed reason;
  a **totality cross-product test whose expected-reason set is DERIVED FROM the committed reason enum artifact**
  (RS-010 — not a test literal, so shrinking it is a reviewable diff, per AP-022/PMB-003) asserting every reason
  maps to exactly one `{rejected|unavailable}` **and** the DEFAULT is `rejected`; the transient reasons
  (`verifier-unavailable`, `trust-root-or-tool-unreadable`) and `trust-root-or-pin-mismatch` **induced through the
  real cosign subprocess** (missing binary / present-but-unreadable root / swapped-digest root — not synthetic
  layer-1 injection), so the `→ unavailable` and the digest-mismatch `→ rejected` branches are both really
  exercised; an ancestry-uncomputable (shallow-clone) fixture asserting `rejected`; and a no-free-string-fallthrough
  assertion on the internal→carrier-token boundary.
- **Guards against**: AP-001, AP-004
- **Test approach**: integration

### INV-013: A three-layer test architecture isolates crypto authenticity from claim policy; positives are fixture-identity until PR3 [integration]
- **Type**: must
- **Category**: security
- **Statement**: the tests are three layers, not a mislabeled cross-product of mutated signed bundles (mutating
  a signed bundle invalidates its signature — testing crypto rejection, not policy):
  **(1) Pure policy matrix** — exhaustive combinations over **already-authenticated typed receipts** (RID, cores,
  outcome, commit, staleness, schema semantics), no cosign;
  **(2) Real cosign integration** — one **genuine positive signed under a FIXTURE identity** (never acceptable
  under the production identity) + forged-signature + tampered-payload + wrong-identity + wrong-predicate-type +
  wrong-subject-digest + malformed-bundle **+ (RS-006, corrected in round-7 re-review) TWO HONEST negatives that
  exercise the identity constant and the SHA cross-check SEPARATELY — because cosign rejects a fixture-identity
  bundle on IDENTITY FIRST, a single "prod-argv → reject on SHA" test is impossible (the SHA path is unreachable):**
  **(2a) identity constant** — a bundle genuinely signed under the FIXTURE identity, driven through the EXACT frozen
  PRODUCTION verifier argv (production `--certificate-identity`), asserting rejection is attributable to the
  SPECIFIC reason `identity-mismatch`, not a generic reject (so the production identity constant is proven *read and
  value-specific* — an always-reject/typo'd/default-accept production verifier cannot pass it); **(2b) SHA
  cross-check** — a fixture bundle verified under a **fixture-ACCEPTING crypto policy** (identity passes), with the
  authenticated receipt's `attested_commit` **differing from the certificate's workflow-SHA**, asserting rejection
  attributable specifically to the **INV-011 cert-SHA↔attested_commit cross-check** (a Corrected-side check, reached
  only once identity has passed). This exercises the SHA binding honestly before PR3. **The production-argv SHA
  binding (production identity AND production SHA together) cannot be exercised until the PR3 production bundle
  exists — recorded as an accepted residual, not faked;**
  **(3) Orchestration** — absent bundle, missing binary, process timeout, oversized file, parse failure.
  **No genuinely-production-signed `ran-failed` fixture** exists (contradicts never-sign-failures). The **first
  production-identity POSITIVE cannot exist until PR2 merges and the main workflow runs** — it arrives as PR3's
  committed evidence; PR2's tests therefore prove the mechanism using a fixture identity only. **Residual-trust
  ledger (RS-006/RS-011):** the production-identity *accept* branch **and** the production-argv SHA binding are not
  positively driven until PR3 — recorded as accepted residuals; the (2a)/(2b) negatives are what keep an
  always-accept/always-reject production verifier and an unwired SHA cross-check from shipping green in the interim.
- **Violated when**: a semantic policy row shells to cosign against a mutated-signature fixture; absent/bare-JSON
  cases claim to traverse the real cosign path; a production-identity-signed failure fixture exists; a PR2 test
  asserts a production-identity positive; **a test claims to reject a fixture-identity bundle on SHA under the
  production argv** (impossible — identity rejects first); or **the (2a) identity negative or the (2b) SHA-cross-check
  negative is absent** (AP-002/AP-010).
- **Enforcement**: the three layers with distinct harnesses; a meta-assertion that layer-1 rows never invoke
  cosign and that no committed fixture carries the production identity before PR3; the **(2a) identity negative**
  (prod argv → `identity-mismatch`) and the **(2b) SHA-cross-check negative** (fixture-accepting policy + receipt
  `attested_commit` ≠ cert SHA → the INV-011 cross-check reason), each *reason-specific*. **The same discipline
  applies to the entry-receipt verifier (INV-030): a fixture-identity entry-bundle through the production entry argv
  → `identity-mismatch`, plus the entry SHA-cross-check under a fixture-accepting entry policy**, since the
  entry-accept path is likewise unexercisable-until-P2 (RS-011).
- **Guards against**: AP-011, AP-012, AP-022
- **Test approach**: integration

### INV-014: The cosign subprocess seam is hardened [integration]
- **Type**: must
- **Category**: security
- **Boundary**: TB-004
- **Statement**: the verify wrapper invokes cosign with an **absolute pinned executable path**, an **argv array**
  (no interpolation — AP-008), a **fixed working directory**, a **clean environment** (no ambient `HOME`/TUF/
  config), **regular-file / no-symlink** checks on inputs, **size caps** on receipt/bundle/root/stdout/stderr, a
  **process timeout + process-tree termination**, an **exact exit-code/error taxonomy**, **no response-file/config
  injection**, and **atomic** output handling.
- **Violated when**: any of the above is absent.
- **Enforcement**: a subprocess-contract test (oversized output fails; symlinked input rejects; a hung cosign is
  killed) + a code scan for string-interpolated argv.
- **Guards against**: AP-007, AP-008
- **Test approach**: integration

### Group D — Toolchain + trust anchors (TB-004 / TB-007; PR2, frozen)

### INV-015: cosign is pinned to exactly one version + per-RID digest, bootstrapped non-circularly [integration]
- **Type**: must
- **Category**: security
- **Boundary**: TB-004
- **Statement**: the `cosign` binary is pinned to **exactly one version and one per-RID digest** (not a range),
  chosen at/after the advisory floors — **GHSA-w6c6-c85g-mmv6 / CVE-2026-39395** (fixed **v3.0.6**) and the
  **distinct** **GHSA-whqx-f9j3-ch6m** (fixed **v3.0.4**) — recommend the current stable line. Bootstrap is a
  **reviewed hard-coded SHA-256 of the exact release asset per RID** + an authenticated source URL — **never
  cosign-verifying-cosign**. Never "latest".
- **Violated when**: a range/"latest"/floating digest resolves; the bootstrap self-verifies; or a version below
  the applicable floor is pinned.
- **Enforcement**: a provisioning-config assertion (one version, one digest per RID, hard-coded SHA-256) + a scan
  for "latest"/range/self-verify + a version-floor assertion.
- **Guards against**: AP-015
- **Test approach**: integration

### INV-016: The trust root and every mechanism artifact are frozen in PR2; the evidence PR changes none [integration]
- **Type**: must
- **Category**: security
- **Boundary**: TB-007
- **Statement**: the Sigstore trust root is an **append-only registry of immutable, versioned root files** (each
  root file is written once and never overwritten, so historical bundles stay independently verifiable against
  the exact root current at their signing time). PR2 **freezes**: the cosign version+digest, the active
  trust-root version+digest, verifier argv, OIDC identity policy, the receipt+predicate+Statement+manifest
  schemas, and the subject-manifest classifier rules. The **evidence PR (PR3) changes none of them**. Trust-root
  rotation is a **separate reviewed mechanism/trust-policy PR** that **appends** a new immutable versioned root
  (never mutates an existing one) → a new main-branch run under the new root → a subsequent evidence PR.
  **Each signed receipt binds the trust-root registry ID + SHA-256 that verifies it (RS-029):** after a rotation
  (root v2 active) an old baseline signed under v1 must select **v1** — so the verify wrapper does **exact-root
  selection from the receipt-bound root ID**, not "try only the active root" (which would reject every historical
  bundle) nor a heuristic multi-root probe. **The cosign-version bump is a coupled frozen-asset upgrade along the
  TOOL axis (RS-030), not only the root axis:** a version-bump mechanism PR **re-runs the transcript spike AND
  proves every historical committed bundle still verifies (or is re-minted) under the new pin**, retaining a
  **version→frozen-argv map** so INV-016's "historical bundles stay verifiable" holds across the tool axis, not
  just the root axis.
- **Violated when**: PR3 touches any frozen artifact; an existing versioned root file is overwritten/mutated
  (rather than a new version appended); rotation happens inside an evidence PR; **a signed receipt does not bind
  the trust-root ID/digest that verifies it, or the verifier selects a root heuristically rather than from the
  receipt-bound ID** (RS-029); or **a cosign version bump lands without re-verifying every historical bundle**
  under the new pin (RS-030).
- **Enforcement**: the PRH-007 parsed-span diff check on PR3 + an append-only trust-root registry test + **a
  receipt→trust-root-ID binding test + a post-rotation historical-bundle-under-v1 verify test** (RS-029) + **a
  version-bump gate that re-verifies every committed historical bundle** (RS-030).
- **Guards against**: AP-005, AP-017
- **Test approach**: integration

### INV-017: Online provisioning and offline verification are distinct, executably-enforced phases [integration]
- **Type**: must
- **Category**: security
- **Boundary**: TB-004
- **Statement**: an **online provisioning phase** obtains the exact cosign binary + trust root and validates the
  hard-coded digests (network allowed); a distinct **offline verification phase** runs with **network access
  disabled** using only the binary + bundle + root + receipt. "Offline" is enforced by an **executable
  mechanism** (a Linux network namespace or a syscall-level network detector), **not prose**. A fresh clone is
  **not** offline until provisioning completes — from-clean ≠ offline.
- **Violated when**: verification is asserted offline without an executable network-disable mechanism; or
  provisioning downloads are conflated with the offline verify phase.
- **Enforcement**: the verify phase runs under a network-namespace/blocked-network harness that fails if a socket opens.
- **Guards against**: AP-013, AP-021
- **Test approach**: integration

### Group E — Capability-baseline semantics + self-reference (TB-006; PR3-gated)

### INV-018: One executable subject-classification policy drives both the manifest and the live-CI trigger [integration]
- **Type**: must
- **Category**: data-integrity
- **Boundary**: TB-006
- **Statement**: a **single executable subject-classification policy** — **pinned, not "structurally
  discovered" by an unspecified rule** — defines the determinism-relevant set and drives **both** the manifest
  set-equality check **and** the live-CI required decision. The policy pins: **closed-world owned roots/globs**
  (the determinism surface's directories), **exact anchors** for scattered individual files, **exact exclusions**
  (enumerated files — **no broad exclusion globs**), **base/head semantics** for change classification, and the
  **treatment of renamed and deleted files**. The **classifier code/policy itself and the manifest schema are
  listed in the manifest as relevant inputs**; the **manifest does not list its own digest** (avoiding
  self-reference) — its exact bytes are bound through the receipt (INV-006). The manifest enumerates exact digests
  for: spike source + projects; package/tool locks; Z3/SDK provisioning; the evidence schema + projection code;
  the report-kind + role registries; `run-spike.sh` + the nested runner; the receipt + predicate + Statement
  schemas; the Statement builder; the signing workflow + its extracted script; the verifier + policy config +
  classifier policy; the platform/RID policy; and the relevant fixtures. The gate asserts **set-equality** between
  the manifest and the classifier's discovered set — a new relevant file omitted **fails closed**. The evidence
  files, the P3 declaration, the **P2 completion-manifest and P2 active-reference paths** (the exact
  `test/manifests/phase-0.0-completion.json` + the `P2.evidence` active-reference file — named so a downstream P2
  evidence activation cannot stale P3, round-12), and **explicitly-named** migration surfaces are **excluded**, with
  the **same completeness protection** as the inclusions. (This exclusion covers the P2 *evidence artifacts* only;
  the P2 *validator code* in the shared `gate/Corrected.Gate/Probes.cs` remains a P3 verifier-surface input, which is
  why the PRH-007 delegated sequence invalidates P3 before the P2 mechanism PR.)
- **Violated when**: relevance is "structurally discovered" without a pinned closed-world rule; broad exclusion
  globs are used; base/head or rename/delete semantics are unspecified; the manifest lists its own digest; the
  classifier/policy/manifest-schema are not themselves manifest inputs; manifest membership and the live-CI
  trigger use different definitions; or the exclusion set is unprotected.
- **Enforcement**: a manifest schema + set-equality test (one classifier, two consumers) + an exclusion-
  completeness test (a mutated exclusion fails closed).
- **Guards against**: AP-016, AP-006
- **Test approach**: integration
- **Integration contract**:
  Entry: `gate/run-readiness-gate.sh` from clean + the live-lane trigger
  Through: the single classifier consumed by both the gate set-equality and the CI trigger; NOT a hard-coded list or a divergent path filter
  Exit: an omitted relevant file fails the gate AND would trigger the live job; the exclusion set is complete

### INV-019: P3 valid only while the signed manifest digest matches HEAD; a live job runs on every relevant change; parent fully remapped [integration]
- **Type**: must
- **Category**: functional
- **Boundary**: TB-006
- **Statement**: the committed baseline is P3-valid **only while** the signed receipt's **subject-manifest digest
  equals the current manifest digest** on HEAD **and** the current subject files are **set- and digest-equal** to
  the manifest (INV-018) **and** `attested_commit` is an ancestor of HEAD. The **live determinism job runs on
  every determinism-relevant change** (the classifier requires it — path filters may **optimize** but are **not**
  the acceptance mechanism; the job runs on every push/PR and the classifier decides). **Staleness invalidation is
  scoped to the PRE-ENTERED regime** (while `lifecycle=BLOCKED`, `src/` empty per INV-036 — so P3 going false is
  safe): a stale baseline is **invalidated** until a new signed main-branch result is committed; unrelated
  descendant commits keep the baseline. **POST-ENTERED, staleness never re-BLOCKs** — it surfaces as a
  non-blocking **health-check** finding + triggers the **refresh protocol** (Group G, INV-026–030), so it does
  not retroactively prohibit existing `src/` (the deadlock Blocking-1). Parent **INV-005** is amended to this
  model **mapping every orthogonal outcome** — the parent's `{ran-passed|ran-failed|skipped-resource-floor}`
  becomes the derived presentation of the legal `execution_status × comparison_status` table, **with added
  representation for `infrastructure_invalid` and attestation/verification failure** (the parent's three-value
  vocabulary has no slot for those today).
- **Violated when**: a stale baseline (manifest digest changed) reads P3-valid; the live job is gated by a path
  filter rather than the classifier; the parent amendment omits infrastructure/attestation-failure outcomes; or
  `attested_commit` is not verified as an ancestor.
- **Enforcement**: gate precondition (P3 valid ⟺ signed manifest digest == HEAD manifest digest ∧ ancestry) + a
  CI trigger that runs every push/PR with the classifier deciding + a parent-amendment completeness check
  (every orthogonal outcome represented).
- **Guards against**: AP-016, AP-005
- **Test approach**: integration

### INV-020: No self-reference; the flip is atomic and gated on from-clean re-verification [integration]
- **Type**: must
- **Category**: data-integrity
- **Boundary**: TB-006
- **Statement**: the attestation binds `attested_commit` to the **merged-main commit whose run produced it** —
  which does **not** contain the evidence files — never the evidence-PR tree that adds them. PR1 & PR2 keep
  `P3.satisfied:false`; **PR3** commits `{receipt, bundle}` via the PRH-007 parsed-span allowlist and flips
  `P3.satisfied:true` **only** when the gate re-verifies the committed bundle **from a clean checkout** (offline).
  An accepted tree is never partially-migrated.
- **Violated when**: the attestation binds to a tree containing it; P3 flips without a from-clean-verifying
  bundle; or PR3 exceeds the parsed-span allowlist.
- **Enforcement**: gate precondition (P3 true ⟺ from-clean verify) + the attested-commit non-self-reference/ancestry check + PRH-007.
- **Guards against**: AP-021, AP-016
- **Test approach**: integration

### INV-021: OQ-A#3 discharged; carrier INV-010 made real; only the P3 declaration changes in PR3 (readiness stays BLOCKED until P2) [integration]
- **Type**: must
- **Category**: functional
- **Statement**: carrier **OQ-A#3** is resolved to this keyless-OIDC-Cosign capability-baseline mechanism;
  carrier **INV-010**'s "returns `validator-deferred` unconditionally" is updated to the real verifier; the
  carrier `P3Probe` replaces the stub. **Carrier INV-010's provenance clause is RE-LABELED TB-003 → TB-007 and its
  flat `{outcome, ProcessorCount, RID}` shape replaced with the three-artifact model (RS-033):** leaving the
  TB-003 label / flat shape is INV-023's own Violated-when. The `StatusRenderer` (anchor by symbol —
  `RenderPassBlockedBanner` / `RenderReason` in `gate/Corrected.Gate/StatusRenderer.cs`, NOT a line number, RS-026;
  today the banner text is at ≈`:25` and the `unclassified` default at ≈`:49`) is updated to (a) render the new
  typed P3 reasons **with a distinct, actionable case per reason and NO `unclassified` fallthrough** (a totality
  test derived from the committed reason enum, RS-010/RS-003-of-UX); (b) render a **distinct `p3-not-yet-activated`
  zero-state** (RS-035) — the pre-PR3/pre-activation state where no bundle is expected — as an *expected* state
  pointing at the PR3 activation flow, **never** the degraded-environment `evidence-absent` "restore the committed
  evidence and re-run" text (which tells a new maintainer to restore a file that by design does not exist yet);
  (c) carry each rendered reason's **`{retryable | hard}` disposition** so pre-entry an operator can tell a
  transient `verifier-unavailable` from a hard `signature-invalid` (RS-034); (d) on a flap, emit the **`run_attempt>1`
  refusal message** ("re-runs never mint; push a new reviewed commit", RS-036/INV-008); (e) pin the **post-P3
  banner to name P2 as the sole remaining blocker** ("P3 satisfied; readiness remains BLOCKED solely because P2 is
  deferred — expected", RS-036) so a successful PR3 activation does not read as a P3 failure; and (f) **re-point the
  `validator-deferred` rendering to P2's own discharge lane** (RS-036) — after this feature only carrier INV-009/P2
  returns `validator-deferred`, so the hard-coded P3/spike-specific "INV-009/010 / DF-003 / DD-002" pointers become
  mis-targeted. **The signing-outcome diagnostic gets an OWNING mechanism (RS-034; scoped in round-7 re-review):** the
  `{not_attempted | failed}` distinction (INV-001) is emitted **by the signing workflow as a typed job-summary + a
  workflow artifact under a committed SCHEMA — NOT a committed-to-repo file** (a `failed`/aborted invocation cannot
  reliably commit anything; "named, committed unsigned diagnostic" was ambiguous/impossible). The workflow emits it
  on the paths it *can* reach (`not_attempted` = the sign step was skipped by a guard → the summary records which
  guard; `failed` = the sign step ran and errored → the summary records the error class); an abrupt process death
  emits nothing, which the RunnerInvocationOutcome classifies externally as `infrastructure_invalid` (INV-001). And
  `evidence-absent`'s remediation is **split by cause** (transient signer outage → **push a new reviewed commit to
  initiate a fresh attempt-1 run; NEVER rerun the failed workflow** — round-8 fix: "retry signer" contradicted the
  attempt-1-only / reruns-never-mint rule, INV-008/PRH-005; deliberate not-attempted → investigate, don't retry;
  benign zero-state → `p3-not-yet-activated`) — not a single "restore and re-run" for all. **The carrier's declared-vs-actual cross-check is amended (RS-018):** carrier
  **readiness-gate-carrier INV-005** today hard-fails any declared-vs-re-derived mismatch; post-entry a stale P3 is
  a **neutral `refresh-required` health finding, NOT a hard-fail**, so the carrier INV-005 rule is enumerated as an
  edit site and made lifecycle-conditioned (this spec amends the PARENT INV-005 *and* must amend the CARRIER
  cross-check). Through PR1 & PR2, `P3.satisfied` stays **false**. In PR3, **only the P3 declaration changes**
  (`P3.satisfied:false→true` + evidence pointer) — **overall `implementation_readiness` remains BLOCKED because P2
  is still false**; the `lifecycle` latch stays `BLOCKED` and does not transition to ENTERED (that awaits
  `P1∧P2∧P3`, Group G). No claim is made that readiness itself changes in PR3. **DD-002's `P3AttestationPath`
  migration is single-shaped (RS-025):** the pinned constant `test/attestations/inv010-determinism.json` is
  **retained as the active-baseline POINTER** (receipts move to `test/attestations/inv010/<commit>/`) — resolving
  the 974/975 contradiction — and the committed test that hard-asserts it (`gate/Corrected.Gate.Tests/Inv009And010ProbesTests.cs`,
  which also asserts the `validator-deferred` stub) + the `Probes.cs` `P3AttestationPath` const are **named
  migration sites** (they go red-from-clean when the stub is replaced / the shape changes).
- **Violated when**: OQ-A#3 stays open; the P3Probe remains the stub after PR2; carrier INV-010 keeps the TB-003
  label / flat shape (RS-033); StatusRenderer still hard-codes the old text, renders P3 reasons as `unclassified`,
  renders the pre-activation zero-state as `evidence-absent` "restore and re-run" (RS-035), or leaves the
  `validator-deferred`/post-P3-banner rendering mis-targeted (RS-036); the signing-outcome diagnostic is prose-only
  (RS-034); the carrier declared-vs-actual cross-check still hard-fails a post-entry stale P3 (RS-018);
  `Inv009And010ProbesTests.cs`/the `P3AttestationPath` const are unnamed migration sites (RS-025); or the spec/docs
  claim overall readiness (not just the P3 declaration) changes in PR3.
- **Enforcement**: gate precondition (carrier suite exercises the real P3Probe + StatusRenderer over the typed
  reasons with a no-`unclassified` totality assertion) + a spec-consistency check + the readiness-block regression
  + a post-entry-stale-P3 → neutral (not hard-fail) carrier fixture (RS-018) + the migrated
  `Inv009And010ProbesTests.cs` (RS-025).
- **Guards against**: AP-005
- **Test approach**: integration

### Group F — Integration + boundaries (ARCHITECTURE / parent)

### INV-022: Corrected.Provenance is a 5th, non-shipped gate project + a designed shared CONTRACT; the exact-four→five migration is complete [integration]
- **Type**: must
- **Category**: functional
- **Statement**: `gate/Corrected.Provenance` is added as a **5th** gate project, and **every** exact-four
  contract site is migrated to exact-five. **The site list is ITEMIZED to the exact committed literals — not the
  under-count "the INV-015 membership meta-test array" that leaves siblings red-from-clean (RS-020, the AP-016 /
  PR#6 test-name+guard+fixture-consistency class):** the `Corrected.Gate.slnx` aggregator + its explicit 4-set
  comment; INV-014 "aggregates **exactly** the four gate projects"; **in `gate/Corrected.Gate.Tests/Inv015PinnedToolchainTests.cs`
  — ALL of: the `GateProjects` array (≈`:18`), the behavioral guard `Assert.Equal(4, projectCount)` (≈`:87`, which
  RED-fails the instant the 5th project is aggregated), the test-method NAME `Aggregator_membership_is_exactly_the_four_projects`
  (≈`:79`, rename → `…_exactly_the_five_projects`), and the "four" comments (≈`:11`/`:76`)**; the BND-002 loop; the
  new **`gate/Corrected.Provenance/packages.lock.json`** the `--locked-mode` restore needs (else from-clean restore
  fails); carrier `readiness-gate-carrier.md:133/882/1216`; ARCHITECTURE:84; **and `docs/features/readiness-gate-carrier.md:24`
  + its adjacent project-role table** (add the `Corrected.Provenance` row) (historical journal/verification records
  stay historical — not migrated). (Anchor line numbers are illustrative — re-pin by symbol/exact-literal at
  implementation, RS-026.) It is a **non-shipped** substrate with a **deliberately partitioned reuse contract**:
  the **generic** in-toto **Statement / subject / DSSE-envelope / signer-identity verification contracts** are the
  reusable part the eventual release-provenance consumers (parent **INV-031/032/033**) will reuse; the
  **determinism predicate schema + the RunReceipt schema are P3-SPECIFIC and are NOT reused** by release provenance
  (which carries a different predicate). Reuse is at the generic-contract level, **not** a shipped binary linking
  the gate project (PRH-010). **The two-homes conflict is reconciled (RS-023):** `gate/Corrected.Provenance/**` is
  **already** in the `reference-ci-provenance` entrypoint scope (`ARCHITECTURE.md:97`, bound to TB-003 release
  provenance + INV-005/031/032/033); INV-023 amends that entrypoint so the directory is **owned by
  `readiness-build-gate` (TB-007)** and only **reused-by-reimplementation** for TB-003 (PRH-010) — the `INV-005/P3`
  claim is removed from `reference-ci-provenance`, and the new two-job determinism+signing **workflow file is named
  and mapped to its own entrypoint** (ARCHITECTURE:94's stale three-value `{ran/skipped}` prose → the INV-001
  four-row `execution × comparison` model).
- **Violated when**: any exact-four site still asserts four (**incl. `Assert.Equal(4, …)`, the method name, and the
  comments**, RS-020); the doc-copy migration misses `:24` or its role table; the new `Corrected.Provenance/packages.lock.json`
  is absent (from-clean `--locked-mode` restore fails); `gate/Corrected.Provenance/**` is left dual-homed with the
  `INV-005/P3` claim still on `reference-ci-provenance` (RS-023); the new workflow file is unnamed/unmapped; the
  P3-specific predicate/receipt schema is framed as reusable by release provenance; or the substrate is framed as a
  shipped dependency.
- **Enforcement**: the migrated membership meta-test (exact-five, method renamed, `Assert.Equal(5, …)`) + a
  from-clean 5-project `--locked-mode` restore + a lockfile-registry test + a reuse-contract note in ARCHITECTURE +
  the reconciled `reference-ci-provenance` entrypoint + PRH-010's no-shipped-reference scan.
- **Guards against**: AP-016
- **Test approach**: integration

### INV-023: TB-007 is registered as a distinct trusted-CI evidence boundary; TB-003 is unchanged [integration]
- **Type**: must
- **Category**: security
- **Statement**: a new **TB-007 — "trusted-CI evidence signing/verification"** boundary is registered in
  ARCHITECTURE (crosses: trusted-CI execution → a durable, provenance-bound determinism claim); **TB-003**
  (published-artifact release provenance) is **unchanged**. TB-004 (cosign intake) + TB-006 (committed evidence)
  reused as-defined.
- **Violated when**: this feature's signing/verification boundary is labeled TB-003; or TB-003 is silently broadened.
- **Enforcement**: an ARCHITECTURE entry for TB-007 + a spec cross-reference check.
- **Guards against**: AP-004
- **Test approach**: integration

### INV-024: The serial lane's operator surface is executed verbatim and (if charter-backed) kept in sync [integration]
- **Type**: must
- **Category**: functional
- **Statement**: the lane's runnable surface is exercised **verbatim** (AP-020); if it carries a co-located
  requirements charter (mirroring `spike-ci.yml` ↔ `dafny-compat-spike.yml`, `Inv014OperatorSurfaceTests`), the
  live workflow and charter are asserted **in sync**. **The lane's logic MUST live in a committed EXTRACTED shell
  script the workflow invokes, NOT inline `run:` steps (RS-028):** GitHub Actions steps are not runnable locally,
  so an inline-YAML lane can only be "verified" by reconstructing the commands (a proxy) or grepping the YAML —
  the exact AP-020/PMB-001 trap that made the spike's `run-spike.sh` exit-127 escape (a path reused after a `cd`, a
  cwd/`argv[0]` assumption, an env-strip survives a reconstruction but not a verbatim exec). The workflow calls the
  extracted script; a **workflow↔script sync assertion** (mirroring `Inv014OperatorSurfaceTests`) proves the live
  YAML invokes exactly that script.
- **Violated when**: the lane is verified by a doc/keyword grep or a fixed-cwd proxy; **the lane logic is inline
  `run:` steps with no committed extracted script to exec verbatim** (RS-028); or it drifts from its charter.
- **Enforcement**: an execution test running the **committed extracted script** verbatim (documented cwd + argv[0]
  form) + a workflow↔script sync assertion + (if charter) a charter sync assertion.
- **Guards against**: AP-020, AP-011
- **Test approach**: integration
- **Integration contract**:
  Entry: the lane's runnable script executed exactly as documented
  Through: the real script; NOT a fixed-cwd/absolute-path helper
  Exit: the command gets past launch and runs the determinism step; charter (if present) matches the workflow

### INV-025: The threat claim is narrowed to evidence tampering under a protected mechanism, with stated external assumptions [integration]
- **Type**: must
- **Category**: security
- **Statement**: the asserted guarantee is **"tampering or fabrication of evidence under an unchanged, reviewed
  verifier, workflow, tool pin, and trust policy"** — cryptography does **not** defend against the principal who
  can also replace `P3Probe`, the trust root, the workflow, the cosign pin, the evidence allowlist, or the
  readiness kernel (consistent with Corrected's TCB model, which excludes defending a host that rewrites policy /
  forges CI). The out-of-band protections are stated as **external assumptions** (EA-006, now the **complete**
  protected set incl. `phase-0-1-worker.md`'s lifecycle/preconditions spans, `test/attestations/**` + pointers, the
  floor constant, and `spikes/dafny-compat/**` — RS-012). The CODEOWNERS **presence check is an
  ASSUMPTION-COMPLETENESS check (does the file COVER the protected paths), NOT structural enforcement of the
  guarantee (RS-039):** required-review + branch protection live in out-of-band GitHub settings that EA-006
  assumes and that no in-repo test can prove (a presence check passes even if branch protection is disabled) —
  labeled as such per PAT-004/AP-004, so its strength is not overstated.
- **Violated when**: the spec/receipt/docs claim protection against "anyone with commit access" without the
  narrowing + external assumptions; the protected-path set is incomplete (omits the lifecycle field or the
  pointers, RS-012); or the CODEOWNERS presence check is represented as structural enforcement of the guarantee
  rather than assumption-completeness (RS-039).
- **Enforcement**: a doc/spec assertion of the narrowed guarantee + a CODEOWNERS **assumption-completeness** check
  (the file covers the **complete** RS-012 protected-path set) — explicitly distinct from the out-of-band
  required-review enforcement it cannot verify.
- **Guards against**: AP-004
- **Test approach**: integration

### Group G — Readiness phase-entry lifecycle (carrier kernel + parent INV-036; built + fixture-tested here, activates when P2 lands)

**Readiness state model (schema v2).** The carrier today recognizes readiness **schema v1** with a committed
`status` declaration (`gate/Corrected.Gate.Kernel/ReadinessBlock.cs:63`). Schema v2 **separates the small
persisted wire format from runtime-derived values** — persisted declarations and derived results must never be
conflated:

**Persisted (committed, small wire format)** — `{ schema_version, status, ready_predicate, lifecycle,
entry_evidence_pointer, preconditions }` (v2; **`ready_predicate` is RETAINED from v1**, round-8 fix — the
existing domain type + parser require it, `ReadinessBlock.cs:73`; the earlier v2 tuple dropped it):
- `schema_version ∈ {1, 2}` — the recognized set (RS-021); v1 blocks still parse.
- `status ∈ {BLOCKED, READY}` — a committed **declaration** (the gate always re-derives the live computation from
  probes; it never trusts this field). `indeterminate` is an **internal parser result, never a legal serialized
  value**.
- `ready_predicate` — **required in BOTH v1 and v2** (the declared readiness predicate the domain type carries;
  never dropped by the migration).
- `lifecycle ∈ {BLOCKED, ENTERED}` — `COMPLETE` is **reserved conceptually** and added only via a later schema
  version (no COMPLETE code here). The declared latch is monotonic (see the transition rules).
- `entry_evidence_pointer` — a **versioned** path to the entry receipt/bundle; **required iff `lifecycle=ENTERED`,
  absent iff `BLOCKED`**.
- `preconditions` — the P1/P2/P3 declarations (as v1).

**Exact per-version field table (round-8 — the wire format must be unambiguous, and presence bits are for
VERSION-AWARE PARSING, not for making required v2 fields semantically optional):**

| Field | v1 | v2 |
|---|---|---|
| `schema_version` | required (`=1`) | required (`=2`) |
| `status` | required | required |
| `ready_predicate` | **required** | **required (retained)** |
| `preconditions` | required | required |
| `lifecycle` | **PROHIBITED** (absent ⇒ implicit BLOCKED) | **REQUIRED** (`BLOCKED`\|`ENTERED`) |
| `entry_evidence_pointer` | **PROHIBITED** | **required iff `ENTERED`, prohibited iff `BLOCKED`** |

A **v1** block carrying a `lifecycle`/`entry_evidence_pointer` key, or a **v2** block **missing** `lifecycle` or
`ready_predicate` (or carrying a pointer while `BLOCKED` / missing one while `ENTERED`), **fails closed**. Presence
bits let the parser tell "field absent **because v1**" from "field **required in v2** and missing" — they do
**not** make v2's `lifecycle` optional (correcting the earlier "optional `lifecycle`, default BLOCKED" phrasing:
the *default-BLOCKED* semantics apply **only** to interpreting a v1 block, where `lifecycle` is absent by design).

**Derived at runtime (never persisted)**:
- `effective_lifecycle` — for the **src/ ban** (INV-027) it equals the **declared** `lifecycle` (once ENTERED,
  monotonically ENTERED — a transient integrity fault never reverts it).
- `entry_integrity ∈ {verified | rejected | unavailable | absent}` — the cryptographic verdict on the committed
  entry receipt (INV-026/030); drives the **gate verdict**, not the src/ ban.
- `current_health` — the post-entry determinism/precondition signal (total mapping below).
- `readiness_verdict` — the overall gate verdict composed from the above.

**Legal combinations & transitions**:
- **v1** (no `lifecycle`/pointer): interpreted **pre-entry only** (`declared lifecycle=BLOCKED`); recognized-set
  retains v1 so a not-yet-migrated block still parses (RS-UC / AP-005). **The real committed block is v1 today and
  stays v1 through all of P1/P2/P3 activation** — `precondition-reactivation` (k=3) flips `P3.satisfied` in the v1 block (no
  schema change); the block only ever leaves v1 via the atomic phase-entry transition below.
- **v2 `BLOCKED`**: `entry_evidence_pointer` **absent**; `current_health = not-applicable` (**not** `ok`); the
  gate drives `src/` off the re-derived preconditions exactly as v1.
- **v2 `ENTERED`**: `entry_evidence_pointer` **required** and an activation entry receipt is **expected +
  verified** (INV-029/030). No `ENTERED→BLOCKED` transition exists.
- **Phase-entry transition (round-8 fix — the ONLY legal path a v1 block reaches ENTERED; there was previously NO
  legal v1→v2 entry because the phase-entry class permitted `lifecycle:BLOCKED→ENTERED` but not the `schema_version`
  bump, and the mechanism-change class forbade touching `lifecycle`/pointers):** phase entry is **one atomic
  transition** (PRH-007 phase-entry class), in either of two shapes, and **nothing else in the block changes**:
  - **implicit-v1-BLOCKED → v2-ENTERED** (the common real case): permits **exactly** `schema_version:1→2`, **adds**
    `lifecycle:ENTERED`, **sets** `entry_evidence_pointer`, and **adds** the entry evidence — `status`,
    `ready_predicate`, and `preconditions` are unchanged.
  - **v2-BLOCKED → v2-ENTERED**: permits `lifecycle:BLOCKED→ENTERED`, **sets** `entry_evidence_pointer`, **adds**
    the entry evidence (no `schema_version` change).
  There is **no standalone v1→v2 schema-migration class** — atomic phase entry is the single path v1 leaves (it
  preserves the two-step sign→activate protocol: the two steps are *sign the entry receipt for X* then *activate*,
  not *migrate* then *enter*). The entry receipt attests the state **at X** (which is v1); the schema bump is part
  of the activation diff, not part of what the receipt attests. **Both shapes are fixture-tested** (v1→v2 entry AND
  v2-BLOCKED→v2-ENTERED); the live transition cannot fire until P2 lands.
- **Serialized `indeterminate`**: **illegal** (parser-internal only).

**`current_health` — a SET of typed findings** (post-entry; staleness, a P1 regression, and a disagreement can
occur **simultaneously**, so health is a set, not one enum). Each finding is typed and carries a **severity**
(`hard-red` | `advisory`): `refresh-required` (advisory — stale baseline vs current relevant state) |
`disagreement` (hard-red — live two-run projection diff) | `infrastructure-invalid` (hard-red — runner/tool
fault) | `resource-floor-skipped` (advisory) | `evidence-integrity-rejected` (hard-red — an entry/P3 receipt is
rejected/tampered) | `p3-verifier-unavailable` (advisory — the current P3 verifier/root is transiently
unreadable; **never** represented as `ok`) | `precondition-regression` (hard-red — a post-entry **P1 or P2**
regression; health models these too, not only P3). **Conclusion fold**: any `hard-red` finding → CI **failure**;
else any `advisory` finding → **neutral**; else **success**. Health **never changes `lifecycle` or reapplies the
src/ ban** — but some findings **intentionally produce hard-red CI** (they are not merely advisory).

**Overall post-entry conclusion is the CROSS-PRODUCT of the (B) `entry_integrity` row AND the health fold, with
explicit precedence (RS-019):** `hard-red` **always wins** — if *either* the `entry_integrity` row is a hard-red
failure (`rejected`/`absent`) *or* the health fold is `failure` (any hard-red finding, e.g. a live
`disagreement`), the overall conclusion is **hard-red failure**. A neutral `entry_integrity=unavailable` row
**never downgrades** a hard-red health finding to neutral (the earlier text said "the health fold applies only in
the `verified` row," which would have let a real disagreement fail open when integrity was transiently
unavailable). Only when the `entry_integrity` row is non-failing (`verified` or transient-`unavailable`) **and**
the health fold is not `failure` does the softer conclusion (success / neutral) apply. This is enforced as a
`transition_context × entry_integrity × health-severity` cross-product test (INV-028 enforcement).

**Overall verdict — a CLOSED CROSS-PRODUCT over `transition_context × entry_integrity` (RS-001).** The earlier
single-axis `entry_integrity` table was **fail-open on the accept side** (AP-022): it lacked the
`is_activation_event` dimension INV-029 depends on, so a **forged first activation** carrying a bundle *crafted to
FAULT* (an `unavailable`, not a clean `rejected` — the malformed-payload class the cosign pin floor guards, RS-002)
would read as the non-failing `unavailable → neutral, ban-lifted` row and **merge**. The table is now split by
whether this evaluation is a **first activation** (`transition_context = at-activation`: the PR's protected-`main`
merge-base is `lifecycle=BLOCKED` and this PR proposes `BLOCKED→ENTERED`) or an **already-established** ENTERED
state (`transition_context = established-ENTERED`: the merge-base is already `lifecycle=ENTERED`):

**(A) `transition_context = at-activation` (first BLOCKED→ENTERED) — activation acceptance requires a `verified`
entry receipt; anything else FAILS CLOSED:**

| `entry_integrity` | activation accepted? | `src/` ban | `readiness_verdict` |
|---|---|---|---|
| `verified` | **yes** — activation accepted, `lifecycle→ENTERED` | lifted | success (health fold applies) |
| `unavailable` | **NO** — hard-fail; a first activation must NOT merge on a fault | **NOT lifted** (activation didn't happen) | **hard-red failure** (never neutral — RS-001) |
| `rejected` | **NO** — hard-fail | not lifted | **hard-red failure** |
| `absent` | **NO** — hard-fail | not lifted | **hard-red failure** |

**(B) `transition_context = established-ENTERED` (the base was already ENTERED; the src/ ban keys off the
*declared, monotonic* `effective_lifecycle`; the verdict off `entry_integrity`):**

| `entry_integrity` | `src/` ban (INV-027) | `readiness_verdict` |
|---|---|---|
| `verified` | lifted | success (health fold applies) |
| `unavailable` (transient outage) | **still lifted** (monotonic — a *transient* outage never re-bans existing `src/`) | **neutral/degraded** — integrity fails closed, but src/ **not** re-banned |
| `rejected` (tampered committed ENTERED receipt) | still lifted (moot) | **hard-red failure** — the gate fails; the tamper gains nothing |
| `absent` while `declared:ENTERED` | still lifted (moot) | **hard-red failure** — an ENTERED declaration without its committed entry receipt |

The safety-direction invariant, cross-product-tested (INV-026 enforcement): **no `at-activation` evaluation with
`entry_integrity ≠ verified` EVER yields a non-failing verdict or an accepted activation.** The monotonic
"still-lifted / neutral" rows in (B) are the *deliberate, narrow* exception for a **transient** outage of an
**already-verified-at-activation** ENTERED state — they never apply to a first activation.

### INV-026: The pure kernel PROPOSES a transition; an impure orchestrator revalidates + signs; ENTERED is derived by VERIFYING the entry receipt [integration]
- **Type**: must
- **Category**: functional
- **Boundary**: TB-006
- **Statement**: the lifecycle is realized by **four distinct components** (never conflated — the kernel is
  I/O-free, in `gate/Corrected.Gate.Kernel/ReadinessGate.cs`, the `ReadinessGate` kernel class — anchor by SYMBOL,
  not line number, RS-026): **(1) a pure transition evaluator** — a **NEW kernel function** computing a **proposed
  transition** `{stay-BLOCKED | propose-ENTER | honor-ENTERED}` from the 3-tuple `(readinessBlock, probeResults,
  entryIntegrity)`, minting/writing nothing. **It is ADDED ALONGSIDE the retained 2-arg verdict function
  `EvaluateReadiness(ReadinessBlock, probeResults) → {Pass|Fail, offending}` — NOT a replacement (RS-022):** the
  existing verdict function, its return type, carrier INV-004/005's total-verdict table, and the ARCHITECTURE
  `readiness-build-gate` handler YAML (`ARCHITECTURE.md:83`) + `docs/features/readiness-gate-carrier.md` all stay
  valid; both the new transition evaluator's signature AND the ARCHITECTURE handler YAML are enumerated edit sites
  (the handler string the Phase-0.1 `[integration]` invariants bind Entry/Through/Exit to must describe the added
  function, not be left stale). **(2) a main-branch entry producer/signer** (trusted
  CI) signs the Phase-0.1-entry receipt for a commit `X` (INV-029); **(3) a gate-side receipt verifier** (impure,
  in `gate/Corrected.Gate/`) cryptographically verifies the committed entry receipt (INV-030), yielding
  `entry_integrity ∈ {verified|rejected|unavailable|absent}`; **(4) an activation-diff validator** enforces the
  activation-only diff (INV-029/PRH-007). **`declared_lifecycle` (persisted) vs `entry_integrity` (derived) are
  distinct**: `effective_lifecycle` for the src/ ban tracks the **declared** latch (monotonic — a transient
  `unavailable`/`rejected` integrity never reverts it, so a verifier outage or damaged root does **not**
  re-illegalize existing `src/`, INV-027), while `entry_integrity` drives the **gate verdict** (a forged
  `declared:ENTERED` still cannot pass because its independent integrity check yields `rejected` → the gate fails;
  it gains the forger nothing). **Entry verification is against the HISTORICAL entry snapshot at `X`, by one
  pinned rule**: **at activation** (INV-029) the gate performs a **full re-validation** — it re-derives
  `P1∧P2∧P3` from the evidence **committed at `X`** and checks it equals the entry receipt's digests; **on every
  ordinary later run** the gate performs **signature + schema + ancestry** verification of that same entry
  receipt against `X` (no re-derivation) — **never** against current HEAD's evidence (so a later P3 refresh, which
  moves the active P3 pointer, does **not** invalidate entry). Current evidence + active pointers feed **health
  only**. Reading the evidence blobs at `X` requires `X` reachable from a from-clean checkout (fetch-depth /
  full history), which is stated as an environment assumption.
- **Violated when**: the kernel signs/writes/persists; `effective_lifecycle` (src/ ban) is keyed off
  `entry_integrity` so a transient outage re-bans `src/`; entry verification compares evidence digests against
  current HEAD rather than the historical snapshot at `X`; or the four components are collapsed (e.g. the local
  gate "obtains the signed receipt" as if it were the remote signer).
- **Enforcement**: a kernel-purity test (no I/O — carrier INV-004) + a **`transition_context × entry_integrity ×
  declared_lifecycle` cross-product** state-machine test over synthetic `(block, probeResults, entryIntegrity)`
  fixtures realizing the state-model tables (A)/(B) and its safety-direction invariant (RS-001): propose-ENTER only
  when `P1∧P2∧P3` re-derive AND `entry_integrity=verified`; **at-activation with `entry_integrity ∈
  {unavailable, rejected, absent}` → activation NOT accepted, hard-red (NOT neutral)**; established-ENTERED
  declared-latch monotonic under a *transient* `entry_integrity=unavailable` (src/ not re-banned, verdict
  neutral); forged declared-ENTERED with `rejected`/`absent` integrity → gate hard-red, src/ **not** re-banned but
  cannot land (the fused verdict fails) + a historical-snapshot test (a P3 refresh after entry keeps entry valid).
- **Guards against**: AP-002 (dead code), AP-005, AP-001
- **Test approach**: integration
- **Integration contract**:
  Entry: the four components driven over synthetic fixtures via `gate/run-readiness-gate.sh`
  Through: the pure evaluator (proposes) + the gate-side verifier (integrity of the historical entry snapshot); NOT a committed lifecycle flag trusted directly, NOT current-HEAD evidence for entry
  Exit: declared-ENTERED src/ ban is monotonic under a transient outage; a forged ENTERED fails the gate; a P3 refresh does not invalidate entry

### INV-027: Parent INV-036's production-code ban is scoped to the pre-ENTERED state [integration]
- **Type**: must
- **Category**: security
- **Statement**: parent **INV-036** is amended so the production-code ban depends **solely on
  `effective_lifecycle`** — `effective_lifecycle != ENTERED → production src/ prohibited`. This is **not** the
  same as `status ∈ {BLOCKED, indeterminate}`: the entry commit `X` has all preconditions satisfied
  (`status=READY`) while still `declared lifecycle=BLOCKED` (activation hasn't happened), and that
  **`READY + BLOCKED` state MUST remain pre-entry** (empty `src/` still required) — a status-based predicate would
  wrongly permit production code before the signed entry activation. `effective_lifecycle=ENTERED` tracks the
  **declared** latch (INV-026, monotonic), so once entered, a later precondition **health** signal or a transient
  `entry_integrity=unavailable` does **not** re-trip INV-036 (the Blocking-1/Blocking-3 deadlock). The ban is
  **not** weakened pre-entry.
  **The amendment is not one-sided (RS-004 — this project's escaped-bug class is the *incomplete* cross-doc edit,
  AP-016):** re-keying INV-036 requires rewriting **every** currently-`status`-keyed clause, enumerated as edit
  sites so none is left contradicting the child: parent **INV-036 Statement**, its **Violated-when** (incl. the
  "an implementation PR merges while `status ≠ READY`" clause — *false* at `READY+BLOCKED`, must become
  `effective_lifecycle != ENTERED`), and its **Enforcement**; **`ARCHITECTURE.md:111`** ("fail a PR … while
  `implementation_readiness.status = BLOCKED`") and the **:113** partition prose; and the **kernel ban-predicate**
  (`gate/Corrected.Gate.Kernel/ReadinessGate.cs:43–44`, "ban stays active while status ∈ {BLOCKED,
  indeterminate}"). Leaving any of these `status`-keyed produces **two contradictory predicates for one
  invariant**, one of which (the unamended parent) fails open at `READY+BLOCKED`.
  **The lift never trusts the committed flag alone (RS-004 / PAT-005 "never trust the declared flag"):** the
  **first** lift (a `BLOCKED→ENTERED` activation) is accepted **only** when the derived `entry_integrity=verified`
  at activation (state-model table **(A)**), never from the declared `lifecycle` field; and the **src/-ban check
  and the `entry_integrity` verdict are FUSED into one required gate — there is NO standalone consumer of
  `effective_lifecycle`.** So a **forged `declared:ENTERED`** committed without a verified activation yields
  `entry_integrity ∈ {rejected, absent}` → the fused gate is **hard-red** (table (B)) → `src/` cannot land: the
  "ban lifted (moot)" cell is safe *only because* the co-required verdict fails in the same gate. Once the
  transition is *established*-ENTERED (verified at activation, recorded), the monotonic declared latch governs the
  ban so a later *transient* `entry_integrity=unavailable` does not re-ban existing `src/` (availability).
- **Violated when**: the ban keys off `status` rather than `effective_lifecycle` (so `READY+BLOCKED` permits
  `src/`); **any** enumerated clause (parent INV-036, ARCHITECTURE:111/113, the kernel predicate) is left
  `status`-keyed; the **first** lift is accepted from the declared flag without `entry_integrity=verified` at
  activation; a **standalone consumer** of `effective_lifecycle` lifts the ban without co-requiring the
  `entry_integrity` verdict in the same required gate; it still trips post-`ENTERED`; a transient integrity outage
  re-applies it; or it is weakened pre-`ENTERED`.
- **Enforcement**: the amended INV-036 predicate exercised by a fixture matrix (BLOCKED+content → trip;
  **READY+BLOCKED+content → trip**; ENTERED+content → allowed; ENTERED+content+integrity-unavailable → allowed;
  BLOCKED+empty → allowed) + a **forged-declared-ENTERED fixture** (`declared:ENTERED` + `src/` content +
  `entry_integrity ∈ {rejected, absent}` → the **fused** gate is hard-red, ban-lift moot, `src/` cannot land) + a
  **cross-doc consistency check** asserting no enumerated clause (parent INV-036, ARCHITECTURE:111/113, kernel
  predicate) still references the `status` predicate + a scan proving no standalone `effective_lifecycle` consumer
  exists outside the fused gate.
- **Guards against**: AP-001, AP-005
- **Test approach**: integration

### INV-028: Post-entry determinism is a health check with SEPARATELY-PINNED CI conclusions and an append-only refresh [integration]
- **Type**: must
- **Category**: functional
- **Boundary**: TB-006
- **Statement**: after `ENTERED`, the live determinism job runs on every determinism-relevant change with an
  **exact, pinned CI conclusion per outcome class** (a choice-free policy, so a required check cannot deadlock a
  relevant PR — every relevant change necessarily staled the old baseline):

  | Outcome class | Pinned CI conclusion | `current_health` finding |
  |---|---|---|
  | stale baseline (relevant change) | **neutral / advisory** (never a required merge-blocker) | `refresh-required` |
  | resource-floor skip | **neutral** | `resource-floor-skipped` |
  | current two-run projection disagreement | **hard red (failure)** | `disagreement` |
  | **current P3 verifier/root transiently unavailable** | **neutral** (retryable; not a false-green, not a merge-blocker) | `p3-verifier-unavailable` (**never** `ok`) |
  | malformed evidence / signature rejection | **hard red (failure)** | `evidence-integrity-rejected` |
  | runner / infrastructure failure | **hard red (failure)** | `infrastructure-invalid` |

  (Distinct from the **entry** receipt being rejected/absent/unavailable — that is the `readiness_verdict ×
  entry_integrity` closed table in the state model: `declared_lifecycle` stays `ENTERED`, `src/` is **not**
  re-banned, and only the verdict changes.)

  **"neutral/advisory" is realized as an ORDINARY-SUCCESSFUL non-required job with a typed summary/artifact — NOT a
  literal GitHub "neutral" Check-Run conclusion (RS-017, corrected in round-7 re-review):** `gate/run-readiness-gate.sh`
  exposes only exit 0 / non-zero (advisory→exit-0 is an ordinary success, →non-zero an ordinary failure,
  `continue-on-error` converts failure→success — none is "neutral"). Publishing a *literal* neutral Check Run needs
  a custom publisher with **`checks: write`**, but **public-fork PR tokens are downgraded to read-only** — an
  unacceptable dependency for an open-source project (per GitHub's workflow-permissions docs). So the advisory
  outcomes are produced by **separating the two CI surfaces**: a **REQUIRED hard-red gate job** (the merge-blocker —
  only the `hard red` outcome classes fail it, exit non-zero) and a **distinct, NON-required advisory health job
  that runs to an ordinary SUCCESS (green) and carries its `refresh-required`/`resource-floor-skipped`/
  `p3-verifier-unavailable` outcome in a TYPED job-summary + an emitted workflow artifact under a committed schema**
  — no `checks: write`, no fork-token dependency, no literal-neutral requirement. The spec pins **which surface owns
  each outcome class** so a stale baseline / resource-floor skip / transient verifier-unavailable never blocks a
  relevant PR (the non-required advisory job stays green + records the typed outcome) while a disagreement /
  malformed-evidence / infra failure always fails the required gate.

  After the PR merges, **main** signs the new observation; an **evidence-refresh PR appends and activates** the new
  baseline. Refresh uses **versioned evidence paths** `test/attestations/inv010/<attested-commit>/…` with a small
  **active-baseline pointer** (updated through the PRH-007 **P3-refresh** allowlist mode), so the prior baseline
  stays independently verifiable (append-only, INV-016 style). None of this re-BLOCKs readiness or trips INV-036
  (INV-027); the refresh reuses the **same frozen mechanism** (a mechanism change is a separate PR2-class PR).
  **Pointer↔receipt coupling is validated as a fail-closed pair (RS-029, AP-017):** each pointer (the P3
  active-baseline pointer AND `entry_evidence_pointer`) **must resolve to an existing committed versioned receipt**
  — a **half-applied refresh** (new versioned receipt committed but the pointer not updated, or the pointer moved
  but its target receipt absent) is caught fail-closed, because append-only evidence never self-heals a dangling
  pointer.
  **Closed pointer schema (F4 / round-7 re-review):** the pointer is parsed into a **closed schema**, fail-closed on
  any deviation — a **normalized, repo-relative path** under a **fixed root** (`test/attestations/inv010/…` for the
  P3 pointer; the entry root for `entry_evidence_pointer`); **no `..`, no absolute paths, no symlinks**; **exact
  cardinality** (each pointer names exactly one receipt AND its one bundle — no more, no fewer); and
  **commit-directory agreement** (the pointer's `<commit>` segment equals the receipt's `attested_commit`/entry-`X`
  and the on-disk directory name). A pointer that escapes the root, is symlinked, names the wrong cardinality, or
  disagrees with its receipt's commit fails closed.
- **Violated when**: any outcome class is left as an unpinned "hard red or classified" choice; **"neutral" is
  emitted by mapping to exit-0 / `continue-on-error` on the required gate rather than a distinct non-required
  advisory surface** (RS-017); a stale baseline or a resource-floor skip is a **required** red merge-blocker; a
  real disagreement / malformed-evidence / runner failure is downgraded to advisory; a refresh overwrites (rather
  than appends via versioned path + pointer) the baseline; **a pointer resolves to a missing/superseded target (a
  half-applied refresh passes)** (RS-029); or a refresh smuggles a mechanism change.
- **Enforcement**: a CI-conclusion fixture matrix (stale→neutral on the advisory surface; disagreement→red on the
  required gate; infra→classified) + a **required-vs-advisory surface separation assertion** (RS-017) + a
  post-ENTERED health fixture (stale → `health=refresh-required`, `status`/`lifecycle` unchanged, INV-036 not
  tripped) + an append-only versioned-path + pointer test + **a half-applied-refresh (dangling-pointer) fixture for
  both pointer families → fail-closed** (RS-029) + the `transition_context × entry_integrity × health-severity`
  cross-product (RS-019).
- **Guards against**: AP-005, AP-016, AP-017, AP-001
- **Test approach**: integration

### INV-029: The entry receipt is activated by a self-reference-safe two-step sign→activate protocol [integration]
- **Type**: must
- **Category**: security
- **Boundary**: TB-006, TB-007
- **Statement**: a receipt cannot both bind "the entry commit" and be contained in it, so entry uses the same
  **two-step** pattern as P3: **(A)** a main commit `X` has `P1∧P2∧P3` (`status=READY`, set by the prior P2 evidence activation — `precondition-reactivation` k=2)
  while still **pre-entry** — either **implicit-`BLOCKED` on schema v1** (the real case; v1 has no `lifecycle` field)
  or explicit **v2-`BLOCKED`**; **(B)** **trusted main CI signs a Phase-0.1-entry receipt for `X`** (binding `X` +
  the three evidence digests); **(C)** a **tightly-constrained activation PR** (the PRH-007 **phase-entry** class)
  commits that receipt and performs the **atomic entry transition** — **from v1**: `schema_version:1→2` + **add**
  `lifecycle:ENTERED` + **set** `entry_evidence_pointer`; **from v2-BLOCKED**: `lifecycle:BLOCKED→ENTERED` + **set**
  `entry_evidence_pointer` (an activation-only parsed-span diff against the validated protected-main merge-base,
  `status`/`ready_predicate`/`preconditions` unchanged); **(D)** the gate
  verifies the entry receipt (INV-030), that `X` is an ancestor of HEAD, and the evidence digests **against the
  historical snapshot at `X`** (INV-026 — never current HEAD), plus the activation-only diff. The entry commit
  `X` does **not** contain the entry receipt (no self-reference). Built + fixture-tested here; the live protocol
  cannot complete until P2 lands.
- **Violated when**: the entry receipt binds a tree that contains it; `lifecycle` flips to ENTERED without a
  verifying entry receipt for an ancestor commit; or the activation PR exceeds the activation-only diff.
- **Enforcement**: gate precondition — **activation acceptance and a successful ENTERED-state gate verdict both
  require a from-clean-verifying entry receipt for an ancestor `X`** (whereas `effective_lifecycle` / the src/ ban
  intentionally **survives** later `entry_integrity=unavailable`, INV-026/027 — a transient outage degrades the
  verdict, it does not un-enter) + the activation-only parsed-span diff + a self-reference negative fixture.
- **Guards against**: AP-021 (self-reference / circular gate), AP-016
- **Test approach**: integration

### INV-030: The entry receipt has its own independently-typed identity contract [integration]
- **Type**: must
- **Category**: security
- **Boundary**: TB-007
- **Statement**: the Phase-0.1-entry receipt is specified with the **same rigor as the P3 receipt**, and its
  identity contract is pinned independently: its own **predicate type + schema** (distinct from the determinism
  predicate — its subject is the **entry commit `X` + the three precondition evidence digests**, not a run
  receipt); the **signing workflow + certificate identity** (event = protected-main `push`, `run_attempt==1`,
  exact cert `workflow-sha`/repo/ref/trigger, no PR events); its **subject manifest**; and whether it **shares
  the P3 signer or uses a distinct trusted workflow** (a decision pinned in PR2). It **reuses the generic
  `Corrected.Provenance` Statement/subject/DSSE-envelope/identity verification contracts** (INV-022) but keeps
  its **entry predicate + policy independently typed** (release provenance's non-recursion, PRH-010, still holds).
  **The "three evidence digests" have a CANONICAL artifact graph (RS-024) — not left to an implementation to hash
  reference strings:** P1/P2/P3 each have a **multi-file evidence closure**, so the entry subject pins **exact
  subject cardinality, subject names, digest algorithm(s), canonical byte encoding, ordering, and a set-equal
  digest MANIFEST for each precondition's FULL evidence closure** (not the readiness-reference strings or the
  active pointers) + the **commit-`X` representation** — so a builder that hashed only pointer strings (whose own
  builder+verifier fixtures would agree) is a schema violation, not a passing implementation. **Because the entry
  receipt may share the P3 signer identity, distinctness cannot rest on the predicate-type URI alone (RS-024):** a
  **bidirectional predicate cross-rejection** is required — a genuine **P3 bundle presented to the entry verifier
  MUST reject** (wrong predicate type) and a genuine **entry bundle presented to the P3 probe MUST reject** — so a
  replay of one genuine attestation into the other's gate fails; a **distinct signer workflow/identity is
  preferred** so identity alone also separates them.
- **Violated when**: the entry receipt reuses the determinism predicate schema; its signer identity/event/attempt
  rules are unpinned; **its "three evidence digests" lack a canonical graph (cardinality/names/algorithm/ordering/
  full-closure manifest undefined) so an impl can hash reference strings** (RS-024); **the bidirectional
  cross-rejection (P3-bundle→entry-verifier, entry-bundle→P3-probe) is not tested** (RS-024); or its verification
  borrows P3's subject/staleness semantics rather than its own.
- **Enforcement**: an entry-predicate schema test (exact subject cardinality/names/algorithm/ordering + the
  per-precondition full-closure digest manifest) + an entry-receipt verify test (own identity policy) reusing the
  generic envelope contracts + a distinct-predicate assertion + **a bidirectional predicate cross-rejection test**
  + **the RS-006 two honest negatives applied to the entry verifier** — (2a) a fixture-identity entry bundle through
  the production entry argv → `identity-mismatch`, and (2b) the entry cert-SHA↔`X` cross-check under a
  fixture-accepting entry policy (receipt `X` ≠ cert SHA → the SHA-cross-check reason) — since the production-argv
  entry-accept path, like P3's, is unexercisable until P2 (RS-011 residual-ledger entry).
- **Guards against**: AP-004, AP-014
- **Test approach**: integration

## Prohibitions

### PRH-001: Insecure or over-broad cosign verify flags
- **Statement**: never `--check-claims=false`, `--insecure-ignore-tlog`, `--insecure-ignore-sct`,
  `--certificate-identity-regexp`, or `--certificate-oidc-issuer-regexp`; identity, issuer, and the workflow cert
  constraints pinned **exact**.
- **Detection**: a scan of the frozen argv + a negative test.
- **Consequence**: envelope-only verification / ignored transparency log / an over-broad identity matches an unintended signer.

### PRH-002: A bare committed claim satisfies P3
- **Statement**: never `satisfied:true` from a committed JSON claim lacking a cryptographically-verified bundle (RS-RT-13).
- **Detection**: a bare `ran-passed` JSON (no bundle) → `evidence-absent`.
- **Consequence**: commit-access forgery unblocks production `src/`.

### PRH-003: Local environment identity leaks into Corrected-authored evidence fields
- **Statement**: the **Corrected-authored** receipt + predicate fields contain **no** local hostname, username,
  or home/temp/absolute local path. The Sigstore bundle's **public** GitHub/Sigstore issuer/repo/workflow/cert
  identities + service hostnames are **explicitly exempt**.
- **Detection**: a scan restricted to Corrected receipt/predicate fields (not the opaque bundle).
- **Consequence**: a public repo leaks local environment identity.

### PRH-004: A mechanism PR (PR1 or PR2) flips P3 → true
- **Statement**: `P3.satisfied` is edited **only** inside a **typed PRH-007 class** — never an "ordinary" edit:
  `false→true` requires the **`precondition-reactivation`** class (k=3, with a from-clean-re-verified signed bundle
  + the active-baseline pointer; this covers both PR3's first activation and any post-invalidation re-mint — round-10);
  `true→false` requires the **`precondition-invalidation`** class (k=3, no bundle). Both may move `status` in lockstep
  (`status`-follows-preconditions, PRH-007). A **mechanism PR (PR1/PR2, or any mechanism-change class) never touches
  `satisfied`/`status`** (mechanism-change and evidence are mutually exclusive, PRH-007), and no PR outside these
  typed classes edits `satisfied` in either direction.
- **Detection**: the readiness-block value in PR1/PR2 diffs + the gate + the PRH-007 classifier (`false→true` legal
  only in `precondition-reactivation`; `true→false` legal only in `precondition-invalidation`).
- **Consequence**: P3 satisfied with no committed verifiable evidence.

### PRH-005: A disagreeing or reran result is signed / retried into green
- **Statement**: no attestation for `comparison_status=different`, `execution_status!=completed`, or
  `GITHUB_RUN_ATTEMPT!=1`; no retry/`continue-on-error`; a flap requires a new reviewed commit.
- **Detection**: signer guards + a workflow retry-construct scan + INV-003/008 negatives.
- **Consequence**: a real disagreement is masked and a false attestation minted.

### PRH-006: Unpinned/ranged/latest cosign, ambient trust root, or a cosign-verifies-cosign bootstrap
- **Statement**: exactly-one pinned cosign version+digest, hard-coded-SHA bootstrap, pinned trust root; never latest/range/ambient.
- **Detection**: a provisioning/workflow scan.
- **Consequence**: a compromised/newer cosign or swapped root subverts verification.

### PRH-007: A controlled-field PR changes anything beyond its typed classification
- **Statement**: the classifier is **TOTAL over every PR that touches any controlled path/field** (the
  `satisfied`/`lifecycle`/pointer fields, `test/attestations/**`, or any frozen-mechanism path), and assigns
  **exactly one** class (B1 fix — RS-027 + the user's round-7 re-review: the earlier "four activation modes" was
  self-blocking — it forbade its own pre-entry invalidation and forced PR2 / trust-root rotation / cosign upgrades
  to match zero activation modes and fail closed; **round-9 re-review found a further deadlock — the classifier is
  total over `satisfied` but the earlier classes only permitted `P3.satisfied`, so the eventual P2-landing PR
  (set `P2.satisfied:true` + add the Phase-0.0 completion-manifest evidence + `status:BLOCKED→READY`) matched NO
  class and failed closed, preventing P2 from ever landing → phase entry could never reach its required `status=READY`
  commit `X`).** Round-10 re-review then found the **inverse deadlock: once `status=READY` (post-P2-activation, all
  preconditions true, `lifecycle` implicit-BLOCKED), no class could take the block back to a recoverable state** — the
  old pre-entry-invalidation permitted `P3.satisfied:true→false` but **not** `status:READY→BLOCKED`, so the
  invalidation PR was itself rejected by the carrier (which forbids `status=READY` with any precondition re-deriving
  false, `ReadinessGate.cs:66`); and the re-mint permitted `P3.satisfied:false→true` but **not** `status:BLOCKED→READY`,
  so the block could never return to the `READY` state phase entry requires. **Fix: the invalidate/re-mint modes are
  GENERALIZED into two typed precondition classes that move `status` in LOCKSTEP with the preconditions
  (`status`-follows-preconditions), subsuming the former P3-initial-activation, P3-pre-entry-re-mint, and
  P2-activation rows.** The classes are now: **two pre-entry evidence classes — precondition-reactivation and
  precondition-invalidation** — plus the post-entry **P3-refresh**, the **phase-entry** atomic transition, and the
  **mechanism-change** class; **evidence-activation and mechanism-change are MUTUALLY EXCLUSIVE** (one PR can never be
  both). Each is enforced at the **parsed field/span** level against the **validated protected-`main` merge-base of
  the PR** (a filename allowlist is insufficient — the readiness doc shares its path with many normative contracts)
  and **forbids every field/path outside its own allowlist**:

  | Class | Regime | Permitted changes (and NOTHING else) |
  |---|---|---|
  | **Evidence: precondition-reactivation** (round-10; subsumes P3-initial-activation/PR3, P3-pre-entry-re-mint, P2-activation) | pre-entry (`lifecycle=BLOCKED`) | for **exactly one** precondition `Pk` (k∈{2,3} — see the P1-scope note below): `Pk.satisfied:false→true` **AND `Pk.evidence: null → <registered reference>`** (the readiness-block evidence field MUST become non-null, else the block re-derives the `satisfied:true`-without-evidence Fail, `ReadinessGate.cs:96`) **AND set the external active reference in the SAME PR** — **P3**: a new **versioned** signed receipt/bundle + **set** the active-baseline pointer file that `P3.evidence` names; **P2**: the Phase-0.0 completion manifest + set the file that `P2.evidence` names (*P2-feature deliverable*) — a non-null `Pk.evidence` whose file is absent is `Unresolvable` → hard Fail (`ReadinessGate.cs:110`), so the block field and the on-disk reference move together; historical **versioned** evidence is preserved, only the active reference is (re)set — **and** `status:BLOCKED→READY` **iff `P1∧P2∧P3` now ALL re-derive true** (else `status` stays BLOCKED — e.g. PR3 activates P3 while P2 is still false, so `status` does not move). No `lifecycle`/`schema_version`/**other**-precondition/mechanism change |
  | **Evidence: precondition-invalidation** (round-10; subsumes the P3 pre-entry-invalidation; no bundle) | pre-entry (`lifecycle=BLOCKED`) | for **exactly one** precondition `Pk` (k∈{2,3}): `Pk.satisfied:true→false` **AND `Pk.evidence: <registered reference> → null`** (the readiness-block evidence field MUST become null) **AND retire the external active reference in the SAME PR** (delete/unpublish the P3 active-baseline pointer file / the P2 evidence file that `Pk.evidence` named) — leaving `Pk.evidence` non-null after deleting its file yields a dangling `Unresolvable` reference that hard-Fails **regardless of the declared `satisfied:false`** (`ReadinessGate.cs:110`), so the invalidation PR would itself fail closed; historical **versioned** evidence is preserved, only the active reference is retired — **and** `status:READY→BLOCKED` **iff the block was `READY`** (`status` MUST follow, else the carrier rejects the inconsistent `READY`+precondition-false). No new bundle, no `lifecycle`/`schema_version`/**other**-precondition/mechanism change |
  | **Evidence: P3-refresh** (post-entry, INV-028) | post-entry (`lifecycle=ENTERED`) | a new **versioned** P3 receipt/bundle; **move** the active-baseline pointer **only** (never `satisfied`/`status`) |
  | **Evidence: phase-entry** (INV-029) | pre-entry → ENTERED | a new **versioned** entry receipt/bundle; the **atomic entry transition** — **from v1**: `schema_version:1→2` + **add** `lifecycle:ENTERED`; **from v2-BLOCKED**: `lifecycle:BLOCKED→ENTERED`; and **set** `entry_evidence_pointer`. `status`/`ready_predicate`/`preconditions` unchanged (round-8: the schema bump is now IN the allowlist, else no class could carry the real v1 block to ENTERED) |
  | **Mechanism-change** (PR2, **the P2 validator/probe**, trust-root rotation, cosign/pin/schema/verifier/classifier/workflow upgrades) | any | frozen-mechanism paths + gate code — and **MUST NOT touch any evidence field** (`satisfied`/`status`/`lifecycle`/either pointer/`test/attestations/**` evidence). The legitimate home for PR2 / the P2 validator / rotation / upgrade so they no longer fail closed |

  **Mode declaration = TRUSTED PR METADATA, not a committed marker (B1 fix — a committed marker is itself a
  controlled field that would need its own recursive carve-out):** the class is declared by a **PR label supplied
  in the trusted GitHub Actions PR-event context** (set through the reviewer/CODEOWNERS-adjacent controls, EA-006),
  and the classifier **cross-checks the declared label against the observed parsed-span diff** — the diff is the
  authority, so a label claiming `P3-refresh` on a diff that flips `satisfied`, or a `mechanism-change` label on a
  diff touching `test/attestations/**`, **fails closed**. (If a committed marker is ever used instead of a label, it
  lives at one **explicitly-allowlisted path that every class permits** — but the label form is preferred.) Because
  the classification is keyed on the **trusted PR event**, it is a **CI-side check**; the from-clean local gate does
  **not** classify PR intent (it verifies committed evidence — INV-010/020 — not a diff).
  **Total classification + fail-closed (RS-027):** a controlled-field PR matching **zero** classes (an untyped touch
  of a controlled field) **OR more than one** class **fails closed** (never defaults to the most permissive); any
  touch outside the assigned class's allowlist fails closed. The gate **renders** `PR class = X; permitted spans =
  …; observed changes = …` for the reviewer.
  **Precondition class scope + the delegated P2 landing sequence (round-9/10):** this P3 feature **concretely wires
  the P3 (k=3) reactivation/invalidation evidence** (the signed cosign bundle + the active-baseline pointer) and
  **reserves the generic class contract** so P2 fits the same shape — but the **Phase-0.0 completion manifest
  schema and the P2 validator/probe are OUT of scope** (the future P2 feature owns `P2.evidence`, the manifest
  content, and making the P2 probe real). **The generalized precondition-reactivation/-invalidation classes are
  scoped to k∈{2,3} (P2, P3) ONLY (round-11): P1 is NOT one of these classes — `P1.satisfied`/`P1.evidence` are
  governed by P1's own already-landed Stage-B migration contract (`phase-0-1-worker.md`, the committed-tree-migrated
  probe), never by a pre-entry evidence class, so the `{P2,P3}` cross-product in Detection is the COMPLETE controlled
  precondition set for this feature.** Until the P2 probe is real, `P2.satisfied` is additionally protected by the
  gate's re-derivation (PAT-005 — the current `P2Probe` is `validator-deferred`, so a declared `P2.satisfied:true`
  re-derives false → `status` cannot legally become READY regardless of the flag). **The delegated P2 landing
  sequence is EXPLICIT (round-10, corrected round-12).** Round-9 jumped straight to P2 evidence activation while the
  P2 probe was still deferred (so `status:BLOCKED→READY` would fail the re-derivation guard); round-10 then led with
  the P2 mechanism PR — **but that PR makes `P2Probe.Evaluate` real by editing `gate/Corrected.Gate/Probes.cs`, the
  SHARED file that also holds `P3Probe`, which is inside the P3 subject-manifest verifier surface (INV-018)**. With P3
  still declared true, that edit **STALES the committed P3 baseline** (P3 re-derives false while declared true →
  `CellFails` declared≠actual mismatch), and `mechanism-change` may **not** touch P3 evidence to re-mint it — so the
  mechanism PR is rejected before it can merge. **P3 must therefore be INVALIDATED before the staling mechanism PR.**
  The correct **7-step** order (round-12):
  1. **P3 precondition-invalidation** (`precondition-invalidation`, k=3) — `P3.satisfied:true→false` + retire the P3
     evidence (`P3.evidence`→null **and** delete the active-baseline file); `status` stays BLOCKED (already BLOCKED — P2 false).
  2. **P2 mechanism/validator lands under `mechanism-change`** — makes `P2Probe` real (edits the shared `Probes.cs`);
     **changes NO readiness evidence** — and P3 is already false with no active reference, so nothing stales.
  3. **Main produces a NEW P3 observation** — a fresh determinism run over the post-P2 tree mints a new signed
     attestation over the updated manifest (which now reflects the real-`P2Probe` `Probes.cs` digest).
  4. **P3 precondition-reactivation** (`precondition-reactivation`, k=3) — `P3.satisfied:false→true` + the new
     versioned bundle + set the active-baseline pointer (field + file together); `status` stays BLOCKED (P2 still false).
  5. **P2 evidence activation** (`precondition-reactivation`, k=2) — `P2.satisfied:false→true` + the completion
     manifest + set `P2.evidence` + `status:BLOCKED→READY` at a commit `X` (the real P2 probe now re-derives true).
  6. **The entry receipt is minted** — trusted main CI signs the Phase-0.1-entry receipt for `X`.
  7. **Phase entry lands** (`phase-entry`) on a descendant of `X` — the atomic `lifecycle:BLOCKED→ENTERED` +
     `schema_version:1→2`, `status`/`preconditions` unchanged (which is why the status-moving step 5 must precede it).
  The full cross-feature order is: **P3-activation (this feature, `status` stays BLOCKED because P2 false) → steps 1–7
  above.** For **step 5 not to itself stale P3**, the **P2 completion-manifest and P2 active-reference paths are EXACT
  INV-018 exclusions** from the P3 subject classifier (named in INV-018) — otherwise adding them would perturb the P3
  manifest and the exactly-one-precondition rule would forbid restoring P3 and activating P2 in one PR. If P3 (or P2)
  goes stale at any other pre-entry point, the `precondition-invalidation` / `precondition-reactivation` classes
  recover it while keeping `status` consistent — including from `status=READY`. (**Root cause**: `P2Probe` and `P3Probe`
  share one file, so a P2-only code change touches the P3 verifier surface; a future P2 feature MAY instead split
  `P2Probe` into its own unit excluded from the P3 manifest, which would remove the invalidate-first steps 1/3/4 — but
  the reorder + exclusion is the in-contract fix that needs no restructuring of this feature's `Probes.cs`.)
  **Authenticated base/head (RS-027):** the diff is computed against **validated** base/head parameters **proven to
  be the protected-`main` merge-base of the PR** — never a caller-provided SHA, never a guessed `HEAD^`, and never
  the run-from-clean `HEAD^..HEAD` (which a two-commit branch — an unrelated mechanism change in commit 1 + the
  activation in commit 2 — would pass though the real merge-base diff rejects it). The signed
  `attested_commit`/entry-`X` is used **separately** for **ancestry** (ancestor-of-HEAD) and **subject-integrity**
  (the receipt binds it), **never** as the diff base. In every **evidence** class, **frozen mechanism digests**
  independently prove the cosign digest, trust root, verifier argv, identity policy, schemas, and subject-manifest
  rules did not change, and no `src/`/mechanism code changes; the **mechanism-change** class is the *only* class
  that may change those, and it may not touch evidence.
- **Detection**: a **total** classifier over every PR touching a controlled path/field (zero-or-multi-class →
  fail-closed; evidence/mechanism mutual-exclusion enforced) + the trusted-label↔parsed-span cross-check + a
  parsed-object diff relative to the **validated protected-main merge-base** + a frozen-mechanism-digest equality
  check (in evidence classes) + a path check for the versioned evidence files + the reviewer-facing class render +
  **a `status`-follows-preconditions CROSS-PRODUCT test (round-10): `{status: BLOCKED, READY} × {precondition: P2, P3}
  × {invalidate, restore}`** (the `{P2,P3}` set is COMPLETE — P1 is governed by its own Stage-B contract, round-11),
  asserting each cell is a legal precondition-invalidation/reactivation diff that keeps
  `status` consistent with the re-derived preconditions (invalidate from READY ⇒ `status→BLOCKED`; restore ⇒
  `status→READY` iff all re-derive true), that the **`Pk.evidence` block field moves WITH the external reference in
  the same diff** (reactivate ⇒ `evidence` non-null **and** its reference resolves; invalidate ⇒ `evidence` null
  **and** no reference file remains), and that the carrier (`ReadinessGate.cs`) accepts the resulting block — plus a
  **negative cell (round-11): an invalidation that deletes the external reference file but leaves `Pk.evidence`
  non-null is REJECTED** (dangling `Unresolvable` → hard Fail, `ReadinessGate.cs:110`), proving the
  block-field↔reference coupling is ENFORCED, not merely documented — so the recovery cycle is bootstrappable from
  every pre-entry state, including `status=READY`.
- **Consequence**: trust policy or a sibling normative contract changes under an "evidence/activation" label; a
  class permits fields belonging to another class; a legitimate mechanism / rotation / **precondition-invalidation /
  -reactivation** PR fails closed (round-9: no P2 class → P2 can never land, phase entry never reaches its required
  `status=READY` commit; round-10: the invalidate/re-mint classes cannot move `status`, so recovery from
  `status=READY` deadlocks); an untyped/multi-typed PR routes a controlled-field change around the check; a PR is
  both a mechanism change and an evidence flip; a class moves `status` **out of lockstep** with the re-derived
  preconditions; a class moves `Pk.evidence` **out of lockstep** with its external reference (a dangling
  `Unresolvable` reference that fails closed); or the diff base is attacker-chosen / guessed / polluted.

### PRH-008: The determinism producer holds OIDC signing privilege, or the signer runs repository code
- **Statement**: the producer has no `id-token: write`; the signer runs no spike/test/dependency-restore code beyond a frozen statement surface.
- **Detection**: per-job permissions + the signer's job body.
- **Consequence**: compromised test code / a build dependency requests the OIDC token.

### PRH-009: Signing on a PR event or a non-attempt-1 run
- **Statement**: signing runs only on a protected-main `push` at `run_attempt==1`; never `pull_request`/`pull_request_target`, never a rerun.
- **Detection**: the workflow event + `run_attempt` guards.
- **Consequence**: an untrusted PR context or a rerun mints an attestation.

### PRH-010: A shipped `src/Corrected.*` binary references Corrected.Provenance
- **Statement**: `gate/Corrected.Provenance` is **non-shipped**; no shipped `src/Corrected.{Core,DafnyAdapter,Cli}`
  project references or links it. Reuse by parent INV-031/032/033 is at the **schema/contract/fixture** level, a
  production reimplementation of the same contract — **not** a dependency on the gate project (preserving the
  non-recursive bootstrap, INV-033).
- **Detection**: a project-reference / compilation-closure scan (the INV-011-style shipped-closure check) for any src→gate/Corrected.Provenance edge.
- **Consequence**: the shipped worker recursively depends on gate provenance machinery, breaking non-recursion.

## Boundary Conditions

### BND-001: Committed attestation + bundle → P3 verdict (TB-006)
- **Boundary**: TB-006 · **Input from**: a commit-access principal (within the narrowed threat model, INV-025)
- **Validation required**: full crypto verify + decoded-payload equality + cert-SHA↔attested-commit + typed-status + non-stale manifest + ancestry (INV-010/011/012/018/019)
- **Failure mode**: fail-closed (typed `rejected`/`unavailable`)

### BND-002: Keyless-OIDC signing identity + trust root → the bundle (TB-007)
- **Boundary**: TB-007 · **Input from**: the GitHub OIDC issuer + Fulcio/Rekor/TSA at signing time
- **Validation required**: exact issuer + workflow cert constraints; signed-timestamp anchoring; two-job isolation; attempt==1; protected-main push; manifest re-check at attested_commit (INV-007/008/009/011)
- **Failure mode**: fail-closed — no valid attestation → P3 false

### BND-003: cosign binary + trust root intake → verification tooling (TB-004)
- **Boundary**: TB-004 · **Input from**: the cosign release + the Sigstore trust root
- **Validation required**: exactly-one version+per-RID digest; hard-coded-SHA bootstrap; versioned append-only root; outside ambient discovery (INV-015/016)
- **Failure mode**: fail-closed — unpinned/unverified aborts

### BND-004: The cosign subprocess seam (TB-004)
- **Boundary**: TB-004 (process seam) · **Input from**: cosign stdout/stderr/exit + the input files
- **Validation required**: absolute pinned path, argv array, clean env, no-symlink, size caps, timeout + tree-kill, exact exit-code taxonomy, no response-file injection (INV-014)
- **Failure mode**: fail-closed — a hung/oversized/symlinked/ambient case → typed error → false

## STRIDE Analysis

### STRIDE for TB-006: committed attestation → gate verdict
- **Spoofing**: forged `ran-passed` / swapped bundle → decoded-payload equality + exact identity + cert-SHA↔commit (INV-010/011); bare claim rejected (PRH-002).
- **Tampering**: mutated receipt → `--check-claims` + decoded-Statement equality break; a changed subject file changes the manifest digest → stale (INV-018/019).
- **Repudiation**: keyless cert SAN + workflow-SHA OID bind signer + source commit; Rekor logs it.
- **Info disclosure**: local identity in Corrected fields → PRH-003 (bundle public identities exempt).
- **DoS**: malformed bundle → bounded (size caps + timeout + tree-kill, INV-014), fail-closed.
- **Elevation of privilege**: flip P3 → from-clean-verifying, non-self-referential, non-stale, ancestor-bound baseline only (INV-012/018/019/020); mechanism PRs can't flip (PRH-004); narrowed adversary (INV-025).

### STRIDE for TB-007: trusted-CI signing / evidence identity
- **Spoofing**: an unintended signer → exact identity + issuer + workflow-SHA/repo/ref/trigger (INV-011); two-job isolation (INV-007); PR event refused (PRH-009).
- **Tampering**: a compromised trust root → pinned versioned root (INV-016/PRH-006).
- **Repudiation**: cert expiry → signed timestamps anchor signing-time validity (INV-009).
- **DoS**: signing outage → no mint; signing outcome `failed`/`not_attempted`; P3 false (EA-001).
- **Elevation of privilege**: retry-until-green → `run_attempt==1` (INV-008); producer holds no OIDC (PRH-008).

### STRIDE for TB-004: inbound cosign toolchain
- **Spoofing**: trojaned binary → exactly-one-version + per-RID digest + hard-coded-SHA bootstrap (INV-015).
- **Tampering**: injected switch → argv array, clean env, no response file (INV-014).
- **DoS**: cosign unavailable → typed `verifier-unavailable` → fail-closed; the integration layer requires cosign (AP-013).
- **Elevation of privilege**: an older vulnerable cosign → version-floor over both advisories (INV-015).

## Environment Assumptions
- **EA-001**: signing CI has `id-token: write` (signer job only) + network to the OIDC issuer + Fulcio/Rekor/TSA **at signing time**. — Wrong → no mint; P3 false.
- **EA-002**: the verify environment has the digest-pinned cosign + pinned trust root; verification is **offline** (executably enforced, INV-017). — Wrong → typed `verifier-unavailable`/`trust-root-or-pin-mismatch` → false.
- **EA-003**: RID `linux-x64` (spike EA-002); ProcessorCount observed; a different RID needs a re-mint. **The real-cosign gate path (INV-010/014) requires a `linux-x64` host** — the pinned cosign binary is per-RID (INV-015) and `linux-x64`-only today, while `gate/run-readiness-gate.sh` is the project's cross-platform `commands.test`. On a non-`linux-x64` host (macOS/arm64) the P3 verify path **must record an honest, typed `rid-platform-mismatch`/`verifier-unavailable`** — **never a silent skip** (AP-013): the gate stays runnable and honest off-RID, it does not claim a green P3 it could not verify. — Wrong (off-RID silent skip) → the gate that is the project's sole reliable green signal is dishonest off `linux-x64` (RS-015). (Adding per-RID cosign digests for other supported dev RIDs is the alternative to the linux-x64 restriction.)
- **EA-004**: the exact cosign version + bundle format + frozen argv are settled by the PR2 transcript spike. — Wrong → PR2 cannot freeze / is not landable.
- **EA-005**: the floor is set by the PR1 **commit-anchored** measurement campaign; the platform is pinned + recorded. — Wrong → an unstable floor / unreproducible platform.
- **EA-006** (external, out-of-band): branch protection on `main`, required checks, and CODEOWNERS/required-review over the **complete** set of security-load-bearing surfaces (RS-012 corrected an inverted risk-weighting — the earlier list protected the cosign pin but not the field that lifts the ban): `gate/**`, `gate/Corrected.Provenance/**`, the signing workflow, the trust root, the cosign pin, **AND** — newly added — **`.correctless/specs/phase-0-1-worker.md`** (at minimum the readiness-block `lifecycle` / `preconditions` / `satisfied` spans — the `lifecycle` field is what lifts the `src/` ban under INV-027, so it is *more* load-bearing than the cosign pin), **`test/attestations/**`** (the committed receipts/bundles and **every pointer file** — the active-baseline pointer + `entry_evidence_pointer` *select* which bundle each verifier consumes), the **resource-floor constant + runner-escalation policy** (INV-004 — an unreviewed floor raise silently suppresses the determinism observation, RS-040), and the **spike determinism producer + projection surface** under `spikes/dafny-compat/**` (the code whose recorded projection facts verification trusts, RS-005). The boundary of the narrowed threat model (INV-025) — **not** a cryptographic guarantee. — Wrong → the protection assumption does not hold.
- **EA-007** (RS-013 — full git history / fetch-depth): every ancestry check (`attested_commit`/entry-`X` ancestor-of-HEAD, `plan_commit` ancestor-of-`head_sha`, INV-004/012/019/020/029) **and** every historical-snapshot read of the evidence blobs *at* `X` (INV-026) requires the gate/signer/from-clean-CI checkout to carry **full history (`fetch-depth: 0`)**, not the shallow (`fetch-depth: 1`) default of `actions/checkout`. This is enforced **executably** (a preflight asserting `fetch-depth: 0` / that `X` and its blobs are locally reachable — INV-017-style, not prose), and an **uncomputable** ancestry maps strictly to `rejected` (fail-closed), **never `unavailable`** (INV-012), so a shallow clone cannot degrade into the non-failing `unavailable` class that arms the RS-001/RS-002 bypass. — Wrong (shallow) → ancestry/historical reads error → `rejected`/preflight-fail, never a silent pass; the live entry transition (INV-029) cannot complete.
- **EA-008** (RS-014 — from-clean ≠ offline; provisioning network): a fresh clone is **not** offline until the **online provisioning phase** (`provision-cosign.sh`, INV-017) has fetched + digest-validated the per-RID cosign binary + the pinned TUF `trusted_root.json`; that phase needs network to the pinned release + TUF mirror. "Offline" (INV-010/017) applies **only to the verify phase**. The provisioning phase is invoked by a documented gate pre-step **and** the from-clean CI job **before** the offline verify (RS-014); a from-clean assertion proves the real cosign path executed. — Wrong (air-gapped host with no vendored binary) → provisioning cannot complete → typed `verifier-unavailable`, fail-closed (never a phantom green). (Vendoring the per-RID binary + root into the repo is the air-gap alternative.)
- **EA-009** (RS-029 — verifier clock + trust-root anchor durability): offline verify-later soundness (INV-009) relies on signed-timestamp anchoring; it additionally assumes the **verifier host clock is sane** and that the **pinned historical `trusted_root.json`'s own Fulcio/Rekor/TSA anchors have not passed their validity** over the (indefinite) lifetime the gate re-verifies committed bundles from clean. INV-016's rotation protocol must cover anchor **expiry**, not only key rotation. — Wrong (badly-skewed clock or an expired pinned anchor) → `verify-blob-attestation` fails closed on a commit whose evidence never changed (a spurious pre-entry re-BLOCK / a post-entry `evidence-integrity-rejected` health finding).
- **EA-010** (RS-032 + pinned-image lifetime): (a) producer and signer are two jobs in the **same** workflow run using the **same-run `@actions/artifact` runtime-token transfer** (no REST/cross-run download) — this is what makes INV-007's `id-token:write`+`contents:read` minimal set provably sufficient without `actions: read`; and (b) **(RS-031)** the producer records **exactly `${{ github.sha }}`** (checks out the pinned trigger SHA, never a branch ref), so the cert's source-repo-digest OID equals the recorded `attested_commit` (INV-011). The **pinned non-floating OS LABEL** (e.g. `ubuntu-24.04`, not `ubuntu-latest`; INV-005's contract — a pinned label + the *recorded* actual image/version, **not** a digest-pinned container unless exact reproducibility is later required) is re-pinned via the frozen-mechanism update path (INV-016) before GitHub retires the labeled image. — Wrong → a cross-run/REST hand-off 403s (needs `actions:read`, forbidden by INV-007); or a second push between trigger and checkout makes `attested_commit` ≠ the cert workflow-SHA → INV-011 rejects a legitimate run; or a retired labeled image hard-fails the refresh lane (`infrastructure-invalid`).

## Design Decisions (resolved)
- **DD-001 (verify path)**: digest-pinned cosign CLI (not `Sigstore.Net`). (User 2026-07-27.)
- **DD-002 (cosign object model)**: `attest-blob --statement` (Corrected owns Statement semantics); verify with `verify-blob-attestation --check-claims=true` + exact identity/issuer/workflow-SHA + `--use-signed-timestamps` + pinned `--trusted-root`; the **semantic** check decodes the signed DSSE Statement and byte-compares it to the reconstructed one. Exact version + argv + bundle format frozen by the PR2 transcript spike. **Bundle-format contingency (RS-008):** if the transcript spike shows the pinned version's **new protobuf bundle format cannot be offline-verified** (the research brief's Open Question #1, UNCONFIRMED at v3.1.x), the signer uses **`--new-bundle-format=false`** (old format embeds the Rekor SET for offline verify) — a pinned decision recorded here, not deferred to OQ-001. **Pointer shape (RS-025):** the DD-002 `P3AttestationPath` constant `test/attestations/inv010-determinism.json` is **retained as the active-baseline POINTER**; versioned receipts live under `test/attestations/inv010/<commit>/` (append-only). This resolves the earlier internal contradiction (the value is preserved *as a pointer*, not dropped) and names `Inv009And010ProbesTests.cs` + the `Probes.cs` const as migration sites.
- **DD-003 (landing)**: **three** PRs — counted runner + campaign / **frozen** provenance mechanism / evidence-only activation. Resolves carrier **OQ-A#3**. (User 2026-07-27.)
- **DD-004 (P3 semantics)**: a **committed capability baseline** bound to a versioned determinism-subject manifest (digest bound into the signed receipt) + a **live** determinism job on every relevant change + a **bounded parent INV-005 amendment mapping every orthogonal outcome**. (User 2026-07-27.)
- **DD-005 (provenance home)**: `gate/Corrected.Provenance` as a **5th, non-shipped gate project + shared CONTRACT** for INV-031/032/033, with the full exact-four→five migration; never referenced by a shipped `src/` binary (PRH-010). (User 2026-07-27.)
- **DD-006 (trust boundary)**: register **TB-007**; TB-003 unchanged. (User 2026-07-27.)
- **DD-007 (status model)**: orthogonal `execution/comparison` status in the signed subject; signing outcome + probe result outside it; `ran-passed` probe-derived; a disagreement is "projections differed in this observation", not "proven nondeterminism" or universal determinism.
- **DD-008 (signer identity)**: a **direct** signing workflow is the chosen contract (simplest; `workflow_sha` is the run's commit); exact GitHub cert constraints incl. `--certificate-github-workflow-sha` cross-checked to `attested_commit`. A reusable workflow is allowed only if the PR2 transcript demonstrates exactly which claim each flag checks (`workflow_sha` vs reusable-only `job_workflow_sha`).
- **DD-009 (cosign pin)**: exactly one version + per-RID digest; hard-coded-SHA bootstrap (non-circular); advisories cited correctly (GHSA-w6c6-c85g-mmv6 → v3.0.6; GHSA-whqx-f9j3-ch6m → v3.0.4).
- **DD-010 (intensity)**: critical. (User 2026-07-27.)
- **DD-011 (readiness lifecycle)**: build the **phase-entry latch fully in-feature** (Group G) — the carrier
  readiness kernel gains a monotonic `BLOCKED→ENTERED` latch (**COMPLETE reserved**, schema v2), realized by
  **four components** (pure transition evaluator / main-branch entry producer-signer / gate-side receipt verifier
  / activation-diff validator — INV-026); the pure kernel **proposes** and never mints/persists. Parent
  **INV-036** is amended to scope the ban by `effective_lifecycle != ENTERED`; post-entry determinism is a
  non-blocking health check + append-only refresh protocol. Resolves the Blocking-1 post-entry deadlock. Built +
  fixture-tested here (the live entry transition awaits P2). (User chose the build-fully-here option 2026-07-27,
  over the bounded direction-only and the merge-queue alternatives.)
- **DD-012 (Group G scope reaffirmed post-review-7)**: the /creview-spec + gpt-5.6-xhigh pass flagged (RS-011) that
  Group G's entry-signing mechanism (INV-029/030) is a **second full attestation pipeline unexercisable through the
  real orchestrator until P2 lands** (the real `P2Probe` is `validator-deferred`), i.e. built-frozen-and-fixture-only
  (AP-002/AP-013 at scale). **User reaffirmed KEEPING all of Group G in-feature** (2026-07-27) rather than deferring
  INV-029/030. Accepted residuals, recorded rather than waived: (a) the production-identity **entry-accept** branch
  is not positively driven until P2 — mitigated by the RS-006 PROD-ARGV reason-specific negatives applied to the
  entry verifier (INV-030) so an always-accept/always-reject entry verifier cannot ship green; (b) a
  residual-trust-ledger entry records the unexercisable accept path. No entry-signing deferral.
- **DD-013 (new pattern registration — deferred to `/cupdate-arch`, RS-038)**: register **PAT-006** ("pure kernel
  *proposes*; impure orchestrator *revalidates + persists*") + the **append-only versioned-evidence-with-active-pointer**
  convention when this feature's `/cupdate-arch` runs — so future features (P2 activation, INV-031/032/033 reuse)
  compose with the "kernel never mints/persists" rule (INV-026) rather than reinventing it. Not a code change here.

## Open Questions
- **OQ-001 [scheduled, HARD PR2 landing gate — RS-007/RS-008]**: the exact cosign version, bundle format, and frozen sign→offline-verify argv — a **PR2 transcript-spike deliverable** (real GitHub-OIDC sign → fresh-machine network-disabled verify), capturing exact argv, bundle media type, output shape. The spike **must positively demonstrate**: (a) a committed fixture bundle **offline-verifying AFTER the ~10-min Fulcio cert expires** (signed-timestamp attached — add a TSA if not attached by default, RS-007); and (b) whether the pinned version's **new bundle format is offline-verifiable**, else take the `--new-bundle-format=false` contingency (RS-008, pinned in DD-002). If either is disproven, INV-009 is **amended, not waived**, and PR2 cannot freeze until resolved. Blocks the PR2 freeze.
- **OQ-002 [scheduled]**: the measured stable core floor — a **PR1 measurement-campaign deliverable** (a plan committed before N retained attempt-1 runs, the **eligible run sequence predefined + verified set-equal to an authoritative listing**, RS-016). The campaign also settles the RS-009 runner-class commitment (which pinned runner reaches `completed` on the standing lane).
- **OQ-003 [cross-ref]**: parent **OQ-006** (keyless vs key-backed for the release-provenance path) — informed by this feature's keyless determinism lane; the release path stays a parent decision.
- **OQ-005 [scheduled, LOW — RS-037]**: the retention / GC policy for the append-only evidence surface (`test/attestations/inv010/<commit>/**` + the append-only trust roots) and whether a P3 **de-activation / rollback** affordance is ever needed — which historical `<commit>/` attestations + roots are safe to prune (each old bundle stays verifiable only against the exact root current at its signing, INV-016) vs a documented "keep all" with rationale. Advisory; does not block PR1/PR2/PR3.
- **OQ-004 [RESOLVED — Group G state model]**: the wire format (`{schema_version, status, ready_predicate,
  lifecycle, entry_evidence_pointer, preconditions}` — **`ready_predicate` retained**, round-8/9), the exact
  per-version field table, the derived values, the legal combinations/transitions, the atomic v1→v2 entry
  transition, and the entry-receipt identity contract are all defined by the **Group G state model + INV-026–030**.
  `COMPLETE` is reserved for a later schema version (no code here). Nothing about the lifecycle representation
  remains open to GREEN.

## Packages Affected (monorepo)
- **Spike** (`spikes/dafny-compat/**`, TB-004): `tests/SpikeTests/Inv010DeterminismTests.cs` (orthogonal status model + pure classifier + 5-role/3-kind comparison), the per-role receipt writer, the serial-lane runnable surface + charter, the recorded platform identity.
- **Test/build-gate carrier** (`gate/**`, exempt): NEW **`gate/Corrected.Provenance/**`** (non-shipped shared substrate — generic Statement/subject/envelope/identity verify contracts + the P3-specific predicate & RunReceipt schemas + cosign verify wrapper) + its `packages.lock.json`; `gate/Corrected.Gate/Probes.cs` (`P3Probe` real verifier + typed internal result + expanded typed reasons w/ the fail-closed `unclassified-verifier-fault` default + `projection-policy-mismatch`/`ancestry-uncomputable` + pinned constants incl. the retained `P3AttestationPath` pointer); **`gate/Corrected.Gate.Kernel/ReadinessBlock.cs`** (schema-v2 migration — `RecognizedSchemaVersion` int→recognized-set `{1,2}`, **`ready_predicate` retained**, version-aware presence bits (v2 `lifecycle` **required**, `entry_evidence_pointer` required iff ENTERED — not "optional"), RS-021); **the readiness lifecycle** (`gate/Corrected.Gate.Kernel/ReadinessGate.cs` — the **NEW, added-alongside** pure `BLOCKED→ENTERED` transition evaluator (3-arg), COMPLETE reserved, distinct from the retained 2-arg `EvaluateReadiness` verdict fn, RS-022; the impure `gate/Corrected.Gate/` orchestrator = gate-side entry-receipt verifier + activation-diff validator with a **validated protected-main merge-base** base/head + a **total PR class classifier** (evidence-activation modes incl. the generalized **precondition-reactivation/-invalidation** classes with `status`-follows-preconditions + the P2-activation contract, mutually-exclusive **mechanism-change**, trusted-PR-label declaration, RS-027/round-9/10); the schema-v2 wire format + atomic v1→v2 entry migration; INV-026–030) + its state-machine + purity tests (incl. the `transition_context × entry_integrity × declared_lifecycle` cross-product (RS-001) **and the `{status:BLOCKED,READY} × {P2,P3} × {invalidate,restore}` status-follows-preconditions cross-product (round-10)**); **`gate/Corrected.Gate/StatusRenderer.cs`** (per-reason actionable rendering w/ no `unclassified` fallthrough + `p3-not-yet-activated` zero-state + `{retryable|hard}` disposition + post-P3 banner naming P2 + re-pointed `validator-deferred`→P2, RS-035/036) + its no-`unclassified` totality test; a **typed signing-outcome diagnostic emitted as a workflow artifact/summary under a committed schema** (RS-034, F5 — not a committed repo file); the 3-layer fixture corpus under `gate/Corrected.Gate.Tests/fixtures/attestations/` (incl. the **PROD-ARGV reason-specific negatives** for P3 and the entry verifier, RS-006/024) + synthetic `P1∧P2∧P3` lifecycle fixtures + a **forged-declared-ENTERED** fixture (RS-004) + a **half-applied-refresh dangling-pointer** fixture (RS-029); the migrated **`gate/Corrected.Gate.Tests/Inv009And010ProbesTests.cs`** (RS-025) and **`Inv015PinnedToolchainTests.cs`** (array + `Assert.Equal(4→5)` + method rename + comments, RS-020); the determinism-subject manifest + schema + pinned classifier + the committed **role/kind→projection-policy map** (RS-005) + committed kind/role registries (RS-020); the **exact-four→five** migration (`Corrected.Gate.slnx` + comment, INV-014, INV-015 membership meta-test **and its behavioral count guard + method name + comments**, BND-002 loop).
- **CI** (`.github/workflows/**`): the two-job serial determinism lane (unprivileged producer + minimal signer; **same-run `@actions/artifact` transfer**, RS-032; producer records `${{ github.sha }}`; **committed pinned runner CLASS reaching `completed`**, RS-009; a **committed EXTRACTED lane script** the workflow invokes + a workflow↔script sync test, RS-028; isolated; pinned OS + recorded image; `run_attempt==1`; protected-main push only; SHA-pinned actions; the classifier as the acceptance trigger; **fetch-depth:0**, RS-013) + a **separate NON-required advisory health job** carrying the neutral CI conclusions (RS-017) + a **provisioning pre-step** (`provision-cosign.sh`) wired into both the documented gate command and the from-clean CI job before the offline verify (RS-014).
- **Evidence surface** (`test/attestations/**`): **versioned** P3 evidence under `test/attestations/inv010/<attested-commit>/` (the production-identity receipt + `.sigstore.json` bundle) + a small **active-baseline pointer** file (set in PR3; on refresh the **evidence history is APPENDED** — a new versioned `<commit>/` dir — while the **pointer is atomically MOVED/REPLACED** to the new baseline, round-8 wording fix: the pointer is not "appended"); the **Phase-0.1-entry receipt + bundle** + the `entry_evidence_pointer` (phase-entry activation, when P2 lands). **No fixed RECEIPT filename; the fixed POINTER remains** `inv010-determinism.json` — receipts are versioned under `inv010/<commit>/` and reached via the pointer (INV-028/PRH-007; F3 wording fix).
- **Specs / docs**: `readiness-gate-carrier.md` (INV-010 real; OQ-A#3 resolved; 133/882/1216 exact-five; the readiness-kernel phase-latch; **DD-002's pinned `P3AttestationPath = test/attestations/inv010-determinism.json` becomes the fixed active-baseline POINTER**, pointing to the versioned receipts under `test/attestations/inv010/<commit>/` — preserving the pinned constant while enabling append-only history); **`phase-0-1-worker.md`** (INV-005 amend — capability-baseline + full orthogonal remap; **INV-036 amend — production-code ban scoped to pre-`ENTERED`**); ARCHITECTURE (TB-007; exact-five incl. ARCHITECTURE:84; Corrected.Provenance non-shipped substrate + partitioned reuse contract); **`docs/features/readiness-gate-carrier.md:24`** (exact-five). Historical journal/verification records stay historical.
- **Repo root / provisioning**: the pinned cosign version+per-RID digest + hard-coded SHA-256 + versioned append-only `trusted_root.json` (a `provision-cosign.sh` mirroring `provision-z3.sh`) + CODEOWNERS over the protected paths (EA-006).
