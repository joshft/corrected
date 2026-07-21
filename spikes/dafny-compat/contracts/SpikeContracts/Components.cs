// STUB:TDD — RED-phase component stubs. Every method body throws
// NotImplementedException; GREEN supplies behavior. No implementation logic
// may appear here during RED (workflow gate).

namespace Corrected.Spike.Contracts;

/// <summary>
/// The spec-owned probe manifest, loaded from the committed versioned file
/// (manifest/probe-manifest.json). The manifest file's SHA-256 is hard-coded as
/// a TEST-SUITE constant (RS-002) and verified against this load.
/// </summary>
public sealed class ProbeManifest
{
    public int ManifestVersion { get; init; }
    public IReadOnlyList<string> MandatoryRoutes { get; init; } = Array.Empty<string>();
    public IReadOnlyList<ManifestEntry> Entries { get; init; } = Array.Empty<ManifestEntry>();

    public static ProbeManifest Load(string manifestPath)
    {
        // STUB:TDD
        throw new NotImplementedException("STUB:TDD — GREEN phase implements manifest loading.");
    }

    public static string ComputeSha256(string manifestPath)
    {
        // STUB:TDD
        throw new NotImplementedException("STUB:TDD");
    }
}

/// <summary>
/// INV-006: the named aggregator component. Derives its expected report set
/// from the committed manifest's route plan (never from reports found on
/// disk), owns the shared probes (P02, P04), binds every consumed report to
/// the current run_id, and computes fail-closed per-route verdicts.
/// </summary>
public static class VerdictAggregator
{
    /// <summary>
    /// INV-009 (codex R3-6): compiled-in trust anchor for the evidence schema
    /// digest, independent of the on-disk schema file. Validated against the
    /// committed schema BEFORE parsing or projecting any report.
    /// </summary>
    public const string EvidenceSchemaSha256TrustAnchor =
        "a630b1aa10294b688867ee0cd73574f7c12c15050a2724245b43b3e8b4650259"; // = SHA-256 of schema/evidence-schema.json (registry row v1; TA-B15 + TA-B16/TA-A15 in-place RED amendments).

    public static RouteOutcome ComputeRouteVerdict(
        ProbeManifest manifest,
        string route,
        IReadOnlyCollection<ProbeResult> completedSet,
        SuiteStatus finalSuiteStatus)
    {
        // STUB:TDD
        throw new NotImplementedException("STUB:TDD — GREEN phase implements fail-closed verdict computation.");
    }

    public static RunResult Aggregate(
        ProbeManifest manifest,
        string runId,
        IReadOnlyList<RouteReport> reportsFromPerformedLaunches,
        SuiteStatus finalSuiteStatus)
    {
        // STUB:TDD
        throw new NotImplementedException("STUB:TDD");
    }

    /// <summary>Rejects duplicate composite keys BEFORE dictionary conversion (route plan rule).</summary>
    public static IReadOnlyDictionary<ProbeKey, ProbeResult> ToKeyedResults(IReadOnlyList<ProbeResult> raw)
    {
        // STUB:TDD
        throw new NotImplementedException("STUB:TDD");
    }
}

/// <summary>INV-009: evidence schema validation and the three-way deterministic projection.</summary>
public static class EvidenceSchema
{
    /// <summary>Recomputes and validates the committed schema's digest before any report parse; rejects mismatch and version reuse.</summary>
    public static void ValidateSchemaFile(string schemaPath, string registryPath)
    {
        // STUB:TDD
        throw new NotImplementedException("STUB:TDD");
    }

    /// <summary>Validates a report against the committed schema (closed, additionalProperties: false recursively).</summary>
    public static void ValidateReport(string reportJson, string schemaPath)
    {
        // STUB:TDD
        throw new NotImplementedException("STUB:TDD");
    }

    /// <summary>
    /// Builds the class-2 deterministic projection. Fails if any instance field
    /// lacks exactly one classification; canonicalizes every run-root-dependent
    /// path to the fixed token <run-root>; the run_id value must appear NOWHERE
    /// in the projection (codex R2-1).
    /// </summary>
    public static string DeterministicProjection(string reportJson, string schemaPath)
    {
        // STUB:TDD
        throw new NotImplementedException("STUB:TDD");
    }
}

/// <summary>INV-002/P03: pass-predicate evaluation for loaded-assembly + runtime identity.</summary>
public static class P03Evaluator
{
    public static ProbeStatus Evaluate(P03Evidence evidence, ExpectedLoadedSet expected)
    {
        // STUB:TDD
        throw new NotImplementedException("STUB:TDD");
    }
}

/// <summary>
/// INV-013: the mechanical adjudication state machine. Terminal transitions
/// each require a schema-valid adjudication record; harness faults terminate at
/// INCOMPLETE, never any INCOMPATIBLE.
/// </summary>
public static class AdjudicationStateMachine
{
    public static RouteOutcome ClassifyEnvironmentFault(IncompleteCause cause, string detail)
    {
        // STUB:TDD
        throw new NotImplementedException("STUB:TDD");
    }

    public static RouteOutcome EnterInProcessFailure(UnadjudicatedInProcessFailure payload)
    {
        // STUB:TDD
        throw new NotImplementedException("STUB:TDD");
    }

    public static RouteOutcome TerminalTransition(UnadjudicatedInProcessFailure payload, AdjudicationRecord record)
    {
        // STUB:TDD
        throw new NotImplementedException("STUB:TDD");
    }

    /// <summary>Maps a child-process exit (code or signal) + report presence to the committed exit/report consistency table.</summary>
    public static RouteOutcome MapExit(int? exitCode, string? signal, RouteReport? report)
    {
        // STUB:TDD
        throw new NotImplementedException("STUB:TDD");
    }
}

/// <summary>
/// INV-013 (PAT-004): the ADR linter — a mechanism that runs. Operates on
/// mandatory machine-readable fields (boundary_decision, route,
/// adjudication_record_id) and validates positive selection claims as well as
/// rejection claims against schema-valid terminal adjudication records.
/// </summary>
public static class AdrLinter
{
    public static IReadOnlyList<string> Lint(string adrPath, IReadOnlyList<AdjudicationRecord> records)
    {
        // STUB:TDD
        throw new NotImplementedException("STUB:TDD");
    }
}

/// <summary>
/// INV-003/RS-003c: ONE shared, unit-tested function builds the solver-path
/// option for every probe (sentinel and real runs alike), narrowing the
/// sentinel→real-run composition gap.
/// </summary>
public static class SolverPathOptionBuilder
{
    public static IReadOnlyList<string> Build(string solverAbsolutePath)
    {
        // STUB:TDD
        throw new NotImplementedException("STUB:TDD");
    }
}

/// <summary>
/// PRH-004 layer 2: the managed launcher for post-build processes — route
/// harness executables, the net8 control host, the provisioned z3 identity
/// check, and the sentinel stub. The only sanctioned Process.Start site in the
/// spike (source-scan enforced). Honors LaunchRequest.KillAfterMs by delivering
/// SIGKILL externally (TA-A9).
/// </summary>
public static class ManagedLauncher
{
    public static LaunchResult Launch(LaunchRequest request)
    {
        // STUB:TDD
        throw new NotImplementedException("STUB:TDD — GREEN phase implements the allowlisted child-process launcher.");
    }
}

/// <summary>
/// INV-013/BND-004 (codex R2-2, TA-B12): control-equivalence proof — digest
/// equality of normalized source, resolved package graph, options, fixtures,
/// and compiler inputs between the main asset and its control counterpart.
/// </summary>
public static class ControlEquivalence
{
    /// <summary>Returns normally when every keyed digest matches; throws a typed mismatch (never a silent bool) naming the first divergent key otherwise.</summary>
    public static void Prove(
        IReadOnlyDictionary<string, string> mainInputDigests,
        IReadOnlyDictionary<string, string> controlInputDigests)
    {
        // STUB:TDD
        throw new NotImplementedException("STUB:TDD");
    }
}
