// PR1 (Group A) RED tests — INV-005 slice ONLY.
//
// INV-005: the two nested runs execute in a DEDICATED CI job with NO competing
// parallel suite; the job records the observed ProcessorCount, RID, a PINNED OS
// label (not floating ubuntu-latest), and the actual runner image/version,
// kernel, architecture, and resolved SDK into the receipt. Enforcement includes
// a WORKFLOW-STRUCTURE assertion (distinct job; pinned OS not floating) — that
// structure check IS from-clean testable (a static YAML parse). The ACTUAL
// runner image/version recorded needs a real CI run (CI-network-only — see the
// note at the bottom).
//
// RED: the dedicated determinism-lane workflow file does not exist yet (missing
// file), and RunReceiptCodec.Parse is a stub (STUB:TDD).
using Corrected.Spike.Contracts;
using Xunit;

namespace Corrected.Spike.Tests;

public class Pr1Inv005SerialLaneTests
{
    // The dedicated serial determinism-lane workflow (GREEN creates it at this
    // pinned path; the spec calls it the isolated serial determinism lane).
    private const string LaneWorkflowRel = ".github/workflows/p3-determinism-lane.yml";

    private static string LaneWorkflowPath => SpikePaths.Repo(".github", "workflows", "p3-determinism-lane.yml");

    // Tests INV-005 [unit] (structural guard): the receipt platform identity type
    // carries EVERY pinned field — observed ProcessorCount, RID, a pinned OS label,
    // and the runner image/kernel/architecture/resolved SDK — and RunReceipt binds
    // it. Fails if a field is dropped (a synthesized/absent platform identity).
    [Fact]
    public void PlatformIdentity_ReceiptShape_CarriesEveryPinnedField()
    {
        var props = typeof(PlatformIdentity).GetProperties().Select(p => p.Name).ToHashSet();
        foreach (var required in new[] { "ProcessorCount", "Rid", "OsLabel", "RunnerImage", "Kernel", "Architecture", "ResolvedSdk" })
        {
            Assert.Contains(required, props);
        }
        Assert.Equal(typeof(PlatformIdentity), typeof(RunReceipt).GetProperty("Platform")!.PropertyType);
    }

    // Tests INV-005 [integration] (workflow-structure): the determinism run is
    // isolated in a DEDICATED job with a PINNED OS label (never floating
    // *-latest). RED: the dedicated lane workflow file does not exist yet.
    [Fact]
    public void DeterminismLaneWorkflow_IsDedicatedJob_WithPinnedOs()
    {
        Assert.True(File.Exists(LaneWorkflowPath),
            $"the dedicated serial determinism-lane workflow must exist at {LaneWorkflowRel} (INV-005) — a distinct job, not shared with the parallel conformance suite");
        var lines = File.ReadAllLines(LaneWorkflowPath);

        // Pinned OS: every runs-on is a pinned label, never a floating *-latest.
        var runsOn = lines.Where(l => l.TrimStart().StartsWith("runs-on:", StringComparison.Ordinal))
            .Select(l => l.Split(':', 2)[1].Trim()).ToList();
        Assert.NotEmpty(runsOn);
        Assert.All(runsOn, v => Assert.DoesNotContain("-latest", v)); // e.g. ubuntu-24.04, never ubuntu-latest

        // Dedicated job: a job id under `jobs:` naming the determinism/serial lane,
        // and no matrix strategy (no competing parallel suite in the same job).
        var jobIds = lines.Where(l => System.Text.RegularExpressions.Regex.IsMatch(l, "^  [A-Za-z0-9_-]+:\\s*$"))
            .Select(l => l.Trim().TrimEnd(':')).ToList();
        Assert.Contains(jobIds, id => id.Contains("determinism", StringComparison.OrdinalIgnoreCase)
            || id.Contains("serial", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(lines, l => l.TrimStart().StartsWith("matrix:", StringComparison.Ordinal));
    }

    // The heavyweight [integration] Exit tests that DRIVE the real lane — the
    // observed platform identity and the full real-artifact/host binding (B1) — now
    // live in Pr1DeterminismLaneTests.cs, which executes scripts/determinism-lane.sh
    // ONCE via the shared DeterminismLaneFixture and carries the
    // [Trait("Category","determinism-lane")] CI-separation trait. This class keeps
    // only the from-clean STATIC guards (receipt shape + workflow structure).

    // INV-005 CI-NETWORK-ONLY (NOT from-clean) — documented placeholder, NOT a
    // skipped phantom test (AP-013): the ACTUAL runner image/version being recorded
    // (a real GitHub Actions runner fact) can only be observed by a real CI run of
    // the serial lane. The from-clean tests above cover the receipt SHAPE + the
    // workflow STRUCTURE (distinct job, pinned OS) only.
}
