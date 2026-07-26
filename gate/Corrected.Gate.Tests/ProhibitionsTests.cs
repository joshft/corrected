using System;
using System.Collections.Generic;
using System.IO;
using Corrected.Gate;
using Corrected.Gate.Kernel;
using Xunit;

namespace Corrected.Gate.Tests;

/// <summary>
/// PRH-001..007 — the prohibitions. Each has at least one failing test exercising
/// the named detection mechanism.
/// </summary>
public class ProhibitionsTests
{
    private static ReadinessBlock BlockP1(bool satisfied, string? evidence, ReadinessStatus status = ReadinessStatus.BLOCKED)
    {
        var pcs = new List<ReadinessPrecondition>
        {
            ReadinessPrecondition.Create(PreconditionId.P1, "p1", satisfied, evidence, Array.Empty<string>()),
            ReadinessPrecondition.Create(PreconditionId.P2, "p2", false, null, Array.Empty<string>()),
            ReadinessPrecondition.Create(PreconditionId.P3, "p3", false, null, Array.Empty<string>()),
        };
        return ReadinessBlock.TryCreate(1, status, "P1 AND P2 AND P3", pcs)!;
    }

    private static IReadOnlyDictionary<PreconditionId, ProbeResult> Probes(bool p1Actual, ReferenceResolution rr)
        => new Dictionary<PreconditionId, ProbeResult>
        {
            [PreconditionId.P1] = ProbeResult.TryCreate(p1Actual, "probe", rr)!,
            [PreconditionId.P2] = ProbeResult.TryCreate(false, ProbeReasons.ValidatorDeferred, ReferenceResolution.Resolved)!,
            [PreconditionId.P3] = ProbeResult.TryCreate(false, ProbeReasons.ValidatorDeferred, ReferenceResolution.Resolved)!,
        };

    // Tests PRH-001 [unit]: never trust `satisfied` without the actual probe verdict
    // AND reference resolution — satisfied:true + unregistered/unresolvable evidence
    // -> Fail. RED against the stub kernel.
    [Fact]
    public void PRH001_satisfied_true_with_unresolvable_evidence_is_Fail()
    {
        var v = ReadinessGate.EvaluateReadiness(
            BlockP1(true, "unregistered", ReadinessStatus.READY),
            Probes(true, ReferenceResolution.Unresolvable));
        Assert.Equal(VerdictKind.Fail, v.Kind);
    }

    // Tests PRH-002 [integration]: no gate/** source is linked into a shipped project
    // (the gate becomes production / launders policy). Genuine guard: src/ carries no
    // project referencing gate/**; and the shipped-closure partition is the enforcer.
    [Fact]
    public void PRH002_no_gate_source_linked_into_a_shipped_project()
    {
        string srcDir = TestPaths.RepoFile("src");
        if (Directory.Exists(srcDir))
        {
            foreach (var csproj in Directory.EnumerateFiles(srcDir, "*.csproj", SearchOption.AllDirectories))
            {
                Assert.DoesNotContain("gate/", File.ReadAllText(csproj));
            }
        }
        // PAT-005 is registered by this feature in ARCHITECTURE.
        Assert.Contains("PAT-005", File.ReadAllText(TestPaths.RepoFile(".correctless", "ARCHITECTURE.md")));
    }

    // Tests PRH-003 [unit]: the parser never materializes a tag, anchor, alias, or
    // multi-doc — for the readiness block. Each yields indeterminate (never a gadget).
    // RED against the stub parser.
    [Theory]
    [InlineData("```yaml\nimplementation_readiness: &a\n  status: BLOCKED\n```")]            // anchor
    [InlineData("```yaml\nimplementation_readiness:\n  status: *a\n```")]                    // alias
    [InlineData("```yaml\nimplementation_readiness:\n  status: BLOCKED\n---\nx: 1\n```")]    // multi-doc
    public void PRH003_tag_anchor_alias_multidoc_yield_indeterminate(string yaml)
    {
        ReadinessBlock block = ReadinessBlockParser.Parse(yaml);
        Assert.Equal(ReadinessStatus.Indeterminate, block.Status);
    }

    // Tests PRH-004 [unit]: the committed readiness block is never READY while any
    // precondition is unmet. Genuine guard over the real committed parent (Stage A:
    // status BLOCKED).
    // Source: .correctless/specs/phase-0-1-worker.md
    [Fact]
    public void PRH004_committed_block_is_not_READY()
    {
        string parent = File.ReadAllText(TestPaths.RepoFile(".correctless", "specs", "phase-0-1-worker.md"));
        // The committed block declares status BLOCKED (never READY while unmet).
        Assert.Contains("status: BLOCKED", parent);
    }

    // Tests PRH-005 [integration]: the ADR trust decision uses the HARDENED path, and
    // the permissive spike line-scanner runs only as a redundant cross-check ANDed
    // with the authoritative decision — never as the sole trust source. RED: the
    // authoritative AdrLintBlockParser is the decision home (stub).
    [Fact]
    public void PRH005_authoritative_decision_is_the_hardened_parser()
    {
        string adr = File.ReadAllText(TestPaths.Fixture("adr", "migrated-adr-lint.md"));
        AdrParseResult r = AdrLintBlockParser.Parse(adr);
        Assert.Equal(AdrParseOutcome.Ok, r.Outcome);
        Assert.Equal("A", r.Block!.SelectedRoute);
    }

    // Tests PRH-006 [unit]: no committed P1.satisfied:true without a passing carrier
    // re-deriving it (the inverse-partial guard) — a satisfied:true the carrier does
    // NOT re-derive is itself a blocking condition. RED against the stub kernel.
    [Fact]
    public void PRH006_satisfied_true_not_re_derived_is_blocking()
    {
        var v = ReadinessGate.EvaluateReadiness(
            BlockP1(true, "gate-id", ReadinessStatus.READY),
            Probes(false, ReferenceResolution.Resolved)); // carrier does NOT re-derive
        Assert.Equal(VerdictKind.Fail, v.Kind);
        Assert.Equal(PreconditionId.P1, v.OffendingPrecondition);
    }

    // Tests PRH-007 [integration]: the recompute never passes a vacuous / plan-shrunk
    // / content-tampered evidence sample — the compiled canonical_sample_sha256 must
    // match the committed sample (a coherently-tampered sample fails). RED: the const
    // is a placeholder until GREEN pins the real digest.
    [Fact]
    public void PRH007_compiled_canonical_sha_pins_the_sample()
    {
        string sample = TestPaths.RepoFile("spikes", "dafny-compat", "evidence", "samples", "run-report.canonical.sample.json");
        using var sha = System.Security.Cryptography.SHA256.Create();
        string digest = Convert.ToHexString(sha.ComputeHash(File.ReadAllBytes(sample))).ToLowerInvariant();
        Assert.Equal(digest, P1EvidenceAnchors.CanonicalSampleSha256);
    }

    // Tests PRH-007 [integration]: the recompute never passes a vacuous / plan-shrunk /
    // content-tampered sample — drive the REAL probe over a SYNTHESIZED migrated temp
    // tree with each such sample and assert it fails closed (evidence-malformed), NOT a
    // vacuous Assert.NotNull. Covers {vacuous route-B-only, empty per_probe_results,
    // duplicate JSON key, tampered manifest FILE, coherently-tampered canonical sample}.
    // RED against the stub probe.
    [Theory]
    [InlineData(P1Mutation.RouteBOnlyPerProbe)]
    [InlineData(P1Mutation.EmptyPerProbe)]
    [InlineData(P1Mutation.DuplicateJsonKeyRoot)]
    [InlineData(P1Mutation.TamperedManifestFile)]
    [InlineData(P1Mutation.CoherentlyTamperedSample)]
    public void PRH007_recompute_never_passes_a_forged_sample(P1Mutation mutation)
    {
        using var tree = P1Tree.Build(mutation);
        ProbeResult r = new P1Probe().Evaluate(GateContext.ForRepoRoot(tree.Root));
        Assert.False(r.Satisfied);
        Assert.NotEqual(ProbeReasons.EvidenceSchemaIncomplete, r.Reason);
        Assert.Equal(ProbeReasons.EvidenceMalformed, r.Reason);
    }
}
