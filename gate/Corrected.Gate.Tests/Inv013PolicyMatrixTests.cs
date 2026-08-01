using System;
using System.IO;
using Corrected.Gate;
using Corrected.Provenance.Determinism;
using Xunit;

namespace Corrected.Gate.Tests;

/// <summary>
/// P3 determinism-attestation spec INV-013 LAYER 1 (~507-508): a PURE policy matrix over
/// ALREADY-AUTHENTICATED typed receipts — NO cosign. The classifier accepts
/// (equal ∧ completed ∧ rid==linux-x64 ∧ non-stale-manifest ∧ attested-commit ancestor-of-HEAD)
/// and rejects each SINGLE violation with its SPECIFIC reason. A meta-assertion proves a layer-1
/// row NEVER invokes cosign (a structural source scan — the classifier references no subprocess
/// seam).
///
/// The authenticated receipt view is built from the committed REAL PR1 producer receipt fixture
/// (<c>determinism-receipt.sample.json</c> — completed / equal / rid=linux-x64), parsed through
/// the real <see cref="RunReceipt.FromJson"/>, so the accept case is driven off genuine producer
/// bytes, not hand-written literals (AP-014).
/// </summary>
public class Inv013PolicyMatrixTests
{
    private const string ExpectedRid = "linux-x64";

    // The committed REAL determinism RunReceipt fixture (the same one P3SignerHarness signs).
    private static RunReceipt SampleReceipt()
    {
        byte[] bytes = File.ReadAllBytes(TestPaths.Fixture("provenance", "determinism-receipt.sample.json"));
        return RunReceipt.FromJson(bytes);
    }

    /// <summary>
    /// Build the fully-VALID authenticated view from the committed receipt (completed / equal /
    /// rid=linux-x64), with a non-stale manifest and an ancestor attested_commit — the ACCEPT case.
    /// </summary>
    private static AuthenticatedReceiptView AcceptView()
    {
        RunReceipt r = SampleReceipt();
        return new AuthenticatedReceiptView
        {
            ExecutionStatus = r.ExecutionStatus,
            ComparisonStatus = r.ComparisonStatus,
            Rid = r.Platform.Rid,
            ManifestStale = false,
            AttestedCommitAncestry = AncestryStatus.Ancestor,
        };
    }

    // Tests INV-013 [unit] (layer-1 accept): the committed receipt's real fields
    // (completed / equal / linux-x64) with a non-stale manifest + ancestor attested_commit are
    // ACCEPTED — Classify returns null. RED: the deny stub returns UnclassifiedVerifierFault.
    [Fact]
    public void Fully_valid_authenticated_receipt_is_accepted()
    {
        AuthenticatedReceiptView view = AcceptView();
        DeterminismVerifyReason? reason = DeterminismPolicyClassifier.Classify(view, ExpectedRid);
        Assert.Null(reason);
    }

    // Tests INV-013 [unit] (fixture sanity): the committed producer receipt genuinely carries the
    // pass fields, so the accept case above is driven off real (completed / equal / linux-x64)
    // bytes — not a hand-built view that could accept for the wrong reason (AP-010/AP-014).
    [Fact]
    public void Committed_receipt_fixture_carries_the_pass_fields()
    {
        RunReceipt r = SampleReceipt();
        Assert.Equal("completed", r.ExecutionStatus);
        Assert.Equal("equal", r.ComparisonStatus);
        Assert.Equal("linux-x64", r.Platform.Rid);
    }

    // Tests INV-013 [unit] (single violation -> non-pass-outcome): a comparison_status != equal is
    // rejected with the SPECIFIC reason non-pass-outcome. RED: the deny stub returns the generic
    // UnclassifiedVerifierFault instead of the specific reason.
    [Fact]
    public void Non_equal_comparison_is_rejected_as_non_pass_outcome()
    {
        AuthenticatedReceiptView view = AcceptView() with { ComparisonStatus = "different" };
        Assert.Equal(DeterminismVerifyReason.NonPassOutcome, DeterminismPolicyClassifier.Classify(view, ExpectedRid));
    }

    // Tests INV-013 [unit] (single violation -> non-pass-outcome): a non-completed execution_status
    // is rejected as non-pass-outcome.
    [Fact]
    public void Non_completed_execution_is_rejected_as_non_pass_outcome()
    {
        AuthenticatedReceiptView view = AcceptView() with { ExecutionStatus = "resource_floor_skipped" };
        Assert.Equal(DeterminismVerifyReason.NonPassOutcome, DeterminismPolicyClassifier.Classify(view, ExpectedRid));
    }

    // Tests INV-013 [unit] (ALLOWLIST, not denylist — the SECOND legal non-pass comparison value):
    // the pass check must be comparison==equal (allowlist), NOT comparison != "different" (denylist).
    // comparison_status "not_evaluated" is the committed status model's OTHER legal non-pass value
    // (spec legal-status table ~119-127: {equal, different, not_evaluated}), so a denylist GREEN that
    // only rejects "different" fails OPEN here. RED: the deny stub returns UnclassifiedVerifierFault.
    [Fact]
    public void Not_evaluated_comparison_is_rejected_as_non_pass_outcome()
    {
        AuthenticatedReceiptView view = AcceptView() with { ComparisonStatus = "not_evaluated" };
        Assert.Equal(DeterminismVerifyReason.NonPassOutcome, DeterminismPolicyClassifier.Classify(view, ExpectedRid));
    }

    // Tests INV-013 [unit] (ALLOWLIST, not denylist — the SECOND legal non-pass execution value): the
    // pass check must be execution==completed (allowlist), NOT execution != "resource_floor_skipped".
    // execution_status "infrastructure_invalid" is the committed status model's OTHER legal non-pass
    // value (spec legal-status table ~119-127: {completed, resource_floor_skipped,
    // infrastructure_invalid}), so a denylist GREEN fails OPEN here. RED: the deny stub returns
    // UnclassifiedVerifierFault.
    [Fact]
    public void Infrastructure_invalid_execution_is_rejected_as_non_pass_outcome()
    {
        AuthenticatedReceiptView view = AcceptView() with { ExecutionStatus = "infrastructure_invalid" };
        Assert.Equal(DeterminismVerifyReason.NonPassOutcome, DeterminismPolicyClassifier.Classify(view, ExpectedRid));
    }

    // Tests INV-013 [unit] (single violation -> rid-platform-mismatch): a receipt RID other than
    // the expected linux-x64 is rejected with the SPECIFIC reason rid-platform-mismatch (NEVER a
    // silent skip, RS-015). RED: the deny stub returns UnclassifiedVerifierFault.
    [Fact]
    public void Wrong_rid_is_rejected_as_rid_platform_mismatch()
    {
        AuthenticatedReceiptView view = AcceptView() with { Rid = "osx-arm64" };
        Assert.Equal(DeterminismVerifyReason.RidPlatformMismatch, DeterminismPolicyClassifier.Classify(view, ExpectedRid));
    }

    // Tests INV-013 [unit] (single violation -> stale-subject-manifest): a stale manifest is
    // rejected with the SPECIFIC reason stale-subject-manifest.
    [Fact]
    public void Stale_manifest_is_rejected_as_stale_subject_manifest()
    {
        AuthenticatedReceiptView view = AcceptView() with { ManifestStale = true };
        Assert.Equal(DeterminismVerifyReason.StaleSubjectManifest, DeterminismPolicyClassifier.Classify(view, ExpectedRid));
    }

    // Tests INV-013 [unit] (single violation -> attested-commit-not-ancestor): an attested_commit
    // that is NOT an ancestor of HEAD is rejected with the SPECIFIC reason.
    [Fact]
    public void Attested_commit_not_ancestor_is_rejected_specifically()
    {
        AuthenticatedReceiptView view = AcceptView() with { AttestedCommitAncestry = AncestryStatus.NotAncestor };
        Assert.Equal(
            DeterminismVerifyReason.AttestedCommitNotAncestor,
            DeterminismPolicyClassifier.Classify(view, ExpectedRid));
    }

    // Tests INV-013 [unit] (single violation -> ancestry-uncomputable, NEVER unavailable, RS-013):
    // an uncomputable ancestry (shallow clone / absent X) is rejected as ancestry-uncomputable, and
    // (cross-check via INV-012) that reason's committed severity is Rejected — never unavailable, so
    // a shallow clone cannot degrade into the non-failing unavailable class that armed RS-001.
    [Fact]
    public void Uncomputable_ancestry_is_rejected_as_ancestry_uncomputable_never_unavailable()
    {
        AuthenticatedReceiptView view = AcceptView() with { AttestedCommitAncestry = AncestryStatus.Uncomputable };
        Assert.Equal(
            DeterminismVerifyReason.AncestryUncomputable,
            DeterminismPolicyClassifier.Classify(view, ExpectedRid));

        // The severity of ancestry-uncomputable is Rejected (fail-closed), not Unavailable.
        Assert.Equal(
            VerifySeverity.Rejected,
            DeterminismVerifyReasonMap.Classify(DeterminismVerifyReason.AncestryUncomputable));
    }

    // ---- meta: a layer-1 row NEVER invokes cosign (INV-013) ----

    // Tests INV-013 [unit] (meta, structural): the layer-1 classifier source INVOKES no cosign
    // subprocess seam — no CosignRunner reference, no Process launch, no COSIGN_BIN env seam, no
    // cosign verb. This structurally enforces "a layer-1 row never invokes cosign" (it is a pure
    // function over a supplied view), rather than merely asserting it in prose. The scan targets
    // INVOCATION constructs, not the bare word "cosign" (which legitimately appears in the file's
    // explanatory comments). Genuine guard (the pure stub already holds it; GREEN must keep it pure).
    [Fact]
    public void Layer1_classifier_source_never_invokes_the_cosign_seam()
    {
        string src = File.ReadAllText(
            TestPaths.RepoFile("gate", "Corrected.Gate", "DeterminismPolicyClassifier.cs"));
        Assert.DoesNotContain("CosignRunner", src);
        Assert.DoesNotContain("Process.Start", src);
        Assert.DoesNotContain("new Process(", src);
        Assert.DoesNotContain("COSIGN_BIN", src);
        Assert.DoesNotContain("verify-blob", src);
        Assert.DoesNotContain("attest-blob", src);
    }
}
