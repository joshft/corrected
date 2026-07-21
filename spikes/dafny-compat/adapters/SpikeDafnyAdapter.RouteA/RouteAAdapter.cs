// STUB:TDD — RED-phase seam stub. GREEN implements Route A via DafnyDriver's
// CliCompilation (DD-001): CliCompilation wraps a Compilation built from
// CompilationInput (dafny v4.11.0 Source/DafnyDriver/CliCompilation.cs:50-52),
// so the OQ-003 canary's ProcessSolverOptions transformation runs on this
// route's verification path (Source/DafnyCore/Pipeline/TextDocumentLoader.cs:58-59).
// Public surface uses contract types ONLY (INV-008/RS-019b).

using Corrected.Spike.Contracts;

namespace Corrected.Spike.RouteA;

public sealed class RouteAAdapter : ISpikeRouteAdapter
{
    public string RouteId => "A";

    public VerificationRun Verify(FixtureInput input)
    {
        // STUB:TDD
        throw new NotImplementedException("STUB:TDD");
    }

    public NodeTable RecoverAst(FixtureInput input)
    {
        // STUB:TDD
        throw new NotImplementedException("STUB:TDD");
    }

    public OptionReadback ReadOptionsPostVerification(FixtureInput input)
    {
        // STUB:TDD
        throw new NotImplementedException("STUB:TDD");
    }

    public ClosureResult RecoverClosure(FixtureInput input, bool verifyIncludedFiles)
    {
        // STUB:TDD
        throw new NotImplementedException("STUB:TDD");
    }

    public HygieneReport WalkFixtureHygiene(FixtureInput input)
    {
        // STUB:TDD
        throw new NotImplementedException("STUB:TDD");
    }
}
