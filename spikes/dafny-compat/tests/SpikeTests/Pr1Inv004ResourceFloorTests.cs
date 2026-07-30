// PR1 (Group A) RED tests — INV-004 slice, FROM-CLEAN SUBSET ONLY.
//
// INV-004: the resource floor is a SINGLE-SOURCED constant set by a campaign
// whose plan commit PREDATES every included run; each row records
// {run_id, run_attempt, head_sha, plan_commit, plan_digest}. From-clean-testable
// assertions ONLY: plan_commit is an ANCESTOR of the row's head_sha; all rows
// run_attempt==1; the floor is defined in exactly ONE place; retained rows
// committed as the basis. Below the floor => execution_status=resource_floor_skipped
// (valid non-attesting).
//
// CI-NETWORK-ONLY (NOT from-clean — see the note at the bottom): the
// run_id<->head_sha association (a live GitHub Actions API fact) and the
// eligible-run-SEQUENCE set-equality vs the authoritative run listing (RS-016).
//
// RED: ResourceFloorCampaign.Load / CoreFloor / PlanPredatesRun and
// DeterminismClassifier.Classify throw NotImplementedException (STUB:TDD); the
// committed campaign rows are an empty placeholder until GREEN's measurement
// campaign fills them, and the routing through Load asserts Rows.Count>0 so an
// empty campaign can never vacuously pass (AP-018).
using System.Security.Cryptography;
using System.Text;
using Corrected.Spike.Contracts;
using Xunit;

namespace Corrected.Spike.Tests;

public class Pr1Inv004ResourceFloorTests
{
    private static string FloorPath => SpikePaths.P("manifest", "determinism", "resource-floor.json");
    private static string RowsPath => SpikePaths.P("manifest", "determinism", "campaign-rows.json");

    // Tests INV-004 [unit]: the campaign retains at least one row committed as the
    // basis, and EVERY retained row is attempt-1 (no cherry-picking a passing
    // re-run). Routing through Load asserts non-empty so an empty campaign cannot
    // vacuously pass (AP-018).
    [Fact]
    public void Campaign_RetainsRows_AllAttemptOne()
    {
        var plan = ResourceFloorCampaign.Load(FloorPath, RowsPath);
        Assert.NotEmpty(plan.Rows);
        Assert.All(plan.Rows, row => Assert.Equal(1, row.RunAttempt));
    }

    // Tests INV-004 [unit] (IB-002 / PAT-004): the committed plan_digest is RECOMPUTABLE
    // from the committed plan parameters — not merely loaded and trusted. sha256 of the
    // canonical preimage `core_floor=…;plan_commit=…;n=…;rule=…` must equal the committed
    // plan_digest, the committed row COUNT must equal the declared campaign size n, and
    // every retained row must carry that same plan_digest. A parameter edited without
    // recomputing the digest (or a row count that drifts from n) fails RED.
    [Fact]
    public void PlanDigest_RecomputesFromCommittedParameters_AndBindsEveryRow()
    {
        using var floor = SpikePaths.Json(FloorPath);
        var coreFloor = floor.RootElement.GetProperty("core_floor").GetInt32();
        var planCommit = floor.RootElement.GetProperty("plan_commit").GetString()!;
        var n = floor.RootElement.GetProperty("n").GetInt32();
        var committedDigest = floor.RootElement.GetProperty("plan_digest").GetString()!;

        // The canonical campaign-protocol rule token — the `rule=` field of the
        // plan_digest preimage documented in resource-floor.json's _comment (a shortened
        // protocol constant, NOT the prose eligible_run_sequence_rule field).
        const string RuleToken = "first-N-attempt-1-runs-after-plan-commit-on-pinned-serial-lane";
        var preimage = $"core_floor={coreFloor};plan_commit={planCommit};n={n};rule={RuleToken}";
        var recomputed = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(preimage))).ToLowerInvariant();
        Assert.Equal(committedDigest, recomputed);

        var plan = ResourceFloorCampaign.Load(FloorPath, RowsPath);
        Assert.Equal(n, plan.Rows.Count);
        Assert.All(plan.Rows, row => Assert.Equal(committedDigest, row.PlanDigest));
    }

    // Tests INV-004 [unit]: the plan cannot be chosen after seeing results — the
    // plan_commit PREDATES every included run, expressed as: plan_commit is an
    // ANCESTOR of each row's head_sha (git cannot compare a commit to a run ID
    // directly). Every row also carries the same plan_commit as the plan header.
    [Fact]
    public void Campaign_PlanCommit_IsAncestorOfEveryRowHeadSha()
    {
        var plan = ResourceFloorCampaign.Load(FloorPath, RowsPath);
        Assert.NotEmpty(plan.Rows);
        foreach (var row in plan.Rows)
        {
            Assert.Equal(plan.PlanCommit, row.PlanCommit);
            Assert.True(ResourceFloorCampaign.PlanPredatesRun(plan.PlanCommit, row.HeadSha),
                $"plan_commit {plan.PlanCommit} must be an ancestor of head_sha {row.HeadSha} (INV-004) — the plan predates the run");
        }
    }

    // Tests INV-004 [unit] (AP-005 negative — B3): an always-true PlanPredatesRun
    // (a `return true` stub) must FAIL here. Using two REAL repo commits in known
    // order (HEAD and its parent), the parent IS an ancestor of HEAD but HEAD is
    // NOT an ancestor of its own parent — so the ancestry guard is proven to
    // actually compute reachability, not vacuously accept.
    [Fact]
    public void PlanPredatesRun_HasHonestNegative_OverRealCommits()
    {
        var head = GitRevParse("HEAD");
        var parent = GitRevParse("HEAD~1");
        Assert.NotEqual(head, parent); // two distinct real commits in known order

        Assert.True(ResourceFloorCampaign.PlanPredatesRun(parent, head),
            "the parent commit must be an ancestor of HEAD (INV-004)");
        Assert.False(ResourceFloorCampaign.PlanPredatesRun(head, parent),
            "HEAD must NOT be an ancestor of its own parent — an always-true impl fails here (AP-005)");
    }

    private static string GitRevParse(string rev)
    {
        var psi = new System.Diagnostics.ProcessStartInfo("git", "")
        {
            WorkingDirectory = SpikePaths.RepoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add("rev-parse");
        psi.ArgumentList.Add(rev);
        using var proc = System.Diagnostics.Process.Start(psi)!;
        var output = proc.StandardOutput.ReadToEnd().Trim();
        proc.WaitForExit(15000);
        Assert.True(proc.ExitCode == 0, $"git rev-parse {rev} failed (need real repo history for the ancestry negative)");
        return output;
    }

    // Tests INV-004 [unit]: the core floor is a SINGLE-SOURCED constant — it lives
    // in exactly ONE committed place (manifest/determinism/resource-floor.json) and
    // the old hardcoded `const int coreFloor = 8` early-return is GONE from
    // Inv010DeterminismTests.cs. RED: CoreFloor is a stub, and `coreFloor` is still
    // hardcoded in Inv010 until GREEN removes it.
    [Fact]
    public void ResourceFloor_IsSingleSourced_NotHardcodedInInv010()
    {
        // Single-source scan (RED now): the floor may not be hardcoded in the test source.
        var inv010 = File.ReadAllText(SpikePaths.P("tests", "SpikeTests", "Inv010DeterminismTests.cs"));
        Assert.DoesNotContain("coreFloor", inv010);

        // The single source is the committed floor file (RED via stub).
        Assert.True(ResourceFloorCampaign.CoreFloor(FloorPath) > 4,
            "the campaign-confirmed floor must exceed 4 vCPU (RS-009) — 4-core public runners flap the two-nested-run controller");
    }

    // Tests INV-004 [unit]: below the floor, the observation maps to the TYPED
    // execution_status=resource_floor_skipped / not_evaluated — a valid
    // non-attesting outcome, never a silent skip and never comparison_status=different.
    [Fact]
    public void BelowFloor_MapsTo_ResourceFloorSkipped()
    {
        Assert.Equal(new ReceiptStatus(ExecutionStatus.ResourceFloorSkipped, ComparisonStatus.NotEvaluated),
            DeterminismClassifier.Classify(RunnerOutcomeKind.BelowResourceFloor));
    }

    // INV-004 CI-NETWORK-ONLY (NOT from-clean) — documented placeholder, NOT a
    // skipped phantom test (AP-013): the run_id<->head_sha association is a live
    // GitHub Actions API fact, and the eligible-run-SEQUENCE set-equality vs the
    // authoritative complete run listing (RS-016 — a per-row + count check cannot
    // detect an OMITTED disagreeing run) are verified only in the live CI lane, not
    // from a clean checkout. The from-clean gate above covers ancestry + attempt-1
    // + single-source + retained-rows-committed only.
}
