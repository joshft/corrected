using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;

namespace Corrected.Gate.Tests;

// This file carries the P1 temp-tree BUILDER (a test helper, real logic) used by the
// INV-008 / PRH-007 / BND-003 mutation tests. It is named *Tests.cs so the workflow
// gate classifies it as a TEST file (real logic permitted) rather than a source stub.

/// <summary>
/// The set of INV-008 P1 mutations B1 requires, each paired with a MIGRATED ADR
/// (status:accepted, superseded_by:null) that passes the schema-completeness gate so
/// the short-circuit does NOT fire — a Stage-A-short-circuit-only GREEN probe must
/// FAIL these because they reach the downstream (a)/(a′)/(a″)/(a‴)/(b) checks.
///
/// PUBLIC because it is a parameter type of the public [Theory] test methods in
/// Inv008P1ProbeTests (an internal enum there triggers CS0051 inconsistent accessibility).
/// </summary>
public enum P1Mutation
{
    /// <summary>Clean migrated tree — GREEN P1 resolves TRUE (Stage-B positive).</summary>
    None,

    // ---- (a′)/(a″) evidence-sample tampers: caught at the probe level by the (a′)
    //      compiled canonical_sample_sha256 / probe_manifest_sha256 pins (any content
    //      change flips the file SHA) -> evidence-malformed. The (a″) recompute is
    //      belt-and-suspenders behind the pins. ----
    RouteBOnlyPerProbe,
    FinalSuiteStatusUnknown,
    FlippedRouteAProbe,
    EmptyPerProbe,
    DuplicateRouteAVerdict,
    DuplicateProbeRouteEntry,
    DuplicateJsonKeyRoot,
    DuplicateJsonKeyRouteVerdict,
    DuplicateJsonKeyPerProbe,
    WrongProbeManifestShaField,
    TamperedManifestFile,
    CoherentlyTamperedSample,

    // ---- (a) decision-field / prose↔machine tampers: sample untouched, caught in
    //      (a) step 3 / (b) -> evidence-refutes. ----
    SelectedRouteB,
    RouteAIncompatible,
    ProseMachineMismatch,
    DroppedDafnyDriver,

    // ---- (a‴) registry / supersession-graph: caught in (a‴) -> evidence-malformed. ----
    UnregisteredOnDiskAdrLint,
    SupersessionCycle,
    SupersessionDangling,
    SupersessionTwoTerminals,
    SupersessionDisconnected,
}

/// <summary>
/// Builds a SYNTHESIZED temp tree for driving the real P1 probe through the repo-root
/// seam (GateContext.ForRepoRoot / ForRepoRootWithAdrRegistry). The base is a faithful
/// copy of the committed P1 evidence (canonical sample, schema, probe manifest,
/// route-a.json, ARCHITECTURE production-assembly block — the EA-002 committed data
/// dependency), overlaid with a MIGRATED ADR-0001 at the pinned decision path and a
/// single targeted mutation. Reading committed spike data files (never out/ or a
/// prior-run root) matches the INV-013 degraded-env pattern (AP-021-safe).
///
/// NOTE (GREEN): the (c) trust-root inputs (gate/Corrected.Gate/lint-source-registry.json
/// + the extracted lib sources) are NOT yet committed, so they are not laid into the
/// base tree; GREEN extends this builder when it wires INV-008(c). These tests are RED
/// against the throwing stub probe regardless.
/// </summary>
internal sealed class P1Tree : IDisposable
{
    private const string AdrRelPath = "docs/adr/ADR-0001-dafny-integration-boundary.md";
    private const string SampleRel = "spikes/dafny-compat/evidence/samples/run-report.canonical.sample.json";
    private const string SchemaRel = "spikes/dafny-compat/schema/evidence-schema.json";
    private const string ManifestRel = "spikes/dafny-compat/manifest/probe-manifest.json";
    private const string RouteARel = "spikes/dafny-compat/manifest/expected-loaded/route-a.json";
    private const string ArchRel = ".correctless/ARCHITECTURE.md";

    public string Root { get; }

    /// <summary>
    /// The synthesized ADR registry for ForRepoRootWithAdrRegistry (the supersession
    /// shapes). Starts as the single live entry; supersession mutations extend it so
    /// the graph-shape rule (not registry set-equality) is the thing under test.
    /// </summary>
    public List<string> AdrRegistry { get; } = new() { "ADR-0001" };

    private P1Tree(string root) => Root = root;

    public void Dispose()
    {
        try { Directory.Delete(Root, recursive: true); } catch { /* best effort */ }
    }

    private static string ReadReal(string rel) => File.ReadAllText(TestPaths.RepoFile(rel.Split('/')));

    private void Write(string rel, string text)
    {
        string dst = Path.Combine(Root, Path.Combine(rel.Split('/')));
        Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
        File.WriteAllText(dst, text);
    }

    public static P1Tree Build(P1Mutation mutation)
    {
        var tree = new P1Tree(Path.Combine(Path.GetTempPath(), "gate-p1tree-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(tree.Root);

        // --- base evidence (verbatim unless mutated) ---
        string sample = ReadReal(SampleRel);
        string manifest = ReadReal(ManifestRel);
        string routeA = ReadReal(RouteARel);
        tree.Write(SchemaRel, ReadReal(SchemaRel));
        tree.Write(ArchRel, ReadReal(ArchRel));

        // --- migrated ADR-0001 params (varied by the decision-field mutations) ---
        string boundaryDecision = "in-process-selected";
        string selectedRoute = "A";
        string machineStatus = "accepted";
        string proseToken = "accepted";
        string routeAVerdict = "COMPATIBLE";
        string supersededBy = "null";
        string? supersedes = null;

        switch (mutation)
        {
            case P1Mutation.RouteBOnlyPerProbe:
                sample = MutateSampleJson(sample, det =>
                {
                    var kept = det["per_probe_results"]!.AsArray()
                        .Where(e => e!["route"]!.GetValue<string>() == "B")
                        .Select(e => e!.DeepClone()).ToArray();
                    det["per_probe_results"] = new JsonArray(kept);
                });
                break;
            case P1Mutation.FinalSuiteStatusUnknown:
                sample = MutateSampleJson(sample, det => det["final_suite_status"] = "unknown");
                break;
            case P1Mutation.FlippedRouteAProbe:
                sample = MutateSampleJson(sample, det =>
                {
                    var first = det["per_probe_results"]!.AsArray()
                        .First(e => e!["route"]!.GetValue<string>() == "A");
                    first!["status"] = "fail";
                });
                break;
            case P1Mutation.EmptyPerProbe:
                sample = MutateSampleJson(sample, det => det["per_probe_results"] = new JsonArray());
                break;
            case P1Mutation.DuplicateRouteAVerdict:
                sample = MutateSampleJson(sample, det =>
                {
                    var rv = det["route_verdicts"]!.AsArray();
                    var routeAEntry = rv.First(e => e!["route"]!.GetValue<string>() == "A");
                    rv.Add(routeAEntry!.DeepClone());
                });
                break;
            case P1Mutation.DuplicateProbeRouteEntry:
                sample = MutateSampleJson(sample, det =>
                {
                    var ppr = det["per_probe_results"]!.AsArray();
                    ppr.Add(ppr[0]!.DeepClone()); // duplicate (P01, A) -> multiset-count breach
                });
                break;
            case P1Mutation.DuplicateJsonKeyRoot:
                // System.Text.Json.Nodes cannot represent duplicate keys, so inject text.
                sample = ReplaceFirst(sample, "\"kind\": \"run-report\",",
                    "\"kind\": \"run-report\",\n  \"kind\": \"run-report\",");
                break;
            case P1Mutation.DuplicateJsonKeyRouteVerdict:
                sample = ReplaceFirst(sample, "\"state\": \"COMPATIBLE\",",
                    "\"state\": \"COMPATIBLE\",\n          \"state\": \"COMPATIBLE\",");
                break;
            case P1Mutation.DuplicateJsonKeyPerProbe:
                sample = ReplaceFirst(sample, "\"status\": \"pass\",",
                    "\"status\": \"pass\",\n          \"status\": \"pass\",");
                break;
            case P1Mutation.WrongProbeManifestShaField:
                sample = MutateSampleRootJson(sample, root =>
                    root["probe_manifest_sha256"] = new string('0', 64));
                break;
            case P1Mutation.TamperedManifestFile:
                manifest = MutateManifestJson(manifest, root =>
                    root["entries"]!.AsArray().RemoveAt(0)); // drop a (probe,route) -> file SHA breaks
                break;
            case P1Mutation.CoherentlyTamperedSample:
                // Recompute-neutral edit (a benign field) — only the compiled
                // canonical_sample_sha256 pin catches it (R3-B2).
                sample = ReplaceFirst(sample, "\"run_id\": \"runid-2478510f8049f355\",",
                    "\"run_id\": \"runid-COHERENTTAMPER0\",");
                break;

            case P1Mutation.SelectedRouteB:
                selectedRoute = "B";
                break;
            case P1Mutation.RouteAIncompatible:
                routeAVerdict = "INCOMPATIBLE";
                break;
            case P1Mutation.ProseMachineMismatch:
                machineStatus = "superseded"; // machine says superseded...
                proseToken = "accepted";       // ...prose (line 3) says accepted -> split
                break;
            case P1Mutation.DroppedDafnyDriver:
                routeA = MutateRouteAJson(routeA);
                break;

            case P1Mutation.UnregisteredOnDiskAdrLint:
                // A second on-disk column-0-in-fence adr_lint block that is NOT in the
                // registry -> registry set-equality fail-closed ("register this ADR").
                tree.Write("docs/adr/ADR-0002-unregistered-synth.md",
                    SynthAdr("ADR-0002 (unregistered synth)", "accepted", "accepted", null, "null", "A", "COMPATIBLE"));
                break;

            case P1Mutation.SupersessionCycle:
                supersededBy = "ADR-0002";
                supersedes = "ADR-0002";
                tree.WriteExtraAdr("ADR-0002", "accepted", "accepted", "ADR-0001", "ADR-0001", "A", "COMPATIBLE");
                break;
            case P1Mutation.SupersessionDangling:
                supersededBy = "ADR-0099"; // target not on disk / not registered -> dangling
                break;
            case P1Mutation.SupersessionTwoTerminals:
                tree.WriteExtraAdr("ADR-0002", "accepted", "accepted", null, "null", "A", "COMPATIBLE");
                break;
            case P1Mutation.SupersessionDisconnected:
                // A registered node with no edge to/from the ADR-0001 root -> unreachable.
                tree.WriteExtraAdr("ADR-0002", "superseded", "superseded", null, "null", "A", "COMPATIBLE");
                break;
        }

        tree.Write(SampleRel, sample);
        tree.Write(ManifestRel, manifest);
        tree.Write(RouteARel, routeA);
        tree.Write(AdrRelPath,
            SynthAdr("ADR-0001: Dafny integration boundary (accepted)",
                proseToken, machineStatus, supersedes, supersededBy, selectedRoute, routeAVerdict, boundaryDecision));
        return tree;
    }

    private void WriteExtraAdr(string id, string proseToken, string machineStatus, string? supersedes,
        string supersededBy, string route, string verdict)
    {
        Write($"docs/adr/{id}-synth.md",
            SynthAdr($"{id} (synth)", proseToken, machineStatus, supersedes, supersededBy, route, verdict));
        AdrRegistry.Add(id);
    }

    // -------- JSON mutation helpers --------

    private static readonly System.Text.Json.JsonSerializerOptions Indented = new() { WriteIndented = true };

    private static string MutateSampleJson(string text, Action<JsonNode> mutateDeterministic)
    {
        JsonNode root = JsonNode.Parse(text)!;
        mutateDeterministic(root["deterministic"]!);
        return root.ToJsonString(Indented);
    }

    private static string MutateSampleRootJson(string text, Action<JsonNode> mutateRoot)
    {
        JsonNode root = JsonNode.Parse(text)!;
        mutateRoot(root);
        return root.ToJsonString(Indented);
    }

    private static string MutateManifestJson(string text, Action<JsonNode> mutateRoot)
    {
        JsonNode root = JsonNode.Parse(text)!;
        mutateRoot(root);
        return root.ToJsonString(Indented);
    }

    private static string MutateRouteAJson(string text)
    {
        JsonNode root = JsonNode.Parse(text)!;
        var kept = root["assemblies"]!.AsArray()
            .Where(a => a!["simple_name"]!.GetValue<string>() != "DafnyDriver")
            .Select(a => a!.DeepClone()).ToArray();
        root["assemblies"] = new JsonArray(kept);
        var anchors = root["anchors"]!.AsArray()
            .Where(a => a!.GetValue<string>() != "DafnyDriver")
            .Select(a => a!.DeepClone()).ToArray();
        root["anchors"] = new JsonArray(anchors);
        return root.ToJsonString(Indented);
    }

    private static string ReplaceFirst(string s, string find, string repl)
    {
        int i = s.IndexOf(find, StringComparison.Ordinal);
        return i < 0 ? s : s.Substring(0, i) + repl + s.Substring(i + find.Length);
    }

    // -------- ADR synthesis (line-3 **Status** prose + column-0 adr_lint fence) --------

    private static string SynthAdr(string title, string proseToken, string machineStatus,
        string? supersedes, string supersededBy, string route, string verdict,
        string boundaryDecision = "in-process-selected")
    {
        var sb = new StringBuilder();
        sb.Append("# ").Append(title).Append('\n');
        sb.Append('\n');
        // Line 3: the multi-line parenthetical the prose↔machine extractor reads the
        // LEADING {accepted|superseded|provisional} token from (a naive == dead-reds; R3-M2).
        sb.Append("- **Status**: ").Append(proseToken).Append(" (DD-003 Stage-B migrated synthesized fixture)\n");
        sb.Append("- **Scope**: synthesized P1 temp-tree fixture (B1)\n");
        sb.Append('\n');
        sb.Append("## Machine-readable decision block (INV-013 ADR linter input)\n");
        sb.Append('\n');
        sb.Append("```yaml\n");
        sb.Append("adr_lint:\n");
        sb.Append("  boundary_decision: ").Append(boundaryDecision).Append('\n');
        sb.Append("  selected_route: ").Append(route).Append('\n');
        sb.Append("  status: ").Append(machineStatus).Append('\n');
        if (supersedes is not null) sb.Append("  supersedes: ").Append(supersedes).Append('\n');
        sb.Append("  superseded_by: ").Append(supersededBy).Append('\n');
        sb.Append("  routes:\n");
        sb.Append("    - route: A\n");
        sb.Append("      verdict: ").Append(verdict).Append('\n');
        sb.Append("      adjudication_record_id: null\n");
        sb.Append("      evidence: ").Append(SampleRel).Append('\n');
        sb.Append("    - route: B\n");
        sb.Append("      verdict: COMPATIBLE\n");
        sb.Append("      adjudication_record_id: null\n");
        sb.Append("      evidence: ").Append(SampleRel).Append('\n');
        sb.Append("```\n");
        return sb.ToString();
    }
}
