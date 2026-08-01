using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Corrected.Provenance.Determinism;
using Corrected.Provenance.Entry;
using Corrected.Provenance.InToto;
using Xunit;

namespace Corrected.Gate.Tests;

/// <summary>
/// INV-030 (P3, Group G / TB-007): "The entry receipt has its own independently-typed
/// identity contract." The Phase-0.1-entry receipt is built with the SAME rigor as the
/// P3 receipt, but its identity is pinned INDEPENDENTLY: its own predicate-type URI +
/// schema (distinct from determinism -- its subject is the entry commit `X` + the three
/// precondition evidence digests, NOT a run receipt). It REUSES the generic
/// Corrected.Provenance Statement/Subject/DigestSet/DSSE contracts (INV-022) but keeps
/// its entry predicate + policy INDEPENDENTLY TYPED.
///
/// RS-024 CANONICAL ARTIFACT GRAPH: the "three evidence digests" are not left to an impl
/// to hash reference/pointer strings. P1/P2/P3 each have a MULTI-FILE evidence CLOSURE,
/// so the entry subject/predicate pins exact subject cardinality, subject names, digest
/// algorithm (sha256), canonical byte encoding, ordering, and -- per precondition -- a
/// SET-EQUAL digest MANIFEST over the FULL closure (never the reference strings / active
/// pointers) + the commit-`X` representation. The subject<->manifest binding is the core
/// canonical-graph property: each precondition SUBJECT digest is the manifest ROOT of its
/// FULL closure. A builder that hashed only pointer strings (whose own builder+verifier
/// fixtures would agree) is a SCHEMA VIOLATION, not a passing impl.
///
/// RS-024 BIDIRECTIONAL CROSS-REJECTION: because the entry receipt MAY share the P3
/// signer identity, distinctness cannot rest on the predicate-type URI alone -- a genuine
/// determinism Statement presented to the entry gate MUST reject, and a genuine entry
/// Statement presented to the determinism (P3) predicate-type gate MUST reject.
///
/// SCOPE: all SYNTHETIC -- no cosign, no real bundles/fixtures. Modeled at the
/// predicate-type + schema layer (INV-022 generic verify + the entry schema validator).
/// AP-031 note: these tests parse NO Correctless-produced artifact (in-memory synthetic
/// closures only), so the real-fixture requirement is DORMANT here.
///
/// RS-006 / RS-011 DEFERRED RESIDUAL (encoded as a comment, NOT tested here -- needs real
/// cosign + fixtures, Track T3/T4): the TWO honest negatives through the PRODUCTION entry
/// argv are NOT built in this synthetic track --
///   (2a) a fixture-identity entry bundle driven through the frozen production entry argv
///        (production --certificate-identity) -> rejection attributable to
///        `identity-mismatch`; and
///   (2b) the entry cert-SHA <-> `X` cross-check under a fixture-ACCEPTING entry policy
///        (receipt `X` != cert workflow-SHA -> the SHA-cross-check reason).
/// Like P3's, the production-argv entry-accept path is unexercisable until P2 (RS-011
/// residual-ledger entry). No test for it here.
///
/// RED expectation (against the STUB:TDD substrate):
///   - distinctness cells (read constants)                -> PASS
///   - schema NEGATIVES (assert Valid==false)             -> PASS (deny-by-default stub)
///   - cross-rejection mismatch cells (assert false)      -> PASS (deny-by-default stub)
///   - builder-hashes-FULL-closure + subject<->root cells -> FAIL (stub returns empty stmt)
///   - schema POSITIVE + builder round-trip cells         -> FAIL (deny-by-default stub)
///   - predicate-type verifier MATCHING-type positive     -> FAIL (stub returns false)
/// </summary>
public class Inv030EntryReceiptIdentityTests
{
    // ---- Pinned entry-identity contract literals (the test is the source of truth in ----
    // ---- RED; A4-CLASS invented defaults -- confirm-before-commit). ----

    // DECISION: pin the DISTINCT entry predicate-type URI as the contract. Chosen shape:
    // corrected domain + versioned phase-entry path. Must be != the determinism URI.
    private const string EntryPredicateTypeUri = "https://correctless.org/attestations/phase-entry/v1";
    private const string DeterminismPredicateTypeUri = "https://correctless.org/attestations/determinism/v1";

    private const string CommitSubjectName = "phase-entry-commit";
    private const string P1SubjectName = "phase-entry-precondition-p1";
    private const string P2SubjectName = "phase-entry-precondition-p2";
    private const string P3SubjectName = "phase-entry-precondition-p3";

    // A synthetic 40-hex entry commit `X`.
    private const string CommitX = "0123456789abcdef0123456789abcdef01234567";

    // ---- Independent (BCL) oracles -- never call the EntryAttestation stubs for these ----

    private static string Sha256Hex(byte[] bytes)
        => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static string Sha256HexUtf8(string s)
        => Sha256Hex(Encoding.UTF8.GetBytes(s));

    // The pinned canonical manifest-root: entries sorted by Path (ordinal); each line =
    // sha256 + "  " + path + "\n"; root = lowercase-hex sha256 over the UTF-8 concat.
    // GREEN's EntryAttestation.ComputeManifestRoot MUST match this exactly.
    private static string ManifestRoot(IReadOnlyList<ClosureDigest> manifest)
    {
        var sb = new StringBuilder();
        foreach (ClosureDigest e in manifest.OrderBy(x => x.Path, StringComparer.Ordinal))
        {
            sb.Append(e.Sha256).Append("  ").Append(e.Path).Append('\n');
        }

        return Sha256HexUtf8(sb.ToString());
    }

    // The FULL-closure digest manifest of a file->bytes closure: one entry per file,
    // path -> lowercase-hex sha256 of its FULL bytes, sorted by path (ordinal).
    private static ClosureDigest[] ManifestOf(IReadOnlyDictionary<string, byte[]> closure)
        => closure
            .Select(kv => new ClosureDigest { Path = kv.Key, Sha256 = Sha256Hex(kv.Value) })
            .OrderBy(x => x.Path, StringComparer.Ordinal)
            .ToArray();

    // ---- Synthetic multi-file evidence closures (RS-024: each precondition is a ----
    // ---- MULTI-FILE closure). Invented paths/bytes -- confirm-before-commit. ----

    private static Dictionary<string, byte[]> P1Closure() => new()
    {
        ["test/attestations/entry/p1/inv008-p1-probe.json"] = Encoding.UTF8.GetBytes("p1-probe-body-alpha"),
        ["test/attestations/entry/p1/inv008-tree-migrated.json"] = Encoding.UTF8.GetBytes("p1-tree-migrated-beta"),
        ["test/attestations/entry/p1/inv008-closure.json"] = Encoding.UTF8.GetBytes("p1-closure-gamma"),
    };

    private static Dictionary<string, byte[]> P2Closure() => new()
    {
        ["test/attestations/entry/p2/inv010-receipt.json"] = Encoding.UTF8.GetBytes("p2-receipt-delta"),
        ["test/attestations/entry/p2/inv010-bundle.sigstore.json"] = Encoding.UTF8.GetBytes("p2-bundle-epsilon"),
    };

    private static Dictionary<string, byte[]> P3Closure() => new()
    {
        ["test/attestations/entry/p3/inv010-receipt.json"] = Encoding.UTF8.GetBytes("p3-receipt-zeta"),
        ["test/attestations/entry/p3/inv010-bundle.sigstore.json"] = Encoding.UTF8.GetBytes("p3-bundle-eta"),
        ["test/attestations/entry/p3/active-baseline-pointer.json"] = Encoding.UTF8.GetBytes("p3-pointer-theta"),
        ["test/attestations/entry/p3/trusted-root.json"] = Encoding.UTF8.GetBytes("p3-root-iota"),
    };

    // A hand-built WELL-FORMED entry Statement (the schema-positive base + cross-rejection
    // source). Built with independent BCL oracles so it is genuinely well-formed even in
    // RED; only ValidateEntrySchema being a STUB makes the positive go RED. The per-
    // precondition subject digest is the manifest ROOT of that closure (subject<->manifest
    // binding), so a correct GREEN validator that recomputes the root will accept it.
    private static InTotoStatement WellFormedEntryStatement(
        string commitX,
        IReadOnlyDictionary<string, byte[]> p1,
        IReadOnlyDictionary<string, byte[]> p2,
        IReadOnlyDictionary<string, byte[]> p3)
    {
        ClosureDigest[] p1m = ManifestOf(p1);
        ClosureDigest[] p2m = ManifestOf(p2);
        ClosureDigest[] p3m = ManifestOf(p3);

        var predicate = new EntryPredicate
        {
            CommitX = commitX,
            Preconditions = new[]
            {
                new PreconditionClosure { Precondition = "P1", Manifest = p1m },
                new PreconditionClosure { Precondition = "P2", Manifest = p2m },
                new PreconditionClosure { Precondition = "P3", Manifest = p3m },
            },
        };

        return new InTotoStatement
        {
            Type = InTotoStatement.StatementTypeV1,
            PredicateType = EntryPredicateTypeUri,
            Subjects = new[]
            {
                new Subject { Name = CommitSubjectName, Digest = new DigestSet { Sha256 = Sha256HexUtf8(commitX) } },
                new Subject { Name = P1SubjectName, Digest = new DigestSet { Sha256 = ManifestRoot(p1m) } },
                new Subject { Name = P2SubjectName, Digest = new DigestSet { Sha256 = ManifestRoot(p2m) } },
                new Subject { Name = P3SubjectName, Digest = new DigestSet { Sha256 = ManifestRoot(p3m) } },
            },
            Predicate = predicate,
        };
    }

    private static InTotoStatement WellFormedEntryStatement()
        => WellFormedEntryStatement(CommitX, P1Closure(), P2Closure(), P3Closure());

    // Re-wrap a statement with a replacement predicate, preserving type/predicate-type/
    // subjects (used by the predicate-STRUCTURE negatives).
    private static InTotoStatement WithPredicate(InTotoStatement stmt, EntryPredicate predicate)
        => new InTotoStatement
        {
            Type = stmt.Type,
            PredicateType = stmt.PredicateType,
            Subjects = stmt.Subjects,
            Predicate = predicate,
        };

    // A genuine determinism-predicate-type Statement (hand-built at the predicate-type
    // layer -- no dependency on the RunReceipt parser stub). Used for cross-rejection.
    private static InTotoStatement DeterminismStatement()
    {
        string subjectDigest = Sha256HexUtf8("synthetic-determinism-run-receipt-bytes");
        return new InTotoStatement
        {
            Type = InTotoStatement.StatementTypeV1,
            PredicateType = DeterminismAttestation.PredicateTypeUri,
            Subjects = new[]
            {
                new Subject
                {
                    Name = DeterminismAttestation.SubjectName,
                    Digest = new DigestSet { Sha256 = subjectDigest },
                },
            },
            Predicate = new DeterminismPredicate
            {
                ReceiptDigest = subjectDigest,
                ProjectionFacts = Array.Empty<ProjectionFact>(),
            },
        };
    }

    // =====================================================================
    // (1) Distinct pinned identity constants -- the entry predicate type +
    //     subject names are DISTINCT from determinism (INV-030 clause: "its
    //     own predicate type + schema (distinct from the determinism predicate)").
    // =====================================================================

    // Tests INV-030 [unit]: the entry predicate-type URI is pinned AND distinct from the
    // determinism predicate-type URI. RED expectation: PASS (reads real constants).
    [Fact]
    public void Entry_predicate_type_uri_is_pinned_and_distinct_from_determinism()
    {
        // Pinned to the invented contract literal (A4 -- catches drift).
        Assert.Equal(EntryPredicateTypeUri, EntryAttestation.PredicateTypeUri);

        // The load-bearing distinctness: an entry attestation and a determinism
        // attestation must NEVER share a predicate type.
        Assert.NotEqual(DeterminismAttestation.PredicateTypeUri, EntryAttestation.PredicateTypeUri);
        Assert.NotEqual(DeterminismPredicateTypeUri, EntryAttestation.PredicateTypeUri);
    }

    // Tests INV-030 [unit]: EVERY pinned entry subject name is distinct from the
    // determinism run-receipt subject name (the entry subject is the entry commit + the
    // three precondition digests, NOT a run receipt). RED expectation: PASS.
    [Fact]
    public void Entry_subject_names_are_distinct_from_determinism_run_receipt()
    {
        string[] entryNames =
        {
            EntryAttestation.CommitSubjectName,
            EntryAttestation.P1SubjectName,
            EntryAttestation.P2SubjectName,
            EntryAttestation.P3SubjectName,
        };

        // Pinned to the invented contract literals (A4 -- catches drift).
        Assert.Equal(CommitSubjectName, EntryAttestation.CommitSubjectName);
        Assert.Equal(P1SubjectName, EntryAttestation.P1SubjectName);
        Assert.Equal(P2SubjectName, EntryAttestation.P2SubjectName);
        Assert.Equal(P3SubjectName, EntryAttestation.P3SubjectName);

        // None may collide with the determinism run-receipt subject name.
        foreach (string name in entryNames)
        {
            Assert.NotEqual(DeterminismAttestation.SubjectName, name);
            Assert.NotEqual("determinism-run-receipt", name);
        }

        // The four entry subject names are themselves distinct (cardinality-4 identity).
        Assert.Equal(4, entryNames.Distinct().Count());
    }

    // =====================================================================
    // (2) The builder hashes the FULL closure BYTES into a per-precondition
    //     set-equal digest MANIFEST -- NOT a reference/pointer-string hash
    //     (RS-024). RED expectation: FAIL (stub returns an empty Statement).
    // =====================================================================

    // Tests INV-030 [integration]: the built entry Statement carries EXACTLY FOUR
    // subjects with the pinned canonical names, in canonical order (commit, P1, P2, P3).
    [Fact]
    public void Builder_produces_four_subjects_with_pinned_names_in_order()
    {
        InTotoStatement stmt = EntryAttestation.BuildEntryStatement(
            CommitX, P1Closure(), P2Closure(), P3Closure());

        Assert.Equal(InTotoStatement.StatementTypeV1, stmt.Type);
        Assert.Equal(EntryAttestation.PredicateTypeUri, stmt.PredicateType);

        Assert.Equal(4, stmt.Subjects.Count);
        Assert.Equal(
            new[] { CommitSubjectName, P1SubjectName, P2SubjectName, P3SubjectName },
            stmt.Subjects.Select(s => s.Name).ToArray());

        // Every subject digest is 64 lowercase-hex (sha256 algorithm pinned).
        foreach (Subject s in stmt.Subjects)
        {
            Assert.Matches("^[0-9a-f]{64}$", s.Digest.Sha256);
        }
    }

    // Tests INV-030 [integration]: the commit subject digest is sha256 over the UTF-8
    // bytes of the commit id (the canonical commit-`X` representation).
    [Fact]
    public void Builder_commit_subject_digest_is_sha256_of_commit_bytes()
    {
        InTotoStatement stmt = EntryAttestation.BuildEntryStatement(
            CommitX, P1Closure(), P2Closure(), P3Closure());

        // Assert presence first so the stub (0 subjects) fails as an ASSERTION here,
        // not via a later Single() sequence-empty exception.
        Assert.Contains(stmt.Subjects, s => s.Name == CommitSubjectName);

        Subject commit = stmt.Subjects.Single(s => s.Name == CommitSubjectName);
        Assert.Equal(Sha256HexUtf8(CommitX), commit.Digest.Sha256);
    }

    // Tests INV-030 [integration] (RS-024 LOAD-BEARING): the per-precondition manifest is
    // SET-EQUAL to the lowercase-hex sha256 of each closure file's FULL bytes,
    // independently recomputed -- proving it is NOT a single reference/pointer-string hash.
    // Across a MULTI-FILE closure per precondition; ordering pinned (sorted by path).
    [Fact]
    public void Builder_per_precondition_manifest_set_equals_full_closure_digests()
    {
        Dictionary<string, byte[]> p1 = P1Closure();
        Dictionary<string, byte[]> p2 = P2Closure();
        Dictionary<string, byte[]> p3 = P3Closure();

        InTotoStatement stmt = EntryAttestation.BuildEntryStatement(CommitX, p1, p2, p3);

        EntryPredicate predicate = Assert.IsType<EntryPredicate>(stmt.Predicate);
        Assert.Equal(CommitX, predicate.CommitX);

        // Exactly the three preconditions, in canonical order.
        Assert.Equal(
            new[] { "P1", "P2", "P3" },
            predicate.Preconditions.Select(p => p.Precondition).ToArray());

        var closures = new Dictionary<string, IReadOnlyDictionary<string, byte[]>>
        {
            ["P1"] = p1,
            ["P2"] = p2,
            ["P3"] = p3,
        };

        foreach (PreconditionClosure pc in predicate.Preconditions)
        {
            IReadOnlyDictionary<string, byte[]> closure = closures[pc.Precondition];

            // A FULL closure is MULTI-FILE -- a single-entry manifest is the ref-string
            // violation this cell exists to defeat.
            Assert.True(closure.Count >= 2, "test fixture: each precondition closure is multi-file");
            Assert.Equal(closure.Count, pc.Manifest.Count);

            // SET-EQUAL: the manifest's (path -> sha256-of-FULL-bytes) pairs equal the
            // independently recomputed closure digests, exactly.
            var expected = closure
                .Select(kv => (kv.Key, Sha256Hex(kv.Value)))
                .OrderBy(x => x.Key, StringComparer.Ordinal)
                .ToArray();
            var actual = pc.Manifest
                .Select(m => (m.Path, m.Sha256))
                .OrderBy(x => x.Path, StringComparer.Ordinal)
                .ToArray();
            Assert.Equal(expected, actual);

            // ORDERING pinned: the manifest is emitted sorted by path (ordinal).
            Assert.Equal(
                pc.Manifest.Select(m => m.Path).OrderBy(x => x, StringComparer.Ordinal).ToArray(),
                pc.Manifest.Select(m => m.Path).ToArray());

            // Each manifest digest is genuine 64 lowercase-hex sha256, NOT a pointer string.
            foreach (ClosureDigest cd in pc.Manifest)
            {
                Assert.Matches("^[0-9a-f]{64}$", cd.Sha256);
            }
        }
    }

    // Tests INV-030 [integration] (RS-024 B2(i) subject<->manifest binding): each
    // precondition SUBJECT digest equals the INDEPENDENTLY-recomputed manifest ROOT of
    // that precondition's FULL closure. This pins the core canonical-graph property; it
    // defeats a builder that emits any 64-hex subject digest while ComputeManifestRoot
    // stays a "" stub (the regex-shape check alone cannot catch that). RED expectation:
    // FAIL (stub returns an empty Statement).
    [Fact]
    public void Builder_precondition_subject_digest_is_manifest_root_of_full_closure()
    {
        Dictionary<string, byte[]> p1 = P1Closure();
        Dictionary<string, byte[]> p2 = P2Closure();
        Dictionary<string, byte[]> p3 = P3Closure();

        InTotoStatement stmt = EntryAttestation.BuildEntryStatement(CommitX, p1, p2, p3);

        // Assert cardinality first so the stub (0 subjects) fails as an ASSERTION here,
        // not via a later Single() sequence-empty exception.
        Assert.Equal(4, stmt.Subjects.Count);

        Assert.Equal(
            ManifestRoot(ManifestOf(p1)),
            stmt.Subjects.Single(s => s.Name == P1SubjectName).Digest.Sha256);
        Assert.Equal(
            ManifestRoot(ManifestOf(p2)),
            stmt.Subjects.Single(s => s.Name == P2SubjectName).Digest.Sha256);
        Assert.Equal(
            ManifestRoot(ManifestOf(p3)),
            stmt.Subjects.Single(s => s.Name == P3SubjectName).Digest.Sha256);
    }

    // Tests INV-030 [integration] (RS-024): the manifest digest is genuinely OVER the file
    // BYTES -- flipping ONE byte of ONE closure file changes exactly that precondition's
    // manifest. A ref-string hash (over an unchanged pointer) could not distinguish them.
    // A1: the change propagates to the precondition SUBJECT digest (manifest root), not
    // only the per-file ClosureDigest.
    [Fact]
    public void Builder_manifest_digest_changes_when_a_closure_file_byte_flips()
    {
        Dictionary<string, byte[]> p1 = P1Closure();

        // Tamper one byte of one P1 file.
        Dictionary<string, byte[]> p1Tampered = P1Closure();
        string firstKey = p1Tampered.Keys.OrderBy(k => k, StringComparer.Ordinal).First();
        byte[] tampered = (byte[])p1Tampered[firstKey].Clone();
        tampered[0] ^= 0xFF;
        p1Tampered[firstKey] = tampered;

        // Oracle sanity: a one-byte flip changes the independently-recomputed P1 root.
        Assert.NotEqual(ManifestRoot(ManifestOf(p1)), ManifestRoot(ManifestOf(p1Tampered)));

        InTotoStatement original = EntryAttestation.BuildEntryStatement(CommitX, p1, P2Closure(), P3Closure());
        InTotoStatement mutated = EntryAttestation.BuildEntryStatement(CommitX, p1Tampered, P2Closure(), P3Closure());

        EntryPredicate op = Assert.IsType<EntryPredicate>(original.Predicate);
        EntryPredicate mp = Assert.IsType<EntryPredicate>(mutated.Predicate);

        // (a) the per-file ClosureDigest differs.
        ClosureDigest origEntry = op.Preconditions.Single(p => p.Precondition == "P1")
            .Manifest.Single(m => m.Path == firstKey);
        ClosureDigest mutEntry = mp.Preconditions.Single(p => p.Precondition == "P1")
            .Manifest.Single(m => m.Path == firstKey);
        Assert.NotEqual(origEntry.Sha256, mutEntry.Sha256);

        // (b, A1) the P1 SUBJECT digest (manifest root) differs too.
        string origRoot = original.Subjects.Single(s => s.Name == P1SubjectName).Digest.Sha256;
        string mutRoot = mutated.Subjects.Single(s => s.Name == P1SubjectName).Digest.Sha256;
        Assert.NotEqual(origRoot, mutRoot);
    }

    // =====================================================================
    // (3) The entry-predicate schema validator -- POSITIVE + NEGATIVES.
    // =====================================================================

    // Tests INV-030 [integration]: a well-formed entry Statement validates.
    // RED expectation: FAIL (deny-by-default stub returns Valid=false).
    [Fact]
    public void Schema_validator_accepts_well_formed_entry_statement()
    {
        EntrySchemaResult result = EntryAttestation.ValidateEntrySchema(WellFormedEntryStatement());
        Assert.True(result.Valid, $"expected well-formed entry statement to validate; reason='{result.Reason}'");
    }

    // Tests INV-030 [integration] (RS-024 B2(iii) round-trip): the builder's OWN output
    // validates -- proving the builder and the validator are mutually consistent (neither
    // can drift into a private encoding the other rejects). RED expectation: FAIL (empty-
    // statement builder stub + deny-by-default validator stub).
    [Fact]
    public void Builder_output_validates_round_trip()
    {
        InTotoStatement built = EntryAttestation.BuildEntryStatement(
            CommitX, P1Closure(), P2Closure(), P3Closure());
        EntrySchemaResult result = EntryAttestation.ValidateEntrySchema(built);
        Assert.True(result.Valid, $"builder output must validate; reason='{result.Reason}'");
    }

    // Tests INV-030 [integration]: the schema rejects a WRONG predicate type (here the
    // determinism URI). RED expectation: PASS (stub denies). This is also the entry-side
    // half of the bidirectional cross-rejection.
    [Fact]
    public void Schema_rejects_wrong_predicate_type()
    {
        InTotoStatement stmt = WellFormedEntryStatement();
        var bad = new InTotoStatement
        {
            Type = stmt.Type,
            PredicateType = DeterminismAttestation.PredicateTypeUri, // wrong
            Subjects = stmt.Subjects,
            Predicate = stmt.Predicate,
        };

        Assert.False(EntryAttestation.ValidateEntrySchema(bad).Valid);
    }

    // Tests INV-030 [integration]: the schema rejects a WRONG subject cardinality
    // (drops the P3 subject -> 3 subjects). RED expectation: PASS (stub denies).
    [Fact]
    public void Schema_rejects_wrong_subject_cardinality()
    {
        InTotoStatement stmt = WellFormedEntryStatement();
        var bad = new InTotoStatement
        {
            Type = stmt.Type,
            PredicateType = stmt.PredicateType,
            Subjects = stmt.Subjects.Take(3).ToArray(), // 3, not 4
            Predicate = stmt.Predicate,
        };

        Assert.False(EntryAttestation.ValidateEntrySchema(bad).Valid);
    }

    // Tests INV-030 [integration]: the schema rejects a WRONG subject NAME (renames the
    // commit subject). RED expectation: PASS (stub denies).
    [Fact]
    public void Schema_rejects_wrong_subject_name()
    {
        InTotoStatement stmt = WellFormedEntryStatement();
        Subject[] subjects = stmt.Subjects.ToArray();
        subjects[0] = new Subject { Name = "not-the-entry-commit", Digest = subjects[0].Digest };

        var bad = new InTotoStatement
        {
            Type = stmt.Type,
            PredicateType = stmt.PredicateType,
            Subjects = subjects,
            Predicate = stmt.Predicate,
        };

        Assert.False(EntryAttestation.ValidateEntrySchema(bad).Valid);
    }

    // Tests INV-030 [integration]: the schema rejects a WRONG digest algorithm (a 40-hex
    // sha1-length digest where sha256/64-hex is pinned). RED expectation: PASS (stub denies).
    [Fact]
    public void Schema_rejects_wrong_digest_algorithm()
    {
        InTotoStatement stmt = WellFormedEntryStatement();
        Subject[] subjects = stmt.Subjects.ToArray();
        // 40 lowercase-hex chars (sha1 length) -- not the pinned 64-hex sha256.
        subjects[1] = new Subject
        {
            Name = subjects[1].Name,
            Digest = new DigestSet { Sha256 = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" },
        };

        var bad = new InTotoStatement
        {
            Type = stmt.Type,
            PredicateType = stmt.PredicateType,
            Subjects = subjects,
            Predicate = stmt.Predicate,
        };

        Assert.False(EntryAttestation.ValidateEntrySchema(bad).Valid);
    }

    // Tests INV-030 [integration] (RS-024 LOAD-BEARING NEGATIVE, B1-hardened): the schema
    // rejects a REFERENCE-STRING manifest -- a single-entry manifest hashing a pointer
    // string instead of the FULL multi-file closure.
    //
    // B1: the manifest here is INTERNALLY CONSISTENT -- the P2 subject digest is the VALID
    // manifest root of the (single-entry) manifest, so a least-resistance GREEN that checks
    // only `subjectDigest == ManifestRoot(manifest)` CANNOT reject it on a consistency
    // mismatch. The ONLY remaining reason to reject is the RS-024 full-closure / multi-file
    // structure requirement. A consistency-only GREEN would therefore ACCEPT this consistent
    // single-entry ref-string manifest -> this negative FAILS in that GREEN, catching exactly
    // the "builder+verifier agree on ref-string hashing" defeat.
    //
    // (Reason-specificity is intentionally NOT asserted here: on the deny-by-default stub
    // Reason == "stub-not-implemented", so a `Reason names closure` assert would make this
    // negative RED, violating the green-on-stub contract. GREEN must attribute a
    // closure/cardinality reason; that is pinned by the round-trip positive + B2(i).)
    [Fact]
    public void Schema_rejects_reference_string_manifest_not_full_closure()
    {
        InTotoStatement stmt = WellFormedEntryStatement();

        // The RS-024 violation: P2's closure collapsed to a single hash of the readiness
        // POINTER string (not the full evidence closure).
        const string pointerRef = "test/attestations/inv010-determinism.json";
        string refHash = Sha256HexUtf8(pointerRef);
        var refManifest = new[] { new ClosureDigest { Path = pointerRef, Sha256 = refHash } };

        var predicate = (EntryPredicate)stmt.Predicate!;
        var badPreconditions = predicate.Preconditions
            .Select(pc => pc.Precondition == "P2"
                ? new PreconditionClosure { Precondition = "P2", Manifest = refManifest } // single ref-string entry
                : pc)
            .ToArray();

        Subject[] subjects = stmt.Subjects.ToArray();
        int p2Index = Array.FindIndex(subjects, s => s.Name == P2SubjectName);
        // CONSISTENT: the P2 subject digest is the VALID manifest root of the single-entry
        // manifest (so only the full-closure structure rule can reject this).
        subjects[p2Index] = new Subject { Name = P2SubjectName, Digest = new DigestSet { Sha256 = ManifestRoot(refManifest) } };

        var bad = new InTotoStatement
        {
            Type = stmt.Type,
            PredicateType = stmt.PredicateType,
            Subjects = subjects,
            Predicate = new EntryPredicate { CommitX = predicate.CommitX, Preconditions = badPreconditions },
        };

        Assert.False(EntryAttestation.ValidateEntrySchema(bad).Valid);
    }

    // Tests INV-030 [integration] (RS-024 B2(ii) subject<->manifest binding): a well-formed
    // statement with a CORRECT multi-file P2 manifest but a P2 SUBJECT digest that does NOT
    // equal that manifest's root is rejected. RED expectation: PASS (stub denies).
    [Fact]
    public void Schema_rejects_subject_digest_not_matching_manifest_root()
    {
        InTotoStatement stmt = WellFormedEntryStatement();
        Subject[] subjects = stmt.Subjects.ToArray();

        // A DIFFERENT valid 64-hex digest (!= the true P2 manifest root); the P2 manifest
        // in the predicate is left CORRECT + multi-file, so the ONLY deviation is the broken
        // subject<->root binding.
        string tamperedDigest = Sha256HexUtf8("some-other-content-not-the-p2-manifest-root");
        Assert.NotEqual(ManifestRoot(ManifestOf(P2Closure())), tamperedDigest); // sanity
        Assert.Matches("^[0-9a-f]{64}$", tamperedDigest);                        // still a valid sha256 shape

        int p2Index = Array.FindIndex(subjects, s => s.Name == P2SubjectName);
        subjects[p2Index] = new Subject { Name = P2SubjectName, Digest = new DigestSet { Sha256 = tamperedDigest } };

        var bad = new InTotoStatement
        {
            Type = stmt.Type,
            PredicateType = stmt.PredicateType,
            Subjects = subjects,
            Predicate = stmt.Predicate,
        };

        Assert.False(EntryAttestation.ValidateEntrySchema(bad).Valid);
    }

    // Tests INV-030 [integration] (RS-024 A2 ordering): a SET-EQUAL but PERMUTED SUBJECT
    // order (P2 subject before P1) is rejected -- ordering is part of the canonical graph.
    // RED expectation: PASS (stub denies).
    [Fact]
    public void Schema_rejects_permuted_subject_order()
    {
        InTotoStatement stmt = WellFormedEntryStatement();
        Subject[] subjects = stmt.Subjects.ToArray();

        // Swap the P1 and P2 subjects (same SET, wrong ORDER).
        int i1 = Array.FindIndex(subjects, s => s.Name == P1SubjectName);
        int i2 = Array.FindIndex(subjects, s => s.Name == P2SubjectName);
        (subjects[i1], subjects[i2]) = (subjects[i2], subjects[i1]);

        var bad = new InTotoStatement
        {
            Type = stmt.Type,
            PredicateType = stmt.PredicateType,
            Subjects = subjects,
            Predicate = stmt.Predicate,
        };

        Assert.False(EntryAttestation.ValidateEntrySchema(bad).Valid);
    }

    // Tests INV-030 [integration] (RS-024 A2 ordering): a precondition MANIFEST whose
    // entries are NOT in canonical (path-ordinal) order is rejected. RED expectation:
    // PASS (stub denies).
    [Fact]
    public void Schema_rejects_permuted_manifest_order_within_precondition()
    {
        InTotoStatement stmt = WellFormedEntryStatement();
        var predicate = (EntryPredicate)stmt.Predicate!;

        var badPreconditions = predicate.Preconditions
            .Select(pc => pc.Precondition == "P1"
                ? new PreconditionClosure
                {
                    Precondition = "P1",
                    // Reverse the canonical (path-ordinal) order -> not canonically ordered
                    // (P1 has 3 distinct paths, so reversed != sorted).
                    Manifest = pc.Manifest.Reverse().ToArray(),
                }
                : pc)
            .ToArray();

        var bad = new InTotoStatement
        {
            Type = stmt.Type,
            PredicateType = stmt.PredicateType,
            Subjects = stmt.Subjects,
            Predicate = new EntryPredicate { CommitX = predicate.CommitX, Preconditions = badPreconditions },
        };

        Assert.False(EntryAttestation.ValidateEntrySchema(bad).Valid);
    }

    // Tests INV-030 [integration] (RS-024 A3 predicate structure): a predicate whose
    // Preconditions list has the wrong SHAPE is rejected -- (a) only two preconditions,
    // (b) a mislabelled "P4", (c) an empty Manifest for one precondition. RED expectation:
    // PASS (stub denies).
    [Fact]
    public void Schema_rejects_malformed_precondition_structure()
    {
        InTotoStatement stmt = WellFormedEntryStatement();
        var predicate = (EntryPredicate)stmt.Predicate!;

        // (a) Only two preconditions (P3 dropped).
        var twoOnly = new EntryPredicate
        {
            CommitX = predicate.CommitX,
            Preconditions = predicate.Preconditions.Where(p => p.Precondition != "P3").ToArray(),
        };
        Assert.False(EntryAttestation.ValidateEntrySchema(WithPredicate(stmt, twoOnly)).Valid);

        // (b) A mislabelled precondition id ("P4" instead of "P3").
        var mislabelled = new EntryPredicate
        {
            CommitX = predicate.CommitX,
            Preconditions = predicate.Preconditions
                .Select(p => p.Precondition == "P3"
                    ? new PreconditionClosure { Precondition = "P4", Manifest = p.Manifest }
                    : p)
                .ToArray(),
        };
        Assert.False(EntryAttestation.ValidateEntrySchema(WithPredicate(stmt, mislabelled)).Valid);

        // (c) An empty Manifest for one precondition (P2).
        var emptyManifest = new EntryPredicate
        {
            CommitX = predicate.CommitX,
            Preconditions = predicate.Preconditions
                .Select(p => p.Precondition == "P2"
                    ? new PreconditionClosure { Precondition = "P2", Manifest = Array.Empty<ClosureDigest>() }
                    : p)
                .ToArray(),
        };
        Assert.False(EntryAttestation.ValidateEntrySchema(WithPredicate(stmt, emptyManifest)).Valid);
    }

    // Tests INV-030 [integration] (QA-012): a manifest entry with a BAD SHAPE is rejected —
    // precondition-manifest-entry-shape. The manifest stays a >=2-entry full closure (so it passes the
    // not-full-closure gate) but one entry carries a non-hex sha256 or an empty path. Guards the
    // per-entry shape check that had no negative before.
    [Fact]
    public void Schema_rejects_malformed_manifest_entry_shape()
    {
        InTotoStatement stmt = WellFormedEntryStatement();
        var predicate = (EntryPredicate)stmt.Predicate!;

        // (a) a non-lowercase-hex sha256 on one entry.
        var badSha = new EntryPredicate
        {
            CommitX = predicate.CommitX,
            Preconditions = predicate.Preconditions
                .Select(p => p.Precondition == "P1"
                    ? new PreconditionClosure
                    {
                        Precondition = "P1",
                        Manifest = new[]
                        {
                            new ClosureDigest { Path = "a.txt", Sha256 = new string('a', 64) },
                            new ClosureDigest { Path = "b.txt", Sha256 = "NOT-HEX" },
                        },
                    }
                    : p)
                .ToArray(),
        };
        // Assert the SPECIFIC shape reason, not merely !Valid: the per-entry shape check (272-278) runs
        // BEFORE the subject<->manifest binding check (291-294). Without the reason assertion this test
        // would pass even with the shape check removed, because replacing P1's manifest also breaks the
        // binding (its root no longer equals subjects[1].Digest) -> the binding check would reject anyway.
        EntrySchemaResult rBadSha = EntryAttestation.ValidateEntrySchema(WithPredicate(stmt, badSha));
        Assert.False(rBadSha.Valid);
        Assert.Equal("precondition-manifest-entry-shape", rBadSha.Reason);

        // (b) an EMPTY path on one entry.
        var emptyPath = new EntryPredicate
        {
            CommitX = predicate.CommitX,
            Preconditions = predicate.Preconditions
                .Select(p => p.Precondition == "P1"
                    ? new PreconditionClosure
                    {
                        Precondition = "P1",
                        Manifest = new[]
                        {
                            new ClosureDigest { Path = "", Sha256 = new string('a', 64) },
                            new ClosureDigest { Path = "b.txt", Sha256 = new string('b', 64) },
                        },
                    }
                    : p)
                .ToArray(),
        };
        EntrySchemaResult rEmptyPath = EntryAttestation.ValidateEntrySchema(WithPredicate(stmt, emptyPath));
        Assert.False(rEmptyPath.Valid);
        Assert.Equal("precondition-manifest-entry-shape", rEmptyPath.Reason);
    }

    // =====================================================================
    // (4) Bidirectional predicate CROSS-REJECTION (RS-024), synthetic at the
    //     predicate-type layer (real cosign/cert identity verify defers to T3).
    // =====================================================================

    // Tests INV-030 [integration]: a genuine ENTRY Statement presented to the DETERMINISM
    // (P3) predicate-type gate MUST reject (wrong predicate type). RED expectation: PASS
    // (deny-by-default verifier returns false).
    [Fact]
    public void Entry_statement_is_rejected_by_determinism_predicate_type_gate()
    {
        InTotoStatement entry = WellFormedEntryStatement();
        Assert.False(
            PredicateTypeVerifier.VerifyPredicateType(entry, DeterminismAttestation.PredicateTypeUri),
            "an entry attestation must NOT verify against the determinism predicate type");
    }

    // Tests INV-030 [integration]: a genuine DETERMINISM Statement presented to the ENTRY
    // gate MUST reject -- both via the generic predicate-type verifier AND via the entry
    // schema validator (wrong predicate type). RED expectation: PASS (both deny).
    [Fact]
    public void Determinism_statement_is_rejected_by_entry_predicate_type_gate()
    {
        InTotoStatement determinism = DeterminismStatement();

        Assert.False(
            PredicateTypeVerifier.VerifyPredicateType(determinism, EntryAttestation.PredicateTypeUri),
            "a determinism attestation must NOT verify against the entry predicate type");

        Assert.False(
            EntryAttestation.ValidateEntrySchema(determinism).Valid,
            "the entry schema validator must reject a determinism-predicate statement");
    }

    // Tests INV-030 [integration]: the predicate-type verifier is not vacuously false -- a
    // Statement DOES verify against its OWN predicate type (both directions). This keeps
    // the cross-rejection cells honest (defeats an always-false verifier). RED expectation:
    // FAIL (stub returns false for everything).
    [Fact]
    public void Predicate_type_verifier_accepts_matching_type()
    {
        InTotoStatement entry = WellFormedEntryStatement();
        InTotoStatement determinism = DeterminismStatement();

        Assert.True(
            PredicateTypeVerifier.VerifyPredicateType(entry, EntryAttestation.PredicateTypeUri),
            "an entry attestation MUST verify against the entry predicate type");
        Assert.True(
            PredicateTypeVerifier.VerifyPredicateType(determinism, DeterminismAttestation.PredicateTypeUri),
            "a determinism attestation MUST verify against the determinism predicate type");
    }
}
