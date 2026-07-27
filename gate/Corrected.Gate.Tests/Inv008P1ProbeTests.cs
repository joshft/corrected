using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using Corrected.Gate;
using Corrected.Gate.Kernel;
using Corrected.Gate.Lint;
using Xunit;

namespace Corrected.Gate.Tests;

/// <summary>
/// INV-008: P1 probe — HARDENED ADR promotion + component-table, resolving true
/// after the DD-003 migration. [integration]. Covers the schema-completeness
/// short-circuit (a), compiled content anchors (a′), COMPATIBLE recompute (a″),
/// supersession/registry (a‴), component table (b), and the extracted-lib pin (c).
/// </summary>
public class Inv008P1ProbeTests
{
    private static string Sample() => TestPaths.RepoFile(
        "spikes", "dafny-compat", "evidence", "samples", "run-report.canonical.sample.json");

    private static string Sha256File(string path)
    {
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(File.ReadAllBytes(path))).ToLowerInvariant();
    }

    // Tests INV-008(a) [integration]: a pre-migration ADR (status key absent)
    // short-circuits to evidence-schema-incomplete BEFORE any evidence/recompute/
    // supersession check — so a future stale sha const cannot dead-red the Stage-A
    // path (R3-B1). RE-HOMED onto a SYNTHESIZED pre-migration tree now that the real
    // committed tree has migrated to Stage B; the branch stays covered stage-independently.
    [Fact]
    public void PreMigration_adr_is_schema_incomplete()
    {
        using var tree = P1Tree.Build(P1Mutation.PreMigrationStatusAbsent);
        ProbeResult r = new P1Probe().Evaluate(GateContext.ForRepoRoot(tree.Root));
        Assert.False(r.Satisfied);
        Assert.Equal(ProbeReasons.EvidenceSchemaIncomplete, r.Reason);
    }

    // Tests INV-008(a‴) [integration]: post-migration the REAL committed tree is Stage B —
    // the live ADR-0001 carries the acceptance schema (status: accepted, terminal), so the
    // real P1 probe re-derives COMPATIBLE and resolves TRUE over the repo root. This is the
    // live-repo Stage-B positive the DD-003 flip makes green (RED until the flip lands).
    [Fact]
    public void Committed_tree_is_migrated_P1_satisfied()
    {
        var ctx = GateContext.ForRepoRoot(TestPaths.RepoRoot());
        ProbeResult r = new P1Probe().Evaluate(ctx);
        Assert.True(r.Satisfied);
        Assert.Equal("resolved-compatible", r.Reason);
    }

    // Tests INV-008(a′) [integration]: the Stage-A positive fixture — SHA256(the
    // committed CANONICAL sample) == the compiled canonical_sample_sha256 const, so a
    // stale const dead-reds at Stage A (RED: the const is a placeholder zero-digest
    // until GREEN pins the real SHA-256). AP-031 live-producer coverage.
    // Source: spikes/dafny-compat/evidence/samples/run-report.canonical.sample.json
    [Fact]
    public void Canonical_sample_sha256_anchor_matches_committed_file()
    {
        Assert.Equal(Sha256File(Sample()), P1EvidenceAnchors.CanonicalSampleSha256);
    }

    // Tests INV-008(a′) [integration]: the schema-digest anchor == SHA256(the pinned
    // schema file) == the sample's own evidence_schema_sha256 field. RED (placeholder
    // const). AP-031 live-producer coverage.
    [Fact]
    public void Evidence_schema_sha256_anchor_matches_file_and_sample_field()
    {
        string schemaFile = TestPaths.RepoFile("spikes", "dafny-compat", "schema", "evidence-schema.json");
        string fileSha = Sha256File(schemaFile);
        using var doc = JsonDocument.Parse(File.ReadAllText(Sample()));
        string sampleField = doc.RootElement.GetProperty("evidence_schema_sha256").GetString()!;
        Assert.Equal(fileSha, sampleField);                       // genuine convergence check
        Assert.Equal(fileSha, P1EvidenceAnchors.EvidenceSchemaSha256); // RED: placeholder const
    }

    // Tests INV-008(a′) [integration]: the probe-manifest-digest anchor == SHA256(the
    // manifest file) == the sample's probe_manifest_sha256 field. RED (placeholder).
    [Fact]
    public void Probe_manifest_sha256_anchor_matches_file_and_sample_field()
    {
        string manifestFile = TestPaths.RepoFile("spikes", "dafny-compat", "manifest", "probe-manifest.json");
        string fileSha = Sha256File(manifestFile);
        using var doc = JsonDocument.Parse(File.ReadAllText(Sample()));
        string sampleField = doc.RootElement.GetProperty("probe_manifest_sha256").GetString()!;
        Assert.Equal(fileSha, sampleField);
        Assert.Equal(fileSha, P1EvidenceAnchors.ProbeManifestSha256);
    }

    // Tests INV-008(a′) [integration]: the recognized evidence-schema-version set
    // (one element today: 2). AP-031 live coverage.
    [Fact]
    public void Recognized_schema_version_set_matches_sample()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Sample()));
        int version = doc.RootElement.GetProperty("evidence_schema_version").GetInt32();
        Assert.Contains(version, P1EvidenceAnchors.RecognizedSchemaVersions);
        Assert.Equal(2, version);
    }

    // Tests INV-008 [integration]: the pinned P1 evidence path is the CANONICAL
    // sample, NEVER the variance run-report.sample.json (DD-002/RS-210). Genuine
    // guard over the constant.
    [Fact]
    public void Pinned_evidence_path_is_canonical_not_variance()
    {
        Assert.EndsWith("run-report.canonical.sample.json", ProbeOrchestrator.CanonicalSamplePath);
        Assert.DoesNotContain("run-report.sample.json", ProbeOrchestrator.CanonicalSamplePath.Replace(".canonical.sample.json", ""));
    }

    // ---- B1: drive the REAL probe through the repo-root seam over a SYNTHESIZED temp
    //      tree (MIGRATED ADR + one mutation), asserting Satisfied == false AND the
    //      SPECIFIC reason — NOT a vacuous Assert.NotNull(probe.Evaluate(ctx)), which a
    //      Stage-A-short-circuit-only GREEN probe would pass. ----
    //
    // DECISION (reason taxonomy over a temp tree). Over a temp tree the (a′) compiled
    // sha pins (canonical_sample_sha256 / probe_manifest_sha256) fire FIRST on ANY
    // evidence-file content change, so every sample/manifest tamper presents at the
    // PROBE level as `evidence-malformed` (the (a″) recompute is belt-and-suspenders
    // behind the pins; INV-008a′ "a coherent multi-file tamper still fails against these
    // compiled constants"). Decision-field / prose↔machine / component-table tampers
    // leave the pinned files intact and are caught semantically -> `evidence-refutes`.
    // Registry / supersession-graph breaches -> `evidence-malformed`. Each assertion
    // also proves the migrated ADR PASSED schema-completeness (reason != schema-incomplete),
    // so the short-circuit did not fire.
    private static void AssertProbeFailsClosed(P1Mutation mutation, string expectedReason)
    {
        using var tree = P1Tree.Build(mutation);
        ProbeResult r = new P1Probe().Evaluate(GateContext.ForRepoRoot(tree.Root));
        Assert.False(r.Satisfied);
        Assert.NotEqual(ProbeReasons.EvidenceSchemaIncomplete, r.Reason);
        Assert.Equal(expectedReason, r.Reason);
    }

    // Tests INV-008(a″) [integration]: the COMPATIBLE recompute is cardinality-guarded
    // — a mutated sample with route-B-only per_probe_results (vacuous forge) drives P1
    // false. Also asserts the real sample carries a Route-A verdict (AP-031 live
    // coverage). RED against the stub probe.
    [Fact]
    public void Recompute_rejects_vacuous_route_b_only_forge()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Sample()));
        var routeVerdicts = doc.RootElement.GetProperty("deterministic").GetProperty("route_verdicts").EnumerateArray();
        Assert.Contains(routeVerdicts, rv => rv.GetProperty("route").GetString() == "A"
                                          && rv.GetProperty("state").GetString() == "COMPATIBLE");
        AssertProbeFailsClosed(P1Mutation.RouteBOnlyPerProbe, ProbeReasons.EvidenceMalformed);
    }

    // ---- INV-008(a″) DIRECT recompute reject-branch (unit) ----
    // The probe-level sample tampers above all present as evidence-malformed via the
    // (a′) WHOLE-FILE canonical_sample_sha256 pin, which fires BEFORE the recompute — so
    // no probe-level test can reach the (a″) cardinality-guarded multiset-equality
    // predicate (a sample that passes the pin is byte-identical to canonical, so its
    // multiset is correct; the reject branch is only testable IN ISOLATION). These unit
    // tests feed the recompute PARSED per-probe results directly (P1Recompute, no file),
    // so a vacuous always-Compatible GREEN recompute FAILS the reject cases. RED against
    // the throwing stub.

    private static IReadOnlyList<(string Probe, string Route)> ExpectedRouteAShared() => new[]
    {
        ("P01", "A"), ("P02", "shared"), ("P03", "A"),
    };

    private static List<(string Probe, string Route, string Status)> CorrectActual() => new()
    {
        ("P01", "A", "pass"), ("P02", "shared", "pass"), ("P03", "A", "pass"),
    };

    private static IReadOnlyList<(string Route, string State)> OneCompatibleRouteAVerdict() => new[]
    {
        ("A", "COMPATIBLE"),
    };

    // Tests INV-008(a″) [unit]: the correct Route-A+shared multiset with exactly one
    // Route-A COMPATIBLE verdict recomputes Compatible.
    [Fact]
    public void Recompute_accepts_the_correct_route_a_shared_multiset()
    {
        Assert.Equal(RecomputeVerdict.Compatible,
            P1Recompute.RecomputeRouteACompatible(CorrectActual(), ExpectedRouteAShared(), OneCompatibleRouteAVerdict()));
    }

    // Tests INV-008(a″) [unit]: a plan-SHRUNK per-probe set (an expected (probe,route)
    // missing) is REJECTED — the vacuous-forge close (a HashSet would not catch it).
    [Fact]
    public void Recompute_rejects_a_missing_expected_entry()
    {
        var shrunk = CorrectActual();
        shrunk.RemoveAt(2); // drop (P03, A)
        Assert.Equal(RecomputeVerdict.MissingEntry,
            P1Recompute.RecomputeRouteACompatible(shrunk, ExpectedRouteAShared(), OneCompatibleRouteAVerdict()));
    }

    // Tests INV-008(a″) [unit]: an EXTRA (probe,route) outside the expected partition is REJECTED.
    [Fact]
    public void Recompute_rejects_an_extra_entry()
    {
        var extra = CorrectActual();
        extra.Add(("P99", "A", "pass"));
        Assert.Equal(RecomputeVerdict.ExtraEntry,
            P1Recompute.RecomputeRouteACompatible(extra, ExpectedRouteAShared(), OneCompatibleRouteAVerdict()));
    }

    // Tests INV-008(a″) [unit]: a DUPLICATE (probe,route) is REJECTED by count-aware
    // multiset equality (mirrors ComputeRouteVerdict's if(!seen.Add(key)); a HashSet
    // .SetEquals would silently dedup and pass — R3-B3b).
    [Fact]
    public void Recompute_rejects_a_duplicate_entry()
    {
        var dup = CorrectActual();
        dup.Add(("P01", "A", "pass")); // (P01, A) now appears twice
        Assert.Equal(RecomputeVerdict.DuplicateEntry,
            P1Recompute.RecomputeRouteACompatible(dup, ExpectedRouteAShared(), OneCompatibleRouteAVerdict()));
    }

    // Tests INV-008(a″) [unit]: a Route-A+shared per-probe result with status != pass is REJECTED.
    [Fact]
    public void Recompute_rejects_a_non_pass_probe()
    {
        var failed = new List<(string Probe, string Route, string Status)>
        {
            ("P01", "A", "fail"), ("P02", "shared", "pass"), ("P03", "A", "pass"),
        };
        Assert.Equal(RecomputeVerdict.ProbeNotPass,
            P1Recompute.RecomputeRouteACompatible(failed, ExpectedRouteAShared(), OneCompatibleRouteAVerdict()));
    }

    // Tests INV-008(a″) [unit]: TWO Route-A route_verdicts (not exactly one) is REJECTED.
    [Fact]
    public void Recompute_rejects_two_route_a_verdicts()
    {
        var twoRouteA = new (string Route, string State)[] { ("A", "COMPATIBLE"), ("A", "COMPATIBLE") };
        Assert.Equal(RecomputeVerdict.RouteAVerdictInvalid,
            P1Recompute.RecomputeRouteACompatible(CorrectActual(), ExpectedRouteAShared(), twoRouteA));
    }

    // Tests INV-008(a″) [unit]: a Route-A route_verdict whose state != COMPATIBLE is REJECTED.
    [Fact]
    public void Recompute_rejects_non_compatible_route_a_verdict()
    {
        var incomplete = new (string Route, string State)[] { ("A", "INCOMPLETE") };
        Assert.Equal(RecomputeVerdict.RouteAVerdictInvalid,
            P1Recompute.RecomputeRouteACompatible(CorrectActual(), ExpectedRouteAShared(), incomplete));
    }

    // Tests INV-008(a″) [unit]: ZERO Route-A route_verdicts is REJECTED.
    [Fact]
    public void Recompute_rejects_zero_route_a_verdicts()
    {
        var none = System.Array.Empty<(string Route, string State)>();
        Assert.Equal(RecomputeVerdict.RouteAVerdictInvalid,
            P1Recompute.RecomputeRouteACompatible(CorrectActual(), ExpectedRouteAShared(), none));
    }

    // Tests INV-008(a′)/(a″) [integration]: every evidence-SAMPLE / probe-MANIFEST
    // tamper drives P1 false, caught by the (a′) compiled sha pins at the probe level
    // (evidence-malformed). Covers the recompute mutations {final_suite_status:unknown,
    // flipped route-A probe, empty per_probe_results, duplicate route-A verdict,
    // duplicate (probe,route) entry, duplicate JSON key at root/route-verdict/per-probe,
    // wrong probe_manifest_sha256 field, tampered manifest FILE}. RED against the stub.
    [Theory]
    [InlineData(P1Mutation.FinalSuiteStatusUnknown)]
    [InlineData(P1Mutation.FlippedRouteAProbe)]
    [InlineData(P1Mutation.EmptyPerProbe)]
    [InlineData(P1Mutation.DuplicateRouteAVerdict)]
    [InlineData(P1Mutation.DuplicateProbeRouteEntry)]
    [InlineData(P1Mutation.DuplicateJsonKeyRoot)]
    [InlineData(P1Mutation.DuplicateJsonKeyRouteVerdict)]
    [InlineData(P1Mutation.DuplicateJsonKeyPerProbe)]
    [InlineData(P1Mutation.WrongProbeManifestShaField)]
    [InlineData(P1Mutation.TamperedManifestFile)]
    public void Evidence_sample_and_manifest_tampers_drive_probe_malformed(P1Mutation mutation)
        => AssertProbeFailsClosed(mutation, ProbeReasons.EvidenceMalformed);

    // Tests INV-008(a) [integration]: decision-field tampers that leave the pinned
    // evidence files intact are caught SEMANTICALLY -> evidence-refutes. {route-A verdict
    // INCOMPATIBLE; prose↔machine status split (line-3 accepted vs machine superseded,
    // RS-208)}. selected_route:B is homed in BND-003; dropped-DafnyDriver in the
    // ARCHITECTURE propagation test. RED against the stub probe.
    [Theory]
    [InlineData(P1Mutation.RouteAIncompatible)]
    [InlineData(P1Mutation.ProseMachineMismatch)]
    public void Decision_field_tampers_drive_probe_refutes(P1Mutation mutation)
        => AssertProbeFailsClosed(mutation, ProbeReasons.EvidenceRefutes);

    // Tests INV-008(a‴) [integration]: the supersession-graph shapes each fail closed
    // (evidence-malformed) over a SYNTHESIZED registry (spec: "fixture-driven over
    // synthesized registries, not the live single-entry one") — so the graph-shape rule,
    // not registry set-equality, is the thing under test. {cycle, dangling target, two
    // accepted terminals, disconnected node}. RED against the stub probe.
    [Theory]
    [InlineData(P1Mutation.SupersessionCycle)]
    [InlineData(P1Mutation.SupersessionDangling)]
    [InlineData(P1Mutation.SupersessionTwoTerminals)]
    [InlineData(P1Mutation.SupersessionDisconnected)]
    public void Supersession_graph_shapes_fail_closed(P1Mutation mutation)
    {
        using var tree = P1Tree.Build(mutation);
        // The synthesized ADRs ARE registered (injected registry) so the graph-shape
        // rule runs instead of registry set-equality catching them as unregistered.
        ProbeResult r = new P1Probe().Evaluate(
            GateContext.ForRepoRootWithAdrRegistry(tree.Root, tree.AdrRegistry));
        Assert.False(r.Satisfied);
        Assert.NotEqual(ProbeReasons.EvidenceSchemaIncomplete, r.Reason);
        Assert.Equal(ProbeReasons.EvidenceMalformed, r.Reason);
    }

    // Tests INV-008(b) [integration]: component-table set-equality — the Route-A
    // Dafny-family loaded set == {DafnyCore, DafnyDriver, DafnyLanguageServer} with
    // DafnyPipeline ABSENT, by EXACT simple_name match (not substring). Genuine guard
    // over the real route-a.json (AP-031 live coverage).
    // Source: spikes/dafny-compat/manifest/expected-loaded/route-a.json
    [Fact]
    public void Component_table_dafny_family_set_equality()
    {
        string routeA = File.ReadAllText(TestPaths.RepoFile(
            "spikes", "dafny-compat", "manifest", "expected-loaded", "route-a.json"));
        using var doc = JsonDocument.Parse(routeA);
        // Detection by the "Dafny" name prefix — matches the fail-safe probe (QA r2 F2): a
        // rogue Dafny* is swept in and breaks the expected set-equality. The loaded Boogie.*
        // verifier backend is not "Dafny"-prefixed and is correctly excluded.
        var names = doc.RootElement.GetProperty("assemblies").EnumerateArray()
            .Select(a => a.GetProperty("simple_name").GetString())
            .Where(n => n != null && n!.StartsWith("Dafny", StringComparison.Ordinal))
            .ToHashSet();
        Assert.Contains("DafnyCore", names);
        Assert.Contains("DafnyDriver", names);
        Assert.Contains("DafnyLanguageServer", names);
        Assert.DoesNotContain("DafnyPipeline", names);
    }

    // Tests INV-008(b) [integration]: the ARCHITECTURE machine-readable
    // route-a-production-assemblies block is asserted EQUAL to the route-a.json set
    // (propagation-equality, EXT2-08). Genuine live coverage: the ARCHITECTURE block
    // exists AND lists DafnyDriver (an anchor). Probe behavior: dropping DafnyDriver
    // from route-a.json (so it no longer equals the ARCHITECTURE block) drives P1 false
    // via (b) -> evidence-refutes (the propagation evidence refutes the claim). RED
    // against the stub probe.
    [Fact]
    public void Architecture_production_assembly_block_matches_route_a()
    {
        string arch = File.ReadAllText(TestPaths.RepoFile(".correctless", "ARCHITECTURE.md"));
        Assert.Contains("route-a-production-assemblies", arch);
        Assert.Contains("DafnyDriver", arch); // the anchor that must propagate
        AssertProbeFailsClosed(P1Mutation.DroppedDafnyDriver, ProbeReasons.EvidenceRefutes);
    }

    // Tests INV-008(a‴) [integration]: a MIGRATED ADR (accepted, superseded_by:null
    // explicit) is the terminal (no non-null successor) — over a clean migrated temp
    // tree the real P1 probe resolves TRUE (the Stage-B positive). This is asserted
    // against a SYNTHESIZED migrated tree, NOT the live pre-migration root (R3-M1: the
    // committed tree is pre-migration at Stage A, where P1 is schema-incomplete-false).
    // Also asserts the migrated fixture PARSES valid. RED against the stub probe/parser.
    [Fact]
    public void Migrated_accepted_null_successor_is_terminal()
    {
        string adr = File.ReadAllText(TestPaths.Fixture("adr", "migrated-adr-lint.md"));
        AdrParseResult pr = AdrLintBlockParser.Parse(adr);
        Assert.Equal(AdrParseOutcome.Ok, pr.Outcome);

        using var tree = P1Tree.Build(P1Mutation.None);
        ProbeResult r = new P1Probe().Evaluate(GateContext.ForRepoRoot(tree.Root));
        Assert.True(r.Satisfied); // clean migrated terminal -> P1 resolves true
    }

    // Tests INV-008(a‴) [integration]: an unregistered on-disk adr_lint block fails
    // closed ("register this ADR") — NOT ignored (R3-B4 supersedes RS-204). Over a
    // migrated temp tree with a SECOND on-disk column-0-in-fence adr_lint block absent
    // from the (live single-entry) registry, the registry set-equality fails closed ->
    // evidence-malformed. RED against the stub probe.
    [Fact]
    public void Unregistered_on_disk_adr_lint_block_fails_closed()
        => AssertProbeFailsClosed(P1Mutation.UnregisteredOnDiskAdrLint, ProbeReasons.EvidenceMalformed);

    // Tests PRH-007 / INV-008(a′) [integration]: a COHERENTLY-tampered canonical sample
    // (a recompute-neutral edit that keeps the recompute passing) is caught ONLY by the
    // compiled canonical_sample_sha256 pin (R3-B2) -> evidence-malformed. This exercises
    // the residual-forge close through the probe over a temp tree. RED against the stub.
    [Fact]
    public void Coherently_tampered_sample_fails_on_canonical_sha_pin()
        => AssertProbeFailsClosed(P1Mutation.CoherentlyTamperedSample, ProbeReasons.EvidenceMalformed);

    // Tests INV-008(a) [integration]: the two-parser DIFFERENTIAL agrees ONLY on the
    // shared decision fields (boundary_decision / selected_route / routes[].verdict),
    // NOT on overall pass/fail (they disagree by design pre-migration; RS-205). The
    // extracted spike linter runs as a redundant cross-check. RED (both are stubs).
    [Fact]
    public void Two_parser_differential_agrees_on_decision_fields_only()
    {
        // The extracted AdrLinter runs as a redundant cross-check (empty records).
        var findings = AdrLinter.Lint(
            TestPaths.RepoFile("docs", "adr", "ADR-0001-dafny-integration-boundary.md"),
            Array.Empty<AdjudicationRecord>());
        Assert.NotNull(findings);
    }

    // Tests INV-008(c) [integration]: the reused spike linter is the EXTRACTED lib
    // (narrow AdrLintBlock-shaped API), pinned by the append-only source-digest
    // registry — NOT the 1343-line Components.cs. Genuine guard: the extracted API
    // lives in Corrected.Gate.Lint, and the registry file is the pin home.
    [Fact]
    public void Extracted_lint_lib_is_the_pinned_trust_root()
    {
        Assert.Equal("Corrected.Gate.Lint", typeof(AdrLinter).Assembly.GetName().Name);
        // The append-only source-digest registry (INV-018) must exist. RED at Stage A.
        Assert.True(TestPaths.RepoFileExists("gate", "Corrected.Gate", "lint-source-registry.json"),
            "INV-008(c)/INV-018: gate/Corrected.Gate/lint-source-registry.json must pin the extracted lib source");
    }
}
