# Spec: P3 Determinism Attestation (capability-baseline; carrier INV-010 real; OQ-A#3 discharge)

## Metadata
- **Created**: 2026-07-27T21:36:00Z
- **Status**: approved — converged after six external adversarial review rounds (15 + 4 + 5 + 6 + 5 + 1 blocking findings triaged; reviewer: "no architectural reason to hold implementation"); 5 structural decisions taken 2026-07-27 incl. the readiness phase-entry lifecycle built in-feature; user-approved v7 2026-07-27, advancing to review-spec
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
- **Bounded cross-doc edits**: amend parent **INV-005** (full orthogonal-outcome remap) and **INV-036**
  (pre-ENTERED scoping); register **TB-007** + migrate **exact-four → exact-five** gate projects in ARCHITECTURE;
  resolve carrier **OQ-A#3** + make carrier **INV-010** real + add the readiness-kernel phase-latch.

**NOT in scope**:
- **Release/binary provenance** — parent **INV-031/032/033**. Corrected.Provenance is **designed** as the
  reusable schema/predicate/Statement **contract** they will reuse (INV-022), but it is **non-shipped** and
  **never referenced by a shipped `src/Corrected.*` binary** (PRH-010, preserving the parent's non-recursive
  bootstrap, INV-033).
- **Closing parent OQ-006 wholesale** — informed, not closed.
- **The P2 completion manifest / DD-002 P2 validator**, DF-003's schema-v3 doc row, DRIFT-001/002/003.

## Complexity Budget
- **Estimated LOC**: ~3200–4800 across 3 PRs (the round-3/4 additions — the three-artifact receipt model, the
  pinned subject classifier, the two-job signer isolation + offline harness, the parsed activation diff, and the
  **full readiness lifecycle protocol** (schema v2 + v1→v2 migration + kernel/orchestrator split + two-step
  sign→activate + entry-receipt provenance + the status×lifecycle×health state model) + the exact-five carrier
  migration — push this above the earlier estimate). The exact-five migration alone touches ~9 propagation sites
  (slnx, INV-014/015 meta-tests, BND-002, carrier ×3, ARCHITECTURE, `docs/features/readiness-gate-carrier.md:24`, `StatusRenderer.cs`).
- **Files touched**: ~38–50
- **New abstractions**: 7 (three-artifact determinism status model; the `Corrected.Provenance` non-shipped shared substrate; the versioned determinism-subject manifest + single classifier; the two-job signing lane; **readiness schema v2 + the pure-kernel/impure-orchestrator lifecycle protocol + the two-step entry activation**; the Phase-0.1-entry receipt + its own provenance identity contract; the post-entry health-check + append-only refresh protocol)
- **Trust boundaries touched**: 3 (TB-004, TB-006, TB-007-new)
- **Risk surface delta**: high

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
  exactly once, and every per-role **projection** digest matches. (Raw digests are expected to differ; equality
  is a projection property.)
- **Violated when**: kinds and roles are conflated (e.g., "five schema-declared kinds"); equality is over raw
  bytes; a role/kind is missing/duplicated without → `not_evaluated`; or either registry set-equality is skipped.
- **Enforcement**: hash verification + a schema test pinning the per-role receipt shape + set-equality asserts
  against both the schema kind registry and the committed role registry; a verbatim-captured fixture (AP-014).
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
  `execution_status=resource_floor_skipped` (valid non-attesting). Escalation to a **pinned larger** runner is
  justified for **infrastructure exhaustion only**, never to erase a projection disagreement; never a retry.
- **Violated when**: the plan is not committed ahead of the runs; a row asserts a commit-vs-run-ID ancestry; the
  floor is duplicated or set from one run; reached via retry; a disagreement is "fixed" by a bigger runner; or
  campaign results are cherry-picked.
- **Enforcement**: per-row `plan_commit`-ancestor-of-`head_sha` + `run_id/attempt`↔`head_sha` association + a
  single-definition test + the retained rows committed as the basis.
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
  commit without read access — the earlier "only id-token" was self-contradictory); artifact download uses only
  the run's artifact API. It **checks out at the exact `attested_commit` SHA with credentials NOT persisted, no
  submodules, no LFS, and Git hooks disabled**, re-checks the producer artifacts' digest/schema/producing-job
  result/commit/run-id/attempt **and the subject-manifest at `attested_commit`**, executes **only one frozen,
  reviewed signer-validation surface** (no producer/test/build/restore/package-hook/arbitrary repository code),
  signs, and publishes the bundle. Third-party Actions pinned by **commit SHA**; signs **only** a protected-main
  `push` (never `pull_request` / `pull_request_target`). The PR2 **transcript proves the actual granted
  permissions**, not merely asserts them.
- **Violated when**: the producer has `id-token: write`; the signer has broader permissions than
  `id-token:write`+`contents:read`, persists credentials, enables hooks/submodules/LFS, or executes any
  repository code beyond the frozen surface; an action is tag-pinned; signing runs on a PR event; or the signer
  does not re-check the manifest at `attested_commit`.
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
  ~10-min Fulcio cert expires).
- **Violated when**: cosign is unpinned/ranged; the bundle lacks a tlog proof or signed timestamp; or the argv
  is not the transcript-frozen one.
- **Enforcement**: CI config assertion (pinned version+digest+frozen argv) + a bundle-content assertion.
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
  embedded inclusion proof).
- **Violated when**: verification uses a pre/post file hash as the semantic check; any pinned flag is missing or
  a regexp/insecure variant (PRH-001); the decoded-payload equality is skipped; or verify-time network is required.
- **Enforcement**: gate precondition (real `gate/run-readiness-gate.sh` path) + real cosign verify against a
  genuine signed fixture + the decoded-payload byte-equality assertion (INV-013 layer 2).
- **Guards against**: AP-002, AP-008, AP-011/AP-013, AP-004
- **Test approach**: integration
- **Integration contract**:
  Entry: from a CLEAN checkout run `gate/run-readiness-gate.sh`
  Through: the real Corrected.Provenance verify wrapper → real pinned cosign; NOT a stub or always-pass double (AP-012)
  Exit: a genuine signed fixture verifies AND its decoded DSSE Statement equals the reconstructed Statement; no verify-time network

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
  string, so P3 computes a typed internal result and maps it to a carrier `ProbeReasons` token at the boundary),
  and the mapping from **every** orchestration/crypto/policy failure to `{rejected | unavailable}` is **total**:
  `{evidence-absent, malformed-receipt, malformed-bundle, signature-invalid, identity-mismatch,
  predicate-type-mismatch, subject-digest-mismatch, stale-subject-manifest, attested-commit-not-ancestor,
  rid-platform-mismatch, non-pass-outcome, verifier-unavailable, trust-root-or-pin-mismatch}` (verifier/tool
  faults → `unavailable`; policy/crypto/staleness/ancestry failures → `rejected`). Any internal error → `false`
  (never pass-through).
- **Violated when**: `true`/`ran-passed` for any non-conforming input; `attested_commit` not an ancestor of HEAD
  (→ `attested-commit-not-ancestor`); a reason is a raw stderr string rather than a typed value; the
  failure→`{rejected|unavailable}` mapping is not total; or an internal exception yields anything but `false`.
- **Enforcement**: gate precondition + the 3-layer test architecture (INV-013) exercising **every** typed reason +
  an ancestry assertion + a totality test (every failure class maps to exactly one `{rejected|unavailable}`).
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
  wrong-subject-digest + malformed-bundle;
  **(3) Orchestration** — absent bundle, missing binary, process timeout, oversized file, parse failure.
  **No genuinely-production-signed `ran-failed` fixture** exists (contradicts never-sign-failures). The **first
  production-identity positive cannot exist until PR2 merges and the main workflow runs** — it arrives as PR3's
  committed evidence; PR2's tests therefore prove the mechanism using a fixture identity only.
- **Violated when**: a semantic policy row shells to cosign against a mutated-signature fixture; absent/bare-JSON
  cases claim to traverse the real cosign path; a production-identity-signed failure fixture exists; or a PR2
  test asserts a production-identity positive.
- **Enforcement**: the three layers with distinct harnesses; a meta-assertion that layer-1 rows never invoke
  cosign and that no committed fixture carries the production identity before PR3.
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
- **Violated when**: PR3 touches any frozen artifact; an existing versioned root file is overwritten/mutated
  (rather than a new version appended); or rotation happens inside an evidence PR.
- **Enforcement**: the PRH-007 parsed-span diff check on PR3 + an append-only trust-root registry test.
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
  files, the P3 declaration, and **explicitly-named** migration surfaces are **excluded**, with the **same
  completeness protection** as the inclusions.
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
  carrier `P3Probe` replaces the stub; and **`gate/Corrected.Gate/StatusRenderer.cs:23`** (which today hard-codes
  "P2/P3 not yet dischargeable" and renders unknown reasons as "unclassified") is updated to render the new typed
  P3 reasons and the post-P3-dischargeable status text. Through PR1 & PR2, `P3.satisfied` stays **false**. In PR3,
  **only the P3 declaration changes** (`P3.satisfied:false→true` + evidence pointer) — **overall
  `implementation_readiness` remains BLOCKED because P2 is still false**; the `lifecycle` latch stays `BLOCKED`
  and does not transition to ENTERED (that awaits `P1∧P2∧P3`, Group G). No claim is made that readiness itself
  changes in PR3.
- **Violated when**: OQ-A#3 stays open; the P3Probe remains the stub after PR2; StatusRenderer still hard-codes
  the old text / renders P3 reasons as "unclassified"; or the spec/docs claim overall readiness (not just the P3
  declaration) changes in PR3.
- **Enforcement**: gate precondition (carrier suite exercises the real P3Probe + StatusRenderer over the typed reasons) + a spec-consistency check + the readiness-block regression.
- **Guards against**: AP-005
- **Test approach**: integration

### Group F — Integration + boundaries (ARCHITECTURE / parent)

### INV-022: Corrected.Provenance is a 5th, non-shipped gate project + a designed shared CONTRACT; the exact-four→five migration is complete [integration]
- **Type**: must
- **Category**: functional
- **Statement**: `gate/Corrected.Provenance` is added as a **5th** gate project, and **every** exact-four
  contract site is migrated to exact-five: the `Corrected.Gate.slnx` aggregator + comment, INV-014 "aggregates
  **exactly** the four gate projects", the INV-015 **membership meta-test** array, the BND-002 loop, carrier
  `readiness-gate-carrier.md:133/882/1216`, ARCHITECTURE:84, **and `docs/features/readiness-gate-carrier.md:24`**
  (historical journal/verification records stay historical — not migrated). It is a **non-shipped** substrate with
  a **deliberately partitioned reuse contract**: the **generic** in-toto **Statement / subject / DSSE-envelope /
  signer-identity verification contracts** are the reusable part the eventual release-provenance consumers (parent
  **INV-031/032/033**) will reuse; the **determinism predicate schema + the RunReceipt schema are P3-SPECIFIC and
  are NOT reused** by release provenance (which carries a different predicate). Reuse is at the generic-contract
  level, **not** a shipped binary linking the gate project (PRH-010).
- **Violated when**: any exact-four site still asserts four; the doc-copy migration misses `:24`; the project is
  un-aggregated/unlocked; the P3-specific predicate/receipt schema is framed as reusable by release provenance; or
  the substrate is framed as a shipped dependency.
- **Enforcement**: the migrated membership meta-test (exact-five) + a lockfile-registry test + a reuse-contract
  note in ARCHITECTURE + PRH-010's no-shipped-reference scan.
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
  live workflow and charter are asserted **in sync**.
- **Violated when**: the lane is verified by a doc/keyword grep or a fixed-cwd proxy; or it drifts from its charter.
- **Enforcement**: an execution test running the extracted script verbatim + (if charter) a sync assertion.
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
  forges CI). The out-of-band protections are stated as **external assumptions** (EA-006): branch protection on
  `main`, required checks, and **CODEOWNERS/required-review** over `gate/**`, `gate/Corrected.Provenance/**`, the
  signing workflow, the trust root, and the cosign pin.
- **Violated when**: the spec/receipt/docs claim protection against "anyone with commit access" without the
  narrowing + external assumptions.
- **Enforcement**: a doc/spec assertion of the narrowed guarantee + a CODEOWNERS presence check over the protected paths.
- **Guards against**: AP-004
- **Test approach**: integration

### Group G — Readiness phase-entry lifecycle (carrier kernel + parent INV-036; built + fixture-tested here, activates when P2 lands)

**Readiness state model (schema v2).** The carrier today recognizes readiness **schema v1** with a committed
`status` declaration (`gate/Corrected.Gate.Kernel/ReadinessBlock.cs:63`). Schema v2 **separates the small
persisted wire format from runtime-derived values** — persisted declarations and derived results must never be
conflated:

**Persisted (committed, small wire format)** — `{ schema_version, status, lifecycle, entry_evidence_pointer,
preconditions }`:
- `status ∈ {BLOCKED, READY}` — a committed **declaration** (the gate always re-derives the live computation from
  probes; it never trusts this field). `indeterminate` is an **internal parser result, never a legal serialized
  value**.
- `lifecycle ∈ {BLOCKED, ENTERED}` — `COMPLETE` is **reserved conceptually** and added only via a later schema
  version (no COMPLETE code here). The declared latch is monotonic (see the transition rules).
- `entry_evidence_pointer` — a **versioned** path to the entry receipt/bundle; **required iff `lifecycle=ENTERED`,
  absent iff `BLOCKED`**.
- `preconditions` — the P1/P2/P3 declarations (as v1).

**Derived at runtime (never persisted)**:
- `effective_lifecycle` — for the **src/ ban** (INV-027) it equals the **declared** `lifecycle` (once ENTERED,
  monotonically ENTERED — a transient integrity fault never reverts it).
- `entry_integrity ∈ {verified | rejected | unavailable | absent}` — the cryptographic verdict on the committed
  entry receipt (INV-026/030); drives the **gate verdict**, not the src/ ban.
- `current_health` — the post-entry determinism/precondition signal (total mapping below).
- `readiness_verdict` — the overall gate verdict composed from the above.

**Legal combinations & transitions**:
- **v1** (no `lifecycle`/pointer): interpreted **pre-entry only** (`declared lifecycle=BLOCKED`); recognized-set
  retains v1 so a not-yet-migrated block still parses (RS-UC / AP-005).
- **v2 `BLOCKED`**: `entry_evidence_pointer` **absent**; `current_health = not-applicable` (**not** `ok`); the
  gate drives `src/` off the re-derived preconditions exactly as v1.
- **v2 `ENTERED`**: `entry_evidence_pointer` **required** and an activation entry receipt is **expected +
  verified** (INV-029/030). Transition `BLOCKED→ENTERED` only via the two-step activation (INV-029); no
  `ENTERED→BLOCKED` transition exists.
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

**Overall `readiness_verdict` × `entry_integrity` (post-entry, closed table)** — the src/ ban keys off
`declared_lifecycle`, the verdict off `entry_integrity`:

| `entry_integrity` | src/ ban (INV-027) | `readiness_verdict` |
|---|---|---|
| `verified` | lifted | success (health fold applies) |
| `unavailable` (transient outage) | **still lifted** (monotonic) | **neutral/degraded** — integrity fails closed, but src/ **not** re-banned |
| `rejected` (forged/tampered ENTERED) | still lifted (moot) | **hard-red failure** — the gate fails; the forgery gains nothing |
| `absent` while `declared:ENTERED` | still lifted (moot) | **hard-red failure** — an ENTERED declaration without its committed entry receipt |

### INV-026: The pure kernel PROPOSES a transition; an impure orchestrator revalidates + signs; ENTERED is derived by VERIFYING the entry receipt [integration]
- **Type**: must
- **Category**: functional
- **Boundary**: TB-006
- **Statement**: the lifecycle is realized by **four distinct components** (never conflated — the kernel is
  I/O-free, `gate/Corrected.Gate.Kernel/ReadinessGate.cs:27`): **(1) a pure transition evaluator** (the kernel)
  computes a **proposed transition** `{stay-BLOCKED | propose-ENTER | honor-ENTERED}` from `(readinessBlock,
  probeResults, entryIntegrity)`, minting/writing nothing; **(2) a main-branch entry producer/signer** (trusted
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
- **Enforcement**: a kernel-purity test (no I/O — carrier INV-004) + a state-machine test over synthetic
  `(block, probeResults, entryIntegrity)` fixtures (propose-ENTER only when `P1∧P2∧P3` re-derive; declared-ENTERED
  monotonic under `entry_integrity=unavailable`; forged declared-ENTERED with `rejected` integrity → gate fails,
  src/ **not** re-banned) + a historical-snapshot test (a P3 refresh after entry keeps entry valid).
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
- **Violated when**: the ban keys off `status` rather than `effective_lifecycle` (so `READY+BLOCKED` permits
  `src/`); it still trips post-`ENTERED`; a transient integrity outage re-applies it; or it is weakened
  pre-`ENTERED`.
- **Enforcement**: the amended INV-036 predicate exercised by a fixture matrix (BLOCKED+content → trip;
  **READY+BLOCKED+content → trip**; ENTERED+content → allowed; ENTERED+content+integrity-unavailable → allowed;
  BLOCKED+empty → allowed) + a parent-amendment consistency check.
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

  After the PR merges, **main** signs the new observation; an **evidence-refresh PR appends and activates** the new
  baseline. Refresh uses **versioned evidence paths** `test/attestations/inv010/<attested-commit>/…` with a small
  **active-baseline pointer** (updated through the PRH-007 **P3-refresh** allowlist mode), so the prior baseline
  stays independently verifiable (append-only, INV-016 style). None of this re-BLOCKs readiness or trips INV-036
  (INV-027); the refresh reuses the **same frozen mechanism** (a mechanism change is a separate PR2-class PR).
- **Violated when**: any outcome class is left as an unpinned "hard red or classified" choice; a stale baseline or
  a resource-floor skip is a **required** red merge-blocker; a real disagreement / malformed-evidence / runner
  failure is downgraded to advisory; a refresh overwrites (rather than appends via versioned path + pointer) the
  baseline; or a refresh smuggles a mechanism change.
- **Enforcement**: a CI-conclusion fixture matrix (stale→neutral; disagreement→red; infra→classified) + a
  post-ENTERED health fixture (stale → `health=refresh-required`, `status`/`lifecycle` unchanged, INV-036 not
  tripped) + an append-only versioned-path + pointer test.
- **Guards against**: AP-005, AP-016, AP-017, AP-001
- **Test approach**: integration

### INV-029: The entry receipt is activated by a self-reference-safe two-step sign→activate protocol [integration]
- **Type**: must
- **Category**: security
- **Boundary**: TB-006, TB-007
- **Statement**: a receipt cannot both bind "the entry commit" and be contained in it, so entry uses the same
  **two-step** pattern as P3: **(A)** a main commit `X` has `P1∧P2∧P3` while still `lifecycle=BLOCKED`; **(B)**
  **trusted main CI signs a Phase-0.1-entry receipt for `X`** (binding `X` + the three evidence digests); **(C)**
  a **tightly-constrained activation PR** (the PRH-007 **phase-entry** mode) commits that receipt and sets
  `lifecycle:BLOCKED→ENTERED` (an activation-only parsed-span diff against the PR merge-base); **(D)** the gate
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
- **Violated when**: the entry receipt reuses the determinism predicate schema; its signer identity/event/attempt
  rules are unpinned; or its verification borrows P3's subject/staleness semantics rather than its own.
- **Enforcement**: an entry-predicate schema test + an entry-receipt verify test (own identity policy) reusing the
  generic envelope contracts + a distinct-predicate assertion.
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
- **Statement**: only PR3 sets `P3.satisfied:true`, after from-clean re-verification.
- **Detection**: the readiness-block value in PR1/PR2 diffs + the gate.
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

### PRH-007: An activation/evidence PR changes anything beyond its typed allowlist mode
- **Statement**: activation PRs come in **three typed allowlist modes**, each enforced at the **parsed
  field/span** level against the **PR's own merge-base against protected `main`** (a filename allowlist is
  insufficient — the readiness doc shares its path with many normative contracts), and each **forbids every other
  parsed field or mechanism path**:

  | Mode | Permitted changes |
  |---|---|
  | **P3 initial activation** (PR3) | a new **versioned** P3 receipt/bundle; the P3 `satisfied` flag + the active-baseline pointer |
  | **P3 refresh** (post-entry, INV-028) | a new **versioned** P3 receipt/bundle; the active-baseline pointer **only** |
  | **Phase entry** (INV-029) | a new **versioned** entry receipt/bundle; the `lifecycle` field; the `entry_evidence_pointer` |

  The signed `attested_commit`/entry-`X` is used **separately** for **ancestry** (ancestor-of-HEAD) and
  **subject-integrity** (the receipt binds it), **never** as the diff base. **Frozen mechanism digests**
  independently prove the cosign digest, trust root, verifier argv, identity policy, schemas, and subject-manifest
  rules did not change. No `src/`/mechanism code changes in any mode.
- **Detection**: a mode-typed parsed-object diff relative to the **PR merge-base** + a frozen-mechanism-digest
  equality check + a path check for the versioned evidence files.
- **Consequence**: trust policy or a sibling normative contract changes under an "evidence/activation" label; a
  mode permits fields belonging to another mode; or the diff base is attacker-chosen / polluted by unrelated descendants.

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
- **EA-003**: RID `linux-x64` (spike EA-002); ProcessorCount observed; a different RID needs a re-mint. — Wrong → `rid-platform-mismatch` → false.
- **EA-004**: the exact cosign version + bundle format + frozen argv are settled by the PR2 transcript spike. — Wrong → PR2 cannot freeze / is not landable.
- **EA-005**: the floor is set by the PR1 **commit-anchored** measurement campaign; the platform is pinned + recorded. — Wrong → an unstable floor / unreproducible platform.
- **EA-006** (external, out-of-band): branch protection on `main`, required checks, and CODEOWNERS/required-review over `gate/**`, `gate/Corrected.Provenance/**`, the signing workflow, the trust root, and the cosign pin. The boundary of the narrowed threat model (INV-025) — **not** a cryptographic guarantee. — Wrong → the protection assumption does not hold.

## Design Decisions (resolved)
- **DD-001 (verify path)**: digest-pinned cosign CLI (not `Sigstore.Net`). (User 2026-07-27.)
- **DD-002 (cosign object model)**: `attest-blob --statement` (Corrected owns Statement semantics); verify with `verify-blob-attestation --check-claims=true` + exact identity/issuer/workflow-SHA + `--use-signed-timestamps` + pinned `--trusted-root`; the **semantic** check decodes the signed DSSE Statement and byte-compares it to the reconstructed one. Exact version + argv + bundle format frozen by the PR2 transcript spike.
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

## Open Questions
- **OQ-001 [scheduled, not open-to-GREEN]**: the exact cosign version, bundle format, and frozen sign→offline-verify argv — a **PR2 transcript-spike deliverable** (real GitHub-OIDC sign → fresh-machine network-disabled verify), capturing exact argv, bundle media type, output shape. Blocks the PR2 freeze.
- **OQ-002 [scheduled]**: the measured stable core floor — a **PR1 measurement-campaign deliverable** (a plan committed before N retained attempt-1 runs).
- **OQ-003 [cross-ref]**: parent **OQ-006** (keyless vs key-backed for the release-provenance path) — informed by this feature's keyless determinism lane; the release path stays a parent decision.
- **OQ-004 [RESOLVED — Group G state model]**: the wire format (`{schema_version, status, lifecycle,
  entry_evidence_pointer, preconditions}`), the derived values, the legal combinations/transitions, the v1→v2
  migration, and the entry-receipt identity contract are all defined by the **Group G state model + INV-026–030**.
  `COMPLETE` is reserved for a later schema version (no code here). Nothing about the lifecycle representation
  remains open to GREEN.

## Packages Affected (monorepo)
- **Spike** (`spikes/dafny-compat/**`, TB-004): `tests/SpikeTests/Inv010DeterminismTests.cs` (orthogonal status model + pure classifier + 5-role/3-kind comparison), the per-role receipt writer, the serial-lane runnable surface + charter, the recorded platform identity.
- **Test/build-gate carrier** (`gate/**`, exempt): NEW **`gate/Corrected.Provenance/**`** (non-shipped shared substrate — generic Statement/subject/envelope/identity verify contracts + the P3-specific predicate & RunReceipt schemas + cosign verify wrapper) + its `packages.lock.json`; `gate/Corrected.Gate/Probes.cs` (`P3Probe` real verifier + typed internal result + expanded typed reasons + pinned constants); **the readiness lifecycle** (`gate/Corrected.Gate.Kernel/ReadinessGate.cs` — the pure `BLOCKED→ENTERED` transition evaluator, COMPLETE reserved; the impure `gate/Corrected.Gate/` orchestrator = gate-side entry-receipt verifier + activation-diff validator; the schema-v2 wire format + v1→v2 migration; INV-026–030) + its state-machine + purity tests; **`gate/Corrected.Gate/StatusRenderer.cs`** (post-P3 status text + typed-reason rendering + the ENTERED status) + its tests; the 3-layer fixture corpus under `gate/Corrected.Gate.Tests/fixtures/attestations/` + synthetic `P1∧P2∧P3` lifecycle fixtures; the determinism-subject manifest + schema + pinned classifier; the **exact-four→five** migration (`Corrected.Gate.slnx` + comment, INV-014, INV-015 membership meta-test, BND-002 loop).
- **CI** (`.github/workflows/**`): the two-job serial determinism lane (unprivileged producer + minimal signer; isolated; pinned OS + recorded image; larger-runner escape; `run_attempt==1`; protected-main push only; SHA-pinned actions; the classifier as the acceptance trigger).
- **Evidence surface** (`test/attestations/**`): **versioned** P3 evidence under `test/attestations/inv010/<attested-commit>/` (the production-identity receipt + `.sigstore.json` bundle) + a small **active-baseline pointer** file (P3-initial-activation in PR3, appended on refresh); the **Phase-0.1-entry receipt + bundle** + the `entry_evidence_pointer` (phase-entry activation, when P2 lands). No fixed `inv010-determinism.json` filename — paths are versioned + pointer-indirected (INV-028/PRH-007).
- **Specs / docs**: `readiness-gate-carrier.md` (INV-010 real; OQ-A#3 resolved; 133/882/1216 exact-five; the readiness-kernel phase-latch; **DD-002's pinned `P3AttestationPath = test/attestations/inv010-determinism.json` becomes the fixed active-baseline POINTER**, pointing to the versioned receipts under `test/attestations/inv010/<commit>/` — preserving the pinned constant while enabling append-only history); **`phase-0-1-worker.md`** (INV-005 amend — capability-baseline + full orthogonal remap; **INV-036 amend — production-code ban scoped to pre-`ENTERED`**); ARCHITECTURE (TB-007; exact-five incl. ARCHITECTURE:84; Corrected.Provenance non-shipped substrate + partitioned reuse contract); **`docs/features/readiness-gate-carrier.md:24`** (exact-five). Historical journal/verification records stay historical.
- **Repo root / provisioning**: the pinned cosign version+per-RID digest + hard-coded SHA-256 + versioned append-only `trusted_root.json` (a `provision-cosign.sh` mirroring `provision-z3.sh`) + CODEOWNERS over the protected paths (EA-006).
