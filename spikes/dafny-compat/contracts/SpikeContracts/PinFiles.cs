// PR-007: the committed pin files (config/z3-pin.json, config/net8-control-pin.json)
// are CONSUMED by the execution path, not only certified by the canonical
// suite. Route children and the aggregator startup-anchor the z3 pin (it feeds
// P04 / executed-solver identity and supplies glibc_floor for the run report's
// binding identity); the net8 control cells and the aggregator startup-anchor
// the control pin. Corrupting either file is now a fast typed runtime refusal,
// never a green variance run.
//
// COORDINATION (shell agent): the URL/SHA literals inside scripts/run-spike.sh
// and scripts/provision-z3.sh remain the shell layer's to source from these
// same files; this class owns the C#-side anchoring only. The compiled
// constants below are the trust anchors (same pattern as the evidence-schema
// anchor, codex R3-6): a colluding edit of the pin FILE alone is rejected here.

using System.Text.Json;

namespace Corrected.Spike.Contracts;

public static class PinFiles
{
    /// <summary>Compiled-in anchor for config/net8-control-pin.json (BND-004); the test suite asserts it equals SpecConstants.Net8ControlArchiveSha256.</summary>
    public const string Net8ControlArchiveSha256Anchor = "dba346c5c4357e1befebf14de8c8ee7f09313cc12c7c0015a4cdd4dfd0efba81";
    public const string Net8ControlRuntimeVersionAnchor = "8.0.29";

    public sealed record Z3Pin(string Version, string Sha256, string GlibcFloor, string InstallRelativeToRunRoot);

    /// <summary>
    /// Reads AND anchors config/z3-pin.json: version, archive sha256, and the
    /// install path must equal the compiled SolverLayout constants; glibc_floor
    /// must be present (it is emitted into run-report binding identity —
    /// MA-ED-2). Throws a typed refusal on any mismatch (fail closed).
    /// </summary>
    public static Z3Pin ValidateZ3Pin(string spikeRoot)
    {
        var path = Path.Combine(spikeRoot, "config", "z3-pin.json");
        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"z3 pin file missing at {path} — the solver pin must be committed (BND-002/PR-007)");
        }
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var root = doc.RootElement;
        var version = root.GetProperty("version").GetString() ?? "";
        var sha = root.GetProperty("sha256").GetString() ?? "";
        var glibc = root.TryGetProperty("glibc_floor", out var g) ? g.GetString() ?? "" : "";
        var install = root.TryGetProperty("install_relative_to_run_root", out var inst) ? inst.GetString() ?? "" : "";
        if (version != SolverLayout.Z3Version)
        {
            throw new InvalidOperationException(
                $"z3-pin.json declares version '{version}' but the compiled pin is '{SolverLayout.Z3Version}' — pin-file corruption is refused at runtime (PR-007)");
        }
        if (sha != SolverLayout.Z3PinnedSha256)
        {
            throw new InvalidOperationException(
                $"z3-pin.json declares archive sha256 '{sha}' but the compiled pin is '{SolverLayout.Z3PinnedSha256}' — pin-file corruption is refused at runtime (PR-007)");
        }
        if (string.IsNullOrEmpty(glibc))
        {
            throw new InvalidOperationException("z3-pin.json carries no glibc_floor — EA-002 requires it recorded in evidence (PR-007/MA-ED-2)");
        }
        if (install != SolverLayout.SolverRelativePath)
        {
            throw new InvalidOperationException(
                $"z3-pin.json install path '{install}' diverges from the compiled layout '{SolverLayout.SolverRelativePath}' (PR-007)");
        }
        return new Z3Pin(version, sha, glibc, install);
    }

    /// <summary>
    /// Reads AND anchors config/net8-control-pin.json: archive sha256 and
    /// runtime version must equal the compiled anchors, and the per-file
    /// net8_expected_identities block must be present (TA-B12). Throws a typed
    /// refusal on any mismatch (fail closed).
    /// </summary>
    public static void ValidateNet8ControlPin(string spikeRoot)
    {
        var path = Path.Combine(spikeRoot, "config", "net8-control-pin.json");
        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"net8 control pin file missing at {path} (BND-004/PR-007)");
        }
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var root = doc.RootElement;
        var version = root.GetProperty("runtime_version").GetString() ?? "";
        var sha = root.GetProperty("sha256").GetString() ?? "";
        if (version != Net8ControlRuntimeVersionAnchor)
        {
            throw new InvalidOperationException(
                $"net8-control-pin.json declares runtime_version '{version}' but the compiled pin is '{Net8ControlRuntimeVersionAnchor}' — pin-file corruption is refused at runtime (PR-007)");
        }
        if (sha != Net8ControlArchiveSha256Anchor)
        {
            throw new InvalidOperationException(
                $"net8-control-pin.json declares sha256 '{sha}' but the compiled pin is '{Net8ControlArchiveSha256Anchor}' — pin-file corruption is refused at runtime (PR-007)");
        }
        if (!root.TryGetProperty("net8_expected_identities", out var identities)
            || !identities.TryGetProperty("hostfxr_expected_digest", out _)
            || !identities.TryGetProperty("corelib_expected_digest", out _))
        {
            throw new InvalidOperationException(
                "net8-control-pin.json lacks the net8_expected_identities per-file digests — control identity proof impossible (TA-B12/PR-007)");
        }
    }
}
