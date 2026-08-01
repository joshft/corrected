// INV-030 (P3 phase-entry, Group G / TB-007). The Phase-0.1-ENTRY receipt's OWN
// independently-typed predicate body. It PARALLELS (does NOT reuse)
// Determinism.DeterminismPredicate — release/entry predicates stay independently typed
// (PRH-010 non-recursion). Its subject is the entry commit `X` + the three precondition
// evidence digests (NOT a determinism run receipt).
//
// RS-024 CANONICAL ARTIFACT GRAPH: the "three evidence digests" are not left to an
// implementation to hash reference/pointer strings. Each precondition P1/P2/P3 has a
// MULTI-FILE evidence CLOSURE, so the predicate pins, per precondition, a set-equal
// digest MANIFEST over the FULL evidence closure (one entry per file: canonical path ->
// lowercase-hex sha256 of that file's FULL bytes) — never a single hash of a
// readiness-reference string or an active pointer.
using System;
using System.Collections.Generic;

namespace Corrected.Provenance.Entry;

/// <summary>
/// The typed predicate body of the Phase-0.1-entry attestation (INV-030). It binds the
/// entry commit <see cref="CommitX"/> and, per precondition, the FULL-closure digest
/// <see cref="PreconditionClosure.Manifest"/>. Independently typed from
/// <c>Determinism.DeterminismPredicate</c>.
/// </summary>
public sealed class EntryPredicate
{
    /// <summary>The entry commit <c>X</c> representation (the git commit id being entered).</summary>
    public string CommitX { get; init; } = "";

    /// <summary>
    /// The three precondition evidence closures (P1, P2, P3), in canonical
    /// <c>EntryAttestation.PreconditionOrder</c>. Each carries a FULL-closure digest
    /// manifest — never a reference-string hash (RS-024).
    /// </summary>
    public IReadOnlyList<PreconditionClosure> Preconditions { get; init; } = Array.Empty<PreconditionClosure>();
}

/// <summary>
/// One precondition's FULL evidence closure as a set-equal digest manifest. The manifest
/// is the closure — the exact set of evidence files, each pinned by the lowercase-hex
/// sha256 of its FULL bytes. A single-entry manifest that hashes a pointer/reference
/// string is a schema violation (RS-024), not a valid closure.
/// </summary>
public sealed class PreconditionClosure
{
    /// <summary>The precondition id — one of <c>"P1"</c>, <c>"P2"</c>, <c>"P3"</c>.</summary>
    public string Precondition { get; init; } = "";

    /// <summary>
    /// The set-equal digest manifest over the precondition's FULL evidence closure — one
    /// <see cref="ClosureDigest"/> per evidence file. Canonical order: sorted by
    /// <see cref="ClosureDigest.Path"/> (ordinal).
    /// </summary>
    public IReadOnlyList<ClosureDigest> Manifest { get; init; } = Array.Empty<ClosureDigest>();
}

/// <summary>
/// A single evidence-file digest in a precondition's closure manifest: the file's
/// canonical path + the lowercase-hex sha256 of its FULL bytes.
/// </summary>
public sealed class ClosureDigest
{
    /// <summary>The evidence file's canonical (repo-relative) path.</summary>
    public string Path { get; init; } = "";

    /// <summary>Lowercase-hex sha256 over the file's FULL bytes.</summary>
    public string Sha256 { get; init; } = "";
}
