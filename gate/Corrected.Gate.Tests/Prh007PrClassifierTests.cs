using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Corrected.Gate;
using Corrected.Gate.Kernel;
using Xunit;

namespace Corrected.Gate.Tests;

/// <summary>
/// Track 5c-ii — PRH-007 (spec ~1265–1382): the TOTAL, fail-closed PR-class classifier.
/// Subject: the new impure <see cref="PrClassifier.Classify"/> in Corrected.Gate. All inputs
/// are SUPPLIED/synthetic — the base <see cref="ReadinessBlock"/> (the VALIDATED protected-main
/// merge-base), the head block, the changed controlled paths, and the declared trusted PR label.
///
/// This track covers ONLY the classifier. It does NOT build: the INV-029 cryptographic
/// activation-diff validator (5c-iii), INV-028 health (5d), the entry-receipt crypto (5e), real
/// cosign, or the git merge-base computation (CI/Track-4 — base/head + changed paths + label are
/// supplied here). The carrier <see cref="ReadinessGate.EvaluateReadiness"/> is reused (real
/// kernel) for the round-10 status-follows-preconditions acceptance and the round-11
/// dangling-reference negative — those assertions run against the REAL kernel, not the stub.
///
/// Stub state (RED): <see cref="PrClassifier.Classify"/> is a safe deny-by-default reject, so the
/// POSITIVE-classification cells (Accepted==true) go red, while the fail-closed guards
/// (zero/multi-class, label-mismatch, mutual-exclusion, status-inconsistent, dangling-reference)
/// stay green because the safe default is to reject (AP-001 deny-by-default).
///
/// AP-031 real-artifact clause is NOT triggered: no test here parses another Correctless skill's
/// `.correctless/artifacts/` producer output; all fixtures are synthetic domain objects built via
/// the kernel factories, and the paths are synthetic controlled-path strings.
/// </summary>
public class Prh007PrClassifierTests
{
    // ----------------------------------------------------------------------------------------
    // Synthetic controlled-path constants. Evidence paths (P3 bundle/pointer under
    // test/attestations/**, the P2 completion manifest) vs frozen-mechanism/gate-code paths.
    // ----------------------------------------------------------------------------------------
    private const string P3Bundle = "test/attestations/inv010/v2/receipt.json";
    private const string P3Pointer = "test/attestations/active-baseline.json";
    private const string P2Manifest = "test/manifests/phase-0.0-completion.json";
    private const string EntryReceipt = "test/attestations/entry/receipt-X.json";

    private const string GateCode = "gate/Corrected.Gate/PrClassifier.cs";
    private const string Workflow = ".github/workflows/p3-determinism-lane.yml";
    private const string Schema = "spikes/dafny-compat/schema/evidence-schema.json";

    // ========================================================================================
    // A. VOCABULARY + AUTHENTICATED-BASE STRUCTURE (set-equality / reflection — PMB-003, RS-027).
    // ========================================================================================

    // Tests PRH-007 [unit]: PrClass is EXACTLY the five classes — set-equality, not a count/
    // presence proxy (PMB-003 / AP-022). There is NO sixth "reject"/"default" member: a rejection
    // is Accepted==false, never a most-permissive class. P1 is not among them (round-11).
    [Fact]
    public void PrClass_is_exactly_the_five_classes()
    {
        Assert.Equal(
            new HashSet<PrClass>
            {
                PrClass.PreconditionReactivation,
                PrClass.PreconditionInvalidation,
                PrClass.P3Refresh,
                PrClass.PhaseEntry,
                PrClass.MechanismChange,
            },
            Enum.GetValues<PrClass>().ToHashSet());
    }

    // Tests PRH-007 [unit]: AUTHENTICATED BASE (RS-027). The classifier takes the VALIDATED base
    // as a REQUIRED first parameter (a ReadinessBlock) — there is NO derive-base-from-HEAD^
    // overload and NO parameterless entry. Exactly ONE public Classify, whose first two params
    // are (base, head) ReadinessBlocks and which also cross-checks a string label. Structural
    // proof that a caller-guessed HEAD^ base is not a path the classifier can take.
    [Fact]
    public void Classify_requires_a_validated_base_block_and_has_no_derive_base_overload()
    {
        MethodInfo[] classifyOverloads = typeof(PrClassifier)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.Name == nameof(PrClassifier.Classify))
            .ToArray();

        Assert.Single(classifyOverloads); // no overload can omit / re-derive the base.

        ParameterInfo[] ps = classifyOverloads[0].GetParameters();
        Assert.Equal(typeof(ReadinessBlock), ps[0].ParameterType); // [0] = validated base.
        Assert.Equal(typeof(ReadinessBlock), ps[1].ParameterType); // [1] = head.
        Assert.False(ps[0].IsOptional); // base is REQUIRED — never defaulted/derived.
        Assert.Contains(typeof(string), ps.Select(p => p.ParameterType)); // the trusted label cross-check input.
    }

    // ========================================================================================
    // B. POSITIVE SINGLE-CLASS CELLS (each expects an ACCEPT → RED against the deny stub).
    // ========================================================================================

    // Tests PRH-007 [integration]: precondition-reactivation, k=3, status STAYS BLOCKED (P2 still
    // false) — the PR3 initial-activation shape (this feature's concrete wiring). RED.
    [Fact]
    public void Reactivation_P3_status_stays_blocked_is_classified()
    {
        ReadinessBlock baseB = V2Blocked(ReadinessStatus.BLOCKED, (true, "p1"), (false, null), (false, null));
        ReadinessBlock head = V2Blocked(ReadinessStatus.BLOCKED, (true, "p1"), (false, null), (true, "p3-ref"));

        PrClassification r = PrClassifier.Classify(
            baseB, head, new[] { P3Bundle, P3Pointer }, PrClassLabels.PreconditionReactivation);

        Assert.True(r.Accepted);
        Assert.Equal(PrClass.PreconditionReactivation, r.AssignedClass);
        // A1: the reviewer-facing render REFLECTS the inputs (Detection ~line 1308) — strengthened
        // from bare NotEmpty to require the supplied changed paths be echoed in ObservedChanges.
        AssertRenderReflectsInputs(r, new[] { P3Bundle, P3Pointer });
    }

    // Tests PRH-007 [integration]: precondition-reactivation, k=2, ALL become true so
    // status:BLOCKED→READY (the round-9 P2-landing shape). RED.
    [Fact]
    public void Reactivation_P2_all_true_moves_status_to_ready_is_classified()
    {
        ReadinessBlock baseB = V2Blocked(ReadinessStatus.BLOCKED, (true, "p1"), (false, null), (true, "p3"));
        ReadinessBlock head = V2Blocked(ReadinessStatus.READY, (true, "p1"), (true, "p2-ref"), (true, "p3"));

        PrClassification r = PrClassifier.Classify(
            baseB, head, new[] { P2Manifest }, PrClassLabels.PreconditionReactivation);

        Assert.True(r.Accepted);
        Assert.Equal(PrClass.PreconditionReactivation, r.AssignedClass);
        AssertRenderReflectsInputs(r, new[] { P2Manifest }); // A1: render reflects the supplied path.
    }

    // Tests PRH-007 [integration]: precondition-invalidation, k=3, from READY → status→BLOCKED
    // (the round-10 recover-from-READY shape). RED.
    [Fact]
    public void Invalidation_P3_from_ready_moves_status_to_blocked_is_classified()
    {
        ReadinessBlock baseB = V2Blocked(ReadinessStatus.READY, (true, "p1"), (true, "p2"), (true, "p3-ref"));
        ReadinessBlock head = V2Blocked(ReadinessStatus.BLOCKED, (true, "p1"), (true, "p2"), (false, null));

        PrClassification r = PrClassifier.Classify(
            baseB, head, new[] { P3Pointer }, PrClassLabels.PreconditionInvalidation);

        Assert.True(r.Accepted);
        Assert.Equal(PrClass.PreconditionInvalidation, r.AssignedClass);
        AssertRenderReflectsInputs(r, new[] { P3Pointer }); // A1: render reflects the supplied path.
    }

    // Tests PRH-007 [integration]: P3-refresh (post-entry, lifecycle=ENTERED) — a new versioned
    // bundle + repointed active-baseline file; NO block field changes at all (satisfied/status/
    // evidence/pointer all identical base→head). RED.
    [Fact]
    public void P3refresh_entered_pointer_move_only_is_classified()
    {
        ReadinessBlock baseB = V2Entered(ReadinessStatus.READY, EntryReceipt, (true, "p1"), (true, "p2"), (true, "p3-v1"));
        ReadinessBlock head = V2Entered(ReadinessStatus.READY, EntryReceipt, (true, "p1"), (true, "p2"), (true, "p3-v1"));

        PrClassification r = PrClassifier.Classify(
            baseB, head, new[] { P3Bundle, P3Pointer }, PrClassLabels.P3Refresh);

        Assert.True(r.Accepted);
        Assert.Equal(PrClass.P3Refresh, r.AssignedClass);
        AssertRenderReflectsInputs(r, new[] { P3Bundle, P3Pointer }); // A1: render reflects the supplied paths.
    }

    // Tests PRH-007 [integration]: phase-entry FROM v1 — the atomic transition schema_version:1→2
    // + add lifecycle:ENTERED + set entry_evidence_pointer; status/ready_predicate/preconditions
    // unchanged. RED.
    [Fact]
    public void Phase_entry_from_v1_atomic_transition_is_classified()
    {
        ReadinessBlock baseB = V1(ReadinessStatus.READY, (true, "p1"), (true, "p2"), (true, "p3"));
        ReadinessBlock head = V2Entered(ReadinessStatus.READY, EntryReceipt, (true, "p1"), (true, "p2"), (true, "p3"));

        PrClassification r = PrClassifier.Classify(
            baseB, head, new[] { EntryReceipt }, PrClassLabels.PhaseEntry);

        Assert.True(r.Accepted);
        Assert.Equal(PrClass.PhaseEntry, r.AssignedClass);
        AssertRenderReflectsInputs(r, new[] { EntryReceipt }); // A1: render reflects the supplied path.
    }

    // Tests PRH-007 [integration]: phase-entry FROM v2-BLOCKED — lifecycle:BLOCKED→ENTERED (schema
    // stays 2) + set entry_evidence_pointer; status/preconditions unchanged. RED.
    [Fact]
    public void Phase_entry_from_v2_blocked_is_classified()
    {
        ReadinessBlock baseB = V2Blocked(ReadinessStatus.READY, (true, "p1"), (true, "p2"), (true, "p3"));
        ReadinessBlock head = V2Entered(ReadinessStatus.READY, EntryReceipt, (true, "p1"), (true, "p2"), (true, "p3"));

        PrClassification r = PrClassifier.Classify(
            baseB, head, new[] { EntryReceipt }, PrClassLabels.PhaseEntry);

        Assert.True(r.Accepted);
        Assert.Equal(PrClass.PhaseEntry, r.AssignedClass);
        AssertRenderReflectsInputs(r, new[] { EntryReceipt }); // A1: render reflects the supplied path.
    }

    // Tests PRH-007 [integration]: mechanism-change — frozen-mechanism paths + gate code, NO block
    // field change (base/head fields identical). The legitimate home for PR2 / rotation / upgrades. RED.
    [Fact]
    public void Mechanism_change_gate_code_no_evidence_is_classified()
    {
        ReadinessBlock baseB = V2Blocked(ReadinessStatus.BLOCKED, (true, "p1"), (false, null), (false, null));
        ReadinessBlock head = V2Blocked(ReadinessStatus.BLOCKED, (true, "p1"), (false, null), (false, null));

        PrClassification r = PrClassifier.Classify(
            baseB, head, new[] { GateCode, Workflow, Schema }, PrClassLabels.MechanismChange);

        Assert.True(r.Accepted);
        Assert.Equal(PrClass.MechanismChange, r.AssignedClass);
        AssertRenderReflectsInputs(r, new[] { GateCode, Workflow, Schema }); // A1: render reflects the supplied paths.
    }

    // ========================================================================================
    // C. TOTAL + FAIL-CLOSED (zero-class / P1 / multi-class / outside-allowlist). Deny-by-default
    // guards — green on the safe stub; they catch a fail-OPEN classifier in GREEN.
    // ========================================================================================

    // Tests PRH-007 [integration]: an UNTYPED controlled-field touch matching ZERO classes fails
    // closed (never defaults to the most permissive). Here ready_predicate changes alone — a
    // controlled field belonging to no class's allowlist.
    [Fact]
    public void Zero_class_untyped_controlled_field_touch_fails_closed()
    {
        var basePcs = new[]
        {
            ReadinessPrecondition.Create(PreconditionId.P1, "p1", true, "p1", Array.Empty<string>()),
            ReadinessPrecondition.Create(PreconditionId.P2, "p2", false, null, Array.Empty<string>()),
            ReadinessPrecondition.Create(PreconditionId.P3, "p3", false, null, Array.Empty<string>()),
        };
        ReadinessBlock baseB = ReadinessBlock.TryCreate(2, ReadinessStatus.BLOCKED, "P1 AND P2 AND P3", basePcs, LifecycleState.Blocked, null)!;
        ReadinessBlock head = ReadinessBlock.TryCreate(2, ReadinessStatus.BLOCKED, "P1 AND P2 AND P3 AND MYSTERY", basePcs, LifecycleState.Blocked, null)!;

        PrClassification r = PrClassifier.Classify(baseB, head, Array.Empty<string>(), PrClassLabels.MechanismChange);

        Assert.False(r.Accepted);
        Assert.Null(r.AssignedClass);
    }

    // Tests PRH-007 [integration]: P1 is NOT a precondition class (round-11 — P1 is governed by its
    // own Stage-B migration contract). A PR flipping P1.satisfied under a reactivation label fails
    // closed: the {P2,P3} evidence classes never accept a P1 flip.
    [Fact]
    public void P1_precondition_flip_is_not_a_class_fails_closed()
    {
        ReadinessBlock baseB = V2Blocked(ReadinessStatus.BLOCKED, (false, null), (false, null), (false, null));
        ReadinessBlock head = V2Blocked(ReadinessStatus.BLOCKED, (true, "p1-ref"), (false, null), (false, null));

        PrClassification r = PrClassifier.Classify(
            baseB, head, new[] { "docs/adr/ADR-0001-dafny-integration-boundary.md" }, PrClassLabels.PreconditionReactivation);

        Assert.False(r.Accepted);
        Assert.Null(r.AssignedClass);
    }

    // Tests PRH-007 [integration]: a MULTI-CLASS PR (matching more than one class) fails closed
    // (never resolves to the most permissive). Here the diff flips P3.satisfied (reactivation
    // shape) AND performs the lifecycle:BLOCKED→ENTERED transition (phase-entry shape) at once.
    [Fact]
    public void Multi_class_touch_fails_closed()
    {
        ReadinessBlock baseB = V2Blocked(ReadinessStatus.BLOCKED, (true, "p1"), (true, "p2"), (false, null));
        ReadinessBlock head = V2Entered(ReadinessStatus.BLOCKED, EntryReceipt, (true, "p1"), (true, "p2"), (true, "p3-ref"));

        PrClassification r = PrClassifier.Classify(
            baseB, head, new[] { P3Bundle, P3Pointer, EntryReceipt }, PrClassLabels.PreconditionReactivation);

        Assert.False(r.Accepted);
        Assert.Null(r.AssignedClass);
    }

    // Tests PRH-007 [integration]: B2 — the "EXACTLY ONE precondition Pk" boundary (~line 1291:
    // "for exactly one precondition Pk"). A SINGLE PR under ONE precondition-reactivation label
    // that flips BOTH P2 AND P3 satisfied:false→true (evidence:null→ref for both, both evidence
    // paths supplied) is multi-Pk-WITHIN-one-class and fails closed. This is DISTINCT from
    // Multi_class_touch_fails_closed (which is multi-CLASS): here it is a single class touched
    // twice. A GREEN impl that checks only "≥1 Pk reactivated" instead of "exactly one" would
    // wrongly accept — this is the upper boundary of the exactly-one rule. Green on the deny stub.
    [Fact]
    public void Reactivation_flipping_two_preconditions_in_one_pr_fails_closed()
    {
        // base: P2 false + P3 false → head: BOTH flip true+ref. P1 already true ⇒ all re-derive
        // true ⇒ status:BLOCKED→READY (a consistent block; the violation is TWO Pk in one class).
        ReadinessBlock baseB = V2Blocked(ReadinessStatus.BLOCKED, (true, "p1"), (false, null), (false, null));
        ReadinessBlock head = V2Blocked(ReadinessStatus.READY, (true, "p1"), (true, "p2-ref"), (true, "p3-ref"));

        PrClassification r = PrClassifier.Classify(
            baseB, head, new[] { P2Manifest, P3Bundle, P3Pointer }, PrClassLabels.PreconditionReactivation);

        Assert.False(r.Accepted);
        Assert.Null(r.AssignedClass);
    }

    // Tests PRH-007 [integration]: a reactivation-shaped diff that ALSO changes a field OUTSIDE the
    // class allowlist (ready_predicate) fails closed — "any touch outside the assigned class's
    // allowlist fails closed."
    [Fact]
    public void Reactivation_with_extra_field_outside_allowlist_fails_closed()
    {
        var basePcs = new[]
        {
            ReadinessPrecondition.Create(PreconditionId.P1, "p1", true, "p1", Array.Empty<string>()),
            ReadinessPrecondition.Create(PreconditionId.P2, "p2", false, null, Array.Empty<string>()),
            ReadinessPrecondition.Create(PreconditionId.P3, "p3", false, null, Array.Empty<string>()),
        };
        var headPcs = new[]
        {
            ReadinessPrecondition.Create(PreconditionId.P1, "p1", true, "p1", Array.Empty<string>()),
            ReadinessPrecondition.Create(PreconditionId.P2, "p2", false, null, Array.Empty<string>()),
            ReadinessPrecondition.Create(PreconditionId.P3, "p3", true, "p3-ref", Array.Empty<string>()),
        };
        ReadinessBlock baseB = ReadinessBlock.TryCreate(2, ReadinessStatus.BLOCKED, "P1 AND P2 AND P3", basePcs, LifecycleState.Blocked, null)!;
        // ready_predicate ALSO mutated — outside the reactivation allowlist.
        ReadinessBlock head = ReadinessBlock.TryCreate(2, ReadinessStatus.BLOCKED, "P1 OR P2 OR P3", headPcs, LifecycleState.Blocked, null)!;

        PrClassification r = PrClassifier.Classify(
            baseB, head, new[] { P3Bundle, P3Pointer }, PrClassLabels.PreconditionReactivation);

        Assert.False(r.Accepted);
        Assert.Null(r.AssignedClass);
    }

    // Tests PRH-007 [integration]: mechanism-change MUST NOT touch any evidence FIELD. A
    // mechanism-labelled diff that also flips status (an evidence field) fails closed.
    [Fact]
    public void Mechanism_change_touching_evidence_field_fails_closed()
    {
        ReadinessBlock baseB = V2Blocked(ReadinessStatus.READY, (true, "p1"), (true, "p2"), (true, "p3"));
        ReadinessBlock head = V2Blocked(ReadinessStatus.BLOCKED, (true, "p1"), (true, "p2"), (true, "p3"));

        PrClassification r = PrClassifier.Classify(
            baseB, head, new[] { GateCode }, PrClassLabels.MechanismChange);

        Assert.False(r.Accepted);
        Assert.Null(r.AssignedClass);
    }

    // Tests PRH-007 [integration]: DEFENSIVE — a P3-refresh SHAPE (no block field change, only
    // evidence-path touch) but in the PRE-ENTRY (lifecycle=BLOCKED) regime fails closed: P3-refresh
    // is post-entry only; pre-entry an evidence-path touch with no satisfied-flip matches no class.
    [Fact]
    public void P3refresh_shape_pre_entry_fails_closed()
    {
        ReadinessBlock baseB = V2Blocked(ReadinessStatus.BLOCKED, (true, "p1"), (false, null), (true, "p3"));
        ReadinessBlock head = V2Blocked(ReadinessStatus.BLOCKED, (true, "p1"), (false, null), (true, "p3"));

        PrClassification r = PrClassifier.Classify(
            baseB, head, new[] { P3Bundle, P3Pointer }, PrClassLabels.P3Refresh);

        Assert.False(r.Accepted);
        Assert.Null(r.AssignedClass);
    }

    // ========================================================================================
    // D. EVIDENCE-ACTIVATION ⊥ MECHANISM-CHANGE (mutually exclusive — one PR can never be both).
    // ========================================================================================

    // Tests PRH-007 [integration]: a PR that flips P3.satisfied (evidence activation) AND touches a
    // frozen-mechanism/gate-code path is BOTH-at-once — the mutual-exclusion invariant fails it
    // closed (reactivation forbids a mechanism change; mechanism-change forbids an evidence field).
    [Fact]
    public void Evidence_activation_and_mechanism_change_are_mutually_exclusive()
    {
        ReadinessBlock baseB = V2Blocked(ReadinessStatus.BLOCKED, (true, "p1"), (false, null), (false, null));
        ReadinessBlock head = V2Blocked(ReadinessStatus.BLOCKED, (true, "p1"), (false, null), (true, "p3-ref"));

        // Evidence path (activation) + a mechanism path in the SAME PR.
        PrClassification asEvidence = PrClassifier.Classify(
            baseB, head, new[] { P3Bundle, P3Pointer, GateCode }, PrClassLabels.PreconditionReactivation);
        PrClassification asMechanism = PrClassifier.Classify(
            baseB, head, new[] { P3Bundle, P3Pointer, GateCode }, PrClassLabels.MechanismChange);

        // Neither label rescues it — a PR can never be both an evidence activation and a mechanism change.
        Assert.False(asEvidence.Accepted);
        Assert.Null(asEvidence.AssignedClass);
        Assert.False(asMechanism.Accepted);
        Assert.Null(asMechanism.AssignedClass);
    }

    // ========================================================================================
    // E. TRUSTED-LABEL ↔ PARSED-SPAN CROSS-CHECK (the diff is the authority).
    // ========================================================================================

    // B1 — WRONG-DIRECTION label↔diff cross-check on the reactivation⇄invalidation axis. The spec
    // fixes direction: `satisfied:false→true` is legal ONLY in precondition-reactivation and
    // `true→false` ONLY in precondition-invalidation (~line 1251, table ~1291/1292). The diff is
    // the authority, so a label whose direction disagrees with the observed satisfied-flip fails
    // closed. Fail-closed guards — green on the deny stub, they catch a direction-BLIND GREEN impl
    // (one that trusts the label, or keys only on |Δ| / "some Pk changed" without the sign/base).

    // Tests PRH-007 [integration]: an INVALIDATION-shape diff (single Pk satisfied:true→false,
    // evidence:ref→null, pre-entry v2-BLOCKED) carrying a precondition-REACTIVATION label fails
    // closed — the reactivation label claims `false→true` but the diff is a `true→false`.
    [Fact]
    public void Label_reactivation_on_invalidation_shape_diff_fails_closed()
    {
        // base: P3 true+ref → head: P3 false+null (invalidation shape). P2 stays false ⇒ status stays BLOCKED.
        ReadinessBlock baseB = V2Blocked(ReadinessStatus.BLOCKED, (true, "p1"), (false, null), (true, "p3-ref"));
        ReadinessBlock head = V2Blocked(ReadinessStatus.BLOCKED, (true, "p1"), (false, null), (false, null));

        // changed paths = the P3 active-baseline pointer the invalidation retires.
        PrClassification r = PrClassifier.Classify(
            baseB, head, new[] { P3Pointer }, PrClassLabels.PreconditionReactivation);

        Assert.False(r.Accepted);
        Assert.Null(r.AssignedClass);
    }

    // Tests PRH-007 [integration]: the SYMMETRIC case — a REACTIVATION-shape diff (single Pk
    // satisfied:false→true, evidence:null→ref) carrying a precondition-INVALIDATION label fails
    // closed — the invalidation label claims `true→false` but the diff is a `false→true`.
    [Fact]
    public void Label_invalidation_on_reactivation_shape_diff_fails_closed()
    {
        // base: P3 false+null → head: P3 true+ref (reactivation shape). P2 stays false ⇒ status stays BLOCKED.
        ReadinessBlock baseB = V2Blocked(ReadinessStatus.BLOCKED, (true, "p1"), (false, null), (false, null));
        ReadinessBlock head = V2Blocked(ReadinessStatus.BLOCKED, (true, "p1"), (false, null), (true, "p3-ref"));

        // changed paths = the new versioned bundle + active-baseline pointer a reactivation sets.
        PrClassification r = PrClassifier.Classify(
            baseB, head, new[] { P3Bundle, P3Pointer }, PrClassLabels.PreconditionInvalidation);

        Assert.False(r.Accepted);
        Assert.Null(r.AssignedClass);
    }

    // Tests PRH-007 [integration]: a NO-OP under a reactivation label fails closed — base Pk is
    // ALREADY true+ref and head Pk is STILL true+ref (NO satisfied flip at all), only an evidence
    // path is touched. Reactivation REQUIRES a single Pk `satisfied:false→true` (~line 1291); with
    // zero flip the diff matches no reactivation, so the label disagrees → reject. A BASE-IGNORING
    // impl (keying only on head+label, never diffing against the validated base) cannot see that
    // no flip occurred and would wrongly accept — this cell forces the base into the decision.
    [Fact]
    public void Label_reactivation_on_no_satisfied_flip_diff_fails_closed()
    {
        // base and head are IDENTICAL on P3 (true+ref both sides) — no satisfied flip anywhere.
        ReadinessBlock baseB = V2Blocked(ReadinessStatus.BLOCKED, (true, "p1"), (false, null), (true, "p3-ref"));
        ReadinessBlock head = V2Blocked(ReadinessStatus.BLOCKED, (true, "p1"), (false, null), (true, "p3-ref"));

        PrClassification r = PrClassifier.Classify(
            baseB, head, new[] { P3Bundle, P3Pointer }, PrClassLabels.PreconditionReactivation);

        Assert.False(r.Accepted);
        Assert.Null(r.AssignedClass);
    }

    // Tests PRH-007 [integration]: a "P3-refresh" LABEL on a diff that FLIPS satisfied fails closed
    // (the diff — a reactivation — is the authority, not the label). Spec-named cross-check.
    [Fact]
    public void Label_p3refresh_on_satisfied_flip_diff_fails_closed()
    {
        ReadinessBlock baseB = V2Entered(ReadinessStatus.BLOCKED, EntryReceipt, (true, "p1"), (false, null), (false, null));
        ReadinessBlock head = V2Entered(ReadinessStatus.BLOCKED, EntryReceipt, (true, "p1"), (false, null), (true, "p3-ref"));

        PrClassification r = PrClassifier.Classify(
            baseB, head, new[] { P3Bundle, P3Pointer }, PrClassLabels.P3Refresh);

        Assert.False(r.Accepted);
        Assert.Null(r.AssignedClass);
    }

    // Tests PRH-007 [integration]: a "mechanism-change" LABEL on a diff touching test/attestations/**
    // fails closed (the diff touches evidence — the authority overrides the mechanism label).
    // Spec-named cross-check.
    [Fact]
    public void Label_mechanism_change_on_attestations_touch_fails_closed()
    {
        ReadinessBlock baseB = V2Blocked(ReadinessStatus.BLOCKED, (true, "p1"), (false, null), (false, null));
        ReadinessBlock head = V2Blocked(ReadinessStatus.BLOCKED, (true, "p1"), (false, null), (false, null));

        // Block fields unchanged, but a test/attestations/** path is touched under a mechanism label.
        PrClassification r = PrClassifier.Classify(
            baseB, head, new[] { P3Bundle }, PrClassLabels.MechanismChange);

        Assert.False(r.Accepted);
        Assert.Null(r.AssignedClass);
    }

    // Tests PRH-007 [integration]: a VALID single-class diff (a real reactivation) but with a
    // DISAGREEING label (phase-entry) fails closed — label must agree with the observed class.
    [Fact]
    public void Label_disagreeing_with_valid_single_class_diff_fails_closed()
    {
        ReadinessBlock baseB = V2Blocked(ReadinessStatus.BLOCKED, (true, "p1"), (false, null), (false, null));
        ReadinessBlock head = V2Blocked(ReadinessStatus.BLOCKED, (true, "p1"), (false, null), (true, "p3-ref"));

        PrClassification r = PrClassifier.Classify(
            baseB, head, new[] { P3Bundle, P3Pointer }, PrClassLabels.PhaseEntry);

        Assert.False(r.Accepted);
        Assert.Null(r.AssignedClass);
    }

    // Tests PRH-007 [integration]: an UNKNOWN / garbage label fails closed (a controlled-field PR
    // whose declared label is not one of the five known classes is untyped → reject).
    [Fact]
    public void Unknown_label_fails_closed()
    {
        ReadinessBlock baseB = V2Blocked(ReadinessStatus.BLOCKED, (true, "p1"), (false, null), (false, null));
        ReadinessBlock head = V2Blocked(ReadinessStatus.BLOCKED, (true, "p1"), (false, null), (true, "p3-ref"));

        PrClassification r = PrClassifier.Classify(
            baseB, head, new[] { P3Bundle, P3Pointer }, "totally-not-a-real-label");

        Assert.False(r.Accepted);
        Assert.Null(r.AssignedClass);
    }

    // ========================================================================================
    // F. AUTHENTICATED BASE — the two-commit-branch attack (RS-027). A guessed HEAD^..HEAD base
    // hides commit-1's mechanism change, which the real merge-base diff carries and rejects.
    // ========================================================================================

    // Tests PRH-007 [integration]: the classifier's verdict is DETERMINED ENTIRELY by the supplied
    // base/diff, so the base MUST be the validated protected-main merge-base. A two-commit branch —
    // commit 1 an unrelated mechanism change, commit 2 a P3 reactivation — presents a NARROW
    // HEAD^..HEAD diff (only the activation → single class → would ACCEPT) but a WIDE merge-base diff
    // (activation + the commit-1 mechanism path → evidence+mechanism → multi/mutual-exclusion →
    // REJECT). The narrow (guessed) diff wrongly accepts (RED); the authenticated wide diff fails
    // closed. Their disagreement is exactly why the base must be authenticated, not caller-guessed.
    [Fact]
    public void Guessed_head_caret_base_hides_a_multi_class_diff_the_merge_base_rejects()
    {
        // The block is identical pre-activation for both bases — the commit-1 mechanism change is a
        // PATH-only change (does not alter the readiness block), so the authority is the DIFF SCOPE.
        ReadinessBlock preActivation = V2Blocked(ReadinessStatus.BLOCKED, (true, "p1"), (false, null), (false, null));
        ReadinessBlock head = V2Blocked(ReadinessStatus.BLOCKED, (true, "p1"), (false, null), (true, "p3-ref"));

        string[] narrowDiff = { P3Bundle, P3Pointer };            // HEAD^..HEAD: only commit 2 (the activation).
        string[] wideDiff = { P3Bundle, P3Pointer, GateCode };    // merge-base..HEAD: also commit 1 (mechanism).

        PrClassification narrow = PrClassifier.Classify(preActivation, head, narrowDiff, PrClassLabels.PreconditionReactivation);
        PrClassification wide = PrClassifier.Classify(preActivation, head, wideDiff, PrClassLabels.PreconditionReactivation);

        // The guessed narrow (HEAD^) diff WOULD wrongly accept as a clean reactivation...  (RED)
        Assert.True(narrow.Accepted);
        Assert.Equal(PrClass.PreconditionReactivation, narrow.AssignedClass);
        // A1: the (wrong) accept still renders its narrow inputs — proving the render is input-driven.
        AssertRenderReflectsInputs(narrow, narrowDiff);
        // ...but the AUTHENTICATED merge-base diff carries the mechanism path too → fail closed.
        Assert.False(wide.Accepted);
        Assert.Null(wide.AssignedClass);
        // The verdict flips with the diff scope — proving the base must be the validated merge-base.
        Assert.NotEqual(narrow.Accepted, wide.Accepted);
    }

    // ========================================================================================
    // G. STATUS-FOLLOWS-PRECONDITIONS CROSS-PRODUCT (round-10):
    //    {status-config: pivot/READY, static/BLOCKED} × {precondition: P2, P3} × {action:
    //    invalidate, restore}. The {P2,P3} set is COMPLETE (P1 excluded, round-11). Each cell is a
    //    legal invalidation/reactivation diff that keeps status consistent with the re-derived
    //    preconditions AND is accepted by the carrier (ReadinessGate.EvaluateReadiness).
    // ========================================================================================

    public static IEnumerable<object[]> StatusFollowsCells()
    {
        // The COMPLETE controlled precondition set is {P2, P3} — P1 is governed by its own Stage-B
        // contract and is deliberately excluded (round-11). Derived from the axes, never per-cell
        // literals (PMB-003 / AP-022).
        foreach (PreconditionId k in new[] { PreconditionId.P2, PreconditionId.P3 })
        {
            foreach (string action in new[] { "invalidate", "restore" })
            {
                foreach (bool pivot in new[] { true, false })
                {
                    yield return new object[] { k, action, pivot };
                }
            }
        }
    }

    // Tests PRH-007 [integration]: EACH status-follows-preconditions cell is (1) classified as the
    // correct precondition class with an agreeing label, (2) keeps status consistent with the
    // re-derived preconditions (invalidate-from-READY→BLOCKED; restore→READY iff all true; else
    // status static), (3) moves the Pk.evidence field WITH its external reference, and (4) is
    // ACCEPTED by the carrier (real kernel). The classifier-accept assertion goes RED against the
    // deny stub; the carrier-Pass + status assertions run against the real kernel.
    [Theory]
    [MemberData(nameof(StatusFollowsCells))]
    public void Status_follows_preconditions_cross_product_cell_is_classified_and_carrier_accepts(
        PreconditionId k, string action, bool pivot)
    {
        (ReadinessBlock baseB, ReadinessBlock head, string[] paths, PrClass cls, string label, ReadinessStatus expectedHeadStatus)
            = BuildStatusCell(k, action, pivot);

        // (2) status follows the re-derived preconditions.
        Assert.Equal(expectedHeadStatus, head.Status);

        // (1) the classifier assigns the correct precondition class under the agreeing label. (RED)
        PrClassification r = PrClassifier.Classify(baseB, head, paths, label);
        Assert.True(r.Accepted);
        Assert.Equal(cls, r.AssignedClass);
        // A1: on accept the render reflects the cell's supplied paths (per-cell, not boilerplate).
        AssertRenderReflectsInputs(r, paths);

        // (3)+(4) the block-field↔reference coupling holds and the carrier accepts the head block:
        // restore ⇒ evidence non-null AND its probe Resolved; invalidate ⇒ evidence null AND no
        // dangling reference. Probes are built consistent with the head declaration.
        ReadinessVerdict v = ReadinessGate.EvaluateReadiness(head, ConsistentProbes(head));
        Assert.Equal(VerdictKind.Pass, v.Kind);
    }

    // Tests PRH-007 [unit]: the status-follows-preconditions cross-product is the COMPLETE set —
    // count DERIVED from the axes with a literal pin that breaks if the {P2,P3} set or an axis
    // grows (PMB-003 — a count proxy cannot detect an absent cell). 2 preconditions × 2 actions ×
    // 2 status-configs = 8; P1 is NOT in the controlled precondition set (round-11).
    [Fact]
    public void Status_follows_preconditions_cross_product_is_the_complete_set()
    {
        PreconditionId[] pres = { PreconditionId.P2, PreconditionId.P3 };
        string[] actions = { "invalidate", "restore" };
        int derived = pres.Length * actions.Length * 2 /* pivot/static */;

        Assert.Equal(8, derived);
        Assert.Equal(derived, StatusFollowsCells().Count());
        Assert.DoesNotContain(PreconditionId.P1, pres); // P1 is NOT a precondition class.
    }

    // ========================================================================================
    // H. ROUND-11 DANGLING-REFERENCE NEGATIVE — an invalidation that deletes the external file but
    // leaves Pk.evidence non-null is REJECTED (proving the block-field↔reference coupling is
    // ENFORCED, not merely documented).
    // ========================================================================================

    // Tests PRH-007 [integration]: a P3 invalidation that retires the external reference FILE but
    // leaves P3.evidence NON-NULL is rejected on BOTH surfaces: (a) the carrier hard-Fails the head
    // block because the dangling reference is Unresolvable (ReadinessGate CellFails: Evidence!=null
    // && ReferenceResolution!=Resolved → Fail) REGARDLESS of satisfied:false; (b) the classifier
    // fails closed because an invalidation that does not move evidence→null is outside the class
    // allowlist. Both are green on the safe stub; the carrier assertion is the real kernel.
    [Fact]
    public void Dangling_reference_invalidation_is_rejected_by_carrier_and_classifier()
    {
        ReadinessBlock baseB = V2Blocked(ReadinessStatus.BLOCKED, (true, "p1"), (false, null), (true, "p3-ref"));
        // head: satisfied:true→false BUT evidence LEFT non-null after deleting its file (dangling).
        ReadinessBlock head = V2Blocked(ReadinessStatus.BLOCKED, (true, "p1"), (false, null), (false, "p3-ref"));

        // (a) carrier: the dangling P3 reference resolves Unresolvable → hard Fail (real kernel).
        var probes = new Dictionary<PreconditionId, ProbeResult>
        {
            [PreconditionId.P1] = ProbeResult.TryCreate(true, "probe", ReferenceResolution.Resolved)!,
            [PreconditionId.P2] = ProbeResult.TryCreate(false, "probe", ReferenceResolution.Resolved)!,
            [PreconditionId.P3] = ProbeResult.TryCreate(false, "probe", ReferenceResolution.Unresolvable)!,
        };
        ReadinessVerdict v = ReadinessGate.EvaluateReadiness(head, probes);
        Assert.Equal(VerdictKind.Fail, v.Kind);
        Assert.Equal(PreconditionId.P3, v.OffendingPrecondition);

        // (b) classifier: an invalidation whose evidence field did not move to null is fail-closed.
        PrClassification r = PrClassifier.Classify(baseB, head, new[] { P3Pointer }, PrClassLabels.PreconditionInvalidation);
        Assert.False(r.Accepted);
        Assert.Null(r.AssignedClass);
    }

    // Tests PRH-007 [integration]: the POSITIVE coupling counterpart — a CLEAN invalidation
    // (evidence→null, file retired, no dangling ref) IS accepted by the carrier (the head block
    // with a consistent false+null P3 passes the kernel), confirming the round-11 guard rejects
    // ONLY the dangling shape, not every invalidation.
    [Fact]
    public void Clean_invalidation_no_dangling_reference_is_accepted_by_carrier()
    {
        ReadinessBlock head = V2Blocked(ReadinessStatus.BLOCKED, (true, "p1"), (false, null), (false, null));

        ReadinessVerdict v = ReadinessGate.EvaluateReadiness(head, ConsistentProbes(head));
        Assert.Equal(VerdictKind.Pass, v.Kind);
    }

    // Tests PRH-007 [integration]: A3 — the REACTIVATION-side evidence-coupling negative, symmetric
    // to the round-11 invalidation Dangling_reference test above. A P3 reactivation flips
    // satisfied:false→true but LEAVES P3.evidence NULL — the `null→ref` coupling is broken
    // (activated WITHOUT setting the reference). The reactivation allowlist requires
    // `Pk.evidence: null → <registered reference>` in lockstep (~line 1291: the block evidence
    // field MUST become non-null, else the block re-derives the satisfied-without-evidence Fail);
    // a reactivation that leaves evidence null is outside the class allowlist → the CLASSIFIER
    // fails closed. (Classifier surface only; the carrier's satisfied-true-without-evidence Fail is
    // a separate sub-track — not asserted here.) Green on the deny stub.
    [Fact]
    public void Reactivation_leaving_evidence_null_fails_closed()
    {
        ReadinessBlock baseB = V2Blocked(ReadinessStatus.BLOCKED, (true, "p1"), (false, null), (false, null));
        // head: P3 satisfied:false→true BUT evidence LEFT null (broken null→ref coupling).
        ReadinessBlock head = V2Blocked(ReadinessStatus.BLOCKED, (true, "p1"), (false, null), (true, null));

        PrClassification r = PrClassifier.Classify(
            baseB, head, new[] { P3Bundle, P3Pointer }, PrClassLabels.PreconditionReactivation);

        Assert.False(r.Accepted);
        Assert.Null(r.AssignedClass);
    }

    // ========================================================================================
    // CELL BUILDER + SYNTHETIC BLOCK/PROBE HELPERS.
    // ========================================================================================

    // Builds one status-follows-preconditions cell. Target Pk (k∈{P2,P3}); the OTHER evidence
    // precondition Pj is the complement; P1 is always true+evidence (Stage-B landed). `pivot`
    // selects the status-MOVING configuration (the block is READY-pivoted on Pk alone) vs the
    // status-STATIC configuration (Pj also false, so Pk's change does not move status).
    private static (ReadinessBlock baseB, ReadinessBlock head, string[] paths, PrClass cls, string label, ReadinessStatus expectedHeadStatus)
        BuildStatusCell(PreconditionId k, string action, bool pivot)
    {
        PreconditionId j = k == PreconditionId.P3 ? PreconditionId.P2 : PreconditionId.P3;
        string kev = k == PreconditionId.P3 ? "p3-ref" : "p2-ref";
        string jev = j == PreconditionId.P3 ? "p3-ref" : "p2-ref";
        string[] paths = k == PreconditionId.P3 ? new[] { P3Bundle, P3Pointer } : new[] { P2Manifest };

        // Pj is (true, ev) in the pivot config (so Pk alone pivots READY↔BLOCKED) and (false, null)
        // in the static config (so the block is BLOCKED on both sides regardless of Pk).
        (bool sat, string? ev) jPivot = (true, jev);
        (bool sat, string? ev) jStatic = (false, null);
        (bool sat, string? ev) jVal = pivot ? jPivot : jStatic;

        (bool sat, string? ev) p1 = (true, "p1");

        if (action == "invalidate")
        {
            // base: Pk true+evidence; head: Pk false+null (evidence retired WITH the reference).
            ReadinessBlock baseB = Compose(k, (true, kev), j, jVal, p1, pivot ? ReadinessStatus.READY : ReadinessStatus.BLOCKED);
            ReadinessBlock head = Compose(k, (false, null), j, jVal, p1, ReadinessStatus.BLOCKED);
            // invalidate-from-READY (pivot) ⇒ status→BLOCKED; static ⇒ already BLOCKED.
            return (baseB, head, paths, PrClass.PreconditionInvalidation, PrClassLabels.PreconditionInvalidation, ReadinessStatus.BLOCKED);
        }
        else // restore
        {
            // base: Pk false+null; head: Pk true+evidence (set WITH the reference).
            ReadinessBlock baseB = Compose(k, (false, null), j, jVal, p1, ReadinessStatus.BLOCKED);
            // restore ⇒ status→READY iff ALL re-derive true (pivot: Pj true ⇒ all true ⇒ READY;
            // static: Pj false ⇒ still BLOCKED).
            ReadinessStatus headStatus = pivot ? ReadinessStatus.READY : ReadinessStatus.BLOCKED;
            ReadinessBlock head = Compose(k, (true, kev), j, jVal, p1, headStatus);
            return (baseB, head, paths, PrClass.PreconditionReactivation, PrClassLabels.PreconditionReactivation, headStatus);
        }
    }

    // Assembles a pre-entry (v2, lifecycle=BLOCKED) block from a target precondition value, the
    // other-evidence precondition value, the P1 value, and a status.
    private static ReadinessBlock Compose(
        PreconditionId k, (bool sat, string? ev) kVal,
        PreconditionId j, (bool sat, string? ev) jVal,
        (bool sat, string? ev) p1, ReadinessStatus status)
    {
        (bool sat, string? ev) p2 = PreconditionId.P2 == k ? kVal : (PreconditionId.P2 == j ? jVal : p1);
        (bool sat, string? ev) p3 = PreconditionId.P3 == k ? kVal : (PreconditionId.P3 == j ? jVal : p1);
        return V2Blocked(status, p1, p2, p3);
    }

    // ---- block builders (synthetic; the classifier + carrier consume validated blocks) ----

    private static ReadinessBlock V2Blocked(
        ReadinessStatus status, (bool sat, string? ev) p1, (bool sat, string? ev) p2, (bool sat, string? ev) p3)
        => Build(2, status, LifecycleState.Blocked, null, p1, p2, p3);

    private static ReadinessBlock V2Entered(
        ReadinessStatus status, string pointer, (bool sat, string? ev) p1, (bool sat, string? ev) p2, (bool sat, string? ev) p3)
        => Build(2, status, LifecycleState.Entered, pointer, p1, p2, p3);

    private static ReadinessBlock V1(
        ReadinessStatus status, (bool sat, string? ev) p1, (bool sat, string? ev) p2, (bool sat, string? ev) p3)
        => Build(1, status, null, null, p1, p2, p3);

    private static ReadinessBlock Build(
        int schema, ReadinessStatus status, LifecycleState? lifecycle, string? pointer,
        (bool sat, string? ev) p1, (bool sat, string? ev) p2, (bool sat, string? ev) p3)
    {
        var pcs = new[]
        {
            ReadinessPrecondition.Create(PreconditionId.P1, "p1", p1.sat, p1.ev, Array.Empty<string>()),
            ReadinessPrecondition.Create(PreconditionId.P2, "p2", p2.sat, p2.ev, Array.Empty<string>()),
            ReadinessPrecondition.Create(PreconditionId.P3, "p3", p3.sat, p3.ev, Array.Empty<string>()),
        };
        ReadinessBlock? b = ReadinessBlock.TryCreate(schema, status, "P1 AND P2 AND P3", pcs, lifecycle, pointer);
        Assert.NotNull(b); // fixture-construction guard, not a RED assertion.
        return b!;
    }

    // Probes consistent with a block's DECLARED preconditions: declared true → (true, Resolved);
    // declared false → (false, Resolved). Used for the carrier-acceptance assertions.
    private static IReadOnlyDictionary<PreconditionId, ProbeResult> ConsistentProbes(ReadinessBlock block)
    {
        var map = new Dictionary<PreconditionId, ProbeResult>();
        foreach (var pc in block.Preconditions)
        {
            map[pc.Id] = ProbeResult.TryCreate(pc.Satisfied, "probe", ReferenceResolution.Resolved)!;
        }
        return map;
    }

    // A1 render contract (PRH-007 Detection ~line 1308: "The gate renders PR class = X; permitted
    // spans = …; observed changes = … for the reviewer."). Single-sourced so EVERY accept cell
    // asserts the SAME observable render structure — not 9 divergent, potentially over-fitted
    // copies — and so a GREEN adjustment lands in one place. On ACCEPT the render must REFLECT the
    // inputs: PermittedSpans is non-empty (it corresponds to the assigned class's allowlist) and
    // every SUPPLIED changed path is echoed among ObservedChanges. This asserts observable
    // structure, NOT an exact string format (which would over-constrain GREEN). It is STRICTLY
    // STRONGER than a bare NotEmpty (a boilerplate/empty observed-changes render passes NotEmpty
    // but is caught here), and stays RED against the deny stub as part of the accept contract.
    private static void AssertRenderReflectsInputs(PrClassification r, IEnumerable<string> suppliedPaths)
    {
        Assert.NotEmpty(r.PermittedSpans);
        foreach (string p in suppliedPaths)
        {
            Assert.Contains(p, r.ObservedChanges);
        }
    }
}
