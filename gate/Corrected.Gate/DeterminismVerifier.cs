using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Corrected.Gate.Kernel;
using Corrected.Provenance.Determinism;

namespace Corrected.Gate;

// P3 determinism-attestation spec INV-010/012/013 LAYER 3: the verifier ORCHESTRATES the hardened
// cosign subprocess seam (CosignRunner — Corrected.Gate, INV-014, ALREADY BUILT) + the Corrected
// Statement reconstruction (Corrected.Provenance DeterminismAttestation.SerializeStatementJson).
// It lives in Corrected.Gate. GREEN adds the Corrected.Gate -> Corrected.Provenance ProjectReference
// (INV-022-legal: the referrer is gate/-prefixed) when it wires the real reconstruction internally.
//
// This RED stub keeps the PUBLIC surface Corrected.Gate + Kernel only (file paths + primitives in,
// a Gate-local typed result out) so the tests COMPILE and FAIL AS ASSERTIONS with NO ProjectReference
// yet. The real-cosign LAYER 2 (positive verify + decoded-payload byte-equality + cert-SHA cross-check
// against a REAL committed bundle) is T3b — OUT OF SCOPE here; this interface is DEFINED so those slot in.

/// <summary>
/// The tri-state P3 verify outcome (INV-012): <c>{verified | rejected | unavailable}</c>. Mirrors
/// the carrier <c>ProbeResult</c> tri-state; <c>Satisfied</c> is derived true ONLY on
/// <see cref="Verified"/>.
/// </summary>
public enum DeterminismVerifyOutcome
{
    Verified,
    Rejected,
    Unavailable,
}

/// <summary>
/// One P3 verify request (INV-010/013 layer 3). The <c>CosignBinPath</c> is the injected seam —
/// tests point it at a FAKE cosign; production points it at the provisioned pinned binary
/// (INV-015/017). Ancestry is supplied (an impure gate-side fact), never recomputed here.
/// </summary>
public sealed class DeterminismVerifyRequest
{
    /// <summary>Absolute path to the cosign executable (the COSIGN_BIN seam).</summary>
    public required string CosignBinPath { get; init; }

    /// <summary>Path to the committed Sigstore bundle.</summary>
    public required string BundlePath { get; init; }

    /// <summary>Path to the committed determinism run receipt (the signed subject bytes).</summary>
    public required string ReceiptPath { get; init; }

    /// <summary>Path to the pinned trust root.</summary>
    public required string TrustRootPath { get; init; }

    /// <summary>Fixed working directory for the cosign child.</summary>
    public required string WorkingDirectory { get; init; }

    /// <summary>The expected platform RID (linux-x64 today, EA-003).</summary>
    public required string ExpectedRid { get; init; }

    /// <summary>
    /// The frozen verifier IDENTITY (INV-010/011/013, section C). Selects the exact
    /// cert-identity / issuer / predicate-type / subject-name the frozen argv pins. Defaults to
    /// the PRODUCTION identity; the layer-2 FIXTURE tests set it to
    /// <see cref="DeterminismVerifyIdentity.Fixture"/> (the production ACCEPT branch is a recorded
    /// PR3 residual — never asserted). T3b structural contract; GREEN reads it in the argv builder.
    /// </summary>
    public DeterminismVerifyIdentity Identity { get; init; } = DeterminismVerifyIdentity.Production;

    /// <summary>
    /// The <c>--certificate-github-workflow-sha</c> value (INV-011). When <c>null</c>, GREEN
    /// derives it from the committed receipt's <c>attested_commit</c> (the production real path).
    /// The fixture-ACCEPTING 2b test sets it to
    /// <see cref="DeterminismVerifyIdentity.FixtureCertWorkflowSha"/> so cosign accepts the genuine
    /// crypto, after which Corrected's INV-011 cross-check compares it to the receipt's
    /// <c>attested_commit</c> (SHANEG: <c>14701a9 != 0000…</c> ⇒
    /// <see cref="DeterminismVerifyReason.CertWorkflowShaMismatch"/>).
    /// </summary>
    public string? CertWorkflowSha { get; init; }

    /// <summary>The <c>attested_commit</c>-vs-HEAD ancestry status (INV-012/019).</summary>
    public AncestryStatus AttestedCommitAncestry { get; init; } = AncestryStatus.Ancestor;

    /// <summary>
    /// The subject-manifest staleness input (INV-018/019), supplied as a typed gate-side fact —
    /// NOT recomputed here against a moving HEAD. A3 resolution (deliberate): the committed fixture
    /// verifies its manifest against its OWN frozen manifest context (the receipt's
    /// <c>subject_manifest_digest</c> is frozen at commit <c>14701a9</c>), so a moving HEAD does not
    /// block the fixture positive; a real HEAD-relative staleness gate supplies <c>true</c> here via
    /// the probe orchestrator (INV-018). Staleness stays ENFORCEABLE (a caller passing <c>true</c>
    /// gets <see cref="DeterminismVerifyReason.StaleSubjectManifest"/>) — it is never silently
    /// disabled. Defaults non-stale for the fixture verification the layer-2 tests drive.
    /// </summary>
    public bool ManifestStale { get; init; }

    /// <summary>Bounded process timeout for the cosign verify subprocess.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);
}

/// <summary>
/// The typed P3 verify result (INV-012). <see cref="Satisfied"/> is true ONLY when
/// <see cref="Outcome"/> is <see cref="DeterminismVerifyOutcome.Verified"/> — a rejected /
/// unavailable / any-internal-exception path is never satisfied. On a non-verified outcome the
/// <see cref="Reason"/> carries the specific typed reason; on verified it is <c>null</c>.
/// </summary>
public sealed record DeterminismVerifyResult(
    DeterminismVerifyOutcome Outcome,
    DeterminismVerifyReason? Reason)
{
    /// <summary>True iff the outcome is <see cref="DeterminismVerifyOutcome.Verified"/>.</summary>
    public bool Satisfied => Outcome == DeterminismVerifyOutcome.Verified;
}

/// <summary>
/// The P3 determinism verifier (INV-010/012/013). Orchestrates <see cref="CosignRunner"/> + the
/// Corrected Statement reconstruction, maps the observation to a typed fail-closed result, and
/// bridges that result to the carrier <see cref="ProbeResult"/> at the boundary with a TOTAL
/// internal-reason -> carrier-token map (no free string — RS-010).
///
/// RED-phase structural stub — every method body carries <c>STUB:TDD</c> and returns a
/// deny-by-default / safe-wrong value so the positive cells fail as ASSERTIONS while the
/// fail-closed cells pass. GREEN implements the real orchestration + reconstruction + boundary map.
/// </summary>
public static class DeterminismVerifier
{
    /// <summary>The carrier token a VERIFIED outcome derives (INV-012 — ran-passed is probe-derived).</summary>
    private const string VerifiedToken = "ran-passed";

    /// <summary>
    /// The COMMITTED total internal-reason -> carrier-token map (INV-012 / RS-010). Every
    /// <see cref="DeterminismVerifyReason"/> has exactly one non-empty kebab token here; the closed
    /// <see cref="CarrierProbeReasonTokens"/> set is DERIVED from these values, so a reason added
    /// without a token is a compile-time-visible gap the totality test catches. No free-string /
    /// raw-stderr fallthrough — the DEFAULT for an out-of-range value is the fail-closed
    /// unclassified-verifier-fault token.
    /// </summary>
    private static readonly IReadOnlyDictionary<DeterminismVerifyReason, string> ReasonTokens =
        new Dictionary<DeterminismVerifyReason, string>
        {
            [DeterminismVerifyReason.VerifierUnavailable] = "verifier-unavailable",
            [DeterminismVerifyReason.TrustRootOrToolUnreadable] = "trust-root-or-tool-unreadable",
            [DeterminismVerifyReason.EvidenceAbsent] = "evidence-absent",
            [DeterminismVerifyReason.P3NotYetActivated] = "p3-not-yet-activated",
            [DeterminismVerifyReason.MalformedReceipt] = "malformed-receipt",
            [DeterminismVerifyReason.MalformedBundle] = "malformed-bundle",
            [DeterminismVerifyReason.SignatureInvalid] = "signature-invalid",
            [DeterminismVerifyReason.IdentityMismatch] = "identity-mismatch",
            [DeterminismVerifyReason.PredicateTypeMismatch] = "predicate-type-mismatch",
            [DeterminismVerifyReason.SubjectDigestMismatch] = "subject-digest-mismatch",
            // T3b structural contract: the INV-010 decoded-payload != reconstruction reason (B2).
            [DeterminismVerifyReason.StatementReconstructionMismatch] = "statement-reconstruction-mismatch",
            [DeterminismVerifyReason.ProjectionPolicyMismatch] = "projection-policy-mismatch",
            [DeterminismVerifyReason.StaleSubjectManifest] = "stale-subject-manifest",
            [DeterminismVerifyReason.AttestedCommitNotAncestor] = "attested-commit-not-ancestor",
            // T3b structural contract: the INV-011 cert-SHA cross-check reason, distinct from
            // identity-mismatch (the 2b negative). GREEN produces it from Verify's cosign-Ok branch.
            [DeterminismVerifyReason.CertWorkflowShaMismatch] = "cert-workflow-sha-mismatch",
            [DeterminismVerifyReason.AncestryUncomputable] = "ancestry-uncomputable",
            [DeterminismVerifyReason.RidPlatformMismatch] = "rid-platform-mismatch",
            [DeterminismVerifyReason.NonPassOutcome] = "non-pass-outcome",
            [DeterminismVerifyReason.TrustRootOrPinMismatch] = "trust-root-or-pin-mismatch",
            [DeterminismVerifyReason.UnclassifiedVerifierFault] = "unclassified-verifier-fault",
        };

    /// <summary>
    /// The CLOSED committed set of carrier <c>ProbeReasons</c> tokens the boundary map
    /// (<see cref="ToCarrierProbeReason"/>) may emit (INV-012 / RS-010). Derived from the total
    /// <see cref="ReasonTokens"/> map so the two stay in lockstep — a totality test asserts every
    /// <see cref="DeterminismVerifyReason"/> maps into this set (no free-string fallthrough).
    /// </summary>
    public static IReadOnlyCollection<string> CarrierProbeReasonTokens { get; } =
        ReasonTokens.Values.ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// Run the full P3 verify orchestration (INV-010/013 layer 3): validate inputs, invoke the
    /// pinned cosign verify seam, reconstruct + byte-compare the Statement (T3b), apply the
    /// layer-1 claim policy, and return a typed fail-closed result. Any internal exception yields a
    /// non-verified result — NEVER <see cref="DeterminismVerifyOutcome.Verified"/>, never a throw.
    /// </summary>
    public static DeterminismVerifyResult Verify(DeterminismVerifyRequest request)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(request);

            // ---- pre-cosign structural checks (fail-closed, before any subprocess) ----

            // The bundle evidence must be present; an absent bundle is a hard reject.
            if (!File.Exists(request.BundlePath))
            {
                return Reject(DeterminismVerifyReason.EvidenceAbsent);
            }

            // The committed receipt (the signed subject bytes) must parse. An unparseable receipt is
            // not a valid signed subject -> malformed-receipt. The parsed bytes + DTO are retained
            // for the cosign-Ok byte-equality reconstruction and the INV-011 cert-SHA cross-check.
            byte[] receiptBytes;
            RunReceipt receipt;
            try
            {
                receiptBytes = File.ReadAllBytes(request.ReceiptPath);
                receipt = RunReceipt.FromJson(receiptBytes);
            }
            catch (Exception)
            {
                return Reject(DeterminismVerifyReason.MalformedReceipt);
            }

            // The bundle must parse as JSON; an unparseable bundle -> malformed-bundle.
            try
            {
                using JsonDocument bundleDoc = JsonDocument.Parse(File.ReadAllBytes(request.BundlePath));
            }
            catch (Exception)
            {
                return Reject(DeterminismVerifyReason.MalformedBundle);
            }

            // ---- pre-cosign I/O-fault check (EA-009): a PRESENT-but-UNREADABLE pinned trust root
            //      or binary is a transient I/O fault -> Unavailable/trust-root-or-tool-unreadable.
            //      A MISSING binary is NOT caught here — it falls through to cosign LaunchFailed ->
            //      verifier-unavailable (EA-008). A readable-but-WRONG root is NOT caught here — it
            //      reaches cosign, which fails to load it (-> trust-root-or-pin-mismatch, below). ----
            if (FilePresentButUnreadable(request.TrustRootPath) || FilePresentButUnreadable(request.CosignBinPath))
            {
                return new DeterminismVerifyResult(
                    DeterminismVerifyOutcome.Unavailable, DeterminismVerifyReason.TrustRootOrToolUnreadable);
            }

            // ---- run the pinned cosign verify seam (CosignRunner, INV-014) out-of-process ----
            CosignRunResult run = CosignRunner.Run(new CosignRunOptions
            {
                ExecutablePath = request.CosignBinPath,
                Argv = BuildVerifyArgv(request),
                WorkingDirectory = request.WorkingDirectory,
                FileInputs = new[] { request.BundlePath, request.ReceiptPath, request.TrustRootPath },
                Timeout = request.Timeout,
            });

            return run.Outcome switch
            {
                // A missing / unexecutable cosign binary is a transient (unavailable) cell (EA-008).
                CosignOutcome.LaunchFailed => new DeterminismVerifyResult(
                    DeterminismVerifyOutcome.Unavailable, DeterminismVerifyReason.VerifierUnavailable),

                // A bare timeout / oversize spew / pre-launch input rejection the taxonomy does not
                // positively classify maps to the pinned default unclassified-verifier-fault —
                // NEVER unavailable (RS-002).
                CosignOutcome.Timeout => Reject(DeterminismVerifyReason.UnclassifiedVerifierFault),
                CosignOutcome.OversizeOutput => Reject(DeterminismVerifyReason.UnclassifiedVerifierFault),
                CosignOutcome.InputRejected => Reject(DeterminismVerifyReason.UnclassifiedVerifierFault),

                // A genuine cosign non-zero exit is classified from its output into the SPECIFIC
                // crypto/structure reason (INV-013 layer 2); an output the taxonomy does not match
                // falls to the pinned default unclassified-verifier-fault (fail-closed).
                CosignOutcome.NonZeroExit => Reject(ClassifyCosignFailure(run.StdErr + "\n" + run.StdOut)),

                // On cosign Ok: base64-decode .dsseEnvelope.payload, require it byte-equal the
                // Corrected-reconstructed Statement [INV-010], cross-check cert-SHA==attested_commit
                // [INV-011], then apply the layer-1 claim policy -> Verified only if all hold.
                CosignOutcome.Ok => VerifyCosignOk(request, receiptBytes, receipt),

                _ => Reject(DeterminismVerifyReason.UnclassifiedVerifierFault),
            };
        }
        catch (Exception)
        {
            // Any internal error -> a non-verified result. NEVER Verified, never Unavailable.
            return Reject(DeterminismVerifyReason.UnclassifiedVerifierFault);
        }
    }

    /// <summary>
    /// Classify a genuine cosign non-zero exit from its captured output into the SPECIFIC typed
    /// reason (INV-013 layer 2 negatives). Case-insensitive substring match on the pinned cosign
    /// v3.1.2 error phrases, ordered most-specific-first: the mutated-blob error carries BOTH
    /// "artifact digests" AND "verify signature", so the subject-digest branch is tested BEFORE the
    /// signature branch (an ordering bug would mislabel a subject-digest reject as signature-invalid).
    /// An output that matches no known phrase falls to the pinned default unclassified-verifier-fault
    /// (fail-closed, RS-002) — never a pass, never unavailable.
    /// </summary>
    internal static DeterminismVerifyReason ClassifyCosignFailure(string output)
    {
        string s = (output ?? string.Empty).ToLowerInvariant();

        // "failed to verify certificate identity: no matching CertificateIdentity found ...".
        if (s.Contains("certificate identity"))
        {
            return DeterminismVerifyReason.IdentityMismatch;
        }

        // "invalid predicate type, expected ... got ...".
        if (s.Contains("invalid predicate type"))
        {
            return DeterminismVerifyReason.PredicateTypeMismatch;
        }

        // "failed to verify signature: provided artifact digests do not match digests in statement"
        // — the receipt blob's sha256 does not match the signed subject digest. Checked BEFORE the
        // signature branch (this same message also contains "verify signature").
        if (s.Contains("artifact digests"))
        {
            return DeterminismVerifyReason.SubjectDigestMismatch;
        }

        // "--trusted-root only supported with --new-bundle-format" — a JSON-valid non-bundle input
        // reaches cosign and fails the bundle-format check.
        if (s.Contains("new-bundle-format") || s.Contains("bundle format"))
        {
            return DeterminismVerifyReason.MalformedBundle;
        }

        // "setting trusted material: loading trusted root: unsupported TrustedRoot media type ..."
        // — a readable-but-WRONG/insufficient trust root the offline verify cannot anchor to. A
        // digest/content mismatch of the pinned root, distinct from the unreadable I/O fault above.
        if (s.Contains("loading trusted root") || s.Contains("trusted material") || s.Contains("trustedroot"))
        {
            return DeterminismVerifyReason.TrustRootOrPinMismatch;
        }

        // "failed to verify log inclusion: ... payload hash ... does not match envelope payload
        // hash ..." (tampered DSSE payload) and any other genuine signature-verification failure.
        if (s.Contains("payload hash") || s.Contains("verify signature") || s.Contains("signature"))
        {
            return DeterminismVerifyReason.SignatureInvalid;
        }

        // Pinned DEFAULT: an unrecognized cosign fault is rejected (fail-closed), never a pass.
        return DeterminismVerifyReason.UnclassifiedVerifierFault;
    }

    /// <summary>
    /// The cosign-Ok branch (INV-010/011/013): the crypto verified, so now Corrected applies the
    /// semantic checks cosign's <c>--check-claims</c> does NOT — the decoded signed Statement must
    /// byte-equal the reconstruction [INV-010], the cert workflow-SHA must equal the receipt's
    /// <c>attested_commit</c> [INV-011], and the layer-1 claim policy must accept [INV-013 layer 1].
    /// Returns <see cref="DeterminismVerifyOutcome.Verified"/> ONLY when every check holds; any
    /// failure is a SPECIFIC typed reject (fail-closed).
    /// </summary>
    private static DeterminismVerifyResult VerifyCosignOk(
        DeterminismVerifyRequest request, byte[] receiptBytes, RunReceipt receipt)
    {
        // INV-010: decode the SIGNED DSSE payload and require it BYTE-EQUAL the Statement Corrected
        // reconstructs from the committed receipt through the SINGLE canonical serializer. cosign's
        // --check-claims verifies the subject DIGEST but never the predicate CONTENT, so a mutated
        // predicate that keeps sha256(receipt) is caught ONLY here. A payload that cannot be decoded
        // on the Ok path is fail-closed to the reconstruction-mismatch reject.
        byte[] decodedPayload;
        try
        {
            using JsonDocument bundleDoc = JsonDocument.Parse(File.ReadAllBytes(request.BundlePath));
            string payloadB64 = bundleDoc.RootElement
                .GetProperty("dsseEnvelope").GetProperty("payload").GetString()
                ?? throw new InvalidOperationException("bundle dsseEnvelope.payload is absent");
            decodedPayload = Convert.FromBase64String(payloadB64);
        }
        catch (Exception)
        {
            return Reject(DeterminismVerifyReason.StatementReconstructionMismatch);
        }

        byte[] reconstruction =
            Encoding.UTF8.GetBytes(DeterminismAttestation.SerializeStatementJson(receiptBytes, receipt));

        if (!decodedPayload.AsSpan().SequenceEqual(reconstruction))
        {
            return Reject(DeterminismVerifyReason.StatementReconstructionMismatch);
        }

        // INV-011: the certificate's workflow-SHA (the argv value, or the receipt's attested_commit
        // when the request supplies none) must EQUAL the receipt's attested_commit. This is the
        // Corrected-SIDE binding check reached only once identity has passed (distinct from the
        // cosign identity check) — the 2b negative.
        string certWorkflowSha = ResolveCertWorkflowSha(request, receipt);
        if (!string.Equals(certWorkflowSha, receipt.AttestedCommit, StringComparison.Ordinal))
        {
            return Reject(DeterminismVerifyReason.CertWorkflowShaMismatch);
        }

        // INV-013 layer 1: the pure claim policy over the now-authenticated receipt (outcome, RID,
        // staleness, ancestry). A single specific violation rejects with its own reason.
        var view = new AuthenticatedReceiptView
        {
            ExecutionStatus = receipt.ExecutionStatus,
            ComparisonStatus = receipt.ComparisonStatus,
            Rid = receipt.Platform.Rid,
            ManifestStale = request.ManifestStale,
            AttestedCommitAncestry = request.AttestedCommitAncestry,
        };
        DeterminismVerifyReason? policyReason = DeterminismPolicyClassifier.Classify(view, request.ExpectedRid);
        if (policyReason is not null)
        {
            return Reject(policyReason.Value);
        }

        // Crypto verified AND decoded payload byte-equals the reconstruction AND cert-SHA binds to
        // attested_commit AND the claim policy accepts — the single Verified path (INV-012).
        return new DeterminismVerifyResult(DeterminismVerifyOutcome.Verified, null);
    }

    /// <summary>
    /// The <c>--certificate-github-workflow-sha</c> value used both in the argv and the INV-011
    /// cross-check: the request's explicit value, or — when the request supplies none (the
    /// production real path) — the committed receipt's <c>attested_commit</c>.
    /// </summary>
    private static string ResolveCertWorkflowSha(DeterminismVerifyRequest request, RunReceipt receipt)
        => request.CertWorkflowSha ?? receipt.AttestedCommit;

    /// <summary>
    /// Is the file PRESENT but UNREADABLE by this process (the EA-009 I/O fault)? A missing file is
    /// NOT unreadable here (returns false — a missing binary is a launch fault, not an I/O fault; a
    /// missing root would fail the CosignRunner regular-file check). Opens for a single-byte read so
    /// a large binary is never fully read. Never throws.
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
            // Present (File.Exists true) but the open/read threw — a permission / I/O fault.
            return true;
        }
    }

    /// <summary>
    /// Bridge a typed verify result to the carrier <see cref="ProbeResult"/> (INV-012): the carrier
    /// <c>Satisfied</c> is true ONLY when the outcome is
    /// <see cref="DeterminismVerifyOutcome.Verified"/>, and the reason is a non-empty carrier token
    /// from the TOTAL boundary map (never a free string).
    /// </summary>
    public static ProbeResult ToProbeResult(DeterminismVerifyResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.Outcome == DeterminismVerifyOutcome.Verified)
        {
            // Satisfied ONLY on Verified (INV-012 / item #1.4) — ran-passed is the probe-derived
            // positive token.
            return ProbeResult.TryCreate(true, VerifiedToken, ReferenceResolution.Resolved)!;
        }

        // A non-verified outcome is NEVER satisfied. A missing reason on a non-verified result is
        // fail-closed to the pinned default (a typed result must always carry a reason).
        DeterminismVerifyReason reason = result.Reason ?? DeterminismVerifyReason.UnclassifiedVerifierFault;
        return ProbeResult.TryCreate(false, ToCarrierProbeReason(reason), ReferenceResolution.Resolved)!;
    }

    /// <summary>
    /// Map an internal typed reason to its carrier <c>ProbeReasons</c> token (INV-012 boundary).
    /// Total over <see cref="DeterminismVerifyReason"/>, no free-string fallthrough — every value
    /// resolves to a non-empty token drawn from <see cref="CarrierProbeReasonTokens"/>. An
    /// out-of-range value fails closed to the unclassified-verifier-fault token.
    /// </summary>
    public static string ToCarrierProbeReason(DeterminismVerifyReason reason)
        => ReasonTokens.TryGetValue(reason, out string? token)
            ? token
            : ReasonTokens[DeterminismVerifyReason.UnclassifiedVerifierFault];

    private static DeterminismVerifyResult Reject(DeterminismVerifyReason reason)
        => new(DeterminismVerifyOutcome.Rejected, reason);

    /// <summary>
    /// Build the cosign verify argv (INV-010/011 / DD-002 / PRH-001). GREEN freezes the EXACT
    /// <c>verify-blob-attestation</c> argv from the transcript spike:
    /// <c>--check-claims=true --type &lt;predicateType&gt; --certificate-identity &lt;id&gt;
    /// --certificate-oidc-issuer &lt;iss&gt; --certificate-github-workflow-sha &lt;sha&gt;
    /// --use-signed-timestamps --trusted-root &lt;root&gt; --bundle &lt;bundle&gt; &lt;receipt-blob&gt;</c>
    /// — all flags EXACT, never a <c>-regexp</c>/insecure variant (PRH-001), drawing the identity
    /// from <see cref="DeterminismVerifyRequest.Identity"/> and the SHA from
    /// <see cref="DeterminismVerifyRequest.CertWorkflowSha"/>.
    ///
    /// The argv is EXACT and never a <c>-regexp</c>/<c>--insecure-*</c> variant and never
    /// <c>--check-claims=false</c> (PRH-001): claims-checking is ON, identity + issuer are pinned as
    /// literal SAN values, the predicate type is named (else cosign defaults to <c>custom</c> and
    /// rejects — the CVE-2026-39395 predicate-type binding), and offline anchoring is
    /// <c>--use-signed-timestamps</c> + pinned <c>--trusted-root</c> (no verify-time network /
    /// Rekor content search). When <see cref="DeterminismVerifyRequest.CertWorkflowSha"/> is
    /// <c>null</c> the workflow-SHA is derived from the committed receipt's <c>attested_commit</c>.
    /// </summary>
    public static IReadOnlyList<string> BuildVerifyArgv(DeterminismVerifyRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        string certWorkflowSha = request.CertWorkflowSha ?? DeriveWorkflowShaFromReceipt(request.ReceiptPath);

        return new[]
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
    }

    /// <summary>
    /// Derive the <c>--certificate-github-workflow-sha</c> from the committed receipt's
    /// <c>attested_commit</c> (the production real path, when the request supplies no explicit
    /// value). An unreadable/unparseable receipt yields the empty string — the argv still forms and
    /// cosign fails closed on the empty SHA constraint (never a silent accept).
    /// </summary>
    private static string DeriveWorkflowShaFromReceipt(string receiptPath)
    {
        try
        {
            return RunReceipt.FromJson(File.ReadAllBytes(receiptPath)).AttestedCommit;
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }
}
