// GREEN:TDD — PR1 (Group A) production implementation for the P3
// determinism-attestation spec (.correctless/specs/p3-determinism-attestation.md).
//
// This slice implements INV-001 (three-artifact status model + pure total
// classifier over the CLOSED legal-status table), INV-002 (per-role/per-kind
// comparison derivation with the committed registries + closed
// role/kind->projection-policy map), INV-003 (disagreement disposition),
// INV-004 (commit-anchored resource-floor campaign), INV-005 (serial-lane
// platform identity), and PRH-003 (receipt privacy scan). No signing (PR1 signs
// nothing — PRH-005). This assembly is Dafny-free (INV-008).
//
// The committed registries/policy-map/floor these derive from live under
// spikes/dafny-compat/manifest/determinism/*.json (never in-test literals —
// RS-020/AP-022): the LOAD methods read the file argument and NEVER hardcode
// the set, so a registry that silently shrinks is a reviewable committed diff.

using System.Text;
using System.Text.Json;

namespace Corrected.Spike.Contracts;

// ---------------------------------------------------------------- status enums

/// <summary>
/// INV-001: the RunReceipt execution axis. Orthogonal to <see cref="ComparisonStatus"/>;
/// only the four rows in the committed legal-status table are legal.
/// </summary>
public enum ExecutionStatus
{
    Completed,
    ResourceFloorSkipped,
    InfrastructureInvalid,
}

/// <summary>INV-001: the RunReceipt comparison axis.</summary>
public enum ComparisonStatus
{
    Equal,
    Different,
    NotEvaluated,
}

/// <summary>
/// INV-001/003/PRH-005: the signing outcome is a WORKFLOW fact OUTSIDE every
/// receipt; PR1 signs nothing, so only NotAttempted is ever produced in PR1.
/// </summary>
public enum SigningOutcome
{
    NotAttempted,
    Minted,
    Failed,
}

/// <summary>
/// INV-001: the mutually-exclusive raw outcome a RunnerInvocationOutcome records
/// for the two-nested-run controller path. The pure classifier maps each to
/// exactly one legal (execution_status, comparison_status) pair.
/// </summary>
public enum RunnerOutcomeKind
{
    CompletedProjectionsEqual,
    CompletedProjectionsDiffer,
    BelowResourceFloor,
    InfrastructureFault,
}

// ------------------------------------------------------------- receipt records

/// <summary>INV-001: a legal (execution_status, comparison_status) pair.</summary>
public readonly record struct ReceiptStatus(ExecutionStatus Execution, ComparisonStatus Comparison);

/// <summary>
/// INV-002: the per run x per role evidence cell the receipt carries — repo-relative
/// name, raw SHA-256, deterministic-projection SHA-256, projection schema/version+digest,
/// canonicalization version, a per-role verdict, and (RS-005 hardening) the digest of the
/// ACTUAL projection IMPLEMENTATION the producer ran — a SHA-256 of the real
/// DeterministicProjection output over a fixed committed self-test vector. A producer
/// running different projection code yields a different <see cref="ProjectionImplDigest"/>
/// on that vector, so Compare's manifest-pin cross-check fires on a REAL receipt (not only
/// on the recorded schema-identity string). Raw digests are EXPECTED to differ; equality is
/// a PROJECTION property.
/// </summary>
public sealed record RoleEvidence(
    string Role,
    string Kind,
    string RepoRelativeName,
    string RawSha256,
    string ProjectionSha256,
    string ProjectionSchemaId,
    int ProjectionSchemaVersion,
    string ProjectionSchemaDigest,
    string CanonicalizationVersion,
    string PerRoleVerdict,
    string ProjectionImplDigest);

/// <summary>INV-002 (RS-005): a projection-policy identity (schema id/version + digest).</summary>
public sealed record ProjectionPolicy(string SchemaId, int Version, string Digest);

/// <summary>INV-002: the derived comparison result + a typed rejection reason (null iff Equal/Different).</summary>
public sealed record ComparisonOutcome(ComparisonStatus Status, string? RejectionReason);

/// <summary>
/// INV-005: the pinned platform identity the dedicated serial job records into the
/// receipt: observed ProcessorCount, RID, a PINNED OS label (never floating
/// ubuntu-latest), and the actual runner image/kernel/architecture/resolved SDK.
/// </summary>
public sealed record PlatformIdentity(
    int ProcessorCount,
    string Rid,
    string OsLabel,
    string RunnerImage,
    string Kernel,
    string Architecture,
    string ResolvedSdk);

/// <summary>
/// INV-001/002/005/PRH-003: the (usually unsigned) RunReceipt for an ordinary terminal
/// run. It carries execution/comparison status, the per run x role evidence, the platform
/// identity, attested_commit, and the subject-manifest digest + policy version. It carries
/// NO attestation/verification status and NO ran-passed/satisfied field (ran-passed is
/// probe-derived, outside the signed subject — INV-001).
/// </summary>
public sealed record RunReceipt(
    ExecutionStatus Execution,
    ComparisonStatus Comparison,
    string AttestedCommit,
    string SubjectManifestDigest,
    string PolicyVersion,
    PlatformIdentity Platform,
    IReadOnlyList<RoleEvidence> Run1Evidence,
    IReadOnlyList<RoleEvidence> Run2Evidence);

/// <summary>
/// INV-003/PRH-005: the live-lane disposition derived from a receipt status. A
/// comparison_status=different run exits non-zero, signs nothing (NotAttempted),
/// is not mint-eligible, and reports the exact INV-003 message.
/// </summary>
public sealed record LaneDisposition(int ExitCode, SigningOutcome Signing, string? Message, bool MintEligible);

// ------------------------------------------------------------ campaign records

/// <summary>INV-004: one retained campaign row — {run_id, run_attempt, head_sha, plan_commit, plan_digest}.</summary>
public sealed record CampaignRow(string RunId, int RunAttempt, string HeadSha, string PlanCommit, string PlanDigest);

/// <summary>INV-004: the single-sourced core floor + the commit-anchored campaign plan header + retained rows.</summary>
public sealed record CampaignPlan(int CoreFloor, string PlanCommit, string PlanDigest, IReadOnlyList<CampaignRow> Rows);

// -------------------------------------------------------------- wire utilities

/// <summary>INV-001: snake_case wire token &lt;-&gt; PascalCase enum member conversion (the stable naming convention).</summary>
internal static class WireEnum
{
    public static string ToWire(Enum member)
    {
        var pascal = member.ToString();
        var sb = new StringBuilder(pascal.Length + 4);
        for (var i = 0; i < pascal.Length; i++)
        {
            var c = pascal[i];
            if (i > 0 && char.IsUpper(c))
            {
                sb.Append('_');
            }
            sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }

    public static T Parse<T>(string wire) where T : struct, Enum
    {
        // snake_case -> PascalCase, then Enum.Parse.
        var parts = wire.Split('_', StringSplitOptions.RemoveEmptyEntries);
        var sb = new StringBuilder(wire.Length);
        foreach (var part in parts)
        {
            sb.Append(char.ToUpperInvariant(part[0]));
            if (part.Length > 1)
            {
                sb.Append(part.Substring(1));
            }
        }
        // Enum.IsDefined guards the NUMERIC-token bypass (IB-001): Enum.TryParse
        // accepts a bare numeric string ("3", "999", "-1") as an UNDEFINED underlying
        // value, so TryParse alone would smuggle an out-of-domain enum past the closed
        // check. Require the parsed value to be a DEFINED member.
        if (!Enum.TryParse<T>(sb.ToString(), ignoreCase: true, out var value) || !Enum.IsDefined(value))
        {
            throw new InvalidOperationException(
                $"unrecognized {typeof(T).Name} wire token '{wire}' — not a member of the closed domain (INV-001)");
        }
        return value;
    }
}

// ------------------------------------------------------------- stub components

/// <summary>
/// INV-001/003: the PURE TOTAL classifier over the CLOSED legal-status table. No I/O,
/// no ProcessorCount read (that is the controller's observation) — a total function of
/// a controlled observation.
/// </summary>
public static class DeterminismClassifier
{
    // The CLOSED legal (execution_status, comparison_status) table — the same four
    // rows the committed manifest/determinism/legal-status-table.json carries. Any
    // combination NOT here is schema-invalid; no infrastructure fault or skip may
    // ever be comparison_status=different (the never-fail-open safety direction).
    private static readonly IReadOnlySet<ReceiptStatus> LegalTable = new HashSet<ReceiptStatus>
    {
        new(ExecutionStatus.Completed, ComparisonStatus.Equal),
        new(ExecutionStatus.Completed, ComparisonStatus.Different),
        new(ExecutionStatus.ResourceFloorSkipped, ComparisonStatus.NotEvaluated),
        new(ExecutionStatus.InfrastructureInvalid, ComparisonStatus.NotEvaluated),
    };

    /// <summary>Maps a raw runner outcome to exactly one legal (execution, comparison) pair.</summary>
    public static ReceiptStatus Classify(RunnerOutcomeKind outcome) => outcome switch
    {
        RunnerOutcomeKind.CompletedProjectionsEqual => new(ExecutionStatus.Completed, ComparisonStatus.Equal),
        RunnerOutcomeKind.CompletedProjectionsDiffer => new(ExecutionStatus.Completed, ComparisonStatus.Different),
        RunnerOutcomeKind.BelowResourceFloor => new(ExecutionStatus.ResourceFloorSkipped, ComparisonStatus.NotEvaluated),
        RunnerOutcomeKind.InfrastructureFault => new(ExecutionStatus.InfrastructureInvalid, ComparisonStatus.NotEvaluated),
        // Totality safety-direction (INV-001, guards AP-022): any future/unknown
        // RunnerOutcomeKind absorbs to infrastructure_invalid/not_evaluated —
        // NEVER a fail-open comparison_status=different on an unrecognized outcome.
        _ => new(ExecutionStatus.InfrastructureInvalid, ComparisonStatus.NotEvaluated),
    };

    /// <summary>True iff (execution, comparison) is one of the four legal rows; false for every schema-invalid combo.</summary>
    public static bool IsLegalStatusPair(ExecutionStatus execution, ComparisonStatus comparison) =>
        LegalTable.Contains(new ReceiptStatus(execution, comparison));

    /// <summary>Classifies a MISSING RunReceipt EXTERNALLY as infrastructure_invalid / not_evaluated (INV-001).</summary>
    public static ReceiptStatus ClassifyMissingReceipt() =>
        // A receipt-write failure / abrupt process death cannot self-report; the
        // fault is represented HERE, externally — never comparison_status=different.
        new(ExecutionStatus.InfrastructureInvalid, ComparisonStatus.NotEvaluated);
}

/// <summary>INV-003/PRH-005: derives the live-lane disposition from a receipt status.</summary>
public static class DeterminismDisposition
{
    /// <summary>INV-003: the exact disagreement message — strong evidence, NOT a proof of cause, NOT universal determinism.</summary>
    public const string DifferentMessage =
        "the declared deterministic projections differed in this observation under the recorded environment";

    /// <summary>Maps a receipt status to the lane disposition (exit code, signing outcome, message, mint-eligibility).</summary>
    public static LaneDisposition Dispose(ReceiptStatus status)
    {
        // The ONLY mint-eligible disposition (INV-003): completed AND equal. It
        // exits 0; PR1 still signs NOTHING (SigningOutcome.NotAttempted — no signer
        // exists until PR2 — PRH-005).
        if (status is { Execution: ExecutionStatus.Completed, Comparison: ComparisonStatus.Equal })
        {
            return new LaneDisposition(ExitCode: 0, Signing: SigningOutcome.NotAttempted, Message: null, MintEligible: true);
        }

        // A projection disagreement HARD-FAILS the live lane (INV-003): non-zero
        // exit, signs nothing, not mint-eligible, and the EXACT observation-scoped
        // message (never "proven nondeterminism" / universal). Never retried.
        if (status is { Execution: ExecutionStatus.Completed, Comparison: ComparisonStatus.Different })
        {
            return new LaneDisposition(ExitCode: 1, Signing: SigningOutcome.NotAttempted, Message: DifferentMessage, MintEligible: false);
        }

        // A below-floor skip is a VALID non-attesting outcome (INV-004): exit 0, no
        // mint, no signing. It is deliberately NOT a hard failure of the lane.
        if (status is { Execution: ExecutionStatus.ResourceFloorSkipped, Comparison: ComparisonStatus.NotEvaluated })
        {
            return new LaneDisposition(ExitCode: 0, Signing: SigningOutcome.NotAttempted, Message: null, MintEligible: false);
        }

        // infrastructure_invalid/not_evaluated — a genuine fault: non-zero, no mint,
        // no signing. Also the fail-closed default for any illegal/off-table pair.
        return new LaneDisposition(ExitCode: 1, Signing: SigningOutcome.NotAttempted, Message: null, MintEligible: false);
    }
}

/// <summary>
/// INV-002 (RS-020): loads the COMMITTED schema-kind + role registries and the closed
/// role/kind->projection-policy map from their committed artifact files — NEVER in-test
/// literals. A registry that silently shrank is then a reviewable committed-file diff.
/// </summary>
public static class DeterminismRegistries
{
    /// <summary>The committed schema report-KIND registry as a set.</summary>
    public static IReadOnlySet<string> SchemaKinds(string schemaKindRegistryPath)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(schemaKindRegistryPath));
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var k in doc.RootElement.GetProperty("kinds").EnumerateArray())
        {
            set.Add(k.GetString()!);
        }
        return set;
    }

    /// <summary>The committed artifact-ROLE registry as a set.</summary>
    public static IReadOnlySet<string> Roles(string roleRegistryPath)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(roleRegistryPath));
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var r in doc.RootElement.GetProperty("roles").EnumerateArray())
        {
            set.Add(r.GetString()!);
        }
        return set;
    }

    /// <summary>The committed closed role->kind map.</summary>
    public static IReadOnlyDictionary<string, string> RoleToKind(string roleRegistryPath)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(roleRegistryPath));
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var kv in doc.RootElement.GetProperty("role_to_kind").EnumerateObject())
        {
            map[kv.Name] = kv.Value.GetString()!;
        }
        return map;
    }

    /// <summary>The manifest-pinned projection policy for a (role, kind) via the closed map.</summary>
    public static ProjectionPolicy PinnedPolicy(string policyMapPath, string role, string kind)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(policyMapPath));
        foreach (var p in doc.RootElement.GetProperty("policies").EnumerateArray())
        {
            if (p.GetProperty("role").GetString() == role && p.GetProperty("kind").GetString() == kind)
            {
                return new ProjectionPolicy(
                    p.GetProperty("projection_schema_id").GetString()!,
                    p.GetProperty("projection_schema_version").GetInt32(),
                    p.GetProperty("projection_schema_digest").GetString()!);
            }
        }
        throw new InvalidOperationException(
            $"projection-policy-map.json has no pinned policy for (role={role}, kind={kind}) — the closed map must cover every role/kind (INV-002/RS-005)");
    }

    /// <summary>The committed top-level canonicalization version pinned by the policy map.</summary>
    public static string PinnedCanonicalizationVersion(string policyMapPath)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(policyMapPath));
        return doc.RootElement.GetProperty("canonicalization_version").GetString()!;
    }

    /// <summary>
    /// RS-005 hardening: the manifest-pinned projection-IMPLEMENTATION digest — the
    /// expected SHA-256 of the real DeterministicProjection output over the committed
    /// self-test vector. Compare cross-checks each recorded impl-digest against this, so a
    /// producer running a DIFFERENT projection (a degenerate/no-op) is caught on a real
    /// receipt, not only via the recorded schema-identity string.
    /// </summary>
    public static string PinnedProjectionImplDigest(string policyMapPath)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(policyMapPath));
        return doc.RootElement.GetProperty("projection_impl_digest").GetString()!;
    }

    /// <summary>The committed repo-relative self-test vector the impl-digest is computed over.</summary>
    public static string ProjectionSelfTestVector(string policyMapPath)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(policyMapPath));
        return doc.RootElement.GetProperty("projection_self_test_vector").GetString()!;
    }

    /// <summary>
    /// Computes the projection-IMPLEMENTATION digest by running the REAL
    /// EvidenceSchema.DeterministicProjection over the committed self-test vector and
    /// hashing its output. Different projection code -> different digest on the same vector.
    /// </summary>
    public static string ComputeProjectionImplDigest(string schemaPath, string selfTestVectorAbsolutePath)
    {
        var projection = EvidenceSchema.DeterministicProjection(File.ReadAllText(selfTestVectorAbsolutePath), schemaPath);
        var bytes = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(projection));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}

/// <summary>
/// INV-002: derives comparison_status over the two-run corpus. equal ONLY when kinds set-equal
/// to the schema-kind registry, roles set-equal to the role registry, every role->declared kind,
/// each role appears once per run, every projection digest matches, AND every recorded projection
/// policy is set-equal to the manifest-pinned policy (RS-005). Off-manifest policy => rejected.
/// </summary>
public static class DeterminismComparison
{
    // Typed rejection reasons (INV-012 vocabulary). equal/different carry a null reason.
    public const string ProjectionPolicyMismatch = "projection-policy-mismatch";
    public const string RoleSetMismatch = "role-set-mismatch";
    public const string KindSetMismatch = "kind-set-mismatch";
    public const string RoleKindMapViolation = "role-kind-map-violation";
    public const string DuplicateRole = "duplicate-role";

    /// <summary>Derives the comparison outcome over run1/run2 role-evidence vs the committed registries + policy map.</summary>
    public static ComparisonOutcome Compare(
        IReadOnlyList<RoleEvidence> run1,
        IReadOnlyList<RoleEvidence> run2,
        string schemaKindRegistryPath,
        string roleRegistryPath,
        string projectionPolicyMapPath)
    {
        var committedKinds = DeterminismRegistries.SchemaKinds(schemaKindRegistryPath);
        var committedRoles = DeterminismRegistries.Roles(roleRegistryPath);
        var roleToKind = DeterminismRegistries.RoleToKind(roleRegistryPath);
        var pinnedCanonicalization = DeterminismRegistries.PinnedCanonicalizationVersion(projectionPolicyMapPath);
        var pinnedImplDigest = DeterminismRegistries.PinnedProjectionImplDigest(projectionPolicyMapPath);

        // (1) each role appears EXACTLY ONCE per run (no shrink/duplicate). A
        // duplicated role can never mint equal (INV-002).
        if (!TryIndexUnique(run1, out var idx1) || !TryIndexUnique(run2, out var idx2))
        {
            return new ComparisonOutcome(ComparisonStatus.NotEvaluated, DuplicateRole);
        }

        // (2) role set set-equal to the committed role registry in BOTH runs.
        if (!idx1.Keys.ToHashSet().SetEquals(committedRoles) || !idx2.Keys.ToHashSet().SetEquals(committedRoles))
        {
            return new ComparisonOutcome(ComparisonStatus.NotEvaluated, RoleSetMismatch);
        }

        // (3) kind set (the distinct kinds recorded across the corpus) set-equal to
        // the committed schema-kind registry.
        var recordedKinds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var e in idx1.Values.Concat(idx2.Values))
        {
            recordedKinds.Add(e.Kind);
        }
        if (!recordedKinds.SetEquals(committedKinds))
        {
            return new ComparisonOutcome(ComparisonStatus.NotEvaluated, KindSetMismatch);
        }

        // (4) every role maps to its declared kind (the closed role->kind map is
        // total over the corpus); a role declaring a kind outside its mapping fails.
        foreach (var e in idx1.Values.Concat(idx2.Values))
        {
            if (!roleToKind.TryGetValue(e.Role, out var declaredKind) || declaredKind != e.Kind)
            {
                return new ComparisonOutcome(ComparisonStatus.NotEvaluated, RoleKindMapViolation);
            }
        }

        // (5) every RECORDED per-role projection-policy identity is set-equal to the
        // manifest-pinned policy via the closed role/kind->policy map (RS-005). An
        // off-manifest degenerate projection is rejected EVEN IF both runs agree on
        // it — "set-equal to the manifest pin" is NOT "the two runs agree". Checked
        // BEFORE projection equality, so a policy mismatch is never masked by
        // matching projection hashes.
        foreach (var e in idx1.Values.Concat(idx2.Values))
        {
            var pinned = DeterminismRegistries.PinnedPolicy(projectionPolicyMapPath, e.Role, e.Kind);
            var recordedMatchesPin =
                e.ProjectionSchemaId == pinned.SchemaId
                && e.ProjectionSchemaVersion == pinned.Version
                && string.Equals(e.ProjectionSchemaDigest, pinned.Digest, StringComparison.OrdinalIgnoreCase)
                && e.CanonicalizationVersion == pinnedCanonicalization
                // RS-005 hardening: the recorded projection-IMPLEMENTATION digest must equal
                // the manifest-pinned impl-digest. A producer running a DIFFERENT projection
                // (even one that stamps the pinned schema-identity string) yields a different
                // impl-digest over the committed self-test vector -> rejected on a real receipt.
                && string.Equals(e.ProjectionImplDigest, pinnedImplDigest, StringComparison.OrdinalIgnoreCase);
            if (!recordedMatchesPin)
            {
                return new ComparisonOutcome(ComparisonStatus.NotEvaluated, ProjectionPolicyMismatch);
            }
        }

        // (6) equality is a PROJECTION property (raw digests are expected to
        // differ): every per-role projection digest matches across the two runs =>
        // equal; any per-role projection digest differs => a genuine `different`.
        var allProjectionsAgree = true;
        foreach (var role in committedRoles)
        {
            if (idx1[role].ProjectionSha256 != idx2[role].ProjectionSha256)
            {
                allProjectionsAgree = false;
                break;
            }
        }

        return allProjectionsAgree
            ? new ComparisonOutcome(ComparisonStatus.Equal, null)
            : new ComparisonOutcome(ComparisonStatus.Different, null);
    }

    private static bool TryIndexUnique(IReadOnlyList<RoleEvidence> run, out Dictionary<string, RoleEvidence> index)
    {
        index = new Dictionary<string, RoleEvidence>(StringComparer.Ordinal);
        foreach (var e in run)
        {
            if (!index.TryAdd(e.Role, e))
            {
                return false; // a role appears more than once in this run
            }
        }
        return true;
    }
}

/// <summary>
/// INV-004: the single-sourced core floor + the predeclared, commit-anchored measurement
/// campaign. From-clean assertions only: plan_commit ancestor-of head_sha, attempt-1,
/// single-source, retained rows committed. (run_id&lt;-&gt;head_sha association is CI-network scope.)
/// </summary>
public static class ResourceFloorCampaign
{
    /// <summary>Loads the committed single-sourced floor + campaign rows.</summary>
    public static CampaignPlan Load(string floorPath, string rowsPath)
    {
        using var floorDoc = JsonDocument.Parse(File.ReadAllText(floorPath));
        var coreFloor = floorDoc.RootElement.GetProperty("core_floor").GetInt32();
        var planCommit = floorDoc.RootElement.GetProperty("plan_commit").GetString()!;
        var planDigest = floorDoc.RootElement.GetProperty("plan_digest").GetString()!;

        using var rowsDoc = JsonDocument.Parse(File.ReadAllText(rowsPath));
        var rows = new List<CampaignRow>();
        foreach (var r in rowsDoc.RootElement.GetProperty("rows").EnumerateArray())
        {
            rows.Add(new CampaignRow(
                r.GetProperty("run_id").GetString()!,
                r.GetProperty("run_attempt").GetInt32(),
                r.GetProperty("head_sha").GetString()!,
                r.GetProperty("plan_commit").GetString()!,
                r.GetProperty("plan_digest").GetString()!));
        }

        return new CampaignPlan(coreFloor, planCommit, planDigest, rows);
    }

    /// <summary>The single-sourced core-floor constant (loaded from the ONE committed file).</summary>
    public static int CoreFloor(string floorPath)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(floorPath));
        return doc.RootElement.GetProperty("core_floor").GetInt32();
    }

    /// <summary>True iff plan_commit is an ANCESTOR of head_sha (git). The plan cannot be chosen after seeing results.</summary>
    public static bool PlanPredatesRun(string planCommit, string headSha)
    {
        // `git merge-base --is-ancestor A B` exits 0 iff A is an ancestor of B, 1
        // otherwise. A descendant->ancestor query (e.g. HEAD vs its own parent)
        // therefore returns false — an always-true stub cannot pass (AP-005). The
        // git subprocess is launched ONLY through the sanctioned managed launcher
        // (PRH-004 — never a direct process spawn outside the launcher component).
        var repoRoot = GitResolver.FindRepoRoot(AppContext.BaseDirectory)
            ?? throw new InvalidOperationException(
                "could not locate the git repo root above the assembly base directory — ancestry cannot be computed (INV-004)");

        var env = new Dictionary<string, string>
        {
            ["PATH"] = "/usr/bin:/bin:/usr/local/bin",
        };
        var home = Environment.GetEnvironmentVariable("HOME");
        env["HOME"] = string.IsNullOrEmpty(home) ? repoRoot : home!;

        var result = ManagedLauncher.Launch(new LaunchRequest(
            ExecutablePath: ResolveGitBinary(),
            Argv: new[] { "merge-base", "--is-ancestor", planCommit, headSha },
            WorkingDirectory: repoRoot,
            EnvironmentProfile: env,
            TimeoutSeconds: 15));

        // ExitCode is null on signal death (never a clean 0/1) — treat as not-an-ancestor.
        return result.ExitCode == 0;
    }

    private static string ResolveGitBinary()
    {
        foreach (var dir in new[] { "/usr/bin", "/bin", "/usr/local/bin" })
        {
            var candidate = Path.Combine(dir, "git");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }
        return "git"; // last-resort PATH resolution
    }
}

/// <summary>
/// PRH-003: a scan RESTRICTED to Corrected-authored receipt/predicate fields (never the
/// opaque Sigstore bundle) for local-environment identity leaks — hostname, username,
/// home/temp/absolute-local path.
/// </summary>
public static class ReceiptPrivacyScan
{
    /// <summary>Returns the Corrected-authored field paths that contain a local-identity leak (empty = clean).</summary>
    public static IReadOnlyList<string> LocalIdentityLeaks(string receiptJson)
    {
        var leaks = new List<string>();
        using var doc = JsonDocument.Parse(receiptJson);
        Walk(doc.RootElement, "$", leaks);
        return leaks;
    }

    private static void Walk(JsonElement node, string path, List<string> leaks)
    {
        switch (node.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var prop in node.EnumerateObject())
                {
                    Walk(prop.Value, path == "$" ? prop.Name : $"{path}.{prop.Name}", leaks);
                }
                break;
            case JsonValueKind.Array:
                var i = 0;
                foreach (var item in node.EnumerateArray())
                {
                    Walk(item, $"{path}[{i++}]", leaks);
                }
                break;
            case JsonValueKind.String:
                if (LooksLikeLocalIdentityLeak(node.GetString()!))
                {
                    leaks.Add(path);
                }
                break;
        }
    }

    // Absolute-local-path / temp-dir markers (deterministic; catch the fabricated
    // fixture leaks and never a repo-relative name or a version string).
    private static readonly string[] LocalPathMarkers =
    {
        "/home/", "/users/", "/root/", "/tmp/", "/var/tmp", "/private/var",
        "\\users\\", "c:\\", "%userprofile%", "%homepath%", "$home", "${home}",
    };

    private static bool LooksLikeLocalIdentityLeak(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }
        var lower = value.ToLowerInvariant();

        // (a) an absolute local / temp path anywhere in the value.
        foreach (var marker in LocalPathMarkers)
        {
            if (lower.Contains(marker))
            {
                return true;
            }
        }
        if (lower.StartsWith("/tmp", StringComparison.Ordinal) || lower.StartsWith("/home", StringComparison.Ordinal)
            || lower.StartsWith("/users", StringComparison.Ordinal) || lower.StartsWith("/root", StringComparison.Ordinal))
        {
            return true;
        }

        // (b) the ACTUAL local environment identity of the emitting host — the real
        // leak vector in a genuine receipt (never present in the clean fixture).
        foreach (var identity in LocalIdentityTokens())
        {
            if (identity.Length >= 3 && lower.Contains(identity))
            {
                return true;
            }
        }

        // (c) free-text host/user disclosure markers ("... on host X", "built by Y").
        if (lower.Contains(" on host ") || lower.Contains("built by "))
        {
            return true;
        }

        return false;
    }

    // Generic CI/system account + OS names that are common substrings (e.g. the
    // GitHub Actions user "runner", "root", "ubuntu"). They CANNOT distinguish a
    // real developer-identity leak from a coincidental match and would false-fail
    // the FAIL-CLOSED production wiring (QA-002) on a legitimate receipt — so a host
    // whose user/host equals one of these contributes no identity token. This
    // mirrors Inv009's HygieneGrep genericAccounts exclusion (AP-004).
    private static readonly HashSet<string> GenericIdentities = new(StringComparer.OrdinalIgnoreCase)
    {
        "runner", "runneradmin", "root", "ubuntu", "user", "admin", "build",
        "test", "ci", "github", "actions", "vsts", "azureuser", "ec2-user",
        "localhost", "debian", "fedora", "linux",
    };

    private static IReadOnlyList<string> LocalIdentityTokens()
    {
        var tokens = new List<string>();

        // Distinctive short identifiers (user/host): flagged only when they are NOT
        // a generic account name and are long enough to be distinctive (>= 4).
        void AddIdentifier(string? s)
        {
            if (!string.IsNullOrWhiteSpace(s) && s!.Length >= 4 && !GenericIdentities.Contains(s.Trim()))
            {
                tokens.Add(s.ToLowerInvariant());
            }
        }
        // Absolute home/temp paths: flagged whenever the literal path appears (a
        // legitimate receipt uses repo-relative names, never the absolute HOME/TMP).
        void AddPath(string? s)
        {
            if (!string.IsNullOrWhiteSpace(s) && s!.StartsWith("/", StringComparison.Ordinal) && s.Length >= 2)
            {
                tokens.Add(s.ToLowerInvariant());
            }
        }

        AddIdentifier(Environment.MachineName);
        AddIdentifier(Environment.UserName);
        AddIdentifier(Environment.GetEnvironmentVariable("USER"));
        AddIdentifier(Environment.GetEnvironmentVariable("HOSTNAME"));
        AddPath(Environment.GetEnvironmentVariable("HOME"));
        AddPath(Environment.GetEnvironmentVariable("TMPDIR"));
        AddIdentifier(Environment.GetEnvironmentVariable("USERPROFILE"));
        return tokens;
    }
}

/// <summary>INV-002/005: parses an emitted determinism RunReceipt (the real receipt writer's output).</summary>
public static class RunReceiptCodec
{
    /// <summary>Parses a committed/emitted determinism receipt into the typed RunReceipt.</summary>
    public static RunReceipt Parse(string receiptJson)
    {
        using var doc = JsonDocument.Parse(receiptJson);
        var root = doc.RootElement;

        var execution = WireEnum.Parse<ExecutionStatus>(root.GetProperty("execution_status").GetString()!);
        var comparison = WireEnum.Parse<ComparisonStatus>(root.GetProperty("comparison_status").GetString()!);
        var attestedCommit = root.GetProperty("attested_commit").GetString()!;
        var subjectManifestDigest = root.GetProperty("subject_manifest_digest").GetString()!;
        var policyVersion = root.GetProperty("policy_version").GetString()!;

        var platform = ParsePlatform(root.GetProperty("platform"));
        var run1 = ParseEvidence(root.GetProperty("run1_evidence"));
        var run2 = ParseEvidence(root.GetProperty("run2_evidence"));

        return new RunReceipt(execution, comparison, attestedCommit, subjectManifestDigest, policyVersion, platform, run1, run2);
    }

    private static PlatformIdentity ParsePlatform(JsonElement p) => new(
        p.GetProperty("processor_count").GetInt32(),
        p.GetProperty("rid").GetString()!,
        p.GetProperty("os_label").GetString()!,
        p.GetProperty("runner_image").GetString()!,
        p.GetProperty("kernel").GetString()!,
        p.GetProperty("architecture").GetString()!,
        p.GetProperty("resolved_sdk").GetString()!);

    private static IReadOnlyList<RoleEvidence> ParseEvidence(JsonElement arr)
    {
        var list = new List<RoleEvidence>();
        foreach (var e in arr.EnumerateArray())
        {
            list.Add(new RoleEvidence(
                e.GetProperty("role").GetString()!,
                e.GetProperty("kind").GetString()!,
                e.GetProperty("repo_relative_name").GetString()!,
                e.GetProperty("raw_sha256").GetString()!,
                e.GetProperty("projection_sha256").GetString()!,
                e.GetProperty("projection_schema_id").GetString()!,
                e.GetProperty("projection_schema_version").GetInt32(),
                e.GetProperty("projection_schema_digest").GetString()!,
                e.GetProperty("canonicalization_version").GetString()!,
                e.GetProperty("per_role_verdict").GetString()!,
                e.GetProperty("projection_impl_digest").GetString()!));
        }
        return list;
    }

    /// <summary>
    /// Serializes a RunReceipt to the canonical wire format the codec parses and the
    /// PRH-003 scan reviews (the real determinism-lane receipt writer emits this).
    /// </summary>
    public static string Serialize(RunReceipt receipt)
    {
        var opts = new JsonWriterOptions { Indented = true };
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, opts))
        {
            w.WriteStartObject();
            w.WriteString("execution_status", WireEnum.ToWire(receipt.Execution));
            w.WriteString("comparison_status", WireEnum.ToWire(receipt.Comparison));
            w.WriteString("attested_commit", receipt.AttestedCommit);
            w.WriteString("subject_manifest_digest", receipt.SubjectManifestDigest);
            w.WriteString("policy_version", receipt.PolicyVersion);

            w.WritePropertyName("platform");
            w.WriteStartObject();
            w.WriteNumber("processor_count", receipt.Platform.ProcessorCount);
            w.WriteString("rid", receipt.Platform.Rid);
            w.WriteString("os_label", receipt.Platform.OsLabel);
            w.WriteString("runner_image", receipt.Platform.RunnerImage);
            w.WriteString("kernel", receipt.Platform.Kernel);
            w.WriteString("architecture", receipt.Platform.Architecture);
            w.WriteString("resolved_sdk", receipt.Platform.ResolvedSdk);
            w.WriteEndObject();

            WriteEvidence(w, "run1_evidence", receipt.Run1Evidence);
            WriteEvidence(w, "run2_evidence", receipt.Run2Evidence);
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteEvidence(Utf8JsonWriter w, string name, IReadOnlyList<RoleEvidence> evidence)
    {
        w.WritePropertyName(name);
        w.WriteStartArray();
        foreach (var e in evidence)
        {
            w.WriteStartObject();
            w.WriteString("role", e.Role);
            w.WriteString("kind", e.Kind);
            w.WriteString("repo_relative_name", e.RepoRelativeName);
            w.WriteString("raw_sha256", e.RawSha256);
            w.WriteString("projection_sha256", e.ProjectionSha256);
            w.WriteString("projection_schema_id", e.ProjectionSchemaId);
            w.WriteNumber("projection_schema_version", e.ProjectionSchemaVersion);
            w.WriteString("projection_schema_digest", e.ProjectionSchemaDigest);
            w.WriteString("canonicalization_version", e.CanonicalizationVersion);
            w.WriteString("per_role_verdict", e.PerRoleVerdict);
            w.WriteString("projection_impl_digest", e.ProjectionImplDigest);
            w.WriteEndObject();
        }
        w.WriteEndArray();
    }
}
