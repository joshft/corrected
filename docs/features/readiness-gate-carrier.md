# Feature: Readiness-Gate Carrier (phase-0.1-worker enforcement home)

> **Phase 0.1 enabling infrastructure — Stage A.** Isolated, non-shipped .NET 10
> gate solution under `gate/`. Registers no production code (`src/` stays empty).
>
> - Spec: [`.correctless/specs/readiness-gate-carrier.md`](../../.correctless/specs/readiness-gate-carrier.md) (v11)
> - Verification: [`readiness-gate-carrier-verification.md`](../../.correctless/verification/readiness-gate-carrier-verification.md)
> - Parent spec (what it enforces): [`.correctless/specs/phase-0-1-worker.md`](../../.correctless/specs/phase-0-1-worker.md)
> - Architecture: PAT-005, TB-006, the `readiness-build-gate` entrypoint in [`.correctless/ARCHITECTURE.md`](../../.correctless/ARCHITECTURE.md)

## What this does

The Phase-0.1 worker may not land production code until a machine-readable
`implementation_readiness` block in `phase-0-1-worker.md` says its preconditions
(P1 ADR-boundary, P2 validator, P3 determinism) are genuinely dischargeable. The
danger is trusting that block: anyone with commit access can flip a `satisfied`
flag, forge an ADR claim, or land a `src/` package early. This carrier is the
**fail-closed gate that re-derives the evidence** instead of trusting the flags,
and the **production-surface ban** that keeps `src/` empty while readiness is
`BLOCKED` — the RS-002 unlock the parent worker depends on.

It lives in an **exempt, non-shipped carrier** (`gate/`), *outside* the shipped
compilation closure, so the gate can enforce its own production-code ban without
tripping it (INV-036 self-enforcement; PAT-005). The solution is four projects
aggregated by `gate/Corrected.Gate.slnx`:

| Project | Role |
|---------|------|
| `Corrected.Gate.Kernel` | Pure, **I/O-free** readiness verdict: `EvaluateReadiness((block, probeResults)) → {Pass \| Fail, offending}` + the closed DTOs. No `System.IO`, no clock, no ambient state (INV-004). |
| `Corrected.Gate` | The impure edge: AST-hardened YAML/ADR parsers, the P1/P2/P3 evidence probes, the DD-003 migration-consistency gate, the INV-011 shipped-closure scanner, the INV-012 status renderer. |
| `Corrected.Gate.Tests` | xUnit suite — one `Inv0NN*Tests.cs` per invariant + `ProhibitionsTests.cs`; the SUPPLIED-fixture corpus that drives the pure kernel. |
| `Corrected.Gate.Lint` | Dafny-free ADR linter extracted so the gate build never pulls Dafny assemblies (INV-018 build insulation). |

### Flow

```mermaid
flowchart TD
    subgraph inputs["Untrusted committed input (TB-006)"]
        RB["implementation_readiness block<br/>(phase-0-1-worker.md)"]
        ADR["ADR-0001 adr_lint block<br/>+ pinned evidence sample + route-a.json"]
    end
    RB --> P["AST-hardened strict parse<br/>(closed DTO; rejects tags/anchors/aliases,<br/>duplicate & oversize blocks)"]
    ADR --> P
    P --> PROBES["Evidence probes"]
    PROBES --> P1["P1: ADR boundary + component-table<br/>(Stage A → evidence-schema-incomplete → false)"]
    PROBES --> P2["P2: validator (fail-closed on absent)"]
    PROBES --> P3["P3: determinism (fail-closed on absent)"]
    P1 --> K["Pure kernel EvaluateReadiness<br/>(READY legal iff every actual true<br/>∧ every reference Resolved — INV-005)"]
    P2 --> K
    P3 --> K
    K --> V{"Verdict"}
    V -->|"consistent BLOCKED"| PASS["INV-012 banner → stdout<br/>GATE_EXIT=0"]
    V -->|"forged READY / inconsistent"| FAIL["FAIL text → stdout<br/>GATE_EXIT≠0"]
    SCAN["INV-011 shipped-closure scan<br/>(real out-of-process dotnet build)"] --> PASS
    DD003["DD-003 migration-consistency<br/>(current-state anchors + digests)"] --> PASS
```

The **INV-011 scanner does a real out-of-process `dotnet build -t:Rebuild`** on
the pinned SDK — running generators (`EmitCompilerGeneratedFiles`), extracting the resolved
`-getItem:Compile` and `-getItem:Analyzer` item sets, and diffing an analyzer
baseline — rather than a
static path/text scan, so a generated or linked source that lands *inside* a
shipped project's built closure is caught. The **DD-003 gate** hashes the bytes
between paired current-state anchors in the parent spec against real SHA-256
digests in `readiness-migration-manifest.json`, and fails closed on a missing or
duplicate anchor, a missing/invalid manifest, or an injected appendix marker.

## How to run it

```bash
bash gate/run-readiness-gate.sh
```

Run from a clean checkout (`git clone` + `rm -rf spikes/dafny-compat/out/`). The
script is the single canonical operator **and** CI command (INV-014/INV-017). It:

1. does a locked (`--locked-mode`) restore under the pinned SDK, then runs
   `dotnet test gate/Corrected.Gate.slnx --logger "trx;LogFileName=gate.trx"`;
2. runs an **out-of-suite** TRX guard that fails on zero-discovery / a
   below-floor executed count (a bare `dotnet test` has no such guard);
3. **always** renders the INV-012 status banner to stdout (PASS-BLOCKED on green,
   FAIL text otherwise);
4. exits `0` **iff** `test_rc == 0 && trx_rc == 0 && render_rc == 0` (the combined
   exit is single-sourced in `gate/tools/combined-exit.sh`).

A recursion sentinel (`CORRECTED_GATE_INNER`) makes any gate-invoking helper
running *inside* the discovered suite a no-op. CI wires the from-clean run via
`.github/workflows/readiness-gate.yml` → `gate/ci/from-clean-gate.sh`.

Current green-from-clean state: **226/226, `GATE_EXIT=0`**, banner on stdout.

## Stage A vs Stage B

This carrier is **Stage A**: it *lands the enforcement home* but does **not**
flip readiness.

- The parent readiness block still declares `P1/P2/P3 satisfied: false` — **P1 is
  not flipped**. ADR-0001 carries its decision fields (Route A / COMPATIBLE, set
  earlier by DF-002) but the machine `status:`/`supersedes` keys are absent, so
  the P1 probe short-circuits to `evidence-schema-incomplete` → a consistent
  `BLOCKED` verdict. That is the intended Stage-A green path.
- **Stage B** — the atomic flip of `P1.satisfied` to `true`, bound to a passing
  gate and accompanied by the DD-003 manifest migration — is a separate later
  step. The gate exists so that flip can never be a bare edit ahead of evidence.

## Configuration

- **SDK pin.** Repo-root `global.json` pins SDK `10.0.302` (`rollForward:
  latestPatch`, `allowPrerelease: false`), kept semantically synced with the
  spike's pin (INV-016 / TB-004).
- **Pinned + locked packages** (per-project `packages.lock.json`, `<clear/>`
  `gate/NuGet.Config`, CPM opt-out): `YamlDotNet` 18.1.0 (hardened parse),
  `Microsoft.CodeAnalysis.CSharp` 4.14.0 (syntax scan), `Microsoft.NET.Test.Sdk`
  17.11.1 / `xunit` 2.9.2. No `Microsoft.Build.*` reference — the closure build
  is out-of-process against the pinned SDK's MSBuild (INV-015).

## Known limitations

Stage A ships three **accepted drift-debt residuals**, all dormant while the
gate is BLOCKED (tracked in `.correctless/meta/drift-debt.json`):

- **DRIFT-001 (INV-004):** kernel purity is a token-text substring scan, not a
  Roslyn semantic-symbol scan — blind to short-name/implicit-using I/O. Kernel is
  pure today; backstopped by a BCL-only project-graph bound + a behavioral
  determinism check.
- **DRIFT-002 (INV-008b):** the Dafny-family check uses `StartsWith("Dafny")`
  prefix discovery rather than exact-set membership. Fail-safe (a rogue `Dafny*`
  name yields a false-FAIL, never a forged READY); dormant because P1
  short-circuits before clause (b) runs.
- **DRIFT-003 (INV-002):** the ADR `adr_lint` block deserializes into
  `Dictionary<string,object?>` rather than a closed DTO. Bounded by Stage-1 AST
  pre-validation (tags/anchors/aliases rejected; only known keys read).

A handful of LOW/informational residuals (crafted `obj/`-Compile evasion,
zero-Compile scan-order, restore `--locked-mode` on future production paths,
temp-cleanup on hard-kill) remain accepted and are catalogued in
`.correctless/artifacts/qa-findings-readiness-gate-carrier.json`.

See the spec for the full INV-001..018 / PRH-001..008 rule set — this doc does
not duplicate it.
