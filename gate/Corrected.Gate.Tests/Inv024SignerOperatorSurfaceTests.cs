using System;
using System.IO;
using Xunit;

namespace Corrected.Gate.Tests;

/// <summary>
/// P3 determinism-attestation spec INV-024 (~818-839) — the signing lane's operator surface is a
/// committed EXTRACTED shell script the workflow invokes (never inline `run:` steps a grep can
/// only reconstruct — RS-028), it is executed VERBATIM (AP-020/PMB-001 — the documented cwd +
/// argv[0] form, never a fixed-cwd/absolute-path proxy), and the live workflow ↔ script stay in
/// sync.
///
/// Three clauses:
///   (a) SIGNER workflow ↔ script sync — p3-determinism-sign.yml invokes EXACTLY
///       gate/tools/sign-determinism.sh (static, mirroring the spike's
///       Inv014OperatorSurfaceTests.CiWorkflow_Realized verbatim-command assertion).
///   (b) VERBATIM EXECUTION — run the committed signer script from the DOCUMENTED cwd (repo root)
///       with the RELATIVE argv[0] form; fixture paths are canonicalized to ABSOLUTE before the
///       exec (the AP-020/PMB-001 exit-127 trap). Assert it gets PAST launch and reaches the
///       signing step (the fake cosign is invoked). [Subprocess].
///   (c) PRODUCER-half sync — p3-determinism-lane.yml invokes EXACTLY
///       spikes/dafny-compat/scripts/determinism-lane.sh (static sync assertion only; the
///       heavyweight two-nested-run determinism step is the spike lane's own 8-core job, NOT run
///       here). This clause PASSES today (both already-built) — a GUARD that goes RED if either
///       side drifts.
///
/// [Collection("Subprocess")] is REQUIRED (clause (b) execs the script).
///
/// RED NOW: the signer script + p3-determinism-sign.yml do not exist, so (a) and (b) fail against
/// their absence; (c) passes (the PR1 producer lane is already built).
///
/// AP-031: NOT triggered — the workflow/lane files are CI artifacts this feature authors, not
/// `.correctless/artifacts/` producer outputs parsed at test time.
/// </summary>
[Collection("Subprocess")]
public class Inv024SignerOperatorSurfaceTests
{
    // ==================================================================================
    // (a) SIGNER workflow ↔ extracted script sync.
    // ==================================================================================

    // Tests INV-024 [integration] ("a workflow↔script sync assertion proves the live YAML invokes
    // exactly that script"): the signing workflow p3-determinism-sign.yml invokes the extracted
    // signer script gate/tools/sign-determinism.sh verbatim (the exact repo-relative path).
    [Fact]
    public void Sign_workflow_invokes_the_extracted_signer_script_verbatim()
    {
        string wfPath = TestPaths.RepoFile(".github", "workflows", "p3-determinism-sign.yml");
        Assert.True(File.Exists(wfPath),
            "INV-024: the signing workflow .github/workflows/p3-determinism-sign.yml must exist (GREEN deliverable).");
        string wf = File.ReadAllText(wfPath);
        Assert.Contains("gate/tools/sign-determinism.sh", wf);
    }

    // Tests INV-024 [integration] (RS-028 — "the lane logic MUST live in a committed EXTRACTED
    // shell script the workflow invokes, NOT inline run: steps"): the extracted signer script is a
    // committed, EXECUTABLE file (so it can be exec'd verbatim — never a grep-only proxy).
    [Fact]
    public void Extracted_signer_script_is_committed_and_executable()
    {
        string script = P3SignerHarness.SignerScriptAbsPath();
        Assert.True(File.Exists(script),
            "INV-024: the extracted signer script gate/tools/sign-determinism.sh must be committed (GREEN deliverable).");
        Assert.True(File.GetUnixFileMode(script).HasFlag(UnixFileMode.UserExecute),
            "INV-024: gate/tools/sign-determinism.sh must be executable.");
    }

    // ==================================================================================
    // (b) VERBATIM EXECUTION — documented cwd + relative argv[0], reaches the signing step.
    // ==================================================================================

    // Tests INV-024 [integration] ("an execution test running the committed extracted script
    // verbatim (documented cwd + argv[0] form) ... the command gets past launch and runs the
    // [signing] step"): from the DOCUMENTED cwd (repo root) with the RELATIVE argv[0]
    // `gate/tools/sign-determinism.sh` (never an absolute-path proxy — AP-020/PMB-001), and with
    // fixture paths canonicalized to ABSOLUTE, the signer gets past launch and REACHES the signing
    // step (the fake cosign is invoked). This is the exit-127 trap detector: a BASH_SOURCE/$0 path
    // reused after an internal `cd` would fail here but survive a normalized proxy invocation.
    [Fact]
    public void Documented_invocation_gets_past_launch_and_reaches_signing_step_integration()
    {
        P3SignerHarness.RequireSignerScript();
        string dir = P3SignerHarness.NewTempDir();
        try
        {
            P3SignerHarness.FakeCosign fake = P3SignerHarness.MakeFakeCosign(dir);
            P3SignerHarness.Artifacts art = P3SignerHarness.BuildArtifacts(dir);

            // Canonicalize every argument path to ABSOLUTE before the exec (AP-020) — the operator
            // runs from the repo root; RunSigner sets cwd=repo root and argv[0]=the relative path.
            string absArtifacts = Path.GetFullPath(art.ArtifactsDir);
            string absManifest = Path.GetFullPath(art.ManifestFile);
            string absOut = Path.GetFullPath(Path.Combine(dir, "out.sigstore.json"));

            P3SignerHarness.RunResult r = P3SignerHarness.RunSigner(
                P3SignerHarness.Env(art, fake),
                "--artifacts-dir", absArtifacts,
                "--manifest", absManifest,
                "--out", absOut);

            // Past launch (NOT a 127) AND reached the signing step (fake cosign invoked).
            Assert.NotEqual(127, r.ExitCode);
            Assert.True(fake.WasCalled(),
                "INV-024/AP-020: the documented verbatim invocation must get PAST launch and REACH " +
                "the signing step (the fake cosign must be invoked) — a BASH_SOURCE/$0 path reused " +
                "after an internal cd would fail exactly here.");
            Assert.Equal(0, r.ExitCode);
        }
        finally { P3SignerHarness.Cleanup(dir); }
    }

    // ==================================================================================
    // (c) PRODUCER-half sync — the already-built PR1 lane invokes its extracted script.
    // ==================================================================================

    // Tests INV-024 [integration] (producer-half operator-surface sync): the PR1 determinism
    // producer lane p3-determinism-lane.yml invokes EXACTLY the extracted lane script
    // spikes/dafny-compat/scripts/determinism-lane.sh (static sync only — the heavyweight
    // two-nested-run determinism step is the spike lane's own 8-core job and is NOT run here).
    // GUARD: passes today (both already built); goes RED if either side drifts.
    [Fact]
    public void Producer_lane_workflow_invokes_the_extracted_determinism_lane_script()
    {
        string wfPath = TestPaths.RepoFile(".github", "workflows", "p3-determinism-lane.yml");
        Assert.True(File.Exists(wfPath),
            "INV-024: the PR1 producer lane .github/workflows/p3-determinism-lane.yml must exist.");
        string wf = File.ReadAllText(wfPath);
        Assert.Contains("scripts/determinism-lane.sh", wf);

        // The invoked script is a committed, executable extracted surface (not inline run: only).
        string laneScript = TestPaths.RepoFile("spikes", "dafny-compat", "scripts", "determinism-lane.sh");
        Assert.True(File.Exists(laneScript),
            "INV-024: the extracted lane script spikes/dafny-compat/scripts/determinism-lane.sh must be committed.");
        Assert.True(File.GetUnixFileMode(laneScript).HasFlag(UnixFileMode.UserExecute),
            "INV-024: the extracted lane script must be executable.");
    }
}
