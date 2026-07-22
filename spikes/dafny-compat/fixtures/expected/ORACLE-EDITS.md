# Oracle-edit adjudication log (QA-010 class fix)

`fixtures/`, `fixtures/expected/`, and `manifest/` are **test-owned oracle
paths**. Edits to them during GREEN/fix rounds are recorded here with rationale
so the orchestrator can diff them RED→GREEN and adjudicate each one (QA-010).

## GREEN round (2026-07-21)

1. **`fixtures/expected/ok.sidecar.json` — `min_task_count` 3 → 2**
   (AP-014 real-producer recapture). On the pinned Dafny 4.11.0 / Z3 4.12.1,
   `fixtures/ok.dfy` yields **3 verification targets** (`Add`, `MaxOfTwo`,
   `SumFirst`) but only **2 produce solver tasks** — the trivial function `Add`
   (`function Add(x,y) { x + y }`) has no proof obligation, so it contributes a
   target but zero Boogie/Z3 tasks. The non-vacuity floor is therefore 2 (> 0),
   not 3. Independently confirmed by
   `QaFixRound1Tests.QA010_OkFixture_ThreeTargets_TwoSolverTasks_TrivialAddHasNoObligation`
   (asserts 3 targets, 2 tasks, and that the task-less target is exactly `Add`).

2. **`fixtures/syntax-error.dfy` — planted-token line normalization.**
   The malformed fixture's parse-error token now sits on the committed expected
   line (line 4, `method Broken( {`), matching P08's location oracle. The prior
   EOF-only form reported a virtual line 5. No probe semantics changed; only the
   fixture text was aligned to the committed expected line.

## Fix round 1 (2026-07-21)

3. **`manifest/expected-loaded/route-{a,b}.json` — universe widened (QA-004).**
   The GREEN "universe = Dafny*/Boogie*/System.CommandLine" note (a silent
   narrowing) is **reversed** to the codex-F8 definition: every non-framework
   assembly selected through the route harness's `.deps.json` package runtime
   assets. The predicate is now declared machine-readably in each oracle
   (`universe_predicate.id = "deps-json-package-runtime-assets"`) and consumed
   by BOTH the harness (`HarnessCore.PackageRuntimeAssets`) and the test
   (`Inv002.P03_RuntimeLoadedSet_EqualsCommittedExpectedSet`). The `assemblies`
   arrays were re-captured from a real P03 exercise (23 assemblies route A, 21
   route B) — a designed oracle capture, like RED's placeholder digests.

All three edits are ratified in the spec's QA-amendments line (disposition
2026-07-21) and mirrored in
`.correctless/artifacts/qa-findings-dafny-compat-spike.json` (QA-004, QA-010
`fix_note`s).
