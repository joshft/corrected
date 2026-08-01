using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Corrected.Provenance.Entry;
using Xunit;

namespace Corrected.Gate.Tests;

/// <summary>
/// P3 phase-entry INV-030 (Group G / MA-C part b) — the ENTRY signer operator surface, the entry
/// analog of <see cref="Inv024SignerOperatorSurfaceTests"/>. The signing lane's logic lives in a
/// committed EXTRACTED shell script (<c>gate/tools/sign-entry.sh</c>) the workflow invokes VERBATIM
/// (AP-020/PMB-001 — the documented cwd + relative argv[0] form), never inline <c>run:</c> steps a
/// grep can only reconstruct (RS-028).
///
/// Clauses:
///   (a) the signing workflow p3-entry-sign.yml invokes EXACTLY gate/tools/sign-entry.sh;
///   (b) the extracted signer script is committed + executable;
///   (c) VERBATIM EXECUTION from the documented cwd (repo root) with the relative argv[0]: on a
///       fully-VALID emitter-built hand-off + attempt=1 it reaches the signing step and invokes the
///       fake cosign with the frozen `attest-blob --statement … --bundle … --new-bundle-format=true
///       --yes <commit-X blob>` argv;
///   (d) the INV-030 re-check REFUSES (non-zero exit, "REFUSE" on stderr, NO cosign call) on each
///       hand-off deviation INDEPENDENTLY: blob↔subject-digest mismatch, wrong predicate type, wrong
///       subject name, a re-run (attempt!=1), and a missing attempt.
///
/// The valid hand-off is built by the REAL EntryStatementEmitter over the committed synthetic
/// entry-receipt spec fixture (so the statement genuinely binds its commit-X blob). [Collection("Subprocess")]
/// is REQUIRED (clause (c)/(d) exec the script + a fake cosign).
/// </summary>
[Collection("Subprocess")]
public class Inv030SignEntrySurfaceTests
{
    private const string ScriptRepoRelPath = "gate/tools/sign-entry.sh";
    private const string DeterminismPredicateTypeUri = "https://correctless.org/attestations/determinism/v1";

    private static string ScriptAbsPath() => TestPaths.RepoFile("gate", "tools", "sign-entry.sh");

    private static string FixtureSpecPath() => TestPaths.Fixture("provenance", "entry-receipt.sample.json");

    // ================= (a) workflow ↔ script sync =================

    // Tests INV-030 [integration]: the entry-signing workflow p3-entry-sign.yml invokes the extracted
    // signer script gate/tools/sign-entry.sh verbatim (the exact repo-relative path).
    [Fact]
    public void Sign_workflow_invokes_the_extracted_signer_script_verbatim()
    {
        string wfPath = TestPaths.RepoFile(".github", "workflows", "p3-entry-sign.yml");
        Assert.True(File.Exists(wfPath),
            "INV-030: the signing workflow .github/workflows/p3-entry-sign.yml must exist (GREEN deliverable).");
        Assert.Contains("gate/tools/sign-entry.sh", File.ReadAllText(wfPath));
    }

    // ================= (b) committed + executable =================

    // Tests INV-030 [integration] (RS-028): the extracted signer script is a committed, EXECUTABLE
    // file (so it can be exec'd verbatim — never a grep-only proxy).
    [Fact]
    public void Extracted_signer_script_is_committed_and_executable()
    {
        string script = ScriptAbsPath();
        Assert.True(File.Exists(script),
            "INV-030: the extracted signer script gate/tools/sign-entry.sh must be committed (GREEN deliverable).");
        Assert.True(File.GetUnixFileMode(script).HasFlag(UnixFileMode.UserExecute),
            "INV-030: gate/tools/sign-entry.sh must be executable.");
    }

    // ================= (c) verbatim execution reaches the frozen cosign argv =================

    // Tests INV-030 [integration] (AP-020/PMB-001): a fully-valid emitter-built hand-off + attempt=1,
    // run from the documented cwd with the relative argv[0], reaches the signing step and invokes the
    // fake cosign with the FROZEN entry argv.
    [Fact]
    public void Valid_handoff_reaches_the_frozen_cosign_argv()
    {
        string dir = P3SignerHarness.NewTempDir();
        try
        {
            string artifacts = BuildValidHandoff(dir);
            string outBundle = Path.Combine(dir, "entry.sigstore.json");
            P3SignerHarness.FakeCosign fake = P3SignerHarness.MakeFakeCosign(dir);

            RunResult r = RunEntrySigner(
                Env(fake, attempt: "1"), "--artifacts-dir", artifacts, "--out", outBundle);

            Assert.True(r.ExitCode == 0, $"a valid hand-off must sign (exit 0). stderr:\n{r.StdErr}");
            Assert.True(fake.WasCalled(), "the signer must invoke cosign on a valid hand-off.");

            string[] argv = fake.RecordedArgv();
            string joined = string.Join(" ", argv);
            Assert.Contains("attest-blob", argv);
            Assert.Contains("--statement", argv);
            Assert.Contains("--bundle", argv);
            Assert.Contains("--new-bundle-format=true", argv);
            Assert.Contains("--yes", argv);
            // The statement + the commit-X blob are the emitter-produced files in the hand-off dir.
            Assert.Contains(Path.Combine(artifacts, "entry-statement.json"), argv);
            Assert.Contains(Path.Combine(artifacts, "entry-commit.blob"), argv);
            Assert.Contains(outBundle, argv);
            // Never a claims-off / insecure variant.
            Assert.DoesNotContain("--insecure", joined);
        }
        finally { P3SignerHarness.Cleanup(dir); }
    }

    // ================= (d) INV-030 re-check refusals (each independent) =================

    // Tests INV-030 [integration] (blob↔subject binding): the commit-X blob whose sha256 no longer
    // equals the statement's subjects[0] digest -> REFUSE, no cosign call.
    [Fact]
    public void Refuses_when_blob_sha_does_not_bind_the_commit_subject()
        => AssertRefuses(art =>
        {
            // Overwrite the blob with different bytes so sha256(blob) != subjects[0].
            File.WriteAllText(Path.Combine(art, "entry-commit.blob"), "a-different-commit-representation");
        });

    // Tests INV-030 [integration] (predicate type): a statement carrying the determinism predicate
    // type -> REFUSE (RS-024 cross-rejection at the signer surface), no cosign call.
    [Fact]
    public void Refuses_when_statement_predicate_type_is_not_entry()
        => AssertRefuses(art =>
        {
            string stmtPath = Path.Combine(art, "entry-statement.json");
            var o = (JsonObject)JsonNode.Parse(File.ReadAllText(stmtPath))!;
            o["predicateType"] = DeterminismPredicateTypeUri;
            File.WriteAllText(stmtPath, o.ToJsonString());
        });

    // Tests INV-030 [integration] (subject name): a statement whose subjects[0] name is not the
    // canonical phase-entry-commit -> REFUSE, no cosign call.
    [Fact]
    public void Refuses_when_subject0_name_is_not_phase_entry_commit()
        => AssertRefuses(art =>
        {
            string stmtPath = Path.Combine(art, "entry-statement.json");
            var o = (JsonObject)JsonNode.Parse(File.ReadAllText(stmtPath))!;
            ((JsonObject)((JsonArray)o["subject"]!)[0]!)["name"] = "not-the-entry-commit";
            File.WriteAllText(stmtPath, o.ToJsonString());
        });

    // Tests INV-030 [integration] (attempt guard): a re-run (attempt=2) mints NOTHING -> REFUSE, no
    // cosign call.
    [Fact]
    public void Refuses_on_a_rerun_attempt()
    {
        string dir = P3SignerHarness.NewTempDir();
        try
        {
            string artifacts = BuildValidHandoff(dir);
            P3SignerHarness.FakeCosign fake = P3SignerHarness.MakeFakeCosign(dir);
            RunResult r = RunEntrySigner(
                Env(fake, attempt: "2"), "--artifacts-dir", artifacts, "--out", Path.Combine(dir, "out.json"));
            Assert.NotEqual(0, r.ExitCode);
            Assert.Contains("REFUSE", r.StdErr, StringComparison.Ordinal);
            Assert.False(fake.WasCalled(), "a re-run must never invoke cosign.");
        }
        finally { P3SignerHarness.Cleanup(dir); }
    }

    // Tests INV-030 [integration] (attempt guard, fail-closed): a MISSING attempt is not treated as 1
    // -> REFUSE, no cosign call.
    [Fact]
    public void Refuses_on_a_missing_attempt()
    {
        string dir = P3SignerHarness.NewTempDir();
        try
        {
            string artifacts = BuildValidHandoff(dir);
            P3SignerHarness.FakeCosign fake = P3SignerHarness.MakeFakeCosign(dir);
            var env = Env(fake, attempt: "1");
            env["GITHUB_RUN_ATTEMPT"] = null; // REMOVE the attempt from the child env
            RunResult r = RunEntrySigner(
                env, "--artifacts-dir", artifacts, "--out", Path.Combine(dir, "out.json"));
            Assert.NotEqual(0, r.ExitCode);
            Assert.Contains("REFUSE", r.StdErr, StringComparison.Ordinal);
            Assert.False(fake.WasCalled(), "a missing attempt must never invoke cosign.");
        }
        finally { P3SignerHarness.Cleanup(dir); }
    }

    // ---- helpers ----

    // Build a fully-VALID producer hand-off dir via the REAL emitter (entry-statement.json binds its
    // own commit-X blob), then optionally deviate in EXACTLY ONE place.
    private static string BuildValidHandoff(string root, Action<string>? deviate = null)
    {
        string artifacts = Path.Combine(root, "artifacts");
        Directory.CreateDirectory(artifacts);
        EntryStatementEmitter.Emit(
            FixtureSpecPath(),
            Path.Combine(artifacts, "entry-statement.json"),
            Path.Combine(artifacts, "entry-commit.blob"));
        deviate?.Invoke(artifacts);
        return artifacts;
    }

    // Shared refuse-cell body: a valid hand-off deviated in ONE place must REFUSE before cosign.
    private static void AssertRefuses(Action<string> deviate)
    {
        string dir = P3SignerHarness.NewTempDir();
        try
        {
            string artifacts = BuildValidHandoff(dir, deviate);
            P3SignerHarness.FakeCosign fake = P3SignerHarness.MakeFakeCosign(dir);
            RunResult r = RunEntrySigner(
                Env(fake, attempt: "1"), "--artifacts-dir", artifacts, "--out", Path.Combine(dir, "out.json"));
            Assert.NotEqual(0, r.ExitCode);
            Assert.Contains("REFUSE", r.StdErr, StringComparison.Ordinal);
            Assert.False(fake.WasCalled(), "a deviated hand-off must never invoke cosign.");
        }
        finally { P3SignerHarness.Cleanup(dir); }
    }

    private static Dictionary<string, string?> Env(P3SignerHarness.FakeCosign fake, string attempt)
        => new()
        {
            ["GITHUB_RUN_ATTEMPT"] = attempt,
            ["COSIGN_BIN"] = fake.Path,
        };

    private readonly record struct RunResult(int ExitCode, string StdOut, string StdErr);

    // Run sign-entry.sh VERBATIM as documented: cwd = repo root, argv[0] = the RELATIVE committed
    // path (AP-020 — never an absolute-path proxy). A null env value is REMOVED from the child env.
    private static RunResult RunEntrySigner(IReadOnlyDictionary<string, string?> env, params string[] scriptArgs)
    {
        var psi = new ProcessStartInfo("bash")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = TestPaths.RepoRoot(),
        };
        psi.ArgumentList.Add(ScriptRepoRelPath);
        foreach (string a in scriptArgs) { psi.ArgumentList.Add(a); }
        foreach (KeyValuePair<string, string?> kv in env)
        {
            if (kv.Value is null) { psi.Environment.Remove(kv.Key); }
            else { psi.Environment[kv.Key] = kv.Value; }
        }

        using var p = Process.Start(psi)!;
        string so = p.StandardOutput.ReadToEnd();
        string se = p.StandardError.ReadToEnd();
        p.WaitForExit(60_000);
        return new RunResult(p.ExitCode, so, se);
    }
}
