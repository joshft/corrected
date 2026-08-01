using System.Collections.Generic;

namespace Corrected.Gate;

/// <summary>
/// The PINNED production subject-classification policy (INV-018, TB-006). This is the DATA the pure
/// <see cref="SubjectClassifier"/> engine runs on — the closed-world determinism surface for THIS
/// repo. "Narrow roots + anchors" boundary (maintainer-selected 2026-07-31):
///   * Owned roots = the determinism PRODUCER surface (the spike) + the Statement/receipt/predicate
///     substrate (<c>gate/Corrected.Provenance/</c>) whose bytes shape the signed subject.
///   * Anchors = the scattered P3 VERIFY / SIGN / PIN surface (the verifier + cosign seam + trust
///     root + the two workflows) and the classifier/producer THEMSELVES (self-inputs, INV-018).
///   * Exclusions = within-root non-determinism noise (the spike README + .gitignore). The spec's
///     external exclusions (the P2 completion-manifest, the P2 active-reference, the P3 declaration,
///     migration surfaces) are OUTSIDE these narrow roots, so they cannot stale P3 by construction
///     — the "same completeness protection" is achieved by being off-surface, not by an enumerated
///     carve-out.
///
/// A new P3 verify-surface file added under <c>gate/Corrected.Gate/</c> must be added to
/// <see cref="Anchors"/> (the anchor set is exact, not a directory glob). The pinned-policy sanity
/// test guards the known P3 verify files against silent anchor omission.
/// </summary>
public static class SubjectClassificationPolicy
{
    /// <summary>The pinned production policy (INV-018). Revisable; the sanity test documents it.</summary>
    public static SubjectPolicy Pinned { get; } = new(
        OwnedRoots: new[]
        {
            "spikes/dafny-compat/",
            "gate/Corrected.Provenance/",
        },
        Anchors: new[]
        {
            // The two P3 CI workflows (the live determinism lane + the signing workflow).
            ".github/workflows/p3-determinism-lane.yml",
            ".github/workflows/p3-determinism-sign.yml",
            // The sign / provision scripts + the pinned cosign/trust-root data.
            "gate/tools/sign-determinism.sh",
            "gate/tools/provision-cosign.sh",
            "gate/tools/cosign-pin.json",
            "gate/tools/trusted-root-pin.json",
            "gate/tools/trusted_root.json",
            // The P3 verify surface in the shared gate library (exact files — NOT a dir glob).
            "gate/Corrected.Gate/DeterminismVerifier.cs",
            "gate/Corrected.Gate/DeterminismPolicyClassifier.cs",
            "gate/Corrected.Gate/DeterminismVerifyReason.cs",
            "gate/Corrected.Gate/DeterminismVerifyIdentity.cs",
            "gate/Corrected.Gate/CosignRunner.cs",
            "gate/Corrected.Gate/CosignPin.cs",
            "gate/Corrected.Gate/TrustRootRegistry.cs",
            "gate/Corrected.Gate/GitAncestry.cs",
            "gate/Corrected.Gate/PointerSchema.cs",
            "gate/Corrected.Gate/Probes.cs",
            "gate/Corrected.Gate/StatusRenderer.cs",
            // The classifier + producer themselves (self-inputs — a policy change stales the baseline).
            "gate/Corrected.Gate/SubjectClassification.cs",
            "gate/Corrected.Gate/SubjectClassificationPolicy.cs",
            "gate/Corrected.Gate/SubjectManifestProducer.cs",
            // The verify surface's own package/tool locks + SDK pins (INV-018 "package/tool locks" +
            // "Z3/SDK provisioning"): they fix the System.Text.Json / SDK versions that shape the
            // DSSE-payload + Statement byte-equality, so a bump must stale the baseline (MA-B-AUDIT-04).
            "gate/Corrected.Gate/packages.lock.json",
            "gate/Directory.Packages.props",
            "gate/global.json",
            "global.json",
        },
        Exclusions: new[]
        {
            // Within-root non-determinism noise (a doc / ignore-file edit must not stale P3).
            "spikes/dafny-compat/README.md",
            "spikes/dafny-compat/.gitignore",
        });

    /// <summary>The gate library directory whose <c>.cs</c> files the closed-world anchor net governs.</summary>
    public const string GateLibraryDir = "gate/Corrected.Gate/";

    /// <summary>
    /// The CLOSED-WORLD anchor-completeness net (MA-B-AUDIT-01, AP-022): every committed
    /// <c>gate/Corrected.Gate/*.cs</c> must be EITHER an <see cref="SubjectPolicy.Anchors"/> member
    /// (the P3 determinism-verify surface) OR listed here (a carrier / P1 / lifecycle file that is
    /// intentionally NOT part of the determinism subject). A new file that is neither fails the
    /// anchor-completeness test — so a forgotten verify-surface anchor cannot silently drop out of
    /// the subject set (the fail-open the earlier name-prefix net missed for non-prefixed families
    /// like PointerSchema/Probes/StatusRenderer/GitAncestry). This is a completeness net, not a
    /// semantic one: mis-listing a real verify file here is not caught, but every new file forces a
    /// conscious verify-vs-carrier classification.
    /// </summary>
    public static IReadOnlyList<string> NonVerifyGateFiles { get; } = new[]
    {
        "gate/Corrected.Gate/ActivationValidator.cs",
        "gate/Corrected.Gate/AdrLintBlock.cs",
        "gate/Corrected.Gate/ClosureBuildRunner.cs",
        // The entry-receipt verifier (Group G / INV-030, MA-C) is a SEPARATE verify surface from the
        // determinism subject set — changing it does NOT stale a determinism attestation — so it is a
        // non-(determinism-)verify gate file here, alongside its entry consumers ActivationValidator /
        // LifecycleGate. (The entry attestation has its OWN subject set; it is not INV-018's.)
        "gate/Corrected.Gate/EntryVerifier.cs",
        "gate/Corrected.Gate/EntryVerifyIdentity.cs",
        "gate/Corrected.Gate/EntryVerifyReason.cs",
        "gate/Corrected.Gate/LifecycleGate.cs",
        "gate/Corrected.Gate/MigrationManifest.cs",
        "gate/Corrected.Gate/P1EvidenceAnchors.cs",
        "gate/Corrected.Gate/P1Recompute.cs",
        "gate/Corrected.Gate/Parsers.cs",
        "gate/Corrected.Gate/PostEntryHealth.cs",
        "gate/Corrected.Gate/PrClassifier.cs",
        "gate/Corrected.Gate/ProductionSurfaceScanner.cs",
        "gate/Corrected.Gate/ReadinessExtractionException.cs",
    };
}
