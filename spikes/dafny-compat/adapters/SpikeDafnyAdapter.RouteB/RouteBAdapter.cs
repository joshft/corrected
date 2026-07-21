// STUB:TDD — RED-phase seam stub. GREEN implements Route B as a hand-assembled
// Compilation from DafnyCore/DafnyPipeline with Boogie.ExecutionEngine
// (DD-001). OQ-004 (resolved during RED): the named DafnyPipeline consumption
// on the verification path is the DafnyStandardLibraries.doo embedded
// resource — with --standard-libraries=true, Compilation adds
// dllresource://DafnyPipeline/DafnyStandardLibraries.doo to the file set
// (dafny v4.11.0 Source/DafnyCore/Pipeline/Compilation.cs:181-203 via
// Source/DafnyCore/DafnyMain.cs:21-28) and DafnyFile.HandleDooFile loads it via
// Assembly.Load("DafnyPipeline") + GetManifestResourceStream
// (Source/DafnyCore/DafnyFile.cs:214-222). fixtures/stdlib-anchor.dfy exercises
// this; the removal/differential test proves the consumption matters.
// Public surface uses contract types ONLY (INV-008/RS-019b).

using Corrected.Spike.Contracts;

namespace Corrected.Spike.RouteB;

public sealed class RouteBAdapter : ISpikeRouteAdapter
{
    public string RouteId => "B";

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
