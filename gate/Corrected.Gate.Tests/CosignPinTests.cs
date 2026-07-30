using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Corrected.Gate;
using Xunit;

namespace Corrected.Gate.Tests;

/// <summary>
/// P3 determinism-attestation spec INV-015 (cosign VERSION pin, ~563-579): the cosign
/// binary is pinned to exactly one version + one per-RID digest, bootstrapped
/// non-circularly. DISTINCT from the carrier's same-numbered aggregator invariant
/// (<see cref="Inv015PinnedToolchainTests"/> - the .NET YamlDotNet/Roslyn/test-host
/// toolchain pin, left untouched). This file exercises the cosign-binary pin only.
///
/// Split of concerns (RED phase):
///   * ACCESSOR value cells - go through the stubbed <see cref="CosignPin.Load"/> and
///     therefore FAIL as assertions until GREEN wires the JSON reader.
///   * STATIC-SCAN / FLOOR cells - read the committed provisioning artifacts
///     (gate/tools/cosign-pin.json + gate/tools/provision-cosign.sh) directly and PASS
///     as soon as those artifacts are clean (they assert absence / config properties).
///
/// Frozen facts (OQ-001, committed to the spec):
///   version v3.1.2; cosign-linux-amd64 (linux-x64) sha256
///   f7622ed3cf22e55e1ae6377c080979ff77a22da9981c11df222a2e444991e7cf;
///   advisory floors v3.0.6 (GHSA-w6c6-c85g-mmv6 / CVE-2026-39395) and v3.0.4
///   (GHSA-whqx-f9j3-ch6m); bootstrap = reviewed hard-coded sha256 + authenticated URL,
///   never cosign-verifying-cosign, never "latest"/range/floating.
/// </summary>
public class CosignPinTests
{
    private const string FrozenVersion = "v3.1.2";
    private const string FrozenLinuxRid = "linux-x64";
    private const string FrozenLinuxAsset = "cosign-linux-amd64";
    private const string FrozenLinuxSha256 =
        "f7622ed3cf22e55e1ae6377c080979ff77a22da9981c11df222a2e444991e7cf";
    private const string PinnedReleaseHost = "github.com";
    private const string PinnedOrgPath = "sigstore/cosign";
    // Frozen advisory-floor spec values (B1): the floors themselves are pinned facts, not
    // just an ordering. Anchoring both the production constant AND the >= comparison RHS to
    // these literals keeps the floor enforcement from silently weakening (e.g. a drift of the
    // CVE floor to v3.0.4 would let a still-vulnerable v3.0.5 pin satisfy ">= floor").
    private const string FrozenFloorCve = "v3.0.6";   // GHSA-w6c6-c85g-mmv6 / CVE-2026-39395
    private const string FrozenFloorWhqx = "v3.0.4";  // GHSA-whqx-f9j3-ch6m

    private static string ConfigPath() => TestPaths.RepoFile("gate", "tools", "cosign-pin.json");
    private static string ScriptPath() => TestPaths.RepoFile("gate", "tools", "provision-cosign.sh");

    private static string ReadConfig() => File.ReadAllText(ConfigPath());
    private static string ReadScript() => File.ReadAllText(ScriptPath());

    // ---------------------------------------------------------------------------------
    // Infra: the committed provisioning artifacts must exist, else a rule test that
    // fails would fail for a missing-file reason rather than the intended one.
    // ---------------------------------------------------------------------------------

    // Tests INV-015 [integration]: the committed provisioning config + script exist.
    [Fact]
    public void Committed_provisioning_config_and_script_exist()
    {
        Assert.True(File.Exists(ConfigPath()), $"INV-015: {ConfigPath()} must be committed");
        Assert.True(File.Exists(ScriptPath()), $"INV-015: {ScriptPath()} must be committed");
    }

    // ---------------------------------------------------------------------------------
    // ACCESSOR value cells - RED against the stubbed CosignPin.Load() (empty config).
    // ---------------------------------------------------------------------------------

    // Tests INV-015 [integration] ("pinned to exactly one version"): the accessor resolves
    // to the SINGLE frozen version string v3.1.2 (a lone string field - not a list/range).
    // RED: Load() is stubbed to an empty config, so Version == "" != "v3.1.2".
    [Fact]
    public void Accessor_resolves_to_the_single_pinned_version_v3_1_2()
    {
        CosignPinConfig cfg = CosignPin.Load();
        Assert.Equal(FrozenVersion, cfg.Version);
        // Exactly one version: the resolved Version carries no range/list operator.
        foreach (string tok in new[] { "latest", ">=", "^", "~", "*", "," })
        {
            Assert.DoesNotContain(tok, cfg.Version);
        }
    }

    // Tests INV-015 [integration] ("one per-RID digest ... hard-coded SHA-256"): the accessor
    // resolves EXACTLY ONE sha256 for the linux-x64 RID, equal to the frozen digest, on the
    // frozen asset name. RED: ForRid() is stubbed to null and Rids is empty.
    [Fact]
    public void Accessor_resolves_exactly_one_sha256_for_linux_x64_equal_to_frozen()
    {
        CosignPinConfig cfg = CosignPin.Load();

        CosignRidPin? pin = cfg.ForRid(FrozenLinuxRid);
        Assert.NotNull(pin);
        Assert.Equal(FrozenLinuxAsset, pin!.AssetName);
        Assert.Equal(FrozenLinuxSha256, pin.Sha256);

        // Exactly one entry for that RID - a range/list of digests would violate the pin.
        Assert.Single(cfg.Rids.Where(r => r.Rid == FrozenLinuxRid));
    }

    // Tests INV-015 [integration] (deny-by-default edge): an un-pinned RID resolves to null,
    // never a silently-floating pin. The stub already denies, so this documents the contract.
    [Fact]
    public void Accessor_forrid_unknown_rid_returns_null_deny_by_default()
    {
        CosignPinConfig cfg = CosignPin.Load();
        Assert.Null(cfg.ForRid("totally-unknown-rid-xyz"));
    }

    // ---------------------------------------------------------------------------------
    // STATIC config/script scans + floor - read committed bytes directly (GREEN).
    // ---------------------------------------------------------------------------------

    // Tests INV-015 [integration] ("hard-coded SHA-256 ... one version"): the committed config
    // literally pins a SINGLE version v3.1.2 (exactly one "version" key, value == frozen).
    [Fact]
    public void Committed_config_pins_a_single_frozen_version_v3_1_2()
    {
        using JsonDocument doc = JsonDocument.Parse(ReadConfig());
        JsonElement root = doc.RootElement;

        Assert.True(root.TryGetProperty("version", out JsonElement version),
            "INV-015: cosign-pin.json must carry a single 'version'");
        Assert.Equal(JsonValueKind.String, version.ValueKind);
        Assert.Equal(FrozenVersion, version.GetString());
    }

    // Tests INV-015 [integration] ("hard-coded digest present & well-formed"): the committed
    // linux-amd64 asset sha256 is a 64-lowercase-hex literal equal to the frozen value.
    [Fact]
    public void Committed_linux_amd64_digest_is_64_lowercase_hex_equal_to_frozen()
    {
        CosignRidPin linux = CommittedRid(FrozenLinuxRid);
        Assert.Equal(FrozenLinuxAsset, linux.AssetName);
        Assert.Matches(new Regex("^[0-9a-f]{64}$"), linux.Sha256);
        Assert.Equal(FrozenLinuxSha256, linux.Sha256);
    }

    // Tests INV-015 [integration] (no floating digest, defensive over EVERY rid): every
    // committed per-RID digest is a pinned 64-lowercase-hex literal - never a floating /
    // "sha256:latest" style placeholder.
    [Fact]
    public void Every_committed_rid_digest_is_a_pinned_64_hex_literal()
    {
        IReadOnlyList<CosignRidPin> rids = CommittedRids();
        Assert.NotEmpty(rids);
        var seenRids = new HashSet<string>(StringComparer.Ordinal);
        foreach (CosignRidPin rid in rids)
        {
            Assert.Matches(new Regex("^[0-9a-f]{64}$"), rid.Sha256);
            // Exactly one entry per RID (no duplicate/ambiguous pin).
            Assert.True(seenRids.Add(rid.Rid), $"INV-015: duplicate pin for RID '{rid.Rid}'");
        }
    }

    // Tests INV-015 [unit] (B1: floors are pinned facts, not just an ordering): the PRODUCTION
    // advisory-floor constants EQUAL their frozen spec values, so the floor comparison cannot
    // become vacuous by a constant drifting downward (a v3.0.4 CVE floor would admit a still-
    // vulnerable v3.0.5 pin). Locks the constants exactly like the version/digest are locked.
    [Fact]
    public void Advisory_floor_constants_equal_their_frozen_spec_values()
    {
        Assert.Equal(FrozenFloorCve, CosignPin.AdvisoryFloorCve202639395);
        Assert.Equal(FrozenFloorWhqx, CosignPin.AdvisoryFloorGhsaWhqx);
        // The frozen floors themselves must be well-formed semver (so the compare is meaningful).
        Assert.True(TryParseSemver(FrozenFloorCve, out _));
        Assert.True(TryParseSemver(FrozenFloorWhqx, out _));
    }

    // Tests INV-015 [integration] ("chosen at/after the advisory floors"): the committed
    // pinned version parses to a semver >= v3.0.6 AND >= v3.0.4 (both floors). The comparison
    // RHS is anchored to the FROZEN floor literals AND the production constants are asserted to
    // equal them (B1), so neither the constant nor the RHS can silently weaken this - the only
    // floor enforcement. Reads the committed version (static) so it holds independently of the
    // accessor stub.
    [Fact]
    public void Committed_pinned_version_is_at_or_above_both_advisory_floors()
    {
        using JsonDocument doc = JsonDocument.Parse(ReadConfig());
        string pinned = doc.RootElement.GetProperty("version").GetString()!;

        Assert.True(TryParseSemver(pinned, out (int Major, int Minor, int Patch) pv),
            $"INV-015: pinned version '{pinned}' must be a v-prefixed semver");

        // B1: bind the production floor constants to the frozen literals used as the RHS.
        Assert.Equal(FrozenFloorCve, CosignPin.AdvisoryFloorCve202639395);
        Assert.Equal(FrozenFloorWhqx, CosignPin.AdvisoryFloorGhsaWhqx);
        Assert.True(TryParseSemver(FrozenFloorCve, out var cve));
        Assert.True(TryParseSemver(FrozenFloorWhqx, out var whqx));

        Assert.True(CompareSemver(pv, cve) >= 0,
            $"INV-015: pinned {pinned} must be >= CVE-2026-39395 floor {FrozenFloorCve}");
        Assert.True(CompareSemver(pv, whqx) >= 0,
            $"INV-015: pinned {pinned} must be >= GHSA-whqx-f9j3-ch6m floor {FrozenFloorWhqx}");
    }

    // Tests INV-015 [integration] ("never 'latest' ... not a range ... floating digest"): a
    // static scan over BOTH the committed config and script bytes finds none of the tokens
    // that would indicate a "latest"/range/floating selector.
    [Fact]
    public void Committed_config_and_script_contain_no_latest_range_or_floating_token()
    {
        string[] forbidden = { "latest", ">=", "^", "~", "*" };
        string config = ReadConfig();
        string script = ReadScript();

        foreach (string tok in forbidden)
        {
            Assert.False(config.Contains(tok, StringComparison.OrdinalIgnoreCase),
                $"INV-015: cosign-pin.json must not contain floating/range token '{tok}'");
            Assert.False(script.Contains(tok, StringComparison.OrdinalIgnoreCase),
                $"INV-015: provision-cosign.sh must not contain floating/range token '{tok}'");
        }
    }

    // Tests INV-015 [integration] ("never cosign-verifying-cosign"): a static scan asserts the
    // provisioning path does NOT self-verify the cosign binary with the signing tool itself -
    // the bootstrap integrity is the hard-coded sha256 only.
    [Fact]
    public void Provisioning_script_does_not_self_verify_cosign_with_cosign()
    {
        string script = ReadScript();
        foreach (string selfVerify in new[] { "cosign verify", "verify-blob" })
        {
            Assert.False(script.Contains(selfVerify, StringComparison.OrdinalIgnoreCase),
                $"INV-015: provision-cosign.sh must not self-verify (found '{selfVerify}')");
        }
    }

    // Tests INV-015 [integration] ("bootstrap ... reviewed hard-coded SHA-256"): the
    // provisioning script establishes asset integrity via a plain sha256sum / hard-coded-hash
    // comparison (the non-circular trust anchor).
    [Fact]
    public void Provisioning_script_verifies_asset_via_sha256sum()
    {
        string script = ReadScript();
        Assert.Contains("sha256sum", script);
        // The hard-coded digest is drawn from the committed pin config (the '"sha256"' field).
        Assert.Contains("\"sha256\"", script);
    }

    // Tests INV-015 [integration] ("+ an authenticated source URL"): every per-RID url is an
    // https:// URL to the pinned authenticated release host (github.com), never http/unpinned.
    // A1: the URL must also be internally consistent with the config - its path must name the
    // SAME version, the RID's asset, and the pinned sigstore/cosign org - so a version/asset-
    // mismatched download URL (e.g. config version v3.1.2 but URL points at a different
    // version's asset) cannot pass while the resolved pin claims a different asset.
    [Fact]
    public void Every_rid_url_is_https_to_the_pinned_github_release_host()
    {
        using JsonDocument doc = JsonDocument.Parse(ReadConfig());
        string version = doc.RootElement.GetProperty("version").GetString()!;

        IReadOnlyList<CosignRidPin> rids = CommittedRids();
        Assert.NotEmpty(rids);
        foreach (CosignRidPin rid in rids)
        {
            Assert.True(Uri.TryCreate(rid.Url, UriKind.Absolute, out Uri? uri),
                $"INV-015: RID '{rid.Rid}' url must be an absolute URL: '{rid.Url}'");
            Assert.Equal(Uri.UriSchemeHttps, uri!.Scheme);
            Assert.Equal(PinnedReleaseHost, uri.Host);

            // A1: the download path must be internally consistent with the config.
            string path = uri.AbsolutePath;
            Assert.Contains(version, path);         // same version as the config pin
            Assert.Contains(rid.AssetName, path);   // the RID's own asset, not a different one
            Assert.Contains(PinnedOrgPath, path);   // the pinned sigstore/cosign release org
        }
    }

    // ---------------------------------------------------------------------------------
    // Semver helper sanity - proves the floor assertion is not vacuous.
    // ---------------------------------------------------------------------------------

    // Tests INV-015 [unit]: the test-local semver compare orders known pairs correctly, so the
    // floor assertion genuinely distinguishes below-floor from at/above-floor versions.
    [Fact]
    public void Semver_compare_orders_known_pairs()
    {
        Assert.True(TryParseSemver("v3.1.2", out var a));
        Assert.True(TryParseSemver("v3.0.6", out var b));
        Assert.True(TryParseSemver("v3.0.4", out var c));
        Assert.True(TryParseSemver("v2.9.9", out var below));

        Assert.True(CompareSemver(a, b) > 0);      // v3.1.2 > v3.0.6
        Assert.True(CompareSemver(b, c) > 0);      // v3.0.6 > v3.0.4
        Assert.Equal(0, CompareSemver(a, a));      // reflexive
        Assert.True(CompareSemver(below, b) < 0);  // v2.9.9 < v3.0.6 (below floor)
        Assert.False(TryParseSemver("not-a-version", out _));
        Assert.False(TryParseSemver("", out _));
    }

    // ---------------------------------------------------------------------------------
    // Helpers (test-local - implementation logic is fine in the test file).
    // ---------------------------------------------------------------------------------

    private static IReadOnlyList<CosignRidPin> CommittedRids()
    {
        using JsonDocument doc = JsonDocument.Parse(ReadConfig());
        var list = new List<CosignRidPin>();
        foreach (JsonElement el in doc.RootElement.GetProperty("rids").EnumerateArray())
        {
            list.Add(new CosignRidPin(
                el.GetProperty("rid").GetString()!,
                el.GetProperty("assetName").GetString()!,
                el.GetProperty("sha256").GetString()!,
                el.GetProperty("url").GetString()!));
        }
        return list;
    }

    private static CosignRidPin CommittedRid(string rid)
    {
        CosignRidPin? found = CommittedRids().SingleOrDefault(r => r.Rid == rid);
        Assert.NotNull(found);
        return found!;
    }

    // v-prefixed 3-component semver parse; ignores any pre-release suffix on the patch.
    private static bool TryParseSemver(string s, out (int Major, int Minor, int Patch) v)
    {
        v = default;
        if (string.IsNullOrWhiteSpace(s))
        {
            return false;
        }
        string core = s.StartsWith('v') || s.StartsWith('V') ? s.Substring(1) : s;
        string[] parts = core.Split('.');
        if (parts.Length != 3)
        {
            return false;
        }
        string patch = parts[2].Split('-', '+')[0];
        if (!int.TryParse(parts[0], out int major) ||
            !int.TryParse(parts[1], out int minor) ||
            !int.TryParse(patch, out int patchNum))
        {
            return false;
        }
        v = (major, minor, patchNum);
        return true;
    }

    private static int CompareSemver(
        (int Major, int Minor, int Patch) a, (int Major, int Minor, int Patch) b)
    {
        if (a.Major != b.Major) return a.Major.CompareTo(b.Major);
        if (a.Minor != b.Minor) return a.Minor.CompareTo(b.Minor);
        return a.Patch.CompareTo(b.Patch);
    }
}
