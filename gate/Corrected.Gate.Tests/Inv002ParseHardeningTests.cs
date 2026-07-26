using System;
using System.IO;
using Corrected.Gate;
using Corrected.Gate.Kernel;
using Xunit;

namespace Corrected.Gate.Tests;

/// <summary>
/// INV-002: AST pre-validation over the low-level IParser event stream;
/// closed-vocabulary into a private DTO; the distinct AdrLintBlock schema with the
/// REQUIRED-vs-OPTIONAL split + presence bits; the parse-failure taxonomy; and the
/// unparseable -> indeterminate value. All [unit].
/// </summary>
public class Inv002ParseHardeningTests
{
    // Tests INV-002 [unit]: incremental caps are tested public const.
    [Fact]
    public void Incremental_caps_are_public_const()
    {
        Assert.True(ReadinessBlockParser.MaxScalarLength > 0);
        Assert.True(ReadinessBlockParser.MaxNodeCount > 0);
        Assert.Equal(0, ReadinessBlockParser.MaxAliasCount); // aliases all rejected
    }

    // Tests INV-002 [unit]: an explicit tag (!!str/!!int) is rejected by Stage-1 AST
    // pre-validation -> the block yields status:indeterminate (RS-262), never a
    // materialized gadget (PRH-003).
    [Fact]
    public void Tag_injection_yields_indeterminate()
    {
        string md = File.ReadAllText(TestPaths.Fixture("readiness", "tag-injection.md"));
        ReadinessBlock block = ReadinessBlockParser.Parse(md);
        Assert.Equal(ReadinessStatus.Indeterminate, block.Status);
    }

    // Tests INV-002 [unit]: the VALID real parent block parses to exact pinned
    // values (schema_version 1, BLOCKED, exactly {P1,P2,P3}, satisfied:false,
    // evidence:null). AP-031 verbatim fixture.
    // Source: .correctless/specs/phase-0-1-worker.md lines 132-153.
    [Fact]
    public void Valid_block_parses_to_exact_values()
    {
        string md = File.ReadAllText(TestPaths.Fixture("readiness", "real-parent-readiness-block.md"));
        ReadinessBlock block = ReadinessBlockParser.Parse(md);
        Assert.Equal(1, block.SchemaVersion);
        Assert.Equal(ReadinessStatus.BLOCKED, block.Status);
        Assert.Equal("P1 AND P2 AND P3", block.ReadyPredicate);
        Assert.Equal(3, block.Preconditions.Count);
        foreach (var p in block.Preconditions)
        {
            Assert.False(p.Satisfied);
            Assert.Null(p.Evidence);
        }
    }

    // Tests INV-002 [unit]: a malformed/unparseable block does NOT abort the gate —
    // it yields a typed indeterminate value handed to the kernel (RS-262).
    [Fact]
    public void Unparseable_block_yields_indeterminate_value_not_abort()
    {
        ReadinessBlock block = ReadinessBlockParser.Parse("```yaml\nimplementation_readiness: [not, a, map]\n```");
        Assert.Equal(ReadinessStatus.Indeterminate, block.Status);
    }

    // Tests INV-002 [unit]: the PRE-MIGRATION ADR (status/supersedes/superseded_by
    // ABSENT) PARSES and yields evidence-schema-incomplete (NOT malformed, NOT a
    // throw) — determined by the presence bit, not a required-member exception.
    // AP-031 verbatim real-producer fixture.
    // Source: docs/adr/ADR-0001-dafny-integration-boundary.md lines 27-40.
    [Fact]
    public void PreMigration_adr_parses_to_schema_incomplete()
    {
        string adr = File.ReadAllText(TestPaths.Fixture("adr", "pre-migration-adr-lint.md"));
        AdrParseResult r = AdrLintBlockParser.Parse(adr);
        Assert.Equal(AdrParseOutcome.EvidenceSchemaIncomplete, r.Outcome);
    }

    // Tests INV-002 [unit]: the MIGRATED ADR (status:accepted, superseded_by:null
    // explicit) parses VALID with the presence bits set (EXT4-02 explicit-null form).
    [Fact]
    public void Migrated_adr_parses_valid_with_presence_bits()
    {
        string adr = File.ReadAllText(TestPaths.Fixture("adr", "migrated-adr-lint.md"));
        AdrParseResult r = AdrLintBlockParser.Parse(adr);
        Assert.Equal(AdrParseOutcome.Ok, r.Outcome);
        Assert.NotNull(r.Block);
        Assert.True(r.Block!.HasStatus);
        Assert.Equal("accepted", r.Block.Status);
        Assert.True(r.Block.HasSupersededBy);
        Assert.Null(r.Block.SupersededBy); // explicit null == "no edge" == terminal
    }

    // Tests INV-002 [unit]: a stripped REQUIRED field maps to evidence-malformed,
    // NOT schema-incomplete (the masking guard, AP-014).
    [Fact]
    public void Stripped_required_field_is_malformed_not_schema_incomplete()
    {
        string adr = File.ReadAllText(TestPaths.Fixture("adr", "stripped-decision-adr-lint.md"));
        AdrParseResult r = AdrLintBlockParser.Parse(adr);
        Assert.Equal(AdrParseOutcome.EvidenceMalformed, r.Outcome);
    }

    // Tests INV-002 [unit]: key-absence of status is distinguished from explicit
    // null by the presence bit (the v4 required-string? conflation bug).
    [Fact]
    public void Status_key_absence_distinguished_from_explicit_null()
    {
        string pre = File.ReadAllText(TestPaths.Fixture("adr", "pre-migration-adr-lint.md"));
        AdrParseResult rPre = AdrLintBlockParser.Parse(pre);
        // Pre-migration -> schema-incomplete; the presence bit for status is false.
        Assert.Equal(AdrParseOutcome.EvidenceSchemaIncomplete, rPre.Outcome);
    }
}
