// PR1 (Group A) RED tests — PRH-003 / PRH-004 / PRH-005 slice ONLY.
//
// PRH-003: the Corrected-authored receipt/predicate fields contain NO local
//   hostname/username/home/temp/absolute-local path (a field scan restricted to
//   Corrected fields; the Sigstore bundle's PUBLIC identities are exempt — but
//   PR1 has no bundle).
// PRH-004: PR1 does NOT flip P3 — P3.satisfied stays false; no readiness-block
//   edit in PR1 (a from-clean read of the committed block).
// PRH-005: a `different` result is not signed (PR1 signs nothing); no
//   retry/continue-on-error construct in the lane.
//
// RED: ReceiptPrivacyScan.LocalIdentityLeaks and DeterminismDisposition.Dispose
// throw NotImplementedException (STUB:TDD); the determinism-lane workflow does
// not exist yet (missing file). PRH-004 is a from-clean REGRESSION GUARD (P3 is
// false today and PR1 must keep it that way) — green now, it fails if PR1 ever
// flips P3.
using Corrected.Spike.Contracts;
using Xunit;

namespace Corrected.Spike.Tests;

public class Pr1PrhTests
{
    private const string LaneWorkflowRel = ".github/workflows/p3-determinism-lane.yml";

    // A CLEAN Corrected-authored receipt (public/repo-relative fields only).
    private const string CleanReceiptJson = """
    {
      "execution_status": "completed",
      "comparison_status": "equal",
      "attested_commit": "0123456789abcdef0123456789abcdef01234567",
      "subject_manifest_digest": "c872c710dd390ff8d8050c059077d0eb7d6ef4f2352fc7bf375403014ac18509",
      "policy_version": "1",
      "platform": {
        "processor_count": 8,
        "rid": "linux-x64",
        "os_label": "ubuntu-24.04",
        "runner_image": "ubuntu-24.04-20260720.1",
        "kernel": "6.8.0",
        "architecture": "x64",
        "resolved_sdk": "10.0.302"
      },
      "evidence": [
        { "role": "run", "kind": "run-report", "repo_relative_name": "reports/run-report.json", "raw_sha256": "aa", "projection_sha256": "bb" }
      ]
    }
    """;

    // A LEAKY receipt — local hostname / username / $HOME path / /tmp path injected
    // into Corrected-authored fields (the class PRH-003 forbids on a public repo).
    private const string LeakyReceiptJson = """
    {
      "execution_status": "completed",
      "comparison_status": "equal",
      "attested_commit": "0123456789abcdef0123456789abcdef01234567",
      "platform": {
        "processor_count": 8,
        "rid": "linux-x64",
        "os_label": "ubuntu-24.04",
        "runner_image": "built by jdoe on host dev-laptop-01",
        "resolved_sdk": "10.0.302"
      },
      "evidence": [
        { "role": "run", "kind": "run-report", "repo_relative_name": "/home/jdoe/projects/corrected/reports/run-report.json", "scratch": "/tmp/inv010-run-twice/r1" }
      ]
    }
    """;

    // Tests PRH-003 [unit]: a clean Corrected-authored receipt has NO local-identity
    // leaks (empty finding set).
    [Fact]
    public void ReceiptPrivacyScan_CleanReceipt_HasNoLeaks()
    {
        Assert.Empty(ReceiptPrivacyScan.LocalIdentityLeaks(CleanReceiptJson));
    }

    // Tests PRH-003 [unit] (defensive — QA-006): a receipt leaking a local username /
    // hostname (free-text), a $HOME absolute path, and a /tmp path is FLAGGED at the
    // SPECIFIC offending Corrected-authored fields — the EXACT offender set, not merely
    // non-emptiness. The clean fields in the SAME receipt (rid, os_label, resolved_sdk,
    // role, kind, hex commit) are NOT flagged. FAILS a scan that over- or under-reports.
    [Fact]
    public void ReceiptPrivacyScan_LeakyReceipt_FlagsExactOffenderFields()
    {
        var offenders = ReceiptPrivacyScan.LocalIdentityLeaks(LeakyReceiptJson).ToHashSet();

        // EXACT offender set: the three fields carrying a real local-identity leak.
        var expected = new HashSet<string>
        {
            "platform.runner_image",          // "built by jdoe on host dev-laptop-01"
            "evidence[0].repo_relative_name", // /home/jdoe/... absolute local path
            "evidence[0].scratch",            // /tmp/... temp path
        };
        Assert.Equal(expected, offenders);

        // Belt-and-suspenders: the clean fields in the SAME receipt are excluded.
        foreach (var clean in new[]
                 {
                     "execution_status", "comparison_status", "attested_commit",
                     "platform.rid", "platform.os_label", "platform.resolved_sdk",
                     "platform.processor_count", "evidence[0].role", "evidence[0].kind",
                 })
        {
            Assert.DoesNotContain(clean, offenders);
        }
    }

    // Tests PRH-004 [integration] (from-clean REGRESSION GUARD): PR1 does not flip
    // P3 — the committed implementation_readiness block declares P3.satisfied:false
    // with evidence:null. Fails if any PR1 edit flips P3 to satisfied/true or
    // attaches evidence. (No readiness-block edit is part of the PR1 diff.)
    [Fact]
    public void Pr1_DoesNotFlipP3_ReadinessBlockStaysFalse()
    {
        var specPath = SpikePaths.Repo(".correctless", "specs", "phase-0-1-worker.md");
        var lines = File.ReadAllLines(specPath);

        var p3 = Array.FindIndex(lines, l => l.Trim() == "- id: P3");
        Assert.True(p3 >= 0, "no `- id: P3` precondition found in the committed readiness block");

        string? satisfied = null, evidence = null;
        for (var i = p3 + 1; i < lines.Length; i++)
        {
            var t = lines[i].Trim();
            if (t.StartsWith("- id:", StringComparison.Ordinal))
            {
                break; // next precondition
            }
            if (t.StartsWith("satisfied:", StringComparison.Ordinal))
            {
                satisfied = t.Substring("satisfied:".Length).Trim();
            }
            else if (t.StartsWith("evidence:", StringComparison.Ordinal))
            {
                evidence = t.Substring("evidence:".Length).Split('#')[0].Trim();
            }
        }
        Assert.Equal("false", satisfied);
        Assert.Equal("null", evidence);
    }

    // Tests PRH-005 [unit]: a `different` result is NOT signed and mints nothing;
    // and PR1 SIGNS NOTHING — even a mint-eligible completed∧equal disposition has
    // signing outcome not_attempted in PR1 (no signer exists yet).
    [Fact]
    public void Different_IsNotSigned_And_Pr1SignsNothing()
    {
        var different = DeterminismDisposition.Dispose(new ReceiptStatus(ExecutionStatus.Completed, ComparisonStatus.Different));
        Assert.Equal(SigningOutcome.NotAttempted, different.Signing);
        Assert.False(different.MintEligible);

        var equal = DeterminismDisposition.Dispose(new ReceiptStatus(ExecutionStatus.Completed, ComparisonStatus.Equal));
        Assert.True(equal.MintEligible);                          // a completed∧equal receipt is mint-ELIGIBLE...
        Assert.Equal(SigningOutcome.NotAttempted, equal.Signing); // ...but PR1 signs NOTHING (no signer)
    }

    // Tests PRH-005 [integration] (structure): the determinism lane has NO retry /
    // continue-on-error construct — a flap requires a new reviewed commit, never a
    // retry into green. RED: the dedicated lane workflow does not exist yet.
    [Fact]
    public void DeterminismLaneWorkflow_HasNoRetryOrContinueOnError()
    {
        var path = SpikePaths.Repo(".github", "workflows", "p3-determinism-lane.yml");
        Assert.True(File.Exists(path),
            $"the dedicated serial determinism-lane workflow must exist at {LaneWorkflowRel} (INV-005/PRH-005)");
        var text = File.ReadAllText(path);
        foreach (var forbidden in new[] { "continue-on-error", "max_attempts", "max-attempts", "retries:", "nick-fields/retry" })
        {
            Assert.DoesNotContain(forbidden, text, StringComparison.OrdinalIgnoreCase);
        }
    }
}
