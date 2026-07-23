// GREEN — Route A seam (DD-001): CliCompilation via DafnyDriver 4.11.0, the
// closest in-process analogue of Dafny's reference driver. CliCompilation
// wraps a Compilation built from CompilationInput (dafny v4.11.0
// Source/DafnyDriver/CliCompilation.cs:50-52), so the OQ-003 canary's
// ProcessSolverOptions transformation runs on this route's verification path
// (Source/DafnyCore/Pipeline/TextDocumentLoader.cs:58-59).
//
// Public surface uses contract types ONLY (INV-008/RS-019b). Semantic facts
// come exclusively from typed Dafny/Boogie results — no console scraping, no
// regex, no direct fixture reads (PRH-004/TA-A10).

using Corrected.Spike.Contracts;
using Corrected.Spike.Seam;
using DafnyDriver.Commands;
using Microsoft.Dafny;
using ContractTask = Corrected.Spike.Contracts.VerificationTask;

namespace Corrected.Spike.RouteA;

public sealed class RouteAAdapter : ISpikeRouteAdapter
{
    public string RouteId => "A";

    public VerificationRun Verify(FixtureInput input)
    {
        return VerifyAsync(input).GetAwaiter().GetResult();
    }

    private static async Task<VerificationRun> VerifyAsync(FixtureInput input)
    {
        var options = SeamCommon.BuildOptions(input);
        var cli = CliCompilation.Create(options);
        var diagnostics = new List<DafnyDiagnostic>();
        using var subscription = cli.Compilation.Updates.Subscribe(ev =>
        {
            if (ev is NewDiagnostic nd && nd.Diagnostic.Level == ErrorLevel.Error)
            {
                lock (diagnostics)
                {
                    diagnostics.Add(nd.Diagnostic);
                }
            }
        });
        cli.Start();

        var tasks = new List<ContractTask>();
        await foreach (var result in cli.VerifyAllLazily(null))
        {
            foreach (var taskResult in result.Results)
            {
                tasks.Add(new ContractTask(
                    result.CanVerify.FullDafnyName,
                    SeamCommon.MapOutcome(taskResult.Result.Outcome),
                    taskResult.Result.ResourceCount > 0));
                // Non-valid outcomes carry counterexamples; materialize the
                // typed diagnostics exactly the way the reference driver does
                // (Compilation.GetDiagnosticsFromResult — typed API, never
                // console text).
                if (taskResult.Result.Outcome != Microsoft.Boogie.SolverOutcome.Valid)
                {
                    lock (diagnostics)
                    {
                        diagnostics.AddRange(SeamCommon.DiagnosticsFromResult(options, result.CanVerify, taskResult));
                    }
                }
            }
        }

        var resolution = await cli.Resolution;
        lock (diagnostics)
        {
            return SeamCommon.BuildRun(options, resolution, diagnostics.ToList(), tasks, input);
        }
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
        // The options object is aliased by design (Compilation.Options =>
        // Input.Options — Source/DafnyCore/Pipeline/Compilation.cs:56); the
        // readback observes the state AFTER upstream option processing, which
        // EXECUTED the configured solver and mutated
        // SolverIdentifier/SolverVersion/ProverOptions (OQ-003).
        var options = SeamCommon.BuildOptions(input);
        var cli = CliCompilation.Create(options);
        cli.Start();
        try
        {
            await foreach (var _ in cli.VerifyAllLazily(null))
            {
            }
            _ = await cli.Resolution;
        }
        catch (Exception ex)
        {
            // The canary transformation (ProcessSolverOptions) runs during
            // document load, BEFORE any prover launch; a stub solver that
            // cannot complete verification must not hide the typed readback.
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

    private static async Task<(Program Parsed, ResolutionResult Resolution)> ResolveOnly(FixtureInput input)
    {
        var options = SeamCommon.BuildOptions(input);
        var cli = CliCompilation.Create(options);
        // AST/closure/hygiene probes never solve; skipping solver-option
        // processing keeps them independent of the provisioned binary.
        cli.Compilation.ShouldProcessSolverOptions = false;
        cli.Start();
        var parsed = await cli.Compilation.ParsedProgram;
        var resolution = await cli.Resolution;
        if (parsed is null || resolution is null || resolution.HasErrors)
        {
            throw new InvalidOperationException(
                "frontend failed during AST/closure/hygiene recovery — parse or resolution errors on a fixture expected to resolve (typed in-process failure, INV-013)");
        }
        return (parsed, resolution);
    }
}
