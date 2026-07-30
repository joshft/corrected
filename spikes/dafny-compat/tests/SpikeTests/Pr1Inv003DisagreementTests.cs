// PR1 (Group A) RED tests — INV-003 slice ONLY.
//
// INV-003: when comparison_status=different the live serial lane exits NON-ZERO,
// the signing outcome is not_attempted, and the disagreement is reported as the
// EXACT message "the declared deterministic projections differed in this
// observation under the recorded environment" — strong evidence, NOT a proof of
// cause, NOT a universal-determinism claim; never retried into green. The pure
// classifier proves different => no mint + non-zero disposition (the spec's own
// leading enforcement; there is no production-accessible force-pass/flap switch,
// so a genuine-disagreement integration path is only CI-observed, never
// from-clean forceable — see the note below, NOT a skipped phantom test).
//
// RED: DeterminismDisposition.Dispose throws NotImplementedException (STUB:TDD).
using Corrected.Spike.Contracts;
using Xunit;

namespace Corrected.Spike.Tests;

public class Pr1Inv003DisagreementTests
{
    private static readonly ReceiptStatus Different = new(ExecutionStatus.Completed, ComparisonStatus.Different);
    private static readonly ReceiptStatus Equal = new(ExecutionStatus.Completed, ComparisonStatus.Equal);
    private static readonly ReceiptStatus Skipped = new(ExecutionStatus.ResourceFloorSkipped, ComparisonStatus.NotEvaluated);
    private static readonly ReceiptStatus Infra = new(ExecutionStatus.InfrastructureInvalid, ComparisonStatus.NotEvaluated);

    // Tests INV-003 [unit]: a `different` observation HARD-FAILS the lane —
    // non-zero exit, signing not_attempted, NOT mint-eligible (mints nothing).
    [Fact]
    public void Different_HardFails_ExitsNonZero_SignsNothing()
    {
        var d = DeterminismDisposition.Dispose(Different);
        Assert.NotEqual(0, d.ExitCode);
        Assert.Equal(SigningOutcome.NotAttempted, d.Signing);
        Assert.False(d.MintEligible);
    }

    // Tests INV-003 [unit]: the disagreement message is the EXACT observation-scoped
    // wording — not "proven nondeterminism", not a universal-determinism claim.
    [Fact]
    public void Different_Message_IsExactObservationScoped_NotOverclaimed()
    {
        const string expected = "the declared deterministic projections differed in this observation under the recorded environment";
        // AP-014 value-specificity (green guard): the production constant carries the exact wording.
        Assert.Equal(expected, DeterminismDisposition.DifferentMessage);

        // Behavioral (RED via stub): the disposition reports exactly that message.
        var message = DeterminismDisposition.Dispose(Different).Message;
        Assert.Equal(expected, message);
        Assert.DoesNotContain("proven nondeterminism", message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("universal", message, StringComparison.OrdinalIgnoreCase);
    }

    // Tests INV-003 [unit]: a completed∧equal receipt is the ONLY mint-eligible
    // disposition (exit 0); different / resource_floor_skipped / infrastructure_invalid
    // are all non-attesting (not_attempted, not mint-eligible). Proves different =>
    // no mint.
    [Fact]
    public void Equal_IsTheOnlyMintEligibleDisposition()
    {
        var eq = DeterminismDisposition.Dispose(Equal);
        Assert.True(eq.MintEligible);
        Assert.Equal(0, eq.ExitCode);

        foreach (var nonAttesting in new[] { Different, Skipped, Infra })
        {
            var d = DeterminismDisposition.Dispose(nonAttesting);
            Assert.False(d.MintEligible);
            Assert.Equal(SigningOutcome.NotAttempted, d.Signing);
        }
    }

    // INV-003 [integration] — NOT a from-clean test. A genuine projection
    // disagreement routed through the real lane (non-zero exit + not_attempted, no
    // retry) is only observable when a real flap occurs on the CI serial lane;
    // there is deliberately NO production-accessible force-pass/flap switch
    // (RS-020), so this cannot be forced from a clean checkout. It is lane-covered
    // (CI-observed) and is NOT expressed here as a skipped phantom test (AP-013).
    // The pure-classifier tests above are the from-clean enforcement the spec
    // leads with.
}
