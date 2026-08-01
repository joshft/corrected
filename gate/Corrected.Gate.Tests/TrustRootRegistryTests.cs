using System;
using System.Collections.Generic;
using System.Linq;
using Corrected.Gate;
using Xunit;

namespace Corrected.Gate.Tests;

/// <summary>
/// p3-determinism-attestation spec INV-016 (trust-root registry / RS-029 / RS-030), DISTINCT
/// from the carrier's same-numbered SDK-pin invariant (<see cref="Inv016SdkPinTests"/> — the
/// .NET SDK pin, left untouched). This file exercises the Sigstore TRUST-ROOT registry model:
/// the append-only registry, exact-root selection from the receipt-bound id, the
/// receipt→trust-root binding (id + digest, fail-closed pair), and the version→frozen-argv map.
///
/// INV-016 (spec ~581–609): the trust root is an APPEND-ONLY registry of immutable, versioned
/// root files — each written once, never overwritten — so historical bundles stay independently
/// verifiable against the exact root current at their signing time. Each signed receipt binds
/// the trust-root registry id + SHA-256 that verifies it (RS-029): after a rotation to root v2,
/// an old baseline signed under v1 must select EXACTLY v1 — exact-root selection from the
/// receipt-bound id, NOT "try only the active root" and NOT a heuristic multi-root probe. The
/// cosign-version bump is a coupled frozen-asset upgrade along the TOOL axis (RS-030), retaining
/// a version→frozen-argv map so historical bundles stay verifiable along that axis too.
/// Guards against AP-005 (a frozen asset whose only legitimate change is the append-only
/// rotation) and AP-017 (coupled artifacts validated as a fail-closed pair — id AND digest).
///
/// RED-phase asymmetry (against the deny-by-default <see cref="TrustRootRegistry"/> stub):
///   * POSITIVE cells (append/reorder/identity Valid, exact v1 selection, accepted binding,
///     argv resolves, frozen-set equality) FAIL as ASSERTIONS — the stub denies/returns null.
///   * NEGATIVE fail-closed cells (mutation/removal/re-point reject, absent-id → null with no
///     active-root fallback, tamper reject, unmapped version → null) PASS on the deny stub.
///
/// SCOPE — all SYNTHETIC (no real cosign, no bundles, no crypto verify). The real-cosign
/// post-rotation historical-bundle-UNDER-v1 VERIFY test and the version-bump gate that
/// re-verifies every committed historical bundle are DEFERRED (Track T3/T4) — see the residual
/// comment at the foot of <see cref="TrustRootRegistry"/>. The PR3 "changes none" parsed-span
/// enforcement is the already-built PRH-007 classifier, referenced but not rebuilt here.
/// </summary>
public class TrustRootRegistryTests
{
    // Synthetic versioned root fixtures (RegistryId → SHA-256). Digests are illustrative
    // 64-hex placeholders — no crypto is exercised; identity/equality is all that matters.
    private const string V1Id = "trusted_root/v1";
    private const string V1Sha = "1111111111111111111111111111111111111111111111111111111111111111";
    private const string V2Id = "trusted_root/v2";
    private const string V2Sha = "2222222222222222222222222222222222222222222222222222222222222222";
    private const string V3Id = "trusted_root/v3";
    private const string V3Sha = "3333333333333333333333333333333333333333333333333333333333333333";

    private static TrustRootEntry V1 => new(V1Id, V1Sha);
    private static TrustRootEntry V2 => new(V2Id, V2Sha);
    private static TrustRootEntry V3 => new(V3Id, V3Sha);

    private static List<TrustRootEntry> Registry(params TrustRootEntry[] entries) => entries.ToList();

    // =====================================================================================
    // 1. Append-only registry model (INV-016 / RS-029): valid iff every old entry is present
    //    UNCHANGED (keyed by id) plus zero-or-more appended entries.
    // =====================================================================================

    // Tests INV-016 [unit]: appending a new v2 root to {v1} is a legal append → Valid.
    // POSITIVE — fails as an assertion against the deny stub.
    [Fact]
    public void AppendOnly_appending_new_v2_root_is_valid()
    {
        var old = Registry(V1);
        var updated = Registry(V1, V2);

        AppendOnlyResult result = TrustRootRegistry.ValidateAppendOnly(old, updated);

        Assert.True(result.Valid, $"appending v2 to {{v1}} must be valid; reason: {result.Reason}");
    }

    // Tests INV-016 [unit]: appending the FIRST root to an empty registry → Valid (defensive
    // empty-input boundary). POSITIVE — fails as an assertion against the deny stub.
    [Fact]
    public void AppendOnly_first_root_into_empty_registry_is_valid()
    {
        var old = Registry();
        var updated = Registry(V1);

        AppendOnlyResult result = TrustRootRegistry.ValidateAppendOnly(old, updated);

        Assert.True(result.Valid, $"appending the first root into {{}} must be valid; reason: {result.Reason}");
    }

    // Tests INV-016 [unit]: an unchanged registry (zero appended) is a legal no-op append →
    // Valid. POSITIVE — fails as an assertion against the deny stub.
    [Fact]
    public void AppendOnly_identical_registry_is_valid()
    {
        var old = Registry(V1, V2);
        var updated = Registry(V1, V2);

        AppendOnlyResult result = TrustRootRegistry.ValidateAppendOnly(old, updated);

        Assert.True(result.Valid, $"an identical registry must be valid; reason: {result.Reason}");
    }

    // Tests INV-016 [unit]: reordering with IDENTICAL id→digest pairs is still Valid — the
    // registry is keyed by id, not position, so the impl must NOT over-reject a permutation.
    // POSITIVE — fails as an assertion against the deny stub.
    [Fact]
    public void AppendOnly_reordering_with_identical_pairs_is_valid()
    {
        var old = Registry(V1, V2);
        var updated = Registry(V2, V1); // same pairs, different order

        AppendOnlyResult result = TrustRootRegistry.ValidateAppendOnly(old, updated);

        Assert.True(result.Valid, $"a reordering with identical pairs must be valid (keyed by id); reason: {result.Reason}");
    }

    // Tests INV-016 [unit]: overwriting an existing entry's Sha256 (same id, new digest) is a
    // MUTATION of a write-once root file → reject. NEGATIVE — passes on the deny stub.
    [Fact]
    public void AppendOnly_overwriting_existing_sha256_is_rejected()
    {
        var old = Registry(V1);
        var mutated = new TrustRootEntry(V1Id, V2Sha); // same id, different digest
        var updated = Registry(mutated);

        AppendOnlyResult result = TrustRootRegistry.ValidateAppendOnly(old, updated);

        Assert.False(result.Valid, "overwriting an existing root file's Sha256 must be rejected (write-once)");
    }

    // Tests INV-016 [unit]: re-pointing an existing id to a different digest WHILE also appending
    // a legit new root must still reject — a smuggled mutation is not laundered by a valid append.
    // NEGATIVE — passes on the deny stub.
    [Fact]
    public void AppendOnly_repointing_existing_id_alongside_append_is_rejected()
    {
        var old = Registry(V1);
        var repointed = new TrustRootEntry(V1Id, V3Sha); // v1 re-pointed to a new digest
        var updated = Registry(repointed, V2);            // ...alongside a legitimate v2 append

        AppendOnlyResult result = TrustRootRegistry.ValidateAppendOnly(old, updated);

        Assert.False(result.Valid, "re-pointing an existing id must be rejected even alongside a valid append");
    }

    // Tests INV-016 [unit]: removing an existing entry (the new registry drops v1) → reject.
    // NEGATIVE — passes on the deny stub.
    [Fact]
    public void AppendOnly_removing_existing_entry_is_rejected()
    {
        var old = Registry(V1, V2);
        var updated = Registry(V2); // v1 dropped

        AppendOnlyResult result = TrustRootRegistry.ValidateAppendOnly(old, updated);

        Assert.False(result.Valid, "removing an existing root entry must be rejected (append-only)");
    }

    // =====================================================================================
    // 2. Exact-root selection from the receipt-bound id (INV-016 / RS-029) — load-bearing:
    //    select EXACTLY the bound id, never the active/latest root, never a heuristic probe.
    // =====================================================================================

    // Tests INV-016 [integration]: registry {v1, v2}, a receipt bound to v1 → SelectRoot returns
    // the v1 entry, NOT the latest v2. POSITIVE — fails as an assertion against the deny stub.
    // This is the RS-029 post-rotation "old baseline selects v1, not the active root" core.
    [Fact]
    public void SelectRoot_returns_the_receipt_bound_v1_not_the_latest_v2()
    {
        var registry = Registry(V1, V2); // v2 is the active/latest root

        TrustRootEntry? selected = TrustRootRegistry.SelectRoot(registry, V1Id);

        Assert.NotNull(selected);
        Assert.Equal(V1Id, selected!.RegistryId);
        Assert.Equal(V1Sha, selected.Sha256);
        Assert.NotEqual(V2Id, selected.RegistryId); // never the active/latest root
    }

    // Tests INV-016 [integration]: a receipt bound to an ABSENT id (v3) → null; and it must NOT
    // fall back to the active/latest root v2. NEGATIVE fail-closed — passes on the deny stub.
    [Fact]
    public void SelectRoot_absent_bound_id_returns_null_and_never_falls_back_to_active_root()
    {
        var registry = Registry(V1, V2); // v2 active/latest

        TrustRootEntry? selected = TrustRootRegistry.SelectRoot(registry, V3Id);

        Assert.Null(selected); // fail closed — no active-root fallback
    }

    // Tests INV-016 [unit]: a blank bound id → null (defensive empty-input boundary; the bound
    // id must be an exact match, and "" matches nothing). NEGATIVE — passes on the deny stub.
    [Fact]
    public void SelectRoot_blank_bound_id_returns_null()
    {
        var registry = Registry(V1, V2);

        TrustRootEntry? selected = TrustRootRegistry.SelectRoot(registry, string.Empty);

        Assert.Null(selected);
    }

    // =====================================================================================
    // 3. Receipt→trust-root binding (INV-016 / RS-029, AP-017): the bound (id, digest) pair is
    //    validated fail-closed — select by id AND require the digest to match the registry entry.
    // =====================================================================================

    // Tests INV-016 [integration]: a binding whose TrustRootSha256 matches the registry entry for
    // its id → accept. POSITIVE — fails as an assertion against the deny stub.
    [Fact]
    public void VerifyReceiptRootBinding_matching_id_and_digest_is_accepted()
    {
        var registry = Registry(V1, V2);
        var binding = new SignedReceiptRootBinding(V1Id, V1Sha); // id present, digest agrees

        RootBindingResult result = TrustRootRegistry.VerifyReceiptRootBinding(registry, binding);

        Assert.True(result.Accepted, $"a matching id+digest binding must be accepted; reason: {result.Reason}");
    }

    // Tests INV-016 [integration]: a binding whose id EXISTS but whose TrustRootSha256 DISAGREES
    // with the registry entry (tamper) → reject. NEGATIVE fail-closed — passes on the deny stub.
    [Fact]
    public void VerifyReceiptRootBinding_disagreeing_digest_is_rejected_as_tamper()
    {
        var registry = Registry(V1, V2);
        var binding = new SignedReceiptRootBinding(V1Id, V3Sha); // id present, digest tampered

        RootBindingResult result = TrustRootRegistry.VerifyReceiptRootBinding(registry, binding);

        Assert.False(result.Accepted, "a bound digest disagreeing with the registry entry must be rejected (tamper)");
    }

    // Tests INV-016 [integration]: a binding whose bound id is ABSENT from the registry → reject.
    // NEGATIVE fail-closed — passes on the deny stub.
    [Fact]
    public void VerifyReceiptRootBinding_absent_bound_id_is_rejected()
    {
        var registry = Registry(V1, V2);
        var binding = new SignedReceiptRootBinding(V3Id, V3Sha); // id not in registry

        RootBindingResult result = TrustRootRegistry.VerifyReceiptRootBinding(registry, binding);

        Assert.False(result.Accepted, "a binding whose id is absent from the registry must be rejected");
    }

    // Tests INV-016 [integration]: a CROSS-WIRED binding — v1's id presenting v2's digest, where
    // BOTH the id AND the digest exist in the registry but belong to DIFFERENT entries — must be
    // rejected (AP-017). This defeats a non-coupled impl that only checks "id present somewhere"
    // AND "digest present somewhere" independently (which would accept this cross-wire); the digest
    // must match the entry SELECTED by the bound id, not any registry digest. NEGATIVE
    // fail-closed — passes on the deny stub.
    [Fact]
    public void VerifyReceiptRootBinding_id_bound_to_another_entrys_digest_is_rejected()
    {
        var registry = Registry(V1, V2);
        var binding = new SignedReceiptRootBinding(V1Id, V2Sha); // v1's id, v2's digest — cross-wired; both present

        RootBindingResult result = TrustRootRegistry.VerifyReceiptRootBinding(registry, binding);

        Assert.False(result.Accepted,
            "the bound digest must match the SelectRoot-resolved entry (v1's), not any registry digest (v2's)");
    }

    // =====================================================================================
    // 4. Version→frozen-argv map (INV-016 / RS-030): append-only along the TOOL axis — a bump
    //    appends a new version→argv, retaining prior versions' argv; unmapped → fail-closed null.
    // =====================================================================================

    private const string CosignV1 = "v3.1.2";
    private const string CosignV2 = "v3.2.0";
    private static readonly IReadOnlyList<string> ArgvV1 =
        new[] { "verify-blob-attestation", "--certificate-oidc-issuer", "https://token.actions.githubusercontent.com" };
    private static readonly IReadOnlyList<string> ArgvV2 =
        new[] { "verify-blob-attestation", "--new-required-flag", "--certificate-oidc-issuer", "https://token.actions.githubusercontent.com" };

    // Tests INV-016 [unit]: ArgvForVersion returns the frozen argv for a mapped version.
    // POSITIVE — fails as an assertion against the deny stub (which returns null).
    [Fact]
    public void ArgvForVersion_mapped_version_returns_the_frozen_argv()
    {
        var map = new VersionArgvMap(new[] { new VersionArgvEntry(CosignV1, ArgvV1) });

        IReadOnlyList<string>? argv = map.ArgvForVersion(CosignV1);

        Assert.NotNull(argv);
        Assert.Equal(ArgvV1, argv);
    }

    // Tests INV-016 [unit]: a version bump APPENDS v-new while RETAINING v-old's argv, so BOTH
    // resolve (RS-030 tool-axis retention — historical bundles stay verifiable across the bump).
    // POSITIVE — fails as an assertion against the deny stub.
    [Fact]
    public void ArgvForVersion_bump_appends_new_version_retaining_old()
    {
        var map = new VersionArgvMap(new[] { new VersionArgvEntry(CosignV1, ArgvV1) });

        VersionArgvMap bumped = map.Append(CosignV2, ArgvV2);

        IReadOnlyList<string>? oldArgv = bumped.ArgvForVersion(CosignV1);
        IReadOnlyList<string>? newArgv = bumped.ArgvForVersion(CosignV2);

        Assert.NotNull(oldArgv);
        Assert.Equal(ArgvV1, oldArgv); // v-old argv retained across the bump
        Assert.NotNull(newArgv);
        Assert.Equal(ArgvV2, newArgv); // v-new argv resolves
    }

    // Tests INV-016 [unit]: an unmapped version → null (fail-closed; never a floating/latest
    // argv). NEGATIVE — passes on the deny stub.
    [Fact]
    public void ArgvForVersion_unmapped_version_returns_null()
    {
        var map = new VersionArgvMap(new[] { new VersionArgvEntry(CosignV1, ArgvV1) });

        IReadOnlyList<string>? argv = map.ArgvForVersion("v9.9.9-unmapped");

        Assert.Null(argv);
    }

    // Tests INV-016 [unit]: a blank version → null (defensive empty-input boundary).
    // NEGATIVE — passes on the deny stub.
    [Fact]
    public void ArgvForVersion_blank_version_returns_null()
    {
        var map = new VersionArgvMap(new[] { new VersionArgvEntry(CosignV1, ArgvV1) });

        IReadOnlyList<string>? argv = map.ArgvForVersion(string.Empty);

        Assert.Null(argv);
    }

    // Tests INV-016 [unit]: attempting to re-point an ALREADY-MAPPED version's frozen argv to a
    // DIFFERENT value must not mutate it — an existing version's argv is frozen along the TOOL axis
    // (RS-030), the tool-axis analog of the append-only registry re-point rejection. The frozen
    // argv wins: after the re-point attempt, CosignV1 still resolves to its original argv.
    // This cell is RED against the deny stub (Append returns an empty map, so the retained argv is
    // null) — expected; it is the positive-ish retention assertion of the RS-030 mutation guard.
    [Fact]
    public void Append_repointing_an_already_mapped_version_leaves_the_frozen_argv_unchanged()
    {
        var map = new VersionArgvMap(new[] { new VersionArgvEntry(CosignV1, ArgvV1) });

        // Re-point CosignV1 (already mapped to ArgvV1) to a DIFFERENT argv (ArgvV2).
        VersionArgvMap after = map.Append(CosignV1, ArgvV2);

        // The frozen argv must be unchanged — the re-point is not honored (fail-closed retention).
        Assert.Equal(ArgvV1, after.ArgvForVersion(CosignV1));
    }

    // =====================================================================================
    // 5. (Optional) Frozen-artifact set (INV-016): PR2 freezes a fixed SET of artifact classes;
    //    a set-equality binds the exact membership so the frozen SET cannot silently grow/shrink.
    // =====================================================================================

    // The exact set PR2 freezes, per the INV-016 statement (~587–589): the cosign version+digest,
    // the active trust-root version+digest, verifier argv, OIDC identity policy, the
    // receipt+predicate+Statement+manifest schemas (four), and the subject-manifest classifier
    // rules. Derived from the SPEC prose, not from a production literal, so shrinking the frozen
    // set in production surfaces as a reviewable test failure.
    private static readonly string[] ExpectedFrozenClasses =
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

    // Tests INV-016 [unit]: the frozen-artifact SET is EXACTLY the pinned membership (set-equality,
    // not a count/presence proxy — a missing OR extra class fails). POSITIVE — fails as an
    // assertion against the empty-set deny stub.
    [Fact]
    public void FrozenArtifactClasses_are_exactly_the_pinned_set()
    {
        var actual = new HashSet<string>(TrustRootRegistry.FrozenArtifactClasses());
        var expected = new HashSet<string>(ExpectedFrozenClasses);

        // Set-equality (both directions) — catches a silently shrunk OR grown frozen set.
        Assert.True(expected.SetEquals(actual),
            "frozen-artifact class set drifted. missing=[" +
            string.Join(",", expected.Except(actual)) + "] extra=[" +
            string.Join(",", actual.Except(expected)) + "]");
    }
}
