// GREEN:TDD — PR1 (Group A) determinism-lane receipt writer.
//
// The EXTRACTED serial-lane script (scripts/determinism-lane.sh, INV-005/INV-024)
// drives two nested run-spike.sh runs into per-run subroots (<root>/r1, <root>/r2),
// then invokes THIS emitter (via the already-built aggregator host, so no new
// solution project is added — the spike's project set is pinned) to:
//   * read the 5 role reports from each run's reports/ dir,
//   * compute per-run x per-role RAW SHA-256 + REAL EvidenceSchema deterministic
//     projection SHA-256 (INV-002 — equality is a projection property),
//   * derive comparison_status over the committed registries + closed
//     role/kind->projection-policy map (DeterminismComparison, RS-005),
//   * record the observed platform identity (INV-005) — ProcessorCount, RID,
//     pinned OS label, runner image, kernel, architecture, resolved SDK,
//   * emit the RunReceipt at <root>/receipts/determinism-receipt.json, and
//   * EXIT NON-ZERO on comparison_status=different (INV-003). No signing (PR1).
//
// This assembly is Dafny-free (INV-008).

using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Corrected.Spike.Contracts;

/// <summary>PR1 determinism-lane receipt writer (invoked by the aggregator host from the extracted lane script).</summary>
public static class DeterminismReceiptWriter
{
    // The spike report-artifact layout: role -> the report file the controller
    // emits under a run's reports/ dir. This is the artifact LAYOUT (matches
    // run-spike.sh + Inv010's projection loop), distinct from the role/kind
    // REGISTRY (which is the committed manifest the comparison derives from).
    private static readonly IReadOnlyDictionary<string, string> RoleReportFile = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["run"] = "run-report.json",
        ["route-a"] = "route-a.json",
        ["route-b"] = "route-b.json",
        ["control-a"] = "control-a.json",
        ["control-b"] = "control-b.json",
    };

    /// <summary>CLI entrypoint dispatched from the aggregator host on `--emit-determinism-receipt`.</summary>
    public static int RunCli(string[] args)
    {
        string r1 = "", r2 = "", schema = "", registry = "", kindRegistry = "", roleRegistry = "", policyMap = "", outPath = "";
        string osLabel = "", runnerImage = "", kernel = "", resolvedSdk = "", attestedCommit = "", subjectManifestDigest = "", policyVersion = "1";

        for (var i = 0; i < args.Length; i++)
        {
            string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"missing value for {args[i]}");
            switch (args[i])
            {
                case "--r1": r1 = Next(); break;
                case "--r2": r2 = Next(); break;
                case "--schema": schema = Next(); break;
                case "--registry": registry = Next(); break;
                case "--kind-registry": kindRegistry = Next(); break;
                case "--role-registry": roleRegistry = Next(); break;
                case "--policy-map": policyMap = Next(); break;
                case "--out": outPath = Next(); break;
                case "--os-label": osLabel = Next(); break;
                case "--runner-image": runnerImage = Next(); break;
                case "--kernel": kernel = Next(); break;
                case "--resolved-sdk": resolvedSdk = Next(); break;
                case "--attested-commit": attestedCommit = Next(); break;
                case "--subject-manifest-digest": subjectManifestDigest = Next(); break;
                case "--policy-version": policyVersion = Next(); break;
                default:
                    Console.Error.WriteLine($"emit-determinism-receipt: unknown argument '{args[i]}'");
                    return 2;
            }
        }

        foreach (var (name, value) in new[]
                 {
                     ("--r1", r1), ("--r2", r2), ("--schema", schema), ("--registry", registry),
                     ("--kind-registry", kindRegistry), ("--role-registry", roleRegistry),
                     ("--policy-map", policyMap), ("--out", outPath),
                 })
        {
            if (string.IsNullOrEmpty(value))
            {
                Console.Error.WriteLine($"emit-determinism-receipt: {name} is required");
                return 2;
            }
        }

        try
        {
            // The projection is derived from the reviewed, digest-validated schema
            // (the compiled-in trust anchor is checked here BEFORE any projection).
            EvidenceSchema.ValidateSchemaFile(schema, registry);

            var receipt = Build(
                r1, r2, schema, kindRegistry, roleRegistry, policyMap,
                osLabel, runnerImage, kernel, resolvedSdk, attestedCommit, subjectManifestDigest, policyVersion);

            var json = RunReceiptCodec.Serialize(receipt);

            // PRH-003 (QA-002) — FAIL CLOSED: scan the Corrected-authored receipt for
            // local-environment identity leaks (hostname / username / home / temp /
            // absolute-local path) BEFORE writing. A leak REFUSES the write and exits
            // non-zero — a public-repo receipt must never carry the emitting host's
            // identity. This is the production caller that makes PRH-003 enforced, not
            // just fixture-tested.
            var leaks = ReceiptPrivacyScan.LocalIdentityLeaks(json);
            if (leaks.Count > 0)
            {
                Console.Error.WriteLine(
                    "determinism-lane: REFUSING to write the receipt — local-identity leak(s) in Corrected-authored field(s) "
                    + $"(PRH-003, fail-closed): {string.Join(", ", leaks)}");
                return 3;
            }

            AtomicWrite(outPath, json);

            var disposition = DeterminismDisposition.Dispose(new ReceiptStatus(receipt.Execution, receipt.Comparison));
            Console.Error.WriteLine(
                $"determinism-lane: execution_status={WireEnum.ToWire(receipt.Execution)} "
                + $"comparison_status={WireEnum.ToWire(receipt.Comparison)} exit={disposition.ExitCode}"
                + (disposition.Message is null ? "" : $" — {disposition.Message}"));
            return disposition.ExitCode;
        }
        catch (Exception ex)
        {
            // A receipt-write / projection fault is its own state — fail closed,
            // NEVER emit a comparison_status=different for an infrastructure fault
            // (INV-001). A missing/unwritable receipt is classified externally as
            // infrastructure_invalid by the consumer (ClassifyMissingReceipt).
            Console.Error.WriteLine($"determinism-lane: infrastructure fault emitting receipt: {ex.Message}");
            return 3;
        }
    }

    /// <summary>
    /// Regen utility: prints the projection-IMPLEMENTATION digest (SHA-256 of the real
    /// DeterministicProjection output over a self-test vector). Used to (re)compute the
    /// manifest pin `projection_impl_digest` — the pin is a committed value, never guessed.
    /// </summary>
    public static int PrintProjectionImplDigestCli(string[] args)
    {
        string schema = "", vector = "";
        for (var i = 0; i < args.Length; i++)
        {
            string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"missing value for {args[i]}");
            switch (args[i])
            {
                case "--schema": schema = Next(); break;
                case "--vector": vector = Next(); break;
                default:
                    Console.Error.WriteLine($"print-projection-impl-digest: unknown argument '{args[i]}'");
                    return 2;
            }
        }
        if (string.IsNullOrEmpty(schema) || string.IsNullOrEmpty(vector))
        {
            Console.Error.WriteLine("print-projection-impl-digest: --schema and --vector are required");
            return 2;
        }
        Console.WriteLine(DeterminismRegistries.ComputeProjectionImplDigest(schema, vector));
        return 0;
    }

    /// <summary>Builds the RunReceipt over the two run roots (public for reuse/testing within the assembly).</summary>
    public static RunReceipt Build(
        string r1Root, string r2Root, string schemaPath,
        string kindRegistryPath, string roleRegistryPath, string policyMapPath,
        string osLabel, string runnerImage, string kernel, string resolvedSdk,
        string attestedCommit, string subjectManifestDigest, string policyVersion)
    {
        var roles = DeterminismRegistries.Roles(roleRegistryPath);
        var roleToKind = DeterminismRegistries.RoleToKind(roleRegistryPath);
        var canonicalization = DeterminismRegistries.PinnedCanonicalizationVersion(policyMapPath);

        // RS-005 hardening (QA-005): the producer computes the digest of the ACTUAL
        // projection implementation it runs — SHA-256 of the real
        // DeterministicProjection output over the committed self-test vector — and records
        // it in every cell. Compare then cross-checks it against the manifest pin, so a
        // producer running a DIFFERENT projection is caught on a real receipt.
        var implDigest = DeterminismRegistries.ComputeProjectionImplDigest(schemaPath, ResolveSelfTestVector(schemaPath, policyMapPath));

        var run1 = BuildRunEvidence(r1Root, schemaPath, roles, roleToKind, policyMapPath, canonicalization, implDigest);
        var run2 = BuildRunEvidence(r2Root, schemaPath, roles, roleToKind, policyMapPath, canonicalization, implDigest);

        // Fill the per-role equality verdict now that both runs exist: the
        // deterministic PROJECTION agreeing across the two runs is the guarantee
        // (raw digests are expected to differ). This is per-role diagnostic only;
        // the aggregate comparison_status is derived by DeterminismComparison.
        var proj2 = run2.ToDictionary(e => e.Role, e => e.ProjectionSha256, StringComparer.Ordinal);
        for (var i = 0; i < run1.Count; i++)
        {
            var verdict = proj2.TryGetValue(run1[i].Role, out var p2) && run1[i].ProjectionSha256 == p2 ? "equal" : "different";
            run1[i] = run1[i] with { PerRoleVerdict = verdict };
            var idx2 = run2.FindIndex(e => e.Role == run1[i].Role);
            if (idx2 >= 0)
            {
                run2[idx2] = run2[idx2] with { PerRoleVerdict = verdict };
            }
        }

        var comparison = DeterminismComparison.Compare(run1, run2, kindRegistryPath, roleRegistryPath, policyMapPath);

        // Both nested runs completed (the lane guards the resource floor before it
        // runs). Map the derived comparison onto the CLOSED legal-status table:
        // equal/different keep execution=completed; a structural not_evaluated is a
        // run-level fault -> infrastructure_invalid/not_evaluated (INV-001 — never
        // a completed+not_evaluated illegal pair, never a fail-open different).
        var status = comparison.Status switch
        {
            ComparisonStatus.Equal => new ReceiptStatus(ExecutionStatus.Completed, ComparisonStatus.Equal),
            ComparisonStatus.Different => new ReceiptStatus(ExecutionStatus.Completed, ComparisonStatus.Different),
            _ => new ReceiptStatus(ExecutionStatus.InfrastructureInvalid, ComparisonStatus.NotEvaluated),
        };

        var platform = ObservePlatform(osLabel, runnerImage, kernel, resolvedSdk);

        var commit = string.IsNullOrEmpty(attestedCommit) ? ResolveHeadCommit() : attestedCommit;
        var manifestDigest = string.IsNullOrEmpty(subjectManifestDigest) ? Sha256File(schemaPath) : subjectManifestDigest;

        return new RunReceipt(
            status.Execution, status.Comparison, commit, manifestDigest,
            string.IsNullOrEmpty(policyVersion) ? "1" : policyVersion,
            platform, run1, run2);
    }

    /// <summary>Resolves the committed self-test vector (repo-relative in the manifest) to an absolute path under the spike root.</summary>
    private static string ResolveSelfTestVector(string schemaPath, string policyMapPath)
    {
        var vectorRelative = DeterminismRegistries.ProjectionSelfTestVector(policyMapPath);
        // schemaPath is <spike-root>/schema/evidence-schema.json; two levels up is the spike root.
        var spikeRoot = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetFullPath(schemaPath)))!;
        return Path.Combine(spikeRoot, vectorRelative.Replace('/', Path.DirectorySeparatorChar));
    }

    private static List<RoleEvidence> BuildRunEvidence(
        string runRoot, string schemaPath, IReadOnlySet<string> roles,
        IReadOnlyDictionary<string, string> roleToKind, string policyMapPath, string canonicalization,
        string projectionImplDigest)
    {
        var evidence = new List<RoleEvidence>();
        foreach (var role in roles)
        {
            if (!RoleReportFile.TryGetValue(role, out var fileName))
            {
                throw new InvalidOperationException(
                    $"no report-artifact layout entry for role '{role}' — the emitter's role->file map must cover the committed role registry (INV-002)");
            }
            var kind = roleToKind[role];
            var repoRelativeName = "reports/" + fileName;
            var absolute = Path.Combine(runRoot, "reports", fileName);
            if (!File.Exists(absolute))
            {
                throw new InvalidOperationException(
                    $"run at {runRoot} emitted no {repoRelativeName} — every committed role needs a real per-run artifact (INV-002)");
            }

            var reportText = File.ReadAllText(absolute);
            var rawSha = Sha256File(absolute);
            var projection = EvidenceSchema.DeterministicProjection(reportText, schemaPath);
            var projectionSha = Sha256Text(projection);
            var pinned = DeterminismRegistries.PinnedPolicy(policyMapPath, role, kind);

            evidence.Add(new RoleEvidence(
                Role: role,
                Kind: kind,
                RepoRelativeName: repoRelativeName,
                RawSha256: rawSha,
                ProjectionSha256: projectionSha,
                // The producer runs the reviewed, EA-006-protected projection code,
                // so it records the MANIFEST-PINNED projection-policy identity (the
                // policy cross-check then closes the off-manifest variant, RS-005).
                ProjectionSchemaId: pinned.SchemaId,
                ProjectionSchemaVersion: pinned.Version,
                ProjectionSchemaDigest: pinned.Digest,
                CanonicalizationVersion: canonicalization,
                PerRoleVerdict: "pending",
                // The digest of the ACTUAL projection the producer ran (RS-005 hardening).
                ProjectionImplDigest: projectionImplDigest));
        }

        // Per-role verdict is filled once BOTH runs are built (needs the counterpart).
        return evidence;
    }

    private static PlatformIdentity ObservePlatform(string osLabel, string runnerImage, string kernel, string resolvedSdk)
    {
        string OrEnvOr(string given, string envName, string fallback)
        {
            if (!string.IsNullOrWhiteSpace(given))
            {
                return given;
            }
            var env = Environment.GetEnvironmentVariable(envName);
            return string.IsNullOrWhiteSpace(env) ? fallback : env!;
        }

        // A PINNED OS label (never floating *-latest). The workflow passes the
        // pinned label; local runs fall back to a pinned default.
        var os = OrEnvOr(osLabel, "DETERMINISM_OS_LABEL", "ubuntu-24.04");

        // The actual runner image/version — GitHub Actions exposes ImageOS +
        // ImageVersion; off-CI a non-empty local marker is recorded.
        var image = runnerImage;
        if (string.IsNullOrWhiteSpace(image))
        {
            var imageOs = Environment.GetEnvironmentVariable("ImageOS");
            var imageVersion = Environment.GetEnvironmentVariable("ImageVersion");
            image = (!string.IsNullOrWhiteSpace(imageOs) || !string.IsNullOrWhiteSpace(imageVersion))
                ? $"{imageOs}/{imageVersion}"
                : "local-dev-noncI-runner";
        }

        var kern = string.IsNullOrWhiteSpace(kernel) ? RuntimeInformation.OSDescription : kernel;
        var sdk = OrEnvOr(resolvedSdk, "DETERMINISM_RESOLVED_SDK", Environment.Version.ToString());

        return new PlatformIdentity(
            ProcessorCount: Environment.ProcessorCount,
            Rid: RuntimeInformation.RuntimeIdentifier,
            OsLabel: os,
            RunnerImage: image,
            Kernel: kern,
            Architecture: RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant(),
            ResolvedSdk: sdk);
    }

    private static string ResolveHeadCommit()
    {
        var repoRoot = GitResolver.FindRepoRoot(AppContext.BaseDirectory);
        if (repoRoot is null)
        {
            return "unknown";
        }
        try
        {
            return GitResolver.ReadHeadCommit(repoRoot);
        }
        catch (Exception)
        {
            return "unknown";
        }
    }

    private static string Sha256File(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static string Sha256Text(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    private static void AtomicWrite(string path, string content)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(dir);
        var temp = path + ".tmp-" + Environment.ProcessId;
        File.WriteAllText(temp, content);
        File.Move(temp, path, overwrite: true);
    }
}
