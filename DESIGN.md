# Corrected — Design Document

> **Corrected** is a proof-directed implementation system for Dafny. Given an
> approved, immutable specification, it searches for an implementation and proof
> that satisfy that specification, rejects unapproved proof shortcuts, and emits
> a reproducible receipt stating exactly what was proved, built, tested, assumed,
> and still trusted.

Status: founding design doc (v1), revised 2026-07-18. Nothing here is built yet.

---

## 1. Thesis

Formal verification has a property ordinary software development lacks: a
machine-checkable acceptance condition. Dafny can prove that an implementation
satisfies a specification under Dafny's semantics, or reject the current proof
attempt with diagnostics. The proof search may time out, and an extracted
counterexample is only a diagnostic hint rather than a guaranteed witness, but a
successful proof is still a qualitatively stronger signal than a test suite or a
reviewer's judgment.

One reason formal methods remain expensive is the proof-engineering grind:
discovering invariants, termination measures, helper lemmas, frames, and
assertion hints; reading a failed obligation; and trying again without breaking
the obligations that already close. This is not the only adoption barrier—spec
authoring, model adequacy, toolchain integration, and proof maintenance remain
real costs—but it is a substantial one.

That grind is unusually well matched to agents. An agent can cheaply propose
many proof repairs, inspect structured feedback, and iterate without the fatigue
that makes repetitive proof work punishing for humans. Attempts are not free:
they consume tokens, solver resources, wall-clock time, and context, and careless
attempts can make later proof search less stable. The economic hypothesis is
therefore narrower and more testable:

> **For an already-formalized Dafny specification, a bounded agent loop can lower
> the cost of finding and maintaining an honest proof.**

Corrected is the system that tests and exploits that hypothesis.

---

## 2. Mission, scope, and assurance claim

**Mission:** given an approved Dafny specification package `S`, a manifest `M`,
and an implementation workspace, produce an implementation `I` plus an assurance
receipt `R` such that:

1. the complete declared program verifies against `S`;
2. the proof contains no unapproved logical assumptions or verification bypasses;
3. the approved semantic content of `S` and the verification configuration did
   not change during proof search;
4. the proof is acceptably stable under a pinned robustness policy;
5. the exact verified sources compile into the exact identified artifact; and
6. where contracts are executable, runtime evidence exercises the compiled
   artifact across the verifier/runtime seam.

The receipt, not a green terminal line, is Corrected's product:

```text
R = spec digest
  + implementation digest
  + complete input closure
  + toolchain versions and options
  + verification result
  + logical-assumption ledger
  + robustness evidence
  + compiled-artifact digest
  + runtime-evidence provenance
  + residual trust statement
```

### In scope

- Synthesizing executable Dafny implementations.
- Synthesizing proof annotations: loop invariants, `decreases` clauses, ghost
  code, frames, assertions, and proved helper lemmas.
- Driving a typed verify → diagnose → repair loop to bounded convergence.
- Enforcing proof honesty mechanically.
- Detecting proof vacuity and verification-scope omissions.
- Measuring proof brittleness across solver perturbations.
- Building a compiled artifact from the exact verified source closure.
- Testing executable contracts and approved `{:extern}` seams at runtime.
- Emitting reproducible evidence and an explicit residual-trust ledger.

### Explicitly out of scope

- Deciding whether the approved specification captures the human's real intent.
- Inventing missing requirements or strengthening a thin specification.
- Treating fuzzing as proof of absence.
- Proving Dafny, Boogie, Z3, a backend compiler, a runtime, or the hardware sound.
- Defending against a malicious host that can replace the trusted Corrected
  binary, rewrite the approved manifest, and forge CI results.

### The ownership boundary

Corrected owns the honesty and completeness of the proof process. It does not own
the adequacy of the approved specification.

| Failure | Example | Owner |
|---|---|---|
| **Weak spec** | `ensures Sorted(out)` omits permutation; implementation returns `[]` | Upstream. The proof is honest; the spec is thin. |
| **Vacuous handed spec** | The approved precondition is unsatisfiable | Upstream. Corrected returns `VACUOUS_INPUT`; it does not silently claim useful correctness. |
| **Mutated spec** | The implementer weakens `ensures` or changes the definition of `Sorted` | Corrected. Spec-integrity failure. |
| **Gamed verifier** | The implementation introduces `assume`, `{:axiom}`, `expect`, `{:only}`, or an unlicensed `{:extern}` | Corrected. Honesty failure. |
| **Incomplete proof scope** | An included file is used but never verified | Corrected. Scope-completeness failure. |
| **Compiler/runtime divergence** | Verified source compiles to behavior that violates an executable contract | Corrected reports the witness and the affected trust boundary; the underlying defect may live in the compiler, runtime, extern, or harness. |

### “Trusted base” means three different things

The design must not collapse these into one phrase:

1. **Specification authority** — propositions Corrected was explicitly handed as
   the contract.
2. **Logical assumption set** — unproved propositions admitted by the Dafny
   program. Corrected's default policy is zero *unapproved* entries.
3. **Toolchain and execution TCB** — Corrected, Dafny, Boogie, Z3, libraries,
   backend compiler, runtime, OS, and hardware. Corrected pins and names these; it
   does not claim to eliminate them.

“Empty trusted base beyond the spec” is therefore replaced by the precise claim:

> **Zero unapproved logical assumptions, with an explicit toolchain and runtime
> trust ledger.**

---

## 3. Relationship to Correctless

Corrected is a sibling to
[Correctless](https://github.com/joshft/correctless), not a successor and not a
sub-mode.

- **Correctless** manufactures correctness pressure where there is no complete
  oracle: adversarial review, enforced TDD, mechanical harness gates, mutation
  probes, and compounding project memory. It works across ordinary languages.
  Its assurance is probabilistic.
- **Corrected** searches against a formal acceptance condition. It applies where
  a component can be expressed in Dafny—most naturally small, stable,
  high-blast-radius cores wrapped by ordinary glue. Its proof is conclusive
  relative to the approved model and the named logical assumptions.

### Where the road forks

After a formal Dafny spec exists, work is routed by formalizability × stakes:
small, algorithmic, stable, high-blast-radius components are candidates for
Corrected; UI, orchestration, integration-heavy behavior, and rapidly changing
glue usually stay on the Correctless or ordinary path.

Routing is asymmetric: most software should not be rewritten in Dafny. A single
feature can contain a verified core and TDD'd glue.

For v0, Corrected accepts Dafny as the specification language. If a future
version accepts a higher-level spec and lowers it to Dafny, the lowering becomes
a new faithfulness boundary owned by Corrected. It cannot be treated as free
preprocessing.

### Where the road re-merges

A verified Dafny core compiles down and is consumed by unverified code. The glue
may call it outside its intended domain, serialize values incorrectly, ignore a
failure result, or substitute a native `{:extern}` body that does not satisfy its
declared contract.

The verified/unverified seam receives *more* scrutiny, not less:

- explicit exported-entrypoint manifests;
- generated integration wrappers;
- runtime checks for executable contract fragments;
- independent tests for extern bodies;
- artifact and harness digests in the receipt; and
- ordinary Correctless pressure on the consuming glue.

### What carries over from Correctless

Corrected starts from Correctless's mature lessons, including the lessons learned
after its first implementation:

- Structural enforcement over prompt instruction.
- No agent grades its own work where independent review is available.
- Bounded convergence with an honest give-up state.
- Versioned, machine-checked registries instead of prose-only policy.
- Content fidelity: a gate must prove it inspected the artifact it claims to
  inspect.
- Gate scope: a pre-delivery gate must cover the gate that follows delivery.
- Extraction/rejection: parsers must define adversarial inputs they reject.
- Authoring affordance: an immutable or protected asset needs a legitimate update
  path.
- Mechanism-capability honesty: a guardrail must not be described as a security
  perimeter.
- A compounding memory loop whose new rules are wired into enforcement and tests,
  not merely recorded in prose.

---

## 4. Why this is an agent-shaped problem

The verifier constrains an agent's worst trait while exploiting a useful one.
Plausible but false annotations usually fail verification. The agent may propose
many wrong invariants, but those proposals need not become accepted facts.

The useful abstraction is:

> **Corrected = a learned proposal distribution over implementations, invariants,
> lemmas, frames, and termination measures, wrapped in a deterministic acceptance
> policy and a bounded search controller.**

This is only conditionally a “sound rejection sampler.” Soundness depends on:

- the specification remaining immutable;
- the full program being in verification scope;
- the logical-assumption policy being complete;
- the verifier and translation toolchain being trusted;
- the agent being unable to rewrite the acceptance policy; and
- successful verification, rather than timeout or diagnostic ambiguity.

The LLM proposes; the checker disposes. The controller preserves useful progress,
rejects regressions and shortcuts, and stops when the search no longer improves.

### Diagnostics are hints, not truth

Dafny may report a failing postcondition, an assertion failure, a timeout, or a
potential counterexample. Extracted counterexamples are not guaranteed witnesses
and may contain inconsistent assumptions. Corrected therefore normalizes tool
output into typed *repair signals*, never into unquestioned facts:

```text
parse_or_resolve_error
verification_failure
termination_failure
timeout_or_resource_exhaustion
potential_counterexample
honesty_policy_violation
vacuity_or_contradiction_warning
robustness_regression
build_failure
runtime_contract_failure
infrastructure_invalid
```

---

## 5. Threat model and success predicate

### The optimizer being constrained

Corrected assumes an implementation agent that is cooperative but strongly
optimized for acceptance. Under repeated failure it may:

- weaken a contract;
- introduce an assumption or runtime `expect`;
- skip a method or file;
- strengthen a precondition and shrink the valid domain;
- disable termination checking;
- move code behind `{:extern}` or `{:compile false}`;
- change verifier options;
- edit the test generator or runtime oracle;
- run a gate against a stale worktree or a different binary; or
- stop after the one seed on which a brittle proof happens to pass.

This is reward hacking, not necessarily malice.

### Authority boundary

The agent may edit implementation and proof files. It must not have authority to
replace:

- the approved spec package or its approval digest;
- the acceptance manifest;
- the trusted Corrected executable/container;
- the CI policy that validates the receipt; or
- the approved exception registry.

Local file permissions and host tool allowlists are useful guardrails, but the
actual boundary is independent recomputation in trusted CI from an approved
manifest and a pinned Corrected distribution. If the same agent can rewrite the
checker and the policy that approves the rewrite, no security claim survives.

### Mechanical success predicate

A run is successful only if all required conditions hold:

```text
spec_integrity
∧ input_closure_complete
∧ verify_passed
∧ unapproved_logical_assumptions = ∅
∧ prohibited_verification_controls = ∅
∧ vacuity_policy_passed
∧ robustness_policy_passed
∧ verified_source_digest = compiled_source_digest
∧ required_runtime_evidence_passed
∧ receipt_schema_valid
```

No skill or agent may redefine this conjunction.

---

## 6. Input contract: the manifest and immutable spec package

Corrected runs from a manifest rather than inferred conversation state.

Illustrative `corrected.toml`:

```toml
schema_version = 1
dafny_version = "4.11.0"

spec_roots = ["spec/"]
implementation_roots = ["src/"]
entrypoints = ["VerifiedCore.Sort"]
verify_included_files = true
targets = ["cs"]

approved_spec_digest = "sha256:..."
approved_policy_digest = "sha256:..."

[verification]
warn_contradictory_assumptions = true
warn_redundant_assumptions = true
allow_library_files = false

[budgets]
repair_attempts = 30
no_progress_attempts = 5
resource_limit = 200000
robustness_iterations = 10

[[allowed_assumptions]]
id = "EXT-001"
kind = "extern"
symbol = "NativeEntropy.Fill"
justification = "OS entropy provider; checked by runtime seam campaign"
```

### What is immutable

The approved spec digest covers the semantic closure of the contract:

- `requires`, `ensures`, invariants, frames, and termination clauses;
- predicates and functions referenced by those clauses;
- types, newtypes, datatypes, constants, and representation invariants;
- imports, includes, abstract modules, refinements, and export sets;
- all spec-affecting attributes;
- project configuration and verifier options that can alter meaning or scope; and
- the exact file list or dependency closure from which the above are resolved.

Textual `requires`/`ensures` immutability alone is insufficient: changing the
definition of `Sorted`, an imported abstract declaration, or a module option can
change the contract without touching either keyword.

The first implementation should hash exact approved bytes plus a deterministic
file manifest. A later AST digest is acceptable only after canonicalization is
proved stable across the pinned Dafny version. `dafny format` is a style gate, not
a semantic canonicalizer.

### Legitimate update affordance

Corrected never silently edits the approved package. If proof search exposes a
spec defect:

1. stop with `SPEC_ESCALATION`;
2. emit the residual obligation and proposed upstream issue;
3. let the human or upstream spec process modify the spec;
4. produce a new approval digest; and
5. start a new certification lineage.

The prior receipt remains attached to the prior digest.

### Content-fidelity invariant

Every derived substrate—worktree, container, generated wrapper, translated
source tree, compiled output, or fuzz harness—must carry and re-check the source
digest it claims to represent. Isolation proves that mutations do not leak
outward; it does not prove that the isolated copy contains the right code.

---

## 7. Core proof-search loop

```text
approved spec S + manifest M
          │
          ▼
preflight: validate authority, versions, paths, digests, and complete input closure
          │
          ▼
generate or repair implementation + proof annotations
          │
          ▼
resolve + verify complete closure
     fail │             │ pass
          ▼             ▼
typed diagnostic     honesty policy
          │             │ fail
          │◄────────────┘
          ▼
transactional repair attempt
          │
          └─────────────── loop while progress and budget remain

verify + honesty pass
          │
          ▼
vacuity / proof-dependency checks
          │
          ▼
robustness policy
          │
          ▼
build exact verified sources
          │
          ▼
runtime evidence required by manifest
          │
          ▼
independent adversarial review of residual trust
          │
          ▼
signed or CI-attested assurance receipt
```

### Progress and stuck detection

Each obligation receives a stable fingerprint derived from symbol, diagnostic
class, source span, and normalized message. The controller tracks:

- number of open obligations;
- change in obligation classes;
- verification resource count;
- proof-dependency and contradiction warnings;
- diff size and proof-surface growth;
- regressions in previously closed symbols; and
- repeated fingerprints across attempts.

Each repair is transactional:

1. start from the last accepted checkpoint;
2. apply one coherent repair;
3. run the cheapest relevant checks;
4. run the complete verification gate before checkpointing; and
5. discard the repair if it regresses the accepted state without a measured
   compensating improvement.

After the no-progress or total-attempt budget is exhausted, Corrected returns
`INCOMPLETE` with the best checkpoint and residual proof state. Budget exhaustion
is never converted into success.

---

## 8. Deterministic assurance stack

Guiding principle:

> **Push each concern toward a deterministic, independently repeatable check;
> spend an agent only on the semantic residue.**

### 8.1 Preflight and formatting

- Validate manifest schema and path containment.
- Pin the Corrected and Dafny distributions by version and digest.
- Resolve every declared and transitive file.
- Reject unapproved config files and option sources.
- Run `dafny format --check` for stable style and clean diffs.

Formatting is not used to define semantic identity.

### 8.2 Complete verification

Run `dafny verify` over the explicit complete program closure. The policy must
account for Dafny's default behavior of verifying listed files rather than every
included file:

- enable and verify included-file behavior;
- disallow unapproved `--library` exclusions;
- disallow symbol filters in certification mode;
- record the exact effective options; and
- ensure the same options apply consistently across all files.

Partial verification is a valid development operation, never a certification
operation.

### 8.3 Honesty policy

`dafny audit` is a valuable first-party detector, but it is not the whole policy.
It is documented as under development and exits successfully even when findings
exist. Corrected must parse or compare its report and fail on unapproved entries;
checking the process exit status alone is incorrect.

The audit is complemented by a parser- or AST-based policy over at least these
classes:

**Direct assumptions**

- `assume`, including assume forms of assign-such-that and failure updates;
- `expect` when it contributes a proposition to verification;
- `{:assumption}` and equivalent assumption-producing constructs.

**Verification suppression**

- `{:axiom}`;
- `{:verify false}`;
- declaration- or assertion-level `{:only}`;
- selective-checking controls;
- bodyless declarations, loops, and `forall` statements that contribute facts;
- unapproved abstract declarations whose postconditions are consumed.

**Termination and partial-correctness escapes**

- `decreases *`;
- termination-suppression attributes or options;
- any exported method whose total-correctness policy is weaker than the manifest.

**Scope and compilation escapes**

- `--no-verify`, unapproved `--library`, and symbol filtering;
- `{:compile false}` or ignored declarations on required entrypoints;
- generated code or linked artifacts not represented in the manifest.

**Contract mutation**

- changed spec dependencies;
- automatically synthesized or strengthened entrypoint preconditions unless the
  approved spec explicitly permits them;
- module-level options that change verification semantics or scope.

**External trust**

- `{:extern}` declarations and native bodies;
- replaceable/abstract modules and preverified libraries;
- every exception not tied to a stable symbol, kind, justification, and evidence
  obligation in the approved manifest.

The policy is deny-by-default. A regex scanner may provide fast feedback but
cannot be the certification mechanism: comments, alternate grammar forms,
attributes, module options, and desugaring make lexical completeness too fragile.

### 8.4 Vacuity and proof dependencies

Certification enables Dafny's contradictory- and redundant-assumption analysis
and retains the proof-dependency report.

- Contradictory assumptions are blocking unless a narrowly approved proof by
  contradiction is mechanically identified.
- An implementation-introduced empty domain is an honesty failure.
- An unsatisfiable handed precondition is `VACUOUS_INPUT`, routed upstream.
- Unused implementation statements and partially used specification regions are
  surfaced as assurance gaps, not silently discarded.

Proof-dependency analysis is informative rather than perfect: solver unsat cores
need not be minimal. The receipt records both warnings and policy disposition.

### 8.5 Robustness

Use Dafny's `measure-complexity` workflow, repeated solver perturbations, resource
counts, and a pinned resource budget. Record:

- outcome per iteration and assertion batch;
- coefficient of variation of solver resources;
- maximum resource count;
- proof goals with mixed pass/fail outcomes; and
- change against the accepted baseline.

The default certification policy rejects a proof that passes only under lucky
conditions. Thresholds are project policy, versioned in the manifest. Pinning the
toolchain is still required: seed stability does not guarantee upgrade stability.

### 8.6 Ablation

Ablation is a secondary experiment, not the primary honesty gate.

- Removing an unapproved assumption should already be unnecessary because the
  assumption is forbidden.
- Removing an approved external assumption can demonstrate which obligations
  depend on it.
- Removing a proved lemma and causing verification failure does **not** make the
  lemma suspicious; a load-bearing proved lemma is often exactly what a proof
  should contain.
- Source ablation perturbs solver search and may turn a logically unnecessary
  lemma into an operationally useful stability aid.

Use proof dependencies first. Use ablation for targeted hypotheses, and report
its result as experimental evidence rather than a canonical dependency graph.

### 8.7 Exact-source build

The build step consumes the same resolved source closure and effective
configuration recorded by verification. Before and after translation:

- recompute source and config digests;
- reject any mismatch;
- record generated-source and binary digests;
- record backend compiler/runtime versions; and
- run the configured target-language build without `--no-verify`.

### 8.8 Differential compilation

Multiple backends can expose backend-specific divergence, but agreement is not an
independent proof:

- backends share Dafny's front end and may share a translation bug;
- raw output equality may be invalid across representations and environments;
- nondeterministic or effectful entrypoints need an explicit observational
  equivalence relation.

Differential testing is optional evidence over manifest-declared comparable
observations, not an oracle requiring no assumptions.

---

## 9. Adversarial checking

Z3 owns the verified interior only after the deterministic policy establishes
what was actually verified. The adversarial layer patrols residual boundaries:

- completeness of the logical-assumption policy;
- manifest and input-closure omissions;
- semantic spec mutation that escaped hashing or dependency extraction;
- vacuity and domain shrinkage;
- extern and abstract-module trust;
- content fidelity of worktrees, wrappers, builds, and runtime harnesses;
- runtime-oracle lowering;
- exception justifications; and
- whether the assurance language exceeds the evidence in the receipt.

The implementer is a poor reviewer of the path it just optimized. A fresh,
adversarially framed reviewer is valuable, but portability changes the strength
of that guarantee.

### Capability profiles

| Profile | Agent separation guarantee |
|---|---|
| **Minimum** | One agent may implement and review; deterministic certification remains authoritative. Review is advisory. |
| **Enhanced** | A fresh reviewer context receives source plus deterministic reports. |
| **Strong** | Reviewer is tool-pinned read-only and runs against a content-verified immutable snapshot. |

Corrected must report the active profile. A general skill cannot claim structural
agent separation on a host that does not expose fresh contexts or tool pinning.

### Findings as experiments

Review findings should become falsifiable experiments whenever possible:

- “This extern is load-bearing” → remove or stub it in an isolated, digest-checked
  experiment and observe affected obligations.
- “The precondition is vacuous” → request or synthesize a domain witness.
- “This harness tests the wrong artifact” → compare embedded and actual digests.
- “This backend violates the contract” → retain the concrete runtime witness.

Some findings remain semantic judgments—especially harness adequacy, exception
justification, and observation equivalence. The verifier does not adjudicate
those automatically, and the receipt must not imply that it did.

---

## 10. Runtime evidence across the compiler and extern seam

Verification proves properties of Dafny source under Dafny's model. Runtime
evidence observes a compiled artifact. These are different objects, and their gap
contains the Dafny translation pipeline, backend compiler, runtime, native
externs, serialization, and consuming glue.

Runtime evidence is valuable because a failing input is a concrete witness. It is
not “trust-free ground truth”: the harness, generator, oracle lowering, process
supervisor, corpus, and artifact selection are themselves part of the evidence
pipeline.

### Executable contract subset

Not every Dafny contract is directly executable. Contracts may reference:

- ghost values or ghost functions;
- quantification over unbounded domains;
- mathematical values with no bounded runtime representation;
- old heap state that requires snapshots;
- abstract or opaque predicates; or
- target-language features with different resource bounds.

Corrected classifies each entrypoint contract:

```text
native_runtime_check
generated_runtime_check
extern_assumption_check
non_executable_contract
unsupported
```

Dafny's experimental runtime checking for compilable assumptions should be used
where applicable. Generated wrappers cover only a defined, tested subset. A
contract that cannot be executed remains proven in-model but receives no runtime
evidence claim.

### Generating valid inputs is not free

A `requires` clause is a validity predicate, not automatically an efficient
generator. Narrow relational preconditions can make rejection sampling useless.
The runtime campaign may use:

- manifest-supplied generators;
- bounded enumeration;
- constraint-directed generation;
- property-based generators with shrinkers; or
- rejection sampling only when measured acceptance is adequate.

Every campaign records generated, rejected, executed, and unique-case counts.
Zero or negligible valid inputs invalidates the campaign rather than producing a
green result.

### Runtime verdicts

For every executed valid input:

- a violated executable postcondition fails;
- an unexpected `expect` abort fails;
- a crash fails;
- a timeout or hang fails;
- serialization or wrapper disagreement fails; and
- divergent comparable backends retain the input and observations.

### Harness integrity

The runtime harness is generated or reviewed independently from the
implementation and is outside the implementation agent's write authority during
certification. The receipt binds:

- verified source digest;
- translated/generated-source digest;
- binary digest;
- wrapper and generator digest;
- contract-lowering version;
- backend/runtime version;
- corpus digest and seeds; and
- coverage and valid-input statistics.

A source-verified/runtime-failing witness narrows the defect to the trust gap, but
does not automatically distinguish a compiler bug from a lying extern, wrong
wrapper, bad oracle lowering, or harness defect. Triage remains evidence-driven.

---

## 11. Assurance levels and receipt

Corrected does not collapse all evidence into one “green.”

| Level | Required evidence | Claim |
|---|---|---|
| `CHECKED` | Manifest and spec integrity; scope closure; honesty policy | The certification input is well formed and contains no unapproved bypass known to policy. |
| `VERIFIED` | `CHECKED` + complete Dafny verification + vacuity policy | The source satisfies the approved spec under the named model and logical assumptions. |
| `ROBUST_VERIFIED` | `VERIFIED` + robustness thresholds | The proof also meets the pinned solver-stability policy. |
| `BUILT` | `ROBUST_VERIFIED` + exact-source build and artifact digest | The named artifact was built from the named verified source closure. |
| `SEAM_TESTED` | `BUILT` + all required executable-contract/extern campaigns | The named artifact additionally passed the declared sampled runtime evidence. |
| `INCOMPLETE` | Any required gate absent, failed, or invalid | No stronger claim is emitted; residual state and evidence are retained. |

The final receipt includes the achieved level and all lower-level evidence. It
also includes:

- active agent-separation capability profile;
- unsupported and non-executable contract regions;
- approved assumptions and their consumers;
- skipped optional evidence;
- policy exceptions;
- robustness thresholds;
- runtime campaign adequacy statistics; and
- the complete toolchain TCB.

“Corrected” is the product name. It is not permission to omit qualifiers from the
receipt.

---

## 12. Delivery model

Corrected is toolchain-centered and host-adaptable.

### CLI as the sole structural implementation

Candidate commands:

- `corrected check` — validate manifest, spec integrity, proof scope, and honesty
  policy.
- `corrected repair` — run the bounded proof-search controller.
- `corrected robust` — run proof-dependency and solver-stability policy.
- `corrected build` — build exact verified sources and bind artifact provenance.
- `corrected test` — run declared executable-contract and extern campaigns.
- `corrected certify` — run the required superset and emit the final receipt.
- `corrected explain` — render a receipt or residual proof state for humans.

The subcommands share one library and one schema. `certify` is not a prose
orchestrator that reimplements their rules; it invokes the same coded entrypoints.

### Skills as choreography

Skills provide:

- task intake and scope explanation;
- bounded repair strategy;
- human escalation;
- adversarial review;
- receipt explanation; and
- host-specific progress reporting.

Skills never decide that certification passed. They consume structured CLI output.

### MCP as a thin adapter

An MCP server may expose the CLI operations as tools, but it must call the same
library/CLI implementation and return the same schemas. A second MCP-native
implementation would create an immediate dual-source-of-truth problem.

### Trusted CI

Local runs optimize iteration speed. Trusted CI independently:

1. checks out the approved source commit;
2. loads the approved manifest/policy;
3. runs a pinned Corrected distribution;
4. recomputes all digests;
5. executes `corrected certify`; and
6. attests the receipt and artifact.

The pre-delivery local gate must be a superset of this required certification
scope or must explicitly state which CI-only evidence remains.

### Compounding memory

Escaped failures become:

- a minimized sabotage fixture;
- a versioned policy rule;
- a parser/AST regression test;
- a repair-loop benchmark; and
- where relevant, an adversarial review lens.

No lesson is considered integrated until a structural registry and set-equality
or completeness test bind the documented rule to its consumer.

---

## 13. MVP sequence and evaluation

The implementation order deliberately proves the acceptance mechanism before
optimizing the agent.

### Phase 0 — Deterministic checker and receipt

Build without any LLM dependency:

- manifest and digest model;
- complete verification-scope resolution;
- Dafny audit integration;
- supplemental honesty policy;
- vacuity/proof-dependency policy;
- exact-source build provenance; and
- versioned receipt schema.

Exit criterion: every planted bypass is rejected, honest reference programs are
accepted, and repeated certification of the same inputs produces equivalent
receipts.

### Phase 1 — Narrow bounded repair engine

Support one constrained task family first, such as pure sequence algorithms with
bounded loops and executable contracts. Implement typed diagnostics,
transactional attempts, progress fingerprints, rollback, and escalation.

Exit criterion: better honest-proof completion or lower cost than a plain agent
on a held-out benchmark, without reducing cheat detection.

### Phase 2 — Robustness and proof maintenance

Add solver perturbation, resource thresholds, proof-dependency reporting, and
baseline comparison across harmless source/toolchain changes.

Exit criterion: brittle proofs are detected before acceptance and robust proofs
remain reproducible under the pinned policy.

### Phase 3 — Runtime seam evidence

Add executable-contract classification, exact-artifact wrappers, generators,
extern campaigns, and campaign-adequacy metrics.

Exit criterion: planted extern, wrapper, wrong-artifact, crash, hang, and backend
divergence fixtures are all detected.

### Phase 4 — Host adapters and broader routing

Only after the structural core is stable:

- add skills for supported hosts;
- add the MCP adapter;
- add enhanced/strong reviewer profiles;
- broaden the Dafny task corpus; and
- consider formalizability routing.

### Evaluation corpus

The benchmark suite contains:

1. **Honest solvable tasks** — known implementations and proofs hidden from the
   agent.
2. **Honest unsolved/over-budget tasks** — expected `INCOMPLETE`, testing truthful
   escalation.
3. **Vacuous specs** — contradictory or empty handed domains.
4. **Cheat corpus** — every assumption, verification suppression, scope omission,
   termination escape, contract mutation, and extern trick supported by the
   pinned Dafny version.
5. **Substrate corpus** — stale worktrees, wrong binaries, mutated wrappers,
   mismatched config, and incomplete source closure.
6. **Robustness corpus** — proofs with known seed/resource instability.
7. **Runtime corpus** — lying externs, compiler-wrapper mismatches, crashes,
   hangs, and insufficient valid-input generation.

### Metrics

- honest completion rate;
- cheat recall and false-positive rate;
- spec-integrity and scope-completeness recall;
- attempts, tokens, solver resources, and wall-clock time;
- regression rate across accepted checkpoints;
- robustness outcome variance;
- receipt reproducibility;
- runtime valid-input rate and behavioral coverage;
- unsupported-contract fraction; and
- rate of correct `INCOMPLETE`/`SPEC_ESCALATION` outcomes.

Baselines include a plain coding agent with Dafny access, the same agent with a
simple “do not cheat” prompt, and—where available—human-authored reference proofs.

---

## 14. Open questions

- **Proof-search performance:** Which diagnostic representation, patch
  granularity, and checkpoint metric best prevent oscillation?
- **First supported fragment:** Which Dafny subset is narrow enough for reliable
  proof search but valuable enough to validate the product?
- **AST integration:** Should Corrected use Dafny libraries, a Dafny plugin, or a
  stable resolved-program export for honesty and semantic-closure analysis?
- **Safe option policy:** Which attributes, project options, library artifacts,
  and module features are allowed in certification mode?
- **Spec satisfiability:** What bounded or proof-based witness policy is strong
  enough to reject vacuity without turning Corrected into a spec-design system?
- **Approved libraries:** How are independently verified `.doo`/library artifacts
  pinned, audited, and represented in the logical-assumption ledger?
- **Executable contracts:** What Dafny fragment can be lowered into runtime
  checks with a tested semantics-preservation argument?
- **Input generation:** When should Corrected require user-supplied generators
  rather than attempt constraint-directed generation?
- **Extern policy:** Which extern categories are acceptable, and what runtime
  evidence is mandatory for each?
- **Differential observations:** How are cross-backend observational equivalence
  and nondeterminism specified?
- **Toolchain upgrades:** What evidence permits moving a project from one pinned
  Dafny/Z3/backend version to another?
- **Receipt attestation:** Is trusted CI signing enough, or should Corrected
  support a standard provenance format such as in-toto/SLSA attestations?
- **Agent portability:** Which host capabilities are required for enhanced and
  strong independent-review profiles?
- **Maintenance economics:** Does the repair engine reduce the cost of keeping
  proofs working as implementations and pinned toolchains evolve?

---

## 15. Current Dafny facts this design depends on

The first implementation must verify these against the pinned release rather
than treating documentation as timeless:

- Current target release at revision time: Dafny 4.11.0.
- `dafny audit` reports multiple assumption/suppression classes, is documented as
  under development, and returns success even when it emits findings.
- `dafny verify` does not verify every included file by default.
- extracted counterexamples are potential explanations, not guaranteed witnesses.
- proof-dependency warnings and verification-coverage reports are available.
- `measure-complexity` and resource reports support brittleness analysis.
- `--test-assumptions` is experimental and only inserts runtime checks when the
  assumptions are compilable.
- compilation aims at semantic fidelity but target resource and language limits
  can prevent perfect correspondence.

Primary references:

- [Dafny 4.11.0 release](https://github.com/dafny-lang/dafny/releases/tag/v4.11.0)
- [Current Dafny Reference Manual](https://dafny.org/dafny/DafnyRef/DafnyRef)
- [Dafny proof-dependency analysis](https://dafny.org/blog/2023/10/27/proof-dependencies/)
- [Dafny verification optimization guide](https://dafny.org/v4.9.1/VerificationOptimization/VerificationOptimization)

---

*Corrected is responsible for the honesty and reproducibility of the proof
process, not the adequacy of the approved spec: zero unapproved logical
assumptions, complete proof scope, exact artifact provenance, sampled evidence at
the runtime seam, and explicit trust everywhere in between.*
