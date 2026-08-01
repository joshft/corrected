using System;
using System.Collections.Generic;

namespace Corrected.Gate;

/// <summary>
/// One immutable, versioned Sigstore trust-root file in the append-only registry
/// (P3 determinism-attestation spec INV-016 / RS-029): a <see cref="RegistryId"/>
/// (the versioned root's stable id, e.g. <c>trusted_root/v1</c>) and the SHA-256 of
/// that exact root file's bytes. Each versioned root file is written ONCE and never
/// overwritten, so historical bundles stay independently verifiable against the exact
/// root current at their signing time. Immutable value.
/// </summary>
public sealed record TrustRootEntry(string RegistryId, string Sha256);

/// <summary>
/// Result of the append-only registry validator (INV-016 / RS-029): whether the
/// candidate new registry is a legal append over the old one, plus a human-readable
/// reason on rejection. Immutable value.
/// </summary>
public sealed record AppendOnlyResult(bool Valid, string Reason);

/// <summary>
/// The trust-root binding carried by a signed receipt (INV-016 / RS-029): each signed
/// receipt binds BOTH the trust-root registry id that verifies it AND that root's
/// SHA-256, so a post-rotation historical baseline selects the EXACT root it was signed
/// under — never the active/latest root, never a heuristic multi-root probe. Immutable.
/// </summary>
public sealed record SignedReceiptRootBinding(string TrustRootId, string TrustRootSha256);

/// <summary>
/// Result of the receipt-to-trust-root binding check (INV-016 / RS-029): whether the
/// receipt's bound (id, digest) pair resolves to a matching registry entry, plus a
/// reason on rejection. The pair is validated fail-closed (AP-017): a bound id that is
/// absent, OR a bound digest that disagrees with the registry entry for that id (tamper),
/// rejects. Immutable value.
/// </summary>
public sealed record RootBindingResult(bool Accepted, string Reason);

/// <summary>
/// One frozen version-to-argv row in the append-only <see cref="VersionArgvMap"/>
/// (INV-016 / RS-030): a cosign version string and the immutable verifier argv frozen
/// for that version, so historical bundles stay verifiable along the TOOL axis (not only
/// the root axis) after a version bump. Immutable value.
/// </summary>
public sealed record VersionArgvEntry(string Version, IReadOnlyList<string> Argv);

/// <summary>
/// The version-to-frozen-argv map (INV-016 / RS-030): an append-only map from a cosign
/// version string to the frozen verifier argv for that version. A version bump APPENDS a
/// new version-to-argv row while RETAINING every prior version's argv, so historical
/// committed bundles stay verifiable along the tool axis. Fail-closed lookup: an unmapped
/// version resolves to <c>null</c>, never a floating/latest argv.
///
/// GREEN: the bodies below implement the real append-retentive version-to-argv lookup — a
/// positive resolve returns the frozen argv for a mapped version, append-retention keeps every
/// prior version's argv, and an unmapped version resolves to <c>null</c> (fail-closed).
/// </summary>
public sealed class VersionArgvMap
{
    private readonly IReadOnlyList<VersionArgvEntry> _entries;

    /// <summary>
    /// Structural constructor: retain the supplied rows verbatim. The append-only invariant
    /// enforcement (rejecting a bump that mutates or drops a prior version's argv) lives in the
    /// append/validation methods, not the constructor.
    /// </summary>
    public VersionArgvMap(IReadOnlyList<VersionArgvEntry> entries)
    {
        // Structural plumbing only — the append-only invariant logic lives in the validation methods.
        _entries = entries ?? Array.Empty<VersionArgvEntry>();
    }

    /// <summary>The frozen rows currently in the map (never mutated in place).</summary>
    public IReadOnlyList<VersionArgvEntry> Entries => _entries;

    /// <summary>
    /// Append a new version-to-argv row, returning a NEW map that retains every existing
    /// row plus the appended one (RS-030 tool-axis retention). Fail closed if the version
    /// is already mapped (an existing version's argv is frozen and may not be re-pointed).
    /// </summary>
    public VersionArgvMap Append(string version, IReadOnlyList<string> argv)
    {
        // Append-only along the TOOL axis (RS-030): return a NEW map; never mutate self.
        // A blank/null version is ignored (no mutation) — an equivalent map is returned.
        if (string.IsNullOrWhiteSpace(version))
        {
            return new VersionArgvMap(_entries);
        }

        // Fail-closed re-point guard: if the version is ALREADY mapped, its frozen argv
        // wins — the re-point is NOT honored. Return a map that retains the existing rows
        // unchanged (the tool-axis analog of the append-only registry re-point rejection).
        foreach (VersionArgvEntry existing in _entries)
        {
            if (string.Equals(existing.Version, version, StringComparison.Ordinal))
            {
                return new VersionArgvMap(_entries);
            }
        }

        // Legal append: retain every existing row plus the newly frozen row.
        var appended = new List<VersionArgvEntry>(_entries)
        {
            new VersionArgvEntry(version, FreezeArgv(argv)),
        };
        return new VersionArgvMap(appended);
    }

    /// <summary>
    /// Defensive copy of a supplied argv into an immutable frozen list, so a caller that
    /// later mutates its own array cannot re-point a version's frozen argv after the fact.
    /// </summary>
    private static IReadOnlyList<string> FreezeArgv(IReadOnlyList<string> argv)
    {
        return argv is null ? Array.Empty<string>() : new List<string>(argv);
    }

    /// <summary>
    /// Resolve the frozen verifier argv for a cosign version, or <c>null</c> when the
    /// version is not mapped (fail-closed — an unmapped version never floats to a
    /// latest/default argv).
    /// </summary>
    public IReadOnlyList<string>? ArgvForVersion(string version)
    {
        // Fail-closed lookup: a blank version resolves to null (defensive empty-input
        // boundary) and an unmapped version resolves to null — never a floating/latest argv.
        if (string.IsNullOrWhiteSpace(version))
        {
            return null;
        }

        foreach (VersionArgvEntry entry in _entries)
        {
            if (string.Equals(entry.Version, version, StringComparison.Ordinal))
            {
                return entry.Argv;
            }
        }

        return null;
    }
}

/// <summary>
/// The append-only Sigstore trust-root registry model + exact-root selection + receipt
/// binding for P3 determinism-attestation spec INV-016 (trust-root registry / RS-029 /
/// RS-030) — DISTINCT from the carrier's same-numbered INV-016 (.NET SDK pin,
/// <c>Inv016SdkPinTests</c>), which is unrelated and untouched.
///
/// INV-016 statement: the trust root is an APPEND-ONLY registry of immutable, versioned
/// root files (each written once, never overwritten), so historical bundles stay
/// independently verifiable against the exact root current at their signing time. PR2
/// freezes the toolchain/trust artifacts; PR3 (the evidence PR) changes NONE of them.
/// Each signed receipt binds the trust-root registry id + SHA-256 that verifies it
/// (RS-029), so a post-rotation historical baseline selects the EXACT bound root, not the
/// active/latest one and not a heuristic multi-root probe. A cosign version bump is a
/// coupled frozen-asset upgrade along the TOOL axis (RS-030), retaining a
/// version-to-frozen-argv map so historical bundles stay verifiable along that axis too.
///
/// Guards against AP-005 (a frozen asset with no legitimate-change affordance — here the
/// affordance IS the append-only rotation: a NEW versioned root is appended, an existing
/// one is never mutated) and AP-017 (coupled artifacts validated as a fail-closed pair —
/// here the receipt binds BOTH id AND digest, and a disagreement or absence rejects).
///
/// GREEN: every method body below implements the real fail-closed logic — append-only
/// validation (a mutation/removal rejects, an append is Valid), exact v1 root selection (an
/// absent id resolves to null), and the coupled id+digest receipt binding (a tamper or absence
/// rejects).
/// </summary>
public static class TrustRootRegistry
{
    /// <summary>
    /// The set of artifact CLASSES that PR2 freezes and PR3 must not change
    /// (INV-016 statement). Exposed as a set so the frozen SET itself cannot silently
    /// grow or shrink — a set-equality test binds the exact membership.
    /// </summary>
    public static IReadOnlyCollection<string> FrozenArtifactClasses()
    {
        // The EXACT set PR2 freezes (INV-016 statement ~587–589): the cosign version+digest,
        // the active trust-root version+digest, the verifier argv, the OIDC identity policy,
        // the four schemas (receipt/predicate/Statement/manifest), and the subject-manifest
        // classifier rules. A set-equality test binds this membership so it cannot silently
        // grow or shrink.
        return new[]
        {
            "cosign-version+digest",
            "active-trust-root-version+digest",
            "verifier-argv",
            "oidc-identity-policy",
            "receipt-schema",
            "predicate-schema",
            "statement-schema",
            "manifest-schema",
            "subject-manifest-classifier-rules",
        };
    }

    /// <summary>
    /// Validate that <paramref name="newRegistry"/> is a legal APPEND over
    /// <paramref name="oldRegistry"/> (INV-016 / RS-029): valid IFF it contains every old
    /// entry UNCHANGED (same RegistryId → same Sha256), keyed by id not position, plus
    /// zero-or-more appended new entries. Fail closed if any existing entry's Sha256 is
    /// overwritten/mutated, an entry is removed, or an existing id is re-pointed to a new
    /// digest.
    /// </summary>
    public static AppendOnlyResult ValidateAppendOnly(
        IReadOnlyList<TrustRootEntry> oldRegistry,
        IReadOnlyList<TrustRootEntry> newRegistry)
    {
        if (oldRegistry is null)
        {
            return new AppendOnlyResult(false, "old registry is null");
        }

        if (newRegistry is null)
        {
            return new AppendOnlyResult(false, "new registry is null");
        }

        // Build the keyed view of the NEW registry (id -> digest), keyed by id not position
        // so a reorder with identical pairs is valid. A malformed new registry that carries
        // the same id twice with DISAGREEING digests is an in-place re-point smuggled as a
        // duplicate row — fail closed.
        var newById = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (TrustRootEntry entry in newRegistry)
        {
            if (entry is null || string.IsNullOrWhiteSpace(entry.RegistryId))
            {
                return new AppendOnlyResult(false, "new registry contains an entry with a null/blank id");
            }

            if (newById.TryGetValue(entry.RegistryId, out string? seenDigest))
            {
                if (!string.Equals(seenDigest, entry.Sha256, StringComparison.Ordinal))
                {
                    return new AppendOnlyResult(
                        false,
                        $"new registry carries id '{entry.RegistryId}' twice with disagreeing digests (re-point)");
                }

                // Exact-duplicate row — harmless; the keyed digest is unchanged.
                continue;
            }

            newById[entry.RegistryId] = entry.Sha256;
        }

        // Every OLD entry must survive UNCHANGED in the new registry: same id -> same digest.
        // A missing id is a removal; a differing digest is an overwrite/re-point. Both reject.
        foreach (TrustRootEntry oldEntry in oldRegistry)
        {
            if (oldEntry is null || string.IsNullOrWhiteSpace(oldEntry.RegistryId))
            {
                return new AppendOnlyResult(false, "old registry contains an entry with a null/blank id");
            }

            if (!newById.TryGetValue(oldEntry.RegistryId, out string? newDigest))
            {
                return new AppendOnlyResult(
                    false,
                    $"existing root '{oldEntry.RegistryId}' was removed (append-only violation)");
            }

            if (!string.Equals(newDigest, oldEntry.Sha256, StringComparison.Ordinal))
            {
                return new AppendOnlyResult(
                    false,
                    $"existing root '{oldEntry.RegistryId}' was overwritten/re-pointed (write-once violation)");
            }
        }

        // All prior roots retained unchanged, plus zero-or-more appended new roots.
        return new AppendOnlyResult(true, "append-only: all prior roots retained unchanged");
    }

    /// <summary>
    /// Select EXACTLY the registry entry whose <see cref="TrustRootEntry.RegistryId"/>
    /// equals <paramref name="receiptBoundRootId"/> (INV-016 / RS-029) — NOT the
    /// active/latest root, NOT a heuristic multi-root probe. Returns <c>null</c> (fail
    /// closed) when the bound id is absent; NEVER falls back to the active root.
    /// </summary>
    public static TrustRootEntry? SelectRoot(
        IReadOnlyList<TrustRootEntry> registry,
        string receiptBoundRootId)
    {
        // Fail closed on a null registry or a null/blank bound id ("" matches nothing).
        if (registry is null || string.IsNullOrWhiteSpace(receiptBoundRootId))
        {
            return null;
        }

        // EXACT-id selection: the single entry whose RegistryId equals the bound id — NEVER
        // the active/latest/first entry, NEVER a heuristic probe. If a malformed registry
        // carries the bound id twice with disagreeing digests, fail closed rather than pick
        // one arbitrarily.
        TrustRootEntry? match = null;
        foreach (TrustRootEntry entry in registry)
        {
            if (entry is null)
            {
                continue;
            }

            if (string.Equals(entry.RegistryId, receiptBoundRootId, StringComparison.Ordinal))
            {
                if (match is not null && !string.Equals(match.Sha256, entry.Sha256, StringComparison.Ordinal))
                {
                    return null;
                }

                match ??= entry;
            }
        }

        return match;
    }

    /// <summary>
    /// Verify the receipt-to-trust-root binding as a fail-closed pair (INV-016 / RS-029,
    /// AP-017): select the root by the receipt-bound id (per <see cref="SelectRoot"/>) AND
    /// require the selected entry's Sha256 to EQUAL the receipt-bound
    /// <see cref="SignedReceiptRootBinding.TrustRootSha256"/>. A bound digest that
    /// disagrees with the registry entry (tamper) rejects; a bound id that is absent
    /// rejects.
    /// </summary>
    public static RootBindingResult VerifyReceiptRootBinding(
        IReadOnlyList<TrustRootEntry> registry,
        SignedReceiptRootBinding binding)
    {
        if (registry is null)
        {
            return new RootBindingResult(false, "registry is null");
        }

        if (binding is null)
        {
            return new RootBindingResult(false, "binding is null");
        }

        // Select the entry by the BOUND id (fail-closed if absent — no active-root fallback).
        TrustRootEntry? selected = SelectRoot(registry, binding.TrustRootId);
        if (selected is null)
        {
            return new RootBindingResult(
                false,
                $"no registry entry for bound trust-root id '{binding.TrustRootId}'");
        }

        // Coupled fail-closed pair (AP-017): the bound digest must equal the digest of the
        // entry SELECTED BY THE BOUND ID — NOT any registry digest. A cross-wired binding
        // (v1's id + v2's digest, both present but on different entries) rejects here because
        // the SELECTED entry is v1's, whose digest disagrees with the bound v2 digest.
        if (!string.Equals(selected.Sha256, binding.TrustRootSha256, StringComparison.Ordinal))
        {
            return new RootBindingResult(
                false,
                $"bound digest disagrees with the registry entry selected for '{binding.TrustRootId}' (tamper)");
        }

        return new RootBindingResult(true, "bound id resolves and its digest matches the selected entry");
    }

    // -------------------------------------------------------------------------------------
    // DEFERRED RESIDUAL (Track T3/T4 — NOT built here; needs real cosign + committed bundle
    // fixtures + crypto verify, which is out of scope for this buildable-now synthetic model):
    //
    //   * INV-016 / RS-029 post-rotation historical-bundle-UNDER-v1 crypto VERIFY test — after
    //     a rotation to root v2, an old baseline signed under v1 must actually cosign-verify
    //     against the pinned v1 trusted_root.json (not merely SELECT v1, which SelectRoot above
    //     models synthetically). Requires a committed historical bundle fixture + real cosign.
    //
    //   * INV-016 / RS-030 version-bump gate that RE-VERIFIES every committed historical bundle
    //     under the new cosign pin (or re-mints it). Requires real cosign at both the old and new
    //     pinned versions + committed bundle fixtures.
    //
    // The PR3 "changes none of them" parsed-span diff enforcement is ALREADY the PRH-007
    // classifier (Prh007PrClassifierTests / the parsed-span check) — referenced by INV-016's
    // Enforcement clause, NOT rebuilt here.
    // -------------------------------------------------------------------------------------
}
