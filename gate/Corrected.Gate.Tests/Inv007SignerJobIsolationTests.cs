using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Corrected.Gate.Tests;

/// <summary>
/// P3 determinism-attestation spec INV-007 (~316-350) — the two-job signing workflow's
/// STRUCTURAL isolation, asserted by a pure STATIC YAML scan of the NEW committed workflow
/// <c>.github/workflows/p3-determinism-sign.yml</c> (read at test time via TestPaths.RepoFile,
/// mirroring the scan style of <see cref="TrustBoundaryTb007Tests"/> and
/// <see cref="Inv017CiWiringTests"/>). No subprocess — this class must NOT carry
/// [Collection("Subprocess")].
///
/// This is the BUILDABLE-NOW / reversible half of INV-007: the per-job permission split, the
/// event guard, SHA-pinned actions, the same-run @actions/artifact hand-off (NOT a cross-run
/// REST / gh-run-download path that would need actions:read — RS-032/EA-010), and the signer's
/// checkout hardening — none of which need a real signed bundle, real OIDC, or Rekor.
///
/// DEFERRED (NOT asserted here — needs a live signing run): INV-007's real permissions
/// TRANSCRIPT (the actual granted-permissions capture). This file asserts the DECLARED config
/// only; the transcript proof is out of this track's scope.
///
/// RED NOW: the workflow file does not exist yet, so RequireWorkflow() fails first — every cell
/// reads as "missing workflow", never a compile error. GREEN creates the file correctly and
/// each cell then verifies a specific structural property.
///
/// DECISION: job keys are pinned to `producer` and `signer` (the track's named jobs — the signer
/// carries `needs: producer`). Permissions/permission-set cells assume BLOCK YAML style (the
/// form the repo's existing workflows use); a flow-style {a: b} permissions map would fail the
/// exact-set cell, which is intended — GREEN emits block style.
///
/// AP-031 real-artifact clause is NOT triggered — this scans a committed CI workflow the feature
/// authors, not a `.correctless/artifacts/` producer output.
/// </summary>
public class Inv007SignerJobIsolationTests
{
    private static string WorkflowPath()
        => TestPaths.RepoFile(".github", "workflows", "p3-determinism-sign.yml");

    private static string ReadWorkflow()
    {
        Assert.True(
            File.Exists(WorkflowPath()),
            "INV-007: the NEW two-job signing workflow .github/workflows/p3-determinism-sign.yml must exist (GREEN deliverable).");
        return File.ReadAllText(WorkflowPath());
    }

    /// <summary>Normalize all whitespace runs to a single space so a YAML reflow can't hide a token.</summary>
    private static string Norm(string s) => Regex.Replace(s, @"\s+", " ").Trim();

    /// <summary>
    /// The [start, end) byte bounds of a top-level job section `  &lt;job&gt;:` (2-space indent under
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

    /// <summary>The text of a top-level job section (empty if the job key is absent).</summary>
    private static string JobSection(string yaml, string job)
    {
        (int s, int e) = SectionBounds(yaml, job);
        return s < 0 ? string.Empty : yaml.Substring(s, e - s);
    }

    /// <summary>
    /// The WORKFLOW-LEVEL (column-0) `permissions:` region — the header line plus any indented
    /// block entries — as raw text. Empty if there is no top-level permissions block. Job-level
    /// `permissions:` are indented, so `(?m)^permissions:` matches only the top-level one. This is
    /// the INHERITANCE source: a job that declares no own permissions inherits this.
    /// </summary>
    private static string TopLevelPermissionsRegion(string yaml)
    {
        Match m = Regex.Match(yaml, @"(?m)^permissions:.*$");
        if (!m.Success)
        {
            return string.Empty;
        }
        string rest = yaml.Substring(m.Index + m.Length);
        Match next = Regex.Match(rest, @"(?m)^\S"); // next line starting at column 0 ends the block
        int end = next.Success ? m.Index + m.Length + next.Index : yaml.Length;
        return yaml.Substring(m.Index, end - m.Index);
    }

    /// <summary>
    /// The `push:` trigger sub-block within the `on:` block — from `push:` to the next key at the
    /// same indent (or the end of the on-block). Empty if there is no block-style `push:` trigger.
    /// </summary>
    private static string PushBlock(string onBlock)
    {
        Match m = Regex.Match(onBlock, @"(?m)^(?<indent>[ \t]+)push:[ \t]*$");
        if (!m.Success)
        {
            return string.Empty;
        }
        int pushIndent = m.Groups["indent"].Value.Length;
        string[] lines = onBlock.Substring(m.Index + m.Length).Split('\n');
        var sb = new System.Text.StringBuilder();
        foreach (string raw in lines)
        {
            if (raw.Trim().Length == 0)
            {
                sb.Append('\n');
                continue;
            }
            int indent = raw.Length - raw.TrimStart(' ', '\t').Length;
            if (indent <= pushIndent)
            {
                break; // dedented out of the push sub-block
            }
            sb.Append(raw).Append('\n');
        }
        return sb.ToString();
    }

    /// <summary>
    /// The `on:` trigger block — from `on:` to the next top-level (0-indent) key or EOF.
    /// </summary>
    private static string OnBlock(string yaml)
    {
        Match start = Regex.Match(yaml, @"(?m)^on:[ \t]*$");
        if (!start.Success)
        {
            return string.Empty;
        }
        Match next = Regex.Match(yaml.Substring(start.Index + start.Length), @"(?m)^[A-Za-z0-9_-]+:");
        int end = next.Success ? start.Index + start.Length + next.Index : yaml.Length;
        return yaml.Substring(start.Index, end - start.Index);
    }

    /// <summary>
    /// The set of `key: value` entries in a job's `permissions:` block (block style). Returns the
    /// normalized `key: value` strings. Empty if the job has no block-style permissions map.
    /// </summary>
    private static HashSet<string> PermissionEntries(string jobSection)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        Match perm = Regex.Match(jobSection, @"(?m)^(?<indent>[ \t]+)permissions:[ \t]*$");
        if (!perm.Success)
        {
            return set;
        }
        int permIndent = perm.Groups["indent"].Value.Length;
        string[] lines = jobSection.Substring(perm.Index + perm.Length).Split('\n');
        foreach (string raw in lines)
        {
            if (raw.Trim().Length == 0)
            {
                continue;
            }
            int indent = raw.Length - raw.TrimStart(' ', '\t').Length;
            if (indent <= permIndent)
            {
                break; // dedented out of the permissions block
            }
            Match kv = Regex.Match(raw.Trim(), @"^([A-Za-z0-9_-]+):\s*(\S+)\s*$");
            if (kv.Success)
            {
                set.Add(kv.Groups[1].Value + ": " + kv.Groups[2].Value);
            }
        }
        return set;
    }

    // ==================================================================================
    // Two distinct jobs; the signer needs the producer.
    // ==================================================================================

    // Tests INV-007 [integration] ("two jobs — an unprivileged producer and a minimal signer"):
    // both jobs exist and the signer depends on the producer (`needs: producer`).
    [Fact]
    public void Two_jobs_producer_and_signer_with_signer_needs_producer()
    {
        string wf = ReadWorkflow();
        string producer = JobSection(wf, "producer");
        string signer = JobSection(wf, "signer");
        Assert.NotEqual(string.Empty, producer);
        Assert.NotEqual(string.Empty, signer);

        // `needs: producer` (scalar) or `needs: [producer]` (flow list) both satisfy the ordering.
        string signerNorm = Norm(signer);
        Assert.True(
            Regex.IsMatch(signerNorm, @"needs:\s*producer\b") ||
            Regex.IsMatch(signerNorm, @"needs:\s*\[[^\]]*\bproducer\b[^\]]*\]"),
            "INV-007: the signer job must declare `needs: producer` (same-run hand-off ordering).");
    }

    // ==================================================================================
    // Producer holds NO OIDC privilege.
    // ==================================================================================

    // Tests INV-007 [integration] ("the determinism producer holds no OIDC privilege ... no
    // id-token: write"): the producer job section grants NEITHER id-token NOR actions in its OWN
    // block. (The inheritance hole — a hoisted top-level grant — is closed by the two cells below.)
    [Fact]
    public void Producer_job_own_block_grants_neither_id_token_nor_actions()
    {
        string producer = JobSection(ReadWorkflow(), "producer");
        Assert.NotEqual(string.Empty, producer);
        Assert.DoesNotContain("id-token", producer);
        Assert.DoesNotContain("actions:", producer);
    }

    // Tests INV-007 [integration] (INV-007 CORE: "the determinism producer holds no OIDC
    // privilege"): the ONLY place an `id-token` grant may appear in the whole workflow is INSIDE
    // the signer job section. This closes the workflow-level `permissions:` INHERITANCE hole —
    // GitHub grants a job the TOP-LEVEL permissions when the job declares none (the already-
    // committed p3-determinism-lane.yml uses exactly a top-level permissions block), so a GREEN
    // that hoists `id-token: write` to the top level would leave the producer OIDC-privileged. Any
    // id-token grant outside the signer bounds (top-level or producer-level) fails this cell.
    [Fact]
    public void Every_id_token_grant_is_inside_the_signer_job_no_inheritance()
    {
        string wf = ReadWorkflow();
        (int sStart, int sEnd) = SectionBounds(wf, "signer");
        Assert.True(sStart >= 0, "INV-007: the signer job section must exist.");

        int idx = 0;
        while ((idx = wf.IndexOf("id-token", idx, StringComparison.Ordinal)) >= 0)
        {
            Assert.True(
                idx >= sStart && idx < sEnd,
                $"INV-007: an `id-token` grant at offset {idx} lies OUTSIDE the signer job " +
                "(top-level or producer-level) — the producer would INHERIT OIDC privilege. " +
                "id-token may be granted ONLY inside the signer job.");
            idx += "id-token".Length;
        }
    }

    // Tests INV-007 [integration] (inheritance hole, actions/broad grants): the WORKFLOW-LEVEL
    // (inheritable) permissions must grant NEITHER id-token NOR actions, and must not be the broad
    // `write-all` (which would hand the producer id-token: write). Guards the producer against
    // inheriting a privileged/broad top-level grant it never declares itself.
    [Fact]
    public void No_top_level_permissions_block_grants_id_token_actions_or_write_all()
    {
        string region = TopLevelPermissionsRegion(ReadWorkflow());
        // A missing top-level permissions block is fine (nothing inheritable to leak).
        Assert.DoesNotContain("id-token", region);
        Assert.DoesNotContain("actions", region);
        Assert.DoesNotContain("write-all", region);
    }

    // Tests INV-007 [integration] ("the signer cannot inspect the commit without read access"):
    // read access to contents is granted (to the producer or top-level), so the producer can
    // check out — but crucially WITHOUT id-token (asserted above).
    [Fact]
    public void Contents_read_is_granted_somewhere()
    {
        Assert.Contains("contents: read", ReadWorkflow());
    }

    // ==================================================================================
    // Signer permissions are EXACTLY id-token: write + contents: read.
    // ==================================================================================

    // Tests INV-007 [integration] ("the signer's permissions are exactly `id-token: write` +
    // `contents: read`"): the signer's block-style permissions map is the EXACT two-element set —
    // nothing broader. A superset (e.g. an added `actions: read` or `contents: write`) fails.
    [Fact]
    public void Signer_permissions_are_exactly_id_token_write_and_contents_read()
    {
        string signer = JobSection(ReadWorkflow(), "signer");
        Assert.NotEqual(string.Empty, signer);

        HashSet<string> perms = PermissionEntries(signer);
        var expected = new HashSet<string>(StringComparer.Ordinal)
        {
            "id-token: write",
            "contents: read",
        };
        Assert.True(
            expected.SetEquals(perms),
            "INV-007: signer permissions must be EXACTLY { id-token: write, contents: read }; got { " +
            string.Join(", ", perms.OrderBy(x => x)) + " }.");
    }

    // Tests INV-007 [integration] ("... which would need `actions: read` (forbidden here)"): the
    // signer job must NOT grant `actions:` anything (the tell of a cross-run/REST artifact path
    // — RS-032). Scoped to the signer section so an unrelated mention elsewhere can't false-pass.
    [Fact]
    public void Signer_job_does_not_grant_actions_permission()
    {
        string signer = JobSection(ReadWorkflow(), "signer");
        Assert.NotEqual(string.Empty, signer);
        Assert.DoesNotContain("actions:", signer);
    }

    // ==================================================================================
    // Event guard — signs ONLY a protected-main push + workflow_dispatch, NEVER a PR event.
    // ==================================================================================

    // Tests INV-007 [integration] ("signs only a protected-main `push` (never pull_request /
    // pull_request_target)"): the `on:` block has a `push:` trigger whose `branches:` list BINDS to
    // `main` (so a push-on-all-branches GREEN — a `push:` with no branches filter — fails), plus
    // workflow_dispatch, and carries NO pull_request / pull_request_target trigger. The
    // branches→main binding closes the comment-satisfiable `Contains("main")` gap.
    [Fact]
    public void Event_guard_push_main_and_dispatch_only_no_pull_request()
    {
        string on = OnBlock(ReadWorkflow());
        Assert.NotEqual(string.Empty, on);
        Assert.Contains("workflow_dispatch", on);
        Assert.DoesNotContain("pull_request", on); // also excludes pull_request_target (prefix)

        string push = PushBlock(on);
        Assert.NotEqual(string.Empty, push); // a block-style `push:` trigger must exist
        // The push trigger must be scoped by a `branches:` allow-filter (NOT branches-ignore, whose
        // substring lacks the "branches:" colon) that names `main` — so an unfiltered push (all
        // branches, no `branches:` key) fails, and a comment-only "main" outside the push block
        // cannot satisfy it (PushBlock is scoped to the push sub-block).
        Assert.Contains("branches:", push);
        Assert.Contains("main", push);
    }

    // ==================================================================================
    // Every third-party action is SHA-pinned (40-hex), never a @vN tag.
    // ==================================================================================

    // Tests INV-007 [integration] ("Third-party Actions pinned by commit SHA ... an action is
    // tag-pinned [is a violation]"): enumerate every `uses:` line and assert each references a
    // 40-hex commit SHA, never a @vN / @tag ref. A tag-pinned action fails with its own line.
    [Fact]
    public void Every_uses_action_is_pinned_by_40_hex_commit_sha()
    {
        string wf = ReadWorkflow();
        var uses = Regex.Matches(wf, @"(?m)uses:\s*(?<ref>\S+)")
            .Select(m => m.Groups["ref"].Value)
            .ToList();
        Assert.NotEmpty(uses); // the signing workflow uses checkout + artifact actions

        foreach (string u in uses)
        {
            int at = u.LastIndexOf('@');
            Assert.True(at >= 0, $"INV-007: `uses: {u}` must be pinned with an @<sha> ref.");
            string reference = u.Substring(at + 1);
            Assert.True(
                Regex.IsMatch(reference, "^[0-9a-f]{40}$"),
                $"INV-007: `uses: {u}` must be pinned by a 40-hex commit SHA, not a tag/branch ('{reference}').");
        }
    }

    // ==================================================================================
    // Same-run @actions/artifact hand-off — NO cross-run REST / gh run download / run-id download.
    // ==================================================================================

    // Tests INV-007 [integration] ("using the same-run @actions/artifact (v4) runtime-token
    // transfer"): the producer uploads and the signer downloads via upload-artifact /
    // download-artifact (same run).
    [Fact]
    public void Same_run_artifact_handoff_upload_and_download_present()
    {
        string wf = ReadWorkflow();
        Assert.Contains("upload-artifact", wf);
        Assert.Contains("download-artifact", wf);
    }

    // Tests INV-007 [integration] ("no Artifacts REST / cross-run `run-id` / `gh run download`,
    // which would need actions: read"): the workflow uses NONE of the cross-run download paths.
    // This is the RS-032 fail-open seam guard — a cross-run path silently breaks the minimal set.
    [Fact]
    public void No_cross_run_rest_or_gh_run_download_path()
    {
        string wf = ReadWorkflow();
        string norm = Norm(wf);
        Assert.DoesNotContain("gh run download", norm);
        // download-artifact `run-id:` input (cross-run) — a spaced colon (`run-id :`) must not
        // defeat the guard, so match the key form directly on the raw YAML.
        Assert.False(Regex.IsMatch(wf, @"(?m)run-id\s*:"),
            "INV-007: a cross-run `run-id:` download input needs actions:read (RS-032) — forbidden.");
        Assert.DoesNotContain("actions/artifacts", norm);        // Artifacts REST endpoint
        Assert.DoesNotContain("api.github.com", norm);
    }

    // ==================================================================================
    // Signer checkout hardening.
    // ==================================================================================

    // Tests INV-007 [integration] ("checks out at the exact attested_commit SHA with credentials
    // NOT persisted, no submodules, no LFS, and Git hooks disabled"): the signer job's checkout
    // sets persist-credentials: false and does NOT enable submodules or LFS.
    [Fact]
    public void Signer_checkout_is_hardened_no_persisted_creds_no_submodules_no_lfs()
    {
        string signer = JobSection(ReadWorkflow(), "signer");
        Assert.NotEqual(string.Empty, signer);
        Assert.Contains("persist-credentials: false", signer);
        Assert.DoesNotContain("submodules: true", signer);
        Assert.DoesNotContain("submodules: recursive", signer);
        Assert.DoesNotContain("lfs: true", signer);
    }

    // ==================================================================================
    // The signer invokes the extracted signer script verbatim (INV-007 "one frozen ... surface").
    // ==================================================================================

    // Tests INV-007 [integration] ("executes only one frozen, reviewed signer-validation surface
    // ... no arbitrary repository code"): the signer job invokes the extracted signer script
    // gate/tools/sign-determinism.sh. (INV-024 asserts the exact sync; this is the isolation-side
    // coupling — the signer runs the frozen surface, not producer/test/build code.)
    [Fact]
    public void Signer_job_invokes_the_extracted_signer_script()
    {
        string signer = JobSection(ReadWorkflow(), "signer");
        Assert.NotEqual(string.Empty, signer);
        Assert.Contains("gate/tools/sign-determinism.sh", signer);
    }
}
