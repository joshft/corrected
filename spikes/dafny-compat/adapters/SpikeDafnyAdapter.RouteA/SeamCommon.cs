// GREEN — shared seam logic. This single source file is compiled into BOTH
// route-seam assemblies (Route B links it via <Compile Include>), so route
// package isolation (INV-008/DD-001) is preserved while classification,
// option-construction, and readback logic stay identical across routes —
// which is exactly what BND-004 control equivalence wants (codex R2-2).
//
// DECISION: the file lives inside the Route A seam directory because the
// INV-008 source-grep whitelists only the two seam project directories for
// Microsoft.Dafny/Microsoft.Boogie references; a third shared directory would
// trip that scan. Everything here is internal (RS-019b: no Dafny type may
// appear on any externally visible signature).
//
// PRH-004/TA-A10: no regex, no direct fixture reads — fixture bytes reach
// semantics only through DafnyCore's typed pipeline; classification reads
// resolved-AST properties, never surface syntax.

using System.Globalization;
using Corrected.Spike.Contracts;
using Microsoft.Dafny;
using BoogieOutcome = Microsoft.Boogie.SolverOutcome;
using ContractOutcome = Corrected.Spike.Contracts.SolverOutcome;
using ContractTask = Corrected.Spike.Contracts.VerificationTask;

namespace Corrected.Spike.Seam;

internal static class SeamCommon
{
    internal static DafnyOptions BuildOptions(FixtureInput input)
    {
        var options = new DafnyOptions(TextReader.Null, TextWriter.Null, TextWriter.Null)
        {
            // The new-CLI option surface is the one this spike freezes; without
            // this flag DafnyCore rejects .doo inputs ("cannot be used with the
            // legacy CLI"), which would sever the OQ-004 DafnyPipeline
            // standard-libraries consumption path.
            UsingNewCli = true,
        };

        // The CLI materializes every command option's value before running;
        // library/doo option-compatibility checks (DooFile.CheckAndGetLibraryOptions)
        // only consider options PRESENT in OptionArguments, so programmatic
        // construction must materialize registered global/module option
        // defaults or a .doo's recorded options (e.g. the standard library's
        // reads-clauses-on-methods) are silently dropped.
        MaterializeRegisteredOptionDefaults(options);

        // Frozen Option Manifest values (manifest/option-manifest.json is the
        // oracle — RS-018b). Legacy bindings applied per option so the typed
        // Boogie fields (VcsCores, TimeLimit, ResourceLimit, ProverOptions)
        // carry the effective values. Set+ApplyBinding pairs are written out
        // inline: a generic helper would have to name the option type, whose
        // namespace is banned from spike source by INV-002.
        options.Set(BoogieOptionBag.Cores, 1U);
        options.ApplyBinding(BoogieOptionBag.Cores);
        options.Set(BoogieOptionBag.VerificationTimeLimit, 0U);
        options.ApplyBinding(BoogieOptionBag.VerificationTimeLimit);
        options.Set(BoogieOptionBag.SolverResourceLimit, ParseUint(Get(input, "--resource-limit") ?? "10000000"));
        options.ApplyBinding(BoogieOptionBag.SolverResourceLimit);
        options.Set(BoogieOptionBag.NoVerify, false);
        options.ApplyBinding(BoogieOptionBag.NoVerify);
        options.Set(CommonOptionBag.VerifyIncludedFiles, Get(input, "--verify-included-files") == "true");
        options.ApplyBinding(CommonOptionBag.VerifyIncludedFiles);
        options.Set(CommonOptionBag.UseStandardLibraries, Get(input, "--standard-libraries") == "true");
        options.ApplyBinding(CommonOptionBag.UseStandardLibraries);
        // DECISION: UnicodeCharacters/AllowWarnings are materialized because
        // programmatic construction (unlike a CLI parse) leaves unset options
        // at default(T), and CliCompilation emits a deprecation warning when
        // unicode-char reads false; the spec's option manifest is silent on
        // both, so the Dafny 4.x defaults are used.
        options.Set(CommonOptionBag.UnicodeCharacters, true);
        options.ApplyBinding(CommonOptionBag.UnicodeCharacters);
        options.Set(CommonOptionBag.AllowWarnings, true);
        options.ApplyBinding(CommonOptionBag.AllowWarnings);

        // RS-003c: ONE shared, unit-tested function builds the solver-path
        // option for every probe (sentinel and real runs alike).
        var solverPath = Get(input, "--solver-path")
            ?? throw new InvalidOperationException("no --solver-path in fixture input — ambient solver discovery is prohibited (INV-003)");
        var built = SolverPathOptionBuilder.Build(solverPath);
        if (built.Count != 2 || built[0] != "--solver-path")
        {
            throw new InvalidOperationException("SolverPathOptionBuilder produced an unexpected option shape (RS-003c)");
        }
        options.Set(BoogieOptionBag.SolverPath, new FileInfo(built[1]));
        options.ApplyBinding(BoogieOptionBag.SolverPath);

        // EA-005: the typed Boogie prover-option list must contain exactly one
        // PROVER_PATH entry and no conflicting values before solver-option
        // processing runs (fail-closed startup hygiene).
        var proverPaths = options.ProverOptions.Where(o => o.StartsWith("PROVER_PATH=", StringComparison.Ordinal)).ToList();
        if (proverPaths.Count != 1)
        {
            throw new InvalidOperationException(
                $"startup-gate: expected exactly one PROVER_PATH prover option, found {proverPaths.Count} (EA-005 duplicate/conflict check)");
        }

        // DafnyCore ships DafnyPrelude.bpl as a package content file that lands
        // beside the DIRECT package consumer's assembly only; the harness
        // executable's output does not receive it through the project
        // reference. Point the typed option at the definitive asset instead of
        // relying on Dafny's assembly-adjacent fallback probe.
        options.DafnyPrelude = ResolvePreludePath();

        options.CliRootSourceUris.Add(new Uri(Path.GetFullPath(input.RepoRelativePath)));

        // The CLI handler applies the Boogie-side defaults after binding
        // (TheProverFactory, the default backend, abstract interpretation);
        // programmatic construction must do the same or the checker pool
        // dereferences an uninitialized prover factory.
        options.ApplyDefaultOptionsWithoutSettingsDefault();
        return options;
    }

    private static string ResolvePreludePath()
    {
        var adjacent = Path.Combine(AppContext.BaseDirectory, "DafnyPrelude.bpl");
        if (File.Exists(adjacent))
        {
            return adjacent;
        }
        var coreAssembly = typeof(BoogieGenerator).Assembly;
        var besideCore = Path.Combine(Path.GetDirectoryName(coreAssembly.Location) ?? "", "DafnyPrelude.bpl");
        if (File.Exists(besideCore))
        {
            return besideCore;
        }
        var packagesRoot = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
        var informational = coreAssembly
            .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>()
            .FirstOrDefault()?.InformationalVersion;
        var packageVersion = informational?.Split('+')[0];
        if (packagesRoot is not null && packageVersion is not null)
        {
            var fromPackage = Path.Combine(packagesRoot, "dafnycore", packageVersion,
                "contentFiles", "any", "net8.0", "DafnyPrelude.bpl");
            if (File.Exists(fromPackage))
            {
                return fromPackage;
            }
        }
        throw new InvalidOperationException(
            "DafnyPrelude.bpl not found beside the harness, beside DafnyCore, or in the spike-local package folder — cannot verify (fail-closed)");
    }

    private static void MaterializeRegisteredOptionDefaults(DafnyOptions options)
    {
        // Option registration happens in the option-owning classes' static
        // constructors; the CLI forces them all during command construction.
        // Touch the DafnyCore command option groups so OptionRegistry is fully
        // populated before defaults are materialized and before any .doo
        // option-compatibility check runs.
        _ = DafnyCommands.VerificationOptions.Count;
        _ = DafnyCommands.TranslationOptions.Count;
        _ = DafnyCommands.ExecutionOptions.Count;
        _ = DafnyCommands.ConsoleOutputOptions.Count;
        _ = DafnyCommands.ParserOptions.Count;
        _ = DafnyCommands.ResolverOptions.Count;

        foreach (var option in OptionRegistry.GlobalOptions
                     .Concat(OptionRegistry.ModuleOptions))
        {
            if (options.Options.OptionArguments.ContainsKey(option))
            {
                continue;
            }
            object? value = null;
            // The default-value surface is an explicitly implemented interface;
            // reflect through the interface map instead of naming its type,
            // whose namespace is banned from spike source by INV-002.
            var descriptor = option.GetType().GetInterfaces()
                .FirstOrDefault(i => i.Name == "IValueDescriptor" && !i.IsGenericType);
            var hasDefault = descriptor?.GetProperty("HasDefaultValue")?.GetValue(option) as bool? ?? false;
            if (hasDefault)
            {
                value = descriptor!.GetMethod("GetDefaultValue")!.Invoke(option, null);
            }
            else if (option.ValueType.IsValueType)
            {
                value = Activator.CreateInstance(option.ValueType);
            }
            else if (option.ValueType != typeof(string)
                     && typeof(System.Collections.IEnumerable).IsAssignableFrom(option.ValueType)
                     && option.ValueType.IsGenericType)
            {
                // Multi-value options materialize as EMPTY collections under a
                // CLI parse (never null); doo option-compatibility checks
                // dereference them.
                var element = option.ValueType.GetGenericArguments().FirstOrDefault() ?? typeof(object);
                value = Array.CreateInstance(element, 0);
            }
            options.Options.OptionArguments[option] = value!;
        }
    }

    private static string? Get(FixtureInput input, string key) =>
        input.Options.TryGetValue(key, out var value) ? value : null;

    private static uint ParseUint(string value) => uint.Parse(value, CultureInfo.InvariantCulture);

    internal static ContractOutcome MapOutcome(BoogieOutcome outcome) => outcome switch
    {
        BoogieOutcome.Valid => ContractOutcome.Valid,
        BoogieOutcome.Invalid => ContractOutcome.Invalid,
        BoogieOutcome.TimeOut => ContractOutcome.TimedOut,
        BoogieOutcome.OutOfResource => ContractOutcome.OutOfResource,
        BoogieOutcome.OutOfMemory => ContractOutcome.OutOfResource,
        BoogieOutcome.Bounded => ContractOutcome.Valid, // Compilation.GetOutcome maps Bounded to Correct
        _ => ContractOutcome.Undetermined,
    };

    internal static VerificationStage StageOf(MessageSource source) => source switch
    {
        // Committed derivation rule (schema/evidence-schema.json →
        // stage_typing_derivation_rule): parser diagnostics → parse,
        // resolver/type-checker diagnostics → resolution, Boogie/solver task
        // results → verification. Project-source diagnostics (unreadable or
        // unrecognized input files) surface before parsing and map to parse.
        MessageSource.Parser => VerificationStage.Parse,
        MessageSource.Project => VerificationStage.Parse,
        MessageSource.Verifier => VerificationStage.Verification,
        _ => VerificationStage.Resolution,
    };

    internal static TypedDiagnostic ToTyped(DafnyDiagnostic diagnostic)
    {
        var uri = diagnostic.Range.StartToken?.Uri;
        var file = uri is { IsFile: true }
            ? RelativizeToCwd(uri.LocalPath)
            : uri?.ToString() ?? "";
        return new TypedDiagnostic(
            StageOf(diagnostic.Source),
            file,
            diagnostic.Range.StartToken?.line ?? 0,
            diagnostic.Range.EndToken?.line,
            diagnostic.Message);
    }

    private static string RelativizeToCwd(string absolutePath)
    {
        var cwd = Directory.GetCurrentDirectory();
        var full = Path.GetFullPath(absolutePath);
        return full.StartsWith(cwd + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            ? Path.GetRelativePath(cwd, full).Replace('\\', '/')
            : full.Replace('\\', '/');
    }

    internal static VerificationRun BuildRun(
        DafnyOptions options,
        ResolutionResult? resolution,
        IReadOnlyList<DafnyDiagnostic> errorDiagnostics,
        IReadOnlyList<ContractTask> tasks,
        FixtureInput input)
    {
        var typed = errorDiagnostics.Select(ToTyped).ToList();
        VerificationStage stage;
        if (typed.Any(d => d.Stage == VerificationStage.Parse))
        {
            stage = VerificationStage.Parse;
        }
        else if (typed.Any(d => d.Stage == VerificationStage.Resolution))
        {
            stage = VerificationStage.Resolution;
        }
        else
        {
            stage = VerificationStage.Verification;
        }

        var targets = TargetNames(resolution);
        var effectiveSolver = LastProverPath(options) ?? Get(input, "--solver-path") ?? "";
        return new VerificationRun(stage, targets, tasks, typed, 0, effectiveSolver);
    }

    internal static IReadOnlyList<string> TargetNames(ResolutionResult? resolution)
    {
        if (resolution?.CanVerifies is null)
        {
            return Array.Empty<string>();
        }
        return resolution.CanVerifies
            .SelectMany(kv => kv.Value.Values)
            .Distinct()
            .Select(cv => cv.FullDafnyName)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Materializes typed diagnostics from a completed non-valid verification
    /// result via the same official surface the reference driver uses
    /// (Compilation.GetDiagnosticsFromResult) — typed API, never console text.
    /// </summary>
    internal static IEnumerable<DafnyDiagnostic> DiagnosticsFromResult(
        DafnyOptions options,
        ICanVerify canVerify,
        VerificationTaskResult taskResult)
    {
        var uri = canVerify.Origin.Uri;
        return Compilation
            .GetDiagnosticsFromResult(options, uri, canVerify, taskResult.Task, taskResult.Result)
            .Where(d => d.Level == ErrorLevel.Error);
    }

    internal static string? LastProverPath(DafnyOptions options)
    {
        const string prefix = "PROVER_PATH=";
        return options.ProverOptions
            .Where(o => o.StartsWith(prefix, StringComparison.Ordinal))
            .Select(o => o[prefix.Length..])
            .LastOrDefault();
    }

    internal static OptionReadback BuildReadback(FixtureInput input, DafnyOptions options)
    {
        // Naive readback of Compilation.Options is input echo by API design
        // (Options => Input.Options — codex F5). The values below are read
        // AFTER upstream option processing mutated the aliased object:
        // ProcessSolverOptions executed the configured binary (GetZ3Version)
        // and set SolverIdentifier/SolverVersion plus O: prover options
        // (dafny v4.11.0 Source/DafnyCore/DafnyOptions.cs:941-946,1241-1305).
        var effective = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["--no-verify"] = Lower(!options.Verify),
            ["--filter-symbol"] = null,
            ["--verify-included-files"] = Lower(options.Get(CommonOptionBag.VerifyIncludedFiles)),
            ["--library"] = null,
            ["--cores"] = options.VcsCores.ToString(CultureInfo.InvariantCulture),
            ["--solver-path"] = LastProverPath(options),
            ["--verification-time-limit"] = options.TimeLimit.ToString(CultureInfo.InvariantCulture),
            ["--resource-limit"] = options.ResourceLimit.ToString(CultureInfo.InvariantCulture),
            ["--function-syntax"] = options.FunctionSyntax == FunctionSyntaxOptions.Version4 ? "4" : options.FunctionSyntax.ToString(),
            ["--random-seed"] = (options.RandomSeed ?? 0).ToString(CultureInfo.InvariantCulture),
            ["--standard-libraries"] = Lower(options.Get(CommonOptionBag.UseStandardLibraries)),
            ["--boogie"] = null,
        };
        var requested = new Dictionary<string, string?>(input.Options, StringComparer.Ordinal);
        var proverPathEntry = options.ProverOptions
            .LastOrDefault(o => o.StartsWith("PROVER_PATH=", StringComparison.Ordinal)) ?? "";
        var canary = new NormalizationCanary(
            "--solver-path",
            Get(input, "--solver-path") ?? "",
            proverPathEntry,
            options.SolverIdentifier,
            options.SolverVersion?.ToString(),
            options.ProverOptions.Where(o => o.StartsWith("O:", StringComparison.Ordinal)).ToList());
        return new OptionReadback(requested, effective, canary);
    }

    private static string Lower(bool value) => value ? "true" : "false";

    internal static ClosureResult BuildClosure(Program parsedProgram, ResolutionResult resolution, bool verifyIncludedFiles)
    {
        // The closure is the PARSED/RESOLVED file set (Program.Files carries
        // one FileModuleDefinition per source file, includes included files) —
        // never an echo of the CLI argument (INV-012/AP-011).
        var repoRoot = FindRepoRoot();
        var closure = resolution.ResolvedProgram.Files
            .Select(f => f.Origin.Uri)
            .Where(u => u is { IsFile: true })
            .Select(u => Path.GetRelativePath(repoRoot, Path.GetFullPath(u!.LocalPath)).Replace('\\', '/'))
            .Distinct()
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();
        return new ClosureResult(closure, TargetNames(resolution), verifyIncludedFiles);
    }

    private static string FindRepoRoot()
    {
        // PR-001 (gitfile merge group): route through the ONE shared
        // gitfile-aware resolver — a linked worktree's `.git` FILE resolves
        // like a `.git` directory. FAIL CLOSED when no boundary exists: a
        // silent CWD fallback would emit wrong repo-relative closure paths
        // as INV-012 evidence.
        return Corrected.Spike.Contracts.GitResolver.FindRepoRoot(Directory.GetCurrentDirectory())
               ?? throw new InvalidOperationException(
                   "no git repo boundary above the working directory — repo-relative closure paths are impossible (PR-001/INV-012 fail-closed)");
    }

    internal static NodeTable BuildNodeTable(Program parsedProgram, ResolutionResult resolution)
    {
        var resolved = resolution.ResolvedProgram;
        var explicitGhost = CollectExplicitGhost(parsedProgram);

        var declarations = new List<DeclarationRow>();
        foreach (var member in DefaultClassMembers(resolved))
        {
            var kind = member switch
            {
                Function => "function",
                ConstantField => "const",
                Method => "method",
                _ => null,
            };
            if (kind is null)
            {
                continue;
            }
            var ghost = member.IsGhost;
            var explicitKeyword = explicitGhost.GetValueOrDefault(member.Name, false);
            declarations.Add(new DeclarationRow(member.Name, kind, ghost, ghost && !explicitKeyword, explicitKeyword));
        }
        foreach (var method in DefaultClassMembers(resolved).OfType<Method>())
        {
            if (method.Body is null)
            {
                continue;
            }
            foreach (var vds in AllStatements(method.Body).OfType<VarDeclStmt>())
            {
                foreach (var local in vds.Locals)
                {
                    var ghost = local.IsGhost;
                    var explicitKeyword = explicitGhost.GetValueOrDefault(local.Name, false);
                    declarations.Add(new DeclarationRow(local.Name, "local-variable", ghost, ghost && !explicitKeyword, explicitKeyword));
                }
            }
        }

        var counts = new FormCounts();
        foreach (var method in DefaultClassMembers(resolved).OfType<Method>())
        {
            if (method.Body is not null)
            {
                CountForms(method.Body, counts);
            }
        }
        var nodes = new List<NodeFormRow>
        {
            new("assert", "proof", counts.Assert),
            new("calc", "proof", counts.Calc),
            new("loop-invariant", "proof", counts.LoopInvariant),
            new("decreases", "proof", counts.Decreases),
            new("assignment", "compiled", counts.Assignment),
            new("conditional", "compiled", counts.Conditional),
            new("loop-body", "compiled", counts.LoopBody),
            new("expect", "compiled", counts.Expect),
        };
        return new NodeTable(declarations, nodes);
    }

    private sealed class FormCounts
    {
        public int Assert;
        public int Calc;
        public int LoopInvariant;
        public int Decreases;
        public int Assignment;
        public int Conditional;
        public int LoopBody;
        public int Expect;
    }

    private static void CountForms(Statement statement, FormCounts counts)
    {
        switch (statement)
        {
            case AssertStmt:
                counts.Assert++;
                return; // proof node; do not descend into proof bodies
            case CalcStmt:
                counts.Calc++;
                return; // proof node; hints/lines stay inside the calc
            case ExpectStmt:
                counts.Expect++;
                return;
            case AssumeStmt:
                return;
            case VarDeclStmt varDecl:
                if (varDecl.Assign is not null && !varDecl.IsGhost)
                {
                    counts.Assignment++;
                }
                return; // the embedded assignment is counted once, here
            case ConcreteAssignStatement assign:
                if (!assign.IsGhost)
                {
                    counts.Assignment++;
                }
                return;
            case IfStmt ifStmt:
                if (!ifStmt.IsGhost)
                {
                    counts.Conditional++;
                }
                break;
            case LoopStmt loop:
                counts.LoopInvariant += loop.Invariants.Count;
                if (!loop.InferredDecreases && loop.Decreases.Expressions is { Count: > 0 })
                {
                    counts.Decreases++;
                }
                if (loop is OneBodyLoopStmt { Body: not null } && !loop.IsGhost)
                {
                    counts.LoopBody++;
                }
                break;
        }
        foreach (var sub in statement.SubStatements)
        {
            CountForms(sub, counts);
        }
    }

    private static IEnumerable<Statement> AllStatements(Statement statement)
    {
        yield return statement;
        foreach (var sub in statement.SubStatements)
        {
            foreach (var nested in AllStatements(sub))
            {
                yield return nested;
            }
        }
    }

    private static IEnumerable<MemberDecl> DefaultClassMembers(Program program) =>
        program.DefaultModuleDef.TopLevelDecls.OfType<DefaultClassDecl>().SelectMany(c => c.Members);

    /// <summary>
    /// Explicit-modifier presence comes from the PARSER AST (pre-resolution
    /// clone), independent of the sidecar (codex F10): a name maps to true only
    /// when the parsed tree itself carries the ghost modifier.
    /// </summary>
    private static Dictionary<string, bool> CollectExplicitGhost(Program parsedProgram)
    {
        var result = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (var member in DefaultClassMembers(parsedProgram))
        {
            result[member.Name] = member.IsGhost;
        }
        foreach (var method in DefaultClassMembers(parsedProgram).OfType<Method>())
        {
            if (method.Body is null)
            {
                continue;
            }
            foreach (var vds in AllStatements(method.Body).OfType<VarDeclStmt>())
            {
                foreach (var local in vds.Locals)
                {
                    // The parser marks the explicit `ghost var` keyword on the
                    // LocalVariable itself (Statement.IsGhost is
                    // FilledInDuringResolution); resolution has not run on this
                    // tree, so no inference can have set it.
                    result[local.Name] = local.IsGhost;
                }
            }
        }
        return result;
    }

    internal static HygieneReport BuildHygiene(ResolutionResult resolution)
    {
        var resolved = resolution.ResolvedProgram;
        var allBodied = true;
        var containsAttributes = false;
        var containsAssume = false;
        var suppressionAttributes = new[] { "verify", "axiom" };
        var containsSuppression = false;

        foreach (var member in DefaultClassMembers(resolved))
        {
            var body = member switch
            {
                Method m => (object?)m.Body,
                Function f => f.Body,
                ConstantField c => c.Rhs,
                _ => new object(),
            };
            if (body is null)
            {
                allBodied = false;
            }
            for (var attr = member.Attributes; attr is not null; attr = attr.Prev)
            {
                containsAttributes = true;
                if (suppressionAttributes.Contains(attr.Name))
                {
                    containsSuppression = true;
                }
            }
            if (member is Method { Body: not null } method)
            {
                foreach (var stmt in AllStatements(method.Body))
                {
                    if (stmt is AssumeStmt)
                    {
                        containsAssume = true;
                    }
                    for (var attr = stmt.Attributes; attr is not null; attr = attr.Prev)
                    {
                        containsAttributes = true;
                        if (suppressionAttributes.Contains(attr.Name))
                        {
                            containsSuppression = true;
                        }
                    }
                }
            }
        }
        return new HygieneReport(allBodied, containsAttributes, containsAssume, containsSuppression || containsAssume);
    }
}
