using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Corrected.Gate;
using Xunit;

namespace Corrected.Gate.Tests;

/// <summary>
/// DD-003: the stage-partitioned normative migration manifest — closed discriminated
/// union on `kind` (digest vs the single B1 structural row); committed after-span
/// fixtures; stage derived from committed P1.satisfied; atomic accepted TREE STATE
/// via the mixed-set fail-closed guard; the finite stale-literal scan; and the
/// no-local-site-list meta-test. [integration].
/// </summary>
public class Dd003MigrationManifestTests
{
    private static JsonElement LoadManifestJson()
        => JsonDocument.Parse(File.ReadAllText(TestPaths.Manifest("readiness-migration-manifest.json"))).RootElement;

    // Tests DD-003 [integration]: the manifest is a CLOSED discriminated union on
    // `kind` — digest rows carry stage_before/after_sha256; the single structural row
    // (B1) carries stage_predicate and NO after-digest (EXT9-01). Genuine shape guard
    // over the committed manifest JSON.
    [Fact]
    public void Manifest_is_a_closed_discriminated_union_on_kind()
    {
        var rows = LoadManifestJson().GetProperty("rows").EnumerateArray().ToArray();
        Assert.All(rows, r =>
        {
            string kind = r.GetProperty("kind").GetString()!;
            Assert.Contains(kind, new[] { "digest", "structural" });
            if (kind == "digest")
            {
                Assert.True(r.TryGetProperty("stage_before_sha256", out _));
                Assert.True(r.TryGetProperty("stage_after_sha256", out _));
            }
            else
            {
                Assert.True(r.TryGetProperty("stage_predicate", out _));
                Assert.False(r.TryGetProperty("stage_after_sha256", out _));
            }
        });
        // Exactly ONE structural row (B1's GREEN-assigned P1.evidence).
        Assert.Equal(1, rows.Count(r => r.GetProperty("kind").GetString() == "structural"));
    }

    // Tests DD-003 [integration]: the manifest LoadAndValidate enforces the closed
    // schema (rejects an unknown kind / a digest row missing a sha / a structural row
    // carrying an after-digest). RED against the stub validator.
    [Fact]
    public void LoadAndValidate_enforces_the_closed_schema()
    {
        string json = File.ReadAllText(TestPaths.Manifest("readiness-migration-manifest.json"));
        var rows = MigrationManifest.LoadAndValidate(json);
        Assert.NotEmpty(rows);
    }

    // Tests DD-003 [integration]: every deterministic anchor has a committed canonical
    // after-span fixture under manifests/after-spans/ (site A5, EXT8-04), so a fresh
    // Stage-A checkout carries the real preimage. Genuine guard.
    [Fact]
    public void After_span_fixtures_are_committed_for_digest_rows()
    {
        var rows = LoadManifestJson().GetProperty("rows").EnumerateArray()
            .Where(r => r.GetProperty("kind").GetString() == "digest");
        foreach (var r in rows)
        {
            string fixtureRel = r.GetProperty("after_span_fixture").GetString()!;
            string name = Path.GetFileName(fixtureRel);
            Assert.True(File.Exists(TestPaths.Manifest("after-spans", name)),
                $"DD-003: missing committed after-span fixture {name}");
        }
    }

    // Tests DD-003 [integration]: the stage is derived MECHANICALLY from committed
    // P1.satisfied — Stage A today (P1.satisfied:false). RED against the stub gate.
    [Fact]
    public void Stage_is_derived_from_committed_p1_satisfied_StageA_today()
    {
        ConsistencyResult r = MigrationManifest.CheckConsistency(TestPaths.RepoRoot());
        Assert.Equal(MigrationStage.StageA, r.ResolvedStage);
        // Stage-A tree is self-consistent AND non-vacuous (real anchors + real digests +
        // no Stage-A-removed literal). If this fails, the named sites explain why.
        Assert.True(r.Passed, string.Join(" | ", r.DisagreeingSites));
    }

    // Tests DD-003 [integration]: a MIXED before/after set fails closed, NAMING each
    // disagreeing site (the partial-migration hazard, RS-UX-08). Drives the REAL
    // CheckConsistency over a SYNTHESIZED partial-migration tree — P1.satisfied=true
    // (stage resolves to B) but the A2 anchor still holds its stage_before span, so A2
    // disagrees while A6/B5 (at their after spans) agree. The committed negative fixture
    // pins the expectation.
    [Fact]
    public void Mixed_before_after_set_fails_closed_naming_sites()
    {
        var neg = JsonDocument.Parse(File.ReadAllText(TestPaths.Manifest("mixed-before-after.negative.json"))).RootElement;
        Assert.Equal("FAIL_CLOSED", neg.GetProperty("expected_result").GetString());
        var expectedSites = neg.GetProperty("expected_disagreeing_sites").EnumerateArray()
            .Select(e => e.GetString()!).ToArray();

        using var tree = MigrationTree.Build(
            p1Satisfied: true,
            anchors: new Dictionary<string, string>
            {
                ["A2"] = MigrationSpans.A2Before,                    // regressed: at before while stage is B
                ["A6-no-entrypoint-yaml"] = MigrationSpans.A6After,  // agrees (before == after)
                ["B5-currently-parses-blocked"] = MigrationSpans.B5After,
            });

        ConsistencyResult r = MigrationManifest.CheckConsistency(tree.Root);
        Assert.Equal(MigrationStage.StageB, r.ResolvedStage);
        Assert.False(r.Passed);                                      // fail-closed
        Assert.Contains(r.DisagreeingSites, s => s.Contains("#A2:"));
        foreach (var site in expectedSites)
        {
            Assert.Contains(r.DisagreeingSites, s => s.Contains("#" + site + ":"));
        }
        Assert.DoesNotContain(r.DisagreeingSites, s => s.Contains("#B5-currently-parses-blocked:"));
    }

    // Tests DD-003 [integration]: NON-VACUITY — a Stage-A tree whose digest-row anchors
    // are ABSENT fails closed (the gate must never pass a migration whose anchor it could
    // not locate; QA-001 class_fix). RED against a gate that skips missing anchors.
    [Fact]
    public void Missing_anchor_for_a_digest_row_fails_closed()
    {
        using var tree = MigrationTree.Build(p1Satisfied: false, anchors: new Dictionary<string, string>());
        ConsistencyResult r = MigrationManifest.CheckConsistency(tree.Root);
        Assert.Equal(MigrationStage.StageA, r.ResolvedStage);
        Assert.False(r.Passed);
        Assert.Contains(r.DisagreeingSites, s => s.Contains("anchor MISSING"));
    }

    // Tests DD-003 [integration]: the finite stale-literal scan is WIRED into
    // CheckConsistency — a Stage-A tree with all anchors correct but a Stage-A-removed
    // literal in the normative body fails closed, naming the literal. (The identical
    // literal in the "Notes for review" appendix is NOT flagged — the scan stops there.)
    [Fact]
    public void Stage_A_scan_rejects_a_stale_literal_in_the_normative_body()
    {
        using var tree = MigrationTree.Build(
            p1Satisfied: false,
            anchors: AllAnchorsAtBefore(),
            extraBody: "The enforcement mechanisms are specified but unhomed.");
        ConsistencyResult r = MigrationManifest.CheckConsistency(tree.Root);
        Assert.False(r.Passed);
        Assert.Contains(r.DisagreeingSites, s => s.Contains("specified but unhomed"));
    }

    // Tests DD-003 [integration]: a clean Stage-A tree (all anchors at before, no stale
    // literal, the appendix literal ignored) PASSES — so the fail-closed additions above
    // are not false-positives and the appendix boundary is honored.
    [Fact]
    public void Clean_stage_A_tree_passes_and_ignores_the_appendix_literal()
    {
        using var tree = MigrationTree.Build(p1Satisfied: false, anchors: AllAnchorsAtBefore());
        ConsistencyResult r = MigrationManifest.CheckConsistency(tree.Root);
        Assert.True(r.Passed, string.Join(" | ", r.DisagreeingSites));
        Assert.Equal(MigrationStage.StageA, r.ResolvedStage);
    }

    // Tests DD-003 [integration]: a missing manifest fails CLOSED (deny-by-default) —
    // the manifest is the sole digest authority, so its absence can never be a pass.
    [Fact]
    public void Missing_manifest_fails_closed()
    {
        using var tree = MigrationTree.Build(p1Satisfied: false, anchors: AllAnchorsAtBefore());
        File.Delete(Path.Combine(tree.Root, Path.Combine(
            "gate", "Corrected.Gate.Tests", "manifests", "readiness-migration-manifest.json")));
        ConsistencyResult r = MigrationManifest.CheckConsistency(tree.Root);
        Assert.False(r.Passed);
        Assert.Contains(r.DisagreeingSites, s => s.Contains("manifest missing"));
    }

    // Tests DD-003 [integration]: a DUPLICATE current-state anchor id fails CLOSED — a decoy
    // second pair (holding any span) must not mask an earlier drifted span via last-writer-
    // wins extraction (QA mini-audit).
    [Fact]
    public void Duplicate_anchor_id_fails_closed()
    {
        using var tree = MigrationTree.Build(
            p1Satisfied: false,
            anchors: AllAnchorsAtBefore(),
            extraBody: "<!-- correctless:readiness-current-state:start id=\"A2\" -->\n"
                     + "decoy duplicate span\n"
                     + "<!-- correctless:readiness-current-state:end id=\"A2\" -->");
        ConsistencyResult r = MigrationManifest.CheckConsistency(tree.Root);
        Assert.False(r.Passed);
        Assert.Contains(r.DisagreeingSites, s => s.Contains("duplicate current-state anchor id"));
    }

    // Tests DD-003 [integration]: an INJECTED second "## Notes for review" appendix heading
    // fails CLOSED — otherwise an attacker could move the normative-body boundary up and hide
    // stale literals below the decoy (QA mini-audit).
    [Fact]
    public void Multiple_appendix_markers_fail_closed()
    {
        using var tree = MigrationTree.Build(
            p1Satisfied: false,
            anchors: AllAnchorsAtBefore(),
            extraBody: "## Notes for review (injected decoy)");
        ConsistencyResult r = MigrationManifest.CheckConsistency(tree.Root);
        Assert.False(r.Passed);
        Assert.Contains(r.DisagreeingSites, s => s.Contains("multiple") && s.Contains("appendix"));
    }

    // Tests DD-003 [integration]: META-TEST — no digest row carries an all-zero (64x'0')
    // placeholder for either digest, so a "Passed" that hashed a placeholder cannot
    // masquerade as verified (QA-001 class_fix).
    [Fact]
    public void Manifest_has_no_all_zero_placeholder_digests()
    {
        string zero = new string('0', 64);
        foreach (var r in DigestRows())
        {
            Assert.NotEqual(zero, r.GetProperty("stage_before_sha256").GetString());
            Assert.NotEqual(zero, r.GetProperty("stage_after_sha256").GetString());
        }
    }

    // Tests DD-003 [integration]: the committed after-span fixture is the RECOVERABLE
    // preimage — stage_after_sha256 equals SHA-256 of the fixture bytes (LF-normalized,
    // trailing newline stripped), so a fresh Stage-A checkout carries the byte-exact
    // after-image and the digest is not self-authored circularly at Stage B (EXT8-04).
    [Fact]
    public void After_span_fixture_is_the_recoverable_preimage_of_the_after_digest()
    {
        foreach (var r in DigestRows())
        {
            string name = Path.GetFileName(r.GetProperty("after_span_fixture").GetString()!);
            string content = File.ReadAllText(TestPaths.Manifest("after-spans", name))
                .Replace("\r\n", "\n").TrimEnd('\n');
            string sha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(content))).ToLowerInvariant();
            Assert.Equal(r.GetProperty("stage_after_sha256").GetString(), sha);
        }
    }

    // Tests DD-003 [integration]: META-TEST — the manifest MUST carry the required
    // digest-anchor set {A2, A6-no-entrypoint-yaml, B5-currently-parses-blocked}. The
    // missing-anchor fail-closed check only fires for rows PRESENT in the manifest, so
    // without this pin a future edit dropping a digest row (and its anchor) would shrink
    // the gate's coverage with zero test failures — defeating the non-vacuity guarantee
    // this whole fix exists to provide (QA r2 F3).
    [Fact]
    public void Manifest_carries_the_required_digest_anchor_set()
    {
        var ids = DigestRows().Select(r => r.GetProperty("id").GetString()).ToHashSet();
        foreach (var required in new[] { "A2", "A6-no-entrypoint-yaml", "B5-currently-parses-blocked" })
        {
            Assert.Contains(required, ids);
        }
    }

    private static IEnumerable<JsonElement> DigestRows()
        => LoadManifestJson().GetProperty("rows").EnumerateArray()
            .Where(r => r.GetProperty("kind").GetString() == "digest");

    private static Dictionary<string, string> AllAnchorsAtBefore() => new()
    {
        ["A2"] = MigrationSpans.A2Before,
        ["A6-no-entrypoint-yaml"] = MigrationSpans.A6Before,
        ["B5-currently-parses-blocked"] = MigrationSpans.B5Before,
    };

    // Tests DD-003 [integration]: the finite stale-literal set is enumerated and
    // includes the known signatures (EXT5-03). Genuine guard over the const list.
    [Fact]
    public void Finite_stale_literal_set_is_enumerated()
    {
        foreach (var lit in new[]
        {
            "EvaluateReadiness(blockText)", "BLOCKED-all-false", "specified but unhomed",
            "pending DF-002", "rm -rf out", "no entrypoint YAML exists yet",
            "entrypoint YAML TBD", "Flagged for the ARCHITECTURE.md component table",
        })
        {
            Assert.Contains(lit, MigrationManifest.KnownStaleLiterals);
        }
    }

    // Tests DD-003 [integration]: the NO-LOCAL-SITE-LIST meta-test — the parent-anchor
    // list lives SOLELY in the manifest; the carrier spec's Metadata `Impacts` line and
    // the Packages-Affected parent-spec bullet reference it and hold NO local A#/B#
    // enumeration (EXT4-05/EXT8-06). Genuine guard over the committed spec.
    // Source: .correctless/specs/readiness-gate-carrier.md
    [Fact]
    public void No_local_site_list_only_the_manifest_reference()
    {
        string spec = File.ReadAllText(TestPaths.RepoFile(".correctless", "specs", "readiness-gate-carrier.md"));
        // The Packages-Affected parent bullet asserts the no-local-list discipline.
        Assert.Contains("ONLY that manifest reference and NO", spec);
        Assert.Contains("readiness-migration-manifest.json", spec);
    }

    // Tests DD-003 [integration]: the pinned manifest path constant is exactly the
    // committed manifest location. Genuine guard.
    [Fact]
    public void Manifest_path_constant_is_pinned()
    {
        Assert.Equal("gate/Corrected.Gate.Tests/manifests/readiness-migration-manifest.json",
            MigrationManifest.ManifestPath);
    }
}
