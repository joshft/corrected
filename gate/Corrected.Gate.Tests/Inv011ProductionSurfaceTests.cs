using System;
using System.Collections.Generic;
using System.Linq;
using Corrected.Gate;
using Xunit;

namespace Corrected.Gate.Tests;

/// <summary>
/// INV-011 / INV-036 / PRH-008 (QA-002 FULL ENFORCEMENT): deny-by-default
/// production-surface ban over the REAL shipped compilation closure. The scanner is a
/// REAL out-of-process pinned-SDK build (<c>dotnet build -t:Rebuild</c>), not a static
/// XML/*.cs scan: it runs source generators, emits their output, captures the post-build
/// Compile/Analyzer graph + evaluated DefineConstants, diffs against a dynamic
/// SDK-default analyzer baseline, and rejects executable/synthesizing content — including
/// generated sources and live <c>#if</c> branches invisible to a naive glob. The
/// CLOSED-ALLOWLIST Roslyn predicate, vacuous-vs-uncomputable discriminator, injectable
/// allowlist, and build-extension/build-asset presence policy are all exercised.
/// [integration] — several tests really shell out to <c>dotnet build</c>; timings prove it.
/// </summary>
public class Inv011ProductionSurfaceTests
{
    private static readonly string[] NoDefines = Array.Empty<string>();
    private const string ClosureRoot = "gate/Corrected.Gate.Tests/fixtures/shipped-closure";

    private static string[] Target(string fixtureDir) => new[] { $"{ClosureRoot}/{fixtureDir}" };

    // ----- SyntaxAllowlist unit predicate (unchanged; no build) -----

    // Tests INV-011 [integration]: a SKELETON-ONLY declaration (no body, no
    // initializer) PASSES the closed-allowlist predicate.
    [Fact]
    public void Skeleton_only_passes()
    {
        const string src = "namespace N { public sealed class C { public int X { get; } } }";
        Assert.True(SyntaxAllowlist.ContainsOnlyDeclarations(src, NoDefines));
    }

    // Tests INV-011 [integration]: every EXECUTABLE / member-synthesizing C# form
    // fails closed (default-deny by construction, EXT4-01). One case per form.
    [Theory]
    [InlineData("namespace N { public class C { public int M() { return 1; } } }")]          // one real method (BlockSyntax)
    [InlineData("namespace N { public class C { public C() { X = 1; } int X; } }")]           // constructor body
    [InlineData("namespace N { public class C { static C() { } } }")]                         // static-ctor body
    [InlineData("namespace N { public class C { public static implicit operator int(C c) => 1; } }")] // conversion-operator body
    [InlineData("namespace N { public class C { public int P => 1; } }")]                     // expression-bodied property (ArrowExpressionClause)
    [InlineData("namespace N { public class C { public int F = 42; } }")]                     // field initializer (EqualsValueClause)
    [InlineData("System.Console.WriteLine(1);")]                                              // top-level statement (GlobalStatement)
    [InlineData("namespace N { public class C { public static extern int E(); } }")]          // extern (native, no C# body)
    [InlineData("namespace N { public class C(int x) { } }")]                                 // primary constructor
    [InlineData("namespace N { public record R(int X); }")]                                   // positional record synthesis
    public void Executable_or_synthesizing_forms_fail_closed(string source)
    {
        Assert.False(SyntaxAllowlist.ContainsOnlyDeclarations(source, NoDefines));
    }

    // Tests INV-011 [integration]: executable content inside a LIVE #if branch active
    // under the build's DefineConstants is NOT invisible (EXT7-03 parse-differential).
    [Fact]
    public void Executable_inside_live_if_branch_fails_closed()
    {
        const string src = "namespace N { public class C {\n#if SHIP\n public int M() { return 1; }\n#endif\n } }";
        Assert.False(SyntaxAllowlist.ContainsOnlyDeclarations(src, new[] { "SHIP" }));
    }

    // Tests INV-011 [integration]: the allowed declaration-kind set is ENUMERATED
    // (meta-test) so a newly-added executable/synthesizing C# form fails closed by default.
    [Fact]
    public void Allowed_declaration_kind_set_is_enumerated()
    {
        IReadOnlyList<string> kinds = SyntaxAllowlist.AllowedDeclarationKinds;
        Assert.NotEmpty(kinds);
        Assert.DoesNotContain("BlockSyntax", kinds);
        Assert.DoesNotContain("GlobalStatementSyntax", kinds);
    }

    // ----- Scanner: outcome discriminators -----

    // Tests INV-011 [integration]: the real (empty src/) closure resolves to zero
    // project files -> the VACUOUS pass, NEVER conflated with uncomputable, and NO build.
    [Fact]
    public void Empty_closure_is_vacuous_pass()
    {
        ScanResult r = ProductionSurfaceScanner.Scan(ProductionSurfaceScanner.ProductionClosureTargets, Array.Empty<string>());
        Assert.Equal(ScanOutcome.VacuousPass, r.Outcome);
    }

    // Tests INV-011 [integration]: a resolved target that cannot be evaluated is
    // CLOSURE-UNCOMPUTABLE -> fail-closed (a DISTINCT state, never a vacuous pass).
    [Fact]
    public void Uncomputable_closure_fails_closed_distinctly()
    {
        ScanResult r = ProductionSurfaceScanner.Scan(Target("malformed"), Array.Empty<string>());
        Assert.Equal(ScanOutcome.ClosureUncomputable, r.Outcome);
    }

    // Tests INV-011 [integration]: the production closure-target set is the
    // deny-by-default src/Corrected.* set.
    [Fact]
    public void Production_closure_targets_are_the_src_corrected_set()
    {
        Assert.Contains("src/Corrected.Core", ProductionSurfaceScanner.ProductionClosureTargets);
        Assert.Contains("src/Corrected.DafnyAdapter", ProductionSurfaceScanner.ProductionClosureTargets);
        Assert.Contains("src/Corrected.Cli", ProductionSurfaceScanner.ProductionClosureTargets);
    }

    // Tests INV-011 [integration]: the SDK-default Analyzer allowlist is computed
    // DYNAMICALLY from the committed baseline project (EXT8-05/EXT9-05).
    [Fact]
    public void Analyzer_baseline_fixture_present_for_dynamic_default_set()
    {
        Assert.True(TestPaths.RepoFileExists(
            "gate", "Corrected.Gate.Tests", "fixtures", "analyzer-baseline", "analyzer-baseline.csproj"),
            "INV-011: the committed analyzer-baseline fixture project (under <clear/>+locked restore) must exist");
    }

    // ----- Scanner: injectable allowlist over a REAL build -----

    // Tests INV-011 [integration]: the injectable-allowlist allow-branch over a REAL
    // build of a BUILDABLE fixture (a resolvable <ProjectReference> to AllowedRefLib).
    // The correct identity PASSES; a one-char-off identity FAILS (exact identity, not
    // substring). The build must SUCCEED for the Pass branch, proving real resolution.
    [Fact]
    public void Injectable_allowlist_matches_exact_assembly_identity()
    {
        var target = Target("with-allowed-ref");
        Assert.Equal(ScanOutcome.Pass, ProductionSurfaceScanner.Scan(target, new[] { "AllowedRefLib" }).Outcome);
        Assert.Equal(ScanOutcome.Fail, ProductionSurfaceScanner.Scan(target, new[] { "AllowedRefLi" }).Outcome);
    }

    // ----- Scanner: build-extension / build-asset PRESENCE policy (static, pre-build) -----

    // Tests INV-011 [integration]: a committed custom <Target> fails closed on mere
    // presence (its body would run during dotnet build), before any build.
    [Fact]
    public void Committed_build_extension_presence_fails_closed()
    {
        Assert.Equal(ScanOutcome.Fail, ProductionSurfaceScanner.Scan(Target("with-custom-target"), Array.Empty<string>()).Outcome);
    }

    // Tests INV-011 [integration]: a committed packages.lock.json resolving a package
    // that ships build/buildTransitive MSBuild assets fails closed on the presence policy,
    // reporting the BARE package identity (no path).
    [Fact]
    public void Build_asset_package_presence_fails_closed()
    {
        ScanResult r = ProductionSurfaceScanner.Scan(Target("build-asset-package"), Array.Empty<string>());
        Assert.Equal(ScanOutcome.Fail, r.Outcome);
        Assert.Equal("Microsoft.CodeAnalysis.Analyzers", r.OffendingItem);
    }

    // ----- Scanner: REAL-BUILD meta-tests (the QA-002 class_fix) -----

    // META-TEST (QA-002): the scanner REALLY invokes `dotnet build`. The generated-source
    // fixture's own *.cs is skeleton-only; the ONLY executable content exists because a
    // real build runs the referenced source generator and emits it. Reaching Fail here is
    // IMPOSSIBLE without an out-of-process build. The generator identity is allowlisted so
    // the failure is attributable specifically to the emitted GENERATED content.
    [Fact]
    public void Real_dotnet_build_is_invoked_generated_source_executable_member_fails()
    {
        ScanResult r = ProductionSurfaceScanner.Scan(Target("generated-source"), new[] { "GenSourceGenerator" });
        Assert.Equal(ScanOutcome.Fail, r.Outcome);
        // The offending item is the emitted generated file, NOT the (allowlisted) generator.
        Assert.NotEqual("GenSourceGenerator", r.OffendingItem);
        Assert.Contains("Injected", r.OffendingItem);
    }

    // META-TEST (QA-002 / EXT7-03): executable content inside a live #if SHIP branch is
    // caught because the scanner parses with the BUILD's evaluated DefineConstants (which
    // include SHIP). A static no-defines parse would miss it. Requires a real build.
    [Fact]
    public void Live_if_ship_branch_fails_via_build_defineconstants()
    {
        ScanResult r = ProductionSurfaceScanner.Scan(Target("if-ship-differential"), Array.Empty<string>());
        Assert.Equal(ScanOutcome.Fail, r.Outcome);
    }

    // META-TEST (QA-002 / EXT8-05): a non-default Analyzer/source-generator (present in the
    // real-build analyzer graph, absent from the dynamic baseline) that is NOT allowlisted
    // fails closed, reporting the bare analyzer identity. Requires a real build (the
    // ProjectReference-delivered generator only appears post-build).
    [Fact]
    public void Non_default_analyzer_not_in_allowlist_fails_closed()
    {
        ScanResult r = ProductionSurfaceScanner.Scan(Target("generated-source"), Array.Empty<string>());
        Assert.Equal(ScanOutcome.Fail, r.Outcome);
        Assert.Equal("GenSourceGenerator", r.OffendingItem);
    }

    // META-TEST (QA-002 PATH-LEAK): the OffendingItem of a REAL-BUILD Fail contains NO
    // absolute path, NO "/home/", NO username, NO SDK dir — only a bare identity. The
    // `dotnet build -getItem` graph is riddled with absolute paths internally; none leak.
    [Fact]
    public void OffendingItem_for_real_build_fail_has_no_absolute_path()
    {
        ScanResult r = ProductionSurfaceScanner.Scan(Target("generated-source"), new[] { "GenSourceGenerator" });
        Assert.Equal(ScanOutcome.Fail, r.Outcome);
        string item = r.OffendingItem!;
        Assert.DoesNotContain("/", item);
        Assert.DoesNotContain("\\", item);
        Assert.DoesNotContain("/home/", item);
        Assert.DoesNotContain(".dotnet", item);
        Assert.DoesNotContain(Environment.UserName, item);
        Assert.False(System.IO.Path.IsPathRooted(item), $"offending item must not be an absolute path: {item}");
    }

    // META-TEST (QA-002 class_fix): the negative-fixture corpus is reconciled against the
    // spec's ENUMERATED MUST-FAIL vector list — every vector has a committed fixture whose
    // scan yields a fail-closed outcome (Fail, or ClosureUncomputable for the malformed
    // case). Vectors are grouped; several share the generated-source fixture. This is the
    // guard that a vector never silently loses its fixture.
    [Theory]
    // vector, fixtureDir, allowlist(';'-sep or ""), expected outcome
    [InlineData("generated-executable-member", "generated-source", "GenSourceGenerator", ScanOutcome.Fail)]
    [InlineData("live-#if-active-branch", "if-ship-differential", "", ScanOutcome.Fail)]
    [InlineData("non-allowlisted-analyzer", "generated-source", "", ScanOutcome.Fail)]
    [InlineData("build-transitive-package-asset", "build-asset-package", "", ScanOutcome.Fail)]
    [InlineData("custom-target", "with-custom-target", "", ScanOutcome.Fail)]
    [InlineData("using-task", "presence-usingtask", "", ScanOutcome.Fail)]
    [InlineData("non-sdk-import", "presence-nonsdk-import", "", ScanOutcome.Fail)]
    [InlineData("response-file", "presence-response-file", "", ScanOutcome.Fail)]
    [InlineData("non-default-sdk", "presence-nondefault-sdk", "", ScanOutcome.Fail)]
    [InlineData("msbuild-property-function", "presence-property-function", "", ScanOutcome.Fail)]
    [InlineData("non-allowlisted-reference", "with-allowed-ref", "AllowedRefLi", ScanOutcome.Fail)]
    [InlineData("uncomputable-malformed", "malformed", "", ScanOutcome.ClosureUncomputable)]
    public void MustFail_vector_corpus_is_reconciled_to_spec(string vector, string fixtureDir, string allowCsv, ScanOutcome expected)
    {
        var allow = allowCsv.Length == 0
            ? Array.Empty<string>()
            : allowCsv.Split(';', StringSplitOptions.RemoveEmptyEntries);
        ScanResult r = ProductionSurfaceScanner.Scan(Target(fixtureDir), allow);
        Assert.True(expected == r.Outcome,
            $"vector '{vector}' (fixture {fixtureDir}) expected {expected} but scan returned {r.Outcome} (item: {r.OffendingItem})");
    }
}
