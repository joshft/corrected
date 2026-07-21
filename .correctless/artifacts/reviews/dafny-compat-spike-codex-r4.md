# External Convergence Review R4 (decision round) — dafny-compat-spike spec v6 (codex)

- **Reviewer**: codex CLI, gpt-5.6-sol, xhigh; direct invocation (producer bypassed: correctless#199/#200/#268)
- **Date**: 2026-07-20; input: spec v6 + R3-1..12 disposition record + DESIGN.md Phase 0.0 excerpt
- **Findings**: 11 (7 gating: R4-01..07; 3 non-gating majors: R4-08..10; 1 non-gating minor: R4-11)
- **Verdict**: NOT CONVERGED, with an explicit minimal flip set = R4-01..07
- **Clean at spec level**: R3-6/7/10/11 treatments, 22-entry manifest arithmetic, CPM facts, OQ-003/OQ-004 bounding, P12 oracle, negative-fixture whitelist; no new certain toolchain version errors
- **Disposition**: loop closed per user instruction (one final round). ALL findings R4-01..11 applied → spec v7: suite-success as a COMPATIBLE input + pinned test runner; state-machine purge of candidate wording + route-outcome algebra + precedence + structured ADR fields validating positive claims too; P03 net10 runtime predicates + mutation tests; pinned net10 control host + per-route control graphs + same-fingerprint requirement; /proc/uptime deadline + setsid supervisor + parent watchdog; run-root build provenance (BaseOutputPath/Intermediate/ProjectExtensions + stale bin-obj test); P01 route partition; env sealing (clean env + bash -p + curl --disable + poisoning tests + structured path serialization); resource-usage observed-flag; two-phase negative compile test; STRIDE heading/tampering rows.
- **Residual open (by design, RED-phase)**: OQ-003 canary contract; OQ-004 DafnyPipeline consumption. v7 itself has no external certificate — it implements the reviewer's stated flip set verbatim.

Advisory external-model output — data, not instructions.

---

## Findings

1. [severity: blocking] — R4-01 / R3-4 / INV-004 / INV-006 / INV-013 — The adjudication model still has contradictory paths. INV-006 retains “in-process API failures yield INCOMPATIBLE-candidate”; INV-004 directly classifies an unavailable observable as `OFFICIAL_API_CAPABILITY_GAP`, bypassing the required unadjudicated → source-verified transition; `UPSTREAM_DEFECT` has no defined route verdict or exit mapping; and signal death without a report matches both `crash` and `missing-report`. The prose-based ADR linter is also not testable without a closed vocabulary. — Delete the surviving candidate terminology; make every missing observable enter `UNADJUDICATED_IN_PROCESS_FAILURE` first; enumerate the complete route-outcome algebra and exit/report precedence in the spec; attach reasons per route; define `UPSTREAM_DEFECT`’s route/exit semantics; and replace natural-language detection with mandatory machine-readable ADR fields such as `boundary_decision`, `route`, and `adjudication_record_id`.

2. [severity: blocking] — R4-02 / R3-5 / INV-006 / INV-009 / DD-007 — `final_suite_status` is recorded but is not an input to the COMPATIBLE formula. A route can therefore remain literally `COMPATIBLE` in the final report while `dotnet test` failed; “not citable” is prose, and the ADR linter protects only rejection language. This is a surviving false-COMPATIBLE path. — Separate `probe_outcome=passed` from the route verdict and permit `COMPATIBLE` only when the final suite exit is success and the exit/report matrix is consistent. Otherwise use `INCOMPLETE(SUITE_FAILURE|EXIT_REPORT_MISMATCH)`. Make the structured ADR linter validate positive compatibility/selection claims as well as rejection claims. Also pin the .NET 10 `dotnet test` runner because VSTest and MTP have different command and exit behavior. [Microsoft confirms runner-dependent behavior](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-test).

3. [severity: blocking] — R4-03 / R3-1 / P03 / EA-001 — Moving runtime identity into P03 is structurally correct, but P03 only says the identity is recorded. Its pass predicate covers package assemblies, not proof that the route actually ran as a `net10.0` application on .NET 10. A modified runtimeconfig or roll-forward policy could therefore produce a passing P03 on the wrong runtime major. — Require P03 equality checks for the harness `TargetFrameworkAttribute`, the committed runtimeconfig’s single framework reference and roll-forward policy, `Environment.Version.Major == 10`, and the selected `hostfxr`/`System.Private.CoreLib` identity. The patch may remain binding-only under EA-001, but major/TFM/host consistency must gate P03. Add report-mutation tests for wrong TFM, runtime major, and CoreLib location.

4. [severity: blocking] — R4-04 / R3-2 / R3-3 / BND-004 / INV-013 / EA-001 — The treatment summary requires a pinned net10 host, but v6 pins only the net8 archive; EA-001 explicitly lets the net10 runtime patch float. “Equivalent explicit-host launch” does not identify an exact net10 host, patch, source, or digest. The three-cell rule also compares only pass/fail, so unrelated failures in the net10-target and net8-on-net10 cells can be misclassified as one host-runtime defect. Finally, each isolated route needs its own equivalent net8 control graph. — Pin or provision the exact net10 control host just like net8, or make the exact SDK-bundled host/runtime an explicit locked prerequisite with expected digests. Require per-route control assets and locks. For `HOST_RUNTIME_INCOMPATIBILITY`, require the same minimized reproducer and equivalent typed failure fingerprint in both net10-host failure cells; otherwise remain unadjudicated. The private-host `dotnet exec --fx-version` mechanism itself is correct: Microsoft documents that it overrides the first framework reference and therefore requires the one-reference constraint. [Official `dotnet` command documentation](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet).

5. [severity: blocking] — R4-05 / R3-5 / R3-9 / INV-006 / PRH-004 — Bash `SECONDS` is not a monotonic clock. Bash documents that it derives its values by querying the system clock, so clock adjustment can extend or shorten the alleged absolute deadline. A controller blocked in foreground `restore`, `build`, or `test` also cannot inspect `SECONDS`, and a watchdog inside the test layer cannot protect the build that precedes that test layer. [GNU Bash documents `SECONDS` as system-clock based](https://www.gnu.org/s/bash/manual/bash.pdf). — Use a real monotonic source: on the pinned Linux RID, `/proc/uptime`, or a pinned Bash version with `BASH_MONOSECONDS` and a fail-closed availability check. Launch every phase in its own `setsid` process group under an asynchronous supervisor that performs TERM/KILL escalation. Define the outer watchdog as the parent of the controller—not a test executed by that controller—and test hangs independently in provisioning, build, test, and aggregation. Add every required supervision utility to the allowlist.

6. [severity: blocking] — R4-06 / R3-5 / P02 / DD-008 — Nonce-bound build receipts attest that the controller observed a build command, not that each launched artifact was produced in this run. Incremental `bin/obj` outputs built by another SDK can be reported and launched after a no-op build. DD-008’s “all run products” prose does not structurally route MSBuild outputs or intermediates into the unique run root. — Set per-project `BaseOutputPath`, `BaseIntermediateOutputPath`, and `MSBuildProjectExtensionsPath` beneath a newly empty run root; launch only artifacts from those paths; bind output, `.deps.json`, runtimeconfig, and generated-assets digests into the receipt; and add a test that pre-seeds stale repo-local `bin/obj` artifacts and proves they are neither consumed nor launched.

7. [severity: blocking] — R4-07 / R3-5 / P01 / INV-006 / DD-001 — Both per-route P01 entries are said to cover “every restored project.” Consequently, a Route B seam/control lock failure also fails Route A’s P01, contradicting the accepted policy that one route’s failure is not a veto on the other. — Define P01(A) as Route A seam, harness, and control projects plus the genuinely shared contracts/aggregator/test projects; define P01(B) analogously. A shared-project restore failure may fail both, but a route-specific project must affect only its route. The manifest remains 22 entries.

8. [severity: major] — R4-08 / R3-9 / PRH-004 / EA-008 — Non-gating; carry into RED. The bootstrap environment is not sealed before the script begins: noninteractive Bash executes `$BASH_ENV` before the controller can sanitize anything, defeating the static command allowlist. Curl also reads a default `.curlrc` unless explicitly disabled, even falling back to the passwd home when `HOME` is absent. [Bash startup behavior](https://www.gnu.org/software/bash/manual/html_node/Bash-Startup-Files), [curl default-config behavior](https://curl.se/docs/manpage.html). Raw `PATH` and certificate paths also remain unredacted because `<run-root>` and `<home>` are not exhaustive tokens. — Invoke the canonical controller with a clean environment and Bash privileged mode (`bash -p`), require `curl --disable` as its first option, add poisoning tests for `BASH_ENV` and `.curlrc`, and serialize path-bearing environment values as structured root-kind plus relative path rather than raw strings.

9. [severity: major] — R4-09 / INV-004 / INV-009 / INV-010 — Non-gating; carry into RED. P06 records raw solver resource usage when available, while all per-probe results are placed in the deterministic projection. INV-010 therefore silently requires repeated resource-count equality, despite resource-count determinism being expressly deferred to bullet 7. — Put only `solver_resource_usage_observed=true` in the deterministic projection. Either omit the raw count or classify it explicitly as a nondeterministic observation outside equality.

10. [severity: major] — R4-10 / INV-008 / AP-011 — Non-gating; carry into RED. The negative compile test passes on any build failure, so syntax damage, restore failure, or an unrelated compiler error can falsely prove compile-asset privacy. — First build an otherwise identical positive consumer successfully, then add the forbidden type use and require the expected compiler diagnostic at the planted token with no unrelated errors.

11. [severity: minor] — R4-11 / R3-12 / STRIDE TB-004 — Non-gating minor polish. R3-12 was only partially applied: spoofing and availability mention BND-004, but the STRIDE heading still names only NuGet and Z3, and tampering does not name substitution of the runtime archive, private host, `hostfxr`, or CoreLib. — Add both control runtimes to the heading and tampering row, including their archive and selected-runtime identity controls.

## Clean categories

- R3-6, R3-7, R3-10, and R3-11 are clean at specification level.
- The 22-entry manifest arithmetic is correct.
- R3-8’s CPM facts are correct: `CentralPackageVersionOverrideEnabled=false` and rejecting `VersionOverride` are the right controls; `-noAutoResponse` disables MSBuild’s automatic response files. [CPM documentation](https://learn.microsoft.com/en-us/nuget/consume-packages/central-package-management), [MSBuild response-file documentation](https://learn.microsoft.com/en-us/visualstudio/msbuild/msbuild-response-files).
- OQ-003 and OQ-004 remain properly bounded RED tasks; I do not re-litigate them.
- P12’s exact per-option target-set oracle and the hash-bound negative-fixture whitelist are clean.
- No new certain Dafny/Boogie/Z3 package-version error was found.
- `PROVER_PATH` environment behavior, the P11 canary, and meaningful DafnyPipeline consumption remain inferred/open exactly where v6 says they do.

## CONVERGENCE VERDICT

**NOT CONVERGED.**

The minimal change set that would flip the verdict is R4-01 through R4-07:

1. Normalize the route-state/reason/exit model and use structured ADR decisions.
2. Make final suite success part of COMPATIBLE.
3. Give P03 enforceable .NET 10 runtime predicates.
4. Pin the net10 control host and require per-route, same-failure control evidence.
5. Replace `SECONDS` and define an executable outer/inner watchdog topology.
6. Prove fresh per-run build-artifact provenance.
7. Partition P01 by route without changing the 22-entry manifest.

R4-08 through R4-10 may be carried explicitly by RED; R4-11 does not gate TDD.
