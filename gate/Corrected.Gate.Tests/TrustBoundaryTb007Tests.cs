using System;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace Corrected.Gate.Tests;

/// <summary>
/// P3 determinism-attestation spec INV-023 (~806-816) + the coupling INV-022 text
/// (~787-793): TB-007 — "trusted-CI evidence signing/verification" — is registered in
/// ARCHITECTURE.md as a DISTINCT trusted-CI evidence boundary (Crosses: trusted-CI
/// execution -> a durable, provenance-bound determinism claim; reusing TB-004 cosign
/// intake + TB-006 committed evidence as-defined); TB-003 (published-artifact release
/// provenance / bootstrap TCB) is UNCHANGED / not broadened / not relabeled; and the
/// `reference-ci-provenance` entrypoint is reconciled — its invariant-group map bullet no
/// longer claims INV-005/P3 (the determinism claim re-homes to TB-007 / readiness-build-gate)
/// and the future-tense "...reconciles... registers TB-007" placeholder note is resolved.
///
/// These are pure STRUCTURAL CONTENT SCANS of the committed doc `.correctless/ARCHITECTURE.md`
/// (read at test time via TestPaths.RepoFile, mirroring Inv027CrossDocConsistencyTests /
/// CosignPinTests). INV-023's "production" is the ARCHITECTURE edit GREEN makes — so the
/// RED-NOW cells fail as ASSERTIONS against the current (unreconciled) doc and pass once GREEN
/// edits it. No production stub is needed.
///
/// Cell taxonomy:
///   * RED-NOW  — TB-007 registered w/ structure; reference-ci map bullet drops INV-005;
///                determinism ownership sits with readiness-build-gate/TB-007; placeholder gone.
///   * GUARD    — TB-003 unchanged / not broadened (passes now; goes RED if GREEN broadens TB-003).
///
/// PMB-003 discipline: TB-007 presence is asserted as a STRUCTURED entry (heading + the
/// Crosses/Invariant boundary fields the other TB entries use), NOT a bare `TB-007` substring
/// (which already appears today inside the reference-ci-provenance placeholder note).
///
/// DEFER (deliberately NOT asserted here): naming + mapping the NEW two-job
/// determinism+signing WORKFLOW file to its own entrypoint — that workflow file is a T4
/// deliverable that does not exist yet.
///
/// AP-031 real-artifact clause is NOT triggered — ARCHITECTURE.md is a committed PROJECT doc,
/// not a `.correctless/artifacts/` producer output.
/// </summary>
public class TrustBoundaryTb007Tests
{
    private static string Arch() => File.ReadAllText(TestPaths.RepoFile(".correctless", "ARCHITECTURE.md"));

    /// <summary>Collapse ALL whitespace runs (incl. newlines) to a single space so a GREEN
    /// line-wrap reflow does not falsely satisfy an absence/presence assertion.</summary>
    private static string Norm(string s) => Regex.Replace(s, @"\s+", " ").Trim();

    /// <summary>The section from a markdown heading up to the next `##`/`###` heading (or EOF).
    /// Empty if the heading is absent (the heading-presence assert fires first in RED cells).</summary>
    private static string SectionFrom(string text, string startHeading)
    {
        int start = text.IndexOf(startHeading, StringComparison.Ordinal);
        if (start < 0) { return string.Empty; }
        int bodyFrom = start + startHeading.Length;
        Match m = Regex.Match(text.Substring(bodyFrom), @"\n#{2,3} ");
        int end = m.Success ? bodyFrom + m.Index : text.Length;
        return text.Substring(start, end - start);
    }

    /// <summary>The single map BULLET for `**name**` in the Entrypoint->invariant-group map,
    /// anchored on the markdown-bold form so it does not collide with the entrypoints YAML
    /// `- name: "name"`. Ends at the next map bullet / blank line / heading. Empty if absent.</summary>
    private static string MapBullet(string text, string name)
    {
        string anchor = "**" + name + "**";
        int a = text.IndexOf(anchor, StringComparison.Ordinal);
        if (a < 0) { return string.Empty; }
        int lineStart = text.LastIndexOf('\n', a) + 1;
        int end = text.Length;
        foreach (string stop in new[] { "\n- **", "\n\n", "\n#" })
        {
            int i = text.IndexOf(stop, a, StringComparison.Ordinal);
            if (i >= 0 && i < end) { end = i; }
        }
        return text.Substring(lineStart, end - lineStart);
    }

    // ==================================================================================
    // CLAUSE 1 — TB-007 registered as a STRUCTURED trusted-CI evidence boundary (RED-NOW).
    // ==================================================================================

    // Tests INV-023 [integration] ("a new TB-007 boundary is registered"): the ARCHITECTURE
    // Trust Boundaries section carries a `### TB-007:` boundary HEADING. RED now — only the
    // inline placeholder "registers TB-007" exists today, no heading. PMB-003: this is the
    // heading anchor, distinct from a bare-substring `TB-007` proxy.
    [Fact]
    public void Tb007_boundary_heading_is_registered()
    {
        Assert.Contains("### TB-007", Arch());
    }

    // Tests INV-023 [integration] ("... with the boundary fields the other TB entries use"):
    // the TB-007 entry is STRUCTURED — its body carries the Crosses + Invariant fields (mirroring
    // TB-004 / TB-006), not a bare token. RED now (no TB-007 section at all). PMB-003: asserts the
    // structured entry, not merely that "TB-007" appears somewhere in the file.
    [Fact]
    public void Tb007_entry_has_crosses_and_invariant_fields()
    {
        string section = Norm(SectionFrom(Arch(), "### TB-007"));
        Assert.Contains("Crosses", section);
        Assert.Contains("Invariant", section);
    }

    // Tests INV-023 [integration] ("crosses: trusted-CI execution -> a durable, provenance-bound
    // determinism claim"): the TB-007 body describes the RIGHT boundary — trusted-CI execution
    // producing a provenance-bound determinism claim via signing/verification. RED now.
    [Fact]
    public void Tb007_entry_describes_trusted_ci_provenance_bound_determinism_signing()
    {
        string section = Norm(SectionFrom(Arch(), "### TB-007"));
        Assert.Contains("determinism", section);
        Assert.Contains("trusted-CI", section);
        // signing/verification is the boundary's action.
        Assert.True(
            section.Contains("sign", StringComparison.OrdinalIgnoreCase)
            || section.Contains("verif", StringComparison.OrdinalIgnoreCase),
            "INV-023: TB-007 body must describe the signing/verification action");
    }

    // ==================================================================================
    // CLAUSE 2 — TB-003 UNCHANGED / not broadened / not relabeled (GUARD — passes now).
    // ==================================================================================

    // Tests INV-023 [integration] ("TB-003 ... is unchanged"): TB-003 still exists and still
    // describes published-artifact RELEASE PROVENANCE / bootstrap TCB. GUARD: passes now, goes
    // RED if GREEN deletes/relabels TB-003.
    [Fact]
    public void Tb003_still_describes_release_provenance_bootstrap_tcb()
    {
        string section = SectionFrom(Arch(), "### TB-003");
        Assert.Contains("Release provenance", section);
        Assert.Contains("SLSA Build", section);
    }

    // Tests INV-023 [integration] ("Violated when: ... TB-003 is silently broadened"): TB-003's
    // body does NOT gain this feature's trusted-CI determinism-signing claim — the new boundary is
    // TB-007, NOT a broadened/relabeled TB-003. GUARD: passes now, goes RED if GREEN broadens
    // TB-003 with the TB-007 determinism / trusted-CI wording. (TB-003 legitimately says "trusted
    // builder"; the asserted-absent phrase is the hyphenated "trusted-CI", not bare "trusted".)
    [Fact]
    public void Tb003_is_not_broadened_with_the_tb007_determinism_signing_claim()
    {
        string section = Norm(SectionFrom(Arch(), "### TB-003"));
        Assert.DoesNotContain("determinism", section);
        Assert.DoesNotContain("trusted-CI", section);
    }

    // ==================================================================================
    // CLAUSE 3 — reference-ci-provenance reconciled: map bullet drops INV-005/P3 (RED-NOW).
    // ==================================================================================

    // Tests INV-023 [integration] (INV-022 coupling ~789-791: "the INV-005/P3 claim is removed
    // from reference-ci-provenance"): the reference-ci-provenance MAP bullet no longer lists
    // INV-005. RED now — the bullet currently reads "... (TB-003); INV-005/031/032/033." Scoped to
    // the map bullet (anchored on the **bold** form) so INV-005 elsewhere (the entrypoints
    // test_via, which mentions INV-001/002/003/005) cannot falsely satisfy it.
    [Fact]
    public void Reference_ci_provenance_map_bullet_no_longer_lists_inv005()
    {
        string bullet = MapBullet(Arch(), "reference-ci-provenance");
        Assert.NotEqual(string.Empty, bullet);  // the bullet must exist (else scope drift)
        Assert.DoesNotContain("INV-005", bullet);
    }

    // Tests INV-023 [integration] ("the determinism claim re-homes to TB-007 / readiness-build-gate"):
    // the P3/determinism ownership now sits with the TB-007-mapped entrypoint — either the
    // readiness-build-gate map bullet or the TB-007 boundary section carries the determinism claim
    // (INV-005 or "determinism"). RED now — neither does today (the readiness-build-gate bullet
    // lists INV-001/002/003/004/036/043 with no determinism, and TB-007 does not yet exist). Not
    // over-pinned to one exact token/location so GREEN keeps latitude on where it re-homes.
    [Fact]
    public void Determinism_ownership_sits_with_readiness_build_gate_or_tb007()
    {
        string arch = Arch();
        string gateBullet = Norm(MapBullet(arch, "readiness-build-gate"));
        string tb007 = Norm(SectionFrom(arch, "### TB-007"));

        bool ownedByGate = gateBullet.Contains("INV-005") || gateBullet.Contains("determinism");
        bool ownedByTb007 = tb007.Contains("INV-005") || tb007.Contains("determinism");
        Assert.True(ownedByGate || ownedByTb007,
            "INV-023: the P3/determinism claim must re-home to readiness-build-gate / TB-007");
    }

    // ==================================================================================
    // CLAUSE 4 — the future-tense "...reconciles... registers TB-007" placeholder is GONE (RED-NOW).
    // ==================================================================================

    // Tests INV-023 [integration] (the placeholder note is resolved once reconciled): the
    // reference-ci-provenance test_via no longer carries the FUTURE-tense promise to reconcile
    // this entry and register TB-007 — that reconciliation has now happened. RED now (line ~94
    // carries "... reconciles this entry's handler + scope + invariant-group map and registers
    // TB-007"). Normalized so a GREEN reflow can't hide it. Targets the future-tense promise
    // specifically — a past-tense "reconciled by INV-023" note would (correctly) still pass.
    [Fact]
    public void Stale_future_tense_reconcile_and_register_tb007_placeholder_is_gone()
    {
        string norm = Norm(Arch());
        Assert.DoesNotContain("registers TB-007", norm);
        Assert.DoesNotContain("reconciles this entry", norm);
    }
}
