using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Collections.Immutable;
using System.Collections.Frozen;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Corrected.Gate;

/// <summary>
/// The four distinct closure-scan states (INV-011 / R3-I3). VacuousPass (zero
/// project files -> "no production surface") and ClosureUncomputable (a resolved
/// target whose restore/build/-getItem returns nonzero/unparseable -> fail-closed)
/// are NEVER conflated.
/// </summary>
public enum ScanOutcome
{
    Pass,
    Fail,
    VacuousPass,
    ClosureUncomputable,
}

/// <summary>Result of a shipped-closure / kernel scan (INV-011/004).</summary>
public sealed class ScanResult
{
    private ScanResult(ScanOutcome outcome, string? offendingItem)
    {
        Outcome = outcome;
        OffendingItem = offendingItem;
    }

    public ScanOutcome Outcome { get; }
    public string? OffendingItem { get; }

    internal static ScanResult Create(ScanOutcome outcome, string? offendingItem) => new(outcome, offendingItem);
}

/// <summary>
/// The deny-by-default production-surface ban over the shipped compilation closure
/// (INV-011/036, parent PRH-008). Injectable closure-target set + injectable
/// allowlist (production binds src/Corrected.* + the empty allowlist while BLOCKED;
/// tests bind fixture paths).
///
/// QA-002 FULL ENFORCEMENT: the ban is a REAL out-of-process pinned-SDK build, not a
/// static XML/*.cs scan. Each resolved closure csproj is restored + built out of
/// process (<see cref="ClosureBuildRunner"/>, `dotnet build -t:Rebuild`), so source
/// generators actually run, EmitCompilerGeneratedFiles emits their output, and the
/// post-build Compile/Analyzer item graph + evaluated DefineConstants are captured.
/// A naive csproj-XML + *.cs glob is blind to (a) generated sources carrying
/// executable members and (b) executable code inside a live `#if` branch active under
/// the build's real DefineConstants — the two evasion vectors this control exists to
/// catch. Only a PRESENCE policy (custom build extensions, build-asset packages) stays
/// a static inspection — correct, since presence, not behaviour, is the property.
///
/// Distinct outcomes are NEVER conflated: zero csprojs -> VacuousPass (no build, fast);
/// a restore/build/-getItem that returns nonzero/unparseable -> ClosureUncomputable
/// (fail-closed); a resolved executable/synthesizing form -> Fail.
///
/// PATH-LEAK DISCIPLINE (QA-002): every offending item surfaced to an operator is a
/// bare token/identity/filename — the `-getItem` FullPaths that name absolute paths
/// (home dir, username, SDK analyzer dir) are used only internally and scrubbed via
/// <see cref="ClosureBuildRunner.SafeIdentity"/> before ever reaching ScanResult.
/// </summary>
public static class ProductionSurfaceScanner
{
    /// <summary>The production closure-target set (src/Corrected.*), deny-by-default.</summary>
    public static readonly IReadOnlyList<string> ProductionClosureTargets = new[]
    {
        "src/Corrected.Core",
        "src/Corrected.DafnyAdapter",
        "src/Corrected.Cli",
    };

    // The committed baseline project whose Analyzer set IS the SDK-default set, computed
    // dynamically (EXT8-05/EXT9-05) so a vanilla skeleton never false-fails.
    private static readonly string[] AnalyzerBaselineRel =
    {
        "gate", "Corrected.Gate.Tests", "fixtures", "analyzer-baseline", "analyzer-baseline.csproj",
    };

    public static ScanResult Scan(IReadOnlyList<string> injectedTargetSet, IReadOnlyList<string> injectedAllowlist)
    {
        string root = RepoRootLocator.Locate();
        var allow = new HashSet<string>(injectedAllowlist, StringComparer.Ordinal);

        var csprojs = new List<string>();
        foreach (var target in injectedTargetSet)
        {
            string dir = Path.Combine(root, Path.Combine(target.Split('/')));
            if (!Directory.Exists(dir))
            {
                continue;
            }
            foreach (var f in Directory.EnumerateFiles(dir, "*.csproj", SearchOption.AllDirectories))
            {
                if (!IsUnderObjOrBin(f))
                {
                    csprojs.Add(f);
                }
            }
        }

        // Vacuous: zero project files -> "no production surface (src/ empty)" pass. NO build,
        // NO baseline construction (real builds are slow; the empty closure stays fast).
        if (csprojs.Count == 0)
        {
            return ScanResult.Create(ScanOutcome.VacuousPass, null);
        }

        // ---- PRESENCE PHASE (static, pre-build): a committed build extension / build-asset
        // package FAILS on mere presence, before we ever execute its behaviour. A malformed
        // project cannot be evaluated at all -> ClosureUncomputable. ----
        foreach (var csproj in csprojs)
        {
            string text = File.ReadAllText(csproj);
            XDocument doc;
            try
            {
                doc = XDocument.Parse(text);
            }
            catch (System.Xml.XmlException)
            {
                return ScanResult.Create(ScanOutcome.ClosureUncomputable, ClosureBuildRunner.SafeIdentity(csproj));
            }

            string projDir = Path.GetDirectoryName(csproj)!;

            string? offendingExt = FindForbiddenBuildExtension(doc, text, projDir);
            if (offendingExt is not null)
            {
                return ScanResult.Create(ScanOutcome.Fail, offendingExt);
            }

            // Committed packages.lock.json contributing build/buildTransitive/buildMultiTargeting
            // MSBuild assets (a package that injects build logic like a custom <Target>).
            string? buildAssetPkg = FindBuildAssetPackage(projDir, allow);
            if (buildAssetPkg is not null)
            {
                return ScanResult.Create(ScanOutcome.Fail, buildAssetPkg);
            }
        }

        // ---- REAL BUILD PHASE: the SDK-default Analyzer baseline is computed once (dynamic,
        // same pinned SDK) then each closure is really built and its post-build graph analyzed. ----
        string baselineCsproj = Path.Combine(root, Path.Combine(AnalyzerBaselineRel));
        ClosureBuildRunner.ClosureProbe? baseline = ClosureBuildRunner.TryProbe(baselineCsproj);
        if (baseline is null)
        {
            // Cannot establish the default-analyzer set -> the closure is uncomputable, not passing.
            return ScanResult.Create(ScanOutcome.ClosureUncomputable, "analyzer-baseline");
        }
        var baselineAnalyzers = new HashSet<string>(baseline.AnalyzerFilenames, StringComparer.OrdinalIgnoreCase);

        foreach (var csproj in csprojs)
        {
            XDocument doc = XDocument.Parse(File.ReadAllText(csproj));

            ClosureBuildRunner.ClosureProbe? probe = ClosureBuildRunner.TryProbe(csproj);
            if (probe is null)
            {
                // restore/build/-getItem nonzero or unparseable -> fail-closed, DISTINCT state.
                return ScanResult.Create(ScanOutcome.ClosureUncomputable, ClosureBuildRunner.SafeIdentity(csproj));
            }

            // The default-analyzer set is only meaningful if both builds ran on the SAME SDK.
            if (!string.Equals(probe.SdkVersion, baseline.SdkVersion, StringComparison.Ordinal))
            {
                return ScanResult.Create(ScanOutcome.ClosureUncomputable, "sdk-version-mismatch");
            }

            // (4) ANALYZER BASELINE DIFF: any NON-default analyzer/source-generator (closure set
            // MINUS the dynamic baseline, by bare assembly filename) must be injected-allowlisted.
            foreach (var analyzer in probe.AnalyzerFilenames)
            {
                if (!baselineAnalyzers.Contains(analyzer) && !allow.Contains(analyzer))
                {
                    return ScanResult.Create(ScanOutcome.Fail, ClosureBuildRunner.SafeIdentity(analyzer));
                }
            }

            // (6) REFERENCE identity allowlist: every non-framework reference (exact identity —
            // ProjectReference filename / PackageReference id / <Reference Include>) must be
            // allowlisted. The real build having SUCCEEDED proves the reference actually resolves.
            foreach (var refId in NonFrameworkReferences(doc))
            {
                if (!allow.Contains(refId))
                {
                    return ScanResult.Create(ScanOutcome.Fail, ClosureBuildRunner.SafeIdentity(refId));
                }
            }

            // (5) ANALYSIS: parse the post-build hand-written Compile sources AND the emitted
            // generated *.cs under the build's ACTUAL DefineConstants. Any executable/synthesizing
            // form fails closed. Generated content + live #if branches are the whole point.
            IReadOnlyList<string> defines = probe.DefineConstants;
            foreach (var src in probe.CompileSourceFiles)
            {
                if (!SyntaxAllowlist.ContainsOnlyDeclarations(File.ReadAllText(src), defines))
                {
                    return ScanResult.Create(ScanOutcome.Fail, ClosureBuildRunner.SafeIdentity(src));
                }
            }
            foreach (var gen in probe.GeneratedSourceFiles)
            {
                if (!SyntaxAllowlist.ContainsOnlyDeclarations(File.ReadAllText(gen), defines))
                {
                    return ScanResult.Create(ScanOutcome.Fail, ClosureBuildRunner.SafeIdentity(gen));
                }
            }
        }

        return ScanResult.Create(ScanOutcome.Pass, null);
    }

    private static bool IsUnderObjOrBin(string path)
    {
        string norm = path.Replace('\\', '/');
        return norm.Contains("/obj/") || norm.Contains("/bin/");
    }

    private static string? FindForbiddenBuildExtension(XDocument doc, string text, string projDir)
    {
        // Non-default <Sdk>/Sdk= identity (only Microsoft.NET.Sdk[.Web|.Razor] permitted).
        foreach (var el in doc.Descendants())
        {
            if (el.Name.LocalName is "Project" or "Sdk" or "Import")
            {
                var sdkAttr = el.Attribute("Sdk")?.Value;
                if (sdkAttr is not null && !IsAllowedSdk(sdkAttr))
                {
                    // Scrub the value to a bare identity — a non-default <Sdk> could be an
                    // absolute path; keep the presence-phase offending item path-free too
                    // (QA mini-audit C-F3), consistent with the build-phase SafeIdentity.
                    return "non-default-sdk:" + ClosureBuildRunner.SafeIdentity(sdkAttr);
                }
            }
        }

        foreach (var el in doc.Descendants())
        {
            switch (el.Name.LocalName)
            {
                case "Target":
                case "UsingTask":
                case "Exec":
                case "PreBuildEvent":
                case "PostBuildEvent":
                    return "build-extension:" + el.Name.LocalName;
                case "Import":
                    // A non-SDK explicit <Import Project="..."> is forbidden.
                    if (el.Attribute("Project") is not null)
                    {
                        return "non-sdk-import";
                    }
                    break;
            }
        }

        if (Regex.IsMatch(text, @"BeforeBuild|AfterBuild", RegexOptions.IgnoreCase))
        {
            return "build-event";
        }

        // MSBuild property function anywhere: $([Type]::Member(...)).
        if (Regex.IsMatch(text, @"\$\(\["))
        {
            return "msbuild-property-function";
        }

        // A committed response file in the closure dir.
        foreach (var rsp in new[] { "Directory.Build.rsp", "MSBuild.rsp" })
        {
            if (File.Exists(Path.Combine(projDir, rsp)))
            {
                return "response-file:" + rsp;
            }
        }

        return null;
    }

    private static bool IsAllowedSdk(string sdk)
    {
        string id = sdk.Split('/')[0].Trim();
        return id is "Microsoft.NET.Sdk" or "Microsoft.NET.Sdk.Web" or "Microsoft.NET.Sdk.Razor";
    }

    /// <summary>
    /// Every non-framework reference IDENTITY declared by the project: a ProjectReference's
    /// bare project filename (its output assembly identity), a PackageReference id, or a raw
    /// &lt;Reference Include&gt;. Identity, never substring — a one-char-off allowlist entry fails.
    /// </summary>
    private static IEnumerable<string> NonFrameworkReferences(XDocument doc)
    {
        foreach (var el in doc.Descendants())
        {
            string? include = el.Attribute("Include")?.Value;
            if (string.IsNullOrEmpty(include))
            {
                continue;
            }
            switch (el.Name.LocalName)
            {
                case "Reference":
                case "PackageReference":
                    yield return include!;
                    break;
                case "ProjectReference":
                    // Identity = the referenced project's output-assembly name (filename sans ext).
                    yield return Path.GetFileNameWithoutExtension(include!.Replace('\\', '/'));
                    break;
            }
        }
    }

    /// <summary>
    /// Inspect the committed packages.lock.json for any resolved package that contributes
    /// build / buildTransitive / buildMultiTargeting MSBuild assets (a package that injects
    /// build-time logic — a lock-delivered equivalent of a custom &lt;Target&gt;). Returns the
    /// bare package identity to fail on, or null. Allowlisted identities are exempt. Detection
    /// is by inspecting the restored package in the NuGet global-packages cache; if a package
    /// is not yet cached, a one-shot restore of the project populates it first.
    /// </summary>
    private static string? FindBuildAssetPackage(string projDir, HashSet<string> allow)
    {
        string lockPath = Path.Combine(projDir, "packages.lock.json");
        if (!File.Exists(lockPath))
        {
            return null;
        }

        List<(string Id, string Version)> packages;
        try
        {
            packages = ReadLockPackages(lockPath);
        }
        catch (System.Text.Json.JsonException)
        {
            return null; // an unreadable lock is not a build-asset positive; the build phase judges it.
        }
        if (packages.Count == 0)
        {
            return null;
        }

        string nugetRoot = ClosureBuildRunner.NuGetGlobalPackages();
        bool restored = false;
        foreach (var (id, version) in packages)
        {
            if (allow.Contains(id))
            {
                continue;
            }
            string pkgDir = Path.Combine(nugetRoot, id.ToLowerInvariant(), version);
            if (!Directory.Exists(pkgDir) && !restored)
            {
                // Cold cache: populate it once from the committed lock, then re-check.
                restored = ClosureBuildRunner.TryRestore(Path.Combine(projDir,
                    Directory.EnumerateFiles(projDir, "*.csproj").Select(Path.GetFileName).First()!));
            }
            if (HasMsBuildBuildAssets(pkgDir))
            {
                return id; // bare identity — never a path.
            }
        }
        return null;
    }

    private static List<(string Id, string Version)> ReadLockPackages(string lockPath)
    {
        var result = new List<(string, string)>();
        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(lockPath));
        if (!doc.RootElement.TryGetProperty("dependencies", out var deps)
            || deps.ValueKind != System.Text.Json.JsonValueKind.Object)
        {
            return result;
        }
        foreach (var tfm in deps.EnumerateObject())
        {
            if (tfm.Value.ValueKind != System.Text.Json.JsonValueKind.Object)
            {
                continue;
            }
            foreach (var pkg in tfm.Value.EnumerateObject())
            {
                string id = pkg.Name;
                string version = pkg.Value.TryGetProperty("resolved", out var rv) ? (rv.GetString() ?? "") : "";
                if (version.Length > 0)
                {
                    result.Add((id, version));
                }
            }
        }
        return result;
    }

    private static bool HasMsBuildBuildAssets(string packageVersionDir)
    {
        if (!Directory.Exists(packageVersionDir))
        {
            return false;
        }
        foreach (var sub in new[] { "build", "buildTransitive", "buildMultiTargeting" })
        {
            string dir = Path.Combine(packageVersionDir, sub);
            if (Directory.Exists(dir)
                && (Directory.EnumerateFiles(dir, "*.props", SearchOption.AllDirectories).Any()
                    || Directory.EnumerateFiles(dir, "*.targets", SearchOption.AllDirectories).Any()))
            {
                return true;
            }
        }
        return false;
    }
}

/// <summary>
/// The CLOSED-ALLOWLIST Roslyn predicate (INV-011 / EXT4-01, default-deny by
/// construction). The ONLY permitted member declarations are the namespace/type/member
/// kinds in <see cref="AllowedKinds"/> carrying no body, no initializer, and synthesizing
/// no members; ANY member-declaration kind OUTSIDE the allowlist fails closed, and the
/// executable/synthesizing FORMS inside an allowed member (bodies, initializers, top-level
/// statements, primary-ctor/positional-record parameter lists, extern/[DllImport]) fail
/// closed too.
///
/// Enforcement is TWO coupled gates (MA-D): (1) a closed allowlist over every
/// <see cref="MemberDeclarationSyntax"/> node — a member kind not in <see cref="AllowedKinds"/>
/// (e.g. a constructor/operator/conversion/destructor, incl. their net10 BODYLESS `partial`
/// forms that carry no <c>Block</c> for the deny-list to see) fails closed, so a NEWLY-ADDED
/// synthesizing C# member form fails closed by default; and (2) a deny-list of executable
/// content that can appear WITHIN an allowed member. Non-member structural nodes (type
/// references, parameter lists of allowed methods, accessor lists, identifiers) are neutral.
/// <see cref="AllowedKinds"/> is thus LOAD-BEARING, not decorative.
/// </summary>
public static class SyntaxAllowlist
{
    private static readonly string[] AllowedKinds =
    {
        "CompilationUnit", "UsingDirective",
        "NamespaceDeclaration", "FileScopedNamespaceDeclaration",
        "ClassDeclaration", "StructDeclaration", "InterfaceDeclaration",
        "RecordDeclaration", "EnumDeclaration", "EnumMemberDeclaration",
        "DelegateDeclaration", "FieldDeclaration", "PropertyDeclaration",
        "IndexerDeclaration", "EventFieldDeclaration", "MethodDeclaration",
        "AccessorDeclaration", "AttributeList",
    };

    private static readonly HashSet<string> AllowedKindSet = new(AllowedKinds, StringComparer.Ordinal);

    /// <summary>The enumerated allowed declaration-kind set (meta-test subject, INV-011).</summary>
    public static IReadOnlyList<string> AllowedDeclarationKinds => AllowedKinds;

    /// <summary>
    /// True iff the C# source contains ONLY allowlisted body-free declarations,
    /// parsed under the build's ACTUAL DefineConstants (EXT7-03).
    /// </summary>
    public static bool ContainsOnlyDeclarations(string csharpSource, IReadOnlyList<string> defineConstants)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest)
            .WithPreprocessorSymbols(defineConstants);
        SyntaxTree tree = CSharpSyntaxTree.ParseText(csharpSource, parseOptions);
        SyntaxNode root = tree.GetRoot();

        foreach (var node in root.DescendantNodes())
        {
            switch (node)
            {
                case BlockSyntax:                          // method/ctor/accessor bodies
                case ArrowExpressionClauseSyntax:          // expression-bodied members
                case EqualsValueClauseSyntax:              // field/property initializers
                case GlobalStatementSyntax:                // top-level statements
                    return false;
            }

            // Closed allowlist over MEMBER declarations (MA-D): a member-declaration kind not
            // in AllowedKinds fails closed — this is what catches a member-synthesizing form
            // (constructor / operator / conversion / destructor, incl. their net10 bodyless
            // `partial` variants) that carries no Block for the deny-list above to see.
            if (node is MemberDeclarationSyntax && !AllowedKindSet.Contains(node.Kind().ToString()))
            {
                return false;
            }

            // Primary constructor / positional record parameter list.
            if (node is TypeDeclarationSyntax td && td.ParameterList is not null)
            {
                return false;
            }

            // extern / [DllImport] (native code, no C# body).
            if (node is BaseMethodDeclarationSyntax bm
                && bm.Modifiers.Any(m => m.IsKind(SyntaxKind.ExternKeyword)))
            {
                return false;
            }
            if (node is AttributeSyntax attr && attr.Name.ToString().Contains("DllImport", StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>
/// The kernel purity control (INV-004): a syntax symbol-usage scan over the whole
/// Corrected.Gate.Kernel project's hand-written source + a recursively-checked
/// deep-immutability predicate + extern/[DllImport] rejection + a project-graph bound.
/// </summary>
public static class KernelPurityScanner
{
    private static readonly string[] Forbidden =
    {
        "System.IO",
        "System.Console",
        "System.Net",
        "System.Diagnostics.Process",
        "System.Diagnostics.Stopwatch",
        "System.Reflection.Assembly.Location",
        "System.Reflection.Assembly.LoadFrom",
        "System.Reflection.Assembly.LoadFile",
        "System.Reflection.Assembly.Load",
        "System.Runtime.Loader.AssemblyLoadContext",
        "System.Threading.Thread.Sleep",
        "System.Threading.Tasks.Task.Delay",
        "System.DateTime.Now",
        "System.DateTime.UtcNow",
        "System.DateTime.Today",
        "System.DateTimeOffset.Now",
        "System.DateTimeOffset.UtcNow",
        "System.TimeProvider",
        "System.Environment",
        "System.Random",
        "System.Security.Cryptography.RandomNumberGenerator",
        "System.Guid.NewGuid",
        "System.Globalization.CultureInfo.CurrentCulture",
        "System.Globalization.CultureInfo.CurrentUICulture",
        "System.GC",
        "DllImport",
    };

    /// <summary>The enumerated forbidden-symbol set (meta-test subject, INV-004).</summary>
    public static IReadOnlyList<string> ForbiddenSymbols => Forbidden;

    /// <summary>Scan the Kernel project's hand-written source for forbidden symbols / extern.</summary>
    public static ScanResult ScanKernelProject(string kernelProjectPath)
    {
        string projDir = Path.GetDirectoryName(kernelProjectPath)!;
        foreach (var cs in Directory.EnumerateFiles(projDir, "*.cs", SearchOption.AllDirectories))
        {
            string norm = cs.Replace('\\', '/');
            if (norm.Contains("/obj/") || norm.Contains("/bin/"))
            {
                continue; // exclude generated GlobalUsings / AssemblyInfo (obj/) + outputs (bin/)
            }

            SyntaxTree tree = CSharpSyntaxTree.ParseText(File.ReadAllText(cs));
            SyntaxNode root = tree.GetRoot();

            // extern / [DllImport] declarations (P/Invoke, EXT9-03).
            foreach (var node in root.DescendantNodes())
            {
                if (node is BaseMethodDeclarationSyntax bm
                    && bm.Modifiers.Any(m => m.IsKind(SyntaxKind.ExternKeyword)))
                {
                    return ScanResult.Create(ScanOutcome.Fail, "extern/[DllImport]");
                }
            }

            // Symbol-usage over the CODE tokens only (excludes comment/using-directive trivia).
            string code = string.Concat(root.DescendantTokens().Select(t => t.Text));
            foreach (var forbidden in Forbidden)
            {
                if (code.Contains(forbidden, StringComparison.Ordinal))
                {
                    return ScanResult.Create(ScanOutcome.Fail, forbidden);
                }
            }
        }

        return ScanResult.Create(ScanOutcome.Pass, null);
    }

    /// <summary>
    /// RECURSIVE deep-immutability predicate (EXT9-04): primitive/string/enum, OR an
    /// immutable record / ImmutableArray&lt;T&gt; / FrozenDictionary&lt;K,V&gt; every
    /// one of whose generic args AND record fields itself satisfies this predicate.
    /// </summary>
    public static bool IsDeeplyImmutable(Type type) => IsDeeplyImmutable(type, new HashSet<Type>());

    private static bool IsDeeplyImmutable(Type type, HashSet<Type> visited)
    {
        if (!visited.Add(type))
        {
            return true; // already being evaluated on this path (cycle guard)
        }

        if (type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal))
        {
            return true;
        }

        if (type.IsArray)
        {
            return false; // arrays are mutable
        }

        if (type.IsGenericType)
        {
            Type def = type.GetGenericTypeDefinition();
            if (ImmutableGenericDefs.Contains(def))
            {
                return type.GetGenericArguments().All(a => IsDeeplyImmutable(a, visited));
            }
            return false; // List<>, Dictionary<>, HashSet<>, ... are mutable
        }

        // A record / class: immutable iff every public instance property is get-only
        // AND its type is deeply immutable.
        var props = type.GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
        foreach (var p in props)
        {
            if (p.GetSetMethod(nonPublic: false) is not null)
            {
                return false; // publicly settable
            }
            if (!IsDeeplyImmutable(p.PropertyType, visited))
            {
                return false;
            }
        }
        return true;
    }

    private static readonly HashSet<Type> ImmutableGenericDefs = new()
    {
        typeof(ImmutableArray<>),
        typeof(ImmutableList<>),
        typeof(ImmutableDictionary<,>),
        typeof(ImmutableHashSet<>),
        typeof(ImmutableSortedDictionary<,>),
        typeof(ImmutableSortedSet<>),
        typeof(FrozenDictionary<,>),
        typeof(FrozenSet<>),
    };

    /// <summary>Assert the Kernel project declares NO ProjectReference and NO PackageReference (INV-004 project-graph).</summary>
    public static bool KernelHasNoProjectOrPackageReference(string kernelProjectPath)
    {
        string xml = File.ReadAllText(kernelProjectPath);
        return !xml.Contains("<ProjectReference", StringComparison.Ordinal)
            && !xml.Contains("<PackageReference", StringComparison.Ordinal);
    }
}
