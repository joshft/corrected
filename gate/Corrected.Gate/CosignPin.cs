using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Corrected.Gate;

/// <summary>
/// A single per-RID cosign asset pin (P3 determinism-attestation INV-015 / OQ-001):
/// the runtime identifier, the release asset file name, the reviewed hard-coded
/// SHA-256 of that exact asset, and the authenticated release URL it is fetched from.
/// Immutable value.
/// </summary>
public sealed record CosignRidPin(string Rid, string AssetName, string Sha256, string Url);

/// <summary>
/// The resolved cosign toolchain pin: EXACTLY ONE frozen <see cref="Version"/> string
/// (never a range, never "latest") plus a per-RID digest set. Immutable.
/// </summary>
public sealed record CosignPinConfig(string Version, IReadOnlyList<CosignRidPin> Rids)
{
    /// <summary>
    /// Resolve the pin for a runtime identifier, or <c>null</c> when the RID is not
    /// pinned (deny-by-default — an unknown RID never silently floats). Matching is by
    /// exact ordinal RID equality; the committed config carries exactly one entry per
    /// RID, so the first ordinal match is the only match.
    /// </summary>
    public CosignRidPin? ForRid(string rid)
    {
        if (rid is null)
        {
            return null;
        }

        foreach (CosignRidPin pin in Rids)
        {
            if (string.Equals(pin.Rid, rid, StringComparison.Ordinal))
            {
                return pin;
            }
        }

        // Deny-by-default: an un-pinned RID resolves to null, never a floating pin.
        return null;
    }
}

/// <summary>
/// INV-015 (P3 determinism-attestation spec, ~563–579): the <c>cosign</c> binary is
/// pinned to EXACTLY ONE version and ONE per-RID digest (not a range / "latest" /
/// floating digest), chosen at/after the advisory floors, bootstrapped NON-CIRCULARLY
/// via a reviewed hard-coded SHA-256 + authenticated URL — never cosign-verifying-cosign.
///
/// This accessor loads the committed <c>gate/tools/cosign-pin.json</c> provisioning
/// config into a typed <see cref="CosignPinConfig"/>. It is BCL-only.
///
/// Distinct from the carrier's same-numbered aggregator invariant
/// (<c>Inv015PinnedToolchainTests</c> — the .NET YAML/Roslyn/test-host toolchain pin);
/// this is the cosign-binary version pin only.
/// </summary>
public static class CosignPin
{
    /// <summary>Advisory floor: GHSA-w6c6-c85g-mmv6 / CVE-2026-39395 fixed in this version.</summary>
    public const string AdvisoryFloorCve202639395 = "v3.0.6";

    /// <summary>Advisory floor: the distinct GHSA-whqx-f9j3-ch6m fixed in this version.</summary>
    public const string AdvisoryFloorGhsaWhqx = "v3.0.4";

    /// <summary>The committed provisioning config, relative to the repo root sentinel.</summary>
    private static readonly string[] ConfigRelativePath = { "gate", "tools", "cosign-pin.json" };

    /// <summary>
    /// Load the committed cosign pin config into a typed record. The config path is
    /// resolved from the repo root (the directory containing the <c>.correctless/</c>
    /// sentinel) by walking up from the loaded assembly's base directory, so the value
    /// comes FROM the committed JSON — not from C# literals. Fails loudly (throws) on a
    /// missing or malformed file rather than returning silent, safe-wrong defaults.
    /// </summary>
    public static CosignPinConfig Load()
    {
        string path = ResolveConfigPath();

        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"INV-015: cosign pin config not found at '{path}' — it must be committed.", path);
        }

        string json = File.ReadAllText(path);

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"INV-015: cosign pin config at '{path}' is not valid JSON.", ex);
        }

        using (doc)
        {
            JsonElement root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException(
                    $"INV-015: cosign pin config at '{path}' must be a JSON object.");
            }

            string version = ReadRequiredString(root, "version", path);

            if (!root.TryGetProperty("rids", out JsonElement rids) ||
                rids.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidOperationException(
                    $"INV-015: cosign pin config at '{path}' must carry a 'rids' array.");
            }

            var pins = new List<CosignRidPin>();
            foreach (JsonElement el in rids.EnumerateArray())
            {
                if (el.ValueKind != JsonValueKind.Object)
                {
                    throw new InvalidOperationException(
                        $"INV-015: each entry in 'rids' of '{path}' must be a JSON object.");
                }

                pins.Add(new CosignRidPin(
                    ReadRequiredString(el, "rid", path),
                    ReadRequiredString(el, "assetName", path),
                    ReadRequiredString(el, "sha256", path),
                    ReadRequiredString(el, "url", path)));
            }

            return new CosignPinConfig(version, pins);
        }
    }

    /// <summary>
    /// Read a required non-null string property, throwing loudly if it is absent or of
    /// the wrong JSON kind. Keeps <see cref="Load"/> from silently admitting a malformed
    /// pin (deny-by-default at parse time).
    /// </summary>
    private static string ReadRequiredString(JsonElement obj, string name, string path)
    {
        if (!obj.TryGetProperty(name, out JsonElement value) ||
            value.ValueKind != JsonValueKind.String)
        {
            throw new InvalidOperationException(
                $"INV-015: cosign pin config at '{path}' must carry a string '{name}'.");
        }

        return value.GetString()!;
    }

    /// <summary>
    /// Resolve the committed config path by walking up from the loaded assembly's base
    /// directory to the repo-root sentinel (the directory containing <c>.correctless/</c>).
    /// Mirrors the test harness' repo-root discovery so <see cref="Load"/> deterministically
    /// finds the SAME committed file regardless of the process working directory.
    /// </summary>
    private static string ResolveConfigPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, ".correctless")))
            {
                return Path.Combine(dir.FullName, Path.Combine(ConfigRelativePath));
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException(
            "INV-015: repo-root sentinel (.correctless/) not found — cannot locate cosign pin config.");
    }
}
