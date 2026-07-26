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
    /// <summary>The single recognized readiness-block schema version (INV-002).</summary>
    public const int RecognizedSchemaVersion = 1;

    private readonly ReadinessPrecondition[] _preconditions;

    private ReadinessBlock(
        int schemaVersion,
        ReadinessStatus status,
        string readyPredicate,
        ReadinessPrecondition[] preconditions)
    {
        SchemaVersion = schemaVersion;
        Status = status;
        ReadyPredicate = readyPredicate;
        _preconditions = preconditions;
    }

    public int SchemaVersion { get; }

    public ReadinessStatus Status { get; }

    public string ReadyPredicate { get; }

    public IReadOnlyList<ReadinessPrecondition> Preconditions => _preconditions;

    /// <summary>
    /// The single public validation-performing factory (INV-003/EXT9-08).
    /// Returns null on invalid input.
    /// </summary>
    public static ReadinessBlock? TryCreate(
        int schemaVersion,
        ReadinessStatus status,
        string readyPredicate,
        IReadOnlyList<ReadinessPrecondition> preconditions)
    {
        if (schemaVersion != RecognizedSchemaVersion)
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

        return new ReadinessBlock(schemaVersion, status, readyPredicate, pcs);
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
        return new ReadinessBlock(RecognizedSchemaVersion, ReadinessStatus.Indeterminate, "P1 AND P2 AND P3", pcs);
    }
}
