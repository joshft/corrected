// INV-022 (generic, reusable identity-verify contract). The predicate-type verification
// helper the entry + determinism gates share for the INV-030 / RS-024 BIDIRECTIONAL
// cross-rejection: because the entry receipt MAY share the P3 signer identity,
// distinctness cannot rest on identity alone — a genuine attestation of ONE predicate
// type presented to the OTHER's gate must NOT verify. This is the P3-agnostic contract
// (like Statement/Subject/DSSE); the entry predicate/schema stay independently typed.
using System;

namespace Corrected.Provenance.InToto;

/// <summary>
/// Generic predicate-type verification (INV-022). The synthetic predicate-type layer of
/// the cross-rejection contract (the real cosign/cert-identity verify defers to a later
/// track): does a Statement carry EXACTLY the expected predicate-type URI?
/// </summary>
public static class PredicateTypeVerifier
{
    /// <summary>
    /// True iff <paramref name="statement"/> carries EXACTLY the
    /// <paramref name="expectedPredicateTypeUri"/> (ordinal, case-sensitive, both
    /// non-empty). Used bidirectionally: an entry Statement verified against the
    /// determinism URI (and vice-versa) MUST return false.
    /// </summary>
    public static bool VerifyPredicateType(InTotoStatement statement, string expectedPredicateTypeUri)
    {
        // Deny-by-default on any missing/empty input (fail closed): a null statement, an
        // empty carried predicate type, or an empty expected URI can NEVER verify.
        if (statement is null)
        {
            return false;
        }

        if (string.IsNullOrEmpty(statement.PredicateType) || string.IsNullOrEmpty(expectedPredicateTypeUri))
        {
            return false;
        }

        // EXACT ordinal (case-sensitive) match — the sole distinctness gate for the
        // bidirectional cross-rejection.
        return string.Equals(statement.PredicateType, expectedPredicateTypeUri, StringComparison.Ordinal);
    }
}
