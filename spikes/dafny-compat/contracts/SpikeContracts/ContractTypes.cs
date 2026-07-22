// STUB:TDD — RED-phase contracts. Type definitions and constants only; all
// behavior lives in Components.cs stubs until GREEN.
//
// INV-008: this assembly is Dafny-free. Result/evidence types, the route-seam
// interface, and the verdict-aggregation component contract live here so the
// test project can reference ONLY this assembly.

namespace Corrected.Spike.Contracts;

/// <summary>Composite probe key (probeID, route) — AP-006/RS-010. Route is "A", "B", or "shared".</summary>
public readonly record struct ProbeKey(string ProbeId, string Route)
{
    public override string ToString() => $"{ProbeId}({Route})";
}

public enum ProbeStatus
{
    Pass,
    Fail,
    Incomplete,
}

/// <summary>One completed probe instance in a route/shared report.</summary>
public sealed record ProbeResult(ProbeKey Key, ProbeStatus Status, string? Detail = null);

/// <summary>Route-outcome state enum — the committed route-outcome algebra (INV-013, codex R3-4/R4-01).</summary>
public enum RouteState
{
    Compatible,
    Incomplete,
    Incompatible,
    UpstreamDefect,
    UnadjudicatedInProcessFailure,
}

/// <summary>INCOMPATIBLE terminal classes (INV-013). Reachable only via schema-valid adjudication records.</summary>
public enum IncompatibleClass
{
    HostRuntimeIncompatibility,
    TargetFrameworkIncompatibility,
    OfficialApiCapabilityGap,
}

/// <summary>Typed INCOMPLETE causes (INV-006/INV-013).</summary>
public enum IncompleteCause
{
    PrerequisiteFailure,
    SuiteFailure,
    ExitReportMismatch,
    MissingReport,
    MalformedReport,
    Crash,
    HarnessFault,
    WallClockExpiry,
    UnsupportedRid,
}

/// <summary>Variant payload discriminator for UNADJUDICATED_IN_PROCESS_FAILURE (codex R2-3).</summary>
public enum UnadjudicatedVariant
{
    TypedException,
    UnexpectedTypedResult,
    AbsentRequiredCapability,
}

/// <summary>
/// Discriminated verdict_reason union (INV-013, codex R2-5/R3-4). Every
/// non-COMPATIBLE run-level report carries exactly one of these with
/// variant-specific required fields.
/// </summary>
public abstract record VerdictReason
{
    /// <summary>Probe selected by MANIFEST order, never completion order.</summary>
    public sealed record ProbeFailure(ProbeKey FirstFailingByManifestOrder) : VerdictReason;
    public sealed record PrerequisiteFailure(IncompleteCause Cause, string Detail) : VerdictReason;
    /// <summary>Missing reports carry their own variant — they do not collapse into crash and have no "first failing probe".</summary>
    public sealed record MissingReport(string Route) : VerdictReason;
    public sealed record MalformedReport(string Route, string Detail) : VerdictReason;
    /// <summary>Any unknown exit code or signal death maps here (codex F7).</summary>
    public sealed record Crash(int? ExitCode, string? Signal) : VerdictReason;
    public sealed record ExitReportMismatch(int ExitCode, string ReportSummary) : VerdictReason;
    public sealed record SuiteFailure(string Detail) : VerdictReason;
    public sealed record UnadjudicatedFailure(UnadjudicatedInProcessFailure Payload) : VerdictReason;
    /// <summary>Terminal adjudicated classes — require a schema-valid adjudication record.</summary>
    public sealed record Adjudicated(AdjudicationRecord Record) : VerdictReason;
}

/// <summary>
/// UNADJUDICATED_IN_PROCESS_FAILURE payload (INV-013). Every record must embed
/// the failing probe's typed diagnostic plus that run's P01–P04 identity evidence.
/// </summary>
public sealed record UnadjudicatedInProcessFailure(
    UnadjudicatedVariant Variant,
    ProbeKey Probe,
    string Stage,
    string MinimalInputs,
    string? TypedDiagnostic,
    IdentityEvidence? IdentityEvidence);

/// <summary>P01–P04 identity evidence snapshot embedded in unadjudicated-failure records.</summary>
public sealed record IdentityEvidence(
    string RunId,
    string? RestoreIdentity,
    string? SdkBuildIdentity,
    string? LoadedAssemblyIdentity,
    string? SolverIdentity);

/// <summary>
/// Schema-valid terminal adjudication record (INV-013). Terminal transitions are
/// mechanical; each requires one of these. The ADR linter validates positive and
/// rejection claims against these records (closed vocabulary, not prose).
/// </summary>
public sealed record AdjudicationRecord(
    string AdjudicationRecordId,
    string Route,
    RouteState TerminalState,
    IncompatibleClass? IncompatibleClass,
    string? SourceCitation,
    string? MinimizedReproducerDigest,
    string? ControlAssetDigest,
    ThreeCellOutcome? ThreeCell);

/// <summary>
/// Three-cell control experiment outcome (INV-013, codex R3-2/R4-04):
/// (i) net10 asset on pinned net10 host, (ii) net8 control asset on pinned net8
/// host, (iii) same net8 bits on pinned net10 host.
/// </summary>
public sealed record ThreeCellOutcome(
    ProbeStatus Net10AssetOnNet10Host,
    ProbeStatus Net8AssetOnNet8Host,
    ProbeStatus Net8AssetOnNet10Host,
    string? Net10CellFailureFingerprint,
    string? Net8BitsOnNet10FailureFingerprint,
    string? MinimizedReproducerDigestNet10Cell,
    string? MinimizedReproducerDigestNet8BitsCell);

/// <summary>Per-route outcome with its discriminated reason.</summary>
public sealed record RouteOutcome(RouteState State, IncompatibleClass? Class, VerdictReason? Reason);

/// <summary>Final test-suite exit status — an INPUT to the verdict formula (codex R4-02), not a footnote.</summary>
public enum SuiteStatus
{
    Success,
    Failure,
    Unknown,
}

/// <summary>A route child's (or shared) evidence report as consumed by the aggregator.</summary>
public sealed record RouteReport(
    string RunId,
    string Route,
    IReadOnlyList<ProbeResult> Probes,
    int ExitCode,
    string? Signal,
    string ReportPath);

/// <summary>Run-level aggregation result (INV-006): per-route verdicts, never a bare overall COMPATIBLE.</summary>
public sealed record RunResult(
    string RunId,
    IReadOnlyDictionary<string, RouteOutcome> RouteVerdicts,
    SuiteStatus FinalSuiteStatus);

/// <summary>Committed probe-manifest model (spec-owned; INV-006 binds to it).</summary>
public sealed record ManifestEntry(string ProbeId, string Route, string Owner);

/// <summary>
/// Exit-code contract (INV-013; committed in README.md). Route children exit
/// with distinct codes; any unknown exit code or signal death maps to the crash
/// variant; consumers never read any of these as pass or refutation.
/// </summary>
public static class ExitCodes
{
    public const int RouteProbesPassed = 0;
    public const int ProbeFailure = 10;
    public const int Incomplete = 20;
}

/// <summary>
/// Solver layout constants (INV-003/RS-003a). The provisioned z3 lives OUTSIDE
/// every ambient discovery location — in particular NOT at the
/// assembly-adjacent path {output}/z3/bin/z3-4.12.1 that Dafny's fallback
/// discovery searches (dafny v4.11.0 Source/DafnyCore/DafnyOptions.cs:1206-1217).
/// </summary>
public static class SolverLayout
{
    public const string Z3Version = "4.12.1";
    public const string Z3PinnedSha256 = "c5360fd157b0f861ec8780ba3e51e2197e9486798dc93cd878df69a4b0c2b7c5";
    /// <summary>Canonical run-root token for evidence (codex R2-1).</summary>
    public const string RunRootToken = "<run-root>";
    /// <summary>Install path relative to the run root — not assembly-adjacent, not on PATH.</summary>
    public const string SolverRelativePath = "solver/z3-4.12.1/bin/z3";
    /// <summary>The ambient fallback path Dafny searches next to its assemblies; provisioning must never install here.</summary>
    public const string ProhibitedAssemblyAdjacentRelativePath = "z3/bin/z3-4.12.1";
}

/// <summary>Typed result of a managed-launcher child process run (PRH-004 layer 2).</summary>
public sealed record LaunchResult(int? ExitCode, string? Signal, string StdOut, string StdErr, double DurationMs);

/// <summary>
/// Structured launch request — argv as an array, never string-interpolated (AP-008).
/// KillAfterMs (TA-A9): when set, the LAUNCHER (not the SUT) delivers SIGKILL to
/// the child after the delay — external fault injection for the atomic-emission
/// test; the child never learns it is under test.
/// </summary>
public sealed record LaunchRequest(
    string ExecutablePath,
    IReadOnlyList<string> Argv,
    string WorkingDirectory,
    IReadOnlyDictionary<string, string> EnvironmentProfile,
    double TimeoutSeconds,
    double? KillAfterMs = null);

/// <summary>
/// Committed run-root layout + run-context contract (DD-008, TA-B13/TA-B1).
/// The bootstrap controller exports SPIKE_RUN_CONTEXT (path to the run-context
/// receipt JSON) before invoking `dotnet test`; integration tests resolve every
/// launched artifact through that receipt and verify its digest test-side.
/// Tests launched outside a controller run FAIL LOUDLY — repo-local bin/Debug
/// artifacts are never launched (DD-008 stale-artifact rule).
/// Receipt shape: { "run_id", "run_root", "artifacts": { "<name>":
/// { "path", "sha256" } } } with paths relative to run_root.
/// </summary>
public static class RunLayout
{
    public const string RunContextEnvVar = "SPIKE_RUN_CONTEXT";
    public const string RunContextFileName = "run-context.json";
    /// <summary>Sentinel nonce ledger — pre-created at count zero BEFORE the run (RS-003d); tests read this FILE, never a report echo.</summary>
    public const string SentinelLedgerRelativePath = "sentinel/ledger.json";
    public const string ReceiptsDirRelativePath = "receipts";
    /// <summary>Aggregation receipt: run_id + consumed_report_paths (from performed launches only — RS-010/TA-A4).</summary>
    public const string AggregationReceiptRelativePath = "receipts/aggregation.json";
    /// <summary>Environment audit receipt: the constructed per-launch environments actually applied (EA-008/TA-B9).</summary>
    public const string EnvAuditReceiptRelativePath = "receipts/env-audit.json";
    public const string BuildReceiptRelativePath = "receipts/build-receipt.json";
    /// <summary>
    /// MA-VI-6: nonce-bound SUITE receipt — the controller emits this
    /// (atomically) after the test phase of a canonical run; the aggregator
    /// DERIVES final_suite_status from it and treats --suite-status as at most
    /// a cross-check, refusing on mismatch. COORDINATION CONTRACT (shell
    /// agent): emit JSON at this path with fields
    ///   { "run_id": &lt;SPIKE_RUN_ID&gt;, "nonce": &lt;SPIKE_NONCE&gt;,
    ///     "suite_exit": &lt;int dotnet-test exit&gt;,
    ///     "source_manifest_sha256": &lt;SRC_DIGEST or "unknown"&gt; }
    /// via write-temp-then-mv, ONLY when the suite phase actually ran
    /// (canonical runs). Variance runs emit nothing (final_suite_status stays
    /// unknown). Derivation: suite_exit==0 → success, else failure.
    /// </summary>
    public const string SuiteReceiptRelativePath = "receipts/suite-receipt.json";
    public const string ReportsDirRelativePath = "reports";
    /// <summary>Committed per-launch environment profiles (EA-008/TA-B11).</summary>
    public const string EnvProfilesRelativePath = "config/env-profiles.json";
}

/// <summary>P03 runtime-identity evidence (INV-002, codex R4-03).</summary>
public sealed record P03Evidence(
    string Route,
    string HarnessTargetFramework,
    int RuntimeMajorVersion,
    string RuntimeConfigFrameworkName,
    string RuntimeConfigRollForward,
    string HostfxrPath,
    string HostfxrSha256,
    string CoreLibPath,
    string CoreLibSha256,
    IReadOnlyList<LoadedAssemblyIdentity> LoadedAssemblies);

/// <summary>Loaded-assembly identity: simple name, mapped package version (build metadata stripped per RS-008), file SHA-256.</summary>
public sealed record LoadedAssemblyIdentity(string SimpleName, string? MappedPackageVersion, string FilePath, string FileSha256);

/// <summary>Committed per-route expected-loaded-assembly set (INV-002, codex F8).</summary>
public sealed record ExpectedLoadedSet(
    string Route,
    string Universe,
    IReadOnlyList<string> Anchors,
    IReadOnlyList<LoadedAssemblyIdentity> Assemblies);

/// <summary>
/// Route-seam interface (INV-008). Realized by SpikeDafnyAdapter.RouteA and
/// SpikeDafnyAdapter.RouteB; every signature uses contract types only — the
/// metadata-only API-surface test enforces that nothing here (recursively)
/// originates from Dafny/Boogie assemblies (RS-019b).
/// </summary>
public interface ISpikeRouteAdapter
{
    string RouteId { get; }
    VerificationRun Verify(FixtureInput input);
    NodeTable RecoverAst(FixtureInput input);
    OptionReadback ReadOptionsPostVerification(FixtureInput input);
    ClosureResult RecoverClosure(FixtureInput input, bool verifyIncludedFiles);
    HygieneReport WalkFixtureHygiene(FixtureInput input);
}

/// <summary>Fixture input handed to a seam (repo-relative path + effective option set).</summary>
public sealed record FixtureInput(string RepoRelativePath, IReadOnlyDictionary<string, string?> Options);

public enum VerificationStage
{
    Parse,
    Resolution,
    Verification,
}

/// <summary>Solver-produced outcome enum recovered from typed Boogie/Dafny result objects (INV-004/RS-004a).</summary>
public enum SolverOutcome
{
    Valid,
    Invalid,
    TimedOut,
    OutOfResource,
    Undetermined,
}

public sealed record TypedDiagnostic(VerificationStage Stage, string File, int Line, int? EndLine, string Message);

public sealed record VerificationTask(string TargetName, SolverOutcome Outcome, bool SolverResourceUsageObserved);

public sealed record VerificationRun(
    VerificationStage CompletedThroughStage,
    IReadOnlyList<string> TargetNames,
    IReadOnlyList<VerificationTask> Tasks,
    IReadOnlyList<TypedDiagnostic> Diagnostics,
    int SentinelInvocationCount,
    string EffectiveSolverPath);

/// <summary>Resolved-AST node table (INV-007). Classification read from resolved-AST properties, never surface syntax.</summary>
public sealed record NodeTable(
    IReadOnlyList<DeclarationRow> Declarations,
    IReadOnlyList<NodeFormRow> Nodes);

public sealed record DeclarationRow(string Name, string Kind, bool Ghost, bool InferredGhost, bool ExplicitGhostKeyword);

public sealed record NodeFormRow(string Form, string Classification, int Count);

/// <summary>P11 options readback with the normalization canary (OQ-003).</summary>
public sealed record OptionReadback(
    IReadOnlyDictionary<string, string?> RequestedOptions,
    IReadOnlyDictionary<string, string?> EffectiveOptions,
    NormalizationCanary Canary);

/// <summary>
/// OQ-003 canary contract (resolved during RED; both routes): option id
/// --solver-path. Naive readback of Compilation.Options is input echo by API
/// design (Compilation.Options aliases Input.Options — dafny v4.11.0
/// Source/DafnyCore/Pipeline/Compilation.cs:56). The concrete source-verified
/// transformation: TextDocumentLoader calls Options.ProcessSolverOptions
/// (Source/DafnyCore/Pipeline/TextDocumentLoader.cs:58-59 →
/// Source/DafnyCore/DafnyOptions.cs:941-946), which EXECUTES the configured
/// solver binary (GetZ3Version, DafnyOptions.cs:1241-1263) and mutates the same
/// aliased options object: SolverIdentifier="Z3", SolverVersion=<banner
/// version>, and ProverOptions gains O:auto_config=false,
/// O:smt.qi.eager_threshold=44, ... (SetZ3Options/SetZ3Option,
/// DafnyOptions.cs:1266-1305). The --solver-path binding also rewrites
/// ProverOptions to PROVER_PATH=<FullName> (absolute-path expansion —
/// Source/DafnyCore/Options/BoogieOptionBag.cs:121-126), canonicalized to
/// <run-root> in evidence. Behavioral differential: the same requested option
/// value pointed at a stub whose banner reports a different version yields a
/// different SolverVersion readback.
/// </summary>
public sealed record NormalizationCanary(
    string OptionId,
    string RequestedValue,
    string EffectiveProverPathEntry,
    string? SolverIdentifier,
    string? SolverVersion,
    IReadOnlyList<string> AddedProverOptions);

/// <summary>P12 closure recovery result (INV-012): parsed/resolved closure distinguished from verification targets.</summary>
public sealed record ClosureResult(
    IReadOnlyList<string> ClosureRepoRelative,
    IReadOnlyList<string> VerificationTargets,
    bool VerifyIncludedFilesOptionValue);

/// <summary>AST-based fixture-hygiene walk result (INV-004/RS-009).</summary>
public sealed record HygieneReport(
    bool AllDeclarationsBodied,
    bool ContainsAttributes,
    bool ContainsAssumeStatements,
    bool ContainsVerificationSuppression);

/// <summary>Sentinel-solver nonce ledger row (INV-003/RS-003d): pre-created, nonce-bound, initialized to zero.</summary>
public sealed record SentinelLedger(string Nonce, int InvocationCount, IReadOnlyList<string> RecordedArgv);

/// <summary>One decoded append-only sentinel ledger entry (MA-RB-3/MA-HI-2): per-writer-unique file, base64 fields round-tripping arbitrary argv bytes.</summary>
public sealed record SentinelLedgerEntry(string Nonce, string ProbeTag, IReadOnlyList<string> Argv);

/// <summary>
/// EA-008: committed per-launch environment profiles — environment is
/// constructed, not sanitized by prohibition. Values referencing roots use
/// root-kind + relative path serialization in committed evidence (codex R3-9).
/// </summary>
public static class EnvironmentProfiles
{
    public static readonly IReadOnlyList<string> ProfileKeys = new[]
    {
        "PATH",
        "DOTNET_CLI_HOME",
        "NUGET_PACKAGES",
        "TMPDIR",
        "LC_ALL",
        "LANG",
        "SSL_CERT_FILE",
        "SSL_CERT_DIR",
        "DOTNET_CLI_TELEMETRY_OPTOUT",
        "MSBUILDDISABLENODEREUSE",
        "UseSharedCompilation",
    };

    /// <summary>Documented SDK-injected exceptions, enumerated rather than contradicting the only-profile-keys rule.</summary>
    public static readonly IReadOnlyList<string> DocumentedSdkInjectedKeys = new[]
    {
        "DOTNET_HOST_PATH",
        "DOTNET_ROOT",
    };
}

/// <summary>
/// PRH-004 layer 1: bootstrap-controller direct-command allowlist. The static
/// test compares scripts/run-spike.sh's direct command set against this list.
/// </summary>
public static class BootstrapAllowlist
{
    // ADJUDICATION NOTE (MA-XC-3): `rm` is a recorded widening of the
    // spec-enumerated PRH-004 layer-1 list, adjudicated with QA-014 (run-root
    // housekeeping: provisioning scratch/.corrupt/.download cleanup). The
    // spec-side record of this deviation is the shell/operator layer's
    // obligation (ADR-0001 residual list); this constant is the enforcement
    // copy the static test certifies. Any further widening needs its own
    // adjudication record — the three-point-change discipline applies.
    public static readonly IReadOnlyList<string> Commands = new[]
    {
        "bash", "dotnet", "curl", "sha256sum", "tar", "unzip", "git",
        "mkdir", "mktemp", "mv", "rm", "chmod", "setsid", "kill", "sleep",
    };
}
