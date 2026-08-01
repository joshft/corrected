using Corrected.Gate;
using Xunit;

namespace Corrected.Gate.Tests;

/// <summary>
/// INV-021 (a)/(b)/(c) — the StatusRenderer renders the typed P3 reasons with a DISTINCT, actionable
/// case per reason and NO `unclassified` fallthrough (MA-F). The token set is derived from the
/// committed <see cref="DeterminismVerifier.CarrierProbeReasonTokens"/> (RS-010), so a reason added to
/// the verifier's map without a renderer case fails here. The pre-activation zero-state
/// `p3-not-yet-activated` renders DISTINCTLY as an expected state — never the degraded-environment
/// `evidence-absent` "restore the committed evidence and re-run" text (RS-035). Each reason carries a
/// {retryable | hard} disposition (RS-034). RED: RenderReason switches only the 5 legacy carrier
/// reasons, so every P3 token falls to the `unclassified` default arm.
/// </summary>
public class Inv021RendererTotalityTests
{
    // (a) totality: EVERY committed carrier token renders distinctly, none to the `unclassified` arm.
    [Fact]
    public void Every_p3_carrier_reason_renders_with_no_unclassified_fallthrough()
    {
        foreach (string token in DeterminismVerifier.CarrierProbeReasonTokens)
        {
            string rendered = StatusRenderer.RenderReason(token);
            Assert.False(string.IsNullOrWhiteSpace(rendered));
            // The forbidden default arm is exactly "{token}: unclassified reason." — no token may hit it.
            Assert.NotEqual($"{token}: unclassified reason.", rendered);
        }
    }

    // (b) the pre-activation zero-state is DISTINCT and never the degraded-env "restore ... re-run" text.
    [Fact]
    public void P3_not_yet_activated_zero_state_is_distinct_not_evidence_absent()
    {
        string zero = StatusRenderer.RenderReason("p3-not-yet-activated");
        Assert.Contains("p3-not-yet-activated", zero);
        Assert.DoesNotContain("restore the committed evidence", zero);
        Assert.Contains("expected", zero); // rendered as an EXPECTED pre-activation state
    }

    // (c) disposition: a transient verifier fault is retryable; a crypto reject is hard.
    [Fact]
    public void Reasons_carry_retryable_or_hard_disposition()
    {
        Assert.Contains("[retryable]", StatusRenderer.RenderReason("verifier-unavailable"));
        Assert.Contains("[retryable]", StatusRenderer.RenderReason("trust-root-or-tool-unreadable"));
        Assert.Contains("[hard]", StatusRenderer.RenderReason("signature-invalid"));
        Assert.Contains("[hard]", StatusRenderer.RenderReason("statement-reconstruction-mismatch"));
        Assert.Contains("[hard]", StatusRenderer.RenderReason("cert-workflow-sha-mismatch"));
    }
}
