// GREEN — Route B seam (DD-001): a hand-assembled Compilation from
// DafnyCore/DafnyPipeline with Boogie.ExecutionEngine driven directly — no
// DafnyDriver, no LanguageServer assembly. The parser, resolver, and verifier
// below wrap DafnyCore's own ProgramParser/ProgramResolver/BoogieGenerator, so
// this route's graph proves DafnyCore + DafnyPipeline alone carry the
// verification path.
//
// OQ-004 (resolved during RED): the named DafnyPipeline consumption on the
// verification path is the DafnyStandardLibraries.doo embedded resource — with
// --standard-libraries=true, Compilation adds
// dllresource://DafnyPipeline/DafnyStandardLibraries.doo to the file set
// (dafny v4.11.0 Source/DafnyCore/Pipeline/Compilation.cs:181-203 via
// Source/DafnyCore/DafnyMain.cs:21-28) and DafnyFile.HandleDooFile loads it via
// Assembly.Load("DafnyPipeline") + GetManifestResourceStream
// (Source/DafnyCore/DafnyFile.cs:214-222). fixtures/stdlib-anchor.dfy exercises
// this; the removal/differential test proves the consumption matters.
//
// Public surface uses contract types ONLY (INV-008/RS-019b).

using System.Collections.Concurrent;
using Corrected.Spike.Contracts;
using Corrected.Spike.Seam;
using Microsoft.Boogie;
using Microsoft.Dafny;
using Microsoft.Extensions.Logging.Abstractions;
using ContractTask = Corrected.Spike.Contracts.VerificationTask;
using DafnyProgram = Microsoft.Dafny.Program;

namespace Corrected.Spike.RouteB;

public sealed class RouteBAdapter : ISpikeRouteAdapter
{
    public string RouteId => "B";

    public VerificationRun Verify(FixtureInput input)
    {
        return VerifyAsync(input).GetAwaiter().GetResult();
    }

    private static async Task<VerificationRun> VerifyAsync(FixtureInput input)
    {
        var options = SeamCommon.BuildOptions(input);
        return await VerifyWithOptions(options, input);
    }

    private static async Task<VerificationRun> VerifyWithOptions(DafnyOptions options, FixtureInput input)
    {
        using var compilation = CreateCompilation(options);
        var diagnostics = new List<DafnyDiagnostic>();
        var states = new ConcurrentDictionary<ICanVerify, SymbolState>();

        using var subscription = compilation.Updates.Subscribe(ev =>
        {
            switch (ev)
            {
                case NewDiagnostic nd when nd.Diagnostic.Level == ErrorLevel.Error:
                    lock (diagnostics)
                    {
                        diagnostics.Add(nd.Diagnostic);
                    }
                    break;
                case CanVerifyPartsIdentified parts when states.TryGetValue(parts.CanVerify, out var state):
                    state.TaskCount = parts.Parts.Count;
                    if (state.TaskCount == state.CompletedCount)
                    {
                        state.Finished.TrySetResult();
                    }
                    break;
                case BoogieUpdate { BoogieStatus: Completed completed } update when states.TryGetValue(update.CanVerify, out var state):
                    state.CompletedParts.Enqueue((update.VerificationTask, completed));
                    var done = Interlocked.Increment(ref state.CompletedCountField);
                    if (state.TaskCount == done)
                    {
                        state.Finished.TrySetResult();
                    }
                    break;
                case BoogieException boogieException when states.TryGetValue(boogieException.CanVerify, out var state):
                    state.Finished.TrySetException(boogieException.Exception);
                    break;
                case InternalCompilationException internalException:
                    foreach (var state in states.Values)
                    {
                        state.Finished.TrySetException(internalException.Exception);
                    }
                    break;
            }
        });

        compilation.Start();
        var resolution = await compilation.Resolution;
        if (resolution is null || resolution.HasErrors || resolution.CanVerifies is null)
        {
            lock (diagnostics)
            {
                return SeamCommon.BuildRun(options, resolution, diagnostics.ToList(), Array.Empty<ContractTask>(), input);
            }
        }

        var canVerifies = resolution.CanVerifies
            .SelectMany(kv => kv.Value.Values)
            .Distinct()
            .OrderBy(cv => cv.Origin.pos)
            .ToList();
        var tasks = new List<ContractTask>();
        foreach (var canVerify in canVerifies)
        {
            states[canVerify] = new SymbolState();
            var scheduled = await compilation.VerifyCanVerify(canVerify, _ => true, null);
            if (!scheduled)
            {
                states.TryRemove(canVerify, out _);
            }
        }
        foreach (var canVerify in canVerifies)
        {
            if (!states.TryGetValue(canVerify, out var state))
            {
                continue;
            }
            await state.Finished.Task;
            foreach (var (task, completed) in state.CompletedParts)
            {
                tasks.Add(new ContractTask(
                    canVerify.FullDafnyName,
                    SeamCommon.MapOutcome(completed.Result.Outcome),
                    completed.Result.ResourceCount > 0));
                if (completed.Result.Outcome != Microsoft.Boogie.SolverOutcome.Valid)
                {
                    lock (diagnostics)
                    {
                        diagnostics.AddRange(SeamCommon.DiagnosticsFromResult(options, canVerify,
                            new VerificationTaskResult(task, completed.Result)));
                    }
                }
            }
        }

        lock (diagnostics)
        {
            return SeamCommon.BuildRun(options, resolution, diagnostics.ToList(), tasks, input);
        }
    }

    private sealed class SymbolState
    {
        public int TaskCount = -1;
        public int CompletedCountField;
        public int CompletedCount => CompletedCountField;
        public readonly ConcurrentQueue<(IVerificationTask Task, Completed Result)> CompletedParts = new();
        public readonly TaskCompletionSource Finished = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public NodeTable RecoverAst(FixtureInput input)
    {
        return RecoverAstAsync(input).GetAwaiter().GetResult();
    }

    private static async Task<NodeTable> RecoverAstAsync(FixtureInput input)
    {
        var (parsed, resolution) = await ResolveOnly(input);
        return SeamCommon.BuildNodeTable(parsed, resolution);
    }

    public OptionReadback ReadOptionsPostVerification(FixtureInput input)
    {
        return ReadOptionsAsync(input).GetAwaiter().GetResult();
    }

    private static async Task<OptionReadback> ReadOptionsAsync(FixtureInput input)
    {
        // hand-assembled DafnyOptions for CompilationInput; ProcessSolverOptions
        // still runs in TextDocumentLoader (option-manifest canary row B). The
        // readback happens POST-verification against the same aliased object;
        // a stub solver that cannot complete verification must not hide the
        // typed readback (the transformation runs during document load).
        var options = SeamCommon.BuildOptions(input);
        try
        {
            _ = await VerifyWithOptions(options, input);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"P11: verification leg ended with {ex.GetType().Name}: {ex.Message} (readback proceeds)");
        }
        return SeamCommon.BuildReadback(input, options);
    }

    public ClosureResult RecoverClosure(FixtureInput input, bool verifyIncludedFiles)
    {
        return RecoverClosureAsync(input, verifyIncludedFiles).GetAwaiter().GetResult();
    }

    private static async Task<ClosureResult> RecoverClosureAsync(FixtureInput input, bool verifyIncludedFiles)
    {
        var effectiveOptions = new Dictionary<string, string?>(input.Options)
        {
            ["--verify-included-files"] = verifyIncludedFiles ? "true" : "false",
        };
        var (parsed, resolution) = await ResolveOnly(input with { Options = effectiveOptions });
        return SeamCommon.BuildClosure(parsed, resolution, verifyIncludedFiles);
    }

    public HygieneReport WalkFixtureHygiene(FixtureInput input)
    {
        return WalkHygieneAsync(input).GetAwaiter().GetResult();
    }

    private static async Task<HygieneReport> WalkHygieneAsync(FixtureInput input)
    {
        var (_, resolution) = await ResolveOnly(input);
        return SeamCommon.BuildHygiene(resolution);
    }

    private static async Task<(DafnyProgram Parsed, ResolutionResult Resolution)> ResolveOnly(FixtureInput input)
    {
        var options = SeamCommon.BuildOptions(input);
        using var compilation = CreateCompilation(options);
        compilation.ShouldProcessSolverOptions = false;
        compilation.Start();
        var parsed = await compilation.ParsedProgram;
        var resolution = await compilation.Resolution;
        if (parsed is null || resolution is null || resolution.HasErrors)
        {
            throw new InvalidOperationException(
                "frontend failed during AST/closure/hygiene recovery — parse or resolution errors on a fixture expected to resolve (typed in-process failure, INV-013)");
        }
        return (parsed, resolution);
    }

    private static Compilation CreateCompilation(DafnyOptions options)
    {
        // Mirrors CliCompilation's construction (dafny v4.11.0
        // Source/DafnyDriver/CliCompilation.cs:38-52) but with DafnyCore-only
        // components: ProgramParser, ProgramResolver, and a BoogieGenerator
        // translation — no LanguageServer types anywhere in this graph.
        if (options.DafnyProject is null)
        {
            var firstFile = options.CliRootSourceUris.FirstOrDefault();
            var uri = firstFile ?? new Uri(Directory.GetCurrentDirectory());
            options.DafnyProject = new DafnyProject(null, uri, null,
                new HashSet<string> { uri.LocalPath }, new HashSet<string>(), new Dictionary<string, object>())
            {
                ImplicitFromCli = true,
            };
        }
        options.RunningBoogieFromCommandLine = true;

        var input = new CompilationInput(options, 0, options.DafnyProject);
        var engine = new ExecutionEngine(options, new EmptyVerificationResultCache(), DafnyMain.LargeThreadScheduler);
        var loader = new TextDocumentLoader(
            NullLogger<ITextDocumentLoader>.Instance,
            new CoreParser(options),
            new CoreResolver());
        return new Compilation(
            NullLogger<Compilation>.Instance,
            OnDiskFileSystem.Instance,
            loader,
            new CoreVerifier(),
            engine,
            input);
    }

    /// <summary>DafnyCore-only IDafnyParser: wraps ProgramParser directly.</summary>
    private sealed class CoreParser : IDafnyParser
    {
        private readonly ProgramParser parser;
        private readonly SemaphoreSlim mutex = new(1);

        public CoreParser(DafnyOptions options)
        {
            _ = options;
            parser = new ProgramParser(NullLogger<ProgramParser>.Instance, OnDiskFileSystem.Instance);
        }

        public async Task<ProgramParseResult> Parse(Compilation compilation, CancellationToken cancellationToken)
        {
            await mutex.WaitAsync(cancellationToken);
            try
            {
                var rootFiles = await compilation.RootFiles;
                return await parser.ParseFiles(compilation.Project.ProjectName, rootFiles, compilation.Reporter, cancellationToken);
            }
            finally
            {
                mutex.Release();
            }
        }
    }

    /// <summary>DafnyCore-only ISymbolResolver: wraps ProgramResolver directly.</summary>
    private sealed class CoreResolver : ISymbolResolver
    {
        private readonly SemaphoreSlim mutex = new(1);

        public async Task ResolveSymbols(Compilation compilation, DafnyProgram program, CancellationToken cancellationToken)
        {
            await mutex.WaitAsync(cancellationToken);
            try
            {
                await new ProgramResolver(program).Resolve(cancellationToken);
            }
            finally
            {
                mutex.Release();
            }
        }
    }

    /// <summary>
    /// DafnyCore-only IProgramVerifier: BoogieGenerator translation +
    /// ExecutionEngine.GetVerificationTasks (mirrors the reference
    /// DafnyProgramVerifier without the LanguageServer dependency).
    /// </summary>
    private sealed class CoreVerifier : IProgramVerifier
    {
        private readonly SemaphoreSlim mutex = new(1);

        public async Task<IReadOnlyList<IVerificationTask>> GetVerificationTasksAsync(
            ExecutionEngine engine,
            ResolutionResult resolution,
            ModuleDefinition moduleDefinition,
            CancellationToken cancellationToken)
        {
            if (!BoogieGenerator.ShouldVerifyModule(resolution.ResolvedProgram, moduleDefinition))
            {
                throw new InvalidOperationException("tried to get verification tasks for a module that is not verified");
            }
            await mutex.WaitAsync(cancellationToken);
            try
            {
                var program = resolution.ResolvedProgram;
                var errorReporter = (ObservableErrorReporter)program.Reporter;
                cancellationToken.ThrowIfCancellationRequested();
                var boogieProgram = await DafnyMain.LargeStackFactory.StartNew(() =>
                {
                    Microsoft.Dafny.Type.ResetScopes();
                    var translatorFlags = new BoogieGenerator.TranslatorFlags(errorReporter.Options)
                    {
                        InsertChecksums = 0 < engine.Options.VerifySnapshots,
                        ReportRanges = program.Options.Get(DafnyCore.Snippets.ShowSnippets),
                    };
                    var translator = new BoogieGenerator(errorReporter, program.ProofDependencyManager, translatorFlags);
                    return translator.DoTranslation(program, moduleDefinition);
                }, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                return await engine.GetVerificationTasks(boogieProgram, cancellationToken);
            }
            finally
            {
                mutex.Release();
            }
        }
    }
}
