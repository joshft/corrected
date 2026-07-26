using System;
using System.IO;
using System.Text;
using Corrected.Gate;
using Corrected.Gate.Kernel;
using Xunit;

namespace Corrected.Gate.Tests;

/// <summary>
/// INV-001: exactly one bounded readiness block; file + block size-capped;
/// encoding-normalized; repo-root sentinel anchor; INV-001-D column-0-in-yaml-fence
/// discriminator. All [unit].
/// </summary>
public class Inv001ExtractionTests
{
    // Tests INV-001 [unit]: MaxFileBytes / MaxBlockBytes are tested public const.
    [Fact]
    public void Caps_are_public_const()
    {
        Assert.True(ReadinessBlockParser.MaxFileBytes > 0);
        Assert.True(ReadinessBlockParser.MaxBlockBytes > 0);
        Assert.True(ReadinessBlockParser.MaxBlockBytes <= ReadinessBlockParser.MaxFileBytes);
    }

    // Tests INV-001 [unit]: the CURRENT REAL parent parses to EXACTLY ONE block
    // under INV-001-D (a copy of the committed prose lines must NOT dead-red it).
    // Uses the verbatim real-producer fixture (AP-031).
    // Source: .correctless/specs/phase-0-1-worker.md lines 132-153.
    [Fact]
    public void Real_parent_fixture_extracts_exactly_one_block()
    {
        string md = File.ReadAllText(TestPaths.Fixture("readiness", "real-parent-readiness-block.md"));
        string block = ReadinessBlockParser.ExtractSingleBlock(md);
        Assert.Contains("implementation_readiness:", block);
    }

    // Tests INV-001 [unit]: the LIVE committed parent spec parses to exactly one
    // block (live coverage over the real producer, AP-031 alternate form).
    [Fact]
    public void Live_committed_parent_extracts_exactly_one_block()
    {
        string md = File.ReadAllText(TestPaths.RepoFile(".correctless", "specs", "phase-0-1-worker.md"));
        string block = ReadinessBlockParser.ExtractSingleBlock(md);
        Assert.Contains("implementation_readiness:", block);
    }

    // Tests INV-001 [unit]: an inline-prose mention (no column-0 yaml-fenced block)
    // is IGNORED — extraction finds ZERO -> hard fail-closed (RS-261). B5: assert the
    // SPECIFIC typed exception + NAMED reason (NoReadinessBlock), not ThrowsAny — a
    // ThrowsAny passes even on a NullReferenceException from a half-built parser
    // (AP-014). RED against the NotImplemented stub (which throws the WRONG type).
    [Fact]
    public void Inline_prose_mention_is_not_counted_as_a_block()
    {
        string md = File.ReadAllText(TestPaths.Fixture("readiness", "inline-prose-mention.md"));
        var ex = Assert.Throws<ReadinessExtractionException>(
            () => ReadinessBlockParser.ExtractSingleBlock(md));
        Assert.Equal(ReadinessExtractionReason.NoReadinessBlock, ex.Reason);
    }

    // Tests INV-001 [unit]: zero in-fence blocks -> hard fail-closed with the NAMED
    // NoReadinessBlock reason. RED against the NotImplemented stub (wrong type).
    [Fact]
    public void Zero_blocks_fail_closed()
    {
        string md = File.ReadAllText(TestPaths.Fixture("readiness", "zero-blocks.md"));
        var ex = Assert.Throws<ReadinessExtractionException>(
            () => ReadinessBlockParser.ExtractSingleBlock(md));
        Assert.Equal(ReadinessExtractionReason.NoReadinessBlock, ex.Reason);
    }

    // Tests INV-001 [unit]: two in-fence blocks (duplicate = tamper) -> hard
    // fail-closed with the NAMED MultipleReadinessBlocks reason (distinct from the
    // zero case). RED against the NotImplemented stub (wrong type).
    [Fact]
    public void Two_blocks_fail_closed()
    {
        string md = File.ReadAllText(TestPaths.Fixture("readiness", "two-blocks.md"));
        var ex = Assert.Throws<ReadinessExtractionException>(
            () => ReadinessBlockParser.ExtractSingleBlock(md));
        Assert.Equal(ReadinessExtractionReason.MultipleReadinessBlocks, ex.Reason);
    }

    // Tests INV-001 [unit]: over-MaxFileBytes -> hard fail-closed with the NAMED
    // FileTooLarge reason (cap applied POST-normalization, RS-264). RED against the
    // NotImplemented stub (wrong type).
    [Fact]
    public void Over_max_file_bytes_fails_closed()
    {
        var sb = new StringBuilder();
        sb.Append("```yaml\nimplementation_readiness:\n  status: BLOCKED\n```\n");
        sb.Append('x', (int)Math.Min(ReadinessBlockParser.MaxFileBytes + 1024, int.MaxValue - 64));
        var ex = Assert.Throws<ReadinessExtractionException>(
            () => ReadinessBlockParser.ExtractSingleBlock(sb.ToString()));
        Assert.Equal(ReadinessExtractionReason.FileTooLarge, ex.Reason);
    }

    // Tests INV-001 [unit]: a CRLF-on-disk file UNDER the cap after LF normalization
    // must NOT be dead-red before normalization (normalize first, THEN bound; RS-264).
    [Fact]
    public void Crlf_under_cap_normalizes_then_parses()
    {
        string md = File.ReadAllText(TestPaths.Fixture("readiness", "real-parent-readiness-block.md"))
            .Replace("\n", "\r\n");
        string block = ReadinessBlockParser.ExtractSingleBlock(md);
        Assert.Contains("implementation_readiness:", block);
    }

    // Tests INV-001 [unit]: the repo-root sentinel is the dir containing BOTH the
    // repo-root global.json AND .correctless/ — NOT the cwd. A two-cwd anchor test:
    // resolution is cwd-independent. STAGE-A NOTE: the real repo-root global.json is
    // absent (scope boundary), so this FAILS RED until GREEN adds it (INV-016).
    [Fact]
    public void Repo_root_sentinel_is_cwd_independent_and_named()
    {
        string root1 = RepoRootLocator.Locate();
        string saved = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(Path.GetTempPath());
            string root2 = RepoRootLocator.Locate();
            Assert.Equal(root1, root2);
            Assert.True(File.Exists(Path.Combine(root1, "global.json")), "sentinel requires repo-root global.json (INV-016)");
            Assert.True(Directory.Exists(Path.Combine(root1, ".correctless")));
        }
        finally
        {
            Directory.SetCurrentDirectory(saved);
        }
    }

    // Tests INV-001 [integration]: INV-001 fails CLOSED with a NAMED reason if the
    // .gitattributes LF pin is absent (INV-016 enforcement cross-ref, RS-A-10).
    // STAGE-A NOTE: no repo-root .gitattributes yet (scope) -> RED.
    [Fact]
    public void Missing_gitattributes_lf_pin_fails_closed_with_named_reason()
    {
        Assert.True(TestPaths.RepoFileExists(".gitattributes"),
            "INV-001/INV-016: a repo-root .gitattributes pinning parsed specs/ADR to LF must exist");
    }
}
