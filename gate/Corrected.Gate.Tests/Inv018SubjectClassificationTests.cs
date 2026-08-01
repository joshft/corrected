using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Corrected.Gate;
using Xunit;

namespace Corrected.Gate.Tests;

/// <summary>
/// P3 determinism-attestation spec INV-018 / INV-019 (~628-690), TB-006 — the PURE subject-
/// classification-and-manifest mechanism in <see cref="SubjectClassification"/> et al. ONE pinned,
/// executable policy (owned roots / exact anchors / exact exclusions) drives ONE relevance predicate
/// (<see cref="SubjectClassifier.IsRelevant"/>), and that single predicate feeds BOTH consumers —
/// the manifest set-equality check AND the live-CI required decision (INV-018 "one classifier, two
/// consumers"; "manifest membership and the live-CI trigger must NOT use different definitions").
///
/// This mechanism is exactly the AP-022 / PMB-003 bug class: an accept-side enumeration (the subject
/// set + the exclusion set) that must FAIL CLOSED on any member it forgot to list. The behavioral
/// assertions below (a relevant file omitted from the manifest, a mutated/vacuous exclusion, a
/// duplicate manifest path, an unvalidated policy) all pin the reject direction so a lazy or gaming
/// implementation that fails open cannot pass. Inputs are SYNTHETIC hand-written policies + fake
/// repo-relative path trees — no filesystem I/O, no git (this slice is pure per the contract header).
///
/// NOTE: this is a DIFFERENT invariant from Inv018InsulationTests.cs (the carrier's build-insulation
/// INV-018). Do not conflate; this file exercises the P3 subject classifier only.
/// </summary>
public class Inv018SubjectClassificationTests
{
    // ---- shared synthetic fixtures (no I/O) ------------------------------------------------------

    private const string Root = "gate/Corrected.Gate/";
    private const string AnchorScript = "run-spike.sh";
    private const string AnchorPolicy = "gate/policy.json";
    private const string Excluded = "gate/Corrected.Gate/Gen.cs";

    /// <summary>A well-formed policy: roots end in '/', exact anchors, exact exclusions, no globs.</summary>
    private static SubjectPolicy ValidPolicy() => new(
        OwnedRoots: new[] { Root },
        Anchors: new[] { AnchorScript, AnchorPolicy },
        Exclusions: new[] { Excluded });

    private static ChangeSet ModifiedOnly(string path) => new(
        Added: Array.Empty<string>(),
        Modified: new[] { path },
        Deleted: Array.Empty<string>(),
        Renamed: Array.Empty<RenamedPath>());

    private static string Sha(char c) => new string(c, 64);

    /// <summary>
    /// The canonical manifest digest recomputed INDEPENDENTLY in the test (INV-018/019 byte-compat
    /// pin): rows sorted by Path Ordinal, each emitted as "{Path}\n{Sha256}\n", concatenated, UTF-8,
    /// SHA-256, lowercase hex. This is the same recipe the offline signing script must use, so the
    /// C# verifier stays byte-compatible.
    /// </summary>
    private static string CanonicalDigest(IEnumerable<(string Path, string Sha)> rows)
    {
        var sb = new StringBuilder();
        foreach (var r in rows.OrderBy(r => r.Path, StringComparer.Ordinal))
        {
            sb.Append(r.Path).Append('\n').Append(r.Sha).Append('\n');
        }
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    // ============================================================================================
    // Policy validation (SubjectPolicy.Validate) — INV-018 pinned shape, fail closed
    // ============================================================================================

    // Tests INV-018 [unit]: a well-formed policy (roots end in '/', exact anchors, exact exclusions,
    // no globs, no '..', no leading '/') validates. RED: the stub Validate returns Reject.
    [Fact]
    public void Validate_accepts_a_well_formed_policy()
    {
        PolicyValidation v = ValidPolicy().Validate();
        Assert.True(v.Valid, $"a well-formed policy must validate; got reason: '{v.Reason}'");
    }

    // Build a policy whose ONE targeted list holds a single ill-shaped member; the other two lists
    // stay well-formed, so any rejection is attributable to the injected member.
    private static SubjectPolicy PolicyWith(string kind, string value)
    {
        IReadOnlyList<string> roots = new[] { Root };
        IReadOnlyList<string> anchors = new[] { AnchorScript };
        IReadOnlyList<string> excls = new[] { Excluded };
        switch (kind)
        {
            case "root": roots = new[] { value }; break;
            case "anchor": anchors = new[] { value }; break;
            case "exclusion": excls = new[] { value }; break;
            default: throw new ArgumentOutOfRangeException(nameof(kind), kind, "unknown list kind");
        }
        return new SubjectPolicy(roots, anchors, excls);
    }

    // Tests INV-018 [unit]: an owned root that does NOT end in '/' is rejected (a root is a directory
    // prefix). Genuine fail-closed negative — the closed shape rule.
    [Fact]
    public void Validate_rejects_owned_root_without_trailing_slash()
    {
        Assert.False(PolicyWith("root", "gate/Corrected.Gate").Validate().Valid,
            "an owned root must end in '/'; a root without a trailing slash must be rejected.");
    }

    // Tests INV-018 [unit] (cross-list shape rejection): a leading '/', a '..' segment, an empty
    // string, or a glob metacharacter in ANY of root/anchor/exclusion rejects the whole policy. The
    // cross-product over {list} x {defect} makes an un-checked list or defect a visible failure
    // (AP-022 completeness — a per-list shape rule must not fail open on one un-enumerated list).
    [Theory]
    // root defects
    [InlineData("root", "/gate/x/")]        // leading slash
    [InlineData("root", "gate/../x/")]      // '..' segment
    [InlineData("root", "")]                // empty string
    [InlineData("root", "gate/*/")]         // glob metacharacter
    // anchor defects
    [InlineData("anchor", "/run.sh")]       // leading slash
    [InlineData("anchor", "../run.sh")]     // '..' segment
    [InlineData("anchor", "")]              // empty string
    [InlineData("anchor", "gate/*.sh")]     // glob metacharacter
    // exclusion defects
    [InlineData("exclusion", "/gate/x/g.cs")]     // leading slash
    [InlineData("exclusion", "gate/x/../g.cs")]   // '..' segment
    [InlineData("exclusion", "")]                 // empty string
    public void Validate_rejects_ill_shaped_member(string kind, string value)
    {
        PolicyValidation v = PolicyWith(kind, value).Validate();
        Assert.False(v.Valid,
            $"an ill-shaped {kind} '{value}' must reject the policy (fail closed).");
    }

    // Tests INV-018 [unit] (the central "no broad exclusion globs" property): an exclusion containing
    // a glob metacharacter ('*', '?', or '[') is a broad exclusion glob the spec forbids and REJECTS
    // the policy. One Theory over the three metacharacters (AP-022 — the reject must cover every glob
    // char, not a representative one). Paired with Validate_accepts_a_well_formed_policy (which pins
    // the accept side and fails on the deny stub) this fixes the boundary.
    [Theory]
    [InlineData('*')]
    [InlineData('?')]
    [InlineData('[')]
    public void Validate_rejects_exclusion_containing_a_glob_metacharacter(char meta)
    {
        var policy = new SubjectPolicy(
            OwnedRoots: new[] { Root },
            Anchors: new[] { AnchorScript },
            Exclusions: new[] { $"gate/Corrected.Gate/gen{meta}.cs" });
        Assert.False(policy.Validate().Valid,
            $"an exclusion with the glob metacharacter '{meta}' is a broad exclusion glob (INV-018) " +
            "and must reject the policy.");
    }

    // ============================================================================================
    // Shared relevance predicate (SubjectClassifier.IsRelevant) — INV-018
    // ============================================================================================

    // Tests INV-018 [unit]: the relevance predicate. Under an owned root OR an exact anchor => relevant;
    // an exact exclusion under a root => NOT relevant (exclusion overrides inclusion); an unrelated
    // path OR a prefix-boundary near-miss => NOT relevant. RED: the 'true' rows fail on the deny stub;
    // the 'false' rows are genuine fail-closed negatives.
    [Theory]
    [InlineData("gate/Corrected.Gate/a.cs", true)]          // directly under owned root
    [InlineData("gate/Corrected.Gate/sub/deep.cs", true)]   // nested under owned root
    [InlineData("run-spike.sh", true)]                       // exact anchor
    [InlineData("gate/policy.json", true)]                   // exact anchor
    [InlineData("gate/Corrected.Gate/Gen.cs", false)]        // exact exclusion overrides inclusion
    [InlineData("docs/x.md", false)]                          // unrelated
    [InlineData("gate/Corrected.GateX/y.cs", false)]         // PREFIX BOUNDARY: not a substring match
    [InlineData("gate/Corrected.Gate", false)]               // the root prefix minus the slash is not under-root
    [InlineData("run-spike.sh.bak", false)]                  // anchor is EXACT, not a prefix
    public void IsRelevant_matches_expected(string path, bool expected)
    {
        Assert.Equal(expected, SubjectClassifier.IsRelevant(ValidPolicy(), path));
    }

    // Tests INV-018 [unit] (behavioral pin for exclusion-overrides): a sibling file under the same
    // owned root as an exclusion IS relevant while the exclusion itself is NOT — so the exclusion
    // carves out exactly one file, not the whole root. Fails on the deny stub (the 'true' side).
    [Fact]
    public void IsRelevant_exclusion_removes_only_the_excluded_file_not_its_siblings()
    {
        SubjectPolicy p = ValidPolicy();
        Assert.False(SubjectClassifier.IsRelevant(p, Excluded));
        Assert.True(SubjectClassifier.IsRelevant(p, "gate/Corrected.Gate/sibling.cs"));
    }

    // Tests INV-018 [unit] (prefix-boundary safety, behavioral): the real root path IS relevant while
    // a look-alike sibling directory ("...GateX/") is NOT — a naive StartsWith without the '/' boundary
    // would wrongly accept the look-alike. Fails on the deny stub (the 'true' side).
    [Fact]
    public void IsRelevant_does_not_match_a_look_alike_sibling_directory()
    {
        SubjectPolicy p = ValidPolicy();
        Assert.True(SubjectClassifier.IsRelevant(p, "gate/Corrected.Gate/inside.cs"));
        Assert.False(SubjectClassifier.IsRelevant(p, "gate/Corrected.GateX/inside.cs"));
    }

    // ============================================================================================
    // Consumer 1 (SubjectClassifier.DiscoverSubjectSet) — INV-018 manifest subject set
    // ============================================================================================

    // Tests INV-018 [unit]: the discovered set is EXACTLY the relevant files, Ordinal-sorted,
    // de-duplicated, with exclusions removed. RED: the stub returns an empty set.
    [Fact]
    public void DiscoverSubjectSet_returns_relevant_files_sorted_deduped_and_excluded()
    {
        var tree = new[]
        {
            "gate/Corrected.Gate/b.cs",
            "gate/Corrected.Gate/a.cs",
            "docs/x.md",                       // not relevant -> dropped
            "run-spike.sh",
            "gate/Corrected.Gate/Gen.cs",      // excluded -> dropped
            "gate/Corrected.Gate/a.cs",        // duplicate -> collapsed
            "gate/Corrected.GateX/y.cs",       // prefix-boundary near-miss -> dropped
        };

        IReadOnlyList<string> discovered = SubjectClassifier.DiscoverSubjectSet(ValidPolicy(), tree);

        var expected = new[]
        {
            "gate/Corrected.Gate/a.cs",
            "gate/Corrected.Gate/b.cs",
            "run-spike.sh",
        };
        Assert.Equal(expected, discovered);   // ordered sequence equality (Ordinal sort pinned)
    }

    // Tests INV-018 [unit]: an empty tree yields an empty subject set. Genuine fail-closed negative
    // (holds on the deny stub) — a degenerate input never discovers a phantom subject.
    [Fact]
    public void DiscoverSubjectSet_empty_tree_yields_empty_set()
    {
        Assert.Empty(SubjectClassifier.DiscoverSubjectSet(ValidPolicy(), Array.Empty<string>()));
    }

    // ============================================================================================
    // Consumer 2 (SubjectClassifier.ChangeIsRelevant) — INV-019 live-CI required decision
    // ============================================================================================

    // Tests INV-019 [unit]: an ADDED relevant path requires the live job. RED on the deny stub.
    [Fact]
    public void ChangeIsRelevant_true_for_added_relevant_path()
    {
        var change = new ChangeSet(
            Added: new[] { "gate/Corrected.Gate/a.cs" },
            Modified: Array.Empty<string>(),
            Deleted: Array.Empty<string>(),
            Renamed: Array.Empty<RenamedPath>());
        Assert.True(SubjectClassifier.ChangeIsRelevant(ValidPolicy(), change));
    }

    // Tests INV-019 [unit]: a MODIFIED relevant path requires the live job. RED on the deny stub.
    [Fact]
    public void ChangeIsRelevant_true_for_modified_relevant_path()
    {
        Assert.True(SubjectClassifier.ChangeIsRelevant(ValidPolicy(), ModifiedOnly(AnchorScript)));
    }

    // Tests INV-019 [unit]: a DELETED relevant path requires the live job. RED on the deny stub.
    [Fact]
    public void ChangeIsRelevant_true_for_deleted_relevant_path()
    {
        var change = new ChangeSet(
            Added: Array.Empty<string>(),
            Modified: Array.Empty<string>(),
            Deleted: new[] { "gate/Corrected.Gate/a.cs" },
            Renamed: Array.Empty<RenamedPath>());
        Assert.True(SubjectClassifier.ChangeIsRelevant(ValidPolicy(), change));
    }

    // Tests INV-019 [unit]: a rename whose OLD path is relevant (NEW is not) requires the live job —
    // moving a subject file OUT of the surface must be caught. RED on the deny stub.
    [Fact]
    public void ChangeIsRelevant_true_when_rename_old_path_is_relevant()
    {
        var change = new ChangeSet(
            Added: Array.Empty<string>(),
            Modified: Array.Empty<string>(),
            Deleted: Array.Empty<string>(),
            Renamed: new[] { new RenamedPath("gate/Corrected.Gate/a.cs", "docs/moved.md") });
        Assert.True(SubjectClassifier.ChangeIsRelevant(ValidPolicy(), change));
    }

    // Tests INV-019 [unit]: a rename whose NEW path is relevant (OLD is not) requires the live job —
    // moving a file INTO the surface must be caught. RED on the deny stub.
    [Fact]
    public void ChangeIsRelevant_true_when_rename_new_path_is_relevant()
    {
        var change = new ChangeSet(
            Added: Array.Empty<string>(),
            Modified: Array.Empty<string>(),
            Deleted: Array.Empty<string>(),
            Renamed: new[] { new RenamedPath("docs/old.md", "gate/Corrected.Gate/new.cs") });
        Assert.True(SubjectClassifier.ChangeIsRelevant(ValidPolicy(), change));
    }

    // Tests INV-019 [unit]: a change touching only NON-relevant paths does not require the live job.
    // Genuine fail-closed negative (holds on the deny stub).
    [Fact]
    public void ChangeIsRelevant_false_when_only_non_relevant_paths_change()
    {
        var change = new ChangeSet(
            Added: new[] { "docs/x.md" },
            Modified: new[] { "README.md" },
            Deleted: new[] { "notes/todo.txt" },
            Renamed: new[] { new RenamedPath("a/old.md", "b/new.md") });
        Assert.False(SubjectClassifier.ChangeIsRelevant(ValidPolicy(), change));
    }

    // Tests INV-019 [unit]: an empty change set does not require the live job. Fail-closed negative.
    [Fact]
    public void ChangeIsRelevant_false_for_empty_change()
    {
        var change = new ChangeSet(
            Added: Array.Empty<string>(),
            Modified: Array.Empty<string>(),
            Deleted: Array.Empty<string>(),
            Renamed: Array.Empty<RenamedPath>());
        Assert.False(SubjectClassifier.ChangeIsRelevant(ValidPolicy(), change));
    }

    // ============================================================================================
    // ONE-PREDICATE-TWO-CONSUMERS (INV-018: manifest membership and the live-CI trigger must NOT use
    // different definitions) — the single relevance predicate binds both consumers.
    // ============================================================================================

    // Tests INV-018 [unit] (the binding property): for each (policy, path) the three views AGREE —
    // IsRelevant(path) == DiscoverSubjectSet({path}).Contains(path) == ChangeIsRelevant(modified:path)
    // — AND each equals the expected relevance. A divergent path filter (one consumer using a
    // different definition) breaks the agreement; a deny stub fails the 'true' rows.
    [Theory]
    [InlineData("gate/Corrected.Gate/a.cs", true)]      // under owned root
    [InlineData("run-spike.sh", true)]                   // exact anchor
    [InlineData("gate/Corrected.Gate/Gen.cs", false)]   // excluded => all three must agree FALSE
    [InlineData("docs/x.md", false)]                     // unrelated
    [InlineData("gate/Corrected.GateX/y.cs", false)]    // prefix-boundary near-miss
    public void One_predicate_drives_both_consumers_consistently(string path, bool expected)
    {
        SubjectPolicy p = ValidPolicy();

        bool isRelevant = SubjectClassifier.IsRelevant(p, path);
        bool inDiscovered = SubjectClassifier.DiscoverSubjectSet(p, new[] { path }).Contains(path);
        bool changeRelevant = SubjectClassifier.ChangeIsRelevant(p, ModifiedOnly(path));

        Assert.Equal(isRelevant, inDiscovered);       // consumer 1 agrees with the predicate
        Assert.Equal(isRelevant, changeRelevant);     // consumer 2 agrees with the predicate
        Assert.Equal(expected, isRelevant);           // and the predicate is correct
    }

    // ============================================================================================
    // Manifest digest (SubjectManifest.ComputeDigest / .Paths) — INV-018/019 canonical, fail closed
    // ============================================================================================

    private static SubjectManifest Manifest(params (string Path, string Sha)[] rows)
        => new(rows.Select(r => new SubjectManifestEntry(r.Path, r.Sha)).ToList());

    // Tests INV-018 [unit]: Paths is the Ordinal-distinct set of enumerated paths. RED: the stub
    // returns an empty collection.
    [Fact]
    public void Manifest_Paths_are_the_distinct_enumerated_path_set()
    {
        SubjectManifest m = Manifest(
            ("b/second.cs", Sha('1')),
            ("a/first.cs", Sha('0')));
        Assert.Equal(
            new HashSet<string> { "a/first.cs", "b/second.cs" },
            m.Paths.ToHashSet());
    }

    // Tests INV-018 [unit] (GOLDEN canonical-format pin, byte-compat with the signing script): the
    // digest equals SHA-256 of the rows sorted by Path Ordinal, each "{Path}\n{Sha256}\n", UTF-8,
    // lowercase hex. The expected value is a HARD LITERAL computed OUTSIDE the C# code (sha256sum of
    // the canonical bytes) so it is independent of both the implementation and the in-test helper.
    // Entries are supplied in REVERSE order to prove ComputeDigest sorts internally. RED: the stub
    // returns the "STUB:TDD" sentinel.
    [Fact]
    public void ComputeDigest_matches_the_pinned_canonical_golden()
    {
        // Canonical bytes = "a/first.cs\n<64x'0'>\nb/second.cs\n<64x'1'>\n"
        // External golden: printf ... | sha256sum
        const string golden = "9a082a85cb0ce1af606cfce0c126f167a0b57556db4ce5e672327983952a1497";

        SubjectManifest m = Manifest(
            ("b/second.cs", Sha('1')),   // deliberately out of order
            ("a/first.cs", Sha('0')));

        Assert.Equal(golden, m.ComputeDigest());
        // and the in-test recompute agrees with the external literal (guards the helper itself).
        Assert.Equal(golden, CanonicalDigest(new[] { ("b/second.cs", Sha('1')), ("a/first.cs", Sha('0')) }));
    }

    // Tests INV-018 [unit]: the digest is deterministic (same rows -> same value) and equals the
    // independently-recomputed canonical digest. RED: the stub sentinel != the canonical digest.
    [Fact]
    public void ComputeDigest_is_deterministic_and_equals_the_recomputed_canonical()
    {
        var rows = new[] { ("gate/x.cs", Sha('a')), ("gate/y.cs", Sha('b')) };
        SubjectManifest m1 = Manifest(rows);
        SubjectManifest m2 = Manifest(rows);

        Assert.Equal(m1.ComputeDigest(), m2.ComputeDigest());
        Assert.Equal(CanonicalDigest(rows), m1.ComputeDigest());
    }

    // Tests INV-018 [unit] (ORDER-INDEPENDENCE): the same rows in a different order produce the SAME
    // digest (the canonical form sorts by Path). RED: the stub sentinel != the canonical digest.
    [Fact]
    public void ComputeDigest_is_order_independent()
    {
        SubjectManifest forward = Manifest(
            ("a/one.cs", Sha('a')),
            ("b/two.cs", Sha('b')),
            ("c/three.cs", Sha('c')));
        SubjectManifest shuffled = Manifest(
            ("c/three.cs", Sha('c')),
            ("a/one.cs", Sha('a')),
            ("b/two.cs", Sha('b')));

        Assert.Equal(forward.ComputeDigest(), shuffled.ComputeDigest());
        Assert.Equal(
            CanonicalDigest(new[] { ("a/one.cs", Sha('a')), ("b/two.cs", Sha('b')), ("c/three.cs", Sha('c')) }),
            shuffled.ComputeDigest());
    }

    // Tests INV-018 [unit] (SENSITIVITY): changing any Path yields a DIFFERENT digest. RED: the stub
    // returns the same sentinel for both, so this assertion fails now.
    [Fact]
    public void ComputeDigest_changes_when_a_path_changes()
    {
        SubjectManifest a = Manifest(("gate/x.cs", Sha('a')), ("gate/y.cs", Sha('b')));
        SubjectManifest b = Manifest(("gate/x.cs", Sha('a')), ("gate/z.cs", Sha('b')));
        Assert.NotEqual(a.ComputeDigest(), b.ComputeDigest());
    }

    // Tests INV-018 [unit] (SENSITIVITY): changing any Sha256 yields a DIFFERENT digest. RED on the
    // constant stub sentinel.
    [Fact]
    public void ComputeDigest_changes_when_a_sha_changes()
    {
        SubjectManifest a = Manifest(("gate/x.cs", Sha('a')), ("gate/y.cs", Sha('b')));
        SubjectManifest b = Manifest(("gate/x.cs", Sha('a')), ("gate/y.cs", Sha('c')));
        Assert.NotEqual(a.ComputeDigest(), b.ComputeDigest());
    }

    // Tests INV-018 [unit] (SENSITIVITY): adding a row yields a DIFFERENT digest. RED on the stub.
    [Fact]
    public void ComputeDigest_changes_when_a_row_is_added()
    {
        SubjectManifest a = Manifest(("gate/x.cs", Sha('a')));
        SubjectManifest b = Manifest(("gate/x.cs", Sha('a')), ("gate/y.cs", Sha('b')));
        Assert.NotEqual(a.ComputeDigest(), b.ComputeDigest());
    }

    // Tests INV-018 [unit] (SENSITIVITY): removing a row yields a DIFFERENT digest. RED on the stub.
    [Fact]
    public void ComputeDigest_changes_when_a_row_is_removed()
    {
        SubjectManifest a = Manifest(("gate/x.cs", Sha('a')), ("gate/y.cs", Sha('b')));
        SubjectManifest b = Manifest(("gate/x.cs", Sha('a')));
        Assert.NotEqual(a.ComputeDigest(), b.ComputeDigest());
    }

    // Tests INV-018 [unit] (DUPLICATE PATH FAILS CLOSED, AP-022): a manifest with two rows sharing a
    // Path is malformed and ComputeDigest throws InvalidOperationException — it never silently
    // collapses or hashes an ambiguous set. RED: the stub returns a sentinel without throwing.
    [Fact]
    public void ComputeDigest_throws_on_duplicate_paths()
    {
        SubjectManifest dup = Manifest(
            ("gate/x.cs", Sha('a')),
            ("gate/x.cs", Sha('b')));   // same Path, different Sha -> malformed
        Assert.Throws<InvalidOperationException>(() => dup.ComputeDigest());
    }

    // ============================================================================================
    // Set-equality (SubjectManifestGate.CheckSetEquality) — INV-018 manifest <-> classifier, closed
    // ============================================================================================

    // Tests INV-018 [unit]: when the discovered set and the manifest paths match exactly, Equal==true
    // and both difference lists are empty. RED: the stub forces a non-equal verdict.
    [Fact]
    public void CheckSetEquality_equal_when_sets_match()
    {
        var discovered = new[] { "gate/a.cs", "gate/b.cs" };
        SubjectManifest manifest = Manifest(("gate/a.cs", Sha('a')), ("gate/b.cs", Sha('b')));

        SetEqualityResult r = SubjectManifestGate.CheckSetEquality(discovered, manifest);

        Assert.True(r.Equal, "matching discovered set and manifest must be set-equal.");
        Assert.Empty(r.OmittedRelevant);
        Assert.Empty(r.ExtraInManifest);
    }

    // Tests INV-018 [unit] (THE AP-022 GUARD — OMITTED RELEVANT FAILS CLOSED): a relevant file the
    // classifier discovered but the manifest omits => Equal==false AND the omitted path appears in
    // OmittedRelevant. RED: the stub reports an EMPTY OmittedRelevant, so the behavioral Contains
    // assertion fails now (a fail-open manifest that silently drops a subject file must not pass).
    [Fact]
    public void CheckSetEquality_omitted_relevant_file_fails_closed()
    {
        var discovered = new[] { "gate/a.cs", "gate/b.cs", "gate/c.cs" };
        SubjectManifest manifest = Manifest(("gate/a.cs", Sha('a')), ("gate/b.cs", Sha('b')));

        SetEqualityResult r = SubjectManifestGate.CheckSetEquality(discovered, manifest);

        Assert.False(r.Equal);
        Assert.Contains("gate/c.cs", r.OmittedRelevant);
        Assert.DoesNotContain("gate/c.cs", r.ExtraInManifest);
    }

    // Tests INV-018 [unit]: an extra manifest row not in the discovered set => Equal==false AND the
    // path appears in ExtraInManifest. RED: the stub reports an empty ExtraInManifest.
    [Fact]
    public void CheckSetEquality_extra_manifest_row_is_reported()
    {
        var discovered = new[] { "gate/a.cs", "gate/b.cs" };
        SubjectManifest manifest = Manifest(
            ("gate/a.cs", Sha('a')), ("gate/b.cs", Sha('b')), ("gate/c.cs", Sha('c')));

        SetEqualityResult r = SubjectManifestGate.CheckSetEquality(discovered, manifest);

        Assert.False(r.Equal);
        Assert.Contains("gate/c.cs", r.ExtraInManifest);
        Assert.DoesNotContain("gate/c.cs", r.OmittedRelevant);
    }

    // ============================================================================================
    // Exclusion-completeness (SubjectManifestGate.CheckExclusionCompleteness) — INV-018, fail closed
    // ============================================================================================

    // Tests INV-018 [unit]: every declared exclusion is present in the tree AND under an owned root
    // (would-be-relevant absent the carve-out) => Complete==true, no vacuous exclusions. RED: the stub
    // forces an incomplete verdict.
    [Fact]
    public void CheckExclusionCompleteness_complete_when_every_exclusion_is_present_and_relevant()
    {
        var policy = new SubjectPolicy(
            OwnedRoots: new[] { Root },
            Anchors: new[] { AnchorScript },
            Exclusions: new[] { "gate/Corrected.Gate/Gen.cs" });
        var tree = new[]
        {
            "gate/Corrected.Gate/Gen.cs",   // the excluded file exists AND is under the root
            "gate/Corrected.Gate/a.cs",
            AnchorScript,
        };

        ExclusionCompletenessResult r = SubjectManifestGate.CheckExclusionCompleteness(policy, tree);

        Assert.True(r.Complete, "a present, under-root exclusion is non-vacuous; completeness holds.");
        Assert.Empty(r.VacuousExclusions);
    }

    // Tests INV-018 [unit] (MUTATED EXCLUSION FAILS CLOSED, AP-022): an exclusion typo'd to a path
    // ABSENT from the tree protects nothing => Complete==false AND the typo appears in
    // VacuousExclusions. RED: the stub reports an EMPTY VacuousExclusions, so the behavioral Contains
    // assertion fails now (a stale/mutated carve-out must not silently pass).
    [Fact]
    public void CheckExclusionCompleteness_mutated_exclusion_absent_from_tree_is_vacuous()
    {
        var policy = new SubjectPolicy(
            OwnedRoots: new[] { Root },
            Anchors: new[] { AnchorScript },
            Exclusions: new[] { "gate/Corrected.Gate/Genx.cs" });   // typo: 'Genx' not in the tree
        var tree = new[]
        {
            "gate/Corrected.Gate/Gen.cs",   // the REAL file (the policy points at the typo)
            "gate/Corrected.Gate/a.cs",
        };

        ExclusionCompletenessResult r = SubjectManifestGate.CheckExclusionCompleteness(policy, tree);

        Assert.False(r.Complete);
        Assert.Contains("gate/Corrected.Gate/Genx.cs", r.VacuousExclusions);
    }

    // Tests INV-018 [unit] (VACUOUS-BY-SCOPE FAILS CLOSED): an exclusion that IS present in the tree
    // but is NOT under any owned root and is NOT an anchor protects nothing (it would never have been
    // discovered) => Complete==false AND the path appears in VacuousExclusions. RED: the stub reports
    // an empty VacuousExclusions.
    [Fact]
    public void CheckExclusionCompleteness_exclusion_outside_any_root_is_vacuous()
    {
        var policy = new SubjectPolicy(
            OwnedRoots: new[] { Root },
            Anchors: new[] { AnchorScript },
            Exclusions: new[] { "docs/readme.md" });   // present below, but under NO owned root/anchor
        var tree = new[]
        {
            "docs/readme.md",                 // present, yet not relevant-by-root/anchor
            "gate/Corrected.Gate/a.cs",
        };

        ExclusionCompletenessResult r = SubjectManifestGate.CheckExclusionCompleteness(policy, tree);

        Assert.False(r.Complete);
        Assert.Contains("docs/readme.md", r.VacuousExclusions);
    }

    // Tests INV-018 [unit] (MA-B-AUDIT-03): a policy member containing a backslash is rejected by
    // Validate — a backslash is never a repo-relative forward-slash separator, so it would match
    // nothing against git paths (a silent vacuity / under-inclusion). Checked for all three lists.
    [Theory]
    [InlineData("root", "gate\\Corrected.Gate/")]
    [InlineData("anchor", "gate\\Corrected.Gate\\x.cs")]
    [InlineData("exclusion", "gate\\Corrected.Gate\\x.cs")]
    public void Validate_rejects_a_backslash_member(string kind, string value)
    {
        Assert.False(PolicyWith(kind, value).Validate().Valid,
            $"a {kind} with a backslash must be rejected (forward-slash repo paths only).");
    }

    // Tests INV-018 [unit] (MA-B-AUDIT-02): ComputeDigest fails closed on a newline (or NUL) inside a
    // Path or Sha256. The canonical form uses '\n' as the field/row delimiter with no escaping, so
    // an embedded delimiter would make the serialization NON-injective (a digest collision -> a
    // stale baseline reading fresh). It throws rather than hash an ambiguous manifest.
    [Fact]
    public void ComputeDigest_throws_on_a_delimiter_inside_a_field()
    {
        Assert.Throws<InvalidOperationException>(
            () => Manifest(("gate/a\nb.cs", Sha('a'))).ComputeDigest());
        Assert.Throws<InvalidOperationException>(
            () => Manifest(("gate/a.cs", "ab\ncd")).ComputeDigest());
    }
}
