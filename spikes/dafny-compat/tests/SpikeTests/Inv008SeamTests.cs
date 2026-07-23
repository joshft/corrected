// Tests INV-008: the Dafny seam is a separate assembly, structurally enforced —
// with a Dafny-free contracts assembly.
using System.Reflection;
using System.Text.Json;
using System.Xml.Linq;
using Corrected.Spike.Contracts;
using Xunit;

namespace Corrected.Spike.Tests;

public class Inv008SeamTests
{
    // Tests INV-008 [unit]: project-graph — no project outside the two
    // route-seam projects has a Dafny/Boogie PackageReference.
    [Fact]
    public void OnlyRouteSeamProjects_OwnDafnyBoogiePackageReferences()
    {
        foreach (var (name, rel) in SpikePaths.Projects)
        {
            var doc = SpikePaths.Xml(SpikePaths.CsprojPath(name));
            var familyRefs = doc.Descendants("PackageReference")
                .Where(pr =>
                {
                    var id = pr.Attribute("Include")?.Value ?? "";
                    return id.StartsWith("Dafny", StringComparison.Ordinal) || id.StartsWith("Boogie.", StringComparison.Ordinal);
                })
                .ToList();
            if (SpikePaths.SeamProjectNames.Contains(name))
            {
                Assert.NotEmpty(familyRefs);
            }
            else
            {
                Assert.True(familyRefs.Count == 0,
                    $"{rel} has a Dafny/Boogie PackageReference — only the route-seam projects may own the packages (INV-008)");
            }
        }
    }

    // Tests INV-008 [unit] (RS-019a, codex F2): the documented compile-only
    // privacy pattern is present on every seam family reference (runtime assets
    // still flow; PrivateAssets="all" would strip them).
    [Fact]
    public void SeamFamilyReferences_CarryCompileOnlyPrivacyMetadata()
    {
        foreach (var seam in SpikePaths.SeamProjectNames)
        {
            var doc = SpikePaths.Xml(SpikePaths.CsprojPath(seam));
            var familyRefs = doc.Descendants("PackageReference")
                .Where(pr => (pr.Attribute("Include")?.Value ?? "").StartsWith("Dafny") ||
                             (pr.Attribute("Include")?.Value ?? "").StartsWith("Boogie."))
                .ToList();
            Assert.NotEmpty(familyRefs);
            foreach (var pr in familyRefs)
            {
                var privateAssets = pr.Attribute("PrivateAssets")?.Value ?? pr.Element("PrivateAssets")?.Value;
                Assert.Equal("compile", privateAssets);
            }
        }
    }

    // Tests INV-008/RS-019c [unit]: the test project references ONLY the
    // contracts assembly — "tests never load Dafny" is project-graph-enforced.
    [Fact]
    public void TestProject_ReferencesOnlyTheContractsAssembly()
    {
        var doc = SpikePaths.Xml(SpikePaths.CsprojPath("SpikeTests"));
        var projectRefs = doc.Descendants("ProjectReference").ToList();
        var only = Assert.Single(projectRefs);
        Assert.EndsWith("SpikeContracts.csproj", only.Attribute("Include")!.Value.Replace('\\', '/'));
    }

    // Tests INV-008 [unit]: the test host's resolved closure contains no
    // Dafny*/Boogie* assemblies — checked against the REAL restored lock and the
    // live AppDomain.
    // Source: spikes/dafny-compat/tests/SpikeTests/packages.lock.json
    [Fact]
    public void TestHostClosure_ContainsNoDafnyOrBoogieAssemblies()
    {
        using var doc = SpikePaths.Json(SpikePaths.LockFilePath("SpikeTests"));
        foreach (var fw in doc.RootElement.GetProperty("dependencies").EnumerateObject())
        {
            foreach (var dep in fw.Value.EnumerateObject())
            {
                Assert.False(dep.Name.StartsWith("Dafny", StringComparison.Ordinal)
                          || dep.Name.StartsWith("Boogie.", StringComparison.Ordinal),
                    $"test project resolved closure contains {dep.Name} (INV-008)");
            }
        }

        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            var n = asm.GetName().Name ?? "";
            Assert.False(n.StartsWith("Dafny", StringComparison.Ordinal) || n.StartsWith("Boogie", StringComparison.Ordinal),
                $"test host loaded {n} — the Dafny-free-closure claim is violated");
        }
    }

    // Tests INV-008 [integration] (codex R4-10): the TWO-PHASE negative compile
    // test — an otherwise-identical POSITIVE consumer must first build
    // successfully; then each forbidden variant must fail with the expected
    // compiler diagnostic AT the planted token and no unrelated errors.
    // Any-failure oracles prove nothing.
    [Theory]
    [InlineData("ForbiddenTypeNaming.cs.txt", "forbidden-type")]
    [InlineData("ForbiddenPropertyAccess.cs.txt", "forbidden-property")]
    [InlineData("ForbiddenInterfaceImpl.cs.txt", "forbidden-interface")]
    [InlineData("ForbiddenGenericConstraint.cs.txt", "forbidden-generic-constraint")]
    public void TwoPhaseNegativeCompile_PositiveBuilds_ThenForbiddenFailsAtPlantedTokenOnly(string forbiddenFixture, string plantedMarker)
    {
        var scratch = SpikePaths.TestScratch($"inv008-negcompile-{plantedMarker}");

        // Phase 1: positive control must BUILD.
        var positive = Launch.Script("scripts/run-spike.sh", null, "--phase", "negative-compile",
            "--consumer-fixture", "tests/SpikeTests/fixtures/negative-compile/PositiveConsumer.cs.txt",
            "--scratch", scratch);
        Assert.True(positive.ExitCode == 0,
            $"positive consumer failed to build — the negative oracle is meaningless (codex R4-10): {positive.StdErr}");

        // Phase 2: forbidden variant must FAIL. TA-B3c/TA-A3: the diagnostic is
        // judged BY THE TEST from the real compiler output — it must cite the
        // fixture file name AND the planted line, and be the SOLE error (no
        // unrelated-failure oracle can satisfy this).
        var fixturePath = SpikePaths.P("tests", "SpikeTests", "fixtures", "negative-compile", forbiddenFixture);
        var plantedLine = File.ReadAllLines(fixturePath)
            .Select((text, idx) => (Text: text, Line: idx + 1))
            .Single(t => t.Text.Contains($"/*PLANTED:{plantedMarker}*/")).Line;
        var compiledName = Path.GetFileNameWithoutExtension(forbiddenFixture); // "X.cs.txt" -> "X.cs"

        var negative = Launch.Script("scripts/run-spike.sh", null, "--phase", "negative-compile",
            "--consumer-fixture", $"tests/SpikeTests/fixtures/negative-compile/{forbiddenFixture}",
            "--scratch", scratch);
        Assert.NotEqual(0, negative.ExitCode);
        var output = negative.StdOut + negative.StdErr;

        var errorLines = output.Split('\n').Where(l => System.Text.RegularExpressions.Regex.IsMatch(l, @"\berror CS\d{4}\b")).ToList();
        Assert.NotEmpty(errorLines);
        var distinctErrors = errorLines
            .Select(l => System.Text.RegularExpressions.Regex.Match(l, System.Text.RegularExpressions.Regex.Escape(compiledName) + @"\((\d+)[,)]"))
            .ToList();
        // Every error cites the fixture file at the planted line — no unrelated errors.
        foreach (var (line, match) in errorLines.Zip(distinctErrors))
        {
            Assert.True(match.Success, $"compiler error does not cite {compiledName}: {line.Trim()}");
            Assert.Equal(plantedLine, int.Parse(match.Groups[1].Value));
        }
        Assert.Matches("error (CS0246|CS0234|CS1069)", output); // the expected missing-type diagnostic class
    }

    // Tests INV-008/RS-019b [integration] (codex R2-8, TA-B14): metadata-only
    // API-surface test — MetadataLoadContext (never runtime Assembly.Load in
    // the test host) recursively inspecting every externally visible signature.
    // Assemblies under scan come from ATTESTED locations only: SpikeContracts
    // from the test host's own fresh copy (AppContext.BaseDirectory) and the
    // seam dlls from the run-context harness artifact directories — never from
    // repo-local bin/Debug (the stale-artifact class DD-008/TA-B13 eliminates).
    [Fact]
    public void MetadataOnlySurfaceScan_NoDafnyBoogieOriginsInPublicApi()
    {
        var contractsDll = Path.Combine(AppContext.BaseDirectory, "SpikeContracts.dll");
        Assert.True(File.Exists(contractsDll), $"contracts assembly missing beside the test host: {contractsDll}");

        var routeADir = Path.GetDirectoryName(RunContext.Resolve("RouteAHarness").AbsolutePath)!;
        var routeBDir = Path.GetDirectoryName(RunContext.Resolve("RouteBHarness").AbsolutePath)!;
        var scanTargets = new[]
        {
            contractsDll,
            Path.Combine(routeADir, "SpikeDafnyAdapter.RouteA.dll"),
            Path.Combine(routeBDir, "SpikeDafnyAdapter.RouteB.dll"),
        };
        foreach (var dll in scanTargets)
        {
            Assert.True(File.Exists(dll), $"assembly under scan missing: {dll} (the controller-built artifact set is incomplete — never skip)");
        }

        // TEST_BUG fix #2: de-duplicate resolver paths by ASSEMBLY FILE NAME,
        // with the three explicit scan targets winning, so PathAssemblyResolver
        // can never substitute an unattested copy for a directly-scanned assembly.
        var runtimeDir = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory();
        var resolverByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dll in scanTargets)
        {
            resolverByName[Path.GetFileName(dll)] = dll; // explicit targets win the dedup
        }
        foreach (var dll in Directory.GetFiles(runtimeDir, "*.dll")
                     .Concat(Directory.GetFiles(routeADir, "*.dll"))
                     .Concat(Directory.GetFiles(routeBDir, "*.dll")))
        {
            resolverByName.TryAdd(Path.GetFileName(dll), dll);
        }
        var resolver = new PathAssemblyResolver(resolverByName.Values.ToArray());
        using var mlc = new MetadataLoadContext(resolver);

        foreach (var dll in scanTargets)
        {
            var asm = mlc.LoadFromAssemblyPath(dll);
            foreach (var type in asm.GetExportedTypes())
            {
                AssertSurfaceClean(type, new HashSet<string>(), dll);
            }
        }
    }

    private static void AssertSurfaceClean(Type type, HashSet<string> seen, string origin)
    {
        if (!seen.Add(type.AssemblyQualifiedName ?? type.FullName ?? type.Name))
        {
            return;
        }

        void Check(Type? t, string where)
        {
            if (t is null) { return; }
            var asmName = t.Assembly.GetName().Name ?? "";
            Assert.False(asmName.StartsWith("Dafny", StringComparison.Ordinal) || asmName.StartsWith("Boogie", StringComparison.Ordinal),
                $"{origin}: public surface leaks {t.FullName} from {asmName} at {where} (INV-008/RS-019b)");
            if (t.IsGenericType)
            {
                foreach (var arg in t.GetGenericArguments()) { Check(arg, where + "<generic-arg>"); }
            }
            if (t.HasElementType) { Check(t.GetElementType(), where + "<element>"); }
        }

        const BindingFlags visible = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.FlattenHierarchy;
        Check(type.BaseType, $"{type.FullName} base type");
        foreach (var iface in type.GetInterfaces()) { Check(iface, $"{type.FullName} interface"); }
        foreach (var m in type.GetMethods(visible))
        {
            Check(m.ReturnType, $"{type.FullName}.{m.Name} return");
            foreach (var p in m.GetParameters()) { Check(p.ParameterType, $"{type.FullName}.{m.Name}({p.Name})"); }
            if (m.IsGenericMethodDefinition)
            {
                foreach (var g in m.GetGenericArguments())
                {
                    foreach (var c in g.GetGenericParameterConstraints()) { Check(c, $"{type.FullName}.{m.Name} constraint"); }
                }
            }
        }
        foreach (var p in type.GetProperties(visible)) { Check(p.PropertyType, $"{type.FullName}.{p.Name}"); }
        foreach (var f in type.GetFields(visible)) { Check(f.FieldType, $"{type.FullName}.{f.Name}"); }
        foreach (var e in type.GetEvents(visible)) { Check(e.EventHandlerType, $"{type.FullName}.{e.Name}"); }
        foreach (var nested in type.GetNestedTypes(BindingFlags.Public)) { AssertSurfaceClean(nested, seen, origin); }
    }

    // Tests INV-008 [unit] (codex R3-11): source grep backstop — no
    // Microsoft.Dafny/Microsoft.Boogie references outside the seam projects; the
    // hash-bound negative-compile fixtures are the SOLE whitelisted exception.
    [Fact]
    public void SourceGrep_NoDafnyBoogieNamespacesOutsideSeams_HashBoundWhitelist()
    {
        // First: the whitelist is hash-bound — fixture digests must match the constants.
        foreach (var (file, expectedSha) in SpecConstants.NegativeCompileFixtureSha256)
        {
            var path = SpikePaths.P("tests", "SpikeTests", "fixtures", "negative-compile", file);
            Assert.True(File.Exists(path), $"whitelisted fixture missing: {file}");
            Assert.Equal(expectedSha, SpikePaths.Sha256File(path));
        }

        // TEST_BUG fix #3: needles built by concatenation so this scanner never
        // matches its own source, and the scanner file itself is excluded (its
        // comments legitimately name the namespaces). Everything else stays scanned.
        var dafnyNeedle = "Microsoft." + "Dafny";
        var boogieNeedle = "Microsoft." + "Boogie";
        var seamDirs = new[]
        {
            Path.Combine("adapters", "SpikeDafnyAdapter.RouteA"),
            Path.Combine("adapters", "SpikeDafnyAdapter.RouteB"),
        };
        foreach (var file in SpikePaths.AllSourceFiles())
        {
            var rel = Path.GetRelativePath(SpikePaths.SpikeRoot, file);
            if (seamDirs.Any(d => rel.StartsWith(d, StringComparison.Ordinal)))
            {
                continue; // seam projects legitimately reference the Dafny namespaces in GREEN
            }
            if (Path.GetFileName(file) == "Inv008SeamTests.cs")
            {
                continue; // the scanning test file itself
            }
            var text = File.ReadAllText(file);
            Assert.False(text.Contains(dafnyNeedle) || text.Contains(boogieNeedle),
                $"{rel} references Dafny/Boogie namespaces outside the seam (INV-008; whitelist is only the hash-bound .cs.txt fixtures)");
        }
    }

    // Tests INV-008 [unit] (codex R3-11): the negative-compile fixtures are
    // excluded from every shipping/build project.
    [Fact]
    public void NegativeCompileFixtures_ExcludedFromEveryBuildProject()
    {
        // .cs.txt files are not compiled by the SDK globs; additionally no csproj
        // may pull them in via Compile Include.
        foreach (var csproj in SpikePaths.AllCsprojFiles())
        {
            var text = File.ReadAllText(csproj);
            Assert.DoesNotContain("negative-compile", text);
        }
    }
}
