# Corrected — Design Document

> **Corrected** is an open-source proof-directed verification worker and
> certification toolchain. Given a versioned Dafny program, frozen formal
> obligations, and an explicit edit policy, it searches for an allowed
> implementation or proof patch, rejects unapproved proof shortcuts, and emits
> reproducible evidence stating exactly what was proved, built, tested, assumed,
> and still trusted.

Status: founding design doc (v1.8), revised 2026-07-19. Nothing here is built
yet. The intended implementation is open source.

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

### What the 2026 landscape does to this bet

Web-verified as of 2026-07-19 (protocol details and citations in §15):
proof-completion capability is improving quickly, but the headline rates are
easy to overread. DafnyPro reports 86.2% on the fixed-program DafnyBench
proof-annotation task; AxDafny reports 92.7% on the same task, but 56.4% on its
end-to-end implementation-and-proof benchmark and 28.0% on that benchmark's
hard split. A separate cross-substrate benchmark reports 89% for its strongest
individual model on DafnyBench and 96% only for the union of successful
attempts across several models. Multi-function compositional verification
remains sharply harder than single-function completion.

The field also already recognizes verifier gaming. DafnyPro has a parser-based
diff checker; AxDafny freezes supplied specifications and rejects several proof
bypasses; the vericoding benchmark validates generated blocks for known cheats;
and the Verus project explicitly recommends an independent cheat checker for
LLM proof work. These are task-specific controls, not the complete,
policy-bound certification system described here. Corrected's contribution is
to integrate and generalize that emerging practice: protected-surface
enforcement, complete proof scope, explicit assumption policy, independent
replay, standard attestations, and exact artifact provenance behind one public
interface.

The premise therefore narrows rather than inverts. Routine proof search is
becoming more capable and replaceable; difficult synthesis, decomposition, and
proof maintenance are not yet commodities. The deterministic acceptance layer
is durable infrastructure, while search remains an experimentally measured,
replaceable subsystem. As an open-source project, Corrected does not depend on
an "unoccupied market" claim. Its value is a reusable trust boundary that
models, agents, verifiers, CI systems, and upstream development methods can
share.

---

## 2. Mission, scope, and assurance claim

**Mission:** given a Dafny program and specification package `S` — from any
source, frozen at intake — an optional human manifest `M`, an explicit task mode
and ownership policy, and a candidate workspace, resolve a mandatory
certification lock `L`, produce an allowed patch `P`, and emit an assurance
receipt `R`. The receipt records independent evidence facts and states whether
they satisfy the requested assurance profile.

The default v0 task mode is **proof completion**: executable code and exported
contracts are frozen, while explicitly classified proof annotations and ghost
helpers are editable. **Implementation synthesis** permits selected executable
bodies to change and is a separate, more difficult task mode evaluated
independently.

At minimum, Corrected establishes whether:

1. the complete declared program verifies against `S`;
2. the proof contains no unapproved logical assumptions or verification bypasses;
3. the protected semantic content of `S`, any task-mode-frozen executable
   content, and the effective verification plan did not change during proof
   search;
4. the proof meets the requested pinned robustness policy;
5. the exact verified input closure produced the identified artifact; and
6. where contracts are executable and the target profile requires it, runtime
   evidence exercises that artifact across the verifier/runtime seam.

The receipt, not a green terminal line, is Corrected's primary deliverable:

```text
R = run status + requested profile + profile verdict
  + certification-subject identity (intake + lock + verified closure)
  + intake-snapshot and protected-surface identities and provenance
  + exact candidate-source-tree identity
  + resolved verification-plan and policy identities
  + verification, honesty, vacuity, and robustness facts
  + optional specification-strength and semantic-anchor facts
  + artifact provenance and compiled-artifact identity
  + runtime-evidence provenance
  + methodology-evidence identity and isolation facts
  + residual trust statement
```

### In scope

- Synthesizing proof annotations: loop invariants, `decreases` clauses, ghost
  code, frames, assertions, and proved helper lemmas.
- Repairing and maintaining proofs while executable code and exported contracts
  remain frozen.
- Synthesizing executable Dafny implementations only in an explicitly locked
  implementation-synthesis task mode.
- Driving a typed verify → diagnose → repair loop (Pi-enforced in v0) to
  bounded convergence.
- Enforcing proof honesty mechanically.
- Detecting proof vacuity and verification-scope omissions.
- Reporting optional, non-conclusive specification-strength signals from
  mutation checks and independent semantic anchors.
- Measuring proof brittleness across solver perturbations.
- Building a compiled artifact from the exact verified source closure.
- Testing executable contracts and approved `{:extern}` seams at runtime.
- Emitting reproducible evidence and an explicit residual-trust ledger.

### Explicitly out of scope

- Deciding whether the handed specification captures the human's real intent.
- Inventing missing requirements or strengthening a thin specification.
- Claiming that mutation testing, examples, reference behavior, or other
  semantic anchors prove specification adequacy.
- Treating fuzzing as proof of absence.
- Proving Dafny, Boogie, Z3, a backend compiler, a runtime, or the hardware sound.
- Defending against a malicious host that can replace the trusted Corrected
  binary, rewrite the certification lock or policy, and forge CI results.

### The ownership boundary

Corrected owns the honesty and completeness of the proof process. It does not
own the adequacy of the handed specification — by design it cannot:
source-agnostic intake means Corrected does not even know where the spec came
from. When configured, it can nevertheless try to falsify a weak specification
with mutation checks and compare it with independent semantic anchors. Those
results are reported as specification-strength evidence, never promoted into a
proof of intent.

| Failure | Example | Owner |
|---|---|---|
| **Weak spec** | `ensures Sorted(out)` omits permutation; implementation returns `[]` | Upstream. The proof is honest; the spec is thin. Corrected reports any configured mutation or semantic-anchor witness that exposes the gap. |
| **Vacuous handed spec** | The handed precondition is unsatisfiable | Upstream. Corrected returns `PROVEN_VACUOUS` only when it has a proof; otherwise it reports witnessed non-vacuity or `VACUITY_UNKNOWN`. |
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
  relative to the handed model and the named logical assumptions.

### Coupling direction

The dependency arrow points one way: **the Corrected implementation never knows
Correctless exists; Correctless integrates Corrected.** This document discusses
the project relationship, but the executable boundary is Corrected's neutral
I/O contract — a versioned verification task bundle in; an allowed patch,
receipt, and machine-readable witnesses (`SPEC_ESCALATION`, vacuity,
specification-strength, and coverage signals) out; all versioned schemas.
Correctless is the *reference producer and consumer* of those artifacts: its
spec pipeline can mint a standard intake attestation; its tests, examples, and
reference implementation can be supplied as independent semantic anchors; its
review loop can consume escalations; and that integration code lives on the
Correctless side. Any other upstream — a hand-authored spec directory, a
different spec system — uses the identical contract. Corrected is the git
layer; Correctless is one host that builds on it.

### Where the road forks

After a formal Dafny spec exists, work is routed by formalizability × stakes:
small, algorithmic, stable, high-blast-radius components are candidates for
Corrected; UI, orchestration, integration-heavy behavior, and rapidly changing
glue usually stay on the Correctless or ordinary path.

Routing is asymmetric: most software should not be rewritten in Dafny. A single
feature can contain a verified core and TDD'd glue. The routing decision itself
is upstream's (in Correctless, a natural `/cspec`-phase output alongside the
intensity recommendation); Corrected exposes no routing surface.

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

> **Corrected = replaceable proposal strategies over implementations,
> invariants, lemmas, frames, and termination measures, wrapped in a
> deterministic acceptance policy and a bounded search controller.**

The LLM is the broadest proposal strategy, not necessarily the first one.
Within supported fragments, Corrected may first try direct verification,
library or project-local proof retrieval, deterministic templates, or bounded
SyGuS/CEGIS-style synthesis. The agent handles the semantic residue and chooses
decompositions when cheaper strategies fail. Strategy choice affects cost and
completion evidence, never the acceptance predicate.

This is only conditionally a “sound rejection sampler.” Soundness depends on:

- the specification remaining immutable;
- the full program being in verification scope;
- the logical-assumption policy being complete;
- the verifier and translation toolchain being trusted;
- the agent being unable to rewrite the acceptance policy; and
- successful verification, rather than timeout or diagnostic ambiguity.

A proposal strategy suggests; the checker disposes. The controller preserves
useful progress, rejects regressions and shortcuts, and stops when the search no
longer improves.

### Pi is the v0 agent runtime

Corrected deliberately targets [Pi](https://pi.dev) as its v0 agent harness.
Host-agnostic certification remains a property of the deterministic core; an
agent-agnostic development harness is not a v0 goal.

The architecture has two trusted project components:

```text
Corrected Core
  intake, ownership policy, Dafny integration, gates, digests, receipts
          ▲
          │ versioned structured commands and results
          ▼
Corrected Pi Extension
  phase state, active tools, repair loop, checkpoints, review, user interaction
          ▲
          │ constrained domain tools and proposal results
          ▼
Proposal Strategies
  search/review models, retrieval, templates, bounded synthesizers
```

The Pi extension owns methodology:

- the current development phase and legal transitions;
- which tools are active in each phase;
- attempt budgets, progress state, checkpoints, and rollback;
- normalization and presentation of verifier diagnostics;
- fresh-session adversarial review;
- user interaction, interruption, resumption, and escalation; and
- durable session evidence needed to explain the search.

The core owns acceptance:

- the frozen intake snapshot and resolved certification lock;
- protected/editable AST classification;
- complete Dafny resolution and verification scope;
- honesty, vacuity, specification-strength, robustness, build, and runtime
  gates;
- methodology-evidence validation;
- content-addressed candidate and artifact identities; and
- receipt construction and profile evaluation.

The extension may orchestrate `certify`; it cannot create, edit, or reinterpret a
successful core result. Phase 0 certification remains runnable with no model and
no Pi session.

Pi extensions are trusted executable code and run with the permissions of the Pi
process. The pinned Corrected extension is therefore part of the named TCB, not a
security boundary. Project-supplied extensions, prompts, and settings are
untrusted certification inputs unless the lock explicitly approves their
digests.

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

Corrected assumes a search agent that is cooperative but strongly
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

The search model does not receive general filesystem or shell authority.
It may propose changes only through phase-appropriate Corrected tools. It must
not have authority to replace:

- the frozen intake spec package or its identity digest;
- the resolved certification lock or effective policy;
- the trusted Corrected executable/container;
- the pinned Corrected Pi extension;
- the CI policy that validates the receipt; or
- the lock-pinned exception registry.

The Pi extension disables or replaces general write, edit, and shell tools during
the managed workflow. Models receive ownership-aware operations such as
`replace_method_body`, `add_proof_helper`, and `verify_candidate`. Tool-surface
control is a strong development guardrail, but the actual certification boundary
is independent recomputation by the deterministic core and trusted CI from a
resolved lock and pinned distribution.

An optional sandbox profile routes all model-callable operations through a
pinned container or micro-VM with an explicit filesystem, network, secret,
process, and device policy. The receipt describes the exact profile; a read-only
mount alone is called filesystem isolation, not a complete security boundary.
If the same actor can rewrite the checker, extension, and policy that approve the
rewrite, no security claim survives.

### Mechanical success predicate

A certification run satisfies its requested profile only if all common
conditions and all profile-specific conditions hold:

```text
lock_integrity
∧ spec_integrity
∧ protected_surface_integrity
∧ input_closure_complete
∧ honesty_policy_executed
∧ unapproved_logical_assumptions = ∅
∧ prohibited_verification_controls = ∅
∧ profile_requirements_passed
∧ receipt_schema_valid
```

The requested profile and locked execution mode determine whether verification,
vacuity evidence, specification-strength evidence, robustness, a build, runtime
evidence, methodology evidence, or independent review is required.
Artifact-bearing profiles additionally require the build input closure to equal
the verified input closure. No skill, extension, or agent may redefine the
common predicate, execution-mode overlay, or profile after the certification
lock is minted.

---

## 6. Input contract: intake, ownership, and the certification lock

A bare Dafny package is a valid intake source. `corrected init` snapshots it,
scaffolds an optional human-facing manifest when absent, resolves every effective
default and dependency, and writes a mandatory `corrected.lock`.

Development may begin from an ephemeral generated lock. Certification never runs
from implicit defaults: it requires a present, schema-valid, non-stale lock whose
digests are recomputed. Trusted CI may accept a committed lock or independently
resolve and approve an equivalent one.

Illustrative `corrected.toml`:

```toml
schema_version = 1
verifier_backend = "dafny"
dafny_version = "4.11.0"
task_mode = "proof-completion"

spec_roots = ["spec/"]
implementation_roots = ["src/"]
entrypoints = ["VerifiedCore.Sort"]
verify_included_files = true
targets = ["cs"]
target_profile = "verified-nonvacuous"
ownership_mode = "ast-gated"

# Specification authority — optional. Verified where verifiable, recorded in
# the receipt; absent means attestation status "absent".
[intake_attestation]
kind = "in-toto"
path = "attestations/spec.intoto.jsonl"

[verification]
warn_contradictory_assumptions = true
warn_redundant_assumptions = true
allow_library_files = false

# Optional and orthogonal to proof validity. Tests, examples, reference
# behavior, and mutation policy are versioned inputs when present.
[semantic_anchors]
policy = "report"
paths = ["anchors/"]
mutation_profile = "dafny-v0"

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

The resolved `corrected.lock` is machine-written and contains:

```text
schema and resolver versions
canonical effective manifest
exact spec, implementation, generated, and library file sets
locked task mode
ownership classification and protected-surface identity
effective Dafny options and project-file inheritance
pinned Corrected, Pi-extension, Dafny, Boogie, Z3, and backend identities
policy and exception-registry identities
methodology state-machine, evidence-schema, and domain-tool-schema identities
Corrected verification-predicate and in-toto statement schema identities
SLSA provenance predicate identity when an artifact is requested
requested assurance profile
execution mode and methodology-evidence requirement
intake-attestation subject and verification status
all digest-algorithm and canonicalization versions
```

The lock is not an approval by itself. It is a complete, immutable statement of
what a certification run will evaluate.

### Identity, not authority

The intake snapshot identity has two conceptually distinct jobs, and Corrected
requires only one of them:

1. **Identity (required, self-minted).** The threat model needs the spec
   *frozen*: immutable during proof search, bound into the receipt. Freezing
   requires no one's blessing — Corrected snapshots the bytes it was handed and
   mints the digest itself. Everything downstream is relative to that snapshot.
2. **Authority (optional, recorded).** Who approved the spec is upstream's
   business, whatever upstream is. A supplied attestation is verified against a
   supported generic envelope or an explicitly pinned verifier plugin. Its
   subject digest, predicate, issuer, trust root, signature, verification status,
   and verifier identity are recorded. Status is one of `absent`, `verified`,
   `unverified`, or `invalid`; invalid evidence is never silently downgraded to
   absent evidence. A successful `VERIFIED` fact supports "implements the spec
   you handed me (digest X)." The additional phrase "approved by Y" is available
   only when the corresponding attestation is `verified`.

Source-agnostic is not mutable: once handed, the spec is frozen, and
`SPEC_ESCALATION` remains the only exit if proof search exposes a defect.

### Ownership and editable surface

Corrected does not assume that Dafny specifications and implementations occupy
separate files. Intake produces a candidate overlay plus an AST ownership map:

```text
frozen_spec
frozen_implementation
editable_implementation
editable_proof
generated_bridge
approved_external
```

- `frozen_spec` includes entrypoint signatures and contracts, referenced
  predicates/functions/types, handed abstract declarations, and every
  spec-affecting attribute or option.
- `frozen_implementation` includes executable bodies and declarations that the
  selected task mode does not permit the agent to change. In the default
  proof-completion mode, all existing executable behavior is in this class.
- `editable_implementation` includes executable bodies and local implementation
  declarations explicitly selected by an implementation-synthesis task.
- `editable_proof` includes loop invariants, implementation termination
  arguments, assertions, calculation blocks, ghost state, and proved helper
  declarations that are not part of the exported contract.
- `generated_bridge` includes Corrected-owned wrappers and normalization
  substrates, pinned by generator identity and output digest.
- `approved_external` includes lock-authorized libraries, abstract
  interfaces, and externs with explicit evidence obligations.

If a handed loop invariant, termination clause, or helper declaration is part of
the authority-bearing specification, intake classifies that particular AST node
as `frozen_spec`. Classification is semantic, not keyword-wide.

Task mode constrains the classification rather than merely labeling it:
`proof-completion` rejects any `editable_implementation` node, while
`implementation-synthesis` requires an explicit symbol allowlist. A mode change
requires a new lock.

The search model never writes the overlay directly. Pi exposes
ownership-aware patch tools; the core reparses every result and rejects a patch
whose resolved protected surface differs from the lock. Unsupported or
ambiguous layouts fail intake instead of falling back to textual heuristics.

### What is immutable

The intake identity digest covers the semantic closure of the contract:

- entrypoint signatures, `requires`, `ensures`, reads/modifies frames, and any
  authority-bearing invariants or termination clauses;
- predicates and functions referenced by those clauses;
- types, newtypes, datatypes, constants, and representation invariants;
- imports, includes, abstract modules, refinements, and export sets;
- all spec-affecting attributes;
- project configuration and verifier options that can alter meaning or scope; and
- the exact file list or dependency closure from which the above are resolved.

In proof-completion mode, the protected surface additionally covers every
existing executable declaration and body. In implementation-synthesis mode, it
covers every executable node outside the lock's explicit editable allowlist.
The receipt distinguishes specification integrity from frozen-implementation
integrity even though both contribute to the protected-surface digest.

Textual `requires`/`ensures` immutability alone is insufficient: changing the
definition of `Sorted`, an imported abstract declaration, or a module option can
change the contract without touching either keyword.

The first implementation hashes exact intake bytes plus a deterministic file
manifest. It also produces versioned resolved-node fingerprints for the
protected surface using the pinned Dafny parser/AST layer. Certification tests
that fingerprinting is deterministic across repeated parses; constructs without
a stable supported representation fail intake. `dafny format` is a style tool,
not a semantic canonicalizer.

Exact-byte identity protects provenance for the frozen intake snapshot. The
resolved protected-surface identity allows only task-mode-authorized bodies and
proof annotations to change while proving that authority-bearing specification
nodes and frozen executable nodes did not.

### Digest graph

Digest roles are explicit and non-self-referential:

```text
intake_snapshot_digest
lock_digest
protected_surface_digest
candidate_source_tree_digest
library_closure_digest
verification_plan_digest
policy_digest
toolchain_digest

verified_input_closure_digest =
  H(candidate_source_tree_digest,
    protected_surface_digest,
    library_closure_digest,
    verification_plan_digest,
    policy_digest,
    toolchain_digest)

certification_subject_digest =
  H(intake_snapshot_digest,
    lock_digest,
    verified_input_closure_digest)

artifact_provenance =
  H(certification_subject_digest,
    translated_source_digest,
    backend_toolchain_digest,
    artifact_digest)
```

These are Corrected's internal identity relations, not a competing supply-chain
envelope. At emission, content identities map to in-toto subjects and resource
descriptors. Builds emit a linked SLSA provenance attestation whose subject is
the compiled artifact and whose resolved dependencies include the certification
subject and exact verified source closure.

The human manifest contains no digest of itself. The lock hashes a canonical
resolved representation with its own identity field omitted. Every digest
records its algorithm and canonicalization version. Methodology evidence is
bound separately in the canonical receipt rather than folded into the verified
closure or artifact provenance: process evidence must not change the
model-independent proof or build identity.

### Legitimate update affordance

Corrected never silently edits the frozen package. If proof search exposes a
spec defect:

1. stop with `SPEC_ESCALATION`;
2. emit the residual obligation and proposed upstream issue;
3. let the human or upstream spec process modify the spec;
4. take intake of the revised spec (a new identity digest, plus a new
   attestation if upstream supplies one); and
5. start a new certification lineage.

The prior receipt remains attached to the prior digest.

### Content-fidelity invariant

Every derived substrate—worktree, container, generated wrapper, translated
source tree, compiled output, or fuzz harness—must carry and re-check the
certification-subject identity and applicable stage digest it claims to
represent. Isolation proves that mutations do not leak outward; it does not
prove that the isolated copy contains the right code.

---

## 7. Pi-enforced development methodology

This section defines the full managed workflow Corrected may provide, not a
claim that every phase improves proof search. Phase 1 begins with the thin Pi
baseline in §13 and adds these controls as measured interventions. Deterministic
acceptance never depends on an intervention whose only justification is prose.

```text
INTAKE
  │ snapshot source, resolve ownership, mint lock
  ▼
FREEZE_SPEC
  │ protected surface and authority fixed
  ▼
ADEQUACY_SCREEN
  │ vacuity, mutation, semantic-anchor, and specification-coverage signals
  ▼
PATCH
  │ ownership-aware candidate and proof patches
  ▼
VERIFY ──fail──► DIAGNOSE ──► REPAIR    when verification is required
  ▲                              │
  └──────────────────────────────┘       bounded while progress continues
  │ pass, or locked NOT_APPLICABLE
  ▼
HONESTY_CHECK
  ▼
ROBUSTNESS_CHECK          when required by target profile
  ▼
BUILD                     when required by target profile
  ▼
SEAM_TEST                 when required and executable
  ▼
FRESH_CONTEXT_REVIEW      mandatory for managed delivery; gating only by profile
  ▼
CERTIFY
```

The state machine lives in the pinned Corrected Pi extension. Each transition is
guarded by a structured core result. Representative predicates:

```text
PATCH → VERIFY
  candidate_changed
  ∧ candidate_resolved
  ∧ protected_surface_unchanged

VERIFY → HONESTY_CHECK
  complete_verification_passed
  ∨ (verification_status = NOT_APPLICABLE
     ∧ ¬verification_required_by_profile)

HONESTY_CHECK → ROBUSTNESS_CHECK
  unapproved_logical_assumptions = ∅
  ∧ prohibited_verification_controls = ∅
  ∧ verification_scope_complete

any state → SPEC_ESCALATION
  suspected_spec_defect
  ∧ frozen_spec_unchanged

any state → INCOMPLETE
  required_progress_budget_exhausted

any state → INFRASTRUCTURE_INVALID
  required substrate unavailable
  ∨ tool output malformed or unverifiable
```

No model message changes phase. The extension changes phase only after validating
the corresponding structured result and persists that transition in session
evidence.

Methodology evidence is emitted as canonical records and transported in
extension-appended session entries, never reconstructed from conversation
history: session compaction may summarize away earlier turns, so anything the
receipt or a methodology audit needs must live in dedicated entries.

A Pi session entry is transport, not certification authority. Each methodology
record contains a monotonic sequence number, the previous-record digest, phase
transition, candidate digest, referenced core-result and tool-call/result
digests, model/provider identity, and budget state. The deterministic core
validates the hash chain, transition sequence, locked state-machine and schema
versions, candidate identities, and referenced core results. The final
`methodology_evidence_digest` is bound into the receipt. This chain makes
omission or alteration visible after it is bound; producer authenticity still
comes from the pinned runner and its declared isolation boundary. Missing,
malformed, or inconsistent required evidence makes the methodology gate
`INFRASTRUCTURE_INVALID`; runs whose locked execution mode makes methodology
irrelevant report it as `NOT_APPLICABLE`.

### Phase-specific tool surface

The search model does not receive raw write/edit tools or unrestricted
shell access during a managed run. The initial domain surface is:

```text
read_spec
read_candidate
list_obligations
inspect_obligation
replace_method_body
add_proof_helper
apply_proof_patch
verify_candidate
checkpoint_candidate
rollback_candidate
request_spec_escalation
```

Pi activates only the tools legal in the current phase. Every mutating tool:

1. validates arguments against a typed schema;
2. resolves target symbols through the pinned Dafny parser/AST layer;
3. applies the change to a candidate overlay;
4. reparses and compares the protected surface with the lock;
5. writes a content-addressed candidate only if the comparison passes; and
6. returns the new identity plus structured diagnostics.

`replace_method_body` is absent in proof-completion mode and available only for
symbols explicitly allowlisted by an implementation-synthesis lock.

The model never chooses Dafny flags, edits the lock, or constructs a certification
command. `verify_candidate` invokes the exact effective verification plan.

### Deterministic core path

The extension drives this lower-level core path:

```text
frozen spec S + resolved lock L + candidate I
          │
          ▼
preflight: validate provenance, versions, paths, digests, ownership, and closure
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
robustness policy when required
          │
          ▼
build exact verified sources when required
          │
          ▼
runtime evidence required by locked profile
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
3. run the cheapest relevant checks — resolution first, then dynamic
   falsification where contracts are executable: compile and run the candidate
   on a few valid inputs, and evaluate candidate invariants on concrete traces,
   discarding proposals falsified for pennies before any solver time;
4. run the complete verifier to measure the residual state, even when obligations
   remain open;
5. checkpoint an incomplete candidate only when the pinned progress ordering
   improves; and
6. discard the repair if it regresses the accepted state without a measured
   compensating improvement.

Pi's tree-structured sessions are a natural substrate for these transactional
attempts: each repair can occupy a session branch, with failed attempts
abandoned as dead branches while file-level state remains content-addressed.
Branching is an implementation candidate, not a methodology requirement.

After the no-progress or total-attempt budget is exhausted, Corrected returns
the run status `INCOMPLETE` with the best checkpoint and residual proof state.
Budget exhaustion is never converted into success.

---

## 8. Deterministic assurance stack

Guiding principle:

> **Push each concern toward a deterministic, independently repeatable check;
> spend an agent only on the semantic residue.**

### 8.1 Preflight and formatting

- Validate the optional manifest, mandatory lock, and path containment.
- Pin the Corrected core, Pi extension when present, and Dafny distribution by
  version and digest.
- Resolve every declared and transitive file.
- Reject unapproved config files and option sources.
- Optionally run `dafny format --check` for stable style and clean diffs.

Formatting is advisory by default and policy-controlled when a project wants it
as a gate. A valid source-agnostic intake is not rejected for style alone.
Formatting is never used to define semantic identity.

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
- any exported method whose total-correctness policy is weaker than the lock.

**Scope and compilation escapes**

- `--no-verify`, unapproved `--library`, and symbol filtering;
- `{:compile false}` or ignored declarations on required entrypoints;
- generated code or linked artifacts not represented in the lock.

**Contract mutation**

- changed spec dependencies;
- automatically synthesized or strengthened entrypoint preconditions unless the
  handed spec explicitly permits them;
- module-level options that change verification semantics or scope.

**External trust**

- `{:extern}` declarations and native bodies;
- replaceable/abstract modules and preverified libraries;
- every exception not tied to a stable symbol, kind, justification, and evidence
  obligation in the resolved lock.

The policy is deny-by-default. A regex scanner may provide fast feedback but
cannot be the certification mechanism: comments, alternate grammar forms,
attributes, module options, and desugaring make lexical completeness too fragile.

### 8.4 Vacuity and proof dependencies

Certification enables Dafny's contradictory- and redundant-assumption analysis
and retains the proof-dependency report. It does not treat either analysis as a
complete satisfiability decision procedure.

Each exported entrypoint receives one vacuity fact:

```text
WITNESSED_NONVACUOUS
PROVEN_VACUOUS
VACUITY_UNKNOWN
```

The receipt retains the per-entrypoint map and derives one aggregate fact:

```text
PROVEN_VACUOUS       if any exported entrypoint is PROVEN_VACUOUS
WITNESSED_NONVACUOUS if every exported entrypoint is WITNESSED_NONVACUOUS
VACUITY_UNKNOWN      otherwise
```

An empty exported-entrypoint set is a scope failure, not a vacuous
`WITNESSED_NONVACUOUS` result. If a prerequisite prevents a substantive vacuity
fact, the gate-result status records that condition instead of manufacturing an
aggregate fact.

- A verified concrete or symbolic domain witness yields
  `WITNESSED_NONVACUOUS`.
- A proof that no state satisfies the handed precondition yields
  `PROVEN_VACUOUS` and blocks `verified` and every stronger profile; the
  structural `checked` profile may still report that fact.
- Inconclusive bounded search or warning analysis yields `VACUITY_UNKNOWN`.
- An implementation-introduced empty or narrowed domain is an honesty failure,
  not a spec-vacuity fact.
- Contradictory assumptions are blocking unless the entrypoint is deliberately
  proving a contradiction under a separately approved policy.
- Unused implementation statements and partially used specification regions are
  surfaced as specification-coverage signals, not silently discarded.

Profiles decide whether `VACUITY_UNKNOWN` blocks. The default v0
`verified-nonvacuous` profile requires a witness.

Proof-dependency analysis is informative rather than perfect: solver unsat cores
need not be minimal. The receipt records both warnings and policy disposition.

### 8.5 Specification-strength and semantic-anchor evidence

Formal verification establishes conformance to the frozen specification, not
that the specification captures human intent. Corrected can nevertheless look
for evidence that falsifies a weak or mistranslated specification. This screen
is optional, lock-defined, and orthogonal to proof validity.

Supported evidence may include:

- lock-pinned input/output examples, tests, or properties supplied upstream;
- comparison with a pinned reference implementation or executable model;
- implementation mutation operators whose surviving mutants may expose a
  contract too weak to distinguish materially different behavior;
- constant, empty-output, branch-deletion, and boundary mutations appropriate
  to the supported Dafny fragment; and
- concrete discrepancies discovered by runtime or differential campaigns.

Mutation survival is a signal, not a verdict: a surviving mutant may be
equivalent, outside the intended observation relation, or simply not constrained
by a deliberately partial contract. Passing every configured anchor is also not
proof of adequacy. Corrected therefore records:

```text
anchor sources and digests
anchor checks executed, passed, failed, and unsupported
mutation profile and operator versions
mutants generated, applicable, killed, survived, and inconclusive
concrete counterexamples and observation relation
policy disposition
```

A concrete contradiction between the frozen specification and a lock-authorized
semantic anchor is retained as a witness. It stops managed search with
`SPEC_ESCALATION` only when the lock makes that anchor gating; under a reporting
policy it remains an independent `specification_strength` fact. A policy may
require a minimum mutation or anchor campaign for delivery, but the resulting
claim is always "survived the declared screen," never "the specification is
adequate."

### 8.6 Robustness

Use Dafny's `measure-complexity` workflow, repeated solver perturbations, resource
counts, and a pinned resource budget. Record:

- outcome per iteration and assertion batch;
- coefficient of variation of solver resources;
- maximum resource count;
- proof goals with mixed pass/fail outcomes; and
- change against the accepted baseline.

The `release` and stronger profiles reject a proof that passes only under lucky
conditions. Thresholds are project policy, resolved in the lock. Pinning the
toolchain is still required: seed stability does not guarantee upgrade stability.

### 8.7 Ablation

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

### 8.8 Exact-source build

The build step consumes the same resolved source closure and effective
configuration recorded by verification. Before and after translation:

- recompute the verified and build input-closure digests and require equality;
- reject any mismatch;
- record generated-source and binary digests;
- record backend compiler/runtime versions; and
- run the configured target-language build without `--no-verify`.

### 8.9 Differential compilation

Multiple backends can expose backend-specific divergence, but agreement is not an
independent proof:

- backends share Dafny's front end and may share a translation bug;
- raw output equality may be invalid across representations and environments;
- nondeterministic or effectful entrypoints need an explicit observational
  equivalence relation.

Differential testing is optional evidence over lock-declared comparable
observations, not an oracle requiring no assumptions.

---

## 9. Adversarial checking

Z3 owns the verified interior only after the deterministic policy establishes
what was actually verified. The adversarial layer patrols residual boundaries:

- completeness of the logical-assumption policy;
- lock-resolution and input-closure omissions;
- semantic spec mutation that escaped hashing or dependency extraction;
- vacuity and domain shrinkage;
- extern and abstract-module trust;
- content fidelity of worktrees, wrappers, builds, and runtime harnesses;
- runtime-oracle lowering;
- exception justifications; and
- whether the assurance language exceeds the evidence in the receipt.

The search agent is a poor reviewer of the path it just optimized. After all
applicable verification, robustness, build, and runtime-seam work, the managed
Pi workflow launches review in a fresh session. It receives the frozen spec,
candidate, lock, completed evidence, and draft receipt, but not the
implementation conversation. Review is mandatory before managed delivery;
whether it is a certification gate is declared by the target profile. A finding
that changes a candidate, harness, policy disposition, or claim invalidates the
affected downstream facts and forces those gates to be rerun before
certification.

### Capability profiles

Reviewer separation:

| Profile | Agent separation guarantee |
|---|---|
| **Advisory** | A fresh Pi session receives the candidate, completed evidence, and draft receipt. Its findings are recorded but do not change core facts unless they trigger an underlying deterministic gate. |
| **Gating** | A fresh Pi session is tool-pinned read-only, reviews a content-verified immutable snapshot, and must resolve all policy-defined findings. |

Implementer isolation:

| Profile | Isolation guarantee |
|---|---|
| **Controlled tools** | General model write/edit/shell tools are inactive; all reads and mutations use ownership-aware Corrected tools. Trusted core/CI recomputation remains the security boundary. |
| **Filesystem isolated** | Controlled tools operate in a workspace with read-only spec, lock, policy, and checker mounts plus a bounded writable candidate area. |
| **Sandboxed** | Tool operations run in a pinned container or micro-VM with declared filesystem, network, secret, process, socket, and device capabilities. |

Corrected must report both active profiles in the receipt. A general skill
or prompt cannot upgrade either profile; the extension and execution substrate
must produce the corresponding evidence.

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

- lock-pinned upstream generators;
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

The runtime harness is generated or reviewed independently from the candidate
and is outside the search agent's write authority during
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

## 11. Assurance facts, profiles, and receipt

Corrected does not collapse all evidence into one “green,” nor force orthogonal
facts into a ladder. A receipt contains independent dimensions:

```text
run_status: COMPLETE | INCOMPLETE | SPEC_ESCALATION | INFRASTRUCTURE_INVALID

gate_status =
  PASSED | FAILED | NOT_RUN | INCONCLUSIVE
  | INFRASTRUCTURE_INVALID | NOT_APPLICABLE

gate_result<T> = {
  status: gate_status,
  evidence?: T,
  reason?: Reason
}

integrity: gate_result<IntegrityEvidence>
scope: gate_result<ScopeEvidence>
honesty: gate_result<HonestyEvidence>
verification: gate_result<VerificationEvidence>
vacuity_by_entrypoint: map<symbol, gate_result<VacuityFact>>
aggregate_vacuity: gate_result<VacuityFact>
specification_strength: gate_result<SpecificationStrengthEvidence>
robustness: gate_result<RobustnessEvidence>
artifact: gate_result<ArtifactEvidence>
runtime_seam: gate_result<RuntimeEvidence>
methodology: gate_result<MethodologyEvidence>
review: gate_result<ReviewEvidence>
review_policy: ADVISORY | GATING | NOT_REQUIRED
```

Every backend-bearing fact names a `backend_id`, backend schema version, and
typed backend-specific evidence payload. The outer task, gate-result, profile,
and attestation schemas are backend-neutral from v0; the reference core
implements only the Dafny backend until a second backend proves the boundary.

The common predicate is not a second hidden result model.
`IntegrityEvidence` separately records lock, protected-spec, and
frozen-implementation integrity; `ScopeEvidence` records complete input closure
and verification-plan scope; and
`HonestyEvidence` records policy execution, logical assumptions, and prohibited
controls. `ArtifactEvidence` records the equality between build and verified
closures. The named predicate operands are projections of these typed results.

### Standard attestation envelope

The canonical receipt is the predicate of an
`https://in-toto.io/Statement/v1` statement, not a proprietary top-level
envelope. Corrected publishes a versioned verification predicate schema and
type URI before the first stable release. The in-toto subject binds the
certification subject derived from the candidate source tree and locked
verification inputs.

A build emits a separate SLSA Build Provenance predicate. Its subject is the
compiled artifact; its build definition and resolved dependencies bind the
verified source closure, lock, toolchain, generated sources, and Corrected
verification attestation. The Corrected predicate records verification facts;
SLSA records how an artifact was built. Neither duplicates the other.

Local development may emit unsigned statements. Trusted CI emits an
authenticated envelope or bundle under its configured signing identity.
Signature mechanism and transparency-log policy are deployment choices; the
in-toto statement and predicate schemas are the portable compatibility
boundary.

`COMPLETE` means the pipeline reached a terminal state sufficient to evaluate
the locked profile, whether the profile verdict passed or failed. `INCOMPLETE`
means a budget, interruption, or bounded search ended before such a verdict.
`SPEC_ESCALATION` and `INFRASTRUCTURE_INVALID` retain their distinct causes.
Every non-`PASSED` result carries a reason. A failed early gate leaves later
gates as `NOT_RUN` with the blocking prerequisite named, so receipts remain
valid and total even when parsing or resolution fails.

Building an exact artifact remains a reportable fact even if robustness fails.
A runtime campaign remains reportable even if review finds a non-mechanical
concern. Facts are never erased merely because the requested profile fails.
The profile verdict is separate from `run_status` and passes only when
`run_status` is `COMPLETE`, the common predicate passes, and every locked
profile requirement passes.

Profiles provide simple policy verdicts over those facts:

| Profile | Required facts | Claim |
|---|---|---|
| `checked` | Integrity, scope/closure, and honesty gate results are `PASSED` | The locked input is well formed and contains no unapproved bypass known to policy. |
| `verified` | `checked` + verification `PASSED`; aggregate vacuity `PASSED` with a fact other than `PROVEN_VACUOUS` | The source satisfies the frozen spec under the named model and logical assumptions; usefulness of the valid domain may remain unknown. |
| `verified-nonvacuous` | `verified` + aggregate vacuity `WITNESSED_NONVACUOUS` (therefore a witnessed valid domain for every exported entrypoint) | The verified contract has at least one mechanically witnessed valid input per entrypoint. This is the default v0 profile. |
| `release` | `verified-nonvacuous` + robustness and exact-source artifact gates `PASSED`, with build closure equal to verified closure | The named artifact derives from the verified certification subject and the proof meets the pinned stability policy. |
| `seam` | `release` + every required executable-contract and extern campaign gate `PASSED` | The named artifact additionally passed the declared sampled runtime evidence. |
| `reviewed-release` | `release` + gating fresh-session review `PASSED` | The release facts also survived the declared independent-review policy. |

A lock may attach an orthogonal specification-strength requirement to any
profile—for example, requiring all supplied anchors to agree and a minimum
applicable mutation campaign. This does not upgrade the proof claim. It adds
only the claim that the frozen specification survived the exact declared
screen.

Managed Pi delivery applies an orthogonal locked execution-mode overlay:
methodology must be `PASSED`, and fresh-context review must have executed.
Methodology evidence establishes process conformance only; it cannot upgrade,
repair, or reinterpret a failed verification, integrity, honesty, or artifact
fact.

Under an advisory review policy, `PASSED` means the review completed and its
findings are recorded; findings only fail the profile if they expose a failed
deterministic predicate. Under a gating policy, unresolved policy-defined
findings make review `FAILED`. A model-free core certification has methodology
and review `NOT_APPLICABLE` unless its target profile independently requires
review evidence.

`INCOMPLETE` is a run status, not a lower assurance level. A receipt with that
status retains every established fact plus the best candidate and residual proof
state, but it does not satisfy a successful target profile.

The final receipt also includes:

- requested profile, profile verdict, and failed predicates;
- active Pi extension, model/provider, tool-surface, isolation, and reviewer
  profiles;
- certification-subject digest and, when applicable, the
  methodology-evidence digest, with their schema versions;
- spec provenance and attestation verification status;
- the per-entrypoint vacuity map and derived aggregate;
- specification-strength campaign inputs, results, surviving mutants, and
  concrete semantic-anchor discrepancies;
- unsupported and non-executable contract regions;
- approved assumptions and their consumers;
- skipped optional evidence;
- policy exceptions;
- robustness thresholds;
- runtime campaign adequacy statistics; and
- the complete Corrected, Pi, Dafny, build, runtime, OS, and hardware TCB.

“Corrected” is the project name. It is not permission to omit qualifiers from the
receipt.

---

## 12. Delivery model

Corrected v0 is a deterministic core plus a pinned Pi extension. The core is
agent-optional and host-neutral: Phase 0 certification runs with zero agents.
The managed development experience is intentionally Pi-specific. Generic agent
commands, MCP exposure, and other host adapters are deferred until the Pi path
works and the structured boundary has stabilized.

The core implementation, predicate and lock schemas, policy registries,
conformance fixtures, bypass corpus, and reference CI workflows are public.
Corrected does not require a project-operated hosted service to verify a local
statement. Anyone may build a compatible producer or verifier; trust in a
particular receipt still comes from its named builder/signing identity, pinned
toolchain, and independently checked evidence—not from use of the project name.

### Core library and CLI as the reference acceptance implementation

Candidate commands:

- `corrected init` — snapshot intake, scaffold the optional manifest, resolve
  ownership, and mint `corrected.lock`.
- `corrected check` — validate the lock, protected surface, proof scope, and
  honesty policy.
- `corrected verify` — verify one identified candidate against the locked plan
  and return normalized obligations.
- `corrected screen` — run the locked vacuity, semantic-anchor, and
  specification-strength campaigns without changing the frozen specification.
- `corrected robust` — run proof-dependency and solver-stability policy.
- `corrected build` — build exact verified sources and bind artifact provenance.
- `corrected test` — run declared executable-contract and extern campaigns.
- `corrected certify` — evaluate the lock's target-profile predicate and emit
  the final Corrected in-toto verification statement. Artifact-bearing runs
  also emit linked SLSA Build Provenance. Selecting a different profile
  requires resolving a new lock.
- `corrected explain` — render a receipt or residual proof state for humans.

Within the reference distribution, the subcommands share one library and one
schema. `certify` is not a prose orchestrator that reimplements their rules; it
invokes the same coded entrypoints. Independent implementations must pass the
published conformance suite to claim compatibility.

### Pi extension as the methodology implementation

The pinned extension provides:

- `/corrected` to start intake or resume the managed state machine;
- phase-specific domain tools backed by the core;
- bounded repair, checkpoint, and rollback policy;
- fresh-session adversarial review;
- structured `SPEC_ESCALATION`;
- progress, interruption, and resumption behavior; and
- human-readable receipt and residual-state explanation.

Prompt assets and skills may teach Dafny proof strategy, but they are not
structural gates. They consume the extension's structured state and never decide
that certification passed.

The extension is installed from a pinned package or explicit trusted path
outside the model-writable workspace. The managed launcher must admit only the
lock-pinned extension, prompt, skill, context, theme, settings, tool, and package
set. Pi supports two viable control mechanisms. Its CLI can disable discovered
extensions, skills, prompt templates, themes, context files, and built-in tools,
then load an explicit extension and tool allowlist. Its SDK can create a session
with a custom `ResourceLoader`, explicit tools, and in-memory settings.

Corrected v0 uses the SDK-embedded runner because it provides structured state
injection, resource pinning, and diagnostics through one programmatic boundary;
this is a design choice, not a claim that the CLI lacks exclusion controls. A
future certification-capable CLI mode may use the same core if the lock binds
its exact argv, environment, working directory, explicit resources, and
resource-discovery diagnostics. Any launch mode that cannot prove the effective
resource set is not certification-capable.

### Future adapters

An eventual MCP server or alternate host adapter must call the same core and
return the same schemas. It may reproduce the versioned phase protocol, but it
may not implement a second acceptance policy. A future Verus or other verifier
adapter uses the common fact model and supplies backend-specific ownership,
honesty, verification, and TCB evidence. The shared envelope is stable from v0;
backend generality is earned through conformance tests rather than speculative
implementation abstraction.

### Trusted CI

Local runs optimize iteration speed. Trusted CI independently:

1. materializes the exact intake and candidate snapshots named by digest;
2. loads and validates the resolved certification lock;
3. runs a pinned Corrected distribution;
4. recomputes all digests;
5. executes every model-free `corrected certify` gate without Pi or a model;
6. validates any claimed managed-methodology evidence against the locked
   state-machine, schema, candidate, and core-result identities;
7. consumes a separately attested trusted-review job when the profile requires
   gating review;
8. validates required review evidence against the locked reviewer policy; and
9. emits and authenticates the Corrected in-toto verification statement and,
   for artifact-bearing runs, linked SLSA Build Provenance.

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

- intake snapshot, AST ownership map, resolved lock, and digest DAG;
- complete verification-scope resolution;
- Dafny audit integration;
- supplemental honesty policy;
- tri-state vacuity and proof-dependency policy;
- optional semantic-anchor and implementation-mutation evidence;
- exact-source build provenance;
- total gate-result and target-profile schemas;
- a versioned Corrected verification predicate in an in-toto Statement; and
- linked SLSA Build Provenance for artifact-bearing runs.

Exit criterion: every registered planted bypass and protected-surface mutation is
rejected, honest reference programs satisfy their target profiles, and repeated
certification of the same inputs produces equivalent receipts.

### Phase 1 — Pi methodology and narrow bounded repair

Support proof completion first: existing executable bodies and exported
contracts are frozen, and the agent may add or repair only explicitly
classified proof annotations and ghost helpers. Start with one constrained
family, such as pure sequence algorithms with bounded loops and executable
contracts.

Begin with the smallest credible Pi baseline: ownership-aware read/patch tools,
the pinned verifier with complete diagnostics, access to relevant Dafny
libraries and examples, a checkpoint, and the independent cheat checker. Record
cost and completion before adding methodology. Introduce the enforced phase
state machine, dynamic tool surface, typed diagnostic normalization,
transactional branching, progress fingerprints, rollback, retrieved proof
hints, deterministic templates or bounded synthesis where applicable,
counterexample-guided repair, canonical chained methodology evidence, and
fresh-session review as separate measured interventions. Retain an intervention
only when it improves a declared metric without weakening acceptance. Treat
executable implementation synthesis as a separately scored experiment, not part
of the proof-completion success rate.

Exit criterion: better honest-proof completion or lower cost than a plain agent
on a held-out benchmark, without reducing cheat detection; every illegal phase
transition and direct mutation attempt in the methodology corpus is blocked, and
missing, reordered, or altered required methodology evidence is rejected.

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

### Phase 4 — Broader Dafny surface and optional adapters

Only after the core and Pi path are stable:

- add the gating reviewer and sandboxed execution profiles;
- enable the implementation-synthesis task mode for explicitly allowlisted
  executable bodies and score it separately from proof completion;
- broaden the Dafny task corpus; and
- consider formalizability routing;
- publish the versioned methodology protocol; and
- optionally add MCP or other host adapters that consume the same core.

### Evaluation corpus

The benchmark suite contains:

1. **Honest solvable tasks** — known implementations and proofs hidden from the
   agent.
2. **Honest unsolved/over-budget tasks** — expected `INCOMPLETE`, testing truthful
   escalation.
3. **Vacuous specs** — contradictory or empty handed domains.
4. **Specification-strength corpus** — weak contracts, equivalent and
   non-equivalent implementation mutants, mistranslated contracts, conflicting
   semantic anchors, and intentionally partial specifications.
5. **Compositional corpus** — multi-function and multi-module tasks with
   cross-boundary dependencies, tracked separately from single-function proof
   completion.
6. **Cheat corpus** — every assumption, verification suppression, scope omission,
   termination escape, contract mutation, and extern trick supported by the
   pinned Dafny version.
7. **Substrate corpus** — stale worktrees, wrong binaries, mutated wrappers,
   mismatched config, and incomplete source closure.
8. **Robustness corpus** — proofs with known seed/resource instability.
9. **Runtime corpus** — lying externs, compiler-wrapper mismatches, crashes,
   hangs, and insufficient valid-input generation.
10. **Ownership corpus** — interleaved Dafny contracts/bodies, protected-node
    edits, proof-only edits, generated bridges, and ambiguous layouts.
11. **Methodology corpus** — illegal Pi phase transitions, hidden raw-tool calls,
   direct lock/spec mutation, stale session recovery, checkpoint forgery, and
   reviewer-context leakage.

### Metrics

- honest completion rate;
- completion and cost attribution by proposal strategy;
- cheat recall and false-positive rate;
- spec-integrity and scope-completeness recall;
- attempts, tokens, solver resources, and wall-clock time;
- regression rate across accepted checkpoints;
- robustness outcome variance;
- proof-surface size and annotation density of accepted proofs (maintainability
  proxy);
- receipt reproducibility;
- protected-surface mutation recall;
- semantic-anchor disagreement recall;
- mutation score with equivalent/inconclusive mutants reported separately;
- single-function versus compositional completion rate;
- illegal tool-call and phase-transition recall;
- checkpoint/resume fidelity;
- runtime valid-input rate and behavioral coverage;
- unsupported-contract fraction; and
- rate of correct `INCOMPLETE`/`SPEC_ESCALATION` outcomes.

Baselines include a plain coding agent with Dafny access, the same agent with a
simple “do not cheat” prompt, and—where available—human-authored reference
proofs and reproducible DafnyPro/AxDafny-style protocols. Proof-completion and
implementation-synthesis results are never pooled. Public corpora (e.g.,
DafnyBench's 782 verification tasks and a compositional benchmark) supplement
the self-authored suites so headline completion metrics are not measured solely
on benchmarks Corrected's authors designed.

---

## 14. Open questions

- **Proof-search performance:** Which diagnostic representation, patch
  granularity, and checkpoint metric best prevent oscillation?
- **First supported fragment:** Which Dafny subset is narrow enough for reliable
  proof search but valuable enough to validate the project?
- **Second substrate:** Verus verifies production Rust directly, deleting the
  Dafny-to-target translation seam while retaining the Rust compiler/runtime
  TCB. In the cited cross-substrate benchmark its model-union success rate is
  lower than Dafny's. When does production integration justify implementing a
  Verus backend, and which conformance tasks prove that the common schemas are
  genuinely backend-neutral?
- **AST integration:** Should Corrected use Dafny libraries, a Dafny plugin, or a
  stable resolved-program export for ownership classification, protected-surface
  comparison, honesty, and semantic-closure analysis?
- **Ownership granularity:** Which declarations and proof annotations can v0
  classify reliably, and which ambiguous layouts must intake reject?
- **Safe option policy:** Which attributes, project options, library artifacts,
  and module features are allowed in certification mode?
- **Domain witnesses:** Which entrypoint/state fragments support verified
  concrete or symbolic witnesses, and when must the result remain
  `VACUITY_UNKNOWN`?
- **Specification-strength policy:** Which mutation operators and semantic
  anchors are useful for the first supported fragment, how are equivalent
  mutants classified, and which findings merely warn versus force
  `SPEC_ESCALATION`?
- **Vacuity waivers:** Should the lock support per-entrypoint witness waivers
  (justified, receipt-recorded) so one hard entrypoint does not force a
  whole-run downgrade from `verified-nonvacuous` to `verified`?
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
- **Attestation authentication:** The portable formats are fixed to a Corrected
  predicate in an in-toto Statement plus SLSA Build Provenance. Which signing
  identities, envelope/bundle formats, transparency logs, and offline
  verification policies should trusted CI support first?
- **Intake attestation formats:** Which generic envelopes and trust policies
  should intake verify natively, and what is the stable verifier-plugin
  interface?
- **Pi packaging:** How are the extension, active tool schemas, system prompt,
  model/provider configuration, and project resource policy pinned and upgraded?
- **Sandbox profile:** Which Pi execution backend provides the first complete
  filesystem/network/secret/process capability boundary?
- **Methodology protocol:** Which state and tool schemas must be stable before an
  alternate host adapter is justified?
- **Maintenance economics:** Does the repair engine reduce the cost of keeping
  proofs working as implementations and pinned toolchains evolve? Cedar's
  evolution from earlier Dafny modeling to a compact executable Lean model
  beside production Rust is a counterexample worth monitoring. AWS publicly
  emphasizes Lean's runtime, libraries, small TCB, and concise interactive proof
  environment; it does not publicly attribute the change solely to Dafny proof
  maintenance.
- **Review placement:** Does review-after-all-evidence invalidate expensive
  robustness/build/seam work on candidate-level findings often enough to justify
  a two-touch review — a cheap candidate pass after `HONESTY_CHECK` plus the
  claims audit before `CERTIFY`?
- **Proof reuse:** Should accepted lemmas and proof patterns feed a
  project-level retrieval library that seeds later proposals, and does reuse
  measurably improve completion rate or cost? DafnyPro's retrieval-augmented
  hint system is positive external evidence (≈10 percentage points in its
  ablation).
- **Open-source governance:** How are predicate schemas, policy registries,
  bypass classifications, threat-model changes, and backend conformance tests
  reviewed and versioned without letting one implementation silently redefine
  the assurance claim?

---

## 15. Current substrate facts this design depends on

The first implementation must verify these against the pinned release rather
than treating documentation as timeless:

### Dafny

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

### Pi

- Current release at revision time: Pi v0.80.10 (2026-07-16) — actively
  maintained v0.x with no documented API-stability or deprecation policy. The
  lock pins exact Pi and extension identities; the extension stays thin so API
  churn lands in the methodology layer, never in acceptance.
- Pi extensions can register custom tools and commands, intercept or block tool
  calls, persist session state, and react to lifecycle events.
- Extensions can change the active tool set during a session, enabling a
  phase-specific domain surface.
- Built-in tool operations can be replaced or routed to remote, container, or
  sandbox implementations.
- Extensions execute with the Pi process's full system permissions and are
  trusted code.
- The CLI exposes resource-exclusion flags, explicit extension loading, and
  built-in-tool selection; the SDK exposes `createAgentSession`, custom
  `ResourceLoader` instances, explicit tools, and in-memory settings. Corrected
  v0 chooses the SDK boundary, but a fully locked and diagnosed CLI invocation
  is not inherently excluded.
- Session compaction may summarize earlier turns. Extension-appended entries can
  transport durable methodology evidence, but certification binds their
  exported, core-validated digest rather than trusting mutable session JSONL by
  itself.

### Verified-codegen research landscape (verified 2026-07-19)

Evidence maturity varies. DafnyPro was presented at the Dafny Workshop
co-located with POPL 2026; MutDafny is accepted at ICSE 2026; several other
results below are recent arXiv preprints. This document therefore says
"reports" rather than treating every result as independently replicated.

- The 12,504-task vericoding benchmark reports model-union success—solved by at
  least one evaluated model over multiple attempts—of 82.2% for Dafny, 44.2%
  for Verus/Rust, and 26.8% for Lean. This makes Dafny the strongest substrate
  *within that benchmark and protocol*, not a universal production optimum.
- On the benchmark's fixed-program Dafny verification slice, the earlier 68%
  single-model result rose to 89% for the strongest evaluated individual model;
  96% is the union across models, not a pass@1 result.
- DafnyPro reports 86.2% on DafnyBench with Claude 3.5 Sonnet and 68% with a
  fine-tuned 7B model; without its manually curated proof-hint library it
  reports 76.2%, attributing roughly 10 percentage points to hint retrieval.
  Its task freezes implementation and specification and asks for proof
  annotations. Its parser-based diff checker, invariant pruner, and retrieved
  proof hints provide direct evidence for protected surfaces, proof cleanup,
  and project-level proof retrieval.
- AxDafny reports 92.7% on DafnyBench proof-hint completion, but 56.4% on its
  end-to-end implementation-and-proof benchmark and 28.0% on that benchmark's
  hard split. It freezes supplied specifications and definitions, rejects
  several bypass constructs, applies mutation-style vacuity checks, and
  separately runs compiled outputs against runtime tests. Two primary-source
  details sharpen the contrast with Corrected: its acceptance gate includes an
  LLM reviewer — a candidate is accepted only if it is "not rejected by the
  LLM reviewer," a nondeterministic component inside the accept path — and its
  specification protection is a clause-level requires/ensures subset check, so
  redefining a predicate referenced by a contract evades the deterministic
  check and is left to that LLM reviewer. Corrected accepts on deterministic
  gates only and freezes the semantic closure (protected-surface AST identity),
  closing the predicate-redefinition gap mechanically. Corrected generalizes
  these controls; it does not claim they were absent.
- DafnyCOMP reports that success drops sharply for multi-function programs with
  cross-component dependencies, with fragile specifications,
  implementation/proof misalignment, and unstable reasoning. Single-function
  completion rates must not be extrapolated to repository-scale work.
- The vericoding benchmark validates generated blocks for known cheats,
  including proof bypass and specification mutation. In a manual sample
  conditioned on successful vericoding outputs, the authors report that roughly
  9% involved weak specifications and 15% poor translations. These are sampled,
  conditional figures, not rates over the full 12,504-task corpus.
- Dafny RLVR experiments directly observed specification hacking when weak
  formal tasks were used as rewards; filtered multi-turn results remained much
  lower than the unfiltered headline. A separate RLVR study finds systematic
  reward shortcuts on inductive rule-learning tasks with imperfect extensional
  checkers. The latter supports the general §5 threat model but is not direct
  evidence about Dafny or SMT program verification.
- MutDafny applies implementation mutations to assess Dafny specifications. On
  794 real-world programs it identified weak specifications that allowed
  materially changed programs to keep verifying. This supports an optional
  mutation-based specification-strength screen while not making mutation
  survival or mutation killing a proof of intent.
- ATLAS reports generating 2.7K verified Dafny programs through a staged
  pipeline and using them as training data. Together with DafnyPro and AxDafny,
  this supports making search strategies replaceable and measuring each against
  a plain-agent baseline.
- RAG-Verus reports gains from retrieving repository-local proof context on a
  383-task repository benchmark, while ExVerus uses validated source-level
  counterexamples to guide invariant repair. These results support a portfolio
  of retrieval and counterexample strategies rather than one mandatory prompt
  recipe.
- Syntax-guided synthesis and CEGIS provide deterministic search over an
  explicit candidate grammar. For small supported holes, a solver-backed
  strategy can be cheaper and more reproducible than asking an LLM to propose
  every candidate.
- Verus's official guidance recommends a normal coding agent with verifier
  access, full diagnostics, relevant libraries, sandbox freedom, and an
  independent cheat checker. Pi already supplies the general harness; Corrected
  should add only measured proof-search policy and deterministic acceptance.
- Cedar demonstrates a different production architecture: an executable formal
  model beside production Rust, connected by large differential campaigns.
  This is a standing alternative to shipping code compiled from the verified
  language itself.

### Provenance and ecosystem facts

- The in-toto Attestation Framework provides the standard subject, predicate,
  envelope, and bundle layers needed for portable verification evidence.
- SLSA Build Provenance already models build definitions, builders, resolved
  dependencies, byproducts, and artifact subjects. Corrected uses it rather than
  defining a parallel build-provenance envelope.
- Publicly positioned adjacent open-source and commercial efforts already claim
  formal verification for AI-generated code. Their assurance depth and shipping
  status require separate evaluation, but their existence makes an
  "unoccupied market" claim inappropriate. Corrected's open-source position is
  the specific integration and assurance contract defined in this document.

Primary references:

- [Dafny 4.11.0 release](https://github.com/dafny-lang/dafny/releases/tag/v4.11.0)
- [Current Dafny Reference Manual](https://dafny.org/dafny/DafnyRef/DafnyRef)
- [Dafny proof-dependency analysis](https://dafny.org/blog/2023/10/27/proof-dependencies/)
- [Current Dafny verification optimization guide](https://dafny.org/dafny/VerificationOptimization/VerificationOptimization.html)
- [Pi extension documentation](https://pi.dev/docs/latest/extensions)
- [Pi CLI usage and resource controls](https://pi.dev/docs/latest/usage)
- [Pi security model](https://pi.dev/docs/latest/security)
- [Pi SDK documentation](https://pi.dev/docs/latest/sdk)
- [Pi session format](https://pi.dev/docs/latest/session-format)
- [DafnyPro (Dafny Workshop 2026)](https://arxiv.org/abs/2601.05385)
- [Vericoding benchmark](https://arxiv.org/abs/2509.22908)
- [AxDafny](https://arxiv.org/abs/2606.32007)
- [DafnyBench](https://arxiv.org/abs/2406.08467)
- [DafnyCOMP](https://arxiv.org/abs/2509.23061)
- [MutDafny](https://arxiv.org/abs/2511.15403)
- [ATLAS](https://arxiv.org/abs/2512.10173)
- [RAG-Verus](https://arxiv.org/abs/2502.05344)
- [ExVerus](https://arxiv.org/abs/2603.25810)
- [Syntax-Guided Synthesis language standard](https://sygus-org.github.io/language/)
- [cvc5 synthesis options](https://cvc5.github.io/docs/latest/options.html)
- [Automating Formal Verification with RL and Recursive Inference](https://arxiv.org/abs/2605.30914)
- [LLMs Gaming Verifiers: RLVR can Lead to Reward Hacking](https://arxiv.org/abs/2604.15149)
- [Verus guidance for LLM proof development](https://verus-lang.github.io/verus/guide/llmforverusproof.html)
- [Verus Cargo integration](https://verus-lang.github.io/verus/guide/cargo_verus.html)
- [Cedar's earlier Dafny verification approach](https://www.amazon.science/blog/how-we-built-cedar-with-automated-reasoning-and-differential-testing)
- [Cedar's journey with Lean (AWS)](https://lean-lang.org/use-cases/cedar/)
- [in-toto Attestation Framework](https://github.com/in-toto/attestation)
- [SLSA Build Provenance v1.2](https://slsa.dev/spec/v1.2/build-provenance)
- [Midspiral](https://midspiral.com/)
- [Aretta](https://aretta.ai/)
- [Predictable Code](https://code.predictablemachines.com/)

---

*Corrected is responsible for the honesty and reproducibility of the proof
process, not the adequacy of the handed spec: zero unapproved logical
assumptions, complete proof scope, optional attempts to falsify weak contracts,
exact artifact provenance, sampled evidence at the runtime seam, and explicit
trust everywhere in between — from any source, frozen at intake, in
open-standard attestations anyone can verify.*
