# Antipatterns — Corrected

Every item is a bug class that escaped testing at least once.
The /cspec and /creview skills check new features against this list.

AP-001 through AP-019 are imported from the Correctless project's dogfood
corpus (public repo: joshft/correctless, `.correctless/antipatterns.md`) —
bug classes observed there, generalized to Corrected's domain. Each cites
its upstream entry as `[correctless AP-xxx]`. Local frequency starts at 0;
increment when the class recurs here.

## How to Add an Entry

When a bug is found after merge:
1. Create a new AP-xxx entry (increment the last number)
2. "What went wrong" — describe the bug as a concrete story
3. "How to catch it" — write the spec rule or test that prevents recurrence

## Entries

### Enforcement & trust integrity

### AP-001: Fail-open fallback on enforcement failure `[correctless AP-019]`
- **What went wrong (upstream)**: an enforcement function had `|| result="$raw"` — when enforcement errored, unenforced input passed through as if approved.
- **How to catch it**: enforcement and acceptance code paths must never fall back to the unenforced input. On any internal failure, return reject/error with diagnostics — an acceptance verdict of "pass" must only be producible by the check actually running. Spec rule: every acceptance-relevant function's error path yields reject, never pass-through.

### AP-002: Dead code in security/enforcement paths `[correctless AP-022]`
- **What went wrong (upstream)**: a guard function was defined, fully unit-tested (47 passing tests), and never called from the production chain — structurally inert with green coverage.
- **How to catch it**: for every honesty-policy check, bypass detector, and protected-surface guard, at least one test must exercise the full production entry-point → guard chain (e.g., `corrected certify` on a fixture that the guard must reject), not the guard in isolation. Isolated-unit coverage of enforcement code is not evidence it is wired.

### AP-003: Gate runs against unverified substrate `[correctless AP-035]`
- **What went wrong (upstream)**: adversarial probes ran in isolated worktrees that did not contain the code under test; "target does not exist" outcomes were consumed indistinguishably from genuine survivals, producing authoritative-looking results computed against the wrong tree.
- **How to catch it**: any gate operating on a derived copy (materialized certification subject, container, snapshot, candidate workspace) must first verify the copy contains what it claims — digest match against the intended candidate before verdicts count. Result schemas must include a substrate-invalid state distinct from pass/fail/error, and an aggregate circuit breaker aborts the round when substrate-invalid results exceed a threshold instead of averaging them in.

### AP-004: Claimed enforcement strength exceeds enforcement-layer capability `[correctless AP-040]`
- **What went wrong (upstream)**: a cooperative-loop advisory hook was specced and hardened for years as a "security boundary" it structurally could not be; the mismatch generated both false confidence and chronic friction.
- **How to catch it**: every guarantee-shaped claim (spec invariant, receipt field, doc statement) must name its enforcement layer — deterministic verifier check, digest/signature, independent replay, advisory diagnostic, or agent instruction — and what that layer cannot stop. A claim whose stated strength exceeds its layer is a blocking review finding. This is the receipt/residual-trust-ledger discipline applied to Corrected's own development.

### AP-005: Escape hatch becomes routine plumbing; frozen asset has no legitimate-change affordance `[correctless AP-023, AP-037]`
- **What went wrong (upstream)**: a misconfigured gate made overrides mechanically routine (no alert fired because frequency wasn't monitored); separately, a protection mechanism had no sanctioned path to edit the protected file when it was the feature's deliverable, so every iteration required a manual lift.
- **How to catch it**: track waiver/override/assumption-approval frequency per run — a rising mean is a gate-misconfiguration signal, not legitimate exceptional use. Every freeze mechanism (frozen spec surface, protected lock, pinned toolchain) must ship with its legitimate-update affordance defined (Corrected's intake re-lock path is the model); a freeze without an update path degenerates into routine overrides.

### Data & protocol seams

### AP-006: Paired collections processed without cardinality assertion `[correctless AP-020]`
- **What went wrong (upstream)**: code zipped two parallel arrays by index; when producers returned fewer elements than expected, trailing security-relevant elements were silently dropped — twice, in different code paths.
- **How to catch it**: any zip of paired collections (obligations ↔ verifier results, findings ↔ verdicts, requests ↔ responses) must assert equal cardinality first; mismatch is fail-closed for acceptance-relevant data. Prefer a single keyed structure over parallel collections so the class is structurally impossible.

### AP-007: Unbounded-size data flows through a bounded-size medium `[correctless AP-039]`
- **What went wrong (upstream)**: file contents were passed via argv into subprocesses; everything worked until an artifact crossed ARG_MAX, then failed with no warning. The instance-level fix missed a sibling call site, which failed a month later.
- **How to catch it**: model the medium's bound wherever data crosses a process seam (argv, env, single protocol record). Corrected's protocol rules — explicit record size limits, large artifacts by content-addressed descriptor, never inline — apply to every subprocess invocation (Dafny, solvers, build tools), not just the worker↔adapter seam. When fixing any at-scale failure, grep for sibling instances of the same flow pattern before closing.

### AP-008: String-interpolated construction of commands, queries, or filters `[correctless AP-010]`
- **What went wrong (upstream)**: user-controlled values were interpolated into filter strings; values containing quotes broke the syntax — reintroducing a bug class that had already been fixed once.
- **How to catch it**: build subprocess argv as arrays and queries via parameterized APIs — never by string interpolation of caller-derived values, and never through a shell/eval layer. Config-sourced tool invocations follow the structured-config → argv rule: each config field maps to a discrete argv element with charset validation.

### AP-009: All-or-nothing parsing of append-only logs `[correctless AP-014]`
- **What went wrong (upstream)**: a slurp-mode parser required every JSONL line to be valid; one truncated line (crash mid-append) made the entire dataset silently read as empty, disabling downstream enforcement with no error.
- **How to catch it**: consumers of append-only line-oriented data (telemetry, evidence logs, attempt journals) must handle malformed lines per a declared contract — skip-and-count, or fail loudly — never a whole-file parse whose failure reads as "no data". Note the inverse applies to the worker↔adapter protocol: there, malformed records fail closed per the protocol spec — the antipattern is *silent* zeroing, not strictness.

### Testing honesty

### AP-010: Test passes for the wrong reason `[correctless AP-007]`
- **What went wrong (upstream)**: tests passed via leaked state from a prior test and via an empty-input fast path — assertions held while the feature under test never executed.
- **How to catch it**: every integration test creates isolated state and asserts preconditions before postconditions (e.g., assert the fixture *fails* verification before testing that the patch makes it pass). A test that cannot fail when the feature is removed is not a test.

### AP-011: Keyword-presence tests and test-routing around spec-named resources `[correctless AP-003, AP-016]`
- **What went wrong (upstream)**: tests grepped for keywords instead of exercising behavior; separately, an agent tested an easy auxiliary path while the spec-named hard path stayed untested — coverage looked fine.
- **How to catch it**: when a spec rule names a specific function, command, obligation class, or protocol message, at least one test must exercise that named resource through the real path. Tests covering only simpler adjacent paths are a blocking finding: the requirement was routed around, not satisfied.

### AP-012: Hand-rolled always-succeed mocks `[correctless AP-017]`
- **What went wrong (upstream)**: hand-written stubs returned success unconditionally; no failure mode of the real dependency was ever exercised.
- **How to catch it**: test doubles for the Dafny adapter, solvers, and subprocess seams must implement the real interface contract and exercise failure modes (timeout, crash, malformed output, nonzero exit). Prefer generated/contract-bound mocks; a double that cannot fail tests nothing.

### AP-013: Phantom integration/e2e execution `[correctless AP-018]`
- **What went wrong (upstream)**: integration tests compiled but never ran against real dependencies (`skip("requires docker")` on every CI run) — the suite provided zero confidence while looking present.
- **How to catch it**: integration tests against real Dafny/solver toolchains must produce execution evidence — real durations, actual tool output, timestamp progression. Skipped-in-CI integration suites are a blocking finding, not an accepted limitation; if an environment can't run them, that gap goes in the residual-trust ledger, not under the rug.

### AP-014: Test fixtures diverge from real producer output `[correctless AP-031]`
- **What went wrong (upstream)**: a parser was tested only against hand-written fixtures in a subtly wrong format; all 65 tests passed while the script silently processed 0 real records. Recurred twice more before being promoted to a structural rule.
- **How to catch it**: every parser of external-tool output (Dafny diagnostics, verifier results, solver models, build logs, protocol records) must have at least one fixture captured verbatim from the real producer at the pinned version, with a source citation. Hand-written-only fixture suites for parsers are a blocking finding. Re-capture fixtures on every toolchain version bump.

### AP-015: Toolchain version drift between local dev and CI `[correctless AP-011]`
- **What went wrong (upstream)**: local dev ran a newer tool version than CI; a silent parser-behavior difference between versions meant thousands of tests passed locally while CI failed, costing two debug cycles.
- **How to catch it**: one exact pin for every semantic dependency (Dafny distribution, Z3, .NET SDK, Node/Pi in managed mode) with CI verifying the pins are what actually ran — recorded versions belong in evidence artifacts. Any behavior-relevant version bump reruns the conformance/fingerprint suites (the DafnyAdapter upgrade rule).

### State & lifecycle

### AP-016: Conditional update or migration leaves partial state `[correctless AP-002, AP-004]`
- **What went wrong (upstream)**: presence checks passed while the guarded update was unreachable; migrations moved some components and not others; re-runs duplicated entries. Seven findings across four features.
- **How to catch it**: every update/migration path (lock re-resolution, workspace refresh, schema migration, cache invalidation) is tested from at least three initial states — clean, partial, full — plus a double-run idempotency check (second run produces identical state). Fresh-state-only testing of update code is a blocking gap.

### AP-017: Lock mechanism with coupled artifacts and inconsistent cleanup `[correctless AP-021]`
- **What went wrong (upstream)**: a locking fix added a second artifact (directory beside the lockfile) but stale-lock recovery cleaned only the original — stale recovery was permanently broken until manual intervention.
- **How to catch it**: when a mechanism creates coupled artifacts (lockfile + workspace, lock + journal, snapshot + manifest), every lifecycle path — stale detection, release, crash recovery — must handle all artifacts. Test stale recovery end-to-end: create the stale state (dead PID / orphaned workspace), verify fresh acquisition succeeds without manual cleanup.

### AP-018: Pipeline reports success without completeness verification `[correctless AP-030]`
- **What went wrong (upstream)**: a multi-step pipeline silently truncated after step 2 of 7; the caller saw "completed" with no error, warning, or artifact indicating truncation.
- **How to catch it**: every multi-phase orchestration (certification run: preflight → verify → honesty → vacuity → build → runtime evidence → receipt) declares its expected end state up front and verifies it before reporting success. A receipt must be emittable only from a run whose recorded completed-phase set matches the declared plan — partial runs report partial, loudly.

### AP-019: Parallel agents share a mutable substrate `[correctless AP-034]`
- **What went wrong (upstream)**: six concurrent read-only-by-intent agents ran against the live working tree; one agent's investigative `git stash`/`git worktree` transiently reverted the shared tree and siblings read the half-reverted state, producing spurious findings. Prose-level read-only discipline did not hold.
- **How to catch it**: N≥2 concurrent workers (proof-attempt strategies, probe rounds, adversarial checkers) over any mutable substrate require a structural isolation invariant — per-worker candidate workspaces or a read-only snapshot — never stated discipline. A spec proposing concurrency over shared mutable state without an isolation invariant is a blocking review finding.

### Operator surface & entry points

### AP-020: Documented entry point verified by reference, exercised only through a normalized proxy invocation `[local: PMB-001]`
- **What went wrong (local)**: the spike's sole documented operator command — `env -i HOME="$HOME" bash -p spikes/dafny-compat/scripts/run-spike.sh`, run from the repo root — exited **127**. The controller captured its own path from `BASH_SOURCE` (repo-relative for that invocation), then `cd`'d into `SPIKE_ROOT` and relaunched its inner controller from the captured path (`setsid bash -p -- "$SCRIPT_SELF"`), which no longer resolved post-`cd`. The operator-surface invariant (INV-014) "verified" the entry point with a README keyword-presence check (`Assert.Contains("scripts/run-spike.sh", readme)`), and every behavioral test launched the controller through a helper (`Launch.ScriptCore`) that resolved the script to an **absolute** path and ran from a **fixed** cwd (`SpikeRoot`) — so no test, QA round, or audit ever executed the command as an operator would type it. The full-regression net was deferred to an unwired CI job (DF-001). An external code review found it.
- **How to catch it**: for any entry point a human or CI is *told* to invoke (README/quick-start command, Makefile target, published CLI, install snippet), at least one test must execute that command **verbatim** — same `argv[0]` form (relative vs absolute), same working directory the docs specify — and assert it gets past launch, not merely that the docs mention it. A doc-string grep is not execution (this is AP-011 applied to the invocation contract). A behavioral test that *normalizes* the invocation (absolute path, fixed cwd, injected env) exercises a proxy, not the operator's command, and cannot catch form-specific defects: relative-path resolution after a `cd`, cwd assumptions, `argv[0]`/`$0` parsing, or environment stripping. Prefer a structural test that parses each fenced command out of the doc and runs it from the documented directory. Additionally: any path derived from `BASH_SOURCE`/`$0`/argv that is reused after a `cd` must be canonicalized to absolute at capture. Related: AP-011 (keyword-presence / test-routing to an easier path), AP-013 (phantom integration — an entry point that compiles/references but never actually runs as shipped), AP-014 (the *invocation* diverging from the real operator command is the sibling of fixtures diverging from real producer output).
- **Frequency**: 1 finding across 1 feature (dafny-compat-spike).
