using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Corrected.Gate.Tests;

/// <summary>
/// Shared test harness for the P3 determinism-attestation T4 SIGNER slice (INV-007/008/009/024).
/// This is test infrastructure (real helper logic — NOT a production stub, so no STUB:TDD marker),
/// mirroring the fake-cosign subprocess pattern in <see cref="CosignSubprocessSeamTests"/>.
///
/// It defines — and therefore PINS, as the RED-phase contract GREEN must implement — the operator
/// interface of the not-yet-existent extracted signer script
/// <c>gate/tools/sign-determinism.sh</c>:
///
///   DOCUMENTED INVOCATION (run from the repo root, argv[0] is the RELATIVE path, AP-020):
///     GITHUB_SHA=&lt;40hex&gt; GITHUB_RUN_ID=&lt;digits&gt; GITHUB_RUN_ATTEMPT=1 COSIGN_BIN=&lt;abs cosign&gt; \
///       bash gate/tools/sign-determinism.sh \
///         --artifacts-dir &lt;DIR&gt; --manifest &lt;MANIFEST_FILE&gt; --out &lt;BUNDLE_OUT&gt;
///
///   PRODUCER HAND-OFF (&lt;DIR&gt;, same-run @actions/artifact contents — RS-032/EA-010):
///     * determinism-receipt.json — the RunReceipt / in-toto subject-statement input, string fields:
///         schema_version, attested_commit (40hex), run_id, run_attempt, producing_job_result,
///         execution_status ("completed"), comparison_status ("equal"),
///         subject_manifest_digest (lowercase 64-hex SHA-256 of the manifest file bytes).
///     * receipt.sha256 — the producer-DECLARED digest (bare lowercase 64-hex) of
///         determinism-receipt.json; the signer recomputes SHA-256 of the receipt bytes and
///         REFUSES on mismatch (tamper re-check).
///
///   RE-CHECK CONTRACT (INV-007): before ANY cosign call the signer REFUSES (non-zero exit,
///   a line containing "REFUSE" on stderr, NO cosign invocation) when ANY of these disagree
///   with the trusted source:
///     digest        receipt.sha256 != actual SHA-256(determinism-receipt.json)
///     schema        receipt.schema_version != the pinned RunReceipt schema id (ReceiptSchemaId)
///     attested_commit  receipt.attested_commit != $GITHUB_SHA
///     run_id           receipt.run_id != $GITHUB_RUN_ID
///     producing-job    receipt.producing_job_result != "success"
///     manifest         receipt.subject_manifest_digest != SHA-256(&lt;MANIFEST_FILE&gt;)
///
///   ATTEMPT GUARD (INV-008): $GITHUB_RUN_ATTEMPT must be exactly "1" (and receipt.run_attempt
///   must agree); a missing/empty/&gt;1 attempt REFUSES fail-closed with a message naming
///   "re-runs never mint" / "push a new reviewed commit" (RS-036).
///
///   SIGNING SEAM (INV-009): on all checks passing the signer invokes the pinned cosign
///     $COSIGN_BIN attest-blob --statement &lt;stmt&gt; --bundle &lt;out&gt; --new-bundle-format=true --yes &lt;blob&gt;
///   The cosign executable is resolved from an injectable env override COSIGN_BIN (the test seam):
///   when COSIGN_BIN is set the signer uses it VERBATIM (no per-RID digest check on the injected
///   double — the digest pin governs the DEFAULT provisioned binary path only). The pinned
///   cosign version/digest are single-sourced from gate/tools/cosign-pin.json (never a divergent
///   in-script literal), and the --new-bundle-format=false contingency is NOT taken (DD-002).
///
/// NOTE: everything here is the RED contract. The five subprocess test classes exercise it; the
/// script does not exist yet, so every behavioral assertion is RED against its ABSENCE (each
/// runner first asserts the script exists, so a RED failure reads as "missing script", never a
/// vacuous 127 that masquerades as a refusal — AP-010).
/// </summary>
internal static class P3SignerHarness
{
    // A deterministic, valid 40-hex trigger commit + run id used across the fixtures.
    internal const string TrustedSha = "a94a8fe5ccb19ba61c4c0873d391e987982fbbd3";
    internal const string TrustedRunId = "30511722581";

    // The pinned RunReceipt schema id the signer's `schema` re-check (INV-007) validates against.
    // GREEN pins the expected schema to EXACTLY this value; an off-contract schema_version refuses.
    internal const string ReceiptSchemaId = "corrected/determinism-runreceipt@v1";

    internal static string SignerScriptRepoRelPath => "gate/tools/sign-determinism.sh";

    internal static string SignerScriptAbsPath()
        => TestPaths.RepoFile("gate", "tools", "sign-determinism.sh");

    /// <summary>The committed cosign version pin, read from cosign-pin.json (single source of truth).</summary>
    internal static string PinnedCosignVersion()
    {
        using JsonDocument doc = JsonDocument.Parse(
            File.ReadAllText(TestPaths.RepoFile("gate", "tools", "cosign-pin.json")));
        return doc.RootElement.GetProperty("version").GetString()!;
    }

    /// <summary>
    /// Assert the signer script is present. This makes EVERY behavioral RED failure read as
    /// "missing script" rather than a bash exit-127 that a raw "exit != 0" assert would misread
    /// as a genuine refusal (AP-010 / the PMB-001 127 trap).
    /// </summary>
    internal static void RequireSignerScript()
    {
        Assert.True(
            File.Exists(SignerScriptAbsPath()),
            $"P3 T4 RED: the extracted signer script {SignerScriptRepoRelPath} must exist (GREEN deliverable).");
    }

    // ---- process running ------------------------------------------------------------------

    internal readonly record struct RunResult(int ExitCode, string StdOut, string StdErr)
    {
        internal string Combined => StdOut + "\n" + StdErr;
    }

    /// <summary>
    /// Run the signer VERBATIM as documented: working directory = repo root, argv[0] = the
    /// RELATIVE committed path gate/tools/sign-determinism.sh (AP-020 — never an absolute-path
    /// proxy). Env overrides with a null value are REMOVED from the child env (so a "missing
    /// attempt" case is genuinely absent, not inherited from the parent).
    /// </summary>
    internal static RunResult RunSigner(IReadOnlyDictionary<string, string?> env, params string[] scriptArgs)
    {
        var psi = new ProcessStartInfo("bash")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = TestPaths.RepoRoot(),
        };
        psi.ArgumentList.Add(SignerScriptRepoRelPath);
        foreach (string a in scriptArgs)
        {
            psi.ArgumentList.Add(a);
        }
        foreach (KeyValuePair<string, string?> kv in env)
        {
            if (kv.Value is null)
            {
                psi.Environment.Remove(kv.Key);
            }
            else
            {
                psi.Environment[kv.Key] = kv.Value;
            }
        }

        using var p = Process.Start(psi)!;
        string so = p.StandardOutput.ReadToEnd();
        string se = p.StandardError.ReadToEnd();
        p.WaitForExit(60_000);
        return new RunResult(p.ExitCode, so, se);
    }

    // ---- fake cosign ----------------------------------------------------------------------

    internal sealed class FakeCosign
    {
        internal required string Path { get; init; }
        internal required string ArgvOut { get; init; }
        internal required string Marker { get; init; }

        /// <summary>True iff the signer actually invoked cosign (the fake touched its marker).</summary>
        internal bool WasCalled() => File.Exists(Marker);

        /// <summary>The exact argv the signer passed to cosign (excluding argv[0]/the binary).</summary>
        internal string[] RecordedArgv()
        {
            if (!File.Exists(ArgvOut))
            {
                return Array.Empty<string>();
            }
            return File.ReadAllLines(ArgvOut)
                .Where(l => l.StartsWith("ARG:", StringComparison.Ordinal))
                .Select(l => l.Substring("ARG:".Length))
                .ToArray();
        }
    }

    /// <summary>
    /// Write a fake cosign (an absolute-shebang bash script) into <paramref name="dir"/>. It
    /// records every argv element and touches a "called" marker so a test can prove BOTH that
    /// cosign ran and with exactly which argv. The output paths are baked in as absolute
    /// LITERALS so the recording survives the signer clearing the child env before exec.
    /// </summary>
    internal static FakeCosign MakeFakeCosign(string dir)
    {
        string argvOut = System.IO.Path.Combine(dir, "cosign-argv.txt");
        string marker = System.IO.Path.Combine(dir, "cosign-called.marker");
        string path = System.IO.Path.Combine(dir, "fake-cosign");

        string body =
            "#!" + BashAbs + "\n" +
            ": > '" + marker + "'\n" +
            "prev=''\n" +
            "for a in \"$@\"; do\n" +
            "  printf 'ARG:%s\\n' \"$a\" >> '" + argvOut + "'\n" +
            "  if [ \"$prev\" = '--bundle' ]; then printf '{\"fake\":\"bundle\"}' > \"$a\"; fi\n" +
            "  prev=\"$a\"\n" +
            "done\n" +
            "exit 0\n";

        File.WriteAllText(path, body);
        File.SetUnixFileMode(
            path,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
            UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
            UnixFileMode.OtherRead | UnixFileMode.OtherExecute);

        return new FakeCosign { Path = path, ArgvOut = argvOut, Marker = marker };
    }

    // ---- synthetic producer-artifact fixtures ---------------------------------------------

    internal sealed class Artifacts
    {
        internal required string ArtifactsDir { get; init; }
        internal required string ManifestFile { get; init; }
        internal required string Sha { get; init; }
        internal required string RunId { get; init; }
        internal required string Attempt { get; init; }
    }

    /// <summary>
    /// Build a fully-VALID producer hand-off, then optionally corrupt exactly one field via
    /// <paramref name="receiptMutator"/> (applied BEFORE the declared digest is recomputed, so the
    /// receipt stays self-consistent and only the intended field is "wrong" relative to the
    /// trusted source — this is what makes each mismatch class INDEPENDENTLY catchable) and/or
    /// corrupt the declared artifact digest via <paramref name="corruptDeclaredDigest"/>.
    /// </summary>
    internal static Artifacts BuildArtifacts(
        string root,
        string sha = TrustedSha,
        string runId = TrustedRunId,
        string attempt = "1",
        Action<Dictionary<string, object>>? receiptMutator = null,
        bool corruptDeclaredDigest = false)
    {
        string artifactsDir = System.IO.Path.Combine(root, "artifacts");
        Directory.CreateDirectory(artifactsDir);
        string manifestDir = System.IO.Path.Combine(root, "manifest");
        Directory.CreateDirectory(manifestDir);

        // The determinism-subject manifest checked out at attested_commit (INV-018 input).
        string manifestFile = System.IO.Path.Combine(manifestDir, "determinism-subject-manifest.json");
        File.WriteAllText(
            manifestFile,
            "{\"determinism_subject_manifest\":\"v1\",\"policy_version\":\"v1\",\"roles\":" +
            "[\"run\",\"route-a\",\"route-b\",\"control-a\",\"control-b\"]}");
        string manifestDigest = Sha256File(manifestFile);

        var receipt = new Dictionary<string, object>
        {
            ["schema_version"] = ReceiptSchemaId,
            ["attested_commit"] = sha,
            ["run_id"] = runId,
            ["run_attempt"] = attempt,
            ["producing_job_result"] = "success",
            ["execution_status"] = "completed",
            ["comparison_status"] = "equal",
            ["subject_manifest_digest"] = manifestDigest,
            ["policy_version"] = "v1",
        };
        receiptMutator?.Invoke(receipt);

        string receiptPath = System.IO.Path.Combine(artifactsDir, "determinism-receipt.json");
        File.WriteAllText(
            receiptPath,
            JsonSerializer.Serialize(receipt, new JsonSerializerOptions { WriteIndented = true }));

        string actualDigest = Sha256File(receiptPath);
        string declared = corruptDeclaredDigest ? new string('0', 64) : actualDigest;
        File.WriteAllText(System.IO.Path.Combine(artifactsDir, "receipt.sha256"), declared);

        return new Artifacts
        {
            ArtifactsDir = artifactsDir,
            ManifestFile = manifestFile,
            Sha = sha,
            RunId = runId,
            Attempt = attempt,
        };
    }

    /// <summary>Assemble the trusted-env dict a happy invocation uses (attempt = 1).</summary>
    internal static Dictionary<string, string?> Env(Artifacts art, FakeCosign fake, string? attempt = null)
        => new()
        {
            ["GITHUB_SHA"] = art.Sha,
            ["GITHUB_RUN_ID"] = art.RunId,
            ["GITHUB_RUN_ATTEMPT"] = attempt ?? art.Attempt,
            ["COSIGN_BIN"] = fake.Path,
        };

    // ---- misc helpers ---------------------------------------------------------------------

    internal static string Sha256File(string path)
    {
        using SHA256 sha = SHA256.Create();
        using FileStream fs = File.OpenRead(path);
        return Convert.ToHexString(sha.ComputeHash(fs)).ToLowerInvariant();
    }

    internal static string NewTempDir()
    {
        string dir = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "p3-signer-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    internal static void Cleanup(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
        catch
        {
            // best effort — OS temp cleanup is the backstop
        }
    }

    private static readonly string BashAbs = ResolveBashAbsolute();

    private static string ResolveBashAbsolute()
    {
        foreach (string c in new[] { "/bin/bash", "/usr/bin/bash", "/usr/local/bin/bash" })
        {
            if (File.Exists(c))
            {
                return c;
            }
        }
        throw new FileNotFoundException("bash not found at a known absolute path — the signer fakes cannot run.");
    }
}
