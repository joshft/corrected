// INV-006 (P3-specific). The versioned Corrected determinism predicate.
// It REFERENCES the receipt digest (never re-embeds the receipt bytes) and EMBEDS
// the typed per-role projection FACTS (digests) — NEVER the volatile raw reports.
using System;
using System.Collections.Generic;

namespace Corrected.Provenance.Determinism;

/// <summary>
/// The typed predicate body of the determinism attestation (INV-006). Embed-vs-
/// reference is pinned: <see cref="ReceiptDigest"/> REFERENCES the subject by its
/// sha256; <see cref="ProjectionFacts"/> EMBEDS the typed per-role projection facts.
/// There is intentionally NO field carrying raw report bytes.
/// </summary>
public sealed class DeterminismPredicate
{
    /// <summary>Reference to the subject: the sha256 of the receipt bytes.</summary>
    public string ReceiptDigest { get; init; } = "";

    /// <summary>Embedded typed per-role projection facts (digests, not raw reports).</summary>
    public IReadOnlyList<ProjectionFact> ProjectionFacts { get; init; } = Array.Empty<ProjectionFact>();
}

/// <summary>A typed per-role projection fact embedded in the predicate.</summary>
public sealed class ProjectionFact
{
    public string Role { get; init; } = "";

    public string Kind { get; init; } = "";

    public string ProjectionSha256 { get; init; } = "";
}
