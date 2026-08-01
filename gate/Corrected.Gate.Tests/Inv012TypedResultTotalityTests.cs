using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Corrected.Gate;
using Corrected.Gate.Kernel;
using Xunit;

namespace Corrected.Gate.Tests;

/// <summary>
/// P3 determinism-attestation spec INV-012 (~453-500): the P3 probe computes an INTERNAL typed
/// result (an enum, NOT a free string) and the mapping from EVERY reason to
/// <c>{rejected|unavailable}</c> is TOTAL AND fail-closed by DEFAULT (RS-002). <c>unavailable</c>
/// is reserved for a CLOSED 2-member transient-fault set; everything else — and the DEFAULT — is
/// <c>rejected</c>. <c>satisfied:true</c> is derived ONLY when <c>verified</c>. The internal
/// reason -> carrier <c>ProbeReasons</c> token boundary is total with no free-string fallthrough.
///
/// The totality cross-product derives its expected mapping FROM the committed enum's
/// <see cref="VerifySeverityAttribute"/> annotations via reflection (RS-010 / AP-022 / PMB-003) —
/// never a test literal — so shrinking or re-pointing the map is a reviewable diff on the enum.
/// </summary>
public class Inv012TypedResultTotalityTests
{
    private static DeterminismVerifyReason[] AllReasons()
        => Enum.GetValues<DeterminismVerifyReason>();

    private static VerifySeverity DeclaredSeverity(DeterminismVerifyReason reason)
    {
        FieldInfo field = typeof(DeterminismVerifyReason).GetField(reason.ToString())!;
        VerifySeverityAttribute? attr = field.GetCustomAttribute<VerifySeverityAttribute>();
        Assert.True(
            attr is not null,
            $"INV-012: reason '{reason}' carries NO committed [VerifySeverity(...)] annotation — " +
            "the totality contract requires every reason to declare its severity on the enum.");
        return attr!.Severity;
    }

    // Tests INV-012 [unit] (completeness): EVERY committed reason carries exactly one
    // [VerifySeverity(...)] annotation, so the map's expected side is derived from the enum and a
    // future reason added without a declared severity fails this test (AP-022 completeness).
    [Fact]
    public void Every_reason_declares_exactly_one_committed_severity()
    {
        DeterminismVerifyReason[] reasons = AllReasons();
        Assert.NotEmpty(reasons);
        foreach (DeterminismVerifyReason reason in reasons)
        {
            FieldInfo field = typeof(DeterminismVerifyReason).GetField(reason.ToString())!;
            VerifySeverityAttribute[] attrs =
                field.GetCustomAttributes<VerifySeverityAttribute>().ToArray();
            Assert.Single(attrs);
        }
    }

    // Tests INV-012 [unit] (TOTAL cross-product, RS-010): for EVERY committed reason, the map's
    // Classify result equals the reason's committed annotation. RED: the stub Classify returns
    // Rejected for all, so the two transient-fault members (annotated Unavailable) mismatch.
    [Fact]
    public void Classify_agrees_with_the_committed_annotation_for_every_reason()
    {
        foreach (DeterminismVerifyReason reason in AllReasons())
        {
            VerifySeverity expected = DeclaredSeverity(reason);
            VerifySeverity actual = DeterminismVerifyReasonMap.Classify(reason);
            Assert.Equal(expected, actual);
        }
    }

    // Tests INV-012 [unit] ("unavailable is a CLOSED 2-member set"): the committed enum annotates
    // EXACTLY the two transient faults as Unavailable — a genuine guard binding the committed enum
    // to the spec's closed set (verifier-unavailable + trust-root-or-tool-unreadable ONLY).
    [Fact]
    public void The_committed_unavailable_annotation_set_is_exactly_the_two_transient_faults()
    {
        var annotatedUnavailable = AllReasons()
            .Where(r => DeclaredSeverity(r) == VerifySeverity.Unavailable)
            .ToHashSet();

        var expected = new HashSet<DeterminismVerifyReason>
        {
            DeterminismVerifyReason.VerifierUnavailable,
            DeterminismVerifyReason.TrustRootOrToolUnreadable,
        };

        Assert.Equal(expected, annotatedUnavailable);
    }

    // Tests INV-012 [unit] ("unavailable set derived from the map == the two transient faults"):
    // the set of reasons the MAP resolves to Unavailable equals exactly the two transient faults.
    // RED: the stub Classify resolves NOTHING to Unavailable, so the map's unavailable set is empty
    // and this fails as an assertion (the fail-open seam that armed RS-001 stays closed only when
    // GREEN wires the real map).
    [Fact]
    public void Only_the_two_transient_faults_map_to_unavailable()
    {
        var mappedUnavailable = AllReasons()
            .Where(r => DeterminismVerifyReasonMap.Classify(r) == VerifySeverity.Unavailable)
            .ToHashSet();

        var expected = new HashSet<DeterminismVerifyReason>
        {
            DeterminismVerifyReason.VerifierUnavailable,
            DeterminismVerifyReason.TrustRootOrToolUnreadable,
        };

        Assert.Equal(expected, mappedUnavailable);
    }

    // Tests INV-012 [integration] (QA-002): DeterminismVerifier.Verify DERIVES its non-verified
    // Outcome (Rejected vs Unavailable) from the committed severity map — the map is the single
    // production source of truth, not parallel dead code. An induced Rejected-severity reason (an
    // absent bundle -> EvidenceAbsent) yields Outcome.Rejected, matching Classify. A future edit that
    // bypassed the map (hand-deciding the outcome at the call site) would break this binding.
    [Fact]
    public void Verify_outcome_severity_is_derived_from_the_committed_map()
    {
        string dir = Path.Combine(Path.GetTempPath(), "qa002-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var req = new DeterminismVerifyRequest
            {
                CosignBinPath = "/nonexistent/pinned/cosign",
                BundlePath = Path.Combine(dir, "absent-bundle.json"),   // absent -> EvidenceAbsent (Rejected)
                ReceiptPath = Path.Combine(dir, "absent-receipt.json"),
                TrustRootPath = Path.Combine(dir, "absent-root.json"),
                WorkingDirectory = dir,
                ExpectedRid = "linux-x64",
                Identity = DeterminismVerifyIdentity.Fixture,
                Timeout = TimeSpan.FromSeconds(5),
            };

            DeterminismVerifyResult r = DeterminismVerifier.Verify(req);

            Assert.NotNull(r.Reason);
            VerifySeverity mapped = DeterminismVerifyReasonMap.Classify(r.Reason!.Value);
            DeterminismVerifyOutcome expected = mapped == VerifySeverity.Unavailable
                ? DeterminismVerifyOutcome.Unavailable
                : DeterminismVerifyOutcome.Rejected;
            Assert.Equal(expected, r.Outcome);
            // The induced reason is EvidenceAbsent (Rejected-severity) -> Outcome.Rejected.
            Assert.Equal(DeterminismVerifyReason.EvidenceAbsent, r.Reason);
            Assert.Equal(DeterminismVerifyOutcome.Rejected, r.Outcome);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    // Tests INV-012 [unit] (fail-closed DEFAULT): an out-of-range enum value (a future reason with
    // no explicit branch) maps to Rejected — the pinned default is rejected, NEVER unavailable, so
    // an unenumerated cell cannot fail open. Genuine fail-closed guard (holds on the deny stub).
    [Fact]
    public void The_default_branch_for_an_unmapped_value_is_rejected()
    {
        var outOfRange = (DeterminismVerifyReason)9999;
        Assert.Equal(VerifySeverity.Rejected, DeterminismVerifyReasonMap.Classify(outOfRange));
    }

    // Tests INV-012 [unit] ("unclassified-verifier-fault is the pinned DEFAULT branch"): the
    // unclassified fault classifies as rejected (fail-closed) — a cosign crash/timeout/unknown
    // exit never resolves to unavailable.
    [Fact]
    public void Unclassified_verifier_fault_maps_to_rejected()
    {
        Assert.Equal(
            VerifySeverity.Rejected,
            DeterminismVerifyReasonMap.Classify(DeterminismVerifyReason.UnclassifiedVerifierFault));
    }

    // Tests INV-012 [unit] (spec-named unavailable members present): the two transient faults the
    // spec enumerates exist on the committed enum, so the map has the members to classify. Genuine
    // structural guard (would catch a renamed/removed transient reason).
    [Fact]
    public void The_two_transient_fault_reasons_exist_on_the_committed_enum()
    {
        string[] names = Enum.GetNames<DeterminismVerifyReason>();
        Assert.Contains(nameof(DeterminismVerifyReason.VerifierUnavailable), names);
        Assert.Contains(nameof(DeterminismVerifyReason.TrustRootOrToolUnreadable), names);
    }

    // ---- carrier ProbeReasons token boundary (INV-012 / RS-010) ----
    //
    // SKIP (audit finding 6): NO per-token DISTINCTNESS assertion. Carrier-token collapse (several
    // internal reasons mapping to one carrier token) is acceptable — the carrier ProbeResult reason
    // accepts any nonempty string, and per-reason rendering fidelity is a separate StatusRenderer
    // track (INV-021), not this boundary. The contract here is totality + closed-set membership only.

    // Tests INV-012 [unit] (internal->carrier-token map is TOTAL, no free string): every committed
    // reason maps to a NON-EMPTY carrier token drawn from the committed closed set
    // CarrierProbeReasonTokens (never a raw stderr / free string). RED: the stub ToCarrierProbeReason
    // returns "" and the committed set is empty, so both the non-empty AND the membership assertions
    // fail for every reason.
    [Fact]
    public void Every_reason_maps_to_a_committed_nonempty_carrier_token()
    {
        IReadOnlyCollection<string> closedSet = DeterminismVerifier.CarrierProbeReasonTokens;
        foreach (DeterminismVerifyReason reason in AllReasons())
        {
            string token = DeterminismVerifier.ToCarrierProbeReason(reason);
            Assert.False(
                string.IsNullOrEmpty(token),
                $"INV-012: reason '{reason}' mapped to an empty carrier token (a typed value must " +
                "always carry a non-empty reason; ProbeResult.TryCreate rejects an empty reason).");
            Assert.Contains(token, closedSet);
        }
    }

    // ---- satisfied:true ONLY when verified (INV-012 / item #1.4) ----

    // Tests INV-012 [unit]: the carrier ProbeResult is Satisfied ONLY when the outcome is Verified.
    // RED: the stub ToProbeResult never returns Satisfied==true, so the Verified case fails.
    [Fact]
    public void ToProbeResult_is_satisfied_only_when_verified()
    {
        ProbeResult verified =
            DeterminismVerifier.ToProbeResult(new DeterminismVerifyResult(DeterminismVerifyOutcome.Verified, null));
        Assert.True(verified.Satisfied);
    }

    // Tests INV-012 [unit] (fail-closed direction): a rejected result is NEVER satisfied. Genuine
    // guard (holds on the deny stub) — the accept-side must be unreachable for a rejected outcome.
    [Fact]
    public void ToProbeResult_rejected_is_never_satisfied()
    {
        ProbeResult rejected = DeterminismVerifier.ToProbeResult(
            new DeterminismVerifyResult(DeterminismVerifyOutcome.Rejected, DeterminismVerifyReason.SignatureInvalid));
        Assert.False(rejected.Satisfied);
    }

    // Tests INV-012 [unit] (fail-closed direction): an unavailable result is NEVER satisfied.
    [Fact]
    public void ToProbeResult_unavailable_is_never_satisfied()
    {
        ProbeResult unavailable = DeterminismVerifier.ToProbeResult(
            new DeterminismVerifyResult(DeterminismVerifyOutcome.Unavailable, DeterminismVerifyReason.VerifierUnavailable));
        Assert.False(unavailable.Satisfied);
    }

    // Tests INV-012 [unit] (result contract): the DeterminismVerifyResult.Satisfied derivation is
    // true ONLY for the Verified outcome — a structural guard on the typed result itself.
    [Fact]
    public void VerifyResult_satisfied_derivation_tracks_the_verified_outcome_only()
    {
        Assert.True(new DeterminismVerifyResult(DeterminismVerifyOutcome.Verified, null).Satisfied);
        Assert.False(
            new DeterminismVerifyResult(DeterminismVerifyOutcome.Rejected, DeterminismVerifyReason.NonPassOutcome).Satisfied);
        Assert.False(
            new DeterminismVerifyResult(DeterminismVerifyOutcome.Unavailable, DeterminismVerifyReason.VerifierUnavailable).Satisfied);
    }
}
