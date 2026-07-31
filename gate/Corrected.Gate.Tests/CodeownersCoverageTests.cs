using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Corrected.Gate.Tests;

/// <summary>
/// INV-025 (P3 spec `.correctless/specs/p3-determinism-attestation.md` ~841-863) — the threat
/// claim is NARROWED to evidence tampering/fabrication under an UNCHANGED, reviewed verifier /
/// workflow / tool-pin / trust-policy, with the out-of-band protections stated as external
/// assumptions (EA-006, ~1453 — the COMPLETE RS-012 protected-path set).
///
/// This suite encodes the two enforcement halves of INV-025:
///   (a) a CODEOWNERS ASSUMPTION-COMPLETENESS check — the (GREEN-created) `.github/CODEOWNERS`
///       COVERS the *complete* RS-012 protected-path set (an @owned rule for every surface); and
///   (b) a spec-framing guard — INV-025 STATES the narrowed guarantee AND labels the CODEOWNERS
///       check as assumption-completeness, NOT structural enforcement (RS-039).
///
/// RS-039 (READ FIRST): the CODEOWNERS presence check is ASSUMPTION-completeness, NOT structural
/// enforcement. Required-review + branch-protection are out-of-band GitHub settings that no
/// in-repo test can prove — a presence check passes even if branch protection is DISABLED. These
/// tests therefore assert only that the committed CODEOWNERS *covers* the protected surfaces (an
/// owned rule exists per surface) and that the spec does not overstate that as a cryptographic /
/// structural guarantee. They deliberately do NOT (and cannot) assert real branch protection.
///
/// PMB-003 / AP-022 (false-completeness): coverage is a LOOP over the pinned RS-012 set, and the
/// set count/enumeration is pinned + grounded in the committed EA-006 text — so a REMOVED or
/// MUTATED entry for ANY protected surface (a partial CODEOWNERS) fails CLOSED, and a silent
/// shrink of the set (dropping the lifecycle field or the pointers, RS-012) fails the enumeration
/// pin. A representative-subset or row-count proxy is explicitly rejected.
///
/// AP-031 real-artifact clause is NOT triggered: the inputs are a repo CONFIG file
/// (`.github/CODEOWNERS`) and a committed PROJECT spec — neither is a `.correctless/artifacts/`
/// Correctless-skill/producer output.
///
/// PRODUCTION for this invariant (GREEN's job, NOT written here):
///   * a NEW `.github/CODEOWNERS` whose @owned rules cover the nine RS-012 surfaces below;
///   * the already-committed narrowed-threat-claim + RS-039 framing spec text (guard-only here).
/// To turn the coverage cells GREEN, `.github/CODEOWNERS` must carry an @owned rule per surface,
/// e.g. (illustrative — GREEN owns the exact owners/globs, the tokens below are the contract):
///     gate/**                                    @some-owner
///     gate/Corrected.Provenance/**               @some-owner
///     .github/workflows/**                       @some-owner
///     **/trusted_root*.json                      @some-owner   (glob covers a not-yet-existing root)
///     gate/tools/cosign-pin.json                 @some-owner   (under gate/** but NAMED, RS-012)
///     .correctless/specs/phase-0-1-worker.md     @some-owner   (the field that lifts the src/ ban)
///     test/attestations/**                       @some-owner   (receipts/bundles AND pointers)
///     spikes/dafny-compat/**                     @some-owner   (floor const INV-004 + producer)
/// </summary>
public class CodeownersCoverageTests
{
    // GitHub honors CODEOWNERS at repo-root, `.github/`, or `docs/`; this feature commits it at
    // `.github/CODEOWNERS`, so that is the path asserted.
    private static string CodeownersPath() => TestPaths.RepoFile(".github", "CODEOWNERS");

    private static string SpecText() =>
        File.ReadAllText(TestPaths.RepoFile(".correctless", "specs", "p3-determinism-attestation.md"));

    /// <summary>Collapse all whitespace runs (incl. newlines) to a single space so a line-wrap
    /// reflow in the spec/CODEOWNERS does not defeat a substring assertion.</summary>
    private static string Norm(string s) => Regex.Replace(s, @"\s+", " ").Trim();

    private static string Section(string text, string start, string end)
    {
        int s = text.IndexOf(start, StringComparison.Ordinal);
        Assert.True(s >= 0, $"section start marker '{start}' not found (spec drift?)");
        int e = text.IndexOf(end, s + start.Length, StringComparison.Ordinal);
        if (e < 0) { e = text.Length; }
        return text.Substring(s, e - s);
    }

    private static string Inv025Section() => Section(SpecText(), "### INV-025:", "### Group G");
    private static string Ea006Section() => Section(SpecText(), "- **EA-006**", "- **EA-007**");

    /// <summary>Read CODEOWNERS, GATING the read behind an existence ASSERTION so the RED state
    /// (no file yet) fails as an assertion, not an unhandled IOException.</summary>
    private static string ReadCodeowners()
    {
        var path = CodeownersPath();
        Assert.True(File.Exists(path),
            $"INV-025/RS-012: `.github/CODEOWNERS` must exist (GREEN creates it). Missing at: {path}");
        return File.ReadAllText(path);
    }

    // ---- minimal CODEOWNERS parse ----
    // A CODEOWNERS rule is `<pattern> <owner>...`. Owners are @handle / @org/team / email — all
    // contain '@'. Comment lines start with '#'. This is a MINIMAL parser: it does NOT evaluate
    // glob semantics (anchoring, negation, precedence) — sufficient for an assumption-completeness
    // PRESENCE check (RS-039), not structural enforcement.
    private sealed record CodeownersRule(string Pattern, IReadOnlyList<string> Owners, string Raw);

    private static bool IsOwnerToken(string tok) => tok.Contains('@');

    private static List<CodeownersRule> ParseRules(string content)
    {
        var rules = new List<CodeownersRule>();
        foreach (var raw in content.Split('\n'))
        {
            var line = raw.TrimEnd('\r').Trim();
            if (line.Length == 0) { continue; }
            if (line.StartsWith("#", StringComparison.Ordinal)) { continue; }
            var toks = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (toks.Length == 0) { continue; }
            var owners = toks.Skip(1).Where(IsOwnerToken).ToList();
            rules.Add(new CodeownersRule(toks[0], owners, line));
        }
        return rules;
    }

    // COVERS := some OWNED rule whose pattern TEXT contains the surface's CODEOWNERS token.
    // Substring-in-pattern is intentional: it forces NAMED surfaces (cosign-pin, lifecycle-field,
    // trust-root) to require an EXPLICIT entry — a broad `gate/**` line does NOT contain the
    // literal `gate/tools/cosign-pin.json`, so a wide glob cannot MASK a removed named entry
    // (RS-012 "name it"). LIMITATION (for the audit): no glob evaluation; presence-only.
    private static bool IsCovered(IEnumerable<CodeownersRule> rules, string token) =>
        rules.Any(r => r.Owners.Count > 0 && r.Pattern.Contains(token, StringComparison.Ordinal));

    // ---- the PINNED RS-012 / EA-006 protected-surface set (single source of truth) ----
    // Id             : stable surface identity.
    // CodeownersToken: the substring an @owned CODEOWNERS pattern must contain to COVER the surface.
    // SpecAnchor     : a substring that MUST appear in the committed EA-006 text (grounds the set
    //                  in the spec, not in an invented test literal — PMB-003).
    private sealed record ProtectedSurface(string Id, string CodeownersToken, string SpecAnchor, string Note);

    // NINE surfaces == the COMPLETE EA-006 enumeration (RS-012). floor-constant and spike-producer
    // are DISTINCT EA-006 surfaces that share the `spikes/dafny-compat` coverage glob (the floor
    // const / runner-escalation policy is covered "via the spikes glob").
    private static readonly IReadOnlyList<ProtectedSurface> Rs012ProtectedSet = new[]
    {
        new ProtectedSurface("gate-tree", "gate/**", "gate/**",
            "the gate solution."),
        new ProtectedSurface("provenance", "gate/Corrected.Provenance/**", "gate/Corrected.Provenance/**",
            "the P3 provenance substrate."),
        new ProtectedSurface("signing-workflow", ".github/workflows/**", "the signing workflow",
            "the determinism/signing lane under .github/workflows/**."),
        new ProtectedSurface("trust-root", "trusted_root", "the trust root",
            "a committed trusted_root*.json may not exist yet; a glob still covers it (token is substring, glob-agnostic)."),
        new ProtectedSurface("cosign-pin", "gate/tools/cosign-pin.json", "the cosign pin",
            "under gate/** but NAMED explicitly (RS-012)."),
        new ProtectedSurface("lifecycle-field", ".correctless/specs/phase-0-1-worker.md", ".correctless/specs/phase-0-1-worker.md",
            "the lifecycle/preconditions/satisfied spans — the field that lifts the src/ ban, MORE load-bearing than the cosign pin."),
        new ProtectedSurface("attestations", "test/attestations", "test/attestations/**",
            "committed receipts/bundles AND every pointer file."),
        new ProtectedSurface("floor-constant", "spikes/dafny-compat", "resource-floor constant",
            "INV-004 resource-floor const + runner-escalation policy; covered via the spikes glob (shares token with spike-producer)."),
        new ProtectedSurface("spike-producer", "spikes/dafny-compat", "spikes/dafny-compat/**",
            "the determinism producer + projection surface."),
    };

    public static IEnumerable<object[]> ProtectedSurfaceIds() =>
        Rs012ProtectedSet.Select(s => new object[] { s.Id });

    // -------------------------------------------------------------------------------------------
    // (1) CODEOWNERS exists. Tests INV-025 [integration]: RED now (no `.github/CODEOWNERS` yet).
    // -------------------------------------------------------------------------------------------
    [Fact]
    public void Codeowners_file_exists()
    {
        var path = CodeownersPath();
        Assert.True(File.Exists(path),
            $"INV-025/RS-012: GREEN must create `.github/CODEOWNERS` (the out-of-band assumption " +
            $"surface EA-006 covers). Missing at: {path}");
    }

    // -------------------------------------------------------------------------------------------
    // (2) Coverage completeness (RS-012, PMB-003). Tests INV-025 [integration]: for EVERY pinned
    // protected surface, an @owned CODEOWNERS rule must COVER it. Driven from the pinned set, so a
    // partial CODEOWNERS fails CLOSED. RED now for all nine (no file).
    // -------------------------------------------------------------------------------------------
    [Theory]
    [MemberData(nameof(ProtectedSurfaceIds))]
    public void Codeowners_covers_every_rs012_protected_surface(string surfaceId)
    {
        var surface = Rs012ProtectedSet.Single(s => s.Id == surfaceId);
        var rules = ParseRules(ReadCodeowners());
        Assert.True(IsCovered(rules, surface.CodeownersToken),
            $"RS-012: no @OWNED CODEOWNERS rule covers protected surface '{surface.Id}' " +
            $"(need a rule whose pattern contains '{surface.CodeownersToken}' AND names an @owner). " +
            $"Surface: {surface.Note}");
    }

    // -------------------------------------------------------------------------------------------
    // (3) Every CODEOWNERS rule names an owner. Tests INV-025 [integration]: a pattern with no
    // @owner is a NO-OP that silently drops protection. RED now (no file).
    // -------------------------------------------------------------------------------------------
    [Fact]
    public void Every_codeowners_rule_names_an_owner()
    {
        var rules = ParseRules(ReadCodeowners());
        Assert.NotEmpty(rules); // a CODEOWNERS with zero owned rules covers nothing.
        var unowned = rules.Where(r => r.Owners.Count == 0).Select(r => r.Raw).ToList();
        Assert.True(unowned.Count == 0,
            "INV-025: every non-comment/non-blank CODEOWNERS line must name an @owner; " +
            "these are un-owned no-ops that silently drop protection:\n  " +
            string.Join("\n  ", unowned));
    }

    // -------------------------------------------------------------------------------------------
    // (4) Set-completeness / enumeration pin (RS-012, PMB-003/AP-022). Tests INV-025 [integration]:
    // the pinned protected set is EXACTLY the complete EA-006 enumeration (count pinned) AND every
    // surface is grounded in the committed EA-006 text (not an invented literal). PASSES now — a
    // guard against a future silent shrink of the set (dropping the lifecycle field / the pointers).
    //   LIMITATION (audit): pins the count to nine; a NEW surface ADDED to EA-006 in the spec is
    //   not auto-detected here — extend this set deliberately when EA-006 grows.
    // -------------------------------------------------------------------------------------------
    [Fact]
    public void Rs012_protected_set_is_the_complete_ea006_enumeration()
    {
        Assert.Equal(9, Rs012ProtectedSet.Count);
        Assert.Equal(Rs012ProtectedSet.Count, Rs012ProtectedSet.Select(s => s.Id).Distinct().Count());

        var ea006 = Norm(Ea006Section());
        foreach (var s in Rs012ProtectedSet)
        {
            Assert.True(ea006.Contains(Norm(s.SpecAnchor), StringComparison.Ordinal),
                $"RS-012: pinned surface '{s.Id}' anchor '{s.SpecAnchor}' not found in the committed " +
                $"EA-006 text — the pinned set has drifted from the spec's complete protected-path set.");
        }
    }

    // -------------------------------------------------------------------------------------------
    // (5a) Spec states the NARROWED threat claim. Tests INV-025/RS-039 [integration]: the asserted
    // guarantee is tampering/fabrication of evidence under an UNCHANGED reviewed verifier/workflow/
    // tool-pin/trust-policy. PASSES now (guard against a future edit that broadens the claim).
    // -------------------------------------------------------------------------------------------
    [Fact]
    public void Inv025_spec_states_the_narrowed_threat_claim()
    {
        var inv025 = Norm(Inv025Section());
        Assert.Contains(
            "tampering or fabrication of evidence under an unchanged, reviewed verifier, workflow, tool pin, and trust policy",
            inv025, StringComparison.Ordinal);
    }

    // -------------------------------------------------------------------------------------------
    // (5b) Spec labels the CODEOWNERS check as ASSUMPTION-completeness, NOT structural enforcement
    // (RS-039). Tests INV-025/RS-039 [integration]: guards against a future edit that REFRAMES the
    // presence check as real/cryptographic/structural enforcement. PASSES now (guard).
    // -------------------------------------------------------------------------------------------
    [Fact]
    public void Inv025_spec_labels_codeowners_as_assumption_completeness_not_structural_enforcement()
    {
        var inv025 = Norm(Inv025Section());
        Assert.Contains(
            "presence check is an ASSUMPTION-COMPLETENESS check (does the file COVER the protected paths)",
            inv025, StringComparison.Ordinal);
        Assert.Contains("NOT structural enforcement of the guarantee (RS-039)", inv025, StringComparison.Ordinal);
        // The concrete not-overstated evidence: a presence check cannot prove branch protection.
        Assert.Contains("a presence check passes even if branch protection is disabled", inv025, StringComparison.Ordinal);
        Assert.Contains("its strength is not overstated", inv025, StringComparison.Ordinal);
    }
}
