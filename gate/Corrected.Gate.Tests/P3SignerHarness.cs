using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Corrected.Provenance.Determinism;
using Xunit;

namespace Corrected.Gate.Tests;

/// <summary>
/// Shared test harness for the P3 determinism-attestation T4 SIGNER slice (INV-007/008/009/024)
/// AFTER the Statement-builder reconciliation. This is test infrastructure (real helper logic —
/// NOT a production stub, so no STUB:TDD marker), mirroring the fake-cosign subprocess pattern in
/// <see cref="CosignSubprocessSeamTests"/>.
///
/// It defines — and therefore PINS, as the RED-phase contract GREEN must implement — the operator
/// interface of the extracted signer script <c>gate/tools/sign-determinism.sh</c>:
///
///   DOCUMENTED INVOCATION (run from the repo root, argv[0] is the RELATIVE path, AP-020):
///     GITHUB_SHA=&lt;40hex&gt; GITHUB_RUN_ID=&lt;digits&gt; GITHUB_RUN_ATTEMPT=1 COSIGN_BIN=&lt;abs cosign&gt; \
///       bash gate/tools/sign-determinism.sh \
///         --artifacts-dir &lt;DIR&gt; --manifest &lt;MANIFEST_FILE&gt; --out &lt;BUNDLE_OUT&gt;
///
///   PRODUCER HAND-OFF (&lt;DIR&gt;, same-run @actions/artifact contents — RS-032/EA-010). The SIGNED
///   SUBJECT is now the REAL determinism RunReceipt, and the Statement is CORRECTED-BUILT (never
///   hand-rolled by the signer):
///     * determinism-receipt.json   — the determinism RunReceipt (the SIGNED SUBJECT: a REAL
///         RunReceipt — execution_status / comparison_status / attested_commit /
///         subject_manifest_digest / policy_version / platform / run1_evidence / run2_evidence).
///         The base bytes are the committed PR1 fixture; only the CI-binding fields (attested_commit)
///         + the subject-manifest digest are re-homed to the trusted env + THIS run's manifest.
///     * receipt.sha256             — the producer-DECLARED SHA-256 of the receipt bytes.
///     * determinism-statement.json — the Corrected-built in-toto Statement =
///         DeterminismAttestation.SerializeStatementJson(receiptBytes, RunReceipt.FromJson(bytes)).
///         The HARNESS produces this (it is C#); the signer NEVER builds it and signs THIS file.
///     * ci-context.json            — { run_id, run_attempt, producing_job_result:"success" }: the
///         CI-run metadata that is NOT part of a RunReceipt (run_id / run_attempt / job result).
///     * determinism-subject-manifest.json — the subject manifest passed via --manifest.
///
///   RE-CHECK CONTRACT (INV-007): before ANY cosign call the signer REFUSES (non-zero exit, a line
///   containing "REFUSE" on stderr, NO cosign invocation) when ANY of these disagree — each class
///   INDEPENDENTLY, over a fixture in which exactly ONE field deviates (the declared digest is
///   recomputed AFTER a receipt mutation so only the intended field is "wrong"):
///     digest          receipt.sha256 != actual SHA-256(determinism-receipt.json)
///     schema          the receipt does not parse as a determinism RunReceipt (a missing
///                     subject_manifest_digest / policy_version fails the shape)
///     attested_commit receipt.attested_commit != $GITHUB_SHA
///     run_id          ci-context.run_id != $GITHUB_RUN_ID
///     producing-job   ci-context.producing_job_result != "success"
///     manifest        receipt.subject_manifest_digest != SHA-256(&lt;MANIFEST_FILE&gt;)
///     statement       determinism-statement.json is ABSENT, OR its subject sha256 != actual
///                     SHA-256(receipt bytes), OR its predicateType != the frozen URI, OR its
///                     subject name != "determinism-run-receipt"  (the NEW class-7 binding check —
///                     the signer FAILS CLOSED if the Corrected-built Statement is missing/tampered)
///
///   ATTEMPT GUARD (INV-008): $GITHUB_RUN_ATTEMPT must be exactly "1" AND ci-context.run_attempt
///   must agree; a missing/empty/&gt;1 attempt REFUSES fail-closed with the RS-036 wording
///   ("re-runs never mint" / "push a new reviewed commit").
///
///   SIGNING SEAM (INV-009): on all checks passing the signer invokes the pinned cosign
///     $COSIGN_BIN attest-blob --statement &lt;DIR&gt;/determinism-statement.json --bundle &lt;out&gt; \
///        --new-bundle-format=true --yes &lt;DIR&gt;/determinism-receipt.json
///   i.e. it signs the CORRECTED-BUILT Statement (subject name determinism-run-receipt), NOT a
///   signer-synthesized one. COSIGN_BIN injects the fake double; the version/digest are single-
///   sourced from gate/tools/cosign-pin.json; --new-bundle-format=false is NOT taken (DD-002).
///
/// NOTE: everything here is the RED contract. Against the CURRENT placeholder signer (which reads
/// run_id/run_attempt from the RECEIPT — now absent — and BUILDS its own Statement), the positive
/// controls fail (cosign never reached) and the class-7 statement checks are unmet — exactly the
/// RED signal. Each runner first asserts the script exists, so a RED failure reads as "missing
/// script"/"refused", never a vacuous 127 masquerading as a refusal (AP-010).
/// </summary>
internal static class P3SignerHarness
{
    // A deterministic, valid 40-hex trigger commit + run id used across the fixtures.
    internal const string TrustedSha = "a94a8fe5ccb19ba61c4c0873d391e987982fbbd3";
    internal const string TrustedRunId = "30511722581";

    // The frozen Corrected determinism contract literals the class-7 statement check binds to.
    internal const string PredicateTypeUri = "https://correctless.org/attestations/determinism/v1";
    internal const string CanonicalSubjectName = "determinism-run-receipt";

    internal static string SignerScriptRepoRelPath => "gate/tools/sign-determinism.sh";

    internal static string SignerScriptAbsPath()
        => TestPaths.RepoFile("gate", "tools", "sign-determinism.sh");

    /// <summary>The committed REAL PR1 determinism RunReceipt fixture — the base SIGNED SUBJECT bytes.</summary>
    internal static string FixtureReceiptPath()
        => TestPaths.Fixture("provenance", "determinism-receipt.sample.json");

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

        internal string ReceiptPath => System.IO.Path.Combine(ArtifactsDir, "determinism-receipt.json");
        internal string StatementPath => System.IO.Path.Combine(ArtifactsDir, "determinism-statement.json");
        internal string CiContextPath => System.IO.Path.Combine(ArtifactsDir, "ci-context.json");
        internal string DeclaredDigestPath => System.IO.Path.Combine(ArtifactsDir, "receipt.sha256");
    }

    /// <summary>
    /// Build a fully-VALID producer hand-off (the 5-file contract above), then optionally deviate
    /// in EXACTLY ONE place so a class of the re-check is independently catchable:
    ///   * <paramref name="receiptMutator"/> — mutate the RunReceipt SUBJECT (attested_commit,
    ///     subject_manifest_digest, or drop a required field for the schema class). Applied BEFORE
    ///     the declared digest + the Corrected-built Statement are (re)computed, so both stay bound
    ///     to the actual receipt bytes and only the intended field is "wrong".
    ///   * <paramref name="ciContextMutator"/> — mutate ci-context.json (run_id / run_attempt /
    ///     producing_job_result), the CI-run metadata NOT carried in the RunReceipt.
    ///   * <paramref name="statementTransform"/> — tamper the Corrected-built Statement (returns a
    ///     replacement JSON, or null to DELETE the statement file — the class-7 "absent" case).
    ///   * <paramref name="corruptDeclaredDigest"/> — write a wrong receipt.sha256.
    /// The base receipt is the committed REAL PR1 RunReceipt fixture with attested_commit re-homed
    /// to <paramref name="sha"/> and subject_manifest_digest re-homed to THIS run's manifest digest.
    /// </summary>
    internal static Artifacts BuildArtifacts(
        string root,
        string sha = TrustedSha,
        string runId = TrustedRunId,
        string attempt = "1",
        Action<JsonObject>? receiptMutator = null,
        Action<JsonObject>? ciContextMutator = null,
        Func<string, string?>? statementTransform = null,
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

        // Base = the committed REAL determinism RunReceipt (full platform + run1/run2 evidence),
        // with the CI-binding fields re-homed so a fully-valid hand-off is internally consistent
        // with the trusted env + this run's manifest.
        var receipt = (JsonObject)JsonNode.Parse(File.ReadAllBytes(FixtureReceiptPath()))!;
        receipt["attested_commit"] = sha;
        receipt["subject_manifest_digest"] = manifestDigest;
        receiptMutator?.Invoke(receipt);

        // Write the receipt bytes, then bind the declared digest + the Corrected-built Statement
        // to THOSE EXACT bytes (the declared digest is recomputed AFTER the mutation).
        byte[] receiptBytes = Encoding.UTF8.GetBytes(
            receipt.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        string receiptPath = System.IO.Path.Combine(artifactsDir, "determinism-receipt.json");
        File.WriteAllBytes(receiptPath, receiptBytes);

        string actualDigest = Sha256Bytes(receiptBytes);
        string declared = corruptDeclaredDigest ? new string('0', 64) : actualDigest;
        File.WriteAllText(System.IO.Path.Combine(artifactsDir, "receipt.sha256"), declared);

        // The Corrected-built Statement — the SINGLE canonical byte-source shared with the (future
        // T3) verifier. The signer signs THIS file and NEVER builds its own. At RED
        // SerializeStatementJson is STUB:TDD and returns "" (the CURRENT signer ignores the file);
        // at GREEN it is the real canonical JSON the class-7 check validates.
        string statementPath = System.IO.Path.Combine(artifactsDir, "determinism-statement.json");
        string statementJson = DeterminismAttestation.SerializeStatementJson(
            receiptBytes, RunReceipt.FromJson(receiptBytes));

        if (statementTransform is null)
        {
            File.WriteAllText(statementPath, statementJson);
        }
        else
        {
            string? replacement = statementTransform(statementJson);
            if (replacement is null)
            {
                if (File.Exists(statementPath))
                {
                    File.Delete(statementPath); // class-7 "statement absent" — fail closed.
                }
            }
            else
            {
                File.WriteAllText(statementPath, replacement);
            }
        }

        // ci-context.json — the run metadata that is NOT part of a RunReceipt.
        var ci = new JsonObject
        {
            ["run_id"] = runId,
            ["run_attempt"] = attempt,
            ["producing_job_result"] = "success",
        };
        ciContextMutator?.Invoke(ci);
        File.WriteAllText(System.IO.Path.Combine(artifactsDir, "ci-context.json"), ci.ToJsonString());

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

    // ---- class-7 Statement tampers (operate on the Corrected-built Statement JSON) ----------
    //
    // At GREEN these mutate the REAL SerializeStatementJson output (correct subject digest + name
    // + predicate-type), deviating EXACTLY ONE bound field so the class is independently catchable.
    // At RED SerializeStatementJson returns "" (the signer ignores the file anyway); the helpers
    // fall back to a baseline so they never throw during harness setup.

    /// <summary>Delete the Corrected-built Statement entirely (class-7 "absent" — fail closed).</summary>
    internal static readonly Func<string, string?> DeleteStatement = _ => null;

    /// <summary>Tamper the subject sha256 so it no longer binds the receipt bytes.</summary>
    internal static string? StatementWithWrongSubjectDigest(string json)
        => MutateStatement(json, o => SetSubjectSha256(o, new string('0', 64)));

    /// <summary>Tamper the predicateType off the frozen determinism URI.</summary>
    internal static string? StatementWithWrongPredicateType(string json)
        => MutateStatement(json, o => o["predicateType"] = "https://correctless.org/attestations/WRONG/v9");

    /// <summary>Tamper the subject name back to the OLD placeholder ("determinism-receipt.json").</summary>
    internal static string? StatementWithWrongSubjectName(string json)
        => MutateStatement(json, o => SetSubjectName(o, "determinism-receipt.json"));

    private static string MutateStatement(string json, Action<JsonObject> mutate)
    {
        JsonObject o = ParseStatementOrBaseline(json);
        mutate(o);
        return o.ToJsonString();
    }

    private static JsonObject ParseStatementOrBaseline(string json)
    {
        if (!string.IsNullOrWhiteSpace(json))
        {
            try
            {
                if (JsonNode.Parse(json) is JsonObject parsed && parsed["subject"] is JsonArray)
                {
                    return parsed;
                }
            }
            catch (JsonException)
            {
                // fall through to the baseline (RED: SerializeStatementJson returned "").
            }
        }
        return (JsonObject)JsonNode.Parse(BaselineStatementJson)!;
    }

    private const string BaselineStatementJson =
        "{\"_type\":\"https://in-toto.io/Statement/v1\"," +
        "\"subject\":[{\"name\":\"determinism-run-receipt\",\"digest\":{\"sha256\":" +
        "\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\"}}]," +
        "\"predicateType\":\"https://correctless.org/attestations/determinism/v1\"," +
        "\"predicate\":{\"receiptDigest\":\"aaaaaaaa\",\"projectionFacts\":[]}}";

    private static void SetSubjectSha256(JsonObject o, string sha)
    {
        var subject = (JsonObject)((JsonArray)o["subject"]!)[0]!;
        ((JsonObject)subject["digest"]!)["sha256"] = sha;
    }

    private static void SetSubjectName(JsonObject o, string name)
    {
        var subject = (JsonObject)((JsonArray)o["subject"]!)[0]!;
        subject["name"] = name;
    }

    // ---- misc helpers ---------------------------------------------------------------------

    internal static string Sha256File(string path)
    {
        using SHA256 sha = SHA256.Create();
        using FileStream fs = File.OpenRead(path);
        return Convert.ToHexString(sha.ComputeHash(fs)).ToLowerInvariant();
    }

    internal static string Sha256Bytes(byte[] bytes)
        => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

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
