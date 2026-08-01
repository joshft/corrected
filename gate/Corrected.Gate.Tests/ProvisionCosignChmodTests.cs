using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Corrected.Gate.Tests;

/// <summary>
/// P3 determinism-attestation INV-015 / INV-017 (task #74 chmod fix): the provisioning script
/// <c>gate/tools/provision-cosign.sh</c> curl -o's the pinned cosign asset but never makes it
/// EXECUTABLE — so a later real invoke hits "Permission denied". This static shape scan (mirroring
/// <see cref="CosignPinTests"/>'s script scans) asserts the script <c>chmod +x</c>'s the DEST
/// binary, and does so AFTER the sha256 integrity check (never make an unverified asset runnable).
///
/// RED: the committed script has no chmod, so the presence + ordering assertions fail. GREEN adds
/// the <c>chmod +x "${DEST}"</c> line after the <c>sha256sum --check</c> step.
/// </summary>
public class ProvisionCosignChmodTests
{
    private static string ScriptPath() => TestPaths.RepoFile("gate", "tools", "provision-cosign.sh");

    private static string[] ScriptLines()
        => File.ReadAllText(ScriptPath()).Replace("\r\n", "\n").Split('\n');

    private static bool GrantsExecute(string line)
    {
        // Symbolic execute bit (+x / u+x / a+x) OR an octal mode whose OWNER digit sets execute
        // (owner digit in {1,3,5,7}, e.g. 0755 / 700 / 711).
        if (Regex.IsMatch(line, @"chmod\s+[^\s]*\+x"))
        {
            return true;
        }
        return Regex.IsMatch(line, @"chmod\s+0?[1357][0-7][0-7]\b");
    }

    // Tests INV-015 [integration] (task #74): the provisioning script contains a chmod that grants
    // an execute bit to the DEST binary. RED: the committed script has no chmod at all.
    [Fact]
    public void Provisioning_script_chmods_the_dest_binary_executable()
    {
        string[] lines = ScriptLines();
        string? chmodLine = lines.FirstOrDefault(l => l.Contains("chmod", StringComparison.Ordinal) && GrantsExecute(l));

        Assert.True(
            chmodLine is not null,
            "INV-015/task#74: provision-cosign.sh must chmod the provisioned binary executable " +
            "(else a real cosign invoke hits Permission denied). No execute-granting chmod found.");
        Assert.Contains("DEST", chmodLine!);
    }

    // Tests INV-015 [integration] (order — never make an UNVERIFIED asset runnable): the chmod +x
    // comes AFTER the sha256sum integrity check, so the binary is executable only once its digest is
    // confirmed against the reviewed hard-coded pin. RED: no chmod line exists.
    [Fact]
    public void Chmod_is_after_the_sha256_integrity_check()
    {
        string[] lines = ScriptLines();

        int sha256Index = Array.FindIndex(
            lines, l => l.Contains("sha256sum", StringComparison.Ordinal) && l.Contains("--check", StringComparison.Ordinal));
        int chmodIndex = Array.FindIndex(
            lines, l => l.Contains("chmod", StringComparison.Ordinal) && GrantsExecute(l));

        Assert.True(sha256Index >= 0, "INV-015: the sha256sum --check integrity step must be present.");
        Assert.True(chmodIndex >= 0, "INV-015/task#74: an execute-granting chmod must be present.");
        Assert.True(
            chmodIndex > sha256Index,
            "INV-015: the chmod +x must run AFTER the sha256sum integrity check — never make an " +
            "unverified asset executable.");
    }

    // Tests INV-015 [integration] (same target — a chmod on a DIFFERENT path cannot satisfy it): the
    // chmod's target token equals the `curl -o` DOWNLOAD target token (both ${DEST}), so making some
    // OTHER file executable does not pass. RED: no chmod line exists, so the chmod target token is
    // absent.
    [Fact]
    public void Chmod_target_equals_the_curl_download_target()
    {
        string[] lines = ScriptLines();

        // The `curl -o <target>` download target — the argument token right after `-o`.
        string? curlTarget = null;
        foreach (string line in lines)
        {
            if (!line.Contains("curl", StringComparison.Ordinal))
            {
                continue;
            }
            string[] toks = Tokenize(line);
            int oi = Array.FindIndex(toks, t => t == "-o");
            if (oi >= 0 && oi + 1 < toks.Length)
            {
                curlTarget = StripQuotes(toks[oi + 1]);
                break;
            }
        }

        // The chmod's target token — the LAST token of the execute-granting chmod line.
        string? chmodTarget = null;
        foreach (string line in lines)
        {
            if (line.Contains("chmod", StringComparison.Ordinal) && GrantsExecute(line))
            {
                string[] toks = Tokenize(line);
                if (toks.Length > 0)
                {
                    chmodTarget = StripQuotes(toks[^1]);
                }
                break;
            }
        }

        Assert.True(curlTarget is not null, "INV-015: the script must curl -o the pinned asset to a DEST target.");
        Assert.True(chmodTarget is not null, "INV-015/task#74: an execute-granting chmod must target a path.");
        Assert.Equal(curlTarget, chmodTarget);
        // Belt-and-suspenders: the shared target is the DEST variable, not a hard-coded other path.
        Assert.Contains("DEST", chmodTarget!);
    }

    private static string[] Tokenize(string line)
        => line.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

    // Strip a single layer of surrounding double/single quotes so `"${DEST}"` and `${DEST}` compare
    // equal (the token comparison is about the referenced path, not the quoting).
    private static string StripQuotes(string token)
    {
        if (token.Length >= 2 &&
            ((token[0] == '"' && token[^1] == '"') || (token[0] == '\'' && token[^1] == '\'')))
        {
            return token.Substring(1, token.Length - 2);
        }
        return token;
    }
}
