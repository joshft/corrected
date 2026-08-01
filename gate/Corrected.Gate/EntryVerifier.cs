using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Corrected.Gate.Kernel;
using Corrected.Provenance.Entry;
using Corrected.Provenance.InToto;

namespace Corrected.Gate;

// P3 phase-entry INV-030 (Group G / MA-C) — the gate-side ENTRY-RECEIPT VERIFIER, the missing
// producer that computes EntryIntegrity from a committed entry bundle via cosign. Mirror of
// DeterminismVerifier: it ORCHESTRATES the hardened cosign subprocess seam (CosignRunner, INV-014)
// + the entry statement parse (EntryStatementCodec) + the entry schema validator
// (EntryAttestation.ValidateEntrySchema, INV-030), maps the observation to a typed fail-closed
// result, and yields the carrier EntryIntegrity verdict at the boundary.
//
// KEY DIFFERENCE from determinism: the entry Statement is MULTI-SUBJECT (commit-X + 3 preconditions)
// and SELF-DESCRIBING — its predicate carries the FULL per-precondition closures and each subject
// binds to its manifest ROOT. So the entry INTEGRITY check rides on ValidateEntrySchema's internal
// subject<->manifest binding (a mutated predicate that keeps subjects[0] is caught there), NOT a
// separate receipt reconstruction. cosign --check-claims binds the SIGNED subjects[0] (the commit-X
// representation blob) cryptographically; Corrected ALSO re-checks sha256(receipt)==subjects[0]
// INTERNALLY (so a fake/compromised cosign that skips --check-claims cannot slip a mismatched blob),
// then validates the whole decoded statement.
//
// DEFERRED RESIDUAL (RS-006/RS-011, mirroring determinism): the entry PRODUCTION-identity ACCEPT
// path is unexercisable until P2 — only the 2a identity-mismatch NEGATIVE drives the production
// identity here; the layer-2 POSITIVE uses the FIXTURE identity (minted in MA-C part c). The deeper
// "evidence-digests-against-the-historical-snapshot-at-X" check stays a SEPARATE seam (tracks 5e/T3).

/// <summary>
/// One entry-verify request (INV-030). The <c>CosignBinPath</c> is the injected seam — tests point
/// it at a FAKE cosign; production points it at the provisioned pinned binary (INV-015/017). Ancestry
/// is supplied (an impure gate-side fact), never recomputed here.
/// </summary>
public sealed class EntryVerifyRequest
{
    /// <summary>Absolute path to the cosign executable (the COSIGN_BIN seam).</summary>
    public required string CosignBinPath { get; init; }

    /// <summary>Path to the committed Sigstore entry bundle.</summary>
    public required string BundlePath { get; init; }

    /// <summary>
    /// Path to the committed entry-receipt blob — the commit-X REPRESENTATION whose sha256 equals the
    /// signed commit subject digest (subjects[0]). This is what cosign <c>--check-claims</c> binds.
    /// </summary>
    public required string ReceiptPath { get; init; }

    /// <summary>Path to the pinned trust root.</summary>
    public required string TrustRootPath { get; init; }

    /// <summary>Fixed working directory for the cosign child.</summary>
    public required string WorkingDirectory { get; init; }

    /// <summary>
    /// The frozen verifier IDENTITY (INV-030). Defaults to the PRODUCTION entry identity; the
    /// layer-2 FIXTURE tests set it to <see cref="EntryVerifyIdentity.Fixture"/> (the production
    /// ACCEPT branch is a recorded residual — never asserted, RS-011).
    /// </summary>
    public EntryVerifyIdentity Identity { get; init; } = EntryVerifyIdentity.Production;

    /// <summary>
    /// The <c>--certificate-github-workflow-sha</c> value. When <c>null</c>, GREEN derives it from
    /// the committed entry receipt's commit-X (the production real path). The fixture-ACCEPTING 2b
    /// test sets it explicitly so cosign accepts the genuine crypto, after which Corrected's
    /// cross-check compares it to the receipt's commit-X (mismatch =&gt;
    /// <see cref="EntryVerifyReason.CertWorkflowShaMismatch"/>).
    /// </summary>
    public string? CertWorkflowSha { get; init; }

    /// <summary>
    /// The commit-X-vs-HEAD ancestry status. Defaults to the SAFE direction
    /// <see cref="AncestryStatus.Uncomputable"/> (fail-closed): a caller that omits this input is
    /// rejected, never accepted (QA-001). The layer-2 fixture positive sets <c>Ancestor</c> explicitly.
    /// </summary>
    public AncestryStatus CommitAncestry { get; init; } = AncestryStatus.Uncomputable;

    /// <summary>Bounded process timeout for the cosign verify subprocess.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);
}

/// <summary>
/// The typed entry-verify result (INV-030). <see cref="Satisfied"/> is true ONLY when
/// <see cref="Integrity"/> is <see cref="EntryIntegrity.Verified"/> — a rejected / unavailable /
/// absent / any-internal-exception path is never satisfied. On a non-verified outcome the
/// <see cref="Reason"/> carries the specific typed reason; on verified it is <c>null</c>.
/// </summary>
public sealed record EntryVerifyResult(EntryIntegrity Integrity, EntryVerifyReason? Reason)
{
    /// <summary>True iff the integrity verdict is <see cref="EntryIntegrity.Verified"/>.</summary>
    public bool Satisfied => Integrity == EntryIntegrity.Verified;
}

/// <summary>
/// The gate-side entry-receipt verifier (INV-030). See the file header.
/// </summary>
public static class EntryVerifier
{
    /// <summary>
    /// Run the full entry-verify orchestration (INV-030): validate inputs, invoke the pinned cosign
    /// verify seam under the frozen entry argv, decode + parse the signed entry Statement, re-bind the
    /// commit subject, validate the entry schema, cross-check the commit-X cert-SHA + ancestry, and
    /// return a typed fail-closed result. Any internal exception yields a non-verified result — NEVER
    /// <see cref="EntryIntegrity.Verified"/>, never a throw.
    /// </summary>
    public static EntryVerifyResult Verify(EntryVerifyRequest request)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(request);

            // ---- pre-cosign structural checks (fail-closed, before any subprocess) ----

            // Read the bundle AND the commit-X receipt blob ONCE, each BOUNDED by the size cap
            // (INV-014 / AP-007 / MA-E), BEFORE cosign — so a symlink / oversize / length-0 special
            // file (e.g. /dev/zero) is rejected here, never read unbounded into an OOM, and the bytes
            // Corrected classifies cannot diverge from a later re-read (TOCTOU).
            byte[]? bundleBytes = CosignRunner.ReadRegularFileWithinCap(request.BundlePath, out _);
            if (bundleBytes is null)
            {
                // A genuinely-absent bundle is the pre-entry zero-state (-> Absent; the src/ ban stays
                // active, which is EXPECTED pre-entry). A present-but-NON-REGULAR path (a directory /
                // dangling symlink / special file AT the bundle path) is a tamper, NOT the benign
                // zero-state (MA-C self-audit #2) -> Rejected/MalformedBundle. Distinguish by whether
                // ANY filesystem entry exists there.
                return NothingExistsAt(request.BundlePath)
                    ? Classified(EntryVerifyReason.EvidenceAbsent)
                    : Reject(EntryVerifyReason.MalformedBundle);
            }
            byte[]? receiptBytes = CosignRunner.ReadRegularFileWithinCap(request.ReceiptPath, out _);
            if (receiptBytes is null)
            {
                return Reject(EntryVerifyReason.MalformedReceipt);
            }

            // The bundle must parse as JSON; an unparseable bundle -> malformed-bundle (pre-cosign).
            try
            {
                using JsonDocument bundleDoc = JsonDocument.Parse(bundleBytes);
            }
            catch (Exception)
            {
                return Reject(EntryVerifyReason.MalformedBundle);
            }

            // A PRESENT-but-UNREADABLE pinned trust root or binary is a transient I/O fault (EA-009)
            // -> Unavailable. A MISSING binary is NOT caught here — it falls through to cosign
            // LaunchFailed -> verifier-unavailable (EA-008).
            if (FilePresentButUnreadable(request.TrustRootPath) || FilePresentButUnreadable(request.CosignBinPath))
            {
                return Classified(EntryVerifyReason.TrustRootOrToolUnreadable);
            }

            // ---- run the pinned cosign verify seam (CosignRunner, INV-014) out-of-process ----
            CosignRunResult run = CosignRunner.Run(new CosignRunOptions
            {
                ExecutablePath = request.CosignBinPath,
                Argv = BuildVerifyArgv(request, CommitXFromReceipt(receiptBytes)),
                WorkingDirectory = request.WorkingDirectory,
                FileInputs = new[] { request.BundlePath, request.ReceiptPath, request.TrustRootPath },
                Timeout = request.Timeout,
            });

            return run.Outcome switch
            {
                // A missing / unexecutable cosign binary is a transient (unavailable) fault (EA-008).
                CosignOutcome.LaunchFailed => Classified(EntryVerifyReason.VerifierUnavailable),

                // A bare timeout / oversize spew / pre-launch input rejection the taxonomy does not
                // positively classify maps to the pinned default -> rejected (fail-closed, never
                // unavailable/absent).
                CosignOutcome.Timeout => Reject(EntryVerifyReason.UnclassifiedVerifierFault),
                CosignOutcome.OversizeOutput => Reject(EntryVerifyReason.UnclassifiedVerifierFault),
                CosignOutcome.InputRejected => Reject(EntryVerifyReason.UnclassifiedVerifierFault),

                // A genuine cosign non-zero exit is classified from its output into the SPECIFIC
                // crypto/structure reason; an output the taxonomy does not match falls to the pinned
                // default (fail-closed).
                CosignOutcome.NonZeroExit => Reject(ClassifyCosignFailure(run.StdErr + "\n" + run.StdOut)),

                // On cosign Ok: decode the signed entry Statement, re-bind the commit subject, run the
                // entry schema validator, cross-check cert-SHA + ancestry -> Verified only if all hold.
                CosignOutcome.Ok => VerifyCosignOk(request, receiptBytes, bundleBytes),

                _ => Reject(EntryVerifyReason.UnclassifiedVerifierFault),
            };
        }
        catch (Exception)
        {
            // Any internal error -> a non-verified result. NEVER Verified, never Unavailable/Absent.
            return Reject(EntryVerifyReason.UnclassifiedVerifierFault);
        }
    }

    /// <summary>
    /// The cosign-Ok branch (INV-030): the crypto verified, so Corrected applies the semantic checks
    /// cosign does NOT — decode the signed DSSE payload, PARSE the multi-subject entry Statement,
    /// re-bind the commit subject to the receipt blob (so a fake/compromised cosign that skips
    /// --check-claims cannot slip a mismatched blob), validate the entry schema (subject&lt;-&gt;manifest
    /// binding — the entry INV-010 analog), cross-check the cert workflow-SHA to the receipt commit-X,
    /// and require an ancestor commit. Returns <see cref="EntryIntegrity.Verified"/> ONLY when every
    /// check holds; any failure is a SPECIFIC typed reject (fail-closed).
    /// </summary>
    private static EntryVerifyResult VerifyCosignOk(EntryVerifyRequest request, byte[] receiptBytes, byte[] bundleBytes)
    {
        // Decode the SIGNED DSSE payload (the full 4-subject entry Statement). A payload that cannot
        // be decoded on the Ok path is fail-closed to schema-invalid.
        byte[] decodedPayload;
        try
        {
            using JsonDocument bundleDoc = JsonDocument.Parse(bundleBytes);
            string payloadB64 = bundleDoc.RootElement
                .GetProperty("dsseEnvelope").GetProperty("payload").GetString()
                ?? throw new InvalidOperationException("bundle dsseEnvelope.payload is absent");
            decodedPayload = Convert.FromBase64String(payloadB64);
        }
        catch (Exception)
        {
            return Reject(EntryVerifyReason.EntrySchemaInvalid);
        }

        // Parse the decoded payload into the typed entry graph. A parse failure -> schema-invalid.
        (InTotoStatement? statement, string? _) = EntryStatementCodec.ParseEntryStatement(decodedPayload);
        if (statement is null)
        {
            return Reject(EntryVerifyReason.EntrySchemaInvalid);
        }

        // INTERNAL subject-digest re-bind (the fake/compromised-cosign guard): the commit subject
        // (subjects[0]) MUST be sha256 over the exact receipt blob bytes. cosign --check-claims binds
        // this cryptographically in production, but a fake-Ok cosign does NOT — so Corrected re-checks
        // it here. A statement with no subjects, or a commit subject != sha256(receipt), is rejected.
        IReadOnlyList<Subject> subjects = statement.Subjects;
        if (subjects.Count < 1 || subjects[0].Digest is null ||
            !string.Equals(Sha256Hex(receiptBytes), subjects[0].Digest.Sha256, StringComparison.Ordinal))
        {
            return Reject(EntryVerifyReason.SubjectDigestMismatch);
        }

        // Entry schema (INV-030): exact subjects, canonical order, the entry predicate type (a
        // determinism-typed statement cross-rejects here), and — the entry INV-010 analog — every
        // precondition SUBJECT digest equals the manifest ROOT of its FULL closure. A mutated
        // predicate that keeps subjects[0] is caught by this internal binding.
        EntrySchemaResult schema = EntryAttestation.ValidateEntrySchema(statement);
        if (!schema.Valid)
        {
            return Reject(EntryVerifyReason.EntrySchemaInvalid);
        }

        // INV-011 analog: the cert workflow-SHA must equal the entry receipt's commit-X. On the
        // production real path the request supplies no explicit value, so it is derived from the
        // receipt and the compare is redundant-by-construction (the real binding is the frozen argv
        // flag cosign enforces). The compare does REAL work on the fixture 2b path, where an explicit
        // request.CertWorkflowSha may differ from the receipt commit-X -> reject.
        string commitX = CommitXFromReceipt(receiptBytes);
        string certWorkflowSha = request.CertWorkflowSha ?? commitX;
        if (!string.Equals(certWorkflowSha, commitX, StringComparison.Ordinal))
        {
            return Reject(EntryVerifyReason.CertWorkflowShaMismatch);
        }

        // Ancestry (supplied, fail-closed by default): the entry commit X must be an ancestor of HEAD.
        // Uncomputable (a shallow clone / absent commit, incl. the omitted-input default) is REJECTED,
        // never unavailable (RS-013).
        switch (request.CommitAncestry)
        {
            case AncestryStatus.NotAncestor:
                return Reject(EntryVerifyReason.AttestedCommitNotAncestor);
            case AncestryStatus.Uncomputable:
                return Reject(EntryVerifyReason.AncestryUncomputable);
            case AncestryStatus.Ancestor:
                break;
            default:
                return Reject(EntryVerifyReason.AncestryUncomputable);
        }

        // Crypto verified AND the commit subject re-binds AND the entry schema validates AND the
        // cert-SHA binds to commit-X AND the commit is an ancestor — the single Verified path.
        return new EntryVerifyResult(EntryIntegrity.Verified, null);
    }

    /// <summary>
    /// Classify a genuine cosign non-zero exit from its captured output into the SPECIFIC typed
    /// reason (INV-030). Case-insensitive substring match on the pinned cosign v3.1.2 error phrases,
    /// ordered most-specific-first: the subject-digest error carries BOTH "artifact digests" AND
    /// "verify signature", so the subject-digest branch is tested BEFORE the signature branch. An
    /// output matching no known phrase falls to the pinned default (fail-closed) — never a pass.
    /// </summary>
    internal static EntryVerifyReason ClassifyCosignFailure(string output)
    {
        string s = (output ?? string.Empty).ToLowerInvariant();

        if (s.Contains("certificate identity"))
        {
            return EntryVerifyReason.IdentityMismatch;
        }

        if (s.Contains("invalid predicate type"))
        {
            return EntryVerifyReason.PredicateTypeMismatch;
        }

        // Checked BEFORE the signature branch (this same message also contains "verify signature").
        if (s.Contains("artifact digests"))
        {
            return EntryVerifyReason.SubjectDigestMismatch;
        }

        if (s.Contains("new-bundle-format") || s.Contains("bundle format"))
        {
            return EntryVerifyReason.MalformedBundle;
        }

        if (s.Contains("loading trusted root") || s.Contains("trusted material") || s.Contains("trustedroot"))
        {
            return EntryVerifyReason.TrustRootOrPinMismatch;
        }

        if (s.Contains("payload hash") || s.Contains("verify signature") || s.Contains("signature"))
        {
            return EntryVerifyReason.SignatureInvalid;
        }

        // Pinned DEFAULT: an unrecognized cosign fault is rejected (fail-closed), never a pass.
        return EntryVerifyReason.UnclassifiedVerifierFault;
    }

    /// <summary>
    /// Build the cosign verify argv for the entry attestation (INV-030 / PRH-001 analog). Freezes the
    /// EXACT <c>verify-blob-attestation</c> argv drawing the identity from
    /// <see cref="EntryVerifyRequest.Identity"/> and the SHA from
    /// <see cref="EntryVerifyRequest.CertWorkflowSha"/> (or, when null, the receipt commit-X). The
    /// argv is EXACT and never a <c>-regexp</c>/<c>--insecure-*</c> variant and never
    /// <c>--check-claims=false</c>: claims-checking is ON, identity + issuer are pinned literals, the
    /// entry predicate type is named, and offline anchoring is <c>--use-signed-timestamps</c> + pinned
    /// <c>--trusted-root</c> (no verify-time network / Rekor content search).
    /// </summary>
    public static IReadOnlyList<string> BuildVerifyArgv(EntryVerifyRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        string certWorkflowSha = request.CertWorkflowSha ?? DeriveCommitXFromReceipt(request.ReceiptPath);
        return BuildVerifyArgvWithSha(request, certWorkflowSha);
    }

    /// <summary>
    /// In-<see cref="Verify"/> overload: reuses the ALREADY-READ commit-X for the workflow-SHA, so the
    /// receipt file is NOT re-read on the verify path. Identical argv, one fewer disk read.
    /// </summary>
    internal static IReadOnlyList<string> BuildVerifyArgv(EntryVerifyRequest request, string commitXFromReceipt)
    {
        ArgumentNullException.ThrowIfNull(request);
        return BuildVerifyArgvWithSha(request, request.CertWorkflowSha ?? commitXFromReceipt);
    }

    private static IReadOnlyList<string> BuildVerifyArgvWithSha(EntryVerifyRequest request, string certWorkflowSha)
        => new[]
        {
            "verify-blob-attestation",
            "--check-claims=true",
            "--type", request.Identity.PredicateType,
            "--certificate-identity", request.Identity.CertificateIdentity,
            "--certificate-oidc-issuer", request.Identity.OidcIssuer,
            "--certificate-github-workflow-sha", certWorkflowSha,
            "--use-signed-timestamps",
            "--trusted-root", request.TrustRootPath,
            "--bundle", request.BundlePath,
            request.ReceiptPath,
        };

    /// <summary>
    /// The <c>EntryIntegrity</c> verdict a result carries (INV-030 boundary): identical to
    /// <see cref="EntryVerifyResult.Integrity"/>, exposed as a named accessor mirroring
    /// <c>DeterminismVerifier.ToProbeResult</c>. Verified ONLY on the null-reason accept path.
    /// </summary>
    public static EntryIntegrity ToEntryIntegrity(EntryVerifyResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result.Integrity;
    }

    /// <summary>The entry receipt's commit-X value: the exact UTF-8 decoding of the receipt blob bytes.</summary>
    private static string CommitXFromReceipt(byte[] receiptBytes) => Encoding.UTF8.GetString(receiptBytes);

    /// <summary>
    /// Derive the commit-X from the committed receipt blob (the production real path, when the request
    /// supplies no explicit cert-SHA). The receipt is read through the BOUNDED regular-file reader
    /// (MA-E) so an oversize / special-file receipt cannot OOM here either. An unreadable receipt
    /// yields the empty string — the argv still forms and cosign fails closed on the empty constraint.
    /// </summary>
    private static string DeriveCommitXFromReceipt(string receiptPath)
    {
        try
        {
            byte[]? bytes = CosignRunner.ReadRegularFileWithinCap(receiptPath, out _);
            return bytes is null ? string.Empty : CommitXFromReceipt(bytes);
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    /// <summary>Lowercase-hex sha256 over the exact bytes (the commit-subject binding digest).</summary>
    private static string Sha256Hex(byte[] bytes)
        => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    /// <summary>
    /// Is there NO filesystem entry at all at <paramref name="path"/> (a genuinely-absent bundle,
    /// the pre-entry zero-state)? A regular file, a directory, OR a dangling symlink all count as
    /// PRESENT (returns false — a present-but-non-regular path is a tamper, not the benign zero-state,
    /// MA-C self-audit #2). An empty path is NOT "absent" (it is malformed). Fails closed: any probe
    /// error returns false, so the caller falls to the Rejected path rather than claiming absent.
    /// </summary>
    private static bool NothingExistsAt(string path)
    {
        try
        {
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }
            if (File.Exists(path) || Directory.Exists(path))
            {
                return false;
            }
            // A dangling symlink has no File/Directory target, but the link entry itself IS present.
            return new FileInfo(path).LinkTarget is null;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Is the file PRESENT but UNREADABLE by this process (the EA-009 I/O fault)? A missing file is
    /// NOT unreadable here (returns false — a missing binary is a launch fault). Opens for a single-
    /// byte read so a large binary is never fully read. Never throws.
    /// </summary>
    private static bool FilePresentButUnreadable(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return false;
            }
            using FileStream fs = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            byte[] one = new byte[1];
            _ = fs.Read(one, 0, 1);
            return false;
        }
        catch (Exception)
        {
            return true;
        }
    }

    // Every non-verified result's Absent/Rejected/Unavailable verdict is derived from the committed
    // INV-030 severity map (EntryVerifyReasonMap) — the single production source of truth, so
    // re-pointing a reason's [EntrySeverity] annotation changes Verify's verdict and the negatives
    // catch it. Reject is retained as the call-site name for the (rejected-severity) reasons.
    private static EntryVerifyResult Reject(EntryVerifyReason reason) => Classified(reason);

    // Build the typed result whose verdict is the map's severity for the reason (Absent for the
    // zero-state set, Unavailable for the closed transient set, Rejected for everything else + the
    // default). Never Verified — that is the null-reason accept path in Verify.
    private static EntryVerifyResult Classified(EntryVerifyReason reason)
        => new(EntryVerifyReasonMap.ToIntegrity(EntryVerifyReasonMap.Classify(reason)), reason);
}
