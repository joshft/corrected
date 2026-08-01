using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Corrected.Gate;

/// <summary>
/// The TWO pointer FAMILIES the INV-028 closed pointer schema validates (spec ~1130–1141,
/// "Closed pointer schema (F4)" + "Pointer↔receipt coupling ... fail-closed pair (RS-029)").
/// Each family has a FIXED, spec-pinned repo-relative root that its pointer target MUST live
/// under (a pointer that escapes its family root fails closed):
///   * <see cref="P3ActiveBaseline"/> — the post-entry active-baseline pointer; fixed root
///     <c>test/attestations/inv010/</c> (versioned P3 receipts <c>…/inv010/&lt;commit&gt;/…</c>);
///   * <see cref="EntryEvidence"/> — the <c>entry_evidence_pointer</c>; fixed root
///     <c>test/attestations/entry/</c> (consistent with
///     <see cref="PrClassifier"/>'s <c>EntryReceiptPrefix</c>).
/// This is a SET-pinned vocabulary (PMB-003): exactly these two members, no default/sentinel.
/// </summary>
public enum PointerFamily
{
    /// <summary>Post-entry active-baseline pointer; fixed root <c>test/attestations/inv010/</c>.</summary>
    P3ActiveBaseline,

    /// <summary>The <c>entry_evidence_pointer</c>; fixed root <c>test/attestations/entry/</c>.</summary>
    EntryEvidence,
}

/// <summary>
/// A synthetic, already-parsed pointer descriptor (INV-028 F4). All fields are SUPPLIED test
/// inputs — this sub-track does NO filesystem I/O and NO cosign; the descriptor stands in for a
/// pointer that has already been read off disk. It carries:
///   * <see cref="Family"/> — which fixed-root family the pointer belongs to;
///   * <see cref="ReceiptPaths"/> / <see cref="BundlePaths"/> — the repo-relative path(s) the
///     pointer NAMES (lists so the exactly-one cardinality rule can be exercised: 0, 1, or ≥2);
///   * <see cref="AttestedCommit"/> — the receipt's OWN declared <c>attested_commit</c> /
///     entry-<c>X</c> binding string;
///   * <see cref="OnDiskDirSegment"/> — the on-disk directory-name segment the receipt sits in;
///   * <see cref="TargetIsSymlink"/> — a synthetic "the target is a symlink" flag (symlinks are
///     rejected fail-closed).
/// </summary>
public sealed record PointerDescriptor(
    PointerFamily Family,
    IReadOnlyList<string> ReceiptPaths,
    IReadOnlyList<string> BundlePaths,
    string AttestedCommit,
    string OnDiskDirSegment,
    bool TargetIsSymlink);

/// <summary>
/// The closed-schema validation verdict: <see cref="Valid"/> is true ONLY when EVERY INV-028
/// pointer rule holds; otherwise it is a fail-closed (deny-by-default) rejection carrying a
/// typed <see cref="Reason"/>. Immutable.
/// </summary>
public sealed record PointerValidation(bool Valid, string Reason)
{
    /// <summary>The single accept verdict (empty reason).</summary>
    internal static PointerValidation Accept() => new(true, string.Empty);

    /// <summary>A fail-closed rejection with a typed reason.</summary>
    internal static PointerValidation Reject(string reason) => new(false, reason);
}

/// <summary>
/// INV-028 CLOSED pointer schema (F4) + dangling-pointer fail-closed coupling (RS-029, spec
/// ~1130–1147). Validates a synthetic <see cref="PointerDescriptor"/> against the set of
/// committed repo-relative paths and returns <see cref="PointerValidation.Valid"/> == true iff
/// ALL of the following hold (else fail closed, deny-by-default — AP-001/AP-017):
///   1. exact cardinality — EXACTLY one named receipt AND exactly one named bundle;
///   2. closed path schema for BOTH — a normalized, repo-relative path under the family's fixed
///      root (no absolute path / Windows drive, no <c>..</c> segment, no empty <c>//</c> segment,
///      must start with the family root);
///   3. no symlink target;
///   4. commit-directory agreement — the receipt path's <c>&lt;commit&gt;</c> segment equals both
///      the receipt's declared <c>attested_commit</c> AND the on-disk directory name, and the
///      bundle sits in the SAME <c>&lt;commit&gt;</c> directory;
///   5. no dangling (RS-029 half-applied refresh) — BOTH the named receipt AND bundle are present
///      in the committed-path set.
/// This sub-track (5d-ii) builds ONLY the pointer schema validator; it does NOT touch the sibling
/// 5d-i health fold. All inputs are synthetic (no I/O, no crypto).
/// </summary>
public static class PointerSchema
{
    /// <summary>
    /// The minimal on-disk P3 active-baseline pointer document (maintainer-selected 4-field shape,
    /// 2026-07-31). The <c>receipt</c>/<c>bundle</c> are repo-relative paths under the family fixed
    /// root <c>test/attestations/inv010/&lt;commit&gt;/</c>; everything else (subject-manifest digest,
    /// trust-root id) is carried by the signed receipt, not duplicated here.
    /// </summary>
    public sealed record PointerDocument(string Family, string Receipt, string Bundle, string AttestedCommit);

    /// <summary>The public accessor for a family's fixed repo-relative root (or null for unknown).</summary>
    public static string? FixedRoot(PointerFamily family) => FixedRootOf(family);

    /// <summary>Map the on-disk <c>family</c> wire string to a <see cref="PointerFamily"/>, or null (unknown, fail-closed).</summary>
    public static PointerFamily? FamilyFromWire(string? wire) => wire switch
    {
        "p3-active-baseline" => PointerFamily.P3ActiveBaseline,
        "entry-evidence" => PointerFamily.EntryEvidence,
        _ => null,
    };

    /// <summary>
    /// Parse the minimal on-disk pointer JSON, fail-closed. Returns the typed
    /// <see cref="PointerDocument"/> on success, else <c>(null, error)</c> for malformed JSON, a
    /// non-object root, or any missing / non-string / empty required field. Never throws.
    /// </summary>
    public static (PointerDocument? Document, string? Error) ParsePointerJson(byte[] bytes)
    {
        if (bytes is null)
        {
            return (null, "null pointer bytes");
        }

        try
        {
            using JsonDocument doc = JsonDocument.Parse(bytes);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return (null, "pointer root is not a JSON object");
            }

            string? family = RequiredString(doc.RootElement, "family");
            string? receipt = RequiredString(doc.RootElement, "receipt");
            string? bundle = RequiredString(doc.RootElement, "bundle");
            string? attested = RequiredString(doc.RootElement, "attested_commit");

            if (family is null || receipt is null || bundle is null || attested is null)
            {
                return (null, "pointer missing a required field (family/receipt/bundle/attested_commit)");
            }

            return (new PointerDocument(family, receipt, bundle, attested), null);
        }
        catch (JsonException)
        {
            return (null, "pointer is not parseable JSON");
        }
    }

    /// <summary>A required non-empty string field, or null when absent / wrong-typed / empty.</summary>
    private static string? RequiredString(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out JsonElement value) || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }
        string? s = value.GetString();
        return string.IsNullOrWhiteSpace(s) ? null : s;
    }

    /// <summary>
    /// Validate a parsed pointer descriptor against the committed-path set. Pure over the supplied
    /// inputs (no I/O — <paramref name="committedPaths"/> and <see cref="PointerDescriptor.TargetIsSymlink"/>
    /// are the synthetic stand-ins for the on-disk world; this never touches the filesystem).
    /// Returns <see cref="PointerValidation.Valid"/> == true iff EVERY rule below holds, else it
    /// fails closed (deny-by-default) with a typed reason.
    /// </summary>
    public static PointerValidation ValidatePointer(
        PointerDescriptor? descriptor, IReadOnlySet<string> committedPaths)
    {
        // Rule 0 — null guard (totality/deny-by-default): a malformed call is never permissive and
        // must not throw a raw NRE either.
        if (descriptor is null)
        {
            return PointerValidation.Reject("null-descriptor: descriptor is null (deny-by-default)");
        }
        if (committedPaths is null)
        {
            return PointerValidation.Reject("null-committed-set: committed-path set is null (deny-by-default)");
        }

        // The family's FIXED, spec-pinned repo-relative root (INV-028 F4, spec ~1136–1137). An
        // unknown/undefined enum value fails closed rather than throwing.
        string? root = FixedRootOf(descriptor.Family);
        if (root is null)
        {
            return PointerValidation.Reject($"unknown-family: no fixed root for family '{descriptor.Family}'");
        }

        // Rule 1 — EXACT cardinality: exactly one named receipt AND exactly one named bundle
        // (0 or >=2 of either fails closed).
        IReadOnlyList<string>? receipts = descriptor.ReceiptPaths;
        IReadOnlyList<string>? bundles = descriptor.BundlePaths;
        if (receipts is null || receipts.Count != 1)
        {
            return PointerValidation.Reject(
                $"cardinality: expected exactly one receipt, got {receipts?.Count ?? 0}");
        }
        if (bundles is null || bundles.Count != 1)
        {
            return PointerValidation.Reject(
                $"cardinality: expected exactly one bundle, got {bundles?.Count ?? 0}");
        }

        string receipt = receipts[0];
        string bundle = bundles[0];

        // Rule 2 — CLOSED path schema, enforced on BOTH the receipt AND the bundle (the bundle-only
        // negatives pin that this is not receipt-only). Normalized, repo-relative, under the family
        // fixed root; no absolute/drive/UNC path, no '..' segment, no empty '//' segment.
        PointerValidation receiptSchema = ValidateClosedPath(receipt, root, "receipt");
        if (!receiptSchema.Valid)
        {
            return receiptSchema;
        }
        PointerValidation bundleSchema = ValidateClosedPath(bundle, root, "bundle");
        if (!bundleSchema.Valid)
        {
            return bundleSchema;
        }

        // Rule 3 — no symlink target.
        if (descriptor.TargetIsSymlink)
        {
            return PointerValidation.Reject("no-symlink: target is a symlink (TargetIsSymlink == true)");
        }

        // Rule 4 — commit-directory agreement. The <commit> segment is the FIRST path segment after
        // the family fixed root. Both paths already passed the closed-schema check above (start with
        // the root, no empty/'..' segments), so this extraction is well-defined.
        string receiptCommit = FirstSegmentAfterRoot(receipt, root);
        string bundleCommit = FirstSegmentAfterRoot(bundle, root);
        if (!string.Equals(receiptCommit, descriptor.AttestedCommit, StringComparison.Ordinal))
        {
            return PointerValidation.Reject(
                $"commit-dir: receipt path <commit> '{receiptCommit}' != attested_commit '{descriptor.AttestedCommit}'");
        }
        if (!string.Equals(receiptCommit, descriptor.OnDiskDirSegment, StringComparison.Ordinal))
        {
            return PointerValidation.Reject(
                $"commit-dir: receipt path <commit> '{receiptCommit}' != on-disk dir segment '{descriptor.OnDiskDirSegment}'");
        }
        if (!string.Equals(bundleCommit, receiptCommit, StringComparison.Ordinal))
        {
            return PointerValidation.Reject(
                $"commit-dir: bundle <commit> '{bundleCommit}' != receipt <commit> '{receiptCommit}' (different dir)");
        }

        // Rule 5 — no dangling (RS-029): BOTH named targets must be MEMBERS of the committed set
        // (membership, NOT set-equality — a real attestations tree carries many other committed
        // receipts). Append-only evidence never self-heals a dangling pointer.
        if (!committedPaths.Contains(receipt))
        {
            return PointerValidation.Reject(
                "dangling (RS-029): named receipt is absent from the committed-path set");
        }
        if (!committedPaths.Contains(bundle))
        {
            return PointerValidation.Reject(
                "dangling (RS-029): named bundle is absent from the committed-path set");
        }

        return PointerValidation.Accept();
    }

    /// <summary>The FIXED, spec-pinned repo-relative root for a family (INV-028 F4, spec ~1136–1137),
    /// or <c>null</c> for an unknown/undefined enum value (fails closed at the call site).</summary>
    private static string? FixedRootOf(PointerFamily family) => family switch
    {
        PointerFamily.P3ActiveBaseline => "test/attestations/inv010/",
        PointerFamily.EntryEvidence => "test/attestations/entry/",
        _ => null,
    };

    /// <summary>
    /// The closed path schema (INV-028 F4): a normalized, repo-relative path under the family fixed
    /// root. Rejects an absolute path (leading '/'), a backslash (Windows separator / UNC), a
    /// Windows drive prefix (e.g. <c>C:</c>), a path that does not start with the family fixed root,
    /// any RAW <c>..</c> segment (checked BEFORE any normalization so a self-cancelling
    /// <c>&lt;commit&gt;/../&lt;commit&gt;</c> is caught), and any empty <c>//</c> segment.
    /// </summary>
    private static PointerValidation ValidateClosedPath(string path, string root, string which)
    {
        if (string.IsNullOrEmpty(path))
        {
            return PointerValidation.Reject($"path-schema: {which} path is empty");
        }

        // Absolute — leading '/'.
        if (path[0] == '/')
        {
            return PointerValidation.Reject($"path-schema: {which} path is absolute (leading '/')");
        }

        // Backslash — a Windows separator or UNC '\\' path is not a normalized repo-relative path.
        if (path.IndexOf('\\') >= 0)
        {
            return PointerValidation.Reject($"path-schema: {which} path contains a backslash");
        }

        // Windows drive — e.g. 'C:'.
        if (path.Length >= 2 && path[1] == ':' && char.IsAsciiLetter(path[0]))
        {
            return PointerValidation.Reject($"path-schema: {which} path is drive-absolute (Windows drive prefix)");
        }

        // Must live under the family's fixed root (not merely the shared 'test/attestations/' prefix).
        if (!path.StartsWith(root, StringComparison.Ordinal))
        {
            return PointerValidation.Reject(
                $"path-schema: {which} path escapes the family fixed root '{root}'");
        }

        // RAW segment checks (before any normalization): no empty '//' segment, no '..' segment.
        foreach (string segment in path.Split('/'))
        {
            if (segment.Length == 0)
            {
                return PointerValidation.Reject($"path-schema: {which} path has an empty '//' segment");
            }
            if (string.Equals(segment, "..", StringComparison.Ordinal))
            {
                return PointerValidation.Reject($"path-schema: {which} path contains a '..' segment");
            }
        }

        return PointerValidation.Accept();
    }

    /// <summary>
    /// The <c>&lt;commit&gt;</c> segment = the FIRST path segment after the family fixed root. The
    /// caller guarantees <paramref name="path"/> already passed <see cref="ValidateClosedPath"/>
    /// (starts with <paramref name="root"/>, no empty/'..' segments), so the first post-root
    /// segment is present and well-formed.
    /// </summary>
    private static string FirstSegmentAfterRoot(string path, string root)
    {
        string tail = path.Substring(root.Length);
        int slash = tail.IndexOf('/');
        return slash >= 0 ? tail.Substring(0, slash) : tail;
    }
}
