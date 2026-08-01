using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Corrected.Gate;
using Corrected.Gate.Kernel;
using Xunit;

namespace Corrected.Gate.Tests;

/// <summary>
/// Track 5a — the schema-v2 ReadinessBlock WIRE-FORMAT foundation (Group G, spec
/// lines ~867–936: "Readiness state model (schema v2)" + the exact per-version field
/// table ~887–903). Covers INV-026's ReadinessBlock migration DTO/parser/validation +
/// RS-021's recognized-version SET {1,2}. Does NOT cover the kernel transition
/// evaluator (5b), orchestrator/classifier (5c), health/refresh (5d), or entry-receipt
/// (5e) — later sub-tracks own those.
///
/// Two layers are exercised:
///   * DOMAIN — <see cref="ReadinessBlock.TryCreate"/> [unit], the semantic validator.
///   * PARSER — <see cref="ReadinessBlockParser.Parse"/> [integration], the PRODUCTION
///     path that reads committed blocks. Presence-bit fail-closed cases are asserted
///     THROUGH the parser (the real path) per the RED-phase brief, plus at TryCreate.
///
/// The per-version field table encoded as fail-closed tests:
///   | field                   | v1                         | v2                                          |
///   | schema_version          | required (=1)              | required (=2)                               |
///   | status                  | required                   | required                                    |
///   | ready_predicate         | required                   | required (retained)                         |
///   | preconditions           | required                   | required                                    |
///   | lifecycle               | PROHIBITED (⇒ implicit BLOCKED) | REQUIRED (BLOCKED|ENTERED)              |
///   | entry_evidence_pointer  | PROHIBITED                 | required iff ENTERED, prohibited iff BLOCKED |
///
/// AP-031 note: the v1 positive parse uses the REAL committed parent block fixture
/// (fixtures/readiness/real-parent-readiness-block.md, sourced from
/// .correctless/specs/phase-0-1-worker.md). There is NO real v2 producer artifact —
/// the committed block is v1 today and STAYS v1 through all of P1/P2/P3 (spec lines
/// 914–917), leaving v1 only via the atomic phase-entry transition (a later sub-track).
/// So the v2 fixtures here are necessarily SYNTHETIC; AP-031's real-artifact clause is
/// DORMANT for v2 (no producer has emitted one yet).
/// </summary>
public class SchemaV2ReadinessBlockTests
{
    private const string Pred = "P1 AND P2 AND P3";
    private const string SamplePointer = ".correctless/receipts/phase-entry/P2-activation.json";

    private static IReadOnlyList<ReadinessPrecondition> Pcs()
        => new[]
        {
            ReadinessPrecondition.Create(PreconditionId.P1, "p1", false, null, Array.Empty<string>()),
            ReadinessPrecondition.Create(PreconditionId.P2, "p2", false, null, Array.Empty<string>()),
            ReadinessPrecondition.Create(PreconditionId.P3, "p3", false, null, Array.Empty<string>()),
        };

    /// <summary>
    /// Builds a synthetic readiness block string for the parser path. Keys are emitted
    /// only when supplied so a test can exercise "key absent" vs "key present" (the
    /// presence-bit distinction). ready_predicate defaults to the conjunction the parser
    /// requires; pass includeReadyPredicate:false to omit the key entirely.
    /// </summary>
    private static string Block(
        int schemaVersion,
        string status,
        string? lifecycle = null,
        string? pointer = null,
        bool includeReadyPredicate = true,
        string readyPredicate = "\"P1 AND P2 AND P3\"")
    {
        var sb = new StringBuilder();
        sb.Append("```yaml\n");
        sb.Append("implementation_readiness:\n");
        sb.Append($"  schema_version: {schemaVersion}\n");
        sb.Append($"  status: {status}\n");
        if (includeReadyPredicate)
        {
            sb.Append($"  ready_predicate: {readyPredicate}\n");
        }
        if (lifecycle is not null)
        {
            sb.Append($"  lifecycle: {lifecycle}\n");
        }
        if (pointer is not null)
        {
            sb.Append($"  entry_evidence_pointer: {pointer}\n");
        }
        sb.Append("  preconditions:\n");
        foreach (var id in new[] { "P1", "P2", "P3" })
        {
            sb.Append($"    - id: {id}\n");
            sb.Append($"      name: {id.ToLowerInvariant()}\n");
            sb.Append("      satisfied: false\n");
            sb.Append("      evidence: null\n");
            sb.Append("      discharges: []\n");
        }
        sb.Append("```\n");
        return sb.ToString();
    }

    // ---------------------------------------------------------------------------------
    // DOMAIN LAYER — ReadinessBlock.TryCreate [unit]
    // ---------------------------------------------------------------------------------

    // Tests INV-003 [unit]: TryCreate MUST remain a SINGLE public static method (v2 added
    // via optional params, NOT an overload) — INV-003's Single_public_static_TryCreate_per_type
    // reflection guard depends on this. Encoded here too so a future overload is caught at
    // this track's own boundary. PASSES in the stub (regression guard for the migration).
    [Fact]
    public void TryCreate_remains_a_single_public_static_method()
    {
        var methods = typeof(ReadinessBlock)
            .GetMethods(BindingFlags.Static | BindingFlags.Public)
            .Where(m => m.Name == "TryCreate")
            .ToArray();
        Assert.Single(methods);
    }

    // Tests RS-021 [unit]: the recognized-version SET is EXACTLY {1,2} — set-equality, not a
    // count/presence proxy (PMB-003/AP-022). 1 and 2 recognized; 0 and 3 are NOT. Scaffolding
    // for this track — PASSES in the stub.
    [Fact]
    public void RecognizedSchemaVersions_is_exactly_one_and_two()
    {
        var set = ReadinessBlock.RecognizedSchemaVersions;
        Assert.Contains(1, set);
        Assert.Contains(2, set);
        Assert.DoesNotContain(0, set);
        Assert.DoesNotContain(3, set);
        Assert.Equal(2, set.Count);
    }

    // Tests INV-026 [unit]: the v1 4-arg TryCreate stays backward-compatible — the 8 existing
    // callers pass NO lifecycle/pointer and must still construct implicit-BLOCKED, no pointer.
    // PASSES in the stub (backward-compat regression guard, not a v2 RED).
    [Fact]
    public void V1_four_arg_TryCreate_constructs_implicit_blocked()
    {
        ReadinessBlock? b = ReadinessBlock.TryCreate(1, ReadinessStatus.BLOCKED, Pred, Pcs());
        Assert.NotNull(b);
        Assert.Equal(1, b!.SchemaVersion);
        Assert.Equal(LifecycleState.Blocked, b.Lifecycle);
        Assert.Equal(LifecycleState.Blocked, b.EffectiveLifecycle);
        Assert.Null(b.EntryEvidencePointer);
    }

    // Tests INV-026 [unit]: v2 field-table row `lifecycle=BLOCKED, entry_evidence_pointer
    // PROHIBITED`. A v2 BLOCKED block constructs with declared Blocked lifecycle and NO
    // pointer. RED against the stub (v2 branch returns null).
    [Fact]
    public void V2_blocked_TryCreate_constructs_with_blocked_lifecycle_no_pointer()
    {
        ReadinessBlock? b = ReadinessBlock.TryCreate(
            2, ReadinessStatus.BLOCKED, Pred, Pcs(), LifecycleState.Blocked, entryEvidencePointer: null);
        Assert.NotNull(b);
        Assert.Equal(2, b!.SchemaVersion);
        Assert.Equal(LifecycleState.Blocked, b.Lifecycle);
        Assert.Equal(LifecycleState.Blocked, b.EffectiveLifecycle);
        Assert.Null(b.EntryEvidencePointer);
    }

    // Tests INV-026 [unit]: v2 field-table row `lifecycle=ENTERED, entry_evidence_pointer
    // REQUIRED`. A v2 ENTERED block constructs with declared Entered lifecycle and the
    // pointer retained verbatim. RED against the stub.
    [Fact]
    public void V2_entered_TryCreate_constructs_with_pointer()
    {
        ReadinessBlock? b = ReadinessBlock.TryCreate(
            2, ReadinessStatus.BLOCKED, Pred, Pcs(), LifecycleState.Entered, SamplePointer);
        Assert.NotNull(b);
        Assert.Equal(2, b!.SchemaVersion);
        Assert.Equal(LifecycleState.Entered, b.Lifecycle);
        Assert.Equal(LifecycleState.Entered, b.EffectiveLifecycle);
        Assert.Equal(SamplePointer, b.EntryEvidencePointer);
    }

    // Tests INV-026 [unit]: v2 `lifecycle` is REQUIRED — a v2 call with a null lifecycle
    // (key absent) fails closed. The presence-bit does NOT make lifecycle optional (round-8
    // correction of the earlier "optional lifecycle, default BLOCKED" phrasing). GREEN must
    // keep this rejecting once the v2 branch is built.
    [Fact]
    public void V2_missing_lifecycle_is_rejected()
    {
        ReadinessBlock? b = ReadinessBlock.TryCreate(
            2, ReadinessStatus.BLOCKED, Pred, Pcs(), lifecycle: null, entryEvidencePointer: null);
        Assert.Null(b);
    }

    // Tests INV-026 [unit]: v2 BLOCKED carrying a pointer fails closed (pointer PROHIBITED
    // iff BLOCKED).
    [Fact]
    public void V2_blocked_carrying_pointer_is_rejected()
    {
        ReadinessBlock? b = ReadinessBlock.TryCreate(
            2, ReadinessStatus.BLOCKED, Pred, Pcs(), LifecycleState.Blocked, SamplePointer);
        Assert.Null(b);
    }

    // Tests INV-026 [unit]: v2 ENTERED missing a pointer fails closed (pointer REQUIRED iff
    // ENTERED).
    [Fact]
    public void V2_entered_missing_pointer_is_rejected()
    {
        ReadinessBlock? b = ReadinessBlock.TryCreate(
            2, ReadinessStatus.BLOCKED, Pred, Pcs(), LifecycleState.Entered, entryEvidencePointer: null);
        Assert.Null(b);
    }

    // Tests INV-026 [unit]: v2 field-table row `ready_predicate required (retained)`. A v2
    // call with an empty ready_predicate fails closed — the migration never drops it.
    [Fact]
    public void V2_missing_ready_predicate_is_rejected()
    {
        ReadinessBlock? b = ReadinessBlock.TryCreate(
            2, ReadinessStatus.BLOCKED, readyPredicate: "", preconditions: Pcs(),
            lifecycle: LifecycleState.Blocked, entryEvidencePointer: null);
        Assert.Null(b);
    }

    // Tests INV-026 [unit]: v1 field-table row `lifecycle PROHIBITED`. A v1 call carrying a
    // non-null lifecycle (the wire had the key) fails closed — REGARDLESS of value, incl.
    // the implicit-matching BLOCKED. RED against the stub (v1 branch ignores the arg).
    [Fact]
    public void V1_carrying_lifecycle_is_rejected()
    {
        ReadinessBlock? entered = ReadinessBlock.TryCreate(
            1, ReadinessStatus.BLOCKED, Pred, Pcs(), lifecycle: LifecycleState.Entered);
        Assert.Null(entered);

        ReadinessBlock? blocked = ReadinessBlock.TryCreate(
            1, ReadinessStatus.BLOCKED, Pred, Pcs(), lifecycle: LifecycleState.Blocked);
        Assert.Null(blocked);
    }

    // Tests INV-026 [unit]: v1 field-table row `entry_evidence_pointer PROHIBITED`. A v1 call
    // carrying a pointer fails closed. RED against the stub.
    [Fact]
    public void V1_carrying_pointer_is_rejected()
    {
        ReadinessBlock? b = ReadinessBlock.TryCreate(
            1, ReadinessStatus.BLOCKED, Pred, Pcs(), lifecycle: null, entryEvidencePointer: SamplePointer);
        Assert.Null(b);
    }

    // Tests RS-021 [unit]: schema_version outside the recognized set {1,2} fails closed —
    // 3, 0, and a negative all reject (fail-closed on the accept side, AP-022). PASSES in
    // the stub (the set already excludes them).
    [Fact]
    public void Schema_version_outside_recognized_set_is_rejected()
    {
        Assert.Null(ReadinessBlock.TryCreate(3, ReadinessStatus.BLOCKED, Pred, Pcs()));
        Assert.Null(ReadinessBlock.TryCreate(0, ReadinessStatus.BLOCKED, Pred, Pcs()));
        Assert.Null(ReadinessBlock.TryCreate(-1, ReadinessStatus.BLOCKED, Pred, Pcs()));
    }

    // Tests INV-026 [unit]: `indeterminate` is a parser-INTERNAL result, NEVER a legal
    // serialized/constructed status. TryCreate rejects a ReadinessStatus.Indeterminate for
    // BOTH v1 and v2. PASSES in the stub (the status guard already excludes it).
    [Fact]
    public void Indeterminate_status_is_never_constructible()
    {
        Assert.Null(ReadinessBlock.TryCreate(1, ReadinessStatus.Indeterminate, Pred, Pcs()));
        Assert.Null(ReadinessBlock.TryCreate(
            2, ReadinessStatus.Indeterminate, Pred, Pcs(), LifecycleState.Blocked));
    }

    // ---------------------------------------------------------------------------------
    // PARSER LAYER — ReadinessBlockParser.Parse [integration] (the production read path)
    // Entry: a committed markdown block string. Through: the real Stage-1 AST hardening +
    // typed-DTO deserialize + post-parse validation + TryCreate (NOT mocked). Exit: a
    // validated ReadinessBlock, or the Indeterminate value on any fail-closed case.
    // No documented entrypoint for the in-process parser — using the public Parse API
    // (the same path INV-006 drives over committed blocks).
    // ---------------------------------------------------------------------------------

    // Tests INV-026 [integration]: the REAL committed v1 parent block parses to schema 1 with
    // the v2-surface deriving implicit BLOCKED and no pointer (v1 back-compat over the derived
    // lifecycle surface). AP-031 real-producer fixture.
    // Source: gate/Corrected.Gate.Tests/fixtures/readiness/real-parent-readiness-block.md
    //         (verbatim from .correctless/specs/phase-0-1-worker.md). PASSES in the stub.
    [Fact]
    public void V1_real_block_parses_with_implicit_blocked_lifecycle()
    {
        string md = System.IO.File.ReadAllText(TestPaths.Fixture("readiness", "real-parent-readiness-block.md"));
        ReadinessBlock b = ReadinessBlockParser.Parse(md);
        Assert.Equal(1, b.SchemaVersion);
        Assert.Equal(ReadinessStatus.BLOCKED, b.Status);
        Assert.Equal(LifecycleState.Blocked, b.EffectiveLifecycle);
        Assert.Null(b.EntryEvidencePointer);
    }

    // Tests INV-026 [integration]: a v2 BLOCKED block (no pointer) parses to a valid schema-2
    // block with declared Blocked lifecycle. RED against the stub (v2 → TryCreate null →
    // Indeterminate, so SchemaVersion is 1 / Status is Indeterminate today).
    [Fact]
    public void V2_blocked_block_parses()
    {
        ReadinessBlock b = ReadinessBlockParser.Parse(Block(2, "BLOCKED", lifecycle: "BLOCKED"));
        Assert.Equal(2, b.SchemaVersion);
        Assert.Equal(ReadinessStatus.BLOCKED, b.Status);
        Assert.Equal(LifecycleState.Blocked, b.Lifecycle);
        Assert.Equal(LifecycleState.Blocked, b.EffectiveLifecycle);
        Assert.Null(b.EntryEvidencePointer);
    }

    // Tests INV-026 [integration]: a v2 ENTERED block with a pointer parses to a valid schema-2
    // block with declared Entered lifecycle and the pointer retained. RED against the stub.
    [Fact]
    public void V2_entered_block_parses()
    {
        ReadinessBlock b = ReadinessBlockParser.Parse(
            Block(2, "BLOCKED", lifecycle: "ENTERED", pointer: SamplePointer));
        Assert.Equal(2, b.SchemaVersion);
        Assert.Equal(LifecycleState.Entered, b.Lifecycle);
        Assert.Equal(LifecycleState.Entered, b.EffectiveLifecycle);
        Assert.Equal(SamplePointer, b.EntryEvidencePointer);
    }

    // Tests INV-026 [integration]: a v1 block carrying a `lifecycle:` key fails closed
    // (PROHIBITED in v1). RED against the stub — today the typed DTO absorbs the key and the
    // v1 path parses a valid BLOCKED block. GREEN adds presence detection + rejection.
    [Fact]
    public void V1_block_carrying_lifecycle_key_fails_closed()
    {
        ReadinessBlock b = ReadinessBlockParser.Parse(Block(1, "BLOCKED", lifecycle: "BLOCKED"));
        Assert.Equal(ReadinessStatus.Indeterminate, b.Status);
    }

    // Tests INV-026 [integration]: a v1 block carrying an `entry_evidence_pointer:` key fails
    // closed (PROHIBITED in v1). RED against the stub.
    [Fact]
    public void V1_block_carrying_pointer_key_fails_closed()
    {
        ReadinessBlock b = ReadinessBlockParser.Parse(Block(1, "BLOCKED", pointer: SamplePointer));
        Assert.Equal(ReadinessStatus.Indeterminate, b.Status);
    }

    // Tests INV-026 [integration]: a v2 block MISSING the `lifecycle:` key fails closed
    // (REQUIRED in v2). Guards the presence-bit rule once GREEN lands (fail-closed in the stub).
    [Fact]
    public void V2_block_missing_lifecycle_fails_closed()
    {
        ReadinessBlock b = ReadinessBlockParser.Parse(Block(2, "BLOCKED"));
        Assert.Equal(ReadinessStatus.Indeterminate, b.Status);
    }

    // Tests INV-026 [integration]: a v2 block MISSING `ready_predicate` fails closed (REQUIRED,
    // retained in v2). Guards against the migration dropping the retained field.
    [Fact]
    public void V2_block_missing_ready_predicate_fails_closed()
    {
        ReadinessBlock b = ReadinessBlockParser.Parse(
            Block(2, "BLOCKED", lifecycle: "BLOCKED", includeReadyPredicate: false));
        Assert.Equal(ReadinessStatus.Indeterminate, b.Status);
    }

    // Tests INV-026 [integration]: a v2 BLOCKED block carrying a pointer fails closed (pointer
    // PROHIBITED iff BLOCKED).
    [Fact]
    public void V2_blocked_block_carrying_pointer_fails_closed()
    {
        ReadinessBlock b = ReadinessBlockParser.Parse(
            Block(2, "BLOCKED", lifecycle: "BLOCKED", pointer: SamplePointer));
        Assert.Equal(ReadinessStatus.Indeterminate, b.Status);
    }

    // Tests INV-026 [integration]: a v2 ENTERED block missing a pointer fails closed (pointer
    // REQUIRED iff ENTERED).
    [Fact]
    public void V2_entered_block_missing_pointer_fails_closed()
    {
        ReadinessBlock b = ReadinessBlockParser.Parse(Block(2, "BLOCKED", lifecycle: "ENTERED"));
        Assert.Equal(ReadinessStatus.Indeterminate, b.Status);
    }

    // Tests RS-021 [integration]: a block with schema_version outside {1,2} (here 3) fails
    // closed. PASSES in the stub (version-set gate rejects it).
    [Fact]
    public void Schema_version_three_fails_closed()
    {
        ReadinessBlock b = ReadinessBlockParser.Parse(Block(3, "BLOCKED", lifecycle: "BLOCKED"));
        Assert.Equal(ReadinessStatus.Indeterminate, b.Status);
    }

    // Tests INV-026 [integration]: a SERIALIZED `status: indeterminate` is ILLEGAL — indeterminate
    // is parser-internal only, never a legal wire value. Fails closed (the parser's status
    // vocabulary is exactly {BLOCKED, READY}). PASSES in the stub. Both v1 and v2 shapes.
    [Fact]
    public void Serialized_indeterminate_status_fails_closed()
    {
        ReadinessBlock v1 = ReadinessBlockParser.Parse(Block(1, "indeterminate"));
        Assert.Equal(ReadinessStatus.Indeterminate, v1.Status);

        ReadinessBlock v2 = ReadinessBlockParser.Parse(Block(2, "indeterminate", lifecycle: "BLOCKED"));
        Assert.Equal(ReadinessStatus.Indeterminate, v2.Status);

        // Defensive: the exact PascalCase enum name must not sneak through the wire either.
        ReadinessBlock exact = ReadinessBlockParser.Parse(Block(2, "Indeterminate", lifecycle: "BLOCKED"));
        Assert.Equal(ReadinessStatus.Indeterminate, exact.Status);
    }
}
