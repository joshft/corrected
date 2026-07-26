namespace Corrected.Gate.Kernel;

/// <summary>
/// A single precondition's independently-derived probe result (INV-003/005/006).
/// Type home is Corrected.Gate.Kernel (EXT9-08). Immutable; a PRIVATE instance
/// constructor plus the SOLE public entry point <see cref="TryCreate"/> — a
/// public static validation-gated factory (EXT8-03/EXT9-08) callable across the
/// Gate -> Kernel assembly boundary. INV-003's reflection test asserts
/// GetConstructors(Instance | Public) is empty.
/// </summary>
public sealed class ProbeResult
{
    private ProbeResult(bool satisfied, string reason, ReferenceResolution referenceResolution)
    {
        Satisfied = satisfied;
        Reason = reason;
        ReferenceResolution = referenceResolution;
    }

    /// <summary>Whether the probe independently found the precondition satisfied.</summary>
    public bool Satisfied { get; }

    /// <summary>Typed fail-closed reason taxonomy (INV-006).</summary>
    public string Reason { get; }

    /// <summary>Evidence-reference resolvability, decided by the orchestrator (INV-005).</summary>
    public ReferenceResolution ReferenceResolution { get; }

    /// <summary>
    /// The single public, validation-performing factory for all three protected
    /// types (INV-003/EXT9-08). Returns null when validation fails (an empty/null
    /// reason is invalid — a typed result must always carry a reason).
    /// </summary>
    public static ProbeResult? TryCreate(bool satisfied, string reason, ReferenceResolution referenceResolution)
    {
        if (string.IsNullOrEmpty(reason))
        {
            return null;
        }

        return new ProbeResult(satisfied, reason, referenceResolution);
    }
}
