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

    /// <summary>
    /// Render one INV-006/INV-021 reason-taxonomy category distinctly (INV-012 / RS-291). Covers the
    /// legacy carrier reasons AND every typed P3 determinism-verify reason
    /// (<see cref="DeterminismVerifier.CarrierProbeReasonTokens"/>) with a distinct, actionable case and
    /// a {retryable | hard} disposition — no P3 token falls to the `unclassified` default (INV-021 a/c,
    /// RS-034). The pre-activation zero-state <c>p3-not-yet-activated</c> renders as an EXPECTED state
    /// pointing at the PR3 activation flow, NEVER the degraded-environment "restore the committed
    /// evidence and re-run" text (INV-021 b, RS-035).
    /// </summary>
    public static string RenderReason(string taxonomyReason)
        => taxonomyReason switch
        {
            // ---- legacy carrier reasons (INV-006) ----
            ProbeReasons.ValidatorDeferred =>
                "validator-deferred: expected while BLOCKED; not yet dischargeable "
                + "(see the INV-009/010 discharge pointer: DF-003 remediation lane + the DD-002 manifest schema).",
            // `evidence-absent` is shared: P1/P2 degraded-env AND the P3 pointer-names-a-missing-bundle
            // reason both emit this token. The degraded-env "restore the committed evidence" text fits both.
            ProbeReasons.EvidenceAbsent =>
                "evidence-absent: degraded environment — NOT a code regression; restore the committed evidence and re-run.",
            ProbeReasons.EvidenceMalformed =>
                "evidence-malformed: degraded environment — NOT a code regression; restore the committed evidence and re-run.",
            ProbeReasons.EvidenceSchemaIncomplete =>
                "evidence-schema-incomplete: pre-migration — NOT a code regression; apply the DD-003 migration and re-run.",
            ProbeReasons.EvidenceRefutes =>
                "evidence-refutes: real regression — the evidence contradicts the claim.",

            // ---- the P3 pre-activation zero-state (INV-021 b, RS-035): an EXPECTED state, not a fault ----
            "p3-not-yet-activated" =>
                "p3-not-yet-activated [expected]: no determinism attestation is committed yet — the expected "
                + "pre-activation zero-state, NOT a failure. The PR3 activation flow commits the pointer + bundle; "
                + "do NOT restore any file (none is expected to exist yet).",

            // ---- P3 typed reasons — transient faults (retryable) ----
            "verifier-unavailable" =>
                "verifier-unavailable [retryable]: the cosign verify tool could not run (a transient tool/launch "
                + "fault). Re-provision cosign and re-run — a re-run never mints.",
            "trust-root-or-tool-unreadable" =>
                "trust-root-or-tool-unreadable [retryable]: the pinned trust root or cosign binary is present but "
                + "unreadable (an I/O fault). Fix its permissions and re-run.",

            // ---- P3 typed reasons — hard rejects (fail-closed) ----
            "malformed-receipt" =>
                "malformed-receipt [hard]: the committed determinism receipt is not a valid regular file, or is "
                + "unparseable; fix the committed receipt.",
            "malformed-bundle" =>
                "malformed-bundle [hard]: the committed cosign bundle is not a valid regular file, or is unparseable "
                + "JSON; fix the committed bundle.",
            "signature-invalid" =>
                "signature-invalid [hard]: cosign rejected the signature / log inclusion — the bundle is not "
                + "authentically signed. Push a new reviewed commit; NEVER re-run to mint.",
            "identity-mismatch" =>
                "identity-mismatch [hard]: the signing certificate identity is not the pinned production identity — "
                + "the attestation was not produced by the trusted workflow.",
            "predicate-type-mismatch" =>
                "predicate-type-mismatch [hard]: the attestation predicate type is not the pinned determinism type.",
            "subject-digest-mismatch" =>
                "subject-digest-mismatch [hard]: the signed subject digest does not match the committed receipt bytes.",
            "statement-reconstruction-mismatch" =>
                "statement-reconstruction-mismatch [hard]: the signed Statement does not byte-equal the reconstruction "
                + "from the committed receipt (INV-010) — the predicate content was altered.",
            "projection-policy-mismatch" =>
                "projection-policy-mismatch [hard]: the receipt's projection policy does not match the committed policy.",
            "stale-subject-manifest" =>
                "stale-subject-manifest [hard]: the signed subject-manifest digest no longer matches HEAD's manifest — "
                + "the attestation is stale for this commit.",
            "attested-commit-not-ancestor" =>
                "attested-commit-not-ancestor [hard]: the receipt's attested_commit is not an ancestor of HEAD — the "
                + "attestation belongs to a different history.",
            "cert-workflow-sha-mismatch" =>
                "cert-workflow-sha-mismatch [hard]: the certificate workflow-SHA does not equal the receipt's "
                + "attested_commit (INV-011).",
            "ancestry-uncomputable" =>
                "ancestry-uncomputable [hard]: the attested_commit ancestry could not be computed (e.g. a shallow "
                + "clone). Fetch the full history and re-run.",
            "rid-platform-mismatch" =>
                "rid-platform-mismatch [hard]: the receipt's runtime identifier does not match the expected platform.",
            "non-pass-outcome" =>
                "non-pass-outcome [hard]: the receipt does not record a passing determinism comparison.",
            "trust-root-or-pin-mismatch" =>
                "trust-root-or-pin-mismatch [hard]: cosign could not load the pinned trust root, or it does not match "
                + "the committed pin.",
            "unclassified-verifier-fault" =>
                "unclassified-verifier-fault [hard]: the verifier failed for a reason not positively classified — "
                + "treated as a hard failure (fail-closed).",

            _ => $"{taxonomyReason}: unclassified reason.",
        };

    /// <summary>The INV-011 "no production surface (src/ empty)" vacuous-pass notice (INV-012).</summary>
    public static string RenderNoProductionSurfaceNotice()
        => "no production surface (src/ empty): the shipped closure resolves to zero project files "
         + "while BLOCKED; the production-code ban is vacuously satisfied. "
         + "(readiness explanation lives in the gate output; `corrected explain` is deferred until BLOCKED clears — DD-004.)";
}
