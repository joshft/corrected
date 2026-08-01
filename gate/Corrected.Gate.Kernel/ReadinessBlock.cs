using System;
using System.Collections.Generic;
using System.Linq;

namespace Corrected.Gate.Kernel;

/// <summary>
/// One precondition row of the readiness block, immutable (INV-003).
/// </summary>
public sealed class ReadinessPrecondition
{
    private readonly string[] _discharges;

    private ReadinessPrecondition(
        PreconditionId id, string name, bool satisfied, string? evidence, string[] discharges)
    {
        Id = id;
        Name = name;
        Satisfied = satisfied;
        Evidence = evidence;
        _discharges = discharges;
    }

    public PreconditionId Id { get; }

    public string Name { get; }

    public bool Satisfied { get; }

    /// <summary>Nullable evidence reference (string?), never prose (INV-002).</summary>
    public string? Evidence { get; }

    public IReadOnlyList<string> Discharges => _discharges;

    /// <summary>
    /// Public builder for supplied test rows (ReadinessPrecondition is NOT one of
    /// INV-003's three protected trust types, so a public factory is permitted).
    /// </summary>
    public static ReadinessPrecondition Create(
        PreconditionId id, string name, bool satisfied, string? evidence, IReadOnlyList<string> discharges)
    {
        if (name is null)
        {
            throw new ArgumentNullException(nameof(name));
        }

        if (discharges is null)
        {
            throw new ArgumentNullException(nameof(discharges));
        }

        return new ReadinessPrecondition(id, name, satisfied, evidence, discharges.ToArray());
    }
}

/// <summary>
/// The validated, immutable readiness domain type (INV-003). Type home is
/// Corrected.Gate.Kernel (EXT9-08). Built ONLY through the public static
/// validation-gated <see cref="TryCreate"/> factory; the instance constructor is
/// private (INV-003 reflection test: GetConstructors(Instance | Public) empty).
/// EvaluateReadiness accepts this type, never raw text or the parse DTO (INV-004).
/// </summary>
public sealed class ReadinessBlock
{
    /// <summary>
    /// The v1 schema version. Retained as a scalar for the <see cref="Indeterminate"/>
    /// value + v1 back-compat; the recognized SET is <see cref="RecognizedSchemaVersions"/>.
    /// </summary>
    public const int RecognizedSchemaVersion = 1;

    /// <summary>
    /// RS-021: the recognized readiness-block schema-version SET — exactly {1, 2}. v1
    /// blocks still parse (a not-yet-migrated block is legal); v2 adds the phase-entry
    /// lifecycle wire format. A version outside this set fails closed.
    /// </summary>
    public static readonly IReadOnlySet<int> RecognizedSchemaVersions =
        new HashSet<int> { 1, 2 };

    private readonly ReadinessPrecondition[] _preconditions;

    private ReadinessBlock(
        int schemaVersion,
        ReadinessStatus status,
        string readyPredicate,
        LifecycleState lifecycle,
        string? entryEvidencePointer,
        ReadinessPrecondition[] preconditions)
    {
        SchemaVersion = schemaVersion;
        Status = status;
        ReadyPredicate = readyPredicate;
        Lifecycle = lifecycle;
        EntryEvidencePointer = entryEvidencePointer;
        _preconditions = preconditions;
    }

    public int SchemaVersion { get; }

    public ReadinessStatus Status { get; }

    public string ReadyPredicate { get; }

    /// <summary>
    /// The DECLARED lifecycle latch (v2 wire field). For a v1 block this is the implicit
    /// <see cref="LifecycleState.Blocked"/> (v1 has no serialized lifecycle key).
    /// </summary>
    public LifecycleState Lifecycle { get; }

    /// <summary>
    /// The versioned entry-receipt pointer (v2). Non-null iff <c>lifecycle=ENTERED</c>;
    /// null for v1 and for v2 <c>BLOCKED</c>.
    /// </summary>
    public string? EntryEvidencePointer { get; }

    /// <summary>
    /// Derived: the effective lifecycle used by the src/ ban (INV-027). Equals the
    /// DECLARED lifecycle; a v1 block is Blocked. (Runtime monotonicity — once ENTERED,
    /// a transient integrity fault never reverts it — is a kernel/orchestrator concern of
    /// a later sub-track, not the wire-format DTO.)
    /// </summary>
    public LifecycleState EffectiveLifecycle => Lifecycle;

    public IReadOnlyList<ReadinessPrecondition> Preconditions => _preconditions;

    /// <summary>
    /// The single public validation-performing factory (INV-003/EXT9-08). MUST remain a
    /// SINGLE method named TryCreate (INV-003 reflection test asserts exactly one) — v2 is
    /// added via the OPTIONAL <paramref name="lifecycle"/>/<paramref name="entryEvidencePointer"/>
    /// params, never an overload. The presence bit is null (absent) vs non-null (present):
    /// v1 PROHIBITS both keys; v2 REQUIRES lifecycle, and requires the pointer iff ENTERED
    /// (prohibits it iff BLOCKED). Returns null on invalid input.
    /// </summary>
    public static ReadinessBlock? TryCreate(
        int schemaVersion,
        ReadinessStatus status,
        string readyPredicate,
        IReadOnlyList<ReadinessPrecondition> preconditions,
        LifecycleState? lifecycle = null,
        string? entryEvidencePointer = null)
    {
        if (!RecognizedSchemaVersions.Contains(schemaVersion))
        {
            return null;
        }

        if (status != ReadinessStatus.BLOCKED && status != ReadinessStatus.READY)
        {
            return null;
        }

        if (string.IsNullOrEmpty(readyPredicate))
        {
            return null;
        }

        if (preconditions is null)
        {
            return null;
        }

        var pcs = preconditions.ToArray();
        if (pcs.Length != 3)
        {
            return null;
        }

        var ids = pcs.Select(p => p.Id).ToHashSet();
        if (!ids.Contains(PreconditionId.P1) ||
            !ids.Contains(PreconditionId.P2) ||
            !ids.Contains(PreconditionId.P3))
        {
            return null;
        }

        if (schemaVersion == 1)
        {
            // v1 PROHIBITS both v2 wire keys (absent ⇒ implicit BLOCKED). A non-null
            // lifecycle OR a non-null entry_evidence_pointer means the wire carried a
            // prohibited key — fail closed REGARDLESS of value (even a BLOCKED lifecycle
            // that would coincide with the v1 implicit). The 4-arg callers pass neither
            // (both default to null), so they still construct implicit-BLOCKED, no pointer.
            if (lifecycle is not null || entryEvidencePointer is not null)
            {
                return null;
            }

            return new ReadinessBlock(1, status, readyPredicate, LifecycleState.Blocked, null, pcs);
        }

        // schemaVersion == 2 (the only other recognized version). Presence-bit rules:
        //   * lifecycle REQUIRED (non-null) else reject;
        //   * entry_evidence_pointer REQUIRED iff lifecycle==Entered, PROHIBITED iff Blocked;
        //   * ready_predicate RETAINED (already checked non-empty above).
        // The EffectiveLifecycle of a v2 block equals its DECLARED lifecycle.
        if (lifecycle is null)
        {
            return null;
        }

        LifecycleState declared = lifecycle.Value;
        switch (declared)
        {
            case LifecycleState.Entered:
                // pointer REQUIRED (a present-but-empty pointer is not a real receipt ref).
                if (string.IsNullOrEmpty(entryEvidencePointer))
                {
                    return null;
                }
                break;

            case LifecycleState.Blocked:
                // pointer PROHIBITED.
                if (entryEvidencePointer is not null)
                {
                    return null;
                }
                break;

            default:
                // Any lifecycle value outside the closed {Blocked, Entered} set is illegal.
                return null;
        }

        return new ReadinessBlock(2, status, readyPredicate, declared, entryEvidencePointer, pcs);
    }

    /// <summary>Constructs the Indeterminate-status value handed to the kernel when a block is unparseable (INV-002 RS-262).</summary>
    public static ReadinessBlock Indeterminate()
    {
        var pcs = new[]
        {
            ReadinessPrecondition.Create(PreconditionId.P1, "P1", false, null, Array.Empty<string>()),
            ReadinessPrecondition.Create(PreconditionId.P2, "P2", false, null, Array.Empty<string>()),
            ReadinessPrecondition.Create(PreconditionId.P3, "P3", false, null, Array.Empty<string>()),
        };
        return new ReadinessBlock(
            RecognizedSchemaVersion, ReadinessStatus.Indeterminate, "P1 AND P2 AND P3",
            LifecycleState.Blocked, null, pcs);
    }
}
