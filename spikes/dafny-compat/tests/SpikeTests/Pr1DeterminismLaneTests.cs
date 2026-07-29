// PR1 (Group A) — the HEAVYWEIGHT determinism-lane integration tests, reconciled
// against GREEN's production. They EXECUTE the committed extracted lane script
// (scripts/determinism-lane.sh, INV-005/INV-024/AP-020/PMB-001), which drives the
// genuine TWO nested run-spike.sh runs into <run-root>/r1 and /r2 and emits the
// per-run x per-role RunReceipt at <run-root>/receipts/determinism-receipt.json.
//
// The canonical run does NOT emit a determinism receipt (it is isolated to this
// lane, INV-005), so these tests OBTAIN a real receipt by driving the lane — ONCE,
// via the shared DeterminismLaneFixture (IClassFixture), so the expensive
// two-nested-run lane runs a single time for all four tests here.
//
// CI separation: the class carries [Trait("Category", "determinism-lane")] — the
// name the p3-determinism-lane.yml "GENERAL-GATE SEPARATION" comment pins — so the
// general 4-vCPU conformance gate FILTERS THESE OUT (they throw a LOUD TYPED skip
// below the >= 8-core floor); the dedicated floor-capable lane runs them for real.
using System.Runtime.InteropServices;
using System.Text.Json;
using Corrected.Spike.Contracts;
using Xunit;

namespace Corrected.Spike.Tests;

/// <summary>
/// Drives the extracted determinism lane ONCE (INV-005) and exposes the run root +
/// parsed receipt to every test in the class. Below the committed resource floor it
/// does not run the lane (the two-nested-run controller flaps); each test then
/// throws a LOUD TYPED skip — never a silent pass (INV-001/INV-004).
/// </summary>
public sealed class DeterminismLaneFixture : IDisposable
{
    public int Floor { get; }
    public bool BelowFloor { get; }
    public string? RunRoot { get; }
    public int? ExitCode { get; }
    public string StdErr { get; } = "";
    public RunReceipt? Receipt { get; }

    private readonly SpikePaths.ScratchScope? _scope;

    public DeterminismLaneFixture()
    {
        // The floor is the single committed source (INV-004), read directly.
        using (var floorDoc = SpikePaths.Json(SpikePaths.P("manifest", "determinism", "resource-floor.json")))
        {
            Floor = floorDoc.RootElement.GetProperty("core_floor").GetInt32();
        }
        if (Environment.ProcessorCount < Floor)
        {
            BelowFloor = true;
            return; // do not run the lane below floor — each test issues the loud typed skip
        }

        _scope = SpikePaths.TransientScratch("pr1-determinism-lane-shared");
        RunRoot = _scope.Root;

        // Execute the committed extracted lane script VERBATIM (INV-024/RS-028).
        var run = Launch.Script("scripts/determinism-lane.sh", null, "--run-root", RunRoot);
        ExitCode = run.ExitCode;
        StdErr = run.StdErr;

        var receiptPath = Path.Combine(RunRoot, "receipts", "determinism-receipt.json");
        if (run.ExitCode == 0 && File.Exists(receiptPath))
        {
            Receipt = RunReceiptCodec.Parse(File.ReadAllText(receiptPath));
        }
    }

    public void Dispose()
    {
        // Reclaim the ~GB run roots only on a clean lane run; a failed lane keeps its
        // roots for debugging (ScratchScope deletes only when Commit() was reached).
        if (Receipt is not null)
        {
            _scope?.Commit();
        }
        _scope?.Dispose();
    }
}

[Trait("Category", "determinism-lane")]
public class Pr1DeterminismLaneTests : IClassFixture<DeterminismLaneFixture>
{
    private readonly DeterminismLaneFixture _fx;

    public Pr1DeterminismLaneTests(DeterminismLaneFixture fx) => _fx = fx;

    // A LOUD TYPED resource-floor skip (throw — mirrors SpikePaths.RequireProvenRid /
    // MA-ED-4: a scope gate is a NON-PASS outcome), never a silent early-return.
    private RunReceipt RequireReceipt()
    {
        if (_fx.BelowFloor)
        {
            throw new InvalidOperationException(
                $"skipped (resource floor): the two-nested-run determinism lane needs >= {_fx.Floor} CPUs to avoid "
                + $"contention-induced flap; host reports {Environment.ProcessorCount}. This is a LOUD TYPED non-pass "
                + "— NEVER a silent early-return (INV-001/INV-004); run on the pinned floor-capable lane.");
        }
        Assert.True(_fx.ExitCode == 0, $"determinism lane script failed (exit {_fx.ExitCode}): {_fx.StdErr}");
        Assert.NotNull(_fx.RunRoot);
        Assert.NotNull(_fx.Receipt);
        return _fx.Receipt!;
    }

    // Tests INV-001 [integration]: the REAL two-nested-run controller feeds the
    // classifier; the emitted receipt carries a status pair on the CLOSED legal table
    // (via the real DeterminismClassifier), with no attestation/verification field.
    [Fact]
    public void Lane_StatusPairIsLegal_FromRealRun()
    {
        var receipt = RequireReceipt();
        Assert.True(DeterminismClassifier.IsLegalStatusPair(receipt.Execution, receipt.Comparison),
            "the emitted receipt carries a status pair outside the closed legal-status table (INV-001)");
    }

    // Tests INV-002 [integration] (Exit contract): the receipt covers all 5 roles,
    // and comparison_status is `equal` IFF every per-role deterministic PROJECTION
    // agrees across the two runs — bound to the REAL per-run artifacts under the run
    // root (each cell's ProjectionSha256 == the real projection of its own file).
    [Fact]
    public void Lane_CoversFiveRoles_EqualIffProjectionsAgree()
    {
        var receipt = RequireReceipt();
        var root = _fx.RunRoot!;
        var schemaPath = SpikePaths.P("schema", "evidence-schema.json");
        var roles = new HashSet<string> { "run", "route-a", "route-b", "control-a", "control-b" };
        Assert.Equal(roles, receipt.Run1Evidence.Select(e => e.Role).ToHashSet());
        Assert.Equal(roles, receipt.Run2Evidence.Select(e => e.Role).ToHashSet());

        var allProjectionsAgree = true;
        foreach (var role in roles)
        {
            var c1 = receipt.Run1Evidence.Single(e => e.Role == role);
            var c2 = receipt.Run2Evidence.Single(e => e.Role == role);
            var f1 = Path.Combine(root, "r1", c1.RepoRelativeName.Replace('/', Path.DirectorySeparatorChar));
            var f2 = Path.Combine(root, "r2", c2.RepoRelativeName.Replace('/', Path.DirectorySeparatorChar));

            var p1 = SpikePaths.Sha256Text(EvidenceSchema.DeterministicProjection(File.ReadAllText(f1), schemaPath));
            var p2 = SpikePaths.Sha256Text(EvidenceSchema.DeterministicProjection(File.ReadAllText(f2), schemaPath));
            // each cell's recorded projection digest is the REAL projection of its own file
            Assert.Equal(p1, c1.ProjectionSha256);
            Assert.Equal(p2, c2.ProjectionSha256);
            if (p1 != p2)
            {
                allProjectionsAgree = false;
            }
        }
        // the derived aggregate is `equal` IFF all 5 role projections agree
        Assert.Equal(allProjectionsAgree ? ComparisonStatus.Equal : ComparisonStatus.Different, receipt.Comparison);
    }

    // Tests INV-005 [integration] (Exit contract): the receipt records the observed
    // platform identity of the EMITTING HOST — ProcessorCount / RID / architecture ==
    // this host, a PINNED OS label (never floating *-latest), and non-empty runner
    // image / kernel / resolved SDK. Defeats a synthetic fabricated platform block.
    [Fact]
    public void Lane_CarriesObservedPinnedPlatformIdentity()
    {
        var p = RequireReceipt().Platform;
        Assert.Equal(Environment.ProcessorCount, p.ProcessorCount);
        Assert.Equal(RuntimeInformation.RuntimeIdentifier, p.Rid);
        Assert.Equal(RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant(), p.Architecture.ToLowerInvariant());
        Assert.False(string.IsNullOrWhiteSpace(p.OsLabel));
        Assert.DoesNotContain("-latest", p.OsLabel); // a pinned OS label, not floating
        Assert.False(string.IsNullOrWhiteSpace(p.RunnerImage));
        Assert.False(string.IsNullOrWhiteSpace(p.Kernel));
        Assert.False(string.IsNullOrWhiteSpace(p.ResolvedSdk));
    }

    // Tests PRH-003 [integration] (QA-002): the REAL emitted receipt carries NO
    // local-environment identity leak (hostname / username / home / temp /
    // absolute-local path) in any Corrected-authored field — PRH-003 proven on the
    // ACTUAL artifact, not just fixtures. The producer also FAILS CLOSED on a leak
    // (DeterminismReceiptWriter refuses to write + exits non-zero), so a leaking run
    // never even produces a receipt to reach here.
    [Fact]
    public void Lane_EmittedReceipt_HasNoLocalIdentityLeaks()
    {
        RequireReceipt(); // loud typed skip below floor; otherwise the lane ran and wrote the receipt
        var receiptPath = Path.Combine(_fx.RunRoot!, "receipts", "determinism-receipt.json");
        var leaks = ReceiptPrivacyScan.LocalIdentityLeaks(File.ReadAllText(receiptPath));
        Assert.Empty(leaks);
    }

    // Tests INV-001/002/005 [integration] (B1 — AP-020/PMB-001: an EXECUTION test,
    // never a YAML/doc grep): the receipt BINDS to the REAL emitted artifacts under
    // the EPHEMERAL RUN ROOT, per-run (run1 -> root/r1/<name>, run2 -> root/r2/<name>).
    // RepoRelativeName stays repo-relative in the receipt (no run-root/run-id leak,
    // Inv009); only the TEST resolves it under the run root. The two runs are proven
    // genuinely DISTINCT (separate files + separate raw digests) while their
    // deterministic PROJECTIONS agree — so a synthetic constant receipt, a single run
    // masquerading as two, or a pre-existing repo file cannot pass.
    [Fact]
    public void Lane_BindsReceiptToRealArtifactsAndHost()
    {
        var receipt = RequireReceipt();
        var root = _fx.RunRoot!;

        // INV-001: the emitted status pair is on the closed legal table (real classifier).
        Assert.True(DeterminismClassifier.IsLegalStatusPair(receipt.Execution, receipt.Comparison));

        // INV-005: platform identity == the EMITTING HOST (defeats fabricated processor_count/rid).
        Assert.Equal(Environment.ProcessorCount, receipt.Platform.ProcessorCount);
        Assert.Equal(RuntimeInformation.RuntimeIdentifier, receipt.Platform.Rid);
        Assert.Equal(RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant(), receipt.Platform.Architecture.ToLowerInvariant());

        // INV-002: every role cell binds to a REAL per-run artifact under the run root.
        var schemaPath = SpikePaths.P("schema", "evidence-schema.json");
        var roles = new[] { "run", "route-a", "route-b", "control-a", "control-b" };
        Assert.Equal(roles.ToHashSet(), receipt.Run1Evidence.Select(e => e.Role).ToHashSet());
        Assert.Equal(roles.ToHashSet(), receipt.Run2Evidence.Select(e => e.Role).ToHashSet());

        foreach (var role in roles)
        {
            var c1 = receipt.Run1Evidence.Single(e => e.Role == role);
            var c2 = receipt.Run2Evidence.Single(e => e.Role == role);

            var f1 = Path.Combine(root, "r1", c1.RepoRelativeName.Replace('/', Path.DirectorySeparatorChar));
            var f2 = Path.Combine(root, "r2", c2.RepoRelativeName.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(f1),
                $"role {role}: run1 emitted no artifact under the run root at {f1} — determinism artifacts live under the "
                + "ephemeral run root, never the tracked repo tree (Inv009)");
            Assert.True(File.Exists(f2), $"role {role}: run2 emitted no artifact under the run root at {f2}");

            // Two genuinely distinct runs: distinct files AND distinct raw digests.
            Assert.NotEqual(f1, f2);
            Assert.NotEqual(c1.RawSha256, c2.RawSha256);

            // Raw digest binds to the REAL per-run file (defeats fabricated cells).
            Assert.Equal(SpikePaths.Sha256File(f1), c1.RawSha256);
            Assert.Equal(SpikePaths.Sha256File(f2), c2.RawSha256);

            // The deterministic PROJECTION agrees across the two runs, and each cell's
            // ProjectionSha256 is the REAL projection of its own file.
            var p1 = EvidenceSchema.DeterministicProjection(File.ReadAllText(f1), schemaPath);
            var p2 = EvidenceSchema.DeterministicProjection(File.ReadAllText(f2), schemaPath);
            Assert.Equal(p1, p2);
            Assert.Equal(SpikePaths.Sha256Text(p1), c1.ProjectionSha256);
            Assert.Equal(SpikePaths.Sha256Text(p2), c2.ProjectionSha256);
        }
    }
}
