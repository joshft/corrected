// Tests INV-013 / BND-004: verdict taxonomy — INCOMPATIBLE requires
// adjudication; harness bugs and environment faults are not integration failures.
using System.Text.Json;
using System.Text.RegularExpressions;
using Corrected.Spike.Contracts;
using Xunit;

namespace Corrected.Spike.Tests;

public class Inv013AdjudicationTests
{
    private static UnadjudicatedInProcessFailure Payload(UnadjudicatedVariant variant = UnadjudicatedVariant.TypedException) => new(
        variant,
        new ProbeKey("P06", "A"),
        Stage: "verification",
        MinimalInputs: "fixtures/ok.dfy",
        TypedDiagnostic: "System.MissingMethodException: X",
        IdentityEvidence: new IdentityEvidence("run-1", "p01-ok", "p02-ok", "p03-ok", "p04-ok"));

    // Tests INV-013 [unit] (codex R3-4/R4-01): the COMPLETE route-outcome
    // algebra is committed in the evidence schema — state enum, reason union,
    // transition table, exit/report matrix, precedence order, UPSTREAM_DEFECT
    // semantics.
    [Fact]
    public void RouteOutcomeAlgebra_CommittedCompletely_InEvidenceSchema()
    {
        using var doc = SpikePaths.Json(SpikePaths.P("schema", "evidence-schema.json"));
        var algebra = doc.RootElement.GetProperty("route_outcome_algebra");

        var states = algebra.GetProperty("route_states").EnumerateArray().Select(s => s.GetString()!).ToList();
        foreach (var required in new[]
        {
            "COMPATIBLE", "INCOMPLETE",
            "INCOMPATIBLE(HOST_RUNTIME_INCOMPATIBILITY)",
            "INCOMPATIBLE(TARGET_FRAMEWORK_INCOMPATIBILITY)",
            "INCOMPATIBLE(OFFICIAL_API_CAPABILITY_GAP)",
            "UPSTREAM_DEFECT", "UNADJUDICATED_IN_PROCESS_FAILURE",
        })
        {
            Assert.Contains(required, states);
        }

        var union = algebra.GetProperty("verdict_reason_union");
        foreach (var variant in new[]
        {
            "probe-failure", "prerequisite-failure", "missing-report", "malformed-report",
            "crash", "exit-report-mismatch", "unadjudicated-failure", "adjudicated",
        })
        {
            Assert.True(union.TryGetProperty(variant, out var v), $"reason union missing variant {variant}");
            Assert.NotEmpty(v.GetProperty("required_fields").EnumerateArray().ToList());
        }

        Assert.True(algebra.GetProperty("transition_table").GetArrayLength() >= 7);
        Assert.True(algebra.GetProperty("exit_report_matrix").GetArrayLength() >= 9);
        Assert.NotEmpty(algebra.GetProperty("precedence_order_for_multi_match").EnumerateArray().ToList());
        Assert.Contains("non-boundary-rejection", algebra.GetProperty("upstream_defect_semantics").GetString());
        // OFFICIAL_API_CAPABILITY_GAP reachable ONLY via the source-verified
        // terminal transition (codex R4-01). TEST_BUG fix #1: not every
        // transition row carries a "note" — probe with TryGetProperty; the
        // sole-path note must still EXIST on some row.
        Assert.Contains(algebra.GetProperty("transition_table").EnumerateArray(),
            row => row.TryGetProperty("note", out var n)
                && (n.GetString() ?? "").Contains("ONLY path to OFFICIAL_API_CAPABILITY_GAP"));
    }

    // Tests INV-013 [unit]: environment/prerequisite faults yield INCOMPLETE
    // with a typed cause — never any INCOMPATIBLE.
    [Theory]
    [InlineData(IncompleteCause.PrerequisiteFailure)]
    [InlineData(IncompleteCause.WallClockExpiry)]
    [InlineData(IncompleteCause.UnsupportedRid)]
    public void EnvironmentFault_YieldsIncompleteWithTypedCause_NeverIncompatible(IncompleteCause cause)
    {
        var outcome = AdjudicationStateMachine.ClassifyEnvironmentFault(cause, "absent z3");
        Assert.Equal(RouteState.Incomplete, outcome.State);
        Assert.Null(outcome.Class);
        var reason = Assert.IsType<VerdictReason.PrerequisiteFailure>(outcome.Reason);
        Assert.Equal(cause, reason.Cause);
    }

    // Tests INV-013 [unit] (codex R2-3): in-process failures enter the state
    // machine as UNADJUDICATED_IN_PROCESS_FAILURE with the variant payload —
    // typed exception | unexpected typed result | absent required capability.
    [Theory]
    [InlineData(UnadjudicatedVariant.TypedException)]
    [InlineData(UnadjudicatedVariant.UnexpectedTypedResult)]
    [InlineData(UnadjudicatedVariant.AbsentRequiredCapability)]
    public void InProcessFailure_EntersUnadjudicated_WithVariantPayload(UnadjudicatedVariant variant)
    {
        var outcome = AdjudicationStateMachine.EnterInProcessFailure(Payload(variant));
        Assert.Equal(RouteState.UnadjudicatedInProcessFailure, outcome.State);
        var reason = Assert.IsType<VerdictReason.UnadjudicatedFailure>(outcome.Reason);
        Assert.Equal(variant, reason.Payload.Variant);
        Assert.NotNull(reason.Payload.IdentityEvidence);
    }

    // Tests INV-013 [unit]: harness fault terminal transition → INCOMPLETE,
    // never an INCOMPATIBLE of any kind; requires a schema-valid record.
    [Fact]
    public void HarnessFault_TerminalTransition_IsIncomplete_NeverIncompatible()
    {
        var record = new AdjudicationRecord("adj-1", "A", RouteState.Incomplete, null,
            SourceCitation: null, MinimizedReproducerDigest: null, ControlAssetDigest: null, ThreeCell: null);
        var outcome = AdjudicationStateMachine.TerminalTransition(Payload(), record);
        Assert.Equal(RouteState.Incomplete, outcome.State);
        Assert.Null(outcome.Class);
    }

    // Tests INV-013/RS-004c [unit] (codex R4-01): OFFICIAL_API_CAPABILITY_GAP is
    // reachable ONLY through the source-verified terminal transition — a record
    // without a source citation must be rejected, never a direct classification.
    [Fact]
    public void CapabilityGap_RequiresSourceVerifiedRecord()
    {
        var withCitation = new AdjudicationRecord("adj-2", "A", RouteState.Incompatible,
            IncompatibleClass.OfficialApiCapabilityGap,
            SourceCitation: "dafny v4.11.0 Source/DafnyCore/DafnyOptions.cs:941",
            MinimizedReproducerDigest: new string('c', 64), ControlAssetDigest: null, ThreeCell: null);
        var ok = AdjudicationStateMachine.TerminalTransition(Payload(UnadjudicatedVariant.AbsentRequiredCapability), withCitation);
        Assert.Equal(RouteState.Incompatible, ok.State);
        Assert.Equal(IncompatibleClass.OfficialApiCapabilityGap, ok.Class);

        var withoutCitation = withCitation with { SourceCitation = null, AdjudicationRecordId = "adj-3" };
        var ex = Record.Exception(() =>
            AdjudicationStateMachine.TerminalTransition(Payload(UnadjudicatedVariant.AbsentRequiredCapability), withoutCitation));
        Assert.NotNull(ex);
        Assert.IsNotType<NotImplementedException>(ex); // a stub throw is not schema-valid-record enforcement (AP-010)
    }

    // Tests INV-013 [unit] (codex R3-2/R4-04): three-cell adjudication —
    // HOST_RUNTIME_INCOMPATIBILITY only when BOTH net10-host failure cells share
    // the minimized reproducer and an equivalent typed failure fingerprint.
    [Fact]
    public void ThreeCell_HostRuntime_RequiresMatchingFingerprintsAcrossNet10Cells()
    {
        var matching = new ThreeCellOutcome(
            Net10AssetOnNet10Host: ProbeStatus.Fail,
            Net8AssetOnNet8Host: ProbeStatus.Pass,
            Net8AssetOnNet10Host: ProbeStatus.Fail,
            Net10CellFailureFingerprint: "fp-MissingMethod-X",
            Net8BitsOnNet10FailureFingerprint: "fp-MissingMethod-X",
            MinimizedReproducerDigestNet10Cell: new string('d', 64),
            MinimizedReproducerDigestNet8BitsCell: new string('d', 64));
        var record = new AdjudicationRecord("adj-4", "A", RouteState.Incompatible,
            IncompatibleClass.HostRuntimeIncompatibility, "upstream cross-check", new string('d', 64), new string('e', 64), matching);
        var outcome = AdjudicationStateMachine.TerminalTransition(Payload(), record);
        Assert.Equal(IncompatibleClass.HostRuntimeIncompatibility, outcome.Class);

        // UNRELATED failures across cells remain unadjudicated (codex R4-04).
        var unrelated = matching with { Net8BitsOnNet10FailureFingerprint = "fp-DIFFERENT" };
        var badRecord = record with { AdjudicationRecordId = "adj-5", ThreeCell = unrelated };
        var still = AdjudicationStateMachine.TerminalTransition(Payload(), badRecord);
        Assert.Equal(RouteState.UnadjudicatedInProcessFailure, still.State);
    }

    // Tests INV-013 [unit] (codex R3-2): net8-bits-pass-on-net10 but
    // net10-target-fail → TARGET_FRAMEWORK_INCOMPATIBILITY, a DISTINCT class.
    [Fact]
    public void ThreeCell_TargetFrameworkIncompatibility_IsDistinctClass()
    {
        var cells = new ThreeCellOutcome(
            Net10AssetOnNet10Host: ProbeStatus.Fail,
            Net8AssetOnNet8Host: ProbeStatus.Pass,
            Net8AssetOnNet10Host: ProbeStatus.Pass,
            Net10CellFailureFingerprint: "fp-X",
            Net8BitsOnNet10FailureFingerprint: null,
            MinimizedReproducerDigestNet10Cell: new string('d', 64),
            MinimizedReproducerDigestNet8BitsCell: null);
        var record = new AdjudicationRecord("adj-6", "A", RouteState.Incompatible,
            IncompatibleClass.TargetFrameworkIncompatibility, "upstream cross-check", new string('d', 64), new string('e', 64), cells);
        var outcome = AdjudicationStateMachine.TerminalTransition(Payload(), record);
        Assert.Equal(IncompatibleClass.TargetFrameworkIncompatibility, outcome.Class);
        Assert.NotEqual(IncompatibleClass.HostRuntimeIncompatibility, outcome.Class);
    }

    // Tests INV-013 [unit] (codex F7): signal death without a report resolves to
    // the crash variant — never ambiguously; unknown exit codes likewise.
    [Fact]
    public void SignalDeathWithoutReport_ResolvesToCrashVariant()
    {
        var outcome = AdjudicationStateMachine.MapExit(exitCode: null, signal: "SIGKILL", report: null);
        Assert.NotNull(outcome); // MA-VI-4: only CONSISTENT cells are null
        Assert.NotEqual(RouteState.Compatible, outcome!.State);
        var reason = Assert.IsType<VerdictReason.Crash>(outcome.Reason);
        Assert.Equal("SIGKILL", reason.Signal);
    }

    [Fact]
    public void UnknownExitCode_MapsToCrashVariant_NeverPassOrRefutation()
    {
        var outcome = AdjudicationStateMachine.MapExit(exitCode: 137, signal: null, report: null);
        Assert.NotNull(outcome); // MA-VI-4: only CONSISTENT cells are null
        Assert.NotEqual(RouteState.Compatible, outcome!.State);
        Assert.IsType<VerdictReason.Crash>(outcome.Reason);
    }

    // Tests INV-013 [unit]: missing/malformed reports carry their OWN variants —
    // they do not collapse into crash and have no "first failing probe".
    [Fact]
    public void MissingReport_HasOwnVariant_NotCrash()
    {
        var outcome = AdjudicationStateMachine.MapExit(exitCode: ExitCodes.RouteProbesPassed, signal: null, report: null);
        Assert.NotNull(outcome); // MA-VI-4: only CONSISTENT cells are null
        Assert.IsType<VerdictReason.MissingReport>(outcome!.Reason);
    }

    // Tests INV-013 [integration]: induced-failure test — every
    // unadjudicated-failure record embeds the failing probe's typed diagnostic
    // plus that run's P01–P04 identity evidence.
    [Fact]
    public void InducedUnadjudicatedFailure_RecordEmbedsTypedDiagnosticAndIdentityEvidence()
    {
        var scratch = SpikePaths.TestScratch("inv013-payload");
        var reportPath = Path.Combine(scratch, "report.json");
        var result = Launch.Harness("A", "--force-probe-exception", "P10", "--out", reportPath);
        Assert.NotEqual(ExitCodes.RouteProbesPassed, result.ExitCode);
        using var doc = Launch.Report(reportPath);
        var records = doc.RootElement.GetProperty("deterministic").GetProperty("adjudication_records").EnumerateArray().ToList();
        var unadj = Assert.Single(records);
        Assert.False(string.IsNullOrEmpty(unadj.GetProperty("typed_diagnostic").GetString()));
        var identity = unadj.GetProperty("identity_evidence_p01_p04");
        foreach (var leg in new[] { "restore_identity", "sdk_build_identity", "loaded_assembly_identity", "solver_identity" })
        {
            Assert.True(identity.TryGetProperty(leg, out var v) && v.ValueKind != JsonValueKind.Null,
                $"unadjudicated-failure record missing identity leg {leg} (INV-013)");
        }
    }

    // Tests INV-013 [unit] (PAT-004): the ADR LINTER is a mechanism that runs —
    // machine-readable fields, rejecting rejection claims AND positive
    // selection claims lacking schema-valid terminal adjudication records.
    [Fact]
    public void AdrLinter_RejectsClaimsWithoutSchemaValidAdjudicationRecords()
    {
        var scratch = SpikePaths.TestScratch("inv013-adr-linter");

        // A rejection-shaped ADR without any adjudication record: findings required.
        var rejecting = Path.Combine(scratch, "adr-rejecting.md");
        File.WriteAllText(rejecting, """
        # ADR-0001 (test copy)
        ```yaml
        adr_lint:
          boundary_decision: rejected
          selected_route: null
          routes:
            - route: A
              verdict: INCOMPATIBLE(HOST_RUNTIME_INCOMPATIBILITY)
              adjudication_record_id: null
              evidence: null
        ```
        """);
        var findings = AdrLinter.Lint(rejecting, Array.Empty<AdjudicationRecord>());
        Assert.NotEmpty(findings);

        // A positive-selection ADR without records: findings required too (codex R4-02).
        var positive = Path.Combine(scratch, "adr-positive.md");
        File.WriteAllText(positive, """
        # ADR-0001 (test copy)
        ```yaml
        adr_lint:
          boundary_decision: in-process-selected
          selected_route: A
          routes:
            - route: A
              verdict: COMPATIBLE
              adjudication_record_id: null
              evidence: null
        ```
        """);
        var positiveFindings = AdrLinter.Lint(positive, Array.Empty<AdjudicationRecord>());
        Assert.NotEmpty(positiveFindings);
    }

    // Tests INV-013 [unit]: the committed provisional ADR carries the mandatory
    // machine-readable field block (closed vocabulary, not prose detection).
    [Fact]
    public void CommittedAdr_CarriesMachineReadableLintBlock()
    {
        var adr = File.ReadAllText(SpikePaths.Repo("docs", "adr", "ADR-0001-dafny-integration-boundary.md"));
        Assert.Contains("adr_lint:", adr);
        Assert.Contains("boundary_decision:", adr);
        Assert.Contains("adjudication_record_id:", adr);
        Assert.Contains("Status**: provisional", adr);
    }

    // Tests BND-004 [unit]: control projects are net8.0, per route, with
    // committed control locks; the pinned control runtime carries an exact
    // patch + RID + URL + SHA-256.
    [Fact]
    public void ControlProjects_Net8PerRoute_WithCommittedLocksAndRuntimePin()
    {
        foreach (var name in new[] { "RouteAControl", "RouteBControl" })
        {
            var doc = SpikePaths.Xml(SpikePaths.CsprojPath(name));
            Assert.Equal("net8.0", doc.Descendants("TargetFramework").Single().Value);
            Assert.True(File.Exists(SpikePaths.LockFilePath(name)), $"missing committed control lock for {name} (BND-004)");
            var lockText = File.ReadAllText(SpikePaths.LockFilePath(name));
            Assert.Contains("net8.0", lockText);
        }

        using var pin = SpikePaths.Json(SpikePaths.P("config", "net8-control-pin.json"));
        Assert.Equal(SpecConstants.Net8ControlRuntimeVersion, pin.RootElement.GetProperty("runtime_version").GetString());
        Assert.Equal("linux-x64", pin.RootElement.GetProperty("rid").GetString());
        Assert.Equal(SpecConstants.Net8ControlArchiveSha256, pin.RootElement.GetProperty("sha256").GetString());
        Assert.Contains("--fx-version 8.0.29", pin.RootElement.GetProperty("note").GetString());
    }

    // Tests BND-004 [unit] (codex R4-04): the net10 CONTROL host is pinned with
    // captured digests — EA-001's floating patch never applies to control cells.
    // Fails in RED by design: null digests force the GREEN capture.
    [Fact]
    public void Net10ControlHost_PinnedWithCapturedDigests()
    {
        using var pin = SpikePaths.Json(SpikePaths.P("config", "net8-control-pin.json"));
        var host = pin.RootElement.GetProperty("net10_control_host");
        Assert.Equal(SpecConstants.SdkPin, host.GetProperty("sdk_version").GetString());
        foreach (var leg in new[] { "hostfxr_expected_digest", "corelib_expected_digest" })
        {
            var digest = host.GetProperty(leg).GetString();
            Assert.False(string.IsNullOrEmpty(digest),
                $"net10 control host {leg} not captured — control cells must run on a PINNED host, never the floating patch (codex R4-04)");
            Assert.Matches("^[0-9a-f]{64}$", digest);
        }
    }

    // Tests BND-004 [unit] (TA-B12): per-file identities of the pinned net8
    // runtime are captured. Fails in RED by design (null placeholders).
    [Fact]
    public void Net8ControlRuntime_PerFileIdentities_Captured()
    {
        using var pin = SpikePaths.Json(SpikePaths.P("config", "net8-control-pin.json"));
        var ids = pin.RootElement.GetProperty("net8_expected_identities");
        Assert.Contains("8.0.29", ids.GetProperty("hostfxr_relative_path").GetString());
        Assert.Contains("8.0.29", ids.GetProperty("corelib_relative_path").GetString());
        foreach (var leg in new[] { "hostfxr_expected_digest", "corelib_expected_digest" })
        {
            var digest = ids.GetProperty(leg).GetString();
            Assert.False(string.IsNullOrEmpty(digest),
                $"net8 control runtime {leg} not captured from the digest-verified archive (TA-B12/BND-004)");
            Assert.Matches("^[0-9a-f]{64}$", digest);
        }
    }

    // Tests INV-013/BND-004 [integration] (TA-B12, codex R3-3): a control cell
    // actually LAUNCHES via the explicit private host argv
    // (<control-root>/dotnet exec --fx-version 8.0.29 ...), and the recorded
    // hostfxr/System.Private.CoreLib identities are verified BY THE TEST:
    // digests recomputed from the reported concrete paths and compared to the
    // committed pin — "ran on some 8.x" is insufficient.
    [Fact]
    public void ControlCell_LaunchesViaPinnedPrivateHost_IdentityProven()
    {
        var scratch = SpikePaths.TestScratch("inv013-control-cell");
        var host = RunContext.Resolve("control_dotnet_host");
        var controlDll = RunContext.Resolve("RouteAControl");
        var report = Path.Combine(scratch, "control-report.json");

        var result = ManagedLauncher.Launch(new LaunchRequest(
            host.AbsolutePath,
            new[] { "exec", "--fx-version", SpecConstants.Net8ControlRuntimeVersion, controlDll.AbsolutePath, "--identity-probe", "--out", report },
            SpikePaths.SpikeRoot,
            EnvProfiles.For("control", RunContext.RunRoot()),
            600));
        Assert.Equal(0, result.ExitCode);

        using var pin = SpikePaths.Json(SpikePaths.P("config", "net8-control-pin.json"));
        var ids = pin.RootElement.GetProperty("net8_expected_identities");
        using var doc = Launch.Report(report);
        Assert.Equal("control-report", doc.RootElement.GetProperty("kind").GetString());
        var binding = doc.RootElement.GetProperty("binding_identity");

        foreach (var (key, digestKey, pathKey) in new[]
        {
            ("hostfxr_identity_concrete", "hostfxr_expected_digest", "hostfxr_relative_path"),
            ("corelib_identity_concrete", "corelib_expected_digest", "corelib_relative_path"),
        })
        {
            var identity = binding.GetProperty(key);
            var reportedPath = identity.GetProperty("path").GetString()!;
            var reportedSha = identity.GetProperty("sha256").GetString()!;
            // TEST-side recompute from the reported concrete path.
            Assert.True(File.Exists(reportedPath), $"{key}: reported path does not exist: {reportedPath}");
            Assert.Equal(reportedSha, SpikePaths.Sha256File(reportedPath));
            // Location matches the pinned private-runtime layout; digest matches the pin.
            Assert.EndsWith(ids.GetProperty(pathKey).GetString()!, reportedPath.Replace('\\', '/'));
            Assert.Equal(ids.GetProperty(digestKey).GetString(), reportedSha);
        }
    }

    // Tests INV-013 [unit] (TA-B12): control-equivalence digest comparison —
    // matching input sets prove equivalence; any mismatch is a typed rejection
    // naming the divergent key (never a stub throw, never silence).
    [Fact]
    public void ControlEquivalence_MatchingProves_MismatchRejectsTyped()
    {
        var main = new Dictionary<string, string>
        {
            ["normalized-source"] = new string('a', 64),
            ["resolved-package-graph"] = new string('b', 64),
            ["options"] = new string('c', 64),
            ["fixtures"] = new string('d', 64),
            ["compiler-inputs"] = new string('e', 64),
        };
        var matching = new Dictionary<string, string>(main);
        ControlEquivalence.Prove(main, matching); // must not throw

        var mismatching = new Dictionary<string, string>(main) { ["options"] = new string('f', 64) };
        var ex = Record.Exception(() => ControlEquivalence.Prove(main, mismatching));
        Assert.NotNull(ex);
        Assert.IsNotType<NotImplementedException>(ex); // a stub throw is not equivalence enforcement (AP-010)
        Assert.Contains("options", ex!.Message);
    }

    // Tests INV-013 [unit] (TA-B12, PAT-004/AP-002): the linter runs against the
    // COMMITTED ADR with the adjudication records from the COMMITTED evidence
    // sample and must report zero findings. RED-fails until the sample exists;
    // afterwards this is the wired production-chain check, not an isolated unit.
    [Fact]
    public void AdrLinter_CommittedAdr_WithCommittedSampleRecords_ZeroFindings()
    {
        var samples = Directory.Exists(SpikePaths.P("evidence", "samples"))
            ? Directory.EnumerateFiles(SpikePaths.P("evidence", "samples"), "*.json").ToList()
            : new List<string>();
        Assert.True(samples.Count > 0,
            "no committed evidence sample — the ADR linter cannot be exercised against real records (TA-B12); regenerate via scripts/regen-sample.sh once GREEN lands");

        using var sample = SpikePaths.Json(samples[0]);
        var records = new List<AdjudicationRecord>();
        if (sample.RootElement.GetProperty("deterministic").TryGetProperty("adjudication_records", out var recs)
            && recs.ValueKind == JsonValueKind.Array)
        {
            foreach (var r in recs.EnumerateArray())
            {
                records.Add(new AdjudicationRecord(
                    r.GetProperty("adjudication_record_id").GetString()!,
                    r.GetProperty("route").GetString()!,
                    Enum.Parse<RouteState>(r.GetProperty("terminal_state").GetString()!, ignoreCase: true),
                    null, null, null, null, null));
            }
        }
        var findings = AdrLinter.Lint(SpikePaths.Repo("docs", "adr", "ADR-0001-dafny-integration-boundary.md"), records);
        Assert.Empty(findings);
    }

    // Tests INV-013 [unit] (codex R2-2): TFM-conditional harness logic is
    // prohibited and enforced against by scan — no #if in seam/harness/control
    // sources, no TargetFramework conditions in their project files.
    [Fact]
    public void NoTfmConditionalLogic_InSeamHarnessControl()
    {
        var scanned = new[] { "adapters", "harness", "control", "contracts" }
            .SelectMany(d => Directory.EnumerateFiles(Path.Combine(SpikePaths.SpikeRoot, d), "*.cs", SearchOption.AllDirectories))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));
        foreach (var file in scanned)
        {
            Assert.False(Regex.IsMatch(File.ReadAllText(file), @"^\s*#if\b", RegexOptions.Multiline),
                $"{file} contains preprocessor conditionals — control equivalence requires identical normalized source (codex R2-2)");
        }
        foreach (var csproj in SpikePaths.AllCsprojFiles())
        {
            var text = File.ReadAllText(csproj);
            Assert.False(text.Contains("Condition") && text.Contains("$(TargetFramework)"),
                $"{csproj} has TargetFramework-conditional MSBuild logic (codex R2-2)");
        }
    }
}
