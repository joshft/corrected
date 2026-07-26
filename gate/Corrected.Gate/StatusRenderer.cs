using System;
using Corrected.Gate.Kernel;

namespace Corrected.Gate;

/// <summary>
/// The gate-side INV-012 self-explainer (parent INV-043, DD-004). Distinguishes
/// VALENCE: a consistent BLOCKED is a PASS vs a violation is a FAIL naming the
/// offending precondition. Renders each INV-006 reason-taxonomy category distinctly.
/// Emitted to stdout of the gate command after dotnet test (green-path visibility,
/// RS-290). Emitted paths match ^[\w./-]+$ and carry no Environment.UserName (PRH-005).
/// </summary>
public static class StatusRenderer
{
    /// <summary>
    /// Repo-relative path allowlist regex (INV-012 / RS-T-15): word/./-/ chars, but
    /// NO leading `/` or drive (the first char must not be `/`), so an absolute path
    /// is rejected.
    /// </summary>
    public const string PathAllowlistRegex = @"^[\w.-][\w./-]*$";

    /// <summary>The PASS-BLOCKED banner text (INV-012).</summary>
    public static string RenderPassBlockedBanner(ReadinessVerdict verdict)
        => "PASS: readiness gate consistent; BLOCKED is the expected Phase-0.1 state "
         + "(P2/P3 not yet dischargeable).";

    /// <summary>The FAIL-violation banner naming the offending precondition (INV-012).</summary>
    public static string RenderFailBanner(ReadinessVerdict verdict)
    {
        string offending = verdict.OffendingPrecondition?.ToString() ?? "(unspecified)";
        return $"FAIL: readiness gate violation — offending precondition: {offending}.";
    }

    /// <summary>Render one INV-006 reason-taxonomy category distinctly (INV-012 / RS-291).</summary>
    public static string RenderReason(string taxonomyReason)
        => taxonomyReason switch
        {
            ProbeReasons.ValidatorDeferred =>
                "validator-deferred: expected while BLOCKED; not yet dischargeable "
                + "(see the INV-009/010 discharge pointer: DF-003 remediation lane + the DD-002 manifest schema).",
            ProbeReasons.EvidenceAbsent =>
                "evidence-absent: degraded environment — NOT a code regression; restore the committed evidence and re-run.",
            ProbeReasons.EvidenceMalformed =>
                "evidence-malformed: degraded environment — NOT a code regression; restore the committed evidence and re-run.",
            ProbeReasons.EvidenceSchemaIncomplete =>
                "evidence-schema-incomplete: pre-migration — NOT a code regression; apply the DD-003 migration and re-run.",
            ProbeReasons.EvidenceRefutes =>
                "evidence-refutes: real regression — the evidence contradicts the claim.",
            _ => $"{taxonomyReason}: unclassified reason.",
        };

    /// <summary>The INV-011 "no production surface (src/ empty)" vacuous-pass notice (INV-012).</summary>
    public static string RenderNoProductionSurfaceNotice()
        => "no production surface (src/ empty): the shipped closure resolves to zero project files "
         + "while BLOCKED; the production-code ban is vacuously satisfied. "
         + "(readiness explanation lives in the gate output; `corrected explain` is deferred until BLOCKED clears — DD-004.)";
}
