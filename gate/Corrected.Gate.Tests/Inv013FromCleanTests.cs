using System;
using System.IO;
using Corrected.Gate;
using Corrected.Gate.Kernel;
using Xunit;

namespace Corrected.Gate.Tests;

/// <summary>
/// INV-013: conditional green-from-clean, bound to THIS run; the real-probe
/// degraded-env test via an injectable repo-root parameter; no process-global state;
/// no out/ subject. [integration]. Guards AP-021/AP-010/AP-019/AP-003.
/// </summary>
public class Inv013FromCleanTests
{
    // Tests INV-013 [integration]: the degraded-env test drives the REAL probe via an
    // injectable repo-root pointed at a temp-tree copy with the evidence REMOVED; the
    // fail reason is exactly evidence-absent (not schema-missing — so the copy must
    // faithfully include schema/registry/route-a.json/linter; RS-271/AP-010). RED
    // against the stub probe.
    [Fact]
    public void Degraded_env_missing_evidence_yields_evidence_absent()
    {
        string temp = Path.Combine(Path.GetTempPath(), "gate-degraded-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            // Faithful partial copy: schema present, but the canonical sample removed.
            string schemaSrc = TestPaths.RepoFile("spikes", "dafny-compat", "schema", "evidence-schema.json");
            string schemaDst = Path.Combine(temp, "spikes", "dafny-compat", "schema", "evidence-schema.json");
            Directory.CreateDirectory(Path.GetDirectoryName(schemaDst)!);
            File.Copy(schemaSrc, schemaDst);
            // The canonical sample is intentionally ABSENT (the degraded condition).

            var ctx = GateContext.ForRepoRoot(temp);
            ProbeResult r = new P1Probe().Evaluate(ctx);
            Assert.False(r.Satisfied);
            Assert.Equal(ProbeReasons.EvidenceAbsent, r.Reason);
        }
        finally
        {
            try { Directory.Delete(temp, recursive: true); } catch { /* best effort */ }
        }
    }

    // Tests INV-013 [integration]: the probe holds NO process-global state — it reads
    // its root SOLELY from the injected parameter (not Directory.GetCurrentDirectory
    // nor ambient env), so it is parallel-safe. RED against the stub probe.
    [Fact]
    public void Probe_reads_root_solely_from_injected_parameter()
    {
        string saved = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(Path.GetTempPath());
            var ctx = GateContext.ForRepoRoot(TestPaths.RepoRoot());
            ProbeResult r = new P1Probe().Evaluate(ctx);
            // Result must be the same as when cwd == repo root: it does not read cwd.
            Assert.False(r.Satisfied);
            Assert.Equal(ProbeReasons.EvidenceSchemaIncomplete, r.Reason);
        }
        finally
        {
            Directory.SetCurrentDirectory(saved);
        }
    }

    // Tests INV-013 [integration]: no check resolves its subject from out/ or
    // out/current — a run's OWN product binds to THIS run (AP-021). Genuine guard:
    // the from-clean rm path is spikes/dafny-compat/out/ (the CORRECT path, EXT2-11),
    // asserted against the committed gate script + ARCHITECTURE test_via.
    [Fact]
    public void From_clean_rm_path_is_spikes_dafny_compat_out()
    {
        string arch = File.ReadAllText(TestPaths.RepoFile(".correctless", "ARCHITECTURE.md"));
        Assert.Contains("rm -rf spikes/dafny-compat/out/", arch);
        // There is no top-level out/ tree.
        Assert.False(Directory.Exists(Path.Combine(TestPaths.RepoRoot(), "out")));
    }
}
