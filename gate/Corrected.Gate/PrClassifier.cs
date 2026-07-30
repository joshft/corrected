using System;
using System.Collections.Generic;
using System.Linq;
using Corrected.Gate.Kernel;

namespace Corrected.Gate;

/// <summary>
/// PRH-007 (spec ~1265–1382): the FIVE typed PR classes the TOTAL classifier assigns to
/// any PR that touches a controlled path/field. Exactly these five members — no "reject"
/// member (a rejection is <see cref="PrClassification.Accepted"/> == false, NOT a sixth
/// class), and no "most-permissive default". P1 is deliberately NOT a class (round-11:
/// P1.satisfied/P1.evidence are governed by P1's own Stage-B migration contract).
/// </summary>
public enum PrClass
{
    /// <summary>pre-entry (lifecycle=BLOCKED): for exactly one Pk, k∈{2,3}, satisfied:false→true + evidence:null→ref + set the external reference; status:BLOCKED→READY iff P1∧P2∧P3 all re-derive true.</summary>
    PreconditionReactivation,

    /// <summary>pre-entry: for exactly one Pk, k∈{2,3}, satisfied:true→false + evidence:ref→null + retire the external reference; status:READY→BLOCKED iff the block was READY.</summary>
    PreconditionInvalidation,

    /// <summary>post-entry (lifecycle=ENTERED): a new versioned P3 receipt/bundle; move the active-baseline pointer ONLY — never satisfied/status.</summary>
    P3Refresh,

    /// <summary>pre-entry→ENTERED: the atomic entry transition (from v1: schema 1→2 + add lifecycle:ENTERED; from v2-BLOCKED: lifecycle:BLOCKED→ENTERED) + set entry_evidence_pointer; status/ready_predicate/preconditions unchanged.</summary>
    PhaseEntry,

    /// <summary>any regime: frozen-mechanism paths + gate code; MUST NOT touch any evidence field (satisfied/status/lifecycle/either pointer/test/attestations/** evidence).</summary>
    MechanismChange,
}

/// <summary>
/// The trusted PR-label strings (PRH-007 "Mode declaration = TRUSTED PR METADATA"): the
/// class is DECLARED by a PR label supplied in the trusted GitHub Actions PR-event context
/// and CROSS-CHECKED against the observed parsed-span diff — the diff is the authority.
/// </summary>
public static class PrClassLabels
{
    public const string PreconditionReactivation = "precondition-reactivation";
    public const string PreconditionInvalidation = "precondition-invalidation";
    public const string P3Refresh = "P3-refresh";
    public const string PhaseEntry = "phase-entry";
    public const string MechanismChange = "mechanism-change";
}

/// <summary>
/// The classifier verdict: the assigned class + the reviewer-facing render
/// (<c>PR class = X; permitted spans = …; observed changes = …</c>), OR a fail-closed
/// rejection (<see cref="Accepted"/> == false, <see cref="AssignedClass"/> == null). Immutable.
/// </summary>
public sealed class PrClassification
{
    private static readonly IReadOnlyList<string> Empty = Array.Empty<string>();

    private PrClassification(
        bool accepted,
        PrClass? assignedClass,
        string rejectionReason,
        IReadOnlyList<string> permittedSpans,
        IReadOnlyList<string> observedChanges)
    {
        Accepted = accepted;
        AssignedClass = assignedClass;
        RejectionReason = rejectionReason;
        PermittedSpans = permittedSpans;
        ObservedChanges = observedChanges;
    }

    /// <summary>True iff the PR was assigned EXACTLY one class whose allowlist covers every observed change AND whose label agrees with the diff. False == fail-closed.</summary>
    public bool Accepted { get; }

    /// <summary>The assigned class on accept; null on a fail-closed rejection (never defaults to the most permissive).</summary>
    public PrClass? AssignedClass { get; }

    /// <summary>Typed fail-closed reason on reject; empty on accept.</summary>
    public string RejectionReason { get; }

    /// <summary>The reviewer-facing permitted-spans render (non-empty on accept).</summary>
    public IReadOnlyList<string> PermittedSpans { get; }

    /// <summary>The reviewer-facing observed-changes render.</summary>
    public IReadOnlyList<string> ObservedChanges { get; }

    internal static PrClassification Accept(
        PrClass assignedClass, IReadOnlyList<string> permittedSpans, IReadOnlyList<string> observedChanges)
        => new(true, assignedClass, string.Empty, permittedSpans, observedChanges);

    internal static PrClassification Reject(string reason, IReadOnlyList<string>? observedChanges = null)
        => new(false, null, reason, Empty, observedChanges ?? Empty);
}

/// <summary>
/// PRH-007: the TOTAL, fail-closed PR-class classifier (spec ~1265–1382). Classifies any PR
/// touching a controlled path/field against the VALIDATED protected-<c>main</c> merge-base and
/// assigns EXACTLY ONE of the five <see cref="PrClass"/> values, or fails closed. Enforces:
///   * total + fail-closed: zero classes (untyped touch) OR more than one class → reject
///     (never the most-permissive default); any touch outside the assigned class's allowlist → reject;
///   * evidence-activation ⊥ mechanism-change (mutually exclusive — one PR can never be both);
///   * trusted-label ↔ parsed-span cross-check — the diff is the authority;
///   * status-follows-preconditions lockstep (round-10) over the COMPLETE {P2,P3} set (P1 excluded, round-11);
///   * the block-field ↔ external-reference coupling (round-11 dangling-reference guard is at the carrier).
///
/// Inputs are all SUPPLIED/synthetic (this sub-track builds ONLY the classifier): the validated
/// <paramref name="baseBlock"/> (the authenticated protected-main merge-base — there is no
/// derive-base-from-HEAD^ path), the <paramref name="headBlock"/>, the set of
/// <paramref name="changedControlledPaths"/>, and the <paramref name="declaredLabel"/>. The
/// git merge-base computation, the INV-029 cryptographic activation-diff validator, INV-028
/// health, and real cosign are OUT of scope for this sub-track.
/// </summary>
public static class PrClassifier
{
    /// <summary>
    /// Classify a PR from its validated base/head readiness blocks, its changed controlled
    /// paths, and its declared trusted label. Pure over the supplied inputs (no I/O).
    /// </summary>
    public static PrClassification Classify(
        ReadinessBlock baseBlock,
        ReadinessBlock headBlock,
        IReadOnlyCollection<string> changedControlledPaths,
        string declaredLabel)
    {
        // Defensive: any null supplied input fails closed (a total classifier is never
        // permissive on a malformed call — AP-001 deny-by-default at the boundary).
        if (baseBlock is null || headBlock is null || changedControlledPaths is null || declaredLabel is null)
        {
            return PrClassification.Reject("null-input");
        }

        // (1) Parse the TRUSTED PR label. An unknown/garbage label is an untyped touch → fail closed.
        PrClass? declared = ParseLabel(declaredLabel);
        if (declared is null)
        {
            return PrClassification.Reject($"unknown-label:{declaredLabel}");
        }

        // (2) Bucket the changed controlled paths into the documented families (prefix matching,
        //     never literal fixture strings) so the logic generalizes over the path structure.
        PathBuckets buckets = BucketPaths(changedControlledPaths);

        // (3) Compute the parsed base→head block-field diff — the DIFF is the authority. The base
        //     is the VALIDATED protected-main merge-base supplied by the caller; the verdict is
        //     fully determined by (base, head, changedControlledPaths), so a wider diff scope
        //     legitimately flips accept→reject (RS-027 authenticated base).
        Diff diff = Diff.From(baseBlock, headBlock);

        // (4) Derive the observed class(es) INDEPENDENTLY of the label: a class matches only when
        //     the diff + paths satisfy its ENTIRE contract (block-field allowlist + path allowlist
        //     + direction + coupling + exactly-one + regime). Because the five contracts are
        //     mutually exclusive by construction, a legitimate PR matches exactly one; a
        //     multi-shaped or partial PR matches zero or more than one.
        var matched = new List<PrClass>();
        if (MatchesMechanismChange(diff, buckets)) matched.Add(PrClass.MechanismChange);
        if (MatchesPhaseEntry(baseBlock, headBlock, diff, buckets)) matched.Add(PrClass.PhaseEntry);
        if (MatchesP3Refresh(baseBlock, headBlock, diff, buckets)) matched.Add(PrClass.P3Refresh);
        if (MatchesReactivation(baseBlock, headBlock, diff, buckets)) matched.Add(PrClass.PreconditionReactivation);
        if (MatchesInvalidation(baseBlock, headBlock, diff, buckets)) matched.Add(PrClass.PreconditionInvalidation);

        // (5) TOTAL + fail-closed: zero classes (untyped/no-shape touch) OR more than one class
        //     (multi-class) → reject. NEVER default to the most-permissive class.
        if (matched.Count == 0)
        {
            return PrClassification.Reject("no-class-matched (untyped/zero-class controlled touch)");
        }

        if (matched.Count > 1)
        {
            return PrClassification.Reject("multi-class touch: " + string.Join(",", matched));
        }

        // (6) Trusted-label ↔ parsed-span cross-check: the observed class must AGREE with the
        //     declared label (this catches wrong-direction reactivation⇄invalidation labels, a
        //     valid-but-mislabelled diff, and P3-refresh/mechanism labels on evidence-flip diffs —
        //     the diff, not the label, decides).
        PrClass observed = matched[0];
        if (observed != declared.Value)
        {
            return PrClassification.Reject(
                $"label '{declaredLabel}' disagrees with observed class '{observed}'");
        }

        // (7) Accept: render PR class = X; permitted spans = …; observed changes = … for the reviewer.
        return PrClassification.Accept(
            observed,
            PermittedSpansFor(observed),
            BuildObservedChanges(changedControlledPaths, diff));
    }

    // ------------------------------------------------------------------------------------------
    // Label parsing.
    // ------------------------------------------------------------------------------------------

    private static PrClass? ParseLabel(string label) => label switch
    {
        PrClassLabels.PreconditionReactivation => PrClass.PreconditionReactivation,
        PrClassLabels.PreconditionInvalidation => PrClass.PreconditionInvalidation,
        PrClassLabels.P3Refresh => PrClass.P3Refresh,
        PrClassLabels.PhaseEntry => PrClass.PhaseEntry,
        PrClassLabels.MechanismChange => PrClass.MechanismChange,
        _ => null,
    };

    // ------------------------------------------------------------------------------------------
    // Path bucketing (structural prefix families — never literal fixture strings).
    //   * entry-receipt : test/attestations/entry/**            (the phase-entry receipt)
    //   * evidence      : test/attestations/** (non-entry) + test/manifests/**  (P2/P3 evidence)
    //   * mechanism     : gate/**, .github/workflows/**, spikes/**/schema/**    (frozen mechanism)
    //   * unknown       : anything else (an untyped controlled touch)
    // ------------------------------------------------------------------------------------------

    private const string EntryReceiptPrefix = "test/attestations/entry/";
    private const string AttestationsPrefix = "test/attestations/";
    private const string ManifestsPrefix = "test/manifests/";
    private const string GatePrefix = "gate/";
    private const string WorkflowsPrefix = ".github/workflows/";
    private const string SpikesPrefix = "spikes/";
    private const string SchemaSegment = "/schema/";

    private static PathKind ClassifyPath(string path)
    {
        if (path.StartsWith(EntryReceiptPrefix, StringComparison.Ordinal))
        {
            return PathKind.EntryReceipt;
        }

        if (path.StartsWith(AttestationsPrefix, StringComparison.Ordinal) ||
            path.StartsWith(ManifestsPrefix, StringComparison.Ordinal))
        {
            return PathKind.Evidence;
        }

        if (path.StartsWith(GatePrefix, StringComparison.Ordinal) ||
            path.StartsWith(WorkflowsPrefix, StringComparison.Ordinal) ||
            (path.StartsWith(SpikesPrefix, StringComparison.Ordinal) &&
             path.Contains(SchemaSegment, StringComparison.Ordinal)))
        {
            return PathKind.Mechanism;
        }

        return PathKind.Unknown;
    }

    private static PathBuckets BucketPaths(IReadOnlyCollection<string> paths)
    {
        var b = new PathBuckets { Total = paths.Count };
        foreach (string p in paths)
        {
            switch (ClassifyPath(p))
            {
                case PathKind.EntryReceipt: b.EntryReceipt++; break;
                case PathKind.Evidence: b.Evidence++; break;
                case PathKind.Mechanism: b.Mechanism++; break;
                default: b.Unknown++; break;
            }
        }

        return b;
    }

    private enum PathKind
    {
        Evidence,
        EntryReceipt,
        Mechanism,
        Unknown,
    }

    private sealed class PathBuckets
    {
        public int Total;
        public int Evidence;
        public int EntryReceipt;
        public int Mechanism;
        public int Unknown;

        /// <summary>Non-empty and EVERY changed path is an evidence path.</summary>
        public bool AllEvidence => Total > 0 && Evidence == Total;

        /// <summary>Non-empty and EVERY changed path is a frozen-mechanism/gate path.</summary>
        public bool AllMechanism => Total > 0 && Mechanism == Total;

        /// <summary>Non-empty and EVERY changed path is the phase-entry receipt.</summary>
        public bool AllEntryReceipt => Total > 0 && EntryReceipt == Total;
    }

    // ------------------------------------------------------------------------------------------
    // The parsed base→head diff.
    // ------------------------------------------------------------------------------------------

    private sealed class PcDelta
    {
        public bool BaseSat;
        public bool HeadSat;
        public string? BaseEv;
        public string? HeadEv;

        public bool SatChanged => BaseSat != HeadSat;

        public bool EvChanged => !string.Equals(BaseEv, HeadEv, StringComparison.Ordinal);

        public bool Unchanged => !SatChanged && !EvChanged;

        /// <summary>satisfied:false→true IN LOCKSTEP with evidence:null→&lt;reference&gt;.</summary>
        public bool CleanActivation => !BaseSat && HeadSat && BaseEv is null && HeadEv is not null;

        /// <summary>satisfied:true→false IN LOCKSTEP with evidence:&lt;reference&gt;→null.</summary>
        public bool CleanInvalidation => BaseSat && !HeadSat && BaseEv is not null && HeadEv is null;
    }

    private sealed class Diff
    {
        public PcDelta P1 = new();
        public PcDelta P2 = new();
        public PcDelta P3 = new();
        public bool StatusChanged;
        public bool SchemaChanged;
        public bool LifecycleChanged;
        public bool PointerChanged;
        public bool PredicateChanged;

        /// <summary>No readiness-block FIELD changed at all (the mechanism-change / P3-refresh precondition).</summary>
        public bool NoBlockFieldChange =>
            P1.Unchanged && P2.Unchanged && P3.Unchanged
            && !StatusChanged && !SchemaChanged && !LifecycleChanged
            && !PointerChanged && !PredicateChanged;

        public static Diff From(ReadinessBlock b, ReadinessBlock h) => new()
        {
            P1 = DeltaFor(b, h, PreconditionId.P1),
            P2 = DeltaFor(b, h, PreconditionId.P2),
            P3 = DeltaFor(b, h, PreconditionId.P3),
            StatusChanged = b.Status != h.Status,
            SchemaChanged = b.SchemaVersion != h.SchemaVersion,
            LifecycleChanged = b.Lifecycle != h.Lifecycle,
            PointerChanged = !string.Equals(b.EntryEvidencePointer, h.EntryEvidencePointer, StringComparison.Ordinal),
            PredicateChanged = !string.Equals(b.ReadyPredicate, h.ReadyPredicate, StringComparison.Ordinal),
        };

        private static PcDelta DeltaFor(ReadinessBlock b, ReadinessBlock h, PreconditionId id)
        {
            ReadinessPrecondition pb = b.Preconditions.First(p => p.Id == id);
            ReadinessPrecondition ph = h.Preconditions.First(p => p.Id == id);
            return new PcDelta
            {
                BaseSat = pb.Satisfied,
                HeadSat = ph.Satisfied,
                BaseEv = pb.Evidence,
                HeadEv = ph.Evidence,
            };
        }

        /// <summary>Reviewer-facing notes for the observed block-field deltas (render context only).</summary>
        public IEnumerable<string> FieldDeltaNotes()
        {
            if (StatusChanged) yield return "status changed";
            if (SchemaChanged) yield return "schema_version changed";
            if (LifecycleChanged) yield return "lifecycle changed";
            if (PointerChanged) yield return "entry_evidence_pointer changed";
            if (PredicateChanged) yield return "ready_predicate changed";

            foreach ((string id, PcDelta d) in new[] { ("P1", P1), ("P2", P2), ("P3", P3) })
            {
                if (d.SatChanged) yield return $"{id}.satisfied {d.BaseSat}->{d.HeadSat}";
                if (d.EvChanged) yield return $"{id}.evidence changed";
            }
        }
    }

    private static bool AllSatisfied(ReadinessBlock b) => b.Preconditions.All(p => p.Satisfied);

    // ------------------------------------------------------------------------------------------
    // The five class contracts. Each returns true ONLY when the diff + paths satisfy the class's
    // ENTIRE allowlist — any field/path outside the class is a non-match (fail closed at (5)/(6)).
    // ------------------------------------------------------------------------------------------

    // Mechanism-change: frozen-mechanism/gate paths and NOTHING that touches an evidence field
    // (satisfied/status/lifecycle/either pointer/schema/ready_predicate) or a test/attestations/**
    // evidence path. Regime-agnostic. Evidence-activation ⊥ mechanism-change follows: a diff that
    // flips any precondition is not NoBlockFieldChange, and a diff touching an evidence path is not
    // AllMechanism — so a PR can never be both.
    private static bool MatchesMechanismChange(Diff diff, PathBuckets buckets)
        => diff.NoBlockFieldChange && buckets.AllMechanism;

    // P3-refresh: POST-ENTRY only (lifecycle=ENTERED on BOTH sides); no block field change at all;
    // only evidence (active-baseline / bundle) paths move.
    private static bool MatchesP3Refresh(ReadinessBlock b, ReadinessBlock h, Diff diff, PathBuckets buckets)
        => b.Lifecycle == LifecycleState.Entered
           && h.Lifecycle == LifecycleState.Entered
           && diff.NoBlockFieldChange
           && buckets.AllEvidence;

    // Phase-entry: the atomic entry transition lifecycle:BLOCKED→ENTERED + set entry_evidence_pointer
    // (schema 1→2 from v1, or 2→2 from v2-BLOCKED — the schema bump is IN the allowlist); status,
    // ready_predicate and every precondition UNCHANGED; only the entry-receipt path is touched.
    private static bool MatchesPhaseEntry(ReadinessBlock b, ReadinessBlock h, Diff diff, PathBuckets buckets)
        => b.Lifecycle == LifecycleState.Blocked
           && h.Lifecycle == LifecycleState.Entered
           && b.EntryEvidencePointer is null
           && h.EntryEvidencePointer is not null
           && !diff.StatusChanged
           && !diff.PredicateChanged
           && diff.P1.Unchanged && diff.P2.Unchanged && diff.P3.Unchanged
           && buckets.AllEntryReceipt;

    // Precondition-reactivation: PRE-ENTRY (lifecycle=BLOCKED both sides); for EXACTLY ONE Pk,
    // k∈{P2,P3}, satisfied:false→true IN LOCKSTEP with evidence:null→ref; the other {P2,P3}
    // precondition and P1 UNCHANGED (P1 is not a class — round-11); no schema/lifecycle/pointer/
    // ready_predicate change; status follows the re-derived preconditions (READY iff all three now
    // true, else BLOCKED); only evidence paths touched.
    private static bool MatchesReactivation(ReadinessBlock b, ReadinessBlock h, Diff diff, PathBuckets buckets)
    {
        if (b.Lifecycle != LifecycleState.Blocked || h.Lifecycle != LifecycleState.Blocked)
        {
            return false;
        }

        if (diff.SchemaChanged || diff.LifecycleChanged || diff.PointerChanged || diff.PredicateChanged)
        {
            return false;
        }

        if (!diff.P1.Unchanged)
        {
            return false; // P1 is governed by its own Stage-B contract — never a precondition class.
        }

        bool p2Act = diff.P2.CleanActivation;
        bool p3Act = diff.P3.CleanActivation;
        int activated = (p2Act ? 1 : 0) + (p3Act ? 1 : 0);
        if (activated != 1)
        {
            return false; // exactly-one: zero (no flip / broken coupling) or two both fail closed.
        }

        // The OTHER {P2,P3} precondition must be entirely untouched (no sat/evidence change).
        if (p2Act && !diff.P3.Unchanged) return false;
        if (p3Act && !diff.P2.Unchanged) return false;

        // Status follows the re-derived preconditions (round-10 lockstep) on the head block.
        ReadinessStatus expected = AllSatisfied(h) ? ReadinessStatus.READY : ReadinessStatus.BLOCKED;
        if (h.Status != expected)
        {
            return false;
        }

        return buckets.AllEvidence;
    }

    // Precondition-invalidation: PRE-ENTRY; for EXACTLY ONE Pk, k∈{P2,P3}, satisfied:true→false IN
    // LOCKSTEP with evidence:ref→null (leaving evidence non-null = a dangling reference → fail
    // closed); the other {P2,P3} and P1 UNCHANGED; no schema/lifecycle/pointer/ready_predicate
    // change; status follows (BLOCKED, since a precondition is now false); only evidence paths touched.
    private static bool MatchesInvalidation(ReadinessBlock b, ReadinessBlock h, Diff diff, PathBuckets buckets)
    {
        if (b.Lifecycle != LifecycleState.Blocked || h.Lifecycle != LifecycleState.Blocked)
        {
            return false;
        }

        if (diff.SchemaChanged || diff.LifecycleChanged || diff.PointerChanged || diff.PredicateChanged)
        {
            return false;
        }

        if (!diff.P1.Unchanged)
        {
            return false;
        }

        bool p2Inv = diff.P2.CleanInvalidation;
        bool p3Inv = diff.P3.CleanInvalidation;
        int invalidated = (p2Inv ? 1 : 0) + (p3Inv ? 1 : 0);
        if (invalidated != 1)
        {
            return false;
        }

        if (p2Inv && !diff.P3.Unchanged) return false;
        if (p3Inv && !diff.P2.Unchanged) return false;

        // Status follows: with a precondition now false the block cannot be READY.
        ReadinessStatus expected = AllSatisfied(h) ? ReadinessStatus.READY : ReadinessStatus.BLOCKED;
        if (h.Status != expected)
        {
            return false;
        }

        return buckets.AllEvidence;
    }

    // ------------------------------------------------------------------------------------------
    // Reviewer-facing render.
    // ------------------------------------------------------------------------------------------

    private static IReadOnlyList<string> PermittedSpansFor(PrClass c) => c switch
    {
        PrClass.PreconditionReactivation => new[]
        {
            "precondition Pk in {P2,P3}: satisfied false->true",
            "precondition Pk evidence: null-><registered reference>",
            "status: BLOCKED->READY iff P1 AND P2 AND P3 all re-derive true",
            "evidence paths: test/attestations/** or test/manifests/**",
        },
        PrClass.PreconditionInvalidation => new[]
        {
            "precondition Pk in {P2,P3}: satisfied true->false",
            "precondition Pk evidence: <reference>->null",
            "status: READY->BLOCKED",
            "evidence paths: test/attestations/** or test/manifests/**",
        },
        PrClass.P3Refresh => new[]
        {
            "post-entry (lifecycle=ENTERED) active-baseline repoint",
            "new versioned P3 receipt/bundle under test/attestations/**",
            "no readiness-block field change",
        },
        PrClass.PhaseEntry => new[]
        {
            "lifecycle: BLOCKED->ENTERED (schema_version 1->2 or 2->2)",
            "set entry_evidence_pointer: null-><entry receipt>",
            "entry receipt path: test/attestations/entry/**",
        },
        PrClass.MechanismChange => new[]
        {
            "frozen-mechanism/gate paths: gate/**, .github/workflows/**, spikes/**/schema/**",
            "no readiness-block field change",
        },
        _ => Array.Empty<string>(),
    };

    private static IReadOnlyList<string> BuildObservedChanges(IReadOnlyCollection<string> paths, Diff diff)
    {
        // Echo EVERY supplied changed path verbatim (the render must reflect the inputs), then
        // append the observed block-field deltas for reviewer context.
        var observed = new List<string>(paths);
        observed.AddRange(diff.FieldDeltaNotes());
        return observed;
    }
}
