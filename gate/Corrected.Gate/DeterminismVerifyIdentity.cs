using System;

namespace Corrected.Gate;

// P3 determinism-attestation spec INV-010/011/013 + DD-002 (section C — the FROZEN identity
// constants, single source of truth). The verify argv pins the EXACT cert-identity / issuer /
// predicate-type / subject-name; this type carries the two committed identities a test pins:
//   * Production  — the real workflow identity the PR3 evidence bundle verifies under. The
//                   production ACCEPT branch is a recorded PR3 residual (INV-013 / RS-006/RS-011)
//                   — NEVER asserted here; only the 2a identity-mismatch NEGATIVE drives it.
//   * Fixture     — the throwaway workflow identity the committed layer-2 bundles were signed
//                   under (test/attestations/fixtures/**). The genuine positive + the 2b
//                   SHA-cross-check negative drive this one.
// These are COMMITTED constants (like CosignPin's frozen literals), not a stub body — GREEN reads
// them when it builds the frozen argv; the section C test pins each field as the single source of
// truth and cross-checks the shared PredicateType / SubjectName against the Corrected.Provenance
// contract constants so the gate side and the producer side can never drift.

/// <summary>
/// One frozen verifier identity (INV-010/011 / DD-002): the exact
/// <c>--certificate-identity</c> SAN, the <c>--certificate-oidc-issuer</c>, the
/// <c>--type</c> predicate-type URI, and the in-toto subject name the frozen verify argv pins.
/// Immutable value; the two committed instances are <see cref="Production"/> and
/// <see cref="Fixture"/>. The <c>--certificate-github-workflow-sha</c> value is NOT part of the
/// identity — it is supplied per-request (INV-011 cross-check: it must equal the receipt's
/// <c>attested_commit</c>).
/// </summary>
public sealed record DeterminismVerifyIdentity(
    string CertificateIdentity,
    string OidcIssuer,
    string PredicateType,
    string SubjectName)
{
    /// <summary>The single OIDC issuer both identities share (GitHub Actions OIDC).</summary>
    public const string GitHubActionsOidcIssuer = "https://token.actions.githubusercontent.com";

    /// <summary>The versioned Corrected determinism predicate-type URI (shared, DD-002).</summary>
    public const string DeterminismPredicateTypeUri =
        "https://correctless.org/attestations/determinism/v1";

    /// <summary>The canonical in-toto subject name (shared, DD-002).</summary>
    public const string CanonicalSubjectName = "determinism-run-receipt";

    /// <summary>
    /// The PRODUCTION cert-identity SAN — the real signing workflow at the immutable
    /// <c>refs/heads/main</c> ref. The production accept branch is unexercisable until PR3
    /// (INV-013): only the 2a identity-mismatch negative drives this identity here.
    /// </summary>
    public const string ProductionCertificateIdentity =
        "https://github.com/joshft/corrected/.github/workflows/p3-determinism-sign.yml@refs/heads/main";

    /// <summary>
    /// The FIXTURE cert-identity SAN — the throwaway signing workflow the committed layer-2
    /// bundles were signed under (branch <c>fixture/p3-determinism-bundle</c>). Never acceptable
    /// under the production argv (a different workflow file AND ref).
    /// </summary>
    public const string FixtureCertificateIdentity =
        "https://github.com/joshft/corrected/.github/workflows/p3-fixture-sign.yml@refs/heads/fixture/p3-determinism-bundle";

    /// <summary>
    /// The FIXTURE cert workflow-SHA (the commit the fixture-signing run executed at). Equals the
    /// POS receipt's <c>attested_commit</c> (so POS verifies AND cross-checks positive) but NOT
    /// SHANEG's (<c>0000…</c>) — that is the 2b cross-check negative. Used as the fixture-ACCEPTING
    /// <c>--certificate-github-workflow-sha</c> value so cosign accepts SHANEG's genuine crypto,
    /// after which Corrected's INV-011 cross-check fails on the receipt binding.
    /// </summary>
    public const string FixtureCertWorkflowSha = "14701a99367f76b3e46b7261afc1f5c3dd490244";

    /// <summary>The committed PRODUCTION identity (default for a verify request).</summary>
    public static DeterminismVerifyIdentity Production { get; } = new(
        ProductionCertificateIdentity,
        GitHubActionsOidcIssuer,
        DeterminismPredicateTypeUri,
        CanonicalSubjectName);

    /// <summary>The committed FIXTURE identity (the layer-2 real-cosign tests).</summary>
    public static DeterminismVerifyIdentity Fixture { get; } = new(
        FixtureCertificateIdentity,
        GitHubActionsOidcIssuer,
        DeterminismPredicateTypeUri,
        CanonicalSubjectName);
}
