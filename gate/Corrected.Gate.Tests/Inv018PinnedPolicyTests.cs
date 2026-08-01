using System.Linq;
using Corrected.Gate;
using Xunit;

namespace Corrected.Gate.Tests;

/// <summary>
/// P3 determinism-attestation spec INV-018 (~628-663), TB-006 — the PINNED production policy
/// <see cref="SubjectClassificationPolicy.Pinned"/> exercised against the REAL committed tree
/// (integration, `git ls-files`). Documents + guards the maintainer-selected "narrow roots +
/// anchors" boundary: the determinism surface (the spike + the Provenance substrate + the P3
/// verify/sign/pin anchors) is IN; the P2 completion-manifest, the P3 declaration, and repo-root
/// docs are OUT. Also an anchor-completeness net (a new Determinism*/Cosign*/Subject* file added
/// without an anchor fails here).
/// </summary>
public class Inv018PinnedPolicyTests
{
    private static readonly System.Collections.Generic.IReadOnlyList<string> RepoTree =
        SubjectManifestProducer.EnumerateRepoFiles(TestPaths.RepoRoot());

    // Tests INV-018 [integration]: the pinned policy is well-shaped (roots end in '/', exact anchors,
    // exact exclusions, no globs).
    [Fact]
    public void Pinned_policy_validates()
    {
        PolicyValidation v = SubjectClassificationPolicy.Pinned.Validate();
        Assert.True(v.Valid, $"pinned policy must validate; reason: '{v.Reason}'");
    }

    // Tests INV-018 [integration]: known determinism-surface files (committed) ARE in the subject set.
    [Theory]
    [InlineData("spikes/dafny-compat/scripts/run-spike.sh")]
    [InlineData("spikes/dafny-compat/scripts/determinism-lane.sh")]
    [InlineData("spikes/dafny-compat/schema/evidence-schema.json")]
    [InlineData("gate/Corrected.Provenance/Determinism/RunReceipt.cs")]
    [InlineData("gate/Corrected.Provenance/Determinism/DeterminismStatementEmitter.cs")]
    [InlineData("gate/Corrected.Gate/DeterminismVerifier.cs")]
    [InlineData("gate/tools/sign-determinism.sh")]
    [InlineData(".github/workflows/p3-determinism-sign.yml")]
    public void Determinism_surface_file_is_in_the_subject_set(string path)
    {
        var subject = SubjectClassifier.DiscoverSubjectSet(SubjectClassificationPolicy.Pinned, RepoTree);
        Assert.Contains(path, subject);
    }

    // Tests INV-018 [integration]: files OUTSIDE the determinism surface are NOT in the subject set —
    // the P2 completion-manifest and the P3 declaration especially, so a P2 activation / a readiness
    // edit cannot stale P3 (INV-018 exclusion intent, achieved off-surface under narrow roots).
    [Theory]
    [InlineData("test/manifests/phase-0.0-completion.json")]     // P2 completion-manifest
    [InlineData(".correctless/specs/phase-0-1-worker.md")]        // the P3/readiness declaration
    [InlineData("gate/Corrected.Gate/AdrLintBlock.cs")]           // a non-P3 gate file (not anchored)
    [InlineData("spikes/dafny-compat/README.md")]                 // excluded within-root doc
    [InlineData("README.md")]                                     // repo-root doc
    public void Off_surface_file_is_not_in_the_subject_set(string path)
    {
        var subject = SubjectClassifier.DiscoverSubjectSet(SubjectClassificationPolicy.Pinned, RepoTree);
        Assert.DoesNotContain(path, subject);
    }

    // Tests INV-018 [integration]: over the real committed tree every declared exclusion is present
    // AND under an owned root — non-vacuous. A stale/mutated exclusion would fail here.
    [Fact]
    public void Pinned_exclusions_are_complete_over_the_real_tree()
    {
        ExclusionCompletenessResult r =
            SubjectManifestGate.CheckExclusionCompleteness(SubjectClassificationPolicy.Pinned, RepoTree);
        Assert.True(r.Complete,
            "pinned exclusions must be non-vacuous over the real tree; vacuous: "
            + string.Join(", ", r.VacuousExclusions));
    }

    // Tests INV-018 [integration]: the manifest built from the real tree is SET-EQUAL to the
    // classifier's discovered set — the two consumers stay bound (one classifier, two consumers).
    [Fact]
    public void Built_manifest_is_set_equal_to_the_discovered_set()
    {
        var subject = SubjectClassifier.DiscoverSubjectSet(SubjectClassificationPolicy.Pinned, RepoTree);
        SubjectManifest manifest =
            SubjectManifestProducer.BuildFromRepo(SubjectClassificationPolicy.Pinned, TestPaths.RepoRoot());

        SetEqualityResult eq = SubjectManifestGate.CheckSetEquality(subject.ToList(), manifest);
        Assert.True(eq.Equal,
            $"omitted: {string.Join(",", eq.OmittedRelevant)}; extra: {string.Join(",", eq.ExtraInManifest)}");
    }

    // Tests INV-018 [integration] (CLOSED-WORLD ANCHOR-COMPLETENESS NET, AP-022/MA-B-AUDIT-01): EVERY
    // committed gate/Corrected.Gate/*.cs must be EITHER anchored (the P3 verify surface) OR listed in
    // NonVerifyGateFiles (a carrier/lifecycle file). A new file that is neither fails here — so a
    // forgotten verify-surface anchor cannot silently drop out of the subject set. This replaces the
    // earlier name-prefix net, which missed non-prefixed verify files (PointerSchema/Probes/etc.).
    [Fact]
    public void Every_committed_gate_cs_is_anchored_or_listed_non_verify()
    {
        var anchors = SubjectClassificationPolicy.Pinned.Anchors.ToHashSet();
        var nonVerify = SubjectClassificationPolicy.NonVerifyGateFiles.ToHashSet();

        var gateCs = RepoTree.Where(f =>
            f.StartsWith(SubjectClassificationPolicy.GateLibraryDir, System.StringComparison.Ordinal)
            && f.EndsWith(".cs", System.StringComparison.Ordinal));

        foreach (string f in gateCs)
        {
            Assert.True(anchors.Contains(f) || nonVerify.Contains(f),
                $"'{f}' under {SubjectClassificationPolicy.GateLibraryDir} is neither anchored (P3 verify " +
                "surface) nor listed non-verify (INV-018 closed-world anchor-completeness, MA-B-AUDIT-01).");
        }
    }

    // Tests INV-018 [integration] (net partition sanity): the anchor set and the non-verify set are
    // disjoint — a file classified as both would make the completeness net ambiguous.
    [Fact]
    public void Anchor_set_and_non_verify_set_are_disjoint()
    {
        var anchors = SubjectClassificationPolicy.Pinned.Anchors.ToHashSet();
        foreach (string nv in SubjectClassificationPolicy.NonVerifyGateFiles)
        {
            Assert.DoesNotContain(nv, anchors);
        }
    }
}
