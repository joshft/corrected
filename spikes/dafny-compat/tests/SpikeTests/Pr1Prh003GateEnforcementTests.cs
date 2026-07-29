// PR1 (Group A) — PRH-003 GATE-ENFORCEMENT killing test.
//
// The PRH-003 privacy gate lives inside DeterminismReceiptWriter.RunCli: AFTER it
// Builds + Serializes the Corrected-authored RunReceipt, it scans the serialized
// JSON via ReceiptPrivacyScan.LocalIdentityLeaks and — on ANY leak — REFUSES to
// write and returns the infrastructure exit (3). It is a FAIL-CLOSED gate: a
// public-repo receipt must never carry the emitting host's identity.
//
// GAP this test closes: RunCli's REFUSAL path had zero test callers. Pr1PrhTests
// exercises ReceiptPrivacyScan in isolation (a leaky fixture string), and
// Pr1DeterminismLaneTests only asserts the DATA property "the real receipt happens
// to have no leaks". Nothing drove a LEAKY serialized receipt through the
// production RunCli gate to prove it refuses + does not write — so a regression
// defeating the gate (write the leaky receipt, return success) would survive.
//
// APPROACH — real end-to-end, NO production seam. The leak is injected through the
// LEGITIMATE `--runner-image` CLI arg, whose value flows verbatim into the
// Corrected-authored `platform.runner_image` field of the serialized receipt. A
// "/home/..." value is flagged by the scan's STATIC LocalPathMarkers on ANY host
// (host-independent — does not rely on the local username/hostname). A CONTROL run
// with a clean --runner-image over the SAME fixture proves Build + Serialize +
// AtomicWrite all succeed and the receipt IS written; so the leaky run's exit 3
// provably comes from the GATE, not from an incidental Build fault (which RunCli's
// catch block also maps to 3). The fixture reuses the committed projection
// self-test vector (a valid report that DeterministicProjection never throws on)
// as every role's per-run artifact, and the real committed schema / registries /
// policy-map, so Build succeeds cheaply with no run-spike.sh / resource floor.
using Corrected.Spike.Contracts;
using Xunit;

namespace Corrected.Spike.Tests;

public class Pr1Prh003GateEnforcementTests
{
    private static string Schema => SpikePaths.P("schema", "evidence-schema.json");
    private static string Registry => SpikePaths.P("schema", "schema-version-registry.json");
    private static string KindRegistry => SpikePaths.P("manifest", "determinism", "schema-kind-registry.json");
    private static string RoleRegistry => SpikePaths.P("manifest", "determinism", "role-registry.json");
    private static string PolicyMap => SpikePaths.P("manifest", "determinism", "projection-policy-map.json");
    private static string SampleReport => SpikePaths.P("evidence", "samples", "run-report.sample.json");

    // The emitter's role -> per-run report filename layout (matches RoleReportFile
    // in DeterminismReceiptWriter — Build reads <run-root>/reports/<file> per role).
    private static readonly string[] RoleReportFiles =
    {
        "run-report.json", "route-a.json", "route-b.json", "control-a.json", "control-b.json",
    };

    // Materialize a run root: <runRoot>/reports/<file> for every committed role,
    // each the committed projection self-test vector (a valid report the real
    // DeterministicProjection is proven never to throw on), so Build succeeds.
    private static void MaterializeRun(string runRoot, string reportContent)
    {
        var reports = Path.Combine(runRoot, "reports");
        Directory.CreateDirectory(reports);
        foreach (var file in RoleReportFiles)
        {
            File.WriteAllText(Path.Combine(reports, file), reportContent);
        }
    }

    // The exact emit-determinism-receipt argv the lane script uses
    // (scripts/determinism-lane.sh), plus explicit platform overrides so the
    // control receipt is host-INDEPENDENT and deterministic. The ONLY difference
    // between the control and leaky invocations is the --runner-image value.
    private static string[] EmitArgs(string r1, string r2, string outPath, string runnerImage) => new[]
    {
        "--r1", r1,
        "--r2", r2,
        "--schema", Schema,
        "--registry", Registry,
        "--kind-registry", KindRegistry,
        "--role-registry", RoleRegistry,
        "--policy-map", PolicyMap,
        "--out", outPath,
        "--os-label", "ubuntu-24.04",
        "--runner-image", runnerImage,
        "--kernel", "6.8.0-test",
        "--resolved-sdk", "10.0.302",
        "--attested-commit", "0123456789abcdef0123456789abcdef01234567",
        "--subject-manifest-digest", "c872c710dd390ff8d8050c059077d0eb7d6ef4f2352fc7bf375403014ac18509",
    };

    // Tests PRH-003 [integration] (fail-closed GATE ENFORCEMENT): a serialized
    // receipt carrying a local-identity leak in a Corrected-authored field drives
    // the PRODUCTION RunCli gate to REFUSE the write and return the infrastructure
    // exit (3) — it must NOT write the leaky receipt and NOT return success. A
    // regression that lets RunCli write the leaky receipt and succeed is killed by
    // BOTH assertions below. The CONTROL run proves the pipeline reaches the write
    // on the same fixture, so the leaky exit 3 is the gate, not a Build fault.
    [Fact]
    public void RunCli_LeakyReceipt_RefusesToWrite_AndReturnsInfraExit3()
    {
        var scratch = SpikePaths.TestScratch("pr1-prh003-gate-enforcement");
        var sample = File.ReadAllText(SampleReport);

        var r1 = Path.Combine(scratch, "r1");
        var r2 = Path.Combine(scratch, "r2");
        MaterializeRun(r1, sample);
        MaterializeRun(r2, sample);

        // CONTROL: a clean --runner-image. Build + Serialize + AtomicWrite must all
        // succeed and WRITE the receipt (exit is not the infra-refusal 3). This
        // proves the fixture drives RunCli all the way to the write step — so the
        // leaky run's exit 3 below is provably the PRH-003 gate, not a Build/catch
        // fault (which RunCli also maps to 3).
        var cleanOut = Path.Combine(scratch, "clean", "determinism-receipt.json");
        var cleanExit = DeterminismReceiptWriter.RunCli(EmitArgs(r1, r2, cleanOut, "ci-serial-runner"));
        Assert.True(File.Exists(cleanOut),
            $"control: RunCli must WRITE the receipt on a clean fixture (Build/Serialize/write all reached) — got exit {cleanExit}");
        Assert.NotEqual(3, cleanExit);
        // The written control receipt really is leak-free (else the fixture host itself leaks).
        Assert.Empty(ReceiptPrivacyScan.LocalIdentityLeaks(File.ReadAllText(cleanOut)));

        // LEAKY: inject a $HOME-style absolute-local path into the Corrected-authored
        // platform.runner_image field — flagged by the scan's static "/home/" marker
        // on ANY host (independent of the local username/hostname). Same fixture,
        // same everything else; only the runner_image differs.
        var leakyOut = Path.Combine(scratch, "leaky", "determinism-receipt.json");
        Assert.False(File.Exists(leakyOut)); // fresh path — nothing pre-exists
        var leakyExit = DeterminismReceiptWriter.RunCli(EmitArgs(r1, r2, leakyOut, "/home/attacker/corrected/build-host"));

        // (a) FAIL CLOSED: the infrastructure refusal exit (3), NOT success.
        Assert.Equal(3, leakyExit);
        // (b) and the leaky receipt is NEVER written to disk.
        Assert.False(File.Exists(leakyOut),
            "PRH-003 fail-closed: RunCli must REFUSE to write a receipt carrying a local-identity leak (it wrote the leaky receipt)");
    }
}
