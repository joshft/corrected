using System;
using System.Collections.Generic;
using System.IO;
using Corrected.Gate.Kernel;

namespace Corrected.Gate;

// P3 phase-entry INV-030 (Group G / MA-C part e) — the LIVE-callable entry-integrity producer, the
// entry analog of P3Probe (which MA-B wired for determinism). It makes EntryVerifier reachable from
// the gate: it resolves the durable ENTRY-ACTIVATION pointer under context.RepoRoot, and —
//   * ABSENT pointer  -> EntryIntegrity.Absent (the pre-entry zero-state; the src/ ban stays active,
//                        INV-027, so readiness stays BLOCKED). This is the PR2 live state (Group G
//                        dormant, the committed block is v1) — NO behavior change.
//   * PRESENT pointer -> parse the closed entry-evidence pointer, resolve the committed {commit-X
//                        blob, entry bundle}, and hand them to EntryVerifier under the PRODUCTION
//                        identity + real gate-side ancestry -> the typed EntryIntegrity verdict.
// The full Group-G ACTIVATION orchestrator (assembling ActivationValidator / LifecycleGate /
// ReadinessGate with this verdict) is P2 scope — entry activation only happens then. Like P3Probe's,
// the production ACCEPT branch is unexercisable until P2 (RS-006/RS-011): a committed FIXTURE-identity
// bundle driven through the production argv is an identity-mismatch reject, never Verified.
//
// NEVER throws (mirrors the IEvidenceProbe contract): any internal fault fails closed to a typed
// non-Verified verdict, never the accepting EntryIntegrity.Verified.

/// <summary>
/// The gate-side ENTRY-INTEGRITY probe (INV-030): resolves the durable entry-activation pointer and
/// produces the <see cref="EntryIntegrity"/> verdict by driving <see cref="EntryVerifier"/>. Absent
/// pointer -> <see cref="EntryIntegrity.Absent"/> (the pre-entry zero-state).
/// </summary>
public static class EntryIntegrityProbe
{
    /// <summary>
    /// Evaluate the entry integrity under <paramref name="context"/>. Resolves
    /// <see cref="ProbeOrchestrator.EntryActivationPointerPath"/> under the injected repo root (never
    /// the dotnet test cwd). Absent -> <see cref="EntryIntegrity.Absent"/>; present -> the real verify
    /// path. Never throws.
    /// </summary>
    public static EntryIntegrity Evaluate(GateContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        string pointer = Path.Combine(
            context.RepoRoot, Path.Combine(ProbeOrchestrator.EntryActivationPointerPath.Split('/')));

        // ABSENT pointer = the expected pre-entry zero-state (Group G dormant). Fail closed to Absent
        // (the src/ ban stays active, readiness stays BLOCKED) — never the accepting Verified.
        if (!File.Exists(pointer))
        {
            return EntryIntegrity.Absent;
        }

        // PRESENT pointer -> the real verify path. Any internal fault fails closed (never a throw,
        // never Verified).
        try
        {
            return EvaluatePresentPointer(context, pointer);
        }
        catch (Exception)
        {
            return EntryIntegrity.Rejected;
        }
    }

    private static EntryIntegrity EvaluatePresentPointer(GateContext context, string pointerPath)
    {
        string repoRoot = context.RepoRoot;

        // 1) Parse the minimal pointer JSON. A malformed pointer fails closed (Rejected).
        (PointerSchema.PointerDocument? doc, _) =
            PointerSchema.ParsePointerJson(File.ReadAllBytes(pointerPath));
        if (doc is null)
        {
            return EntryIntegrity.Rejected;
        }

        // The entry pointer MUST be the entry-evidence family; any other family is malformed here
        // (RS-024 — a determinism pointer presented to the entry gate cross-rejects).
        PointerFamily? family = PointerSchema.FamilyFromWire(doc.Family);
        if (family is null || family.Value != PointerFamily.EntryEvidence)
        {
            return EntryIntegrity.Rejected;
        }

        // 2) Build the closed-schema descriptor + validate against the committed-path set. Receipt =
        //    the commit-X blob (the cosign --check-claims anchor); Bundle = the entry sigstore bundle.
        string root = PointerSchema.FixedRoot(family.Value)!;
        string onDiskSegment = FirstSegmentUnderRoot(doc.Receipt, root);
        string receiptAbs = Path.Combine(repoRoot, Path.Combine(doc.Receipt.Split('/')));
        string bundleAbs = Path.Combine(repoRoot, Path.Combine(doc.Bundle.Split('/')));
        bool symlinked = IsSymlink(receiptAbs) || IsSymlink(bundleAbs);

        var descriptor = new PointerDescriptor(
            family.Value, new[] { doc.Receipt }, new[] { doc.Bundle },
            doc.AttestedCommit, onDiskSegment, symlinked);

        var committed = new HashSet<string>(
            SubjectManifestProducer.EnumerateRepoFiles(repoRoot), StringComparer.Ordinal);
        PointerValidation validation = PointerSchema.ValidatePointer(descriptor, committed);
        if (!validation.Valid)
        {
            // A dangling pointer (named target not committed) is the absent zero-state; every other
            // closed-schema violation (bad path / symlink / cardinality / commit-dir) is a Rejected tamper.
            return validation.Reason.StartsWith("dangling", StringComparison.Ordinal)
                ? EntryIntegrity.Absent
                : EntryIntegrity.Rejected;
        }

        // 3) Resolve the cosign + trust-root seam (injected, else the gate-exported env). A missing
        //    seam is a fail-closed Unavailable (retryable), never a silent accept.
        string? cosignBin = context.CosignBinPath ?? Environment.GetEnvironmentVariable("COSIGN_BIN");
        string? trustRoot = context.TrustRootPath ?? Environment.GetEnvironmentVariable("TRUSTED_ROOT");
        if (string.IsNullOrEmpty(cosignBin) || string.IsNullOrEmpty(trustRoot))
        {
            return EntryIntegrity.Unavailable;
        }

        // 4) Commit-X ancestry (a real gate-side fact), computed from the pointer's attested commit.
        AncestryStatus ancestry = GitAncestry.Classify(repoRoot, doc.AttestedCommit);

        // 5) Run the real verifier under the PRODUCTION identity (the live gate never trusts the
        //    fixture identity). CertWorkflowSha=null -> derived from the receipt commit-X. Bridge to
        //    the typed EntryIntegrity verdict.
        var request = new EntryVerifyRequest
        {
            CosignBinPath = cosignBin,
            BundlePath = bundleAbs,
            ReceiptPath = receiptAbs,
            TrustRootPath = trustRoot,
            WorkingDirectory = repoRoot,
            Identity = EntryVerifyIdentity.Production,
            CertWorkflowSha = null,
            CommitAncestry = ancestry,
        };

        return EntryVerifier.ToEntryIntegrity(EntryVerifier.Verify(request));
    }

    /// <summary>The first path segment after <paramref name="root"/> (the &lt;commit&gt; dir), or "" if not under the root.</summary>
    private static string FirstSegmentUnderRoot(string repoRelativePath, string root)
    {
        if (!repoRelativePath.StartsWith(root, StringComparison.Ordinal))
        {
            return string.Empty;
        }
        string rest = repoRelativePath.Substring(root.Length);
        int slash = rest.IndexOf('/');
        return slash < 0 ? rest : rest.Substring(0, slash);
    }

    private static bool IsSymlink(string absPath)
    {
        try
        {
            var info = new FileInfo(absPath);
            return info.Exists && info.LinkTarget is not null;
        }
        catch
        {
            return false;
        }
    }
}
