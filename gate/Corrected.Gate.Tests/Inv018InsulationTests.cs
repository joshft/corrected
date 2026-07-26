using System;
using System.IO;
using System.Linq;
using Corrected.Gate.Lint;
using Xunit;

namespace Corrected.Gate.Tests;

/// <summary>
/// INV-018: the gate build is insulated from the spike's build health; the P1 linter
/// is EXTRACTED into a Dafny-free single-TFM net10.0 lib (not a cross-tree
/// ProjectReference, not a whole-Components.cs pin). [integration].
/// </summary>
public class Inv018InsulationTests
{
    // Tests INV-018 [integration]: the extracted lib is single-TFM net10.0 and does
    // NOT ProjectReference the spike tree (no cross-tree lock/CPM mixing, no net8
    // pack; RS-UC-09). Genuine guard over the committed csproj.
    [Fact]
    public void Extracted_lib_is_single_tfm_net10_and_spike_free()
    {
        string csproj = File.ReadAllText(TestPaths.RepoFile("gate", "Corrected.Gate.Lint", "Corrected.Gate.Lint.csproj"));
        Assert.Contains("<TargetFramework>net10.0</TargetFramework>", csproj);
        Assert.DoesNotContain("net8.0", csproj);
        Assert.DoesNotContain("spikes/dafny-compat", csproj);
        Assert.DoesNotContain("Dafny", csproj);
    }

    // Tests INV-018 [integration]: the extraction carves AdrLinter + its transitive
    // type closure — NOT the whole Components.cs (which drags ManagedLauncher /
    // Process.Start). Genuine guard: the lib assembly has AdrLinter but no launcher type.
    [Fact]
    public void Extracted_lib_carves_adrlinter_without_launcher()
    {
        var asm = typeof(AdrLinter).Assembly;
        Assert.Equal("Corrected.Gate.Lint", asm.GetName().Name);
        Assert.DoesNotContain(asm.GetTypes(), t => t.Name.Contains("ManagedLauncher", StringComparison.Ordinal));
        Assert.DoesNotContain(asm.GetTypes(), t => t.Name.Contains("Process", StringComparison.Ordinal));
        // The transitive closure IS present:
        Assert.Contains(asm.GetTypes(), t => t.Name == "AdjudicationRecord");
        Assert.Contains(asm.GetTypes(), t => t.Name == "RouteState");
    }

    // Tests INV-018 [integration]: the extracted lib carries an append-only
    // source-digest registry pin (INV-008c). RED at Stage A: the registry file is
    // added at GREEN.
    [Fact]
    public void Extracted_lib_source_digest_registry_present()
    {
        Assert.True(TestPaths.RepoFileExists("gate", "Corrected.Gate", "lint-source-registry.json"),
            "INV-018/INV-008c: gate/Corrected.Gate/lint-source-registry.json (append-only source-digest pin) must exist");
    }

    // Tests INV-018 [integration]: insulation is BUILD-only — the gate retains a DATA
    // dependency on committed spike files (route-a.json + the canonical sample + the
    // extracted-lib digest); relocating/pruning the spike tree fails the gate closed
    // (a STATED coupling). Genuine guard: the data-dependency files are enumerated pins.
    [Fact]
    public void Build_only_insulation_data_dependency_pins_present()
    {
        Assert.True(TestPaths.RepoFileExists("spikes", "dafny-compat", "manifest", "expected-loaded", "route-a.json"));
        Assert.True(TestPaths.RepoFileExists("spikes", "dafny-compat", "evidence", "samples", "run-report.canonical.sample.json"));
    }

    // Tests INV-018 [integration]: the DD-003 ADR status: edit keeps the spike's own
    // committed suite green (tolerated by ExtractLintBlock's unknown-key skipping;
    // RS-280). The extracted AdrLinter over the migrated ADR still lints consistently.
    // RED against the stub linter. (Full spike-suite green is the GREEN milestone.)
    [Fact]
    public void Extracted_linter_tolerates_the_migrated_adr_status_edit()
    {
        string migratedAdr = TestPaths.Fixture("adr", "migrated-adr-lint.md");
        var findings = AdrLinter.Lint(migratedAdr, Array.Empty<AdjudicationRecord>());
        Assert.Empty(findings); // migrated ADR is an all-pass COMPATIBLE -> zero findings
        // The spike's own adjudication test still reads the real ADR:
        Assert.True(TestPaths.RepoFileExists(
            "spikes", "dafny-compat", "tests", "SpikeTests", "Inv013AdjudicationTests.cs"));
    }
}
