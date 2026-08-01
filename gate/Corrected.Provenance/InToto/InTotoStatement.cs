// INV-006 / INV-022. GENERIC (reusable) in-toto attestation object model:
// Statement/v1 + subject + digest set. These are the P3-agnostic contracts that the
// release-provenance invariants (INV-031/032/033) will reuse; the determinism-specific
// receipt/predicate live under Corrected.Provenance.Determinism.
using System;
using System.Collections.Generic;

namespace Corrected.Provenance.InToto;

/// <summary>
/// An in-toto <c>Statement/v1</c> (INV-006). Pinned <c>_type</c>, a pinned
/// predicate-type URI, EXACTLY ONE subject, and a typed predicate.
/// </summary>
public sealed class InTotoStatement
{
    /// <summary>
    /// The fixed upstream in-toto Statement/v1 media identity — the ONLY value the
    /// pinned <see cref="Type"/> may carry (single source of truth; A4 mitigation).
    /// </summary>
    public const string StatementTypeV1 = "https://in-toto.io/Statement/v1";

    /// <summary>The in-toto <c>_type</c> field (pinned to the Statement/v1 URI).</summary>
    public string Type { get; init; } = "";

    /// <summary>The pinned predicate-type URI (versioned Corrected determinism predicate).</summary>
    public string PredicateType { get; init; } = "";

    /// <summary>The subject set — INV-006 pins this to exactly one element.</summary>
    public IReadOnlyList<Subject> Subjects { get; init; } = Array.Empty<Subject>();

    /// <summary>The typed predicate (a <c>Determinism.DeterminismPredicate</c> for P3).</summary>
    public object? Predicate { get; init; }
}

/// <summary>An in-toto subject: a canonical name + a digest set.</summary>
public sealed class Subject
{
    public string Name { get; init; } = "";

    public DigestSet Digest { get; init; } = new();
}

/// <summary>
/// An in-toto digest set. INV-006 pins the algorithm to sha256 (lowercase hex over
/// the exact receipt bytes) — this is the ONLY algorithm key the P3 subject carries.
/// </summary>
public sealed class DigestSet
{
    public string Sha256 { get; init; } = "";
}
