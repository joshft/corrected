using System;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace Corrected.Gate.Tests;

/// <summary>
/// P3 determinism-attestation INV-007 (~316-321) — the WORKFLOW WIRING end state GREEN must
/// produce for the runnable test-host emitter. Pure STATIC YAML scan of the committed
/// <c>.github/workflows/p3-determinism-sign.yml</c> (read via TestPaths.RepoFile), modelled on
/// <see cref="Inv007SignerJobIsolationTests"/>. No subprocess — this class must NOT carry
/// [Collection("Subprocess")].
///
/// This asserts the DESIRED END STATE, so every cell FAILS NOW: the current producer job has a
/// no-op "(DEFERRED real-run wiring)" echo instead of a real emit step. GREEN replaces that echo
/// with a step that runs `dotnet test --filter EmitDeterminismStatement` and binds the env keys
/// EMIT_STATEMENT_RECEIPT / EMIT_STATEMENT_OUT to the hand-off receipt / statement paths, BEFORE
/// the upload-artifact step, in the PRODUCER job only, and deletes the deferred placeholder.
///
/// LOAD-BEARING contract (must agree with DeterminismStatementEmitterHostTests + the emitter):
///   filter target : EmitDeterminismStatement
///   env receipt   : EMIT_STATEMENT_RECEIPT -> handoff/determinism-receipt.json
///   env out       : EMIT_STATEMENT_OUT     -> handoff/determinism-statement.json
///
/// AP-031 real-artifact clause is NOT triggered — this scans a committed CI workflow the feature
/// authors, not a `.correctless/artifacts/` producer output.
/// </summary>
public class DeterminismStatementEmitterWorkflowWiringTests
{
    // A3: bound to the ONE shared C# const on the host test class (single source of the filter
    // target name across host test / wiring test / workflow — a rename is a single-place change).
    private const string FilterTarget = DeterminismStatementEmitterHostTests.FilterTargetName;
    private const string EnvKeyReceipt = "EMIT_STATEMENT_RECEIPT";
    private const string EnvKeyOut = "EMIT_STATEMENT_OUT";
    private const string HandoffReceipt = "handoff/determinism-receipt.json";
    private const string HandoffStatement = "handoff/determinism-statement.json";

    private static string WorkflowPath()
        => TestPaths.RepoFile(".github", "workflows", "p3-determinism-sign.yml");

    private static string ReadWorkflow()
    {
        Assert.True(
            File.Exists(WorkflowPath()),
            "INV-007: the two-job signing workflow .github/workflows/p3-determinism-sign.yml must exist.");
        return File.ReadAllText(WorkflowPath());
    }

    private static string Norm(string s) => Regex.Replace(s, @"\s+", " ").Trim();

    /// <summary>
    /// The [start, end) bounds of a top-level job section `  &lt;job&gt;:` (2-space indent under
    /// `jobs:`) up to the next 2-space-indented `  key:` or EOF. (-1, -1) if the job is absent.
    /// </summary>
    private static (int Start, int End) SectionBounds(string yaml, string job)
    {
        Match start = Regex.Match(yaml, @"(?m)^  " + Regex.Escape(job) + @":[ \t]*$");
        if (!start.Success)
        {
            return (-1, -1);
        }
        Match next = Regex.Match(yaml.Substring(start.Index + start.Length), @"(?m)^  [A-Za-z0-9_-]+:[ \t]*$");
        int end = next.Success ? start.Index + start.Length + next.Index : yaml.Length;
        return (start.Index, end);
    }

    private static string JobSection(string yaml, string job)
    {
        (int s, int e) = SectionBounds(yaml, job);
        return s < 0 ? string.Empty : yaml.Substring(s, e - s);
    }

    // ==================================================================================
    // The producer runs a REAL emit step: `dotnet test --filter EmitDeterminismStatement`.
    // ==================================================================================

    // Tests INV-007 [integration]: the PRODUCER job has a real emit step that runs `dotnet test`
    // with a `--filter` naming the EmitDeterminismStatement host fact — and that filter target lives
    // ONLY in the producer, never the signer. RED now: the producer has a no-op echo, so `dotnet
    // test` + the filter substring are absent from the producer section.
    [Fact]
    public void Producer_runs_a_real_dotnet_test_emit_step_targeting_the_filter()
    {
        string wf = ReadWorkflow();
        // RAW (un-normalized) producer section so `[^\n]*` keeps ONE command on ONE line — three
        // tokens spread across different steps/comments must not satisfy the co-location regex.
        string producerRaw = JobSection(wf, "producer");
        string signer = JobSection(wf, "signer");
        Assert.NotEqual(string.Empty, producerRaw);

        // A1: co-locate `dotnet test` + `--filter` + the target name on ONE command line.
        Assert.True(
            Regex.IsMatch(producerRaw, @"dotnet\s+test[^\n]*--filter[^\n]*" + Regex.Escape(FilterTarget)),
            "INV-007: the producer must run ONE `dotnet test ... --filter ...EmitDeterminismStatement` command " +
            "(the tokens must co-locate on a single run step, not scatter across steps/comments).");

        // A2: pin the CONTAINS form (`~`). A wrong exact form (FullyQualifiedName=EmitDeterminismStatement,
        // or a Category=/DisplayName= mismatch) matches ZERO tests -> `dotnet test` exits 0 and writes NO
        // Statement (a silent no-op only the signer's class-7 refuse would catch). Require a `~` directly
        // binding the target name, and REJECT a `=`-bound exact form of the same name.
        Assert.True(
            Regex.IsMatch(producerRaw, @"--filter[^\n]*~" + Regex.Escape(FilterTarget)),
            "INV-007: the --filter must use the CONTAINS form `~EmitDeterminismStatement` " +
            "(a zero-match `=` form makes `dotnet test` a silent no-op that writes no Statement).");
        Assert.False(
            Regex.IsMatch(producerRaw, @"=\s*" + Regex.Escape(FilterTarget)),
            "INV-007: the --filter must NOT use the exact `=EmitDeterminismStatement` form (matches zero tests).");

        // The host emitter belongs to the UNPRIVILEGED producer, never the OIDC-holding signer.
        Assert.DoesNotContain(FilterTarget, signer);

        // A3: bind the committed workflow literal to the ONE shared C# const.
        Assert.Contains(DeterminismStatementEmitterHostTests.FilterTargetName, wf);
    }

    // Tests INV-007 [integration]: the producer's emit step binds EMIT_STATEMENT_RECEIPT to the
    // hand-off receipt and EMIT_STATEMENT_OUT to the hand-off statement, so the host writes the
    // Statement INTO the same-run hand-off. RED now: neither env key appears in the workflow.
    [Fact]
    public void Emit_step_binds_the_receipt_and_out_env_keys_to_the_handoff_paths()
    {
        string producer = JobSection(ReadWorkflow(), "producer");
        Assert.NotEqual(string.Empty, producer);

        Assert.True(
            Regex.IsMatch(producer, EnvKeyReceipt + @":\s*\S*" + Regex.Escape(HandoffReceipt)),
            $"INV-007: {EnvKeyReceipt} must bind to {HandoffReceipt} in the producer emit step.");
        Assert.True(
            Regex.IsMatch(producer, EnvKeyOut + @":\s*\S*" + Regex.Escape(HandoffStatement)),
            $"INV-007: {EnvKeyOut} must bind to {HandoffStatement} in the producer emit step.");
    }

    // Tests INV-007 [integration]: the emit step runs BEFORE the upload-artifact step (so the
    // Corrected-built Statement is inside the uploaded hand-off). RED now: there is no emit step, so
    // the EMIT_STATEMENT_OUT marker is absent and the ordering assertion cannot hold.
    [Fact]
    public void Emit_step_appears_before_the_upload_artifact_step_in_the_producer()
    {
        string producer = JobSection(ReadWorkflow(), "producer");
        Assert.NotEqual(string.Empty, producer);

        int emitIdx = producer.IndexOf(EnvKeyOut, StringComparison.Ordinal);
        int uploadIdx = producer.IndexOf("upload-artifact", StringComparison.Ordinal);

        Assert.True(emitIdx >= 0, $"INV-007: the producer must carry the {EnvKeyOut} emit step.");
        Assert.True(uploadIdx >= 0, "INV-007: the producer must upload the hand-off artifact.");
        Assert.True(
            emitIdx < uploadIdx,
            "INV-007: the emit step must run BEFORE upload-artifact so the Statement is in the hand-off.");
    }

    // Tests INV-007 [integration]: the emit step is NOT in the signer job — the unprivileged
    // producer builds the Statement; the OIDC signer only re-checks and signs it. RED-agnostic on
    // this axis (the current signer has no emit step) but it locks the end-state placement.
    [Fact]
    public void Emit_step_is_not_in_the_signer_job()
    {
        string signer = JobSection(ReadWorkflow(), "signer");
        Assert.NotEqual(string.Empty, signer);
        Assert.DoesNotContain(FilterTarget, signer);
        Assert.DoesNotContain(EnvKeyReceipt, signer);
        Assert.DoesNotContain(EnvKeyOut, signer);
    }

    // Tests INV-007 [integration]: the DEFERRED placeholder is GONE — no "DEFERRED real-run wiring"
    // marker and no echo asserting the statement "is written by the Corrected" builder remain. RED
    // now: the current producer carries exactly that placeholder step and echo, so both markers are
    // present and this cell fails until GREEN deletes them.
    [Fact]
    public void Deferred_placeholder_marker_and_echo_are_gone()
    {
        string wf = ReadWorkflow();
        Assert.DoesNotContain("DEFERRED real-run wiring", wf);
        Assert.DoesNotContain("is written by the Corrected", Norm(wf));
    }
}
