// PR1 (Group A) RED tests — INV-002 slice ONLY.
//
// INV-002: comparison is derived over 3 schema KINDS (run-report, route-report,
// control-report) × 5 artifact ROLES (run, route-a, route-b, control-a,
// control-b) with a closed role->kind map. 2 runs × 5 roles = 10 artifacts, 5
// comparisons. comparison_status=equal ONLY when: kind-set set-equal to the
// schema-kind registry, role-set set-equal to the committed role registry, every
// role->declared kind, each role appears once per run, every projection digest
// matches, AND every recorded projection-policy identity is set-equal to the
// manifest-pinned policy via the closed role/kind->projection-policy map. An
// off-manifest recorded policy => projection-policy-mismatch (rejected). Raw
// digests are EXPECTED to differ; equality is a PROJECTION property.
//
// The kind/role registries AND the policy map are COMMITTED ARTIFACT FILES
// (manifest/determinism/*.json) the set-equalities derive from — NEVER in-test
// Dictionaries/literals (RS-020/AP-022). The hand-constructed RoleEvidence below
// is the INV-013 layer-1 pure-policy matrix (fixture INPUTS, not the registry);
// the [integration] Exit test drives the REAL emitted receipt (AP-014 — the real
// producer). RED: DeterminismComparison/DeterminismRegistries/RunReceiptCodec
// bodies throw NotImplementedException (STUB:TDD).
using System.Text.Json;
using Corrected.Spike.Contracts;
using Xunit;

namespace Corrected.Spike.Tests;

public class Pr1Inv002ComparisonTests
{
    private static string KindRegistryPath => SpikePaths.P("manifest", "determinism", "schema-kind-registry.json");
    private static string RoleRegistryPath => SpikePaths.P("manifest", "determinism", "role-registry.json");
    private static string PolicyMapPath => SpikePaths.P("manifest", "determinism", "projection-policy-map.json");

    // The pinned per-role projection-policy digest (== the committed evidence-schema
    // digest, the identity in projection-policy-map.json). Fixture inputs carry this;
    // an OFF-MANIFEST digest must be rejected.
    private const string PinnedDigest = "c872c710dd390ff8d8050c059077d0eb7d6ef4f2352fc7bf375403014ac18509";

    // RS-005 hardening (QA-005): the manifest-pinned projection-IMPLEMENTATION digest,
    // LOADED from the committed policy map (never an in-test literal — RS-020). A
    // well-formed cell records this; a MISMATCHED impl-digest must be rejected.
    private static readonly string PinnedImplDigest = DeterminismRegistries.PinnedProjectionImplDigest(PolicyMapPath);

    // Fixture-construction map (INPUTS, not the enforced registry).
    private static readonly (string Role, string Kind)[] RolesKinds =
    {
        ("run", "run-report"),
        ("route-a", "route-report"),
        ("route-b", "route-report"),
        ("control-a", "control-report"),
        ("control-b", "control-report"),
    };

    private static RoleEvidence Cell(string role, string kind, int run, string projSha, string policyDigest = PinnedDigest, string? implDigest = null) =>
        new(role, kind, $"reports/{role}.json",
            RawSha256: $"raw-{role}-run{run}",                // raw digests are EXPECTED to differ per run
            ProjectionSha256: projSha,
            ProjectionSchemaId: "corrected.determinism.projection",
            ProjectionSchemaVersion: 2,
            ProjectionSchemaDigest: policyDigest,
            CanonicalizationVersion: "1",
            PerRoleVerdict: "equal",
            ProjectionImplDigest: implDigest ?? PinnedImplDigest);

    private static List<RoleEvidence> Run(int run, Func<string, string> projSha, string policyDigest = PinnedDigest, string? implDigest = null) =>
        RolesKinds.Select(rk => Cell(rk.Role, rk.Kind, run, projSha(rk.Role), policyDigest, implDigest)).ToList();

    private static ComparisonOutcome Compare(IReadOnlyList<RoleEvidence> r1, IReadOnlyList<RoleEvidence> r2) =>
        DeterminismComparison.Compare(r1, r2, KindRegistryPath, RoleRegistryPath, PolicyMapPath);

    // Tests INV-002 [unit] (RS-020): the committed schema-kind registry is loaded
    // from its FILE and is set-equal to the report `kind` enum in the evidence
    // schema — a registry that silently shrank is a reviewable committed diff.
    [Fact]
    public void SchemaKindRegistry_LoadsFromFile_SetEqualToSchemaEnum()
    {
        using var schema = SpikePaths.Json(SpikePaths.P("schema", "evidence-schema.json"));
        var declared = schema.RootElement.GetProperty("report_schema").GetProperty("properties")
            .GetProperty("kind").GetProperty("enum").EnumerateArray().Select(k => k.GetString()!).ToHashSet();
        Assert.Equal(declared, DeterminismRegistries.SchemaKinds(KindRegistryPath).ToHashSet());
    }

    // Tests INV-002 [unit]: the committed role registry has EXACTLY the five roles
    // and its role->kind map is total and matches the spec (run->run-report;
    // route-a,route-b->route-report; control-a,control-b->control-report). Five
    // roles => five comparisons and (× 2 runs) ten artifacts.
    [Fact]
    public void RoleRegistry_HasExactlyFiveRoles_TotalRoleToKindMap()
    {
        var roles = DeterminismRegistries.Roles(RoleRegistryPath);
        Assert.Equal(new HashSet<string> { "run", "route-a", "route-b", "control-a", "control-b" }, roles.ToHashSet());
        Assert.Equal(5, roles.Count); // 5 comparisons; 2 runs × 5 = 10 artifacts

        var map = DeterminismRegistries.RoleToKind(RoleRegistryPath);
        Assert.Equal("run-report", map["run"]);
        Assert.Equal("route-report", map["route-a"]);
        Assert.Equal("route-report", map["route-b"]);
        Assert.Equal("control-report", map["control-a"]);
        Assert.Equal("control-report", map["control-b"]);
        Assert.Equal(5, map.Count); // total over every role, no extras
    }

    // Tests INV-002 [unit] (RS-005): the closed role/kind->projection-policy map
    // pins a policy for every role at its declared kind; the pinned digest is the
    // committed evidence-schema identity.
    [Fact]
    public void ProjectionPolicyMap_IsClosedOverEveryRoleKind()
    {
        foreach (var (role, kind) in RolesKinds)
        {
            var policy = DeterminismRegistries.PinnedPolicy(PolicyMapPath, role, kind);
            Assert.Equal("corrected.determinism.projection", policy.SchemaId);
            Assert.Equal(2, policy.Version);
            Assert.Equal(PinnedDigest, policy.Digest);
        }
    }

    // Tests INV-002 [unit]: a well-formed two-run corpus (all 5 roles once per
    // run, correct role->kind, on-manifest policy, matching projection digests)
    // derives comparison_status=equal.
    [Fact]
    public void Compare_WellFormedMatchingCorpus_IsEqual()
    {
        var outcome = Compare(Run(1, r => $"proj-{r}"), Run(2, r => $"proj-{r}"));
        Assert.Equal(ComparisonStatus.Equal, outcome.Status);
        Assert.Null(outcome.RejectionReason);
    }

    // Tests INV-002 [unit] (RS-005): a degenerate/no-op projection recording an
    // OFF-MANIFEST policy identity is rejected as projection-policy-mismatch — so
    // `equal` cannot be minted from hashes produced by a projection other than the
    // manifest-pinned one, even when the projection digests happen to match.
    [Fact]
    public void Compare_OffManifestPolicy_IsRejected_ProjectionPolicyMismatch()
    {
        var offManifest = new string('a', 64); // a plausible-but-unpinned projection-schema digest
        var outcome = Compare(Run(1, r => $"proj-{r}"), Run(2, r => $"proj-{r}", policyDigest: offManifest));
        Assert.Equal(ComparisonStatus.NotEvaluated, outcome.Status);
        Assert.Equal("projection-policy-mismatch", outcome.RejectionReason);
    }

    // Tests INV-002 [unit] (RS-005 — B2 symmetric off-manifest): BOTH runs record
    // the SAME off-manifest degenerate projection policy with MATCHING projection
    // SHAs. A mere run1==run2 cross-run-consistency check would mint `equal`; the
    // manifest-pin set-equality must REJECT it as projection-policy-mismatch. This
    // distinguishes "set-equal to the manifest pin" from "the two runs agree",
    // closing the real degenerate-projection attack (equal must NOT be minted).
    [Fact]
    public void Compare_BothRunsOffManifestButConsistent_IsRejected_ProjectionPolicyMismatch()
    {
        var offManifest = new string('b', 64); // an unpinned degenerate projection identity, recorded by BOTH runs
        var outcome = Compare(
            Run(1, r => $"proj-{r}", policyDigest: offManifest),
            Run(2, r => $"proj-{r}", policyDigest: offManifest));
        Assert.Equal(ComparisonStatus.NotEvaluated, outcome.Status);
        Assert.Equal("projection-policy-mismatch", outcome.RejectionReason);
    }

    // Tests INV-002 [unit]: a shrunk role set (control-b dropped) is NOT set-equal
    // to the committed role registry => not_evaluated (never a silent `equal`).
    [Fact]
    public void Compare_ShrunkRoleSet_IsRejected_NotEvaluated()
    {
        var r1 = Run(1, r => $"proj-{r}").Where(e => e.Role != "control-b").ToList();
        var r2 = Run(2, r => $"proj-{r}").Where(e => e.Role != "control-b").ToList();
        var outcome = Compare(r1, r2);
        Assert.Equal(ComparisonStatus.NotEvaluated, outcome.Status);
    }

    // Tests INV-002 [unit]: a role appearing twice in a run (control-a duplicated,
    // control-b absent) violates "each role appears once per run" => not_evaluated.
    [Fact]
    public void Compare_DuplicatedRole_IsRejected_NotEvaluated()
    {
        var r1 = Run(1, r => $"proj-{r}").Where(e => e.Role != "control-b").ToList();
        r1.Add(Cell("control-a", "control-report", 1, "proj-control-a")); // control-a now appears twice
        var r2 = Run(2, r => $"proj-{r}");
        var outcome = Compare(r1, r2);
        Assert.Equal(ComparisonStatus.NotEvaluated, outcome.Status);
    }

    // Tests INV-002 [unit]: a role that declares a kind outside its committed
    // role->kind mapping (route-a claiming control-report) => not_evaluated.
    [Fact]
    public void Compare_RoleDeclaresWrongKind_IsRejected_NotEvaluated()
    {
        var r1 = Run(1, r => $"proj-{r}");
        r1[r1.FindIndex(e => e.Role == "route-a")] = Cell("route-a", "control-report", 1, "proj-route-a");
        var r2 = Run(2, r => $"proj-{r}");
        var outcome = Compare(r1, r2);
        Assert.Equal(ComparisonStatus.NotEvaluated, outcome.Status);
    }

    // Tests INV-002 [unit]: a per-role PROJECTION digest that differs between the
    // two runs derives comparison_status=different — a real disagreement, NOT a
    // structural not_evaluated and NOT `equal`.
    [Fact]
    public void Compare_ProjectionDigestDiffers_IsDifferent()
    {
        var r2 = Run(2, r => r == "route-b" ? "proj-route-b-DIFFERENT" : $"proj-{r}");
        var outcome = Compare(Run(1, r => $"proj-{r}"), r2);
        Assert.Equal(ComparisonStatus.Different, outcome.Status);
    }

    // Tests INV-002 [unit]: equality is a PROJECTION property, not a raw-byte one —
    // raw digests differ across the two runs by construction, yet with matching
    // projection digests the outcome is `equal`.
    [Fact]
    public void Compare_RawBytesDiffer_ButProjectionsAgree_IsEqual()
    {
        var r1 = Run(1, r => $"proj-{r}");
        var r2 = Run(2, r => $"proj-{r}");
        // Precondition (green): the raw digests genuinely differ across runs.
        Assert.NotEqual(r1.Single(e => e.Role == "run").RawSha256, r2.Single(e => e.Role == "run").RawSha256);
        Assert.Equal(ComparisonStatus.Equal, Compare(r1, r2).Status);
    }

    // Tests INV-002 [unit] (RS-005 hardening — QA-005): a MISMATCHED projection-IMPL
    // digest is rejected as projection-policy-mismatch even when the recorded
    // schema-identity string and the projection SHAs match. Before the impl-digest
    // cross-check, a producer running a DIFFERENT projection that stamps the pinned
    // schema-identity minted `equal`; now the recorded-impl-digest != manifest-pin
    // closes it on a real receipt. FAILS without the QA-005 Compare step-5 tightening.
    [Fact]
    public void Compare_MismatchedProjectionImplDigest_IsRejected_ProjectionPolicyMismatch()
    {
        var wrongImpl = new string('c', 64); // an off-pin projection-implementation digest
        var outcome = Compare(
            Run(1, r => $"proj-{r}", implDigest: wrongImpl),
            Run(2, r => $"proj-{r}", implDigest: wrongImpl));
        Assert.Equal(ComparisonStatus.NotEvaluated, outcome.Status);
        Assert.Equal("projection-policy-mismatch", outcome.RejectionReason);
    }

    // Tests INV-002 [unit] (RS-005 hardening — QA-005): the committed pinned
    // impl-digest EQUALS the SHA-256 of the REAL EvidenceSchema.DeterministicProjection
    // output over the committed self-test vector — the pin is a genuine product of the
    // reviewed projection code, not an arbitrary committed constant. A drift between the
    // projection code (or the vector) and the pin fails here (a reviewable diff).
    [Fact]
    public void ProjectionImplDigest_PinEqualsRealProjectionOverCommittedSelfTestVector()
    {
        var schema = SpikePaths.P("schema", "evidence-schema.json");
        var vectorRel = DeterminismRegistries.ProjectionSelfTestVector(PolicyMapPath);
        var vectorAbs = SpikePaths.P(vectorRel.Split('/'));
        var computed = DeterminismRegistries.ComputeProjectionImplDigest(schema, vectorAbs);
        Assert.Equal(DeterminismRegistries.PinnedProjectionImplDigest(PolicyMapPath), computed);
        Assert.Equal(64, computed.Length); // a real SHA-256, not a placeholder/empty
    }

    // Tests INV-002 [unit] (RS-020 — QA-004, A2): the registry loaders READ THE ARG
    // PATH — pointed at a TEMP file carrying a deliberately different/shrunk set, each
    // loader reflects THAT file (a hardcoded loader that ignored its arg would fail).
    [Fact]
    public void RegistryLoaders_ReadTheArgPath_NotAHardcodedSet()
    {
        var tmp = SpikePaths.TestScratch("qa004-registry-argsensitivity");

        var kindFile = Path.Combine(tmp, "kinds.json");
        File.WriteAllText(kindFile, /*lang=json*/ "{ \"kinds\": [\"only-one-kind\"] }");
        Assert.Equal(new HashSet<string> { "only-one-kind" }, DeterminismRegistries.SchemaKinds(kindFile).ToHashSet());
        // and it is NOT the committed set (the arg genuinely steers the output)
        Assert.NotEqual(DeterminismRegistries.SchemaKinds(KindRegistryPath).ToHashSet(),
                        DeterminismRegistries.SchemaKinds(kindFile).ToHashSet());

        var roleFile = Path.Combine(tmp, "roles.json");
        File.WriteAllText(roleFile, /*lang=json*/ "{ \"roles\": [\"solo\"], \"role_to_kind\": { \"solo\": \"solo-kind\" } }");
        Assert.Equal(new HashSet<string> { "solo" }, DeterminismRegistries.Roles(roleFile).ToHashSet());
        var map = DeterminismRegistries.RoleToKind(roleFile);
        Assert.Equal("solo-kind", map["solo"]);
        Assert.Single(map);

        var polFile = Path.Combine(tmp, "policy.json");
        File.WriteAllText(polFile, /*lang=json*/
            "{ \"canonicalization_version\": \"9\", \"projection_self_test_vector\": \"x/y.json\", "
            + "\"projection_impl_digest\": \"deadbeef\", \"policies\": [ { \"role\": \"solo\", \"kind\": \"solo-kind\", "
            + "\"projection_schema_id\": \"other.id\", \"projection_schema_version\": 7, \"projection_schema_digest\": \"ffff\" } ] }");
        var pol = DeterminismRegistries.PinnedPolicy(polFile, "solo", "solo-kind");
        Assert.Equal("other.id", pol.SchemaId);
        Assert.Equal(7, pol.Version);
        Assert.Equal("ffff", pol.Digest);
        Assert.Equal("9", DeterminismRegistries.PinnedCanonicalizationVersion(polFile));
        Assert.Equal("deadbeef", DeterminismRegistries.PinnedProjectionImplDigest(polFile));
        Assert.Equal("x/y.json", DeterminismRegistries.ProjectionSelfTestVector(polFile));
    }

    // The INV-002 [integration] Exit test (over the REAL emitted receipt,
    // comparison_status=equal iff all 5 role projections agree, bound to the real
    // per-run artifacts) now lives in
    // Pr1DeterminismLaneTests.Lane_CoversFiveRoles_EqualIffProjectionsAgree, which
    // drives scripts/determinism-lane.sh once via the shared fixture (CI-separation
    // trait "determinism-lane"). The from-clean unit tests above are the fast signal.
}
