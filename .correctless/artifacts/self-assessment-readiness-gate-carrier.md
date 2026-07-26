# Self-Assessment brief — readiness-gate-carrier (shared input to the review team)

Scope note: the parse/kernel/verdict-table core (Groups A–B: INV-001, INV-003..007,
INV-012, INV-015/016) is well-specified and mechanically testable. Residual risk is
concentrated in the **probe layer (Group C)** and the **production-surface scan (Group D)**,
plus unapplied ARCHITECTURE amendments. A codex (GPT-5.6) round already incorporated 12
findings; focus on what codex + the author still under-nail.

## Hardest-to-test invariants
- **INV-011 (shipped-closure ban) — hardest + inert.** `src/` is EMPTY (EA-004): there is no
  shipped closure to scan today, so the real check computes an empty closure and trivially
  passes — only the 6 fixtures ever exercise it (AP-002 residual). Honest closure computation
  needs a real MSBuild/Roslyn build (Compile items + ProjectReference closure + linked
  Compile + run source generators/analyzers). The "type implementing a policy interface"
  predicate references interfaces that don't exist yet.
- **INV-008 (P1 probe) — most novel, least-grounded.** The spike `AdrLinter.Lint`
  (`Components.cs:~980`) NEVER opens the cited evidence file (only `IsNullOrEmpty(Evidence)` +
  cross-checks the adr_lint block against separately-supplied records). So (a′) "resolve the
  path, validate the sample, RECOMPUTE Route-A COMPATIBLE" is all-new carrier logic: (1)
  "recompute COMPATIBLE" is undefined and lives exactly where DF-003 (out-of-scope live
  false-COMPATIBLE: child-exit-20 + all-pass → COMPATIBLE) lives; (2) the `records` arg the
  probe passes to `Lint` is unspecified (canonical sample legitimately has
  adjudication_records:null); (3) (a″) "ADR accepted status validated mechanically" = parsing
  free prose `Status:` (AP-014 brittle); (4) rides on a spike that is currently red-from-clean
  with fragile evidence binding.
- **INV-002 (AST pre-validation) — correct only on ONE of the two offered APIs.** Rejecting
  explicit `!!str`/`!!int` while ACCEPTING an implicitly-typed plain scalar is only possible
  at YamlDotNet's low-level Parser event stream; the `YamlStream` DOM (also offered by the
  spec) resolves tags and loses the explicit-vs-implicit distinction → an implementer picking
  YamlStream cannot pass the discriminating fixture. The impl-path choice is load-bearing but
  left optional.
- **INV-014**: `dotnet test` does NOT fail on zero-discovery by default; asserting "non-zero
  executed count / fixtures a–g executed" needs TRX/console parsing; `.slnx` aggregator
  support on the pinned SDK is plausible but unproven.
- **INV-013 degraded-env branch**: simulating "evidence unavailable" needs an injectable path,
  but INV-001/009/010 pin paths as tested constants → the hard-fail-closed behavior is proven
  on an injected-path proxy, not the production pinned-path probe (AP-010/AP-002 seam).

## Most-likely-wrong assumptions
- **EA-004 (shipped packages skeleton/inspectable) — most likely wrong.** src/ is empty;
  INV-011's real subject doesn't exist; "fail closed when closure can't be computed" collapses
  to "empty closure trivially passes."
- **DD-001 (direct ProjectReference to SpikeContracts).** SpikeContracts is Dafny-free +
  multi-targets net10.0;net8.0 (good), but lives under spikes/dafny-compat/ with its own
  Directory.Build.props + central package management + packages.lock.json + <clear/> NuGet
  config. A cross-tree ProjectReference under the gate's RestoreLockedMode mixes two lock/CPM
  contexts in one restore. Fallback (extract to shared lib) deferred "before GREEN" =
  unresolved.
- **EA-001 repo-root global.json — blast radius.** Globalizes a spike-local pin to the whole
  repo; a contributor with only a newer 10.0.x SDK can no longer build ANYTHING.
- **DD-003 (atomic parent flip) — coupling + partial-migration (AP-016) + signature drift.**
  Carrier both builds the checker and edits the checked artifact in one changeset. Also: parent
  INV-002 still describes the OLD single-arg `EvaluateReadiness(blockText)` while the carrier
  implements the two-arg split kernel (INV-004) — DD-003 updates "current-state references,"
  leaving a parent↔carrier signature drift.
- **DD-005 (INV-044 out) — contradicts ARCHITECTURE**, whose entrypoint test_via + carrier row
  still assign the INV-044 history registry + meta-test to this carrier.
- **EA-003 (RID linux-x64 for P3)** — inert this slice (P3 skeleton-only); deferred/unproven.

## ARCHITECTURE gaps (six, not the two in OQ-A)
1. `readiness-build-gate.test_via` still says the EXT-01-buggy `dotnet test gate/Corrected.Gate`.
2. §Production-surface partition still lists "any `**/*.Tests/**`" as unconditionally exempt
   (the EXT-04 leak); needs the shipped-closure-overrides qualifier INV-011 depends on.
3. **TB-005 unregistered**: carrier INV-001/002/BND-001/STRIDE cite the intake/tamper boundary
   as TB-005, but ARCHITECTURE registers only TB-001..004 — the carrier's primary boundary has
   no home.
4. **Kernel-split handler drift**: entrypoint handler `ReadinessGate.cs:Evaluate` + prose
   describe a single pre-split Evaluate; carrier splits kernel + orchestrator.
5. **INV-044 reconciliation** un-applied (entrypoint + row still assign it here).
6. **PAT-005 unwritten**: INV-011/PRH-002 self-enforcement leans on an unregistered pattern.

## Highest residual risk (flag for external review)
1. INV-008 (P1 "recompute COMPATIBLE") — most likely to block GREEN.
2. INV-011 (shipped-closure ban) — inert; only fixtures prove it.
3. INV-002 (AST pre-validation) — hinges on the low-level event API; explicit-vs-implicit tag.
4. INV-013 (conditional green-from-clean) — degraded branch on a proxy; green only as green as
   the spike it reads.
5. DD-003 (atomic parent flip) — cross-spec coupling + signature drift.

## Overall risk profile
Core Groups A–B are implementable and well-specified; the codex round de-risked the
fail-closed decision logic. Danger is downstream of the kernel: INV-008's "recompute COMPATIBLE
from the exact sample" is undefined + entangled with out-of-scope DF-003 + a red-from-clean
spike; INV-011 scans a shipped closure that doesn't exist so the real check is inert; and 3 of
6 ARCHITECTURE amendments INV-011/INV-014 depend on are unapplied. Buildable, but the P1 probe
and the closure scanner carry nearly all the escape risk.
