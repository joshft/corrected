using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Corrected.Gate.Kernel;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Corrected.Gate;

/// <summary>
/// The typed outcome of an adr_lint parse (INV-002 taxonomy / R3-B1b). Separates
/// the benign pre-migration case (EvidenceSchemaIncomplete — a REQUIRED field is
/// valid but the OPTIONAL acceptance schema `status` key is absent, determined by
/// the presence bit, NOT a required-member exception) from the tamper case
/// (EvidenceMalformed — a REQUIRED field is missing/duplicated, a tag/anchor/2nd
/// block materializes, or structurally malformed).
/// </summary>
public enum AdrParseOutcome
{
    Ok,
    EvidenceSchemaIncomplete,
    EvidenceMalformed,
}

/// <summary>Result of an adr_lint parse (INV-002/008).</summary>
public sealed class AdrParseResult
{
    private AdrParseResult(AdrParseOutcome outcome, AdrLintBlock? block)
    {
        Outcome = outcome;
        Block = block;
    }

    public AdrParseOutcome Outcome { get; }
    public AdrLintBlock? Block { get; }

    internal static AdrParseResult Create(AdrParseOutcome outcome, AdrLintBlock? block)
        => new(outcome, block);
}

/// <summary>
/// Shared fence-extraction + AST-hardening machinery used by BOTH the readiness
/// parser and the ADR parser (INV-001/002 RS-206 — "same machinery, distinct DTO").
/// </summary>
internal static class YamlHardening
{
    /// <summary>Normalize to LF / UTF-8-no-BOM (INV-001, applied BEFORE the byte caps, RS-264).</summary>
    public static string Normalize(string text)
    {
        if (text.Length > 0 && text[0] == '﻿')
        {
            text = text.Substring(1);
        }
        return text.Replace("\r\n", "\n").Replace("\r", "\n");
    }

    public static long Utf8ByteCount(string text) => Encoding.UTF8.GetByteCount(text);

    /// <summary>
    /// Locate every column-0 `<key>:` line inside a ```yaml fence (INV-001-D). Backticked /
    /// inline / non-yaml-fence mentions do NOT count. Returns the fence CONTENT of each match.
    /// </summary>
    public static List<string> FindColumn0KeyBlocks(string normalizedText, string key)
    {
        var blocks = new List<string>();
        string[] lines = normalizedText.Split('\n');
        bool inFence = false;
        bool yamlFence = false;
        var content = new List<string>();
        bool sawKey = false;

        foreach (var line in lines)
        {
            if (line.StartsWith("```", StringComparison.Ordinal))
            {
                if (!inFence)
                {
                    // Opening fence: capture the info string.
                    string info = line.Substring(3).Trim();
                    inFence = true;
                    yamlFence = string.Equals(info, "yaml", StringComparison.Ordinal);
                    content.Clear();
                    sawKey = false;
                }
                else
                {
                    // Closing fence.
                    if (yamlFence && sawKey)
                    {
                        blocks.Add(string.Join("\n", content));
                    }
                    inFence = false;
                    yamlFence = false;
                }
                continue;
            }

            if (inFence)
            {
                content.Add(line);
                // column-0 key: no leading whitespace and trimEnd == "<key>:".
                if (yamlFence
                    && line.Length > 0
                    && line[0] != ' ' && line[0] != '\t'
                    && string.Equals(line.TrimEnd(), key + ":", StringComparison.Ordinal))
                {
                    sawKey = true;
                }
            }
        }

        return blocks;
    }

    /// <summary>
    /// Stage-1 AST pre-validation over the low-level IParser event stream: reject every
    /// explicit tag / anchor / alias / second document / trailing content, and enforce the
    /// incremental caps. Returns false (a breach) rather than throwing (INV-002 RS-RT-16).
    /// </summary>
    public static bool Stage1Ok(string yaml, int maxScalarLength, int maxNodeCount)
    {
        try
        {
            var parser = new Parser(new StringReader(yaml));
            int nodeCount = 0;
            int docCount = 0;

            while (parser.MoveNext())
            {
                ParsingEvent evt = parser.Current!;
                switch (evt)
                {
                    case DocumentStart:
                        docCount++;
                        if (docCount > 1)
                        {
                            return false; // a second document / trailing content
                        }
                        break;
                    case AnchorAlias:
                        return false; // an alias (MaxAliasCount == 0)
                    case NodeEvent ne:
                        if (!ne.Anchor.IsEmpty)
                        {
                            return false; // an anchor
                        }
                        if (!ne.Tag.IsEmpty)
                        {
                            return false; // an explicit tag (incl. built-in !!str/!!int/!!bool)
                        }
                        nodeCount++;
                        if (nodeCount > maxNodeCount)
                        {
                            return false;
                        }
                        if (ne is Scalar sc && sc.Value.Length > maxScalarLength)
                        {
                            return false;
                        }
                        break;
                }
            }

            return true;
        }
        catch (YamlException)
        {
            return false;
        }
    }
}

/// <summary>
/// The hardened readiness-block parser (INV-001/002). Stage-1 AST pre-validation
/// over YamlDotNet's low-level IParser event stream (rejecting every tag/anchor/
/// alias/multi-doc/trailing content and enforcing the incremental caps), Stage-2
/// deserialize into a private DTO with required members, then post-parse
/// validation. Tested public const caps (INV-001/002).
/// </summary>
public static class ReadinessBlockParser
{
    // INV-001: caps applied post-normalization (RS-264). Tested public const.
    public const long MaxFileBytes = 1_048_576;
    public const long MaxBlockBytes = 65_536;
    // INV-002: incremental caps (RS-T-07). Tested public const.
    public const int MaxScalarLength = 8_192;
    public const int MaxNodeCount = 4_096;
    public const int MaxAliasCount = 0; // all aliases rejected

    private const string Key = "implementation_readiness";

    /// <summary>
    /// INV-001-D: locate EXACTLY ONE readiness block (single `implementation_readiness:`
    /// key at column 0 inside the one ```yaml fence). Hard fail-closed on zero/>=2
    /// in-fence blocks or over-cap.
    /// </summary>
    public static string ExtractSingleBlock(string markdownText)
    {
        string normalized = YamlHardening.Normalize(markdownText);
        if (YamlHardening.Utf8ByteCount(normalized) > MaxFileBytes)
        {
            throw new ReadinessExtractionException(
                ReadinessExtractionReason.FileTooLarge, "readiness file exceeds MaxFileBytes after normalization");
        }

        var blocks = YamlHardening.FindColumn0KeyBlocks(normalized, Key);
        if (blocks.Count == 0)
        {
            throw new ReadinessExtractionException(
                ReadinessExtractionReason.NoReadinessBlock, "no column-0 implementation_readiness: in a yaml fence");
        }
        if (blocks.Count >= 2)
        {
            throw new ReadinessExtractionException(
                ReadinessExtractionReason.MultipleReadinessBlocks, "more than one readiness block");
        }

        string block = blocks[0];
        if (YamlHardening.Utf8ByteCount(block) > MaxBlockBytes)
        {
            throw new ReadinessExtractionException(
                ReadinessExtractionReason.BlockTooLarge, "readiness block exceeds MaxBlockBytes");
        }
        return block;
    }

    /// <summary>
    /// Full parse to the validated immutable ReadinessBlock. An unparseable block
    /// yields a ReadinessBlock whose Status is Indeterminate (RS-262), never an abort.
    /// </summary>
    public static ReadinessBlock Parse(string markdownText)
    {
        string yaml;
        try
        {
            yaml = ExtractSingleBlock(markdownText);
        }
        catch (ReadinessExtractionException)
        {
            return ReadinessBlock.Indeterminate();
        }

        if (!YamlHardening.Stage1Ok(yaml, MaxScalarLength, MaxNodeCount))
        {
            return ReadinessBlock.Indeterminate();
        }

        ReadinessDto? dto;
        try
        {
            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(UnderscoredNamingConvention.Instance)
                .WithDuplicateKeyChecking()
                .Build();
            // The extracted block is `implementation_readiness:` at column 0 wrapping the
            // DTO fields, so deserialize the wrapper and take the single inner value.
            var wrapper = deserializer.Deserialize<ReadinessDocDto>(new StringReader(yaml));
            dto = wrapper?.ImplementationReadiness;
        }
        catch (YamlException)
        {
            return ReadinessBlock.Indeterminate();
        }
        catch (InvalidOperationException)
        {
            return ReadinessBlock.Indeterminate();
        }

        if (dto is null || dto.Preconditions is null)
        {
            return ReadinessBlock.Indeterminate();
        }

        if (dto.SchemaVersion != ReadinessBlock.RecognizedSchemaVersion)
        {
            return ReadinessBlock.Indeterminate();
        }

        if (!TryParseStatus(dto.Status, out var status))
        {
            return ReadinessBlock.Indeterminate();
        }

        var preconditions = new List<ReadinessPrecondition>();
        foreach (var p in dto.Preconditions)
        {
            if (p is null || p.Id is null || !Enum.TryParse<PreconditionId>(p.Id, out var pid))
            {
                return ReadinessBlock.Indeterminate();
            }
            preconditions.Add(ReadinessPrecondition.Create(
                pid, p.Name ?? p.Id, p.Satisfied, p.Evidence, p.Discharges ?? new List<string>()));
        }

        // ready_predicate == the conjunction of the precondition ids (INV-002).
        string expectedPredicate = string.Join(" AND ", preconditions.Select(p => p.Id.ToString()));
        if (!string.Equals(dto.ReadyPredicate, expectedPredicate, StringComparison.Ordinal))
        {
            return ReadinessBlock.Indeterminate();
        }

        // dto.ReadyPredicate equals expectedPredicate here (checked above) — pass the
        // validated non-null value.
        var block = ReadinessBlock.TryCreate(dto.SchemaVersion, status, expectedPredicate, preconditions);
        return block ?? ReadinessBlock.Indeterminate();
    }

    private static bool TryParseStatus(string? raw, out ReadinessStatus status)
    {
        switch (raw)
        {
            case "BLOCKED":
                status = ReadinessStatus.BLOCKED;
                return true;
            case "READY":
                status = ReadinessStatus.READY;
                return true;
            default:
                status = ReadinessStatus.Indeterminate;
                return false;
        }
    }

    // Private closed-vocabulary DTOs (INV-002/003) — YAML materializes only into these.
    private sealed class ReadinessDocDto
    {
        public ReadinessDto? ImplementationReadiness { get; set; }
    }

    private sealed class ReadinessDto
    {
        public int SchemaVersion { get; set; }
        public string? Status { get; set; }
        public string? ReadyPredicate { get; set; }
        public List<PreconditionDto>? Preconditions { get; set; }
    }

    private sealed class PreconditionDto
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public bool Satisfied { get; set; }
        public string? Evidence { get; set; }
        public List<string>? Discharges { get; set; }
    }
}

/// <summary>
/// The distinct ADR adr_lint parser sharing the SAME Stage-1/Stage-2 hardening
/// machinery as ReadinessBlockParser but targeting the distinct AdrLintBlock DTO
/// (INV-002 RS-206). Never the spike's permissive line-scanner for a trust
/// decision (PRH-005). A parse failure is caught and mapped to a typed outcome,
/// never thrown (INV-006 "never throws").
/// </summary>
public static class AdrLintBlockParser
{
    private const string Key = "adr_lint";

    public static AdrParseResult Parse(string adrMarkdownText)
    {
        string normalized = YamlHardening.Normalize(adrMarkdownText);
        var blocks = YamlHardening.FindColumn0KeyBlocks(normalized, Key);
        if (blocks.Count != 1)
        {
            // Zero or >=2 adr_lint blocks -> tamper -> malformed.
            return AdrParseResult.Create(AdrParseOutcome.EvidenceMalformed, null);
        }

        string yaml = blocks[0];
        if (!YamlHardening.Stage1Ok(yaml, ReadinessBlockParser.MaxScalarLength, ReadinessBlockParser.MaxNodeCount))
        {
            return AdrParseResult.Create(AdrParseOutcome.EvidenceMalformed, null);
        }

        IDictionary<object, object?>? adr;
        try
        {
            var deserializer = new DeserializerBuilder()
                .WithDuplicateKeyChecking()
                .Build();
            var top = deserializer.Deserialize<Dictionary<string, object?>>(new StringReader(yaml));
            if (top is null || !top.TryGetValue(Key, out var adrObj) || adrObj is not IDictionary<object, object?> map)
            {
                return AdrParseResult.Create(AdrParseOutcome.EvidenceMalformed, null);
            }
            adr = map;
        }
        catch (YamlException)
        {
            return AdrParseResult.Create(AdrParseOutcome.EvidenceMalformed, null);
        }

        bool Has(string k) => adr.ContainsKey(k);
        string? Get(string k) => adr.TryGetValue(k, out var v) ? v as string : null;

        // REQUIRED tier: boundary_decision, selected_route, routes[].
        if (!Has("boundary_decision") || string.IsNullOrEmpty(Get("boundary_decision")))
        {
            return AdrParseResult.Create(AdrParseOutcome.EvidenceMalformed, null);
        }
        if (!Has("selected_route"))
        {
            return AdrParseResult.Create(AdrParseOutcome.EvidenceMalformed, null);
        }
        if (!Has("routes") || adr["routes"] is not IList<object?> routesRaw)
        {
            return AdrParseResult.Create(AdrParseOutcome.EvidenceMalformed, null);
        }

        var routes = new List<AdrRoute>();
        foreach (var r in routesRaw)
        {
            if (r is not IDictionary<object, object?> rd)
            {
                return AdrParseResult.Create(AdrParseOutcome.EvidenceMalformed, null);
            }
            string route = rd.TryGetValue("route", out var rv) ? rv as string ?? "" : "";
            string verdict = rd.TryGetValue("verdict", out var vv) ? vv as string ?? "" : "";
            string? adj = rd.TryGetValue("adjudication_record_id", out var av) ? av as string : null;
            string? ev = rd.TryGetValue("evidence", out var ev0) ? ev0 as string : null;
            routes.Add(AdrRoute.Create(route, verdict, adj, ev));
        }

        bool hasStatus = Has("status");
        bool hasSupersedes = Has("supersedes");
        bool hasSupersededBy = Has("superseded_by");

        var block = AdrLintBlock.TryCreate(
            Get("boundary_decision")!,
            Get("selected_route"),
            routes,
            hasStatus, Get("status"),
            hasSupersedes, Get("supersedes"),
            hasSupersededBy, Get("superseded_by"));

        if (block is null)
        {
            return AdrParseResult.Create(AdrParseOutcome.EvidenceMalformed, null);
        }

        // Schema-completeness short-circuit (INV-008 (a) step 2): absent `status`
        // key is the benign pre-migration case -> schema-incomplete (NOT malformed).
        if (!hasStatus)
        {
            return AdrParseResult.Create(AdrParseOutcome.EvidenceSchemaIncomplete, block);
        }

        return AdrParseResult.Create(AdrParseOutcome.Ok, block);
    }
}
