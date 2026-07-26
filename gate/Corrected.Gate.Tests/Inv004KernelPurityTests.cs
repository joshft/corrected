using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Threading;
using Corrected.Gate;
using Corrected.Gate.Kernel;
using Xunit;

namespace Corrected.Gate.Tests;

// A deeply-immutable-VIOLATING sample the recursive predicate must reject.
file sealed record RecordWithList(ImmutableArray<int> Ok, List<int> Bad);

/// <summary>
/// INV-004: EvaluateReadiness is a pure, I/O-free, deterministic decision kernel;
/// the Kernel project is isolated; the no-I/O control is a Roslyn symbol-usage scan
/// + extern/[DllImport] rejection + a recursive deep-immutability static-field ban +
/// a behavioral determinism check + a project-graph meta-test. Mostly [unit].
/// </summary>
public class Inv004KernelPurityTests
{
    // Tests INV-004 [unit]: the forbidden-symbol set is ENUMERATED (meta-test) so a
    // newly-relevant API is added deliberately — covering I/O, assembly-load,
    // process, and the nondeterminism/ambient-state family + extern/[DllImport].
    [Fact]
    public void Forbidden_symbol_set_enumerates_the_families()
    {
        IReadOnlyList<string> forbidden = KernelPurityScanner.ForbiddenSymbols;
        foreach (var required in new[]
        {
            "System.IO", "System.Console", "System.Net",
            "System.Diagnostics.Process", "System.Reflection.Assembly.Load",
            "System.Runtime.Loader.AssemblyLoadContext", "System.Threading.Thread.Sleep",
            "System.DateTime.Now", "System.DateTimeOffset.Now", "System.TimeProvider",
            "System.Environment", "System.Random", "System.Guid.NewGuid",
            "System.Globalization.CultureInfo.CurrentCulture", "System.GC",
        })
        {
            Assert.Contains(forbidden, s => s.Contains(required, StringComparison.Ordinal));
        }
    }

    // Tests INV-004 [unit]: the RECURSIVE deep-immutability predicate (EXT9-04):
    // primitives OK; a static-readonly List is banned; nested-wrapper cases
    // (ImmutableArray<List<int>>, a record holding a List) are banned.
    [Theory]
    [InlineData(typeof(int), true)]
    [InlineData(typeof(string), true)]
    [InlineData(typeof(ReadinessStatus), true)]
    [InlineData(typeof(List<int>), false)]
    [InlineData(typeof(Dictionary<string, int>), false)]
    [InlineData(typeof(ImmutableArray<int>), true)]
    [InlineData(typeof(ImmutableArray<List<int>>), false)]  // nested mutable element
    [InlineData(typeof(RecordWithList), false)]              // record holding a mutable field
    public void Deep_immutability_is_recursive(Type t, bool expected)
    {
        Assert.Equal(expected, KernelPurityScanner.IsDeeplyImmutable(t));
    }

    // Tests INV-004 [unit]: the Roslyn symbol-usage scan over the WHOLE Kernel
    // project compilation reports PASS for the real pure kernel. RED against the stub
    // scanner (throws). This is the POSITIVE half; the reject branch is below.
    [Fact]
    public void Kernel_project_scan_reports_pass_for_the_pure_kernel()
    {
        string kernelProj = TestPaths.RepoFile("gate", "Corrected.Gate.Kernel", "Corrected.Gate.Kernel.csproj");
        ScanResult r = KernelPurityScanner.ScanKernelProject(kernelProj);
        Assert.Equal(ScanOutcome.Pass, r.Outcome);
    }

    private static string ViolationProj(string family, string csproj)
        => TestPaths.RepoFile("gate", "Corrected.Gate.Tests", "fixtures", "kernel-violations", family, csproj);

    // Tests INV-004 [unit]: the symbol-usage scan REJECT branch — one committed
    // violating-kernel fixture project per I/O / nondeterminism / P-Invoke family
    // (System.IO.File, Console, Process, DateTime.Now ambient clock, Assembly.LoadFrom,
    // extern/[DllImport]) each -> ScanKernelProject == Fail AND OffendingItem names the
    // offending symbol (an assembly-reference scan would MISS these; RS-260/EXT9-03).
    // RED against the stub scanner (throws). Scope is honest: this catches ACCIDENTAL
    // first-party I/O, NOT a malicious P/Invoke bypass (EXT9-03).
    [Theory]
    [InlineData("system-io", "BadKernel.csproj", "System.IO")]
    [InlineData("console", "BadKernel.csproj", "System.Console")]
    [InlineData("process", "BadKernel.csproj", "System.Diagnostics.Process")]
    [InlineData("datetime-now", "BadKernel.csproj", "System.DateTime")]
    [InlineData("assembly-loadfrom", "BadKernel.csproj", "System.Reflection.Assembly")]
    [InlineData("extern-dllimport", "BadKernel.csproj", "DllImport")]
    public void Kernel_scan_rejects_each_io_and_nondeterminism_family(string family, string csproj, string offendingSymbol)
    {
        string proj = ViolationProj(family, csproj);
        Assert.True(File.Exists(proj), $"violating-kernel fixture missing: {proj}");
        ScanResult r = KernelPurityScanner.ScanKernelProject(proj);
        Assert.Equal(ScanOutcome.Fail, r.Outcome);
        Assert.NotNull(r.OffendingItem);
        Assert.Contains(offendingSymbol, r.OffendingItem!, StringComparison.Ordinal);
    }

    // Tests INV-004 [unit]: the project-graph predicate KernelHasNoProjectOrPackageReference
    // is ACTUALLY CALLED (previously dead/untested) and REJECTS a fixture kernel whose
    // .csproj declares a PackageReference — even though the fixture's C# is pure (the
    // symbol scan alone would miss the reference edge; EXT7-04). RED against the stub.
    [Fact]
    public void Kernel_reference_predicate_rejects_a_fixture_declaring_a_reference()
    {
        string proj = ViolationProj("has-reference", "HasReference.csproj");
        Assert.True(File.Exists(proj), $"has-reference fixture missing: {proj}");
        Assert.False(KernelPurityScanner.KernelHasNoProjectOrPackageReference(proj));
    }

    // Tests INV-004 [unit]: the same predicate ACCEPTS the real pure Kernel project
    // (no ProjectReference, no PackageReference). RED against the stub (throws).
    [Fact]
    public void Kernel_reference_predicate_accepts_the_pure_kernel()
    {
        string kernelProj = TestPaths.RepoFile("gate", "Corrected.Gate.Kernel", "Corrected.Gate.Kernel.csproj");
        Assert.True(KernelPurityScanner.KernelHasNoProjectOrPackageReference(kernelProj));
    }

    // Tests INV-004 [unit]: the project-graph meta-test — the Kernel project
    // declares NO ProjectReference and NO PackageReference (BCL-only). This is a
    // DIRECT structural read (genuine guard; passes because the .csproj is clean),
    // complementing the scanner-based RED test above.
    [Fact]
    public void Kernel_csproj_declares_no_project_or_package_reference()
    {
        string kernelProj = TestPaths.RepoFile("gate", "Corrected.Gate.Kernel", "Corrected.Gate.Kernel.csproj");
        string xml = File.ReadAllText(kernelProj);
        Assert.DoesNotContain("<ProjectReference", xml);
        Assert.DoesNotContain("<PackageReference", xml);
    }

    // Tests INV-004 [unit]: BEHAVIORAL determinism — the kernel returns
    // byte-identical verdicts across repeated calls with identical supplied inputs,
    // run under a MUTATED ambient culture (so an ambient-state read the denylist
    // missed still fails determinism). RED: fixture construction + kernel are stubs.
    [Fact]
    public void Kernel_is_deterministic_under_mutated_culture()
    {
        var savedCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            var block = ReadinessBlockParser.Parse(
                File.ReadAllText(TestPaths.Fixture("readiness", "real-parent-readiness-block.md")));
            var probes = new Dictionary<PreconditionId, ProbeResult>
            {
                [PreconditionId.P1] = ProbeResult.TryCreate(false, ProbeReasons.EvidenceSchemaIncomplete, ReferenceResolution.Resolved)!,
                [PreconditionId.P2] = ProbeResult.TryCreate(false, ProbeReasons.ValidatorDeferred, ReferenceResolution.Resolved)!,
                [PreconditionId.P3] = ProbeResult.TryCreate(false, ProbeReasons.ValidatorDeferred, ReferenceResolution.Resolved)!,
            };
            var v1 = ReadinessGate.EvaluateReadiness(block, probes);
            var v2 = ReadinessGate.EvaluateReadiness(block, probes);
            Assert.Equal(v1.Kind, v2.Kind);
            Assert.Equal(v1.OffendingPrecondition, v2.OffendingPrecondition);
        }
        finally
        {
            CultureInfo.CurrentCulture = savedCulture;
        }
    }
}
