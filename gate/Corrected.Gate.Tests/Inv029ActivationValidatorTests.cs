using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Corrected.Gate;
using Corrected.Gate.Kernel;
using Xunit;

namespace Corrected.Gate.Tests;

/// <summary>
/// Track 5c-iii — INV-029 (spec ~1157–1181): the entry receipt is activated by a
/// self-reference-safe TWO-STEP sign→activate protocol. Subject: the new pure
/// <see cref="ActivationValidator.ValidateActivation"/> in Corrected.Gate — architecture
/// entrypoint (4), the ACTIVATION-DIFF VALIDATOR, which is DISTINCT from the PRH-007
/// <see cref="PrClassifier"/> (spec ~1010: "(4) an activation-diff validator" is a separate,
/// deeper protocol check than the classifier). All inputs are SUPPLIED/synthetic — the
/// VALIDATED protected-main merge-base <see cref="ReadinessBlock"/>, the activation-PR head
/// block, the changed controlled paths, an <see cref="EntryReceiptRef"/> (commit X + the
/// receipt's own committed path), an <see cref="ActivationAncestry"/> snapshot, and the
/// <see cref="EntryIntegrity"/> verify RESULT (the cosign crypto + evidence-digest checks are a
/// SEPARATE seam, INV-030 / tracks 5e/T3 — reduced here to an enum input).
///
/// The validator ACCEPTS iff ALL of INV-029's clauses hold (else fail closed, AP-001):
///   (1) activation-only atomic transition against the merge-base — EITHER from-v1
///       (schema_version 1→2 + lifecycle null/BLOCKED→ENTERED + pointer null→set) OR
///       from-v2-BLOCKED (lifecycle BLOCKED→ENTERED, schema stays 2, pointer null→set); and
///       status / ready_predicate / EVERY precondition (satisfied + evidence) IDENTICAL base→head;
///   (2) self-reference-safe — ReceiptPath ∉ X's committed tree (AP-021 circular gate: the entry
///       commit X must not contain the receipt that binds it);
///   (3) ancestor — X is an ancestor of HEAD;
///   (4) a from-clean-VERIFYING entry receipt — entryReceiptIntegrity == Verified.
///
/// Stub state (RED): <see cref="ActivationValidator.ValidateActivation"/> is a safe deny-by-default
/// reject, so the two POSITIVE activation cells (Accepted==true) go RED as ASSERTIONS, while every
/// fail-closed negative stays GREEN because the safe default is to reject. The negatives guard
/// against a fail-OPEN GREEN implementation and MUST STAY green after GREEN.
///
/// AP-031 real-artifact clause is DORMANT/not-triggered: no test here parses another Correctless
/// skill's `.correctless/artifacts/` producer output — all fixtures are synthetic domain objects
/// built via the real <see cref="ReadinessBlock.TryCreate"/> kernel factory, and the paths are
/// synthetic controlled-path strings (there is no committed v2/ENTERED activation producer artifact
/// yet; the parent block is v1 through P1/P2/P3, and the live protocol cannot fire until P2 lands).
/// </summary>
public class Inv029ActivationValidatorTests
{
    // ----------------------------------------------------------------------------------------
    // Synthetic constants. The entry receipt commits at ReceiptPath under the entry root; the
    // head's entry_evidence_pointer points at that same receipt. X's committed tree carries OTHER
    // evidence paths but NOT the receipt (self-reference-safe) — see SafeTree/SelfRefTree below.
    // ----------------------------------------------------------------------------------------
    private const string CommitX = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"; // 40-hex synthetic X
    private const string ReceiptPath = "test/attestations/entry/receipt-X.json";
    private const string Predicate = "P1 AND P2 AND P3";

    // Other paths present in X's committed tree — the P1/P2/P3 evidence closure at X. The entry
    // receipt is DELIBERATELY absent from this set (two-step protocol: X exists BEFORE the receipt
    // that binds it is committed by the later activation PR).
    private const string TreeEvidenceA = "test/attestations/inv010/v2/receipt.json";
    private const string TreeEvidenceB = "test/manifests/phase-0.0-completion.json";
    private const string TreeReadinessBlock = ".correctless/adr/ADR-0001.md";

    private static ActivationAncestry SafeAncestry(bool ancestor = true) =>
        new(ancestor, new HashSet<string> { TreeEvidenceA, TreeEvidenceB, TreeReadinessBlock });

    // Self-reference-UNSAFE: X's committed tree CONTAINS the receipt that binds X (AP-021).
    private static ActivationAncestry SelfRefAncestry() =>
        new(true, new HashSet<string> { TreeEvidenceA, TreeEvidenceB, ReceiptPath });

    private static EntryReceiptRef Ref() => new(CommitX, ReceiptPath);

    // Clause 1: the activation touches ONLY the entry-receipt path.
    private static IReadOnlyCollection<string> EntryOnlyPaths() => new[] { ReceiptPath };

    // ----------------------------------------------------------------------------------------
    // Precondition rows. AllSat = P1∧P2∧P3 satisfied with evidence (the entry commit X has
    // status=READY while still pre-entry, INV-027/029 (A)). A flipped variant mutates P2 for the
    // "diff also flips a precondition" negative.
    // ----------------------------------------------------------------------------------------
    private static ReadinessPrecondition[] AllSatPcs() => new[]
    {
        ReadinessPrecondition.Create(PreconditionId.P1, "p1", true, "gate-id-1", Array.Empty<string>()),
        ReadinessPrecondition.Create(PreconditionId.P2, "p2", true, "gate-id-2", Array.Empty<string>()),
        ReadinessPrecondition.Create(PreconditionId.P3, "p3", true, "gate-id-3", Array.Empty<string>()),
    };

    // P2 flipped satisfied:true→false + evidence:ref→null (a precondition CHANGE across the diff).
    private static ReadinessPrecondition[] P2FlippedPcs() => new[]
    {
        ReadinessPrecondition.Create(PreconditionId.P1, "p1", true, "gate-id-1", Array.Empty<string>()),
        ReadinessPrecondition.Create(PreconditionId.P2, "p2", false, null, Array.Empty<string>()),
        ReadinessPrecondition.Create(PreconditionId.P3, "p3", true, "gate-id-3", Array.Empty<string>()),
    };

    // Base evidence refs, per precondition — the canonical AllSat shape the diff must PRESERVE.
    private static string BaseEvidence(PreconditionId id) => id switch
    {
        PreconditionId.P1 => "gate-id-1",
        PreconditionId.P2 => "gate-id-2",
        PreconditionId.P3 => "gate-id-3",
        _ => throw new ArgumentOutOfRangeException(nameof(id)),
    };

    // Build a head precondition set identical to the AllSat base EXCEPT for exactly ONE
    // (precondition, subfield) cell — so clause 1's "EVERY precondition's satisfied AND evidence
    // unchanged base→head" is pinned field-by-field, not just on the satisfied booleans. The
    // "evidence" subfield keeps satisfied:true and swaps evidence to a DIFFERENT non-null ref
    // (<ref>→<ref>-ALT) — the load-bearing case an impl comparing only Satisfied would miss.
    private static ReadinessPrecondition[] PcsWithSingleSubfieldChange(PreconditionId which, string subfield)
    {
        (bool sat, string? ev) Cell(PreconditionId id) =>
            id == which
                ? (subfield == "satisfied"
                    ? (false, BaseEvidence(id))                 // satisfied:true→false; evidence kept non-null (isolate satisfied)
                    : (true, BaseEvidence(id) + "-ALT"))        // evidence:<ref>→<ref>-ALT; satisfied kept true (isolate evidence)
                : (true, BaseEvidence(id));                     // untouched: the canonical AllSat cell

        (bool sat, string? ev) p1 = Cell(PreconditionId.P1);
        (bool sat, string? ev) p2 = Cell(PreconditionId.P2);
        (bool sat, string? ev) p3 = Cell(PreconditionId.P3);

        return new[]
        {
            ReadinessPrecondition.Create(PreconditionId.P1, "p1", p1.sat, p1.ev, Array.Empty<string>()),
            ReadinessPrecondition.Create(PreconditionId.P2, "p2", p2.sat, p2.ev, Array.Empty<string>()),
            ReadinessPrecondition.Create(PreconditionId.P3, "p3", p3.sat, p3.ev, Array.Empty<string>()),
        };
    }

    private static ReadinessBlock NN(ReadinessBlock? b)
    {
        Assert.NotNull(b); // a null here is a FIXTURE-SETUP defect, not a RED assertion.
        return b!;
    }

    // Base builders (the VALIDATED protected-main merge-base, pre-entry).
    //   v1 base: implicit-BLOCKED, no lifecycle key, no pointer (the real case).
    private static ReadinessBlock V1Base(ReadinessStatus status = ReadinessStatus.READY, string predicate = Predicate) =>
        NN(ReadinessBlock.TryCreate(1, status, predicate, AllSatPcs()));

    //   v2-BLOCKED base: schema 2, lifecycle=BLOCKED, pointer PROHIBITED (null).
    private static ReadinessBlock V2BlockedBase(ReadinessStatus status = ReadinessStatus.READY, string predicate = Predicate) =>
        NN(ReadinessBlock.TryCreate(2, status, predicate, AllSatPcs(), LifecycleState.Blocked, null));

    // Head builder: the ENTERED activation head (schema 2, lifecycle=ENTERED, pointer set to the
    // committed receipt). status/predicate/preconditions default to the SAME shape as the base so
    // the diff is activation-only; the negatives override exactly one axis.
    private static ReadinessBlock EnteredHead(
        ReadinessStatus status = ReadinessStatus.READY,
        string predicate = Predicate,
        ReadinessPrecondition[]? pcs = null,
        string pointer = ReceiptPath) =>
        NN(ReadinessBlock.TryCreate(2, status, predicate, pcs ?? AllSatPcs(), LifecycleState.Entered, pointer));

    // ========================================================================================
    // POSITIVE cells — RED against the deny stub (the stub rejects; these assert Accepted==true).
    // ========================================================================================

    // Tests INV-029 clause (C)/from-v1 [integration]: from-v1 valid activation → ACCEPT.
    // Base is implicit-v1-BLOCKED (schema 1); head is v2-ENTERED with the pointer set to the
    // receipt. status(READY)/ready_predicate/all preconditions are IDENTICAL base→head; only the
    // entry-receipt path is touched; the receipt binds an ancestor X; X's tree does NOT contain the
    // receipt (self-reference-safe); the receipt VERIFIED. All four clauses hold → accept.
    [Fact]
    public void FromV1_valid_activation_is_accepted()
    {
        ActivationValidationResult r = ActivationValidator.ValidateActivation(
            V1Base(),
            EnteredHead(),
            EntryOnlyPaths(),
            Ref(),
            SafeAncestry(),
            EntryIntegrity.Verified);

        Assert.True(r.Accepted, $"from-v1 activation must be accepted; got reject: {r.Reason}");
    }

    // Tests INV-029 clause (C)/from-v2-BLOCKED [integration]: from-v2-BLOCKED valid activation →
    // ACCEPT. Base is v2-BLOCKED (schema stays 2 across the transition); head is v2-ENTERED. The
    // lifecycle BLOCKED→ENTERED + pointer null→set is the only block change; everything else is as
    // the from-v1 case. All four clauses hold → accept.
    [Fact]
    public void FromV2Blocked_valid_activation_is_accepted()
    {
        ActivationValidationResult r = ActivationValidator.ValidateActivation(
            V2BlockedBase(),
            EnteredHead(),
            EntryOnlyPaths(),
            Ref(),
            SafeAncestry(),
            EntryIntegrity.Verified);

        Assert.True(r.Accepted, $"from-v2-BLOCKED activation must be accepted; got reject: {r.Reason}");
    }

    // ========================================================================================
    // FAIL-CLOSED negatives — GREEN on the deny stub; guard a fail-OPEN GREEN impl; MUST STAY green.
    // Each keeps every OTHER clause perfect so the SOLE rejection cause is the axis under test.
    // ========================================================================================

    // Tests INV-029 clause 2 (self-reference-safety) [integration]: the LOAD-BEARING AP-021
    // negative. Diff + ancestry + verify are otherwise a perfect from-v1 (or from-v2-BLOCKED)
    // activation, but ReceiptPath ∈ X's committed tree — the entry commit X contains the very
    // receipt that binds it (circular gate). MUST reject. Parameterized over BOTH transition shapes.
    [Theory]
    [InlineData(true)]  // from-v1 base
    [InlineData(false)] // from-v2-BLOCKED base
    public void SelfReference_receipt_contained_in_bound_tree_is_rejected(bool fromV1)
    {
        ReadinessBlock baseBlock = fromV1 ? V1Base() : V2BlockedBase();

        ActivationValidationResult r = ActivationValidator.ValidateActivation(
            baseBlock,
            EnteredHead(),
            EntryOnlyPaths(),
            Ref(),
            SelfRefAncestry(), // ReceiptPath ∈ CommittedTreeAtBoundCommit → self-reference
            EntryIntegrity.Verified);

        Assert.False(r.Accepted, "an entry commit X that CONTAINS its own binding receipt must be rejected (AP-021)");
    }

    // Tests INV-029 clause 3 (ancestor) [integration]: X is NOT an ancestor of HEAD. Everything
    // else is a perfect from-v1 activation (verified, self-reference-safe, activation-only diff,
    // entry-only paths). MUST reject.
    [Fact]
    public void NonAncestor_bound_commit_is_rejected()
    {
        ActivationValidationResult r = ActivationValidator.ValidateActivation(
            V1Base(),
            EnteredHead(),
            EntryOnlyPaths(),
            Ref(),
            SafeAncestry(ancestor: false), // BoundCommitIsAncestorOfHead == false
            EntryIntegrity.Verified);

        Assert.False(r.Accepted, "activation whose bound commit X is not an ancestor of HEAD must be rejected");
    }

    // Tests INV-029 clause 4 (verifying-receipt required) [integration]: over EACH non-Verified
    // integrity {Rejected, Unavailable, Absent}, activation must be rejected EVEN when the diff +
    // ancestry are otherwise perfect — proves the verifying-receipt gate (the initial-activation
    // asymmetry: a first activation strictly requires Verified; INV-026/027's monotonic
    // "later-unavailable does not un-enter" does NOT apply to a first activation). Cells derived
    // from the committed enum minus Verified (not an ad-hoc handful — PMB-003 / AP-022).
    public static IEnumerable<object[]> NonVerifiedIntegrities() =>
        Enum.GetValues<EntryIntegrity>()
            .Where(i => i != EntryIntegrity.Verified)
            .Select(i => new object[] { i });

    [Theory]
    [MemberData(nameof(NonVerifiedIntegrities))]
    public void NonVerified_receipt_is_rejected_even_when_diff_and_ancestry_are_perfect(EntryIntegrity integrity)
    {
        ActivationValidationResult r = ActivationValidator.ValidateActivation(
            V1Base(),
            EnteredHead(),
            EntryOnlyPaths(),
            Ref(),
            SafeAncestry(),
            integrity); // Rejected | Unavailable | Absent

        Assert.False(r.Accepted, $"activation acceptance requires a Verified entry receipt; {integrity} must be rejected");
    }

    // Tests INV-029 clause 1 (activation-only diff — precondition) [integration]: the activation
    // ALSO flips a precondition (P2 satisfied:true→false + evidence:ref→null) on the head. That
    // exceeds the activation-only diff (preconditions must be IDENTICAL base→head). Every other
    // clause is perfect. MUST reject.
    [Fact]
    public void Diff_that_also_flips_a_precondition_exceeds_activation_only_and_is_rejected()
    {
        ActivationValidationResult r = ActivationValidator.ValidateActivation(
            V1Base(),
            EnteredHead(pcs: P2FlippedPcs()), // a precondition changed across the diff
            EntryOnlyPaths(),
            Ref(),
            SafeAncestry(),
            EntryIntegrity.Verified);

        Assert.False(r.Accepted, "an activation that also flips a precondition exceeds the activation-only diff");
    }

    // Tests INV-029 clause 1 (activation-only diff — EVERY precondition's satisfied AND evidence)
    // [integration]: field-by-field pinning over the cross-product {P1,P2,P3} × {satisfied,evidence}
    // (PMB-003 / AP-022 — a per-cell enumeration, not a representative sample). Each case mutates
    // EXACTLY ONE precondition subfield base→head; every other clause is perfect. The "evidence"
    // rows (satisfied kept true, evidence:<ref>→<ref>-ALT) are the LOAD-BEARING ones: a GREEN impl
    // that compares only the Satisfied booleans would fail OPEN on an evidence-only swap. Each MUST
    // reject.
    public static IEnumerable<object[]> PreconditionSubfieldChanges()
    {
        foreach (PreconditionId which in new[] { PreconditionId.P1, PreconditionId.P2, PreconditionId.P3 })
        {
            foreach (string subfield in new[] { "satisfied", "evidence" })
            {
                yield return new object[] { which, subfield };
            }
        }
    }

    [Theory]
    [MemberData(nameof(PreconditionSubfieldChanges))]
    public void Diff_that_changes_any_precondition_subfield_exceeds_activation_only_and_is_rejected(
        PreconditionId which, string subfield)
    {
        ActivationValidationResult r = ActivationValidator.ValidateActivation(
            V1Base(),
            EnteredHead(pcs: PcsWithSingleSubfieldChange(which, subfield)), // exactly one (pc, subfield) changed
            EntryOnlyPaths(),
            Ref(),
            SafeAncestry(),
            EntryIntegrity.Verified);

        Assert.False(r.Accepted, $"an activation that changes {which}.{subfield} exceeds the activation-only diff");
    }

    // Tests INV-029 clause 1 (activation-only diff — status) [integration]: the activation ALSO
    // changes status (base READY → head BLOCKED). status must be IDENTICAL base→head. Every other
    // clause is perfect. MUST reject.
    [Fact]
    public void Diff_that_also_changes_status_exceeds_activation_only_and_is_rejected()
    {
        ActivationValidationResult r = ActivationValidator.ValidateActivation(
            V1Base(ReadinessStatus.READY),
            EnteredHead(status: ReadinessStatus.BLOCKED), // status changed across the diff
            EntryOnlyPaths(),
            Ref(),
            SafeAncestry(),
            EntryIntegrity.Verified);

        Assert.False(r.Accepted, "an activation that also changes status exceeds the activation-only diff");
    }

    // Tests INV-029 clause 1 (activation-only diff — status, FROM-V2-BLOCKED regime) [integration]:
    // the extra-change guard must hold in BOTH transition shapes, not only from-v1. Here the base is
    // v2-BLOCKED (schema stays 2 across the transition) and the head activation ALSO changes status
    // (READY→BLOCKED). Every other clause is perfect. MUST reject — closing the regime asymmetry.
    [Fact]
    public void FromV2Blocked_diff_that_also_changes_status_exceeds_activation_only_and_is_rejected()
    {
        ActivationValidationResult r = ActivationValidator.ValidateActivation(
            V2BlockedBase(ReadinessStatus.READY),
            EnteredHead(status: ReadinessStatus.BLOCKED), // status changed across the diff
            EntryOnlyPaths(),
            Ref(),
            SafeAncestry(),
            EntryIntegrity.Verified);

        Assert.False(r.Accepted, "a from-v2-BLOCKED activation that also changes status exceeds the activation-only diff");
    }

    // Tests INV-029 clause 1 (activation-only diff — ready_predicate) [integration]: the activation
    // ALSO edits ready_predicate (base "P1 AND P2 AND P3" → head "P1 AND P2"). ready_predicate must
    // be IDENTICAL base→head. Every other clause is perfect. MUST reject.
    [Fact]
    public void Diff_that_also_changes_ready_predicate_exceeds_activation_only_and_is_rejected()
    {
        ActivationValidationResult r = ActivationValidator.ValidateActivation(
            V1Base(predicate: "P1 AND P2 AND P3"),
            EnteredHead(predicate: "P1 AND P2"), // ready_predicate changed across the diff
            EntryOnlyPaths(),
            Ref(),
            SafeAncestry(),
            EntryIntegrity.Verified);

        Assert.False(r.Accepted, "an activation that also edits ready_predicate exceeds the activation-only diff");
    }

    // Tests INV-029 clause 1 (wrong transition shape — base already ENTERED) [integration]: the
    // merge-base is ALREADY lifecycle=ENTERED (there is NO BLOCKED→ENTERED transition to perform —
    // and no ENTERED→BLOCKED transition exists at all). A "re-activation" from an established
    // ENTERED base is not a legal first activation. MUST reject.
    [Fact]
    public void WrongShape_base_already_entered_has_no_blocked_to_entered_transition_and_is_rejected()
    {
        ReadinessBlock enteredBase =
            NN(ReadinessBlock.TryCreate(2, ReadinessStatus.READY, Predicate, AllSatPcs(), LifecycleState.Entered, ReceiptPath));

        ActivationValidationResult r = ActivationValidator.ValidateActivation(
            enteredBase,       // base is already ENTERED — no BLOCKED→ENTERED to perform
            EnteredHead(),
            EntryOnlyPaths(),
            Ref(),
            SafeAncestry(),
            EntryIntegrity.Verified);

        Assert.False(r.Accepted, "an activation whose merge-base is already ENTERED (no BLOCKED→ENTERED) must be rejected");
    }

    // Tests INV-029 clause 1 (wrong transition shape — pointer not set in head) [integration]: the
    // "activation" head did NOT set entry_evidence_pointer / did NOT reach ENTERED — modeled as a
    // v2-BLOCKED head (lifecycle still BLOCKED, pointer null). The transition requires
    // lifecycle:BLOCKED→ENTERED AND pointer:null→set; neither happened. MUST reject.
    [Fact]
    public void WrongShape_head_pointer_not_set_is_rejected()
    {
        ReadinessBlock noPointerHead =
            NN(ReadinessBlock.TryCreate(2, ReadinessStatus.READY, Predicate, AllSatPcs(), LifecycleState.Blocked, null));

        ActivationValidationResult r = ActivationValidator.ValidateActivation(
            V1Base(),
            noPointerHead,     // pointer NOT set, lifecycle NOT flipped to ENTERED
            EntryOnlyPaths(),
            Ref(),
            SafeAncestry(),
            EntryIntegrity.Verified);

        Assert.False(r.Accepted, "an activation whose head never set entry_evidence_pointer / never reached ENTERED must be rejected");
    }

    // Tests INV-029 clause 1 (activation touches ONLY the entry-receipt path) [integration]:
    // DEFENSIVE — the changed controlled paths include a path OTHER than the entry receipt (an
    // evidence path smuggled into the activation PR). The activation must touch only the
    // entry-receipt path. Every other clause is perfect. MUST reject.
    [Fact]
    public void Changed_paths_beyond_the_entry_receipt_is_rejected()
    {
        ActivationValidationResult r = ActivationValidator.ValidateActivation(
            V1Base(),
            EnteredHead(),
            new[] { ReceiptPath, TreeEvidenceA }, // a non-entry-receipt path smuggled in
            Ref(),
            SafeAncestry(),
            EntryIntegrity.Verified);

        Assert.False(r.Accepted, "an activation that touches a path beyond the entry receipt must be rejected");
    }

    // Tests INV-029 (deny-by-default at the boundary) [unit]: DEFENSIVE — a malformed call with a
    // null in ANY supplied reference-argument position fails closed (AP-001; mirrors how the sibling
    // PrClassifier.Classify null-guards ALL of its inputs). Covers every nullable position:
    // mergeBaseBlock, headBlock, changedControlledPaths, entryReceiptRef, ancestry (the
    // EntryIntegrity enum is a value type — not nullable — so it has no null cell). null! silences
    // the nullable-context warning; TreatWarningsAsErrors is false so this compiles.
    [Theory]
    [InlineData(0)] // mergeBaseBlock
    [InlineData(1)] // headBlock
    [InlineData(2)] // changedControlledPaths
    [InlineData(3)] // entryReceiptRef
    [InlineData(4)] // ancestry
    public void Null_in_any_supplied_input_position_fails_closed(int nullPosition)
    {
        ReadinessBlock? baseBlock = nullPosition == 0 ? null : V1Base();
        ReadinessBlock? headBlock = nullPosition == 1 ? null : EnteredHead();
        IReadOnlyCollection<string>? paths = nullPosition == 2 ? null : EntryOnlyPaths();
        EntryReceiptRef? receiptRef = nullPosition == 3 ? null : Ref();
        ActivationAncestry? ancestry = nullPosition == 4 ? null : SafeAncestry();

        ActivationValidationResult r = ActivationValidator.ValidateActivation(
            baseBlock!, headBlock!, paths!, receiptRef!, ancestry!, EntryIntegrity.Verified);

        Assert.False(r.Accepted, $"a null supplied input at position {nullPosition} must fail closed (deny-by-default)");
    }

    // ========================================================================================
    // RESULT TYPE — the accept/reject decision is exposed cleanly (a bool accept + a typed reason).
    // ========================================================================================

    // Tests INV-029 (result-type contract) [unit]: ActivationValidationResult exposes a public
    // bool Accepted + a public string Reason. Structural — the reviewer/caller reads the decision
    // and the typed cause off the result, not out of band.
    [Fact]
    public void Result_exposes_a_bool_accepted_and_a_string_reason()
    {
        PropertyInfo? accepted = typeof(ActivationValidationResult).GetProperty(
            nameof(ActivationValidationResult.Accepted), BindingFlags.Public | BindingFlags.Instance);
        PropertyInfo? reason = typeof(ActivationValidationResult).GetProperty(
            nameof(ActivationValidationResult.Reason), BindingFlags.Public | BindingFlags.Instance);

        Assert.NotNull(accepted);
        Assert.Equal(typeof(bool), accepted!.PropertyType);
        Assert.NotNull(reason);
        Assert.Equal(typeof(string), reason!.PropertyType);
    }

    // Tests INV-029 (result-type contract — reject carries a typed reason) [unit]: on a fail-closed
    // reject the accept flag is false AND the reason is a non-empty typed string. Exercised against
    // a self-reference reject (green on the stub and after GREEN — a genuine rejection either way).
    [Fact]
    public void Reject_has_accepted_false_and_a_nonempty_reason()
    {
        ActivationValidationResult r = ActivationValidator.ValidateActivation(
            V1Base(),
            EnteredHead(),
            EntryOnlyPaths(),
            Ref(),
            SelfRefAncestry(),
            EntryIntegrity.Verified);

        Assert.False(r.Accepted);
        Assert.False(string.IsNullOrEmpty(r.Reason), "a reject must carry a typed, non-empty reason string");
    }
}
