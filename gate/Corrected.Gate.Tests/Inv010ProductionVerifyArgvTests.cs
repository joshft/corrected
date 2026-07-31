using System;
using System.Collections.Generic;
using System.Linq;
using Corrected.Gate;
using Corrected.Provenance.Determinism;
using Corrected.Provenance.InToto;
using Xunit;

namespace Corrected.Gate.Tests;

/// <summary>
/// P3 determinism-attestation spec INV-010/011 + DD-002 + PRH-001 (section C — the FROZEN verify
/// argv builder + the frozen identity constants, single source of truth). Pure/deterministic (no
/// cosign): asserts <see cref="DeterminismVerifier.BuildVerifyArgv"/> emits the EXACT
/// transcript-frozen <c>verify-blob-attestation</c> argv — every flag present, EXACT, drawn from
/// the request's <see cref="DeterminismVerifyIdentity"/> — never a <c>-regexp</c>/insecure variant
/// (PRH-001). RED: the T3a placeholder builds a MINIMAL argv (bundle + root only), so every
/// exactness assertion fails.
/// </summary>
public class Inv010ProductionVerifyArgvTests
{
    private const string KnownSha = "14701a99367f76b3e46b7261afc1f5c3dd490244";

    private static DeterminismVerifyRequest Request(DeterminismVerifyIdentity identity) => new()
    {
        CosignBinPath = "/abs/cosign",
        BundlePath = "/abs/work/determinism.sigstore.json",
        ReceiptPath = "/abs/work/determinism-receipt.json",
        TrustRootPath = "/abs/work/trusted_root.json",
        WorkingDirectory = "/abs/work",
        ExpectedRid = "linux-x64",
        Identity = identity,
        CertWorkflowSha = KnownSha,
    };

    // Tests INV-010/INV-011 [integration] (the EXACT frozen argv): BuildVerifyArgv over a PRODUCTION
    // request emits precisely the transcript-frozen argv, in order: verify-blob-attestation,
    // --check-claims=true, --type <predicateType>, --certificate-identity <id>,
    // --certificate-oidc-issuer <iss>, --certificate-github-workflow-sha <sha>,
    // --use-signed-timestamps, --trusted-root <root>, --bundle <bundle>, <receipt-blob>. RED: the
    // stub returns the minimal 3-flag argv, so the ordered-equality fails.
    [Fact]
    public void Build_verify_argv_is_the_exact_frozen_production_argv()
    {
        DeterminismVerifyRequest req = Request(DeterminismVerifyIdentity.Production);

        IReadOnlyList<string> argv = DeterminismVerifier.BuildVerifyArgv(req);

        var expected = new[]
        {
            "verify-blob-attestation",
            "--check-claims=true",
            "--type", DeterminismVerifyIdentity.Production.PredicateType,
            "--certificate-identity", DeterminismVerifyIdentity.Production.CertificateIdentity,
            "--certificate-oidc-issuer", DeterminismVerifyIdentity.Production.OidcIssuer,
            "--certificate-github-workflow-sha", KnownSha,
            "--use-signed-timestamps",
            "--trusted-root", req.TrustRootPath,
            "--bundle", req.BundlePath,
            req.ReceiptPath,
        };
        Assert.Equal(expected, argv);
    }

    // Tests INV-010/INV-011 [integration] (identity FLOWS from the request, value-specific): the argv
    // built from the FIXTURE identity carries the FIXTURE cert-identity SAN and the fixture predicate
    // type — proving the identity constant is READ into the argv (not hard-coded to production). A
    // build that ignored request.Identity could not switch the SAN. RED: the stub argv has no
    // --certificate-identity at all.
    [Fact]
    public void Build_verify_argv_reads_the_request_identity_into_the_cert_flags()
    {
        IReadOnlyList<string> argv = DeterminismVerifier.BuildVerifyArgv(Request(DeterminismVerifyIdentity.Fixture));

        int idIdx = argv.ToList().IndexOf("--certificate-identity");
        Assert.True(idIdx >= 0 && idIdx + 1 < argv.Count, "argv must carry --certificate-identity <value>");
        Assert.Equal(DeterminismVerifyIdentity.FixtureCertificateIdentity, argv[idIdx + 1]);

        int typeIdx = argv.ToList().IndexOf("--type");
        Assert.True(typeIdx >= 0 && typeIdx + 1 < argv.Count, "argv must carry --type <predicateType>");
        Assert.Equal(DeterminismVerifyIdentity.DeterminismPredicateTypeUri, argv[typeIdx + 1]);
    }

    // Tests PRH-001 [integration] (never insecure / over-broad): the frozen argv contains NONE of the
    // insecure/over-broad flags — --check-claims=false, --insecure-ignore-tlog, --insecure-ignore-sct,
    // --certificate-identity-regexp, --certificate-oidc-issuer-regexp — and the identity/issuer are
    // pinned EXACT (the exact SAN value, not a regexp). RED: the stub argv lacks --check-claims=true,
    // so the positive requirement (claims checking ON) is unmet even though no insecure token is present.
    [Fact]
    public void Frozen_argv_uses_no_insecure_or_regexp_flags_prh001()
    {
        IReadOnlyList<string> argv = DeterminismVerifier.BuildVerifyArgv(Request(DeterminismVerifyIdentity.Production));

        foreach (string forbidden in new[]
                 {
                     "--check-claims=false",
                     "--insecure-ignore-tlog",
                     "--insecure-ignore-sct",
                     "--certificate-identity-regexp",
                     "--certificate-oidc-issuer-regexp",
                 })
        {
            Assert.DoesNotContain(forbidden, argv);
        }

        // Claims-checking is explicitly ON, and identity/issuer are pinned EXACT (the literal SAN).
        Assert.Contains("--check-claims=true", argv);
        Assert.Contains("--certificate-identity", argv);
        Assert.Contains(DeterminismVerifyIdentity.ProductionCertificateIdentity, argv);
    }

    // Tests INV-017 [integration] (OFFLINE verify — no verify-time network / no Rekor content-search):
    // the frozen argv anchors verification OFFLINE via --trusted-root + --use-signed-timestamps and
    // carries NO flag that would require a verify-time network / Rekor content lookup (no --rekor-url,
    // no online-tlog flag). RED: the stub argv has --trusted-root but omits --use-signed-timestamps,
    // so the offline time-anchor requirement is unmet.
    [Fact]
    public void Frozen_argv_is_offline_trusted_root_plus_signed_timestamps_no_network()
    {
        IReadOnlyList<string> argv = DeterminismVerifier.BuildVerifyArgv(Request(DeterminismVerifyIdentity.Production));

        Assert.Contains("--trusted-root", argv);          // the offline trust anchor
        Assert.Contains("--use-signed-timestamps", argv); // the offline time anchor (RED: absent in stub)

        // No verify-time network / Rekor content-search flag.
        Assert.DoesNotContain(argv, a => a.StartsWith("--rekor-url", StringComparison.Ordinal));
        Assert.DoesNotContain("--offline=false", argv);
    }

    // ---- the frozen identity constants (single source of truth) ----

    // Tests INV-013 [unit] (the committed PRODUCTION identity constant is value-specific): the frozen
    // production identity carries EXACTLY the production workflow SAN at refs/heads/main, the GitHub
    // Actions OIDC issuer, the determinism predicate type, and the canonical subject name. Pins the
    // constant so a drift (a typo'd SAN, a wrong ref) is a reviewable diff.
    [Fact]
    public void Production_identity_constant_is_pinned_exact()
    {
        DeterminismVerifyIdentity p = DeterminismVerifyIdentity.Production;
        Assert.Equal(
            "https://github.com/joshft/corrected/.github/workflows/p3-determinism-sign.yml@refs/heads/main",
            p.CertificateIdentity);
        Assert.Equal("https://token.actions.githubusercontent.com", p.OidcIssuer);
        Assert.Equal("https://correctless.org/attestations/determinism/v1", p.PredicateType);
        Assert.Equal("determinism-run-receipt", p.SubjectName);
        // The production SAN names the main ref (INV-011: an exact SAN at refs/heads/main), never a
        // fixture branch — the production and fixture identities are genuinely distinct.
        Assert.Contains("p3-determinism-sign.yml@refs/heads/main", p.CertificateIdentity);
        Assert.NotEqual(DeterminismVerifyIdentity.FixtureCertificateIdentity, p.CertificateIdentity);
    }

    // Tests INV-013 [unit] (the committed FIXTURE identity constant is value-specific): the frozen
    // fixture identity carries EXACTLY the throwaway fixture-signing workflow SAN + branch and the
    // fixture cert workflow-SHA (equal to the POS receipt's attested_commit, README-frozen). Pins the
    // constants the layer-2 fixture-accepting policy depends on.
    [Fact]
    public void Fixture_identity_constant_is_pinned_exact()
    {
        DeterminismVerifyIdentity f = DeterminismVerifyIdentity.Fixture;
        Assert.Equal(
            "https://github.com/joshft/corrected/.github/workflows/p3-fixture-sign.yml@refs/heads/fixture/p3-determinism-bundle",
            f.CertificateIdentity);
        Assert.Equal("https://token.actions.githubusercontent.com", f.OidcIssuer);
        Assert.Equal("14701a99367f76b3e46b7261afc1f5c3dd490244", DeterminismVerifyIdentity.FixtureCertWorkflowSha);
    }

    // Tests INV-006/INV-010 [unit] (single source of truth — gate ↔ producer cannot drift): the
    // shared predicate-type URI + subject name the GATE-side identity constants pin EQUAL the
    // PRODUCER-side contract constants in Corrected.Provenance (DeterminismAttestation). A drift
    // between the signer's Statement and the verifier's expectation would break INV-010's byte
    // equality; this asserts they are one value.
    [Fact]
    public void Gate_identity_predicate_and_subject_agree_with_the_provenance_contract()
    {
        Assert.Equal(DeterminismAttestation.PredicateTypeUri, DeterminismVerifyIdentity.DeterminismPredicateTypeUri);
        Assert.Equal(DeterminismAttestation.SubjectName, DeterminismVerifyIdentity.CanonicalSubjectName);
        Assert.Equal(DeterminismVerifyIdentity.Production.PredicateType, DeterminismVerifyIdentity.Fixture.PredicateType);
        Assert.Equal(DeterminismVerifyIdentity.Production.SubjectName, DeterminismVerifyIdentity.Fixture.SubjectName);
        // The DSSE payload media type the producer wraps under is the pinned in-toto JSON type.
        Assert.Equal("application/vnd.in-toto+json", DsseEnvelope.InTotoJsonPayloadType);
    }
}
