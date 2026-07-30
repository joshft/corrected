// INV-030 (P3 phase-entry, Group G / TB-007). The Phase-0.1-ENTRY receipt's OWN
// identity contract — built with the SAME rigor as the P3 determinism receipt, but
// PINNED INDEPENDENTLY. It REUSES the generic Corrected.Provenance in-toto
// Statement/Subject/DigestSet/DSSE contracts (INV-022) while keeping its predicate +
// policy INDEPENDENTLY TYPED (parallels Determinism.DeterminismAttestation; does NOT
// reuse the determinism predicate/schema).
//
// This is the SYNTHETIC substrate track: no cosign, no real bundles/fixtures. The
// real signer/cert-identity verify + the RS-006 production-argv negatives defer to
// Track T3/T4 (see the residual note in Inv030EntryReceiptIdentityTests.cs).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Corrected.Provenance.InToto;

namespace Corrected.Provenance.Entry;

/// <summary>
/// Builds + validates the Phase-0.1-entry in-toto attestation graph (INV-030). Owns the
/// entry predicate-type URI + subject-name constants (DISTINCT from determinism), the
/// per-precondition FULL-closure manifest hashing (RS-024), the Statement assembly, and
/// the entry-predicate schema validator.
/// </summary>
public static class EntryAttestation
{
    // ---- Pinned entry-identity contract literals (A4 mitigation: single source of ----
    // ---- truth; Inv030 re-pins these VALUES as enforcement) ----

    /// <summary>
    /// The versioned Corrected PHASE-ENTRY predicate-type URI. DISTINCT from
    /// <c>DeterminismAttestation.PredicateTypeUri</c> — an entry attestation and a
    /// determinism attestation must never share a predicate type (RS-024
    /// cross-rejection).
    ///
    /// A4-CLASS RED-INVENTED DEFAULT (confirm-before-commit): this literal is a permanent,
    /// INV-016-frozen id. The RED tests pin it as the contract; GREEN/PR2 must confirm or
    /// reconcile the exact literal before it is frozen.
    /// </summary>
    public const string PredicateTypeUri = "https://correctless.org/attestations/phase-entry/v1";

    /// <summary>The canonical in-toto subject name for the entry commit <c>X</c>. Distinct from <c>determinism-run-receipt</c>.</summary>
    public const string CommitSubjectName = "phase-entry-commit";

    /// <summary>The canonical in-toto subject name for precondition P1's evidence closure.</summary>
    public const string P1SubjectName = "phase-entry-precondition-p1";

    /// <summary>The canonical in-toto subject name for precondition P2's evidence closure.</summary>
    public const string P2SubjectName = "phase-entry-precondition-p2";

    /// <summary>The canonical in-toto subject name for precondition P3's evidence closure.</summary>
    public const string P3SubjectName = "phase-entry-precondition-p3";

    /// <summary>The canonical precondition ordering pinned into the subject list + predicate.</summary>
    public static readonly IReadOnlyList<string> PreconditionOrder = new[] { "P1", "P2", "P3" };

    /// <summary>
    /// The canonical, EXACT subject-name ordering: the commit subject first, then the P1,
    /// P2, P3 closure subjects. Cardinality (4), names, and ORDER are all pinned.
    /// </summary>
    public static readonly IReadOnlyList<string> SubjectNameOrder = new[]
    {
        CommitSubjectName,
        P1SubjectName,
        P2SubjectName,
        P3SubjectName,
    };

    /// <summary>
    /// Lowercase-hex sha256 over a single evidence file's FULL bytes — the per-file
    /// closure digest. GREEN: <c>Convert.ToHexString(SHA256.HashData(fileBytes)).ToLowerInvariant()</c>.
    /// </summary>
    public static string ComputeFileDigest(byte[] fileBytes)
    {
        ArgumentNullException.ThrowIfNull(fileBytes);

        // Hash the FULL file bytes — NEVER a reference/pointer string (RS-024).
        return Convert.ToHexString(SHA256.HashData(fileBytes)).ToLowerInvariant();
    }

    /// <summary>
    /// The commit-<c>X</c> subject digest: lowercase-hex sha256 over the UTF-8 bytes of
    /// the commit id (the canonical commit-X representation).
    /// </summary>
    public static string ComputeCommitDigest(string commitX)
    {
        ArgumentNullException.ThrowIfNull(commitX);

        return Sha256HexUtf8(commitX);
    }

    /// <summary>
    /// The per-precondition SUBJECT digest: a canonical manifest-root over the FULL
    /// closure manifest. Canonical form (the pinned contract): entries sorted by
    /// <c>Path</c> (ordinal); each line = <c>sha256 + "  " + path + "\n"</c>; the root is
    /// lowercase-hex sha256 over the UTF-8 concatenation. Binds the subject to the FULL
    /// manifest (defeats a ref-string-hash subject).
    /// </summary>
    public static string ComputeManifestRoot(IReadOnlyList<ClosureDigest> manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        // Canonical byte pre-image: entries sorted by Path (ordinal); per-entry line is
        // exactly `sha256 + "  " + path + "\n"`. This MUST be byte-identical to the test's
        // ManifestRoot oracle, so the sort/separator/newline are pinned verbatim.
        var sb = new StringBuilder();
        foreach (ClosureDigest e in manifest.OrderBy(x => x.Path, StringComparer.Ordinal))
        {
            sb.Append(e.Sha256).Append("  ").Append(e.Path).Append('\n');
        }

        return Sha256HexUtf8(sb.ToString());
    }

    /// <summary>
    /// Assemble the entry in-toto <c>Statement/v1</c>: pinned <c>_type</c>, the entry
    /// predicate-type URI, EXACTLY FOUR subjects (commit-X + one per precondition closure,
    /// canonical names/order), and the entry predicate — whose per-precondition manifest
    /// hashes the FULL closure BYTES (lowercase-hex sha256 PER FILE), NOT a pointer/
    /// reference string (RS-024).
    /// </summary>
    public static InTotoStatement BuildEntryStatement(
        string commitX,
        IReadOnlyDictionary<string, byte[]> p1Closure,
        IReadOnlyDictionary<string, byte[]> p2Closure,
        IReadOnlyDictionary<string, byte[]> p3Closure)
    {
        ArgumentNullException.ThrowIfNull(commitX);
        ArgumentNullException.ThrowIfNull(p1Closure);
        ArgumentNullException.ThrowIfNull(p2Closure);
        ArgumentNullException.ThrowIfNull(p3Closure);

        // Per-precondition FULL-closure manifest: one entry per file, path -> sha256 of
        // that file's FULL bytes, sorted by path (ordinal). NEVER a ref/pointer string.
        ClosureDigest[] p1Manifest = BuildManifest(p1Closure);
        ClosureDigest[] p2Manifest = BuildManifest(p2Closure);
        ClosureDigest[] p3Manifest = BuildManifest(p3Closure);

        var predicate = new EntryPredicate
        {
            CommitX = commitX,
            Preconditions = new[]
            {
                new PreconditionClosure { Precondition = "P1", Manifest = p1Manifest },
                new PreconditionClosure { Precondition = "P2", Manifest = p2Manifest },
                new PreconditionClosure { Precondition = "P3", Manifest = p3Manifest },
            },
        };

        return new InTotoStatement
        {
            Type = InTotoStatement.StatementTypeV1,
            PredicateType = PredicateTypeUri,
            Subjects = new[]
            {
                // subject[0] = the entry commit X (sha256 over the commit-id UTF-8 bytes).
                new Subject { Name = CommitSubjectName, Digest = new DigestSet { Sha256 = ComputeCommitDigest(commitX) } },
                // subjects[1..3] = P1/P2/P3, each digest = manifest ROOT of its FULL closure
                // (the subject<->manifest binding). Order is pinned.
                new Subject { Name = P1SubjectName, Digest = new DigestSet { Sha256 = ComputeManifestRoot(p1Manifest) } },
                new Subject { Name = P2SubjectName, Digest = new DigestSet { Sha256 = ComputeManifestRoot(p2Manifest) } },
                new Subject { Name = P3SubjectName, Digest = new DigestSet { Sha256 = ComputeManifestRoot(p3Manifest) } },
            },
            Predicate = predicate,
        };
    }

    /// <summary>
    /// Validate an entry-predicate Statement against the pinned identity schema (INV-030):
    /// exact subject cardinality (4), the pinned subject names in canonical order, the
    /// sha256 algorithm (64 lowercase-hex), the entry predicate-type URI, and — for EACH
    /// precondition — a FULL-closure digest manifest (multi-file, set-equal to the closure)
    /// whose manifest-root equals the precondition subject digest. REJECTS a reference-
    /// string-hash manifest (RS-024 load-bearing negative) and a determinism predicate type
    /// (bidirectional cross-rejection).
    /// </summary>
    public static EntrySchemaResult ValidateEntrySchema(InTotoStatement statement)
    {
        // Fail closed on a null statement.
        if (statement is null)
        {
            return Fail("null-statement");
        }

        // (1) Predicate type FIRST — a determinism-predicate statement cross-rejects here
        // before any entry-shape inspection (bidirectional cross-rejection). Then the
        // pinned Statement/v1 _type.
        if (!string.Equals(statement.PredicateType, PredicateTypeUri, StringComparison.Ordinal))
        {
            return Fail("predicate-type-not-entry");
        }

        if (!string.Equals(statement.Type, InTotoStatement.StatementTypeV1, StringComparison.Ordinal))
        {
            return Fail("statement-type-not-v1");
        }

        // (2) EXACTLY four subjects with the pinned names in the pinned ORDER, each digest a
        // pinned-algorithm sha256 (64 lowercase-hex). A dropped/permuted/renamed subject or
        // a wrong-length (e.g. sha1) digest fails here.
        IReadOnlyList<Subject> subjects = statement.Subjects;
        if (subjects is null || subjects.Count != SubjectNameOrder.Count)
        {
            return Fail("subject-cardinality");
        }

        for (int i = 0; i < SubjectNameOrder.Count; i++)
        {
            Subject s = subjects[i];
            if (s is null || !string.Equals(s.Name, SubjectNameOrder[i], StringComparison.Ordinal))
            {
                return Fail("subject-name-or-order");
            }

            if (s.Digest is null || !IsSha256Hex(s.Digest.Sha256))
            {
                return Fail("subject-digest-algorithm");
            }
        }

        // (3) The predicate must be the INDEPENDENTLY-TYPED entry predicate.
        if (statement.Predicate is not EntryPredicate predicate)
        {
            return Fail("predicate-not-entry");
        }

        // (4) The commit subject digest must bind to the predicate's commit-X.
        if (!string.Equals(
                subjects[0].Digest.Sha256,
                ComputeCommitDigest(predicate.CommitX ?? string.Empty),
                StringComparison.Ordinal))
        {
            return Fail("commit-subject-binding");
        }

        // (5) EXACTLY the three preconditions P1/P2/P3, correctly labelled, in canonical
        // order. (Two-only / a "P4" mislabel fail here.) Done BEFORE the per-precondition
        // manifest + binding checks so index i+1 into subjects[] is always in range.
        IReadOnlyList<PreconditionClosure> preconditions = predicate.Preconditions;
        if (preconditions is null || preconditions.Count != PreconditionOrder.Count)
        {
            return Fail("precondition-cardinality");
        }

        for (int i = 0; i < PreconditionOrder.Count; i++)
        {
            PreconditionClosure pc = preconditions[i];
            if (pc is null || !string.Equals(pc.Precondition, PreconditionOrder[i], StringComparison.Ordinal))
            {
                return Fail("precondition-label-or-order");
            }
        }

        // (6) Per precondition: a FULL multi-file closure manifest (>=2 entries; a single
        // ref-string entry is a schema violation even if internally consistent), each entry
        // a non-empty path + lowercase-hex sha256, in canonical (path-ordinal) order, and —
        // the core canonical-graph binding — the precondition SUBJECT digest equals the
        // manifest ROOT of that FULL closure.
        for (int i = 0; i < PreconditionOrder.Count; i++)
        {
            PreconditionClosure pc = preconditions[i];
            IReadOnlyList<ClosureDigest> manifest = pc.Manifest;

            // Multi-file FULL closure (defeats the ref-string / empty-manifest collapse).
            if (manifest is null || manifest.Count < 2)
            {
                return Fail("precondition-manifest-not-full-closure");
            }

            // Each entry is a genuine path + lowercase-hex sha256 (not a pointer string).
            foreach (ClosureDigest cd in manifest)
            {
                if (cd is null || string.IsNullOrEmpty(cd.Path) || !IsSha256Hex(cd.Sha256))
                {
                    return Fail("precondition-manifest-entry-shape");
                }
            }

            // Canonical order: paths STRICTLY increasing (ordinal). A permuted/reversed
            // manifest fails here even though ComputeManifestRoot re-sorts internally.
            for (int j = 1; j < manifest.Count; j++)
            {
                if (string.CompareOrdinal(manifest[j - 1].Path, manifest[j].Path) >= 0)
                {
                    return Fail("precondition-manifest-order");
                }
            }

            // Subject<->manifest binding: subjects[i + 1] is precondition i's subject.
            string subjectDigest = subjects[i + 1].Digest.Sha256;
            if (!string.Equals(subjectDigest, ComputeManifestRoot(manifest), StringComparison.Ordinal))
            {
                return Fail("precondition-subject-manifest-binding");
            }
        }

        return new EntrySchemaResult { Valid = true, Reason = string.Empty };
    }

    // ---- Internal helpers (kept inside this file per the BCL-only, no-external-package ----
    // ---- constraint; no I/O — the closures arrive as in-memory byte maps). ----

    /// <summary>
    /// The FULL-closure digest manifest of a file->bytes closure: one entry per file,
    /// path -> lowercase-hex sha256 of its FULL bytes, sorted by path (ordinal).
    /// </summary>
    private static ClosureDigest[] BuildManifest(IReadOnlyDictionary<string, byte[]> closure)
        => closure
            .Select(kv => new ClosureDigest { Path = kv.Key, Sha256 = ComputeFileDigest(kv.Value) })
            .OrderBy(x => x.Path, StringComparer.Ordinal)
            .ToArray();

    /// <summary>Lowercase-hex sha256 over the UTF-8 bytes of <paramref name="s"/>.</summary>
    private static string Sha256HexUtf8(string s)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s))).ToLowerInvariant();

    /// <summary>True iff <paramref name="s"/> is exactly 64 lowercase-hex chars (a pinned sha256 digest).</summary>
    private static bool IsSha256Hex(string? s)
    {
        if (s is null || s.Length != 64)
        {
            return false;
        }

        foreach (char c in s)
        {
            bool isLowerHex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f');
            if (!isLowerHex)
            {
                return false;
            }
        }

        return true;
    }

    private static EntrySchemaResult Fail(string reason)
        => new EntrySchemaResult { Valid = false, Reason = reason };
}

/// <summary>Typed result of the entry-predicate schema validation.</summary>
public sealed class EntrySchemaResult
{
    /// <summary>True iff the Statement satisfies the entry identity schema.</summary>
    public bool Valid { get; init; }

    /// <summary>A specific, actionable reason when <see cref="Valid"/> is false.</summary>
    public string Reason { get; init; } = "";
}
