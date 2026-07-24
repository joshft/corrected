// Tests INV-009 and PRH-005: evidence binds the run to identities, tree state,
// and manifest — via the committed three-way field partition.
using System.Text.Json;
using Corrected.Spike.Contracts;
using Xunit;
using Xunit.Abstractions;

namespace Corrected.Spike.Tests;

// TA-A13: the culture-flip test mutates process-global CultureInfo.
[Collection(SharedStateMutatingCollection.Name)]
public class Inv009EvidenceTests
{
    private readonly ITestOutputHelper _output;

    public Inv009EvidenceTests(ITestOutputHelper output) => _output = output;

    private static string SchemaPath => SpikePaths.P("schema", "evidence-schema.json");
    private static string RegistryPath => SpikePaths.P("schema", "schema-version-registry.json");

    // Tests INV-009 [unit]: schema digest anchored in THREE places — the test
    // constant set (beside the manifest digest), the append-only registry, and
    // the aggregator's compiled-in trust anchor (codex R3-6).
    [Fact]
    public void SchemaDigest_AnchoredInTestConstant_Registry_AndAggregatorTrustAnchor()
    {
        var actual = SpikePaths.Sha256File(SchemaPath);
        Assert.Equal(SpecConstants.EvidenceSchemaSha256, actual);
        Assert.Equal(SpecConstants.EvidenceSchemaSha256, VerdictAggregator.EvidenceSchemaSha256TrustAnchor);

        using var registry = SpikePaths.Json(RegistryPath);
        var currentRow = registry.RootElement.GetProperty("versions").EnumerateArray()
            .Single(v => v.GetProperty("version").GetInt32() == SpecConstants.EvidenceSchemaVersion);
        Assert.Equal(SpecConstants.EvidenceSchemaSha256, currentRow.GetProperty("sha256").GetString());
        // Append-only proof (QA fix round 1): the retired v1 row is preserved
        // byte-for-byte — a version may never be reused with a different digest.
        var row1 = registry.RootElement.GetProperty("versions").EnumerateArray()
            .Single(v => v.GetProperty("version").GetInt32() == 1);
        Assert.Equal(SpecConstants.EvidenceSchemaV1Sha256, row1.GetProperty("sha256").GetString());
        // The on-disk schema declares the version its digest is registered under.
        using var schemaDoc = SpikePaths.Json(SchemaPath);
        Assert.Equal(SpecConstants.EvidenceSchemaVersion, schemaDoc.RootElement.GetProperty("evidence_schema_version").GetInt32());
    }

    // Tests INV-009 [unit]: the registry is append-only — versions are unique,
    // dense from 1, and the known committed rows are present and unaltered.
    [Fact]
    public void Registry_AppendOnly_UniqueVersions_NoReuse()
    {
        using var registry = SpikePaths.Json(RegistryPath);
        var rows = registry.RootElement.GetProperty("versions").EnumerateArray()
            .Select(v => (Version: v.GetProperty("version").GetInt32(), Sha: v.GetProperty("sha256").GetString()!))
            .ToList();
        Assert.NotEmpty(rows);
        Assert.Equal(rows.Count, rows.Select(r => r.Version).Distinct().Count());
        Assert.Equal(rows.Count, rows.Select(r => r.Sha).Distinct().Count());
        Assert.Contains(rows, r => r.Version == 1 && r.Sha == SpecConstants.EvidenceSchemaV1Sha256);
        Assert.Contains(rows, r => r.Version == SpecConstants.EvidenceSchemaVersion && r.Sha == SpecConstants.EvidenceSchemaSha256);
        foreach (var r in rows) { Assert.Matches("^[0-9a-f]{64}$", r.Sha); }
    }

    // Tests INV-009 [unit]: the schema is closed — additionalProperties: false
    // recursively on every object node of the report schema.
    [Fact]
    public void ReportSchema_ClosedRecursively_AdditionalPropertiesFalse()
    {
        using var doc = SpikePaths.Json(SchemaPath);
        var schema = doc.RootElement.GetProperty("report_schema");
        var violations = new List<string>();
        WalkObjects(schema, "report_schema", violations);
        Assert.True(violations.Count == 0, "open object nodes: " + string.Join("; ", violations));
    }

    private static void WalkObjects(JsonElement node, string path, List<string> violations)
    {
        if (node.ValueKind == JsonValueKind.Object)
        {
            if (node.TryGetProperty("type", out var t))
            {
                // TA-A15: union types containing "object" (e.g. ["object","null"])
                // are IN SCOPE — they must carry additionalProperties:false too.
                var isObjectType =
                    (t.ValueKind == JsonValueKind.String && t.GetString() == "object")
                    || (t.ValueKind == JsonValueKind.Array && t.EnumerateArray()
                        .Any(v => v.ValueKind == JsonValueKind.String && v.GetString() == "object"));
                if (isObjectType)
                {
                    if (!node.TryGetProperty("additionalProperties", out var ap) || ap.ValueKind != JsonValueKind.False)
                    {
                        violations.Add(path);
                    }
                }
            }
            foreach (var prop in node.EnumerateObject())
            {
                WalkObjects(prop.Value, $"{path}.{prop.Name}", violations);
            }
        }
        else if (node.ValueKind == JsonValueKind.Array)
        {
            var i = 0;
            foreach (var item in node.EnumerateArray())
            {
                WalkObjects(item, $"{path}[{i++}]", violations);
            }
        }
    }

    // Tests INV-009/RS-005 [unit]: the committed three-way partition — every
    // field in exactly one class, all three classes non-empty, and the equality
    // domain (class 2) carries the spec-named members.
    [Fact]
    public void FieldPartition_ThreeClasses_EveryFieldExactlyOnce()
    {
        using var doc = SpikePaths.Json(SchemaPath);
        var partition = doc.RootElement.GetProperty("field_partition");
        var c1 = partition.GetProperty("class_1_binding_identity").EnumerateArray().Select(f => f.GetString()!).ToList();
        var c2 = partition.GetProperty("class_2_deterministic_projection").EnumerateArray().Select(f => f.GetString()!).ToList();
        var c3 = partition.GetProperty("class_3_volatile").EnumerateArray().Select(f => f.GetString()!).ToList();
        Assert.NotEmpty(c1);
        Assert.NotEmpty(c2);
        Assert.NotEmpty(c3);
        var all = c1.Concat(c2).Concat(c3).ToList();
        Assert.Equal(all.Count, all.Distinct().Count());

        // Spec-named class members (RS-005, codex R2-9/R4-09). TA-B16: partition
        // entries carry the EXACT instance-field names (no alias mapping), and
        // the root envelope fields kind/evidence_schema_version are classified.
        Assert.Contains("run_id", c1);
        Assert.Contains("git_commit_id", c1);
        Assert.Contains("restore_argv_concrete", c1);
        Assert.Contains("kind", c2);
        Assert.Contains("evidence_schema_version", c2);
        Assert.Contains("route_verdicts", c2);
        Assert.Contains("evidence_schema_sha256", c2);
        Assert.Contains("probe_manifest_sha256", c2);
        Assert.Contains("executed_solver_sha256", c2);
        Assert.Contains("final_suite_status", c2);
        Assert.Contains("adjudication_records", c2);
        Assert.Contains("timestamps", c3);
        Assert.Contains("durations", c3);
        Assert.Contains("raw_solver_resource_counts", c3); // codex R4-09: raw counts outside the equality domain

        // TA-B16: every ROOT instance field of the report schema is classified —
        // GREEN derives the projection from the schema file alone, with no
        // hard-coded envelope mapping (RS-005/PAT-004).
        var all3 = c1.Concat(c2).Concat(c3).ToHashSet();
        var rootFields = doc.RootElement.GetProperty("report_schema").GetProperty("properties")
            .EnumerateObject().Select(p => p.Name)
            .Where(n => n is not ("binding_identity" or "deterministic" or "volatile"));
        foreach (var field in rootFields)
        {
            Assert.Contains(field, all3);
        }
    }

    // Tests INV-009 [unit] (codex F11/R2-9): schema-substitution — the
    // aggregator validates the committed schema digest BEFORE parsing any
    // report and rejects a substituted schema file.
    [Fact]
    public void SchemaSubstitution_Rejected()
    {
        var scratch = SpikePaths.TestScratch("inv009-schema-sub");
        var tampered = Path.Combine(scratch, "evidence-schema.json");
        var text = File.ReadAllText(SchemaPath).Replace(
            $"\"evidence_schema_version\": {SpecConstants.EvidenceSchemaVersion}",
            $"\"evidence_schema_version\": {SpecConstants.EvidenceSchemaVersion},\n  \"tampered\": true");
        File.WriteAllText(tampered, text);
        var ex = Record.Exception(() => EvidenceSchema.ValidateSchemaFile(tampered, RegistryPath));
        Assert.NotNull(ex);
        Assert.IsNotType<NotImplementedException>(ex); // a stub throw is not a rejection (AP-010)
        Assert.Contains("digest", ex!.Message, StringComparison.OrdinalIgnoreCase);
    }

    // Tests INV-009 [unit] (codex R3-6): same-version mutation — version 1 with
    // a different digest is a registry violation, rejected.
    [Fact]
    public void SameVersionMutation_Rejected()
    {
        var scratch = SpikePaths.TestScratch("inv009-version-reuse");
        var tamperedSchema = Path.Combine(scratch, "evidence-schema.json");
        File.WriteAllText(tamperedSchema, File.ReadAllText(SchemaPath) + "\n");
        var tamperedRegistry = Path.Combine(scratch, "schema-version-registry.json");
        var reg = File.ReadAllText(RegistryPath)
            .Replace(SpecConstants.EvidenceSchemaSha256, SpikePaths.Sha256File(tamperedSchema));
        File.WriteAllText(tamperedRegistry, reg);
        // Even with a colluding on-disk registry, the compiled-in trust anchor rejects.
        var ex = Record.Exception(() => EvidenceSchema.ValidateSchemaFile(tamperedSchema, tamperedRegistry));
        Assert.NotNull(ex);
        Assert.IsNotType<NotImplementedException>(ex); // a stub throw is not a rejection (AP-010)
    }

    // Tests INV-009 [unit] (codex R2-1): projection canonicalization — every
    // run-root-dependent path becomes <run-root>, and the run_id value appears
    // NOWHERE in the projection.
    [Fact]
    public void Projection_CanonicalizesRunRoot_AndRunIdAppearsNowhere()
    {
        var runId = "runid-3f9a1c2b7d8e";
        var report = $$"""
        {
          "evidence_schema_version": 1,
          "evidence_schema_sha256": "{{SpecConstants.EvidenceSchemaSha256}}",
          "probe_manifest_sha256": "{{SpecConstants.ProbeManifestSha256}}",
          "run_id": "{{runId}}",
          "kind": "route-report",
          "binding_identity": {
            "run_directory": "out/{{runId}}",
            "git_commit_id": "abc123", "git_dirty_flag": false, "host_rid": "linux-x64",
            "sdk_version": "10.0.302", "actual_runtime_version": "10.0.2",
            "solver_path_concrete": "out/{{runId}}/solver/z3-4.12.1/bin/z3"
          },
          "deterministic": { "per_probe_results": [], "final_suite_status": "success" },
          "volatile": {}
        }
        """;
        var projection = EvidenceSchema.DeterministicProjection(report, SchemaPath);
        Assert.DoesNotContain(runId, projection);
        Assert.DoesNotContain("out/" + runId, projection);
    }

    // Tests INV-009 [unit]: projection construction FAILS if any instance field
    // lacks exactly one classification.
    [Fact]
    public void Projection_FailsOnUnclassifiedField()
    {
        var report = """
        {
          "evidence_schema_version": 1,
          "evidence_schema_sha256": "0000000000000000000000000000000000000000000000000000000000000000",
          "probe_manifest_sha256": "0000000000000000000000000000000000000000000000000000000000000000",
          "run_id": "runid-0000000000000000",
          "kind": "route-report",
          "binding_identity": { "run_directory": "x", "git_commit_id": "x", "git_dirty_flag": false, "host_rid": "linux-x64", "sdk_version": "10.0.302", "actual_runtime_version": "10.0.2" },
          "deterministic": { "per_probe_results": [], "final_suite_status": "success" },
          "volatile": {},
          "field_never_classified_anywhere": true
        }
        """;
        var ex = Record.Exception(() => EvidenceSchema.DeterministicProjection(report, SchemaPath));
        Assert.NotNull(ex);
        Assert.IsNotType<NotImplementedException>(ex); // a stub throw is not the classification failure (AP-010)
        Assert.Contains("classif", ex!.Message, StringComparison.OrdinalIgnoreCase);
    }

    // Tests INV-009 [integration] (RS-017f, TA-A9): atomic emission — the
    // LAUNCHER (not the SUT) delivers SIGKILL mid-run; a killed harness leaves
    // either no report or a complete one, never a torn file. Repeated kill
    // delays raise the odds of catching the emission window.
    [Fact]
    public void ReportEmission_IsAtomic_UnderExternalSigkill()
    {
        var scratch = SpikePaths.TestScratch("inv009-atomic");
        foreach (var killAfterMs in new[] { 50.0, 150.0, 400.0, 900.0 })
        {
            var report = Path.Combine(scratch, $"report-{killAfterMs}.json");
            var result = Launch.HarnessKilledAfter("A", killAfterMs, "--probe", "P06", "--out", report);
            if (File.Exists(report))
            {
                using var doc = SpikePaths.Json(report); // must parse — a torn file fails here
                Assert.True(doc.RootElement.TryGetProperty("evidence_schema_version", out _),
                    "report exists but is incomplete — emission is not write-temp-then-rename (RS-017f)");
            }
            else
            {
                Assert.NotEqual(ExitCodes.RouteProbesPassed, result.ExitCode);
            }
        }
    }

    // Tests INV-009/RS-016 [integration] (TA-A2): anti-hardcode — every
    // reported assembly SHA-256 is recomputed BY THE TEST from the file at its
    // reported CONCRETE path (class-1 binding identity, resolved against the
    // report's own run_directory — never an invented layout); the executed
    // solver digest is recomputed the same way.
    [Fact]
    public void AntiHardcode_RecomputeEachReportedAssemblySha_AndSolverSha()
    {
        var scratch = SpikePaths.TestScratch("inv009-antihardcode");
        var reportPath = Path.Combine(scratch, "report.json");
        var result = Launch.Harness("A", "--probe", "P03,P06", "--out", reportPath);
        Assert.Equal(ExitCodes.RouteProbesPassed, result.ExitCode);
        using var doc = Launch.Report(reportPath);
        var binding = doc.RootElement.GetProperty("binding_identity");
        var runDirectory = binding.GetProperty("run_directory").GetString()!;
        var runRootFull = Path.IsPathRooted(runDirectory) ? runDirectory : Path.Combine(SpikePaths.SpikeRoot, runDirectory);

        // Concrete paths come from the binding class; digests from the deterministic class (RS-005).
        var concretePaths = binding.GetProperty("loaded_assembly_file_paths").EnumerateArray()
            .ToDictionary(e => e.GetProperty("simple_name").GetString()!, e => e.GetProperty("path").GetString()!);
        var assemblies = doc.RootElement.GetProperty("deterministic").GetProperty("loaded_assembly_identities").EnumerateArray().ToList();
        Assert.NotEmpty(assemblies);
        foreach (var asm in assemblies)
        {
            var name = asm.GetProperty("simple_name").GetString()!;
            Assert.True(concretePaths.TryGetValue(name, out var concrete), $"no concrete binding path for {name}");
            var full = Path.IsPathRooted(concrete) ? concrete : Path.Combine(runRootFull, concrete);
            Assert.True(File.Exists(full), $"reported assembly path does not exist: {concrete}");
            Assert.Equal(asm.GetProperty("file_sha256").GetString(), SpikePaths.Sha256File(full));
        }

        // QA-002: executed_solver_sha256 is the recomputed digest of the file
        // at the OPTION-MANIFEST solver path (the executed binary), never a
        // stand-in release archive; the archive pin is a separate field.
        var solverConcrete = binding.GetProperty("solver_path_concrete").GetString()!;
        Assert.EndsWith("solver/z3-4.12.1/bin/z3", solverConcrete.Replace('\\', '/'));
        var solverFull = Path.IsPathRooted(solverConcrete) ? solverConcrete : Path.Combine(runRootFull, solverConcrete);
        Assert.True(File.Exists(solverFull), $"reported executed-solver path does not exist: {solverConcrete}");
        var det = doc.RootElement.GetProperty("deterministic");
        Assert.Equal(det.GetProperty("executed_solver_sha256").GetString(), SpikePaths.Sha256File(solverFull));
        Assert.NotEqual(det.GetProperty("executed_solver_sha256").GetString(),
            det.GetProperty("solver_archive_sha256").GetString()); // binary != archive
    }

    // Tests INV-009/EA-006 [unit] (TA-A7): culture-flip — the deterministic
    // projection is byte-identical under a hostile culture (tr-TR dotted/
    // dotless-i, comma decimal separator). Host locale must not affect class 2.
    [Fact]
    public void Projection_CultureFlip_ByteIdentical()
    {
        var report = MinimalValidReport("runid-culture0000001");
        var invariant = System.Globalization.CultureInfo.InvariantCulture;
        var original = System.Globalization.CultureInfo.CurrentCulture;
        string underInvariant, underTurkish;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = invariant;
            System.Globalization.CultureInfo.CurrentUICulture = invariant;
            underInvariant = EvidenceSchema.DeterministicProjection(report, SchemaPath);
            var tr = new System.Globalization.CultureInfo("tr-TR");
            System.Globalization.CultureInfo.CurrentCulture = tr;
            System.Globalization.CultureInfo.CurrentUICulture = tr;
            underTurkish = EvidenceSchema.DeterministicProjection(report, SchemaPath);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = original;
            System.Globalization.CultureInfo.CurrentUICulture = original;
        }
        Assert.Equal(underInvariant, underTurkish);
    }

    // Tests INV-009 [integration] (codex R3-6, TA-A11): ORDERING PROOF — the
    // aggregator validates the committed schema digest BEFORE parsing any
    // report. The TEST hands it BOTH a tampered schema AND a deliberately
    // malformed report; the failure must be the digest rejection, proving
    // schema validation precedes report parsing.
    [Fact]
    public void Aggregator_ValidatesSchemaDigest_BeforeParsingAnyReport()
    {
        var scratch = SpikePaths.TestScratch("inv009-ordering");
        var tamperedSchema = Path.Combine(scratch, "evidence-schema.json");
        File.WriteAllText(tamperedSchema, File.ReadAllText(SchemaPath) + "\n");
        var reportsDir = Path.Combine(scratch, "reports");
        Directory.CreateDirectory(reportsDir);
        File.WriteAllText(Path.Combine(reportsDir, "route-a.json"), "{ this is not JSON [");

        var aggregator = RunContext.Resolve("SpikeAggregator");
        var env = EnvProfiles.For("harness", RunContext.RunRoot());
        var result = Launch.Dll(aggregator.AbsolutePath, env,
            "--manifest", SpikePaths.P("manifest", "probe-manifest.json"),
            "--schema", tamperedSchema,
            "--registry", RegistryPath,
            "--reports-dir", reportsDir,
            "--out", Path.Combine(scratch, "run-report.json"));

        Assert.NotEqual(0, result.ExitCode);
        var output = result.StdOut + result.StdErr;
        Assert.Contains("schema digest", output, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("malformed", output, StringComparison.OrdinalIgnoreCase); // it never got to the report
    }

    // Tests INV-009 [integration] (TA-A11): git commit id + dirty flag are
    // validated at GENERATION time — the report's binding identity must carry
    // the HEAD the TEST resolves independently from .git.
    [Fact]
    public void GitCommitBinding_MatchesHeadAtGenerationTime()
    {
        var scratch = SpikePaths.TestScratch("inv009-git");
        var reportPath = Path.Combine(scratch, "report.json");
        var result = Launch.Harness("A", "--probe", "P03", "--out", reportPath);
        Assert.Equal(ExitCodes.RouteProbesPassed, result.ExitCode);
        using var doc = Launch.Report(reportPath);
        var binding = doc.RootElement.GetProperty("binding_identity");
        Assert.Equal(SpikePaths.GitHeadCommit(), binding.GetProperty("git_commit_id").GetString());
        Assert.True(binding.GetProperty("git_dirty_flag").ValueKind is JsonValueKind.True or JsonValueKind.False);
    }

    private static string MinimalValidReport(string runId) => $$"""
        {
          "evidence_schema_version": 1,
          "evidence_schema_sha256": "{{SpecConstants.EvidenceSchemaSha256}}",
          "probe_manifest_sha256": "{{SpecConstants.ProbeManifestSha256}}",
          "run_id": "{{runId}}",
          "kind": "route-report",
          "binding_identity": {
            "run_directory": "out/{{runId}}",
            "git_commit_id": "abc123", "git_dirty_flag": false, "host_rid": "linux-x64",
            "sdk_version": "10.0.302", "actual_runtime_version": "10.0.2"
          },
          "deterministic": { "per_probe_results": [], "final_suite_status": "success" },
          "volatile": {}
        }
        """;

    // Tests INV-009/PRH-005 [unit]: hygiene grep over COMMITTED artifacts —
    // widened path set + runtime-derived username/hostname (never hardcoded,
    // which would itself violate PRH-005). Known limit stated per AP-004: this
    // cannot catch a different machine's identifiers; human review is the backstop.
    [Fact]
    public void HygieneGrep_CommittedArtifacts_ContainNoHostDetails()
    {
        var committedGlobs = new List<string>();
        void AddDir(string rel, string pattern)
        {
            var dir = SpikePaths.P(rel.Split('/'));
            if (Directory.Exists(dir))
            {
                committedGlobs.AddRange(Directory.EnumerateFiles(dir, pattern, SearchOption.AllDirectories));
            }
        }
        AddDir("evidence/samples", "*");
        AddDir("fixtures", "*");
        AddDir("manifest", "*");
        AddDir("schema", "*");
        AddDir("config", "*");
        AddDir("scripts", "*");
        committedGlobs.Add(SpikePaths.Repo("docs", "adr", "ADR-0001-dafny-integration-boundary.md"));

        var username = Environment.UserName;
        var hostname = System.Net.Dns.GetHostName();
        var badPatterns = new[] { "/home/", "/Users/", "/tmp/", "C:\\" };
        // The username/hostname checks catch a DISTINCTIVE developer identifier
        // leaking into committed evidence. Generic CI/system account names (e.g.
        // GitHub Actions' "runner", "root", "ubuntu") are common substrings — the
        // OS user "runner" collides with the legitimate "test_runner" config key —
        // so they cannot distinguish a leak from coincidence and would false-fail
        // on any such host. Skip them; human review (AP-004) remains the backstop.
        var genericAccounts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "runner", "runneradmin", "root", "ubuntu", "user", "admin", "build",
            "test", "ci", "github", "actions", "vsts", "azureuser", "ec2-user",
        };

        foreach (var file in committedGlobs.Where(File.Exists))
        {
            var text = File.ReadAllText(file);
            foreach (var pattern in badPatterns)
            {
                Assert.False(text.Contains(pattern, StringComparison.OrdinalIgnoreCase),
                    $"{file} contains banned path pattern '{pattern}' (PRH-005)");
            }
            if (username.Length >= 3 && !genericAccounts.Contains(username))
            {
                Assert.False(text.Contains(username, StringComparison.Ordinal),
                    $"{file} contains the generating user's name (PRH-005)");
            }
            if (hostname.Length >= 3 && !genericAccounts.Contains(hostname))
            {
                Assert.False(text.Contains(hostname, StringComparison.OrdinalIgnoreCase),
                    $"{file} contains the generating hostname (PRH-005)");
            }
        }
    }

    // Tests INV-009/DD-008 [integration]: the committed sample must exist and its
    // deterministic projection equal a fresh run's — scoped to the pinned RID
    // (EA-002) with a loud skip elsewhere. The failure message names the
    // sanctioned regeneration procedure and distinguishes regenerate-vs-investigate.
    [Fact]
    public void CommittedSample_Exists_AndFreshRunProjectionEqual_RidScoped()
    {
        // MA-ED-4: the scope gate is a LOUD non-pass outcome (throws on any
        // non-linux-x64 host) — never a green pass with zero comparisons run.
        SpikePaths.RequireProvenRid();

        // QA-006 PAIR: the variance-mode sample compares under FULL class-2
        // equality; the canonical-run sample under the schema-declared
        // suite-status MASK. Both compare against ONE variance fresh run — the
        // mask drops exactly the subtree by which a canonical and a variance
        // run differ (final_suite_status/route_verdicts/verdict_reasons), so no
        // recursive canonical run is needed inside the suite (QA-006 catch-22
        // resolution).
        var varianceSample = SpikePaths.P("evidence", "samples", "run-report.sample.json");
        var canonicalSample = SpikePaths.P("evidence", "samples", "run-report.canonical.sample.json");
        Assert.True(File.Exists(varianceSample),
            "no committed VARIANCE evidence sample (evidence/samples/run-report.sample.json). If this follows an INTENTIONAL change: regenerate via scripts/regen-sample.sh and review the diff (DD-008). If UNEXPLAINED: investigate — do not regenerate (RS-020/AP-005).");
        Assert.True(File.Exists(canonicalSample),
            "no committed CANONICAL evidence sample (evidence/samples/run-report.canonical.sample.json) — regenerate the PAIR via scripts/regen-sample.sh (DD-008/QA-006).");

        using var scope = SpikePaths.TransientScratch("inv009-fresh-run");
        var scratch = scope.Root;
        var fresh = Path.Combine(scratch, "run-report.json");
        var run = Launch.Script("scripts/run-spike.sh", null, "--out", fresh);
        Assert.Equal(0, run.ExitCode);
        var freshText = File.ReadAllText(fresh);

        var freshFull = EvidenceSchema.DeterministicProjection(freshText, SchemaPath);
        var varianceProjection = EvidenceSchema.DeterministicProjection(File.ReadAllText(varianceSample), SchemaPath);
        Assert.True(varianceProjection == freshFull,
            "committed VARIANCE sample's full deterministic projection diverges from a fresh run. If this follows an INTENTIONAL change: regenerate via scripts/regen-sample.sh " +
            "and review the diff (DD-008). If UNEXPLAINED: investigate — never regenerate to silence it (AP-005/RS-020).");

        var freshMasked = EvidenceSchema.DeterministicProjection(freshText, SchemaPath, applySuiteStatusMask: true);
        var canonicalMasked = EvidenceSchema.DeterministicProjection(File.ReadAllText(canonicalSample), SchemaPath, applySuiteStatusMask: true);
        Assert.True(canonicalMasked == freshMasked,
            "committed CANONICAL sample's suite-status-masked projection diverges from a fresh run. If INTENTIONAL: regenerate the PAIR via scripts/regen-sample.sh (DD-008/QA-006). If UNEXPLAINED: investigate (AP-005/RS-020).");

        scope.Commit(); // passed — reclaim the fresh canonical run root (~550 MB)
    }
}
