using System;
using System.Collections.Generic;
using System.Linq;
using Corrected.Gate;
using Xunit;

namespace Corrected.Gate.Tests;

/// <summary>
/// Track 5d-ii — INV-028 (spec ~1089–1155) CLOSED pointer schema (F4, ~1135–1141) + the
/// dangling-pointer fail-closed coupling (RS-029, ~1130–1134); the "Violated when" clause at
/// ~1142–1147 is the pinned negative source.
///
/// Subject: the new <see cref="PointerSchema"/> component in Corrected.Gate —
///   * <see cref="PointerFamily"/> (EXACTLY two fixed-root families; set-equality pinned, PMB-003),
///   * <see cref="PointerDescriptor"/> (a synthetic, already-parsed pointer),
///   * <see cref="PointerSchema.ValidatePointer"/> (Valid iff ALL five rules hold, else fail
///     closed with a typed reason — deny-by-default, AP-001/AP-017).
///
/// This slice validates ONLY the pointer schema. It does NOT touch the sibling 5d-i health fold
/// (<c>PostEntryHealth</c>) — that component is deliberately not referenced here.
///
/// Every rule is exercised across BOTH families (P3ActiveBaseline AND EntryEvidence) so no family
/// is left unpinned (PMB-003): the shared clause tests are [Theory]s over the family enum.
///
/// Level: [unit]. Despite INV-028's overall <c>Test approach: integration</c>, the closed pointer
/// schema is a PURE, total function over synthetic descriptors + a supplied committed-path set —
/// there is no wiring, filesystem, or crypto to exercise here (identical rationale to the sibling
/// 5d-i fold tests). AP-031 real-artifact clause is NOT triggered: these tests build in-memory
/// descriptors, never parse a `.correctless/artifacts/` producer output from another skill.
///
/// RED expectation: <see cref="PointerSchema.ValidatePointer"/> is a deny-by-default stub, so the
/// two well-formed POSITIVE cells (one per family) FAIL as assertions and every fail-closed
/// NEGATIVE cell PASSES on the stub (and MUST stay green once GREEN implements the rules).
/// </summary>
public class Inv028PointerSchemaTests
{
    // =====================================================================================
    // Spec-pinned ORACLE constants: the family fixed roots (spec ~1136–1137) and a synthetic
    // <commit> segment. These are literals ON PURPOSE — they are the oracle the fixtures are
    // built from; if the implementation binds the wrong root for a family, the per-family
    // positive cell (built here under the CORRECT root) fails.
    // =====================================================================================

    private const string P3Root = "test/attestations/inv010/";
    private const string EntryRoot = "test/attestations/entry/";
    private const string Commit = "a1b2c3d4e5f6a1b2";
    private const string OtherCommit = "f6e5d4c3b2a1f6e5";

    private static string RootOf(PointerFamily f) => f switch
    {
        PointerFamily.P3ActiveBaseline => P3Root,
        PointerFamily.EntryEvidence => EntryRoot,
        _ => throw new ArgumentOutOfRangeException(nameof(f), f, "unknown family"),
    };

    private static string OtherRootOf(PointerFamily f) => f == PointerFamily.P3ActiveBaseline
        ? EntryRoot
        : P3Root;

    private static HashSet<string> Committed(params string[] paths)
        => new(paths, StringComparer.Ordinal);

    /// <summary>The parts of a fully well-formed fixture, so each negative can mutate ONE dimension.</summary>
    private sealed record Fixture(
        PointerDescriptor D, HashSet<string> CommittedSet, string Root, string Receipt, string Bundle);

    /// <summary>
    /// A fully well-formed pointer for <paramref name="family"/>: single receipt + single bundle,
    /// both under the family's fixed root in the same <c>&lt;commit&gt;</c> dir agreeing with the
    /// declared attested_commit AND the on-disk dir segment, both committed, not a symlink. The
    /// deny-by-default stub rejects it (RED); the negatives below each break exactly one dimension.
    /// </summary>
    private static Fixture Valid(PointerFamily family)
    {
        string root = RootOf(family);
        string receipt = root + Commit + "/receipt.json";
        string bundle = root + Commit + "/bundle.json";
        var d = new PointerDescriptor(
            Family: family,
            ReceiptPaths: new[] { receipt },
            BundlePaths: new[] { bundle },
            AttestedCommit: Commit,
            OnDiskDirSegment: Commit,
            TargetIsSymlink: false);
        return new Fixture(d, Committed(receipt, bundle), root, receipt, bundle);
    }

    private static PointerValidation Validate(PointerDescriptor? d, IReadOnlySet<string> committed)
        => PointerSchema.ValidatePointer(d, committed);

    /// <summary>Assert a fail-closed rejection carrying a typed (non-empty) reason.</summary>
    private static void AssertReject(PointerDescriptor? d, IReadOnlySet<string> committed, string clause)
    {
        PointerValidation r = Validate(d, committed);
        Assert.False(r.Valid, $"expected REJECT ({clause}) but got Valid==true; reason='{r.Reason}'");
        Assert.False(
            string.IsNullOrWhiteSpace(r.Reason),
            $"reject for {clause} must carry a typed (non-empty) reason (deny-by-default)");
    }

    // =====================================================================================
    // (A) VOCABULARY PIN — PointerFamily is EXACTLY the two fixed-root families (PMB-003).
    // =====================================================================================

    // Tests INV-028 [unit]: PointerFamily is EXACTLY {P3ActiveBaseline, EntryEvidence} — a
    // set-equality pin over Enum.GetValues, NOT a count. Breaks if a member is added/removed/
    // renamed or a default/sentinel family is introduced. Passes on the stub (members exist);
    // goes RED only on a family-vocabulary drift.
    [Fact]
    public void PointerFamily_is_exactly_the_two_families()
    {
        Assert.Equal(
            new HashSet<PointerFamily> { PointerFamily.P3ActiveBaseline, PointerFamily.EntryEvidence },
            Enum.GetValues<PointerFamily>().ToHashSet());
    }

    // Tests INV-028 [unit] (QA-012): an unknown / cast / future PointerFamily fails CLOSED — FixedRootOf
    // returns null for it, so ValidatePointer cannot establish the fixed root and rejects. Guards the
    // deny-by-default direction for a family member added without a fixed-root mapping.
    [Fact]
    public void Cast_out_of_range_pointer_family_fails_closed()
    {
        var castFamily = (PointerFamily)0x7FFF;
        Assert.DoesNotContain(castFamily, Enum.GetValues<PointerFamily>()); // genuinely out-of-range
        Fixture fx = Valid(PointerFamily.P3ActiveBaseline);
        PointerDescriptor d = fx.D with { Family = castFamily };
        AssertReject(d, fx.CommittedSet, "unknown/cast PointerFamily (FixedRootOf -> null)");
    }

    // =====================================================================================
    // (B) POSITIVE — a well-formed pointer is Valid (one per family). RED against the stub.
    // =====================================================================================

    // Tests INV-028 [unit] (F4 accept path, all five rules hold): a single receipt + single
    // bundle under the family's fixed root, commit-dir agreeing across path/attested/on-disk,
    // both committed, not a symlink → Valid==true. FAILS as an assertion against the deny stub.
    [Theory]
    [InlineData(PointerFamily.P3ActiveBaseline)]
    [InlineData(PointerFamily.EntryEvidence)]
    public void WellFormed_pointer_is_valid(PointerFamily family)
    {
        Fixture fx = Valid(family);
        PointerValidation r = Validate(fx.D, fx.CommittedSet);
        Assert.True(r.Valid, $"expected VALID for well-formed {family} pointer; reason='{r.Reason}'");
        Assert.True(string.IsNullOrEmpty(r.Reason), "an accept verdict carries no rejection reason");
    }

    // Tests INV-028 [unit] (A3 — RS-029 is committed-set MEMBERSHIP, not set-EQUALITY): a
    // well-formed pointer whose committed set ALSO contains an UNRELATED committed path (another
    // versioned receipt in a different <commit> dir) is STILL Valid==true. Pins that the dangling
    // check asks "are BOTH my targets present?" (membership), never "is the committed set exactly
    // {receipt,bundle}?" (equality) — an equality impl would wrongly reject a real repo whose
    // attestations tree has many other committed receipts. A NEW positive cell → RED on the deny
    // stub (both families).
    [Theory]
    [InlineData(PointerFamily.P3ActiveBaseline)]
    [InlineData(PointerFamily.EntryEvidence)]
    public void WellFormed_pointer_valid_with_extra_unrelated_committed_paths(PointerFamily family)
    {
        Fixture fx = Valid(family);
        string unrelated = fx.Root + OtherCommit + "/receipt.json"; // a real, committed, unrelated receipt
        var committedSuperset = Committed(fx.Receipt, fx.Bundle, unrelated);
        PointerValidation r = Validate(fx.D, committedSuperset);
        Assert.True(
            r.Valid,
            $"expected VALID for well-formed {family} pointer amid extra committed paths; reason='{r.Reason}'");
    }

    // =====================================================================================
    // (C) CARDINALITY (exact) — exactly one receipt AND exactly one bundle (spec ~1138).
    // Each mutation breaks ONLY cardinality; all other dimensions stay valid.
    // =====================================================================================

    // Tests INV-028 [unit] (F4 exact cardinality): ZERO named receipts → reject (a pointer that
    // names no receipt is not "exactly one").
    [Theory]
    [InlineData(PointerFamily.P3ActiveBaseline)]
    [InlineData(PointerFamily.EntryEvidence)]
    public void Zero_receipts_rejected(PointerFamily family)
    {
        Fixture fx = Valid(family);
        PointerDescriptor d = fx.D with { ReceiptPaths = Array.Empty<string>() };
        AssertReject(d, fx.CommittedSet, "cardinality: zero receipts");
    }

    // Tests INV-028 [unit] (F4 exact cardinality): TWO named receipts → reject (not "exactly one").
    // The second receipt is itself well-formed + committed, so cardinality is the SOLE violation.
    [Theory]
    [InlineData(PointerFamily.P3ActiveBaseline)]
    [InlineData(PointerFamily.EntryEvidence)]
    public void Two_receipts_rejected(PointerFamily family)
    {
        Fixture fx = Valid(family);
        string second = fx.Root + Commit + "/receipt-2.json";
        PointerDescriptor d = fx.D with { ReceiptPaths = new[] { fx.Receipt, second } };
        AssertReject(d, Committed(fx.Receipt, second, fx.Bundle), "cardinality: two receipts");
    }

    // Tests INV-028 [unit] (F4 exact cardinality): ZERO named bundles → reject.
    [Theory]
    [InlineData(PointerFamily.P3ActiveBaseline)]
    [InlineData(PointerFamily.EntryEvidence)]
    public void Zero_bundles_rejected(PointerFamily family)
    {
        Fixture fx = Valid(family);
        PointerDescriptor d = fx.D with { BundlePaths = Array.Empty<string>() };
        AssertReject(d, fx.CommittedSet, "cardinality: zero bundles");
    }

    // Tests INV-028 [unit] (F4 exact cardinality): TWO named bundles → reject.
    [Theory]
    [InlineData(PointerFamily.P3ActiveBaseline)]
    [InlineData(PointerFamily.EntryEvidence)]
    public void Two_bundles_rejected(PointerFamily family)
    {
        Fixture fx = Valid(family);
        string second = fx.Root + Commit + "/bundle-2.json";
        PointerDescriptor d = fx.D with { BundlePaths = new[] { fx.Bundle, second } };
        AssertReject(d, Committed(fx.Receipt, fx.Bundle, second), "cardinality: two bundles");
    }

    // =====================================================================================
    // (D) CLOSED PATH SCHEMA — normalized, repo-relative, under the family fixed root
    // (no absolute/drive, no '..', no empty '//' segment, must start with the family root).
    // Each malformed path is ADDED to the committed set so the dangling clause cannot be the
    // sole reason — the schema clause under test is the only otherwise-open violation.
    // =====================================================================================

    // Tests INV-028 [unit] (F4 "no absolute paths"): an ABSOLUTE receipt path (leading '/') →
    // reject (an absolute path escapes the repo-relative-under-root schema).
    [Theory]
    [InlineData(PointerFamily.P3ActiveBaseline)]
    [InlineData(PointerFamily.EntryEvidence)]
    public void Absolute_receipt_path_rejected(PointerFamily family)
    {
        Fixture fx = Valid(family);
        string abs = "/" + fx.Root + Commit + "/receipt.json";
        PointerDescriptor d = fx.D with { ReceiptPaths = new[] { abs } };
        AssertReject(d, Committed(abs, fx.Bundle), "path schema: absolute (leading slash)");
    }

    // Tests INV-028 [unit] (F4 "no absolute paths"): a Windows-DRIVE-rooted receipt path (C:) →
    // reject (a drive-absolute path is not a normalized repo-relative path under the root).
    [Theory]
    [InlineData(PointerFamily.P3ActiveBaseline)]
    [InlineData(PointerFamily.EntryEvidence)]
    public void Windows_drive_receipt_path_rejected(PointerFamily family)
    {
        Fixture fx = Valid(family);
        string drive = "C:/" + fx.Root + Commit + "/receipt.json";
        PointerDescriptor d = fx.D with { ReceiptPaths = new[] { drive } };
        AssertReject(d, Committed(drive, fx.Bundle), "path schema: Windows drive (C:)");
    }

    // Tests INV-028 [unit] (F4 "no '..'"): a receipt path containing a '..' segment → reject.
    // The path otherwise stays under-root with an agreeing <commit>, so '..' is the sole break.
    [Theory]
    [InlineData(PointerFamily.P3ActiveBaseline)]
    [InlineData(PointerFamily.EntryEvidence)]
    public void Dotdot_segment_receipt_path_rejected(PointerFamily family)
    {
        Fixture fx = Valid(family);
        string dotdot = fx.Root + Commit + "/../" + Commit + "/receipt.json";
        PointerDescriptor d = fx.D with { ReceiptPaths = new[] { dotdot } };
        AssertReject(d, Committed(dotdot, fx.Bundle), "path schema: '..' segment");
    }

    // Tests INV-028 [unit] (F4 normalized — no empty segment): a receipt path with an empty '//'
    // segment (between the commit dir and the filename, so <commit> still agrees) → reject.
    [Theory]
    [InlineData(PointerFamily.P3ActiveBaseline)]
    [InlineData(PointerFamily.EntryEvidence)]
    public void Empty_double_slash_segment_receipt_path_rejected(PointerFamily family)
    {
        Fixture fx = Valid(family);
        string doubled = fx.Root + Commit + "//receipt.json";
        PointerDescriptor d = fx.D with { ReceiptPaths = new[] { doubled } };
        AssertReject(d, Committed(doubled, fx.Bundle), "path schema: empty '//' segment");
    }

    // Tests INV-028 [unit] (F4 closed schema applies to the BUNDLE too, not only the receipt):
    // a malformed (absolute) BUNDLE path → reject. Proves rule 2 is enforced on both named paths.
    [Theory]
    [InlineData(PointerFamily.P3ActiveBaseline)]
    [InlineData(PointerFamily.EntryEvidence)]
    public void Bundle_path_schema_also_enforced(PointerFamily family)
    {
        Fixture fx = Valid(family);
        string absBundle = "/" + fx.Root + Commit + "/bundle.json";
        PointerDescriptor d = fx.D with { BundlePaths = new[] { absBundle } };
        AssertReject(d, Committed(fx.Receipt, absBundle), "path schema: absolute BUNDLE path");
    }

    // B1 (audit): the following three bundle-only negatives isolate a DEDICATED bundle path-schema
    // check that the absolute-bundle case above does NOT (an absolute bundle also fails commit-dir
    // agreement, so it cannot prove the bundle path itself is schema-validated). Each mutates ONLY
    // BundlePaths — the receipt stays fully valid, cardinality 1/1, symlink false — AND the
    // malformed bundle is ADDED to the committed set so "dangling" cannot be the passing reason,
    // AND each malformed bundle's first-after-root <commit> segment equals the receipt's <commit>,
    // so commit-dir agreement is NOT the passing reason either. The bundle path-schema break is the
    // sole otherwise-open violation: a GREEN that fully validates the receipt path but checks the
    // bundle only for committed + same-<commit>-segment fails OPEN and is caught here.

    // Tests INV-028 [unit] (F4 "no '..'" enforced on the BUNDLE): a bundle path with a
    // self-cancelling '..' segment (normalizes to the valid dir; first-after-root segment ==
    // <commit>) → reject on the un-normalized '..' segment.
    [Theory]
    [InlineData(PointerFamily.P3ActiveBaseline)]
    [InlineData(PointerFamily.EntryEvidence)]
    public void Bundle_dotdot_segment_rejected(PointerFamily family)
    {
        Fixture fx = Valid(family);
        string dotdotBundle = fx.Root + Commit + "/../" + Commit + "/bundle.json";
        PointerDescriptor d = fx.D with { BundlePaths = new[] { dotdotBundle } };
        AssertReject(d, Committed(fx.Receipt, dotdotBundle), "path schema: BUNDLE '..' segment");
    }

    // Tests INV-028 [unit] (F4 normalized — no empty segment — enforced on the BUNDLE): a bundle
    // path with an empty '//' segment between the commit dir and the filename (so <commit> still
    // agrees) → reject.
    [Theory]
    [InlineData(PointerFamily.P3ActiveBaseline)]
    [InlineData(PointerFamily.EntryEvidence)]
    public void Bundle_empty_double_slash_segment_rejected(PointerFamily family)
    {
        Fixture fx = Valid(family);
        string doubledBundle = fx.Root + Commit + "//bundle.json";
        PointerDescriptor d = fx.D with { BundlePaths = new[] { doubledBundle } };
        AssertReject(d, Committed(fx.Receipt, doubledBundle), "path schema: BUNDLE empty '//' segment");
    }

    // Tests INV-028 [unit] (F4 "under the FAMILY's fixed root" enforced on the BUNDLE): a bundle
    // under the OTHER family's root (still under the shared 'test/attestations/' prefix) but with a
    // matching <commit> segment → reject. The receipt is under the CORRECT family root; only the
    // bundle escapes the family root. Its <commit> equals the receipt's, so a commit-segment-only
    // impl would pass it — the dedicated bundle family-root check must reject (both directions:
    // P3 pointer with an entry-root bundle, and an entry pointer with an inv010-root bundle).
    [Theory]
    [InlineData(PointerFamily.P3ActiveBaseline)]
    [InlineData(PointerFamily.EntryEvidence)]
    public void Bundle_wrong_family_root_rejected(PointerFamily family)
    {
        Fixture fx = Valid(family);
        string wrongRootBundle = OtherRootOf(family) + Commit + "/bundle.json";
        PointerDescriptor d = fx.D with { BundlePaths = new[] { wrongRootBundle } };
        AssertReject(d, Committed(fx.Receipt, wrongRootBundle), "path schema: BUNDLE escapes family root");
    }

    // Tests INV-028 [unit] (F4 "under the FAMILY's fixed root", RS-029 root-escape): a pointer
    // whose target is under the OTHER family's root but still under the shared 'test/attestations/'
    // prefix → reject. This proves the FAMILY root is enforced, not merely the shared prefix
    // (a P3 pointer under the entry root, and an entry pointer under the inv010 root).
    [Theory]
    [InlineData(PointerFamily.P3ActiveBaseline)]
    [InlineData(PointerFamily.EntryEvidence)]
    public void Wrong_family_root_receipt_rejected(PointerFamily family)
    {
        Fixture fx = Valid(family);
        string wrongRoot = OtherRootOf(family);            // still under test/attestations/, wrong family
        string receipt = wrongRoot + Commit + "/receipt.json";
        string bundle = wrongRoot + Commit + "/bundle.json";
        PointerDescriptor d = fx.D with
        {
            ReceiptPaths = new[] { receipt },
            BundlePaths = new[] { bundle },
        };
        AssertReject(d, Committed(receipt, bundle), "path schema: escapes family root (cross-family)");
    }

    // Tests INV-028 [unit] (F4 root-escape): a target OUTSIDE the test/attestations/ tree entirely
    // → reject (does not start with the family fixed root).
    [Theory]
    [InlineData(PointerFamily.P3ActiveBaseline)]
    [InlineData(PointerFamily.EntryEvidence)]
    public void Outside_attestations_tree_rejected(PointerFamily family)
    {
        Fixture fx = Valid(family);
        string outside = "gate/attestations/" + Commit + "/receipt.json";
        PointerDescriptor d = fx.D with { ReceiptPaths = new[] { outside } };
        AssertReject(d, Committed(outside, fx.Bundle), "path schema: outside test/attestations/ tree");
    }

    // =====================================================================================
    // (E) NO SYMLINK — TargetIsSymlink == true fails closed (spec ~1137).
    // =====================================================================================

    // Tests INV-028 [unit] (F4 "no symlinks"): TargetIsSymlink==true → reject, with every other
    // dimension well-formed (so the symlink flag is the sole break).
    [Theory]
    [InlineData(PointerFamily.P3ActiveBaseline)]
    [InlineData(PointerFamily.EntryEvidence)]
    public void Symlink_target_rejected(PointerFamily family)
    {
        Fixture fx = Valid(family);
        PointerDescriptor d = fx.D with { TargetIsSymlink = true };
        AssertReject(d, fx.CommittedSet, "no symlink: TargetIsSymlink==true");
    }

    // =====================================================================================
    // (F) COMMIT-DIRECTORY AGREEMENT — path <commit> == attested_commit == on-disk dir, and the
    // bundle sits in the SAME <commit> dir (spec ~1139–1140). Each mutation breaks one leg only.
    // =====================================================================================

    // Tests INV-028 [unit] (F4 commit-dir agreement leg 1): the path's <commit> segment disagrees
    // with the receipt's declared attested_commit → reject. Path + on-disk stay in agreement, so
    // the attested_commit mismatch is the sole break.
    [Theory]
    [InlineData(PointerFamily.P3ActiveBaseline)]
    [InlineData(PointerFamily.EntryEvidence)]
    public void Commit_dir_disagrees_with_attested_commit_rejected(PointerFamily family)
    {
        Fixture fx = Valid(family);
        PointerDescriptor d = fx.D with { AttestedCommit = OtherCommit };
        AssertReject(d, fx.CommittedSet, "commit-dir: path <commit> != attested_commit");
    }

    // Tests INV-028 [unit] (F4 commit-dir agreement leg 2): the path's <commit> segment disagrees
    // with the on-disk directory-name segment → reject. Path + attested stay in agreement.
    [Theory]
    [InlineData(PointerFamily.P3ActiveBaseline)]
    [InlineData(PointerFamily.EntryEvidence)]
    public void Commit_dir_disagrees_with_ondisk_dir_rejected(PointerFamily family)
    {
        Fixture fx = Valid(family);
        PointerDescriptor d = fx.D with { OnDiskDirSegment = OtherCommit };
        AssertReject(d, fx.CommittedSet, "commit-dir: path <commit> != on-disk dir name");
    }

    // Tests INV-028 [unit] (F4 "bundle in the SAME <commit> dir"): the bundle lives in a DIFFERENT
    // <commit> directory than the receipt → reject. The other-dir bundle is itself well-formed +
    // committed, so the bundle-dir disagreement is the sole break.
    [Theory]
    [InlineData(PointerFamily.P3ActiveBaseline)]
    [InlineData(PointerFamily.EntryEvidence)]
    public void Bundle_in_different_commit_dir_rejected(PointerFamily family)
    {
        Fixture fx = Valid(family);
        string otherDirBundle = fx.Root + OtherCommit + "/bundle.json";
        PointerDescriptor d = fx.D with { BundlePaths = new[] { otherDirBundle } };
        AssertReject(d, Committed(fx.Receipt, otherDirBundle), "commit-dir: bundle in a different <commit> dir");
    }

    // =====================================================================================
    // (G) NO DANGLING (RS-029 half-applied refresh) — BOTH named receipt AND bundle must be in
    // the committed set (spec ~1130–1134, "Violated when: a pointer resolves to a missing target").
    // Everything else stays well-formed, so committed-set membership is the sole break.
    // =====================================================================================

    // Tests INV-028 [integration-boundary/unit] (RS-029): the named RECEIPT is absent from the
    // committed set (bundle present) — a pointer moved but its target receipt not yet committed →
    // reject fail-closed (append-only evidence never self-heals a dangling pointer).
    [Theory]
    [InlineData(PointerFamily.P3ActiveBaseline)]
    [InlineData(PointerFamily.EntryEvidence)]
    public void Dangling_receipt_absent_rejected(PointerFamily family)
    {
        Fixture fx = Valid(family);
        AssertReject(fx.D, Committed(fx.Bundle), "dangling (RS-029): receipt absent from committed set");
    }

    // Tests INV-028 [unit] (RS-029): the named BUNDLE is absent from the committed set (receipt
    // present) — a new versioned receipt committed but its bundle target missing → reject.
    [Theory]
    [InlineData(PointerFamily.P3ActiveBaseline)]
    [InlineData(PointerFamily.EntryEvidence)]
    public void Dangling_bundle_absent_rejected(PointerFamily family)
    {
        Fixture fx = Valid(family);
        AssertReject(fx.D, Committed(fx.Receipt), "dangling (RS-029): bundle absent from committed set");
    }

    // Tests INV-028 [unit] (RS-029): BOTH targets absent from the committed set → reject.
    [Theory]
    [InlineData(PointerFamily.P3ActiveBaseline)]
    [InlineData(PointerFamily.EntryEvidence)]
    public void Dangling_both_absent_rejected(PointerFamily family)
    {
        Fixture fx = Valid(family);
        AssertReject(fx.D, Committed(), "dangling (RS-029): both receipt and bundle absent");
    }

    // =====================================================================================
    // (H) TOTALITY / DENY-BY-DEFAULT — a maximally-broken descriptor and a null descriptor are
    // NEVER Valid (AP-001 deny-by-default at the boundary).
    // =====================================================================================

    // Tests INV-028 [unit] (deny-by-default): a descriptor violating EVERY rule at once (bad
    // cardinality, absolute+wrong-root paths, symlink, commit mismatch, nothing committed) → reject.
    [Theory]
    [InlineData(PointerFamily.P3ActiveBaseline)]
    [InlineData(PointerFamily.EntryEvidence)]
    public void All_invalid_descriptor_rejected(PointerFamily family)
    {
        var d = new PointerDescriptor(
            Family: family,
            ReceiptPaths: Array.Empty<string>(),                        // zero receipts
            BundlePaths: new[] { "/abs/one.json", "/abs/two.json" },    // two + absolute bundles
            AttestedCommit: "not-the-commit",                           // disagrees
            OnDiskDirSegment: "also-not-the-commit",                    // disagrees
            TargetIsSymlink: true);                                     // symlink
        AssertReject(d, Committed(), "totality: maximally-invalid descriptor");
    }

    // Tests INV-028 [unit] (deny-by-default): a NULL descriptor → reject (a total validator is
    // never permissive on a malformed call; it must not throw a raw NRE either).
    [Fact]
    public void Null_descriptor_rejected()
    {
        AssertReject(null, Committed(), "totality: null descriptor");
    }
}
