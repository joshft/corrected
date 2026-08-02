using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Corrected.Gate;

// P3 determinism-attestation spec INV-018 / INV-019 (~628-690), TB-006. This file is the PURE
// subject-classification-and-manifest MECHANISM: a single executable policy (owned roots/anchors/
// exclusions) drives ONE relevance predicate, and that predicate feeds BOTH consumers — the
// manifest set-equality check AND the live-CI required decision (INV-018 "one classifier, two
// consumers"). The policy CONTENT (the real repo roots/anchors/exclusions) is pinned separately as
// DATA in SubjectClassificationPolicy.Pinned; this engine is a pure function of (policy, tree) and
// is unit-tested against SYNTHETIC trees. No filesystem I/O, no git — the impure HEAD-digest /
// staleness producer that supplies a real tree lives in SubjectManifestProducer (Sub-build B).
//
// Fail-closed direction (AP-001/AP-022, PMB-003): an omitted relevant file, a vacuous/mutated
// exclusion, a duplicate manifest path, or an unvalidated policy all REJECT (fail closed). The
// accept cell is the narrow exact case; every other input rejects.

/// <summary>
/// The verdict of validating a <see cref="SubjectPolicy"/> shape (INV-018). <see cref="Valid"/> is
/// true ONLY when every root/anchor/exclusion satisfies the pinned shape rules; otherwise a
/// fail-closed rejection carrying a human-readable <see cref="Reason"/>.
/// </summary>
public sealed record PolicyValidation(bool Valid, string Reason)
{
    internal static PolicyValidation Accept() => new(true, string.Empty);
    internal static PolicyValidation Reject(string reason) => new(false, reason);
}

/// <summary>
/// The pinned, closed-world subject-classification POLICY (INV-018). It defines the determinism-
/// relevant surface as:
///   * <see cref="OwnedRoots"/> — closed-world owned directory roots (each a repo-relative path
///     ENDING IN <c>/</c>; the safest glob form = "every committed file under this directory");
///   * <see cref="Anchors"/> — EXACT repo-relative paths for scattered individual files outside the
///     owned roots (the verifier/signing/pin surface + this policy itself, as self-inputs);
///   * <see cref="Exclusions"/> — EXACT repo-relative paths carved OUT of the owned roots
///     (enumerated files ONLY — <b>no broad exclusion globs</b>, INV-018).
/// The record is immutable DATA; the engine (<see cref="SubjectClassifier"/>) is the behavior.
/// </summary>
public sealed record SubjectPolicy(
    IReadOnlyList<string> OwnedRoots,
    IReadOnlyList<string> Anchors,
    IReadOnlyList<string> Exclusions)
{
    /// <summary>The glob metacharacters forbidden everywhere (exclusions especially — INV-018).</summary>
    private static readonly char[] GlobMetacharacters = { '*', '?', '[' };

    /// <summary>
    /// Validate the policy shape, fail-closed (INV-018). Valid IFF: every owned root is a non-empty
    /// repo-relative directory prefix ending in <c>/</c> with no glob metacharacter, no <c>..</c>
    /// segment, and no leading <c>/</c>; every anchor is a non-empty exact repo-relative path with
    /// no glob metacharacter, no <c>..</c>, no leading <c>/</c>, and no trailing <c>/</c>; every
    /// exclusion is likewise an exact repo-relative path with NO glob metacharacter (<c>* ? [</c>) —
    /// a glob in the exclusion list is the "broad exclusion glob" the spec forbids and REJECTS the
    /// policy. This is a SHAPE check only; a shape-valid exclusion that protects nothing (not under
    /// any owned root) is caught later by <see cref="SubjectManifestGate.CheckExclusionCompleteness"/>.
    /// </summary>
    public PolicyValidation Validate()
    {
        foreach (string root in OwnedRoots)
        {
            string? err = ShapeError(root, "owned root");
            if (err is not null)
            {
                return PolicyValidation.Reject(err);
            }

            if (!root.EndsWith('/'))
            {
                return PolicyValidation.Reject($"owned root must end in '/': '{root}'");
            }
        }

        foreach (string anchor in Anchors)
        {
            string? err = ShapeError(anchor, "anchor");
            if (err is not null)
            {
                return PolicyValidation.Reject(err);
            }

            if (anchor.EndsWith('/'))
            {
                return PolicyValidation.Reject($"anchor must not end in '/': '{anchor}'");
            }
        }

        foreach (string exclusion in Exclusions)
        {
            string? err = ShapeError(exclusion, "exclusion");
            if (err is not null)
            {
                return PolicyValidation.Reject(err);
            }
        }

        return PolicyValidation.Accept();
    }

    /// <summary>
    /// The shared shape rule for a repo-relative path member (fail-closed). Returns a rejection
    /// reason, or <c>null</c> when the shape is legal: non-empty, no leading <c>/</c>, no <c>..</c>
    /// path segment, and no glob metacharacter.
    /// </summary>
    private static string? ShapeError(string value, string kind)
    {
        if (string.IsNullOrEmpty(value))
        {
            return $"{kind} must be non-empty";
        }

        if (value.StartsWith('/'))
        {
            return $"{kind} must be repo-relative (no leading '/'): '{value}'";
        }

        // Backslash is never a repo-relative forward-slash separator; a backslash member would pass
        // validation yet match nothing against git's forward-slash paths — a silent vacuity /
        // under-inclusion (MA-B-AUDIT-03). Reject it here, symmetric with PointerSchema.
        if (value.Contains('\\'))
        {
            return $"{kind} must use forward slashes (no backslash): '{value}'";
        }

        if (value.IndexOfAny(GlobMetacharacters) >= 0)
        {
            return $"{kind} must not contain a glob metacharacter (* ? [): '{value}'";
        }

        foreach (string segment in value.Split('/'))
        {
            if (segment == "..")
            {
                return $"{kind} must not contain a '..' segment: '{value}'";
            }
        }

        return null;
    }
}

/// <summary>
/// A base→head change set for the live-CI required decision (INV-018/019 consumer 2). Renames carry
/// both the old and the new path so a rename touching the relevant set on EITHER side is caught.
/// </summary>
public sealed record ChangeSet(
    IReadOnlyList<string> Added,
    IReadOnlyList<string> Modified,
    IReadOnlyList<string> Deleted,
    IReadOnlyList<RenamedPath> Renamed);

/// <summary>A rename in a <see cref="ChangeSet"/> — the path before and after.</summary>
public sealed record RenamedPath(string OldPath, string NewPath);

/// <summary>
/// The single executable classifier (INV-018). ONE relevance predicate
/// (<see cref="IsRelevant"/>) drives BOTH consumers: the manifest subject set
/// (<see cref="DiscoverSubjectSet"/>) and the live-CI required decision
/// (<see cref="ChangeIsRelevant"/>). Pure — no I/O, no git; every input is supplied.
/// </summary>
public static class SubjectClassifier
{
    /// <summary>
    /// The relevance predicate both consumers share (INV-018). A path is relevant IFF it is under
    /// an owned root OR equals an anchor, AND it is not an exact exclusion. Comparison is Ordinal on
    /// forward-slash repo-relative paths. An owned root ends in <c>/</c>, so <c>StartsWith(root)</c>
    /// is boundary-safe: a look-alike sibling directory (<c>…GateX/</c>) does NOT match <c>…Gate/</c>.
    /// </summary>
    public static bool IsRelevant(SubjectPolicy policy, string repoRelativePath)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(repoRelativePath);

        // The exclusion carve-out wins over any inclusion (an exact exclusion is never relevant).
        if (policy.Exclusions.Contains(repoRelativePath, StringComparer.Ordinal))
        {
            return false;
        }

        bool underOwnedRoot = policy.OwnedRoots.Any(
            root => repoRelativePath.StartsWith(root, StringComparison.Ordinal));
        bool isExactAnchor = policy.Anchors.Contains(repoRelativePath, StringComparer.Ordinal);

        return underOwnedRoot || isExactAnchor;
    }

    /// <summary>
    /// Consumer 1 (manifest): the sorted, de-duplicated subject set discovered by applying
    /// <see cref="IsRelevant"/> to a supplied repo file list. Ordinal ascending sort for a stable
    /// manifest.
    /// </summary>
    public static IReadOnlyList<string> DiscoverSubjectSet(SubjectPolicy policy, IEnumerable<string> repoFiles)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(repoFiles);

        return repoFiles
            .Where(file => IsRelevant(policy, file))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(file => file, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Consumer 2 (live-CI required decision): true IFF the base→head change touches the relevant
    /// set (INV-019). An added/modified/deleted relevant path, or a rename whose OLD or NEW path is
    /// relevant, requires the live determinism job. Fail-closed direction = require the job.
    /// </summary>
    public static bool ChangeIsRelevant(SubjectPolicy policy, ChangeSet change)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(change);

        return change.Added.Any(path => IsRelevant(policy, path))
            || change.Modified.Any(path => IsRelevant(policy, path))
            || change.Deleted.Any(path => IsRelevant(policy, path))
            || change.Renamed.Any(
                rename => IsRelevant(policy, rename.OldPath) || IsRelevant(policy, rename.NewPath));
    }
}

/// <summary>One manifest row: a repo-relative path and the lowercase-hex SHA-256 of its bytes.</summary>
public sealed record SubjectManifestEntry(string Path, string Sha256);

/// <summary>
/// The subject manifest (INV-018): the enumerated {path, sha256} rows of the determinism subject
/// set. The manifest NEVER lists its own digest (no self-reference) — <see cref="ComputeDigest"/>
/// computes it OVER the rows, and the receipt binds those bytes (INV-006). Duplicate paths are
/// rejected fail-closed by <see cref="ComputeDigest"/>.
/// </summary>
public sealed record SubjectManifest(IReadOnlyList<SubjectManifestEntry> Entries)
{
    /// <summary>The set of repo-relative paths the manifest enumerates (for set-equality).</summary>
    public IReadOnlyCollection<string> Paths => Entries
        .Select(entry => entry.Path)
        .ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// The canonical manifest digest (INV-018/019). Deterministic and order-independent: rows are
    /// sorted by <see cref="SubjectManifestEntry.Path"/> Ordinal, each serialized as
    /// <c>{Path}\n{Sha256}\n</c>, concatenated UTF-8, SHA-256, lowercase hex. A duplicate path is a
    /// fail-closed error (the manifest is malformed). This value is compared to the receipt's signed
    /// <c>subject_manifest_digest</c> for staleness (INV-019). The exact recipe is byte-compatible
    /// with the offline signing script so the C# verifier and the signer agree.
    /// </summary>
    /// <summary>The canonical field/row delimiters — forbidden inside a Path or Sha256 so the serialization stays injective.</summary>
    private static readonly char[] DelimiterChars = { '\n', '\0' };

    /// <summary>
    /// The canonical digest PREIMAGE (INV-018/019): the EXACT byte-string <see cref="ComputeDigest"/>
    /// hashes — rows sorted by <see cref="SubjectManifestEntry.Path"/> Ordinal, each serialized as
    /// <c>{Path}\n{Sha256}\n</c>, concatenated. This is the SINGLE source the CI producer emits to
    /// the hand-off manifest file, so <c>sha256(emitted-preimage) == ComputeDigest ==</c> the
    /// receipt's signed <c>subject_manifest_digest</c> — keeping the producer and the gate verifier
    /// byte-identical (the p3-producer-manifest-digest fix). Fail-closed (AP-001/MA-B-AUDIT-02): a
    /// duplicate path, or a newline/NUL inside a path or sha (a non-injective manifest that could let
    /// a stale baseline read fresh), THROWS rather than hashing an ambiguous manifest.
    /// </summary>
    public string CanonicalPreimage()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (SubjectManifestEntry entry in Entries)
        {
            if (!seen.Add(entry.Path))
            {
                throw new InvalidOperationException(
                    $"malformed manifest: duplicate subject path '{entry.Path}'");
            }

            // The canonical form uses '\n' as the field/row delimiter with no escaping, so a '\n'
            // (or '\0') inside a Path or Sha256 would make the serialization NON-injective (two
            // different manifests colliding to one digest -> a stale baseline could read fresh,
            // MA-B-AUDIT-02). Reject it fail-closed rather than hash an ambiguous manifest.
            if (entry.Path.IndexOfAny(DelimiterChars) >= 0 || entry.Sha256.IndexOfAny(DelimiterChars) >= 0)
            {
                throw new InvalidOperationException(
                    $"malformed manifest: a subject path or digest contains a newline/NUL delimiter ('{entry.Path}')");
            }
        }

        var canonical = new StringBuilder();
        foreach (SubjectManifestEntry entry in Entries.OrderBy(entry => entry.Path, StringComparer.Ordinal))
        {
            canonical.Append(entry.Path).Append('\n').Append(entry.Sha256).Append('\n');
        }

        return canonical.ToString();
    }

    public string ComputeDigest()
    {
        // Single source of truth: the digest is SHA-256 of the canonical preimage bytes. The CI
        // producer emits that same preimage and takes sha256sum of it, so producer and verifier agree.
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(CanonicalPreimage()));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

/// <summary>
/// The manifest ↔ classifier set-equality verdict (INV-018). <see cref="Equal"/> is true ONLY when
/// BOTH difference lists are empty. <see cref="OmittedRelevant"/> = files the classifier discovered
/// but the manifest omits (the dangerous direction — a relevant file that could change unnoticed);
/// <see cref="ExtraInManifest"/> = manifest rows the classifier does not discover.
/// </summary>
public sealed record SetEqualityResult(
    bool Equal,
    IReadOnlyList<string> OmittedRelevant,
    IReadOnlyList<string> ExtraInManifest);

/// <summary>
/// The exclusion-completeness verdict (INV-018). <see cref="Complete"/> is true ONLY when EVERY
/// declared exclusion is NON-VACUOUS — present in the tree AND relevant-by-root/anchor, so removing
/// it would actually change the discovered set. <see cref="VacuousExclusions"/> lists the
/// exclusions that protect nothing (a mutated/mis-typed exclusion), which fail closed.
/// </summary>
public sealed record ExclusionCompletenessResult(
    bool Complete,
    IReadOnlyList<string> VacuousExclusions);

/// <summary>
/// The manifest gate (INV-018): set-equality between the classifier's discovered set and the
/// manifest, plus exclusion-completeness. Both are fail-closed — an omitted relevant file or a
/// vacuous/mutated exclusion rejects (AP-022/PMB-003: an accept-side enumeration must not fail open
/// on a member it forgot to list).
/// </summary>
public static class SubjectManifestGate
{
    /// <summary>
    /// Set-equality between the classifier's discovered set and the manifest's enumerated paths
    /// (INV-018). Equal IFF neither side has a path the other lacks. A relevant file omitted from
    /// the manifest fails closed (Equal=false, listed in <see cref="SetEqualityResult.OmittedRelevant"/>).
    /// </summary>
    public static SetEqualityResult CheckSetEquality(
        IReadOnlyCollection<string> discoveredSet, SubjectManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(discoveredSet);
        ArgumentNullException.ThrowIfNull(manifest);

        var discovered = discoveredSet.ToHashSet(StringComparer.Ordinal);
        var manifestPaths = manifest.Paths.ToHashSet(StringComparer.Ordinal);

        // OmittedRelevant = discovered ∖ manifest (the dangerous fail-open direction).
        List<string> omitted = discovered
            .Where(path => !manifestPaths.Contains(path))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();

        // ExtraInManifest = manifest ∖ discovered.
        List<string> extra = manifestPaths
            .Where(path => !discovered.Contains(path))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();

        return new SetEqualityResult(omitted.Count == 0 && extra.Count == 0, omitted, extra);
    }

    /// <summary>
    /// Exclusion-completeness (INV-018): every declared exclusion must be NON-VACUOUS against the
    /// supplied tree — the path must be present AND relevant-by-root/anchor (so absent the exclusion
    /// it WOULD be discovered). A vacuous exclusion (path not in the tree, or not relevant-by-root/
    /// anchor) is a mutated/mis-targeted exclusion and fails closed. This gives the exclusion set the
    /// same completeness protection as the inclusions.
    /// </summary>
    public static ExclusionCompletenessResult CheckExclusionCompleteness(
        SubjectPolicy policy, IEnumerable<string> repoFiles)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(repoFiles);

        var files = repoFiles.ToHashSet(StringComparer.Ordinal);

        List<string> vacuous = policy.Exclusions
            .Where(exclusion => !IsNonVacuous(policy, exclusion, files))
            .OrderBy(exclusion => exclusion, StringComparer.Ordinal)
            .ToList();

        return new ExclusionCompletenessResult(vacuous.Count == 0, vacuous);
    }

    /// <summary>
    /// An exclusion is NON-VACUOUS iff it is present in the tree AND would be relevant absent the
    /// carve-out (under an owned root OR an exact anchor). Note it is NOT checked through
    /// <see cref="SubjectClassifier.IsRelevant"/> — that predicate already returns false for a
    /// declared exclusion, so it would report every exclusion vacuous.
    /// </summary>
    private static bool IsNonVacuous(SubjectPolicy policy, string exclusion, HashSet<string> files)
    {
        if (!files.Contains(exclusion))
        {
            return false;
        }

        bool wouldBeRelevant =
            policy.OwnedRoots.Any(root => exclusion.StartsWith(root, StringComparison.Ordinal))
            || policy.Anchors.Contains(exclusion, StringComparer.Ordinal);

        return wouldBeRelevant;
    }
}
