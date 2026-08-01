using System;
using Corrected.Provenance.Entry;
using Corrected.Provenance.InToto;

namespace Corrected.Gate;

// P3 phase-entry (Group G / INV-030 / DD-012 + TB-007) — the FROZEN entry-verifier IDENTITY
// (single source of truth, mirroring DeterminismVerifyIdentity for determinism). The entry
// verify argv pins the EXACT cert-identity / issuer / predicate-type / subject-name. This type
// carries the two committed identities a test pins:
//   * Production — the real entry-signing workflow identity a PR2/P2 entry bundle verifies under.
//                  Like determinism's, the production ACCEPT branch is a recorded residual
//                  (RS-006/RS-011 — the entry production-accept path is unexercisable until P2);
//                  only the 2a identity-mismatch NEGATIVE drives this identity here (MA-C).
//   * Fixture    — the throwaway entry-signing workflow the committed layer-2 entry bundles were
//                  signed under (test/attestations/fixtures/entry/**). The genuine positive + the
//                  2b SHA-cross-check negative drive this one — MINTED in MA-C part (c).
// DISTINCT from the determinism identity (a DIFFERENT workflow file AND predicate type): the entry
// receipt keeps its identity pinned INDEPENDENTLY (RS-024 cross-rejection). These are COMMITTED
// constants (like CosignPin's frozen literals); GREEN reads them when it builds the frozen argv.

/// <summary>
/// One frozen entry-verifier identity (INV-030 / DD-002-analog): the exact
/// <c>--certificate-identity</c> SAN, the <c>--certificate-oidc-issuer</c>, the <c>--type</c>
/// predicate-type URI, and the in-toto subject name the frozen verify argv pins. Immutable value;
/// the two committed instances are <see cref="Production"/> and <see cref="Fixture"/>. The
/// <c>--certificate-github-workflow-sha</c> value is NOT part of the identity — it is supplied
/// per-request (the INV-011-analog cross-check: it must equal the entry receipt's commit-X).
/// </summary>
public sealed record EntryVerifyIdentity(
    string CertificateIdentity,
    string OidcIssuer,
    string PredicateType,
    string SubjectName)
{
    /// <summary>The single OIDC issuer both identities share (GitHub Actions OIDC).</summary>
    public const string GitHubActionsOidcIssuer = "https://token.actions.githubusercontent.com";

    /// <summary>
    /// The versioned Corrected PHASE-ENTRY predicate-type URI — DISTINCT from the determinism URI
    /// (RS-024). Single-sourced from <see cref="EntryAttestation.PredicateTypeUri"/> so the gate
    /// side and the producer side can never drift.
    /// </summary>
    public const string EntryPredicateTypeUri = EntryAttestation.PredicateTypeUri;

    /// <summary>
    /// The canonical in-toto PRIMARY subject name (the entry commit X) — single-sourced from
    /// <see cref="EntryAttestation.CommitSubjectName"/>. This is the subject the cosign
    /// <c>--check-claims</c> blob (the commit-X representation) binds to (subjects[0]).
    /// </summary>
    public const string CommitSubjectName = EntryAttestation.CommitSubjectName;

    /// <summary>
    /// The PRODUCTION entry cert-identity SAN — the real entry-signing workflow at the immutable
    /// <c>refs/heads/main</c> ref. DISTINCT from the determinism production workflow
    /// (<c>p3-determinism-sign.yml</c>). The production accept branch is unexercisable until P2
    /// (RS-011); only the 2a identity-mismatch negative drives this identity here.
    /// </summary>
    public const string ProductionCertificateIdentity =
        "https://github.com/joshft/corrected/.github/workflows/p3-entry-sign.yml@refs/heads/main";

    /// <summary>
    /// The FIXTURE entry cert-identity SAN — the throwaway entry-signing workflow the committed
    /// layer-2 entry bundles were signed under (branch <c>fixture/p3-entry-bundle</c>). Never
    /// acceptable under the production argv (a different workflow file AND ref). MINTED in MA-C
    /// part (c); until then no committed fixture bundle carries it.
    /// </summary>
    public const string FixtureCertificateIdentity =
        "https://github.com/joshft/corrected/.github/workflows/p3-entry-fixture-sign.yml@refs/heads/fixture/p3-entry-bundle";

    /// <summary>The committed PRODUCTION entry identity (default for a verify request).</summary>
    public static EntryVerifyIdentity Production { get; } = new(
        ProductionCertificateIdentity,
        GitHubActionsOidcIssuer,
        EntryPredicateTypeUri,
        CommitSubjectName);

    /// <summary>The committed FIXTURE entry identity (the layer-2 real-cosign tests, part (d)).</summary>
    public static EntryVerifyIdentity Fixture { get; } = new(
        FixtureCertificateIdentity,
        GitHubActionsOidcIssuer,
        EntryPredicateTypeUri,
        CommitSubjectName);
}
