// PR1 (Group A) — BLOCKING-1 regression (cverify, 2026-07-29): the extracted
// determinism lane AND the controller MUST refuse an OUT-OF-TREE --run-root
// fail-closed, BEFORE any SDK/build work.
//
// Why (root cause): SpikeRunRootRel is contractually SPIKE-relative
// (Directory.Build.props: SpikeRunRoot = $(MSBuildThisFileDirectory)$(SpikeRunRootRel)).
// For an out-of-tree run root the controller used to pass the ABSOLUTE path, so
// MSBuild wrote build outputs IN-TREE ($SPIKE_ROOT/<abs-minus-slash>/build/…) while
// the DD-008 completeness check resolved the TRUE absolute run root — a silent
// divergence that failed the run INCOMPLETE (exit 20) deep in the nested build; and
// the absolute host path would leak into the recorded restore/build argv (PRH-005).
//
// This is exactly the INV-024 / AP-020 / PMB-001 invocation-form gap the CI lane hit:
// p3-determinism-lane.yml drove the lane with --run-root "$RUNNER_TEMP/…" (outside the
// checked-out tree), but the only prior lane test used an in-tree TestScratch root, so
// the failing form was never exercised. These tests drive the REAL scripts with an
// out-of-tree run root and assert the early, clear, fail-closed refusal.
//
// NOT [Trait("Category","determinism-lane")]: the refusal fires before SDK/build/floor
// work, so these run in the general FROM-CLEAN gate (the real net), not only the
// >= 8-core lane — the very gate that would otherwise stay blind to this form.
using Xunit;

namespace Corrected.Spike.Tests;

public class Pr1RunRootBoundaryTests
{
    // A run root GUARANTEED to be outside the spike tree, mirroring CI's $RUNNER_TEMP
    // form. NOT Path.GetTempPath(): inside a controller run TMPDIR is redirected under
    // the (in-tree) run root, so GetTempPath() would return an in-tree path — the lane
    // legitimately ACCEPTS that and runs to completion, masking the very defect under
    // test (observed in the full from-clean suite). The spike is Linux-x64-pinned, so
    // /tmp is a stable out-of-tree anchor; assert it truly is out-of-tree so a
    // pathological layout fails loudly rather than vacuously passing.
    private static string OutOfTreeRoot()
    {
        var root = Path.Combine("/tmp", "corrected-oot-runroot-" + Guid.NewGuid().ToString("N"));
        Assert.False(root.StartsWith(SpikePaths.SpikeRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal),
            $"test setup: {root} is not outside the spike tree {SpikePaths.SpikeRoot} — pick a different out-of-tree anchor");
        return root;
    }

    // Tests BLOCKING-1 / INV-024 [integration] (AP-020/PMB-001 — EXECUTION of the CI
    // invocation form): the EXTRACTED lane script the CI job runs verbatim refuses an
    // out-of-tree --run-root fail-closed, early, so the SpikeRunRootRel contract
    // (SPIKE-relative → DD-008 agreement + PRH-005 argv) can never be violated.
    [Fact]
    public void DeterminismLane_RefusesOutOfTreeRunRoot_FailClosed()
    {
        var oot = OutOfTreeRoot();
        try
        {
            var run = Launch.Script("scripts/determinism-lane.sh", null, "--run-root", oot);

            Assert.False(run.ExitCode == 0,
                $"the lane accepted an out-of-tree --run-root ({oot}) — it must refuse fail-closed (BLOCKING-1/INV-024). stderr: {run.StdErr}");
            Assert.Contains("within the spike tree", run.StdErr);
            // fail-closed BEFORE any receipt is emitted
            Assert.False(File.Exists(Path.Combine(oot, "receipts", "determinism-receipt.json")),
                "a refused out-of-tree lane must emit NO receipt");
            // and BEFORE any build — no stray in-tree tree mirroring the out-of-tree path
            AssertNoStrayInTreeBuild(oot);
            // RLT-1/AUDIT-ARCH-1: the refused root must not orphan the empty dir it
            // created to canonicalize (the guard `rm -d`s it before exiting).
            Assert.False(Directory.Exists(oot),
                $"a refused out-of-tree lane run-root ({oot}) must not orphan an empty dir");
        }
        finally { TryDelete(oot); }
    }

    // Tests BLOCKING-1 [integration]: the controller itself (the root cause) refuses an
    // out-of-tree --run-root before the inner build, so a direct caller cannot trigger
    // the in-tree-stray-output / DD-008 divergence (or the PRH-005 argv leak) either.
    [Fact]
    public void RunSpike_RefusesOutOfTreeRunRoot_FailClosed()
    {
        var oot = OutOfTreeRoot();
        try
        {
            var run = Launch.Script("scripts/run-spike.sh", null, "--run-root", oot);

            Assert.False(run.ExitCode == 0,
                $"run-spike accepted an out-of-tree --run-root ({oot}) — it must refuse fail-closed (BLOCKING-1). stderr: {run.StdErr}");
            Assert.Contains("within the spike tree", run.StdErr);
            AssertNoStrayInTreeBuild(oot);
            // RLT-1/AUDIT-ARCH-1: the refused root must not orphan the empty dir it
            // created to canonicalize (the guard `rm -d`s it before exiting).
            Assert.False(Directory.Exists(oot),
                $"a refused out-of-tree run-root ({oot}) must not orphan an empty dir");
        }
        finally { TryDelete(oot); }
    }

    // LOW-1 (qa-r1 fix-diff-review): the guard must NOT delete a run-root the operator
    // PRE-CREATED — only the dir the guard itself made to canonicalize. A pre-existing
    // empty out-of-tree dir is refused (exit 20) but SURVIVES. determinism-lane.sh shares
    // the mirrored ensure_in_tree_run_root guard with the same created-only teardown.
    [Fact]
    public void RunSpike_RefusesOutOfTreeRunRoot_ButKeepsAPreExistingDir()
    {
        var oot = OutOfTreeRoot();
        Directory.CreateDirectory(oot); // operator pre-created the (empty) dir
        try
        {
            var run = Launch.Script("scripts/run-spike.sh", null, "--run-root", oot);

            Assert.False(run.ExitCode == 0,
                $"run-spike accepted an out-of-tree --run-root ({oot}). stderr: {run.StdErr}");
            Assert.Contains("within the spike tree", run.StdErr);
            // the guard only tears down a dir IT created; a pre-existing operator dir survives
            Assert.True(Directory.Exists(oot),
                $"the guard deleted a PRE-EXISTING operator dir ({oot}) it did not create (LOW-1)");
            AssertNoStrayInTreeBuild(oot);
        }
        finally { TryDelete(oot); }
    }

    // The bug wrote build outputs under $SPIKE_ROOT/<out-of-tree-path-minus-leading-slash>.
    // Assert no such stray in-tree tree was created (proves the refusal fired before build).
    private static void AssertNoStrayInTreeBuild(string outOfTreeRoot)
    {
        var strayTail = outOfTreeRoot.TrimStart(Path.DirectorySeparatorChar);
        var stray = Path.Combine(SpikePaths.SpikeRoot, strayTail);
        Assert.False(Directory.Exists(stray),
            $"a stray in-tree build tree was created at {stray} — the out-of-tree root was not refused before build (BLOCKING-1)");
    }

    private static void TryDelete(string dir)
    {
        try { if (Directory.Exists(dir)) { Directory.Delete(dir, recursive: true); } }
        catch { /* best-effort cleanup of the out-of-tree scratch */ }
    }
}
