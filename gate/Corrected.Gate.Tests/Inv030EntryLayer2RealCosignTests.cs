using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Corrected.Gate.Kernel;
using Corrected.Provenance.Entry;
using Xunit;

namespace Corrected.Gate.Tests;

/// <summary>
/// P3 phase-entry INV-030 (Group G / MA-C part d) LAYER 2 — the REAL pinned-cosign integration
/// against the COMMITTED entry fixture bundles (<c>test/attestations/fixtures/entry/{pos,shaneg}/**</c>),
/// the entry analog of <see cref="Inv010Inv011Layer2RealCosignTests"/>. It drives the full
/// <see cref="EntryVerifier.Verify"/> real-cosign path (NOT a stub/always-pass double — AP-012) and
/// asserts the fixture-identity POSITIVE, the 2a production-identity-mismatch negative, the 2b
/// cert-SHA cross-check negative (SHANEG), and the reason-specific crypto negatives.
///
/// The two fixtures were minted 2026-08-01 (throwaway workflow p3-entry-fixture-sign.yml, run
/// 30707624686, commit 25db9a3, since torn down) under the FIXTURE identity:
///   * POS    — commit-X == the fixture run commit (== EntryVerifyIdentity.FixtureCertWorkflowSha);
///              verifies POSITIVE and cross-checks EQUAL.
///   * SHANEG — commit-X == 0000…0000 (!= the fixture cert workflow-SHA); genuine crypto, but the 2b
///              cross-check REJECTS.
///
/// LOCATING THE PROVISIONED COSIGN (the env seam, mirroring the determinism L2 test): the gate
/// command exports COSIGN_BIN + TRUSTED_ROOT before the offline verify. RS-015 / AP-013 — NEVER A
/// SILENT SKIP: when cosign is genuinely unavailable (air-gapped / off-RID), each cell records a
/// TYPED reason via the real Verify path (verifier-unavailable / a non-Verified reject), never a
/// [Fact(Skip)].
///
/// [Collection("Subprocess")] — these fork/exec real cosign.
/// </summary>
[Collection("Subprocess")]
public class Inv030EntryLayer2RealCosignTests
{
    private const string FixtureCommit = "25db9a3cca316e6afd1d33df98f5596ea0cb2dba";
    private const string ShanegCommit = "0000000000000000000000000000000000000000";

    // ================= section A/B — the real Verify cells =================

    // Tests INV-030 [integration] (LAYER 2 POSITIVE): the genuine POS entry bundle, driven through the
    // full real-cosign Verify under the FIXTURE identity, VERIFIES; AND the decoded DSSE payload
    // byte-equals the committed statement; AND sha256(commit blob) == the signed subjects[0] digest.
    [Fact]
    public void Pos_fixture_verifies_positive()
    {
        // Fixture-honesty (not gated on cosign): the committed bundle payload IS the committed
        // statement, and the commit blob binds subjects[0].
        byte[] payload = DecodeDssePayload(FixtureFile("pos", "entry.sigstore.json"));
        byte[] statement = File.ReadAllBytes(FixtureFile("pos", "entry-statement.json"));
        Assert.Equal(statement, payload);
        Assert.Equal(FixtureCommit, File.ReadAllText(FixtureFile("pos", "entry-commit.blob")).Trim());

        var rc = RealCosign.Resolve();
        using var fx = EntryFixtureCopy.Of("pos");
        EntryVerifyRequest req = rc.EntryRequest(fx, EntryVerifyIdentity.Fixture, FixtureCommit, AncestryStatus.Ancestor);

        EntryVerifyResult r = EntryVerifier.Verify(req);

        if (!rc.Provisioned) { AssertHonestUnavailableFallback(r); return; }
        Assert.Equal(EntryIntegrity.Verified, r.Integrity);
        Assert.True(r.Satisfied);
        Assert.Null(r.Reason);
    }

    // Tests INV-030 [integration] (2a — production identity): the genuine POS bundle driven through the
    // exact PRODUCTION --certificate-identity is REJECTED with IdentityMismatch SPECIFICALLY. The
    // production ACCEPT branch stays a recorded residual (RS-006/RS-011).
    [Fact]
    public void Pos_through_production_identity_rejects_identity_mismatch()
    {
        var rc = RealCosign.Resolve();
        using var fx = EntryFixtureCopy.Of("pos");
        EntryVerifyRequest req = rc.EntryRequest(fx, EntryVerifyIdentity.Production, FixtureCommit, AncestryStatus.Ancestor);

        EntryVerifyResult r = EntryVerifier.Verify(req);

        if (!rc.Provisioned) { AssertHonestUnavailableFallback(r); return; }
        Assert.Equal(EntryIntegrity.Rejected, r.Integrity);
        Assert.Equal(EntryVerifyReason.IdentityMismatch, r.Reason);
    }

    // Tests INV-030 [integration] (2b — the cert-SHA cross-check, reached only once identity passed):
    // the SHANEG bundle (genuine crypto, commit-X 0000… != cert workflow-SHA 25db9a3) driven through
    // the fixture-ACCEPTING argv (fixture identity + the fixture's frozen workflow-SHA so cosign
    // accepts) is REJECTED attributable SPECIFICALLY to the Corrected-side cert-SHA <-> commit-X
    // cross-check (CertWorkflowShaMismatch) — DISTINCT from IdentityMismatch.
    [Fact]
    public void Shaneg_through_fixture_accepting_argv_rejects_cert_sha_cross_check()
    {
        // Precondition (AP-010): the SHANEG fixture genuinely embeds the mismatch.
        Assert.Equal(ShanegCommit, File.ReadAllText(FixtureFile("shaneg", "entry-commit.blob")).Trim());
        Assert.NotEqual(EntryVerifyIdentity.FixtureCertWorkflowSha, ShanegCommit);

        var rc = RealCosign.Resolve();
        using var fx = EntryFixtureCopy.Of("shaneg");
        // fixture-ACCEPTING: fixture identity + the fixture's frozen workflow-SHA so cosign accepts
        // SHANEG's genuine crypto; Corrected then cross-checks 25db9a3 != commit-X(0000).
        EntryVerifyRequest req = rc.EntryRequest(fx, EntryVerifyIdentity.Fixture, FixtureCommit, AncestryStatus.Ancestor);

        EntryVerifyResult r = EntryVerifier.Verify(req);

        if (!rc.Provisioned) { AssertHonestUnavailableFallback(r); return; }
        Assert.Equal(EntryIntegrity.Rejected, r.Integrity);
        Assert.Equal(EntryVerifyReason.CertWorkflowShaMismatch, r.Reason);
    }

    // Tests INV-030 [integration] (crypto negative — tampered DSSE payload -> signature-invalid): a POS
    // bundle with ONE flipped base64 payload byte is REJECTED with SignatureInvalid.
    [Fact]
    public void Tampered_dsse_payload_rejects_signature_invalid()
    {
        var rc = RealCosign.Resolve();
        using var fx = EntryFixtureCopy.Of("pos", tamperBundle: TamperFlipPayloadByte);
        EntryVerifyRequest req = rc.EntryRequest(fx, EntryVerifyIdentity.Fixture, FixtureCommit, AncestryStatus.Ancestor);

        EntryVerifyResult r = EntryVerifier.Verify(req);

        if (!rc.Provisioned) { AssertHonestUnavailableFallback(r); return; }
        Assert.Equal(EntryIntegrity.Rejected, r.Integrity);
        Assert.Equal(EntryVerifyReason.SignatureInvalid, r.Reason);
    }

    // Tests INV-030 [integration] (crypto negative — wrong predicate-type ARGV -> predicate-type-
    // mismatch): the intact POS bundle driven with a WRONG --type argv (the bundle is NOT mutated) is
    // REJECTED with PredicateTypeMismatch. cosign reports "invalid predicate type".
    [Fact]
    public void Wrong_predicate_type_argv_rejects_predicate_type_mismatch()
    {
        var rc = RealCosign.Resolve();
        using var fx = EntryFixtureCopy.Of("pos");
        EntryVerifyIdentity wrongType =
            EntryVerifyIdentity.Fixture with { PredicateType = "https://correctless.org/attestations/WRONG/v9" };
        EntryVerifyRequest req = rc.EntryRequest(fx, wrongType, FixtureCommit, AncestryStatus.Ancestor);

        EntryVerifyResult r = EntryVerifier.Verify(req);

        if (!rc.Provisioned) { AssertHonestUnavailableFallback(r); return; }
        Assert.Equal(EntryIntegrity.Rejected, r.Integrity);
        Assert.Equal(EntryVerifyReason.PredicateTypeMismatch, r.Reason);
    }

    // Tests INV-030 [integration] (crypto negative — commit blob whose sha256 != subjects[0] ->
    // subject-digest-mismatch): the commit blob is byte-mutated (a trailing space appended) so
    // sha256(blob) no longer equals the signed commit subject; cosign --check-claims reports "provided
    // artifact digests do not match".
    [Fact]
    public void Commit_blob_sha_not_matching_subject_rejects_subject_digest_mismatch()
    {
        var rc = RealCosign.Resolve();
        using var fx = EntryFixtureCopy.Of("pos", tamperBlob: bytes => bytes.Concat(new byte[] { (byte)' ' }).ToArray());
        EntryVerifyRequest req = rc.EntryRequest(fx, EntryVerifyIdentity.Fixture, FixtureCommit, AncestryStatus.Ancestor);

        EntryVerifyResult r = EntryVerifier.Verify(req);

        if (!rc.Provisioned) { AssertHonestUnavailableFallback(r); return; }
        Assert.Equal(EntryIntegrity.Rejected, r.Integrity);
        Assert.Equal(EntryVerifyReason.SubjectDigestMismatch, r.Reason);
    }

    // ================= meta-assert — no committed entry fixture carries the production identity ======

    // Tests INV-030 [integration] (meta-assertion): scan test/attestations/fixtures/entry/** — EVERY
    // committed entry bundle's leaf certificate SAN is the FIXTURE identity, NEVER the production
    // identity (…/p3-entry-sign.yml@refs/heads/main). Hermetic: decode the cert bytes and search.
    [Fact]
    public void No_committed_entry_fixture_carries_the_production_identity()
    {
        string root = TestPaths.RepoFile("test", "attestations", "fixtures", "entry");
        var bundles = Directory.EnumerateFiles(root, "entry.sigstore.json", SearchOption.AllDirectories).ToList();
        Assert.NotEmpty(bundles); // AP-010: not vacuous.

        const string productionSanToken = "p3-entry-sign.yml@refs/heads/main";
        const string fixtureSanToken = "p3-entry-fixture-sign.yml@refs/heads/fixture/p3-entry-bundle";

        foreach (string bundlePath in bundles)
        {
            string certAscii = Encoding.Latin1.GetString(LeafCertBytes(bundlePath));
            Assert.DoesNotContain(productionSanToken, certAscii, StringComparison.Ordinal);
            Assert.Contains(fixtureSanToken, certAscii, StringComparison.Ordinal);
        }
    }

    // ---------------------------------------------------------------------------------------------
    // helpers
    // ---------------------------------------------------------------------------------------------

    private static string FixtureFile(string which, string name)
        => TestPaths.RepoFile("test", "attestations", "fixtures", "entry", which, name);

    private static byte[] DecodeDssePayload(string bundlePath)
    {
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllBytes(bundlePath));
        string b64 = doc.RootElement.GetProperty("dsseEnvelope").GetProperty("payload").GetString()!;
        return Convert.FromBase64String(b64);
    }

    private static byte[] LeafCertBytes(string bundlePath)
    {
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllBytes(bundlePath));
        var found = new List<string>();
        void Walk(JsonElement el)
        {
            switch (el.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (JsonProperty p in el.EnumerateObject())
                    {
                        if ((p.Name == "rawBytes" || p.Name == "certificate") && p.Value.ValueKind == JsonValueKind.String)
                        {
                            found.Add(p.Value.GetString()!);
                        }
                        Walk(p.Value);
                    }
                    break;
                case JsonValueKind.Array:
                    foreach (JsonElement e in el.EnumerateArray()) Walk(e);
                    break;
            }
        }
        Walk(doc.RootElement.GetProperty("verificationMaterial"));
        Assert.NotEmpty(found);

        var bytes = new List<byte>();
        foreach (string b64 in found)
        {
            try { bytes.AddRange(Convert.FromBase64String(b64)); } catch (FormatException) { }
        }
        return bytes.ToArray();
    }

    private static string TamperFlipPayloadByte(string bundleJson)
    {
        JsonObject b = (JsonObject)JsonNode.Parse(bundleJson)!;
        var env = (JsonObject)b["dsseEnvelope"]!;
        string payload = (string)env["payload"]!;
        char c = payload[20];
        char flipped = c == 'A' ? 'B' : 'A';
        env["payload"] = payload.Substring(0, 20) + flipped + payload.Substring(21);
        return b.ToJsonString();
    }

    private static void AssertHonestUnavailableFallback(EntryVerifyResult r)
    {
        // RS-015 / AP-013: a genuinely degraded env records a TYPED reason, never a silent skip; the
        // outcome must never be Verified (fail-closed).
        Assert.NotEqual(EntryIntegrity.Verified, r.Integrity);
        Assert.False(r.Satisfied);
    }

    // ---- EntryFixtureCopy: a temp-dir copy of a committed entry fixture (for in-place mutation) ----

    private sealed class EntryFixtureCopy : IDisposable
    {
        internal required string Dir { get; init; }
        internal required string BundlePath { get; init; }
        internal required string ReceiptPath { get; init; }

        internal static EntryFixtureCopy Of(
            string which,
            Func<string, string>? tamperBundle = null,
            Func<byte[], byte[]>? tamperBlob = null)
        {
            string dir = Path.Combine(Path.GetTempPath(), "inv030-entry-l2-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);

            string bundle = Path.Combine(dir, "entry.sigstore.json");
            string src = File.ReadAllText(FixtureFile(which, "entry.sigstore.json"));
            File.WriteAllText(bundle, tamperBundle is null ? src : tamperBundle(src));

            string receipt = Path.Combine(dir, "entry-commit.blob");
            byte[] rb = File.ReadAllBytes(FixtureFile(which, "entry-commit.blob"));
            File.WriteAllBytes(receipt, tamperBlob is null ? rb : tamperBlob(rb));

            return new EntryFixtureCopy { Dir = dir, BundlePath = bundle, ReceiptPath = receipt };
        }

        public void Dispose()
        {
            try { if (Directory.Exists(Dir)) Directory.Delete(Dir, recursive: true); } catch { }
        }
    }

    // ---- RealCosign: the env-seam locator (COSIGN_BIN + TRUSTED_ROOT), mirror of the determinism L2 ----

    private sealed class RealCosign
    {
        internal string? CosignBinPath { get; init; }
        internal string? TrustRootPath { get; init; }
        internal bool HostIsLinuxX64 { get; init; }

        internal bool Provisioned =>
            HostIsLinuxX64
            && CosignBinPath is not null && File.Exists(CosignBinPath)
            && TrustRootPath is not null && File.Exists(TrustRootPath);

        internal EntryVerifyRequest EntryRequest(
            EntryFixtureCopy fx, EntryVerifyIdentity identity, string certWorkflowSha, AncestryStatus ancestry)
            => new()
            {
                CosignBinPath = CosignBinPath ?? "/nonexistent/pinned/cosign",
                BundlePath = fx.BundlePath,
                ReceiptPath = fx.ReceiptPath,
                TrustRootPath = TrustRootPath ?? Path.Combine(fx.Dir, "trusted_root.json"),
                WorkingDirectory = fx.Dir,
                Identity = identity,
                CertWorkflowSha = certWorkflowSha,
                CommitAncestry = ancestry,
                Timeout = TimeSpan.FromSeconds(60),
            };

        internal static RealCosign Resolve()
        {
            bool linuxX64 = RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
                && RuntimeInformation.OSArchitecture == Architecture.X64;

            string? bin = FirstReadable(
                Environment.GetEnvironmentVariable("COSIGN_BIN"),
                Path.Combine(Home(), ".cache", "cosign", "v3.1.2", "cosign-linux-amd64"));
            if (bin is null && linuxX64)
            {
                bin = TryProvisionCosign();
            }

            string sigstoreRoot =
                Path.Combine(Home(), ".sigstore", "root", "tuf-repo-cdn.sigstore.dev", "targets", "trusted_root.json");
            string? root = FirstReadable(Environment.GetEnvironmentVariable("TRUSTED_ROOT"), sigstoreRoot);
            if (root is null && bin is not null && linuxX64)
            {
                root = TryInitializeTrustedRoot(bin, sigstoreRoot);
            }

            return new RealCosign { CosignBinPath = bin, TrustRootPath = root, HostIsLinuxX64 = linuxX64 };
        }

        private static string? TryInitializeTrustedRoot(string cosignBin, string sigstoreRoot)
        {
            try
            {
                if (File.Exists(sigstoreRoot)) return sigstoreRoot;
                var psi = new ProcessStartInfo(cosignBin) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
                psi.ArgumentList.Add("initialize");
                using var p = Process.Start(psi)!;
                p.StandardOutput.ReadToEnd();
                p.StandardError.ReadToEnd();
                if (!p.WaitForExit(120_000)) { try { p.Kill(true); } catch { } return null; }
                return File.Exists(sigstoreRoot) ? sigstoreRoot : null;
            }
            catch { return null; }
        }

        private static string? TryProvisionCosign()
        {
            try
            {
                string script = TestPaths.RepoFile("gate", "tools", "provision-cosign.sh");
                if (!File.Exists(script)) return null;
                string dest = Path.Combine(Home(), ".cache", "cosign", "v3.1.2", "cosign-linux-amd64");
                var psi = new ProcessStartInfo("bash") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, WorkingDirectory = TestPaths.RepoRoot() };
                psi.ArgumentList.Add(script);
                psi.ArgumentList.Add("linux-x64");
                psi.ArgumentList.Add(dest);
                using var p = Process.Start(psi)!;
                p.StandardOutput.ReadToEnd();
                p.StandardError.ReadToEnd();
                if (!p.WaitForExit(120_000)) { try { p.Kill(true); } catch { } return null; }
                return p.ExitCode == 0 && File.Exists(dest) ? dest : null;
            }
            catch { return null; }
        }

        private static string? FirstReadable(params string?[] candidates)
            => candidates.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c) && File.Exists(c));

        private static string Home()
            => Environment.GetEnvironmentVariable("HOME") ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }
}
