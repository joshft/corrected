using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace Corrected.Gate.Tests;

/// <summary>
/// P3 determinism-attestation INV-018/019 — the PRODUCER subject-manifest-digest emitter and its
/// non-staleness guard (the p3-producer-manifest-digest fix).
///
/// THE BUG THIS GUARDS: the sign workflow producer used to emit <c>subject_manifest_digest</c> as
/// <c>sha256</c> of a hardcoded one-line roles JSON, but the gate verifier
/// (<see cref="SubjectManifestProducer.IsStale"/> → <see cref="SubjectManifest.ComputeDigest"/>)
/// requires the per-file <c>{path}\n{sha}\n</c> content-hash over the pinned subject set. The two
/// recipes never match, so EVERY genuine production receipt read STALE at the gate and PR3 could
/// never activate. It survived because the production accept path is "unexercisable until PR3" and
/// the fixtures inject a synthetic staleness value — no test compared a producer-emitted digest
/// against the gate's live recipe. THESE tests are that missing net.
///
/// THE FIX: the producer emits the gate's OWN <see cref="SubjectManifest.CanonicalPreimage"/> (the
/// exact bytes ComputeDigest hashes) to the hand-off manifest file, then takes sha256sum of it — so
/// <c>sha256(manifest-file) == ComputeDigest == receipt.subject_manifest_digest</c>, and the signer
/// re-check (sha256(manifest-file) == receipt digest) stays correct UNCHANGED.
///
/// FIXED, LOAD-BEARING CONTRACT (the workflow, the fact name, and the env keys MUST agree):
///   * Fact/filter target : EmitSubjectManifestDigest
///   * env output manifest : EMIT_MANIFEST_OUT   (the canonical-preimage hand-off file)
///   * env repo root       : EMIT_MANIFEST_REPO_ROOT   (github.workspace = the attested_commit tree)
/// </summary>
public class Inv019ProducerManifestDigestTests
{
    // The ONE shared source of the workflow --filter target name (a rename is a single-place change).
    public const string FilterTargetName = "EmitSubjectManifestDigest";
    private const string EnvKeyOut = "EMIT_MANIFEST_OUT";
    private const string EnvKeyRepoRoot = "EMIT_MANIFEST_REPO_ROOT";

    // The emitter: write the pinned subject manifest's CANONICAL PREIMAGE at repoRoot to outPath
    // (UTF-8, NO BOM, no extra bytes) — so sha256(file) == the gate's ComputeHeadManifestDigest.
    private static void EmitPreimage(string repoRoot, string outPath)
    {
        SubjectManifest manifest =
            SubjectManifestProducer.BuildFromRepo(SubjectClassificationPolicy.Pinned, repoRoot);
        File.WriteAllText(
            outPath, manifest.CanonicalPreimage(),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static void EmitFromEnvironment(
        Func<string, string?> getenv, string suiteRepoRoot, string suiteOut)
    {
        ArgumentNullException.ThrowIfNull(getenv);
        string? o = getenv(EnvKeyOut);
        string? r = getenv(EnvKeyRepoRoot);
        // CI producer mode: BOTH keys present AND non-empty (an empty value is NOT "set").
        if (!string.IsNullOrEmpty(o) && !string.IsNullOrEmpty(r))
        {
            EmitPreimage(r, o);
        }
        else
        {
            EmitPreimage(suiteRepoRoot, suiteOut);
        }
    }

    private static string Sha256HexOfFile(string path)
        => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

    private static string NewTempOut()
        => Path.Combine(
            Path.Combine(Path.GetTempPath(), "p3-manifest-" + Guid.NewGuid().ToString("N")),
            "determinism-subject-manifest.json");

    private static void CleanupParent(string filePath)
    {
        try
        {
            string? dir = Path.GetDirectoryName(filePath);
            if (dir is not null && Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
        catch { /* best effort — OS temp cleanup is the backstop */ }
    }

    // =====================================================================
    // The LOAD-BEARING workflow --filter target. The producer job triggers this exact fact via
    // `dotnet test --filter 'FullyQualifiedName~EmitSubjectManifestDigest'`. Mode-agnostic: env set
    // -> writes the injected EMIT_MANIFEST_OUT over the EMIT_MANIFEST_REPO_ROOT tree; env unset ->
    // suite mode over the live repo. DO NOT RENAME without updating the workflow --filter + env keys.
    // =====================================================================
    [Fact]
    public void EmitSubjectManifestDigest()
    {
        string? outEnv = Environment.GetEnvironmentVariable(EnvKeyOut);
        string? rootEnv = Environment.GetEnvironmentVariable(EnvKeyRepoRoot);
        bool ciMode = !string.IsNullOrEmpty(outEnv) && !string.IsNullOrEmpty(rootEnv);

        string suiteRepoRoot = TestPaths.RepoRoot();
        string suiteOut = NewTempOut();
        Directory.CreateDirectory(Path.GetDirectoryName(suiteOut)!);
        string outPath = ciMode ? outEnv! : suiteOut;
        string repoRoot = ciMode ? rootEnv! : suiteRepoRoot;

        try
        {
            EmitFromEnvironment(Environment.GetEnvironmentVariable, suiteRepoRoot, suiteOut);

            Assert.True(File.Exists(outPath),
                $"INV-018: the emitter must write the canonical subject-manifest preimage to '{outPath}'.");

            // The load-bearing identity: sha256(emitted preimage file) == the gate's HEAD digest,
            // so the producer's `subject_manifest_digest = sha256sum(manifest-file)` is NON-STALE.
            string fileDigest = Sha256HexOfFile(outPath);
            string headDigest = SubjectManifestProducer.ComputeHeadManifestDigest(
                SubjectClassificationPolicy.Pinned, repoRoot);
            Assert.Equal(headDigest, fileDigest);
            Assert.False(
                SubjectManifestProducer.IsStale(fileDigest, SubjectClassificationPolicy.Pinned, repoRoot),
                "a producer-emitted subject_manifest_digest must read NON-STALE at the live gate.");
        }
        finally
        {
            if (!ciMode) { CleanupParent(suiteOut); }
        }
    }

    // =====================================================================
    // THE MISSING NET (regression guard): a receipt digest built the way the FIXED producer builds it
    // — sha256 of the emitted canonical preimage — must be NON-STALE at the live gate. Before the fix
    // the producer used sha256(roles-JSON), which is stale; this test would have caught that.
    // =====================================================================
    [Fact]
    public void Producer_emitted_manifest_digest_is_non_stale_at_the_gate()
    {
        string repoRoot = TestPaths.RepoRoot();
        string tmp = NewTempOut();
        Directory.CreateDirectory(Path.GetDirectoryName(tmp)!);
        try
        {
            EmitPreimage(repoRoot, tmp);
            string digest = Sha256HexOfFile(tmp);

            Assert.Matches("^[0-9a-f]{64}$", digest);
            Assert.False(
                SubjectManifestProducer.IsStale(digest, SubjectClassificationPolicy.Pinned, repoRoot),
                "REGRESSION GUARD (p3-producer-manifest-digest): sha256(emitted preimage) must equal " +
                "the gate's ComputeHeadManifestDigest — else every genuine production receipt reads stale.");
        }
        finally { CleanupParent(tmp); }
    }

    // =====================================================================
    // Value-preserving refactor guard: ComputeDigest == sha256(CanonicalPreimage), and the preimage
    // is sorted by Path Ordinal with the {path}\n{sha}\n recipe (so a producer that sha256sum's the
    // emitted preimage file gets exactly the digest the gate expects).
    // =====================================================================
    [Fact]
    public void CanonicalPreimage_hashes_to_ComputeDigest_and_is_path_ordinal_sorted()
    {
        var manifest = new SubjectManifest(new[]
        {
            new SubjectManifestEntry("b/second.txt", new string('a', 64)),
            new SubjectManifestEntry("a/first.txt", new string('b', 64)),
        });

        string preimage = manifest.CanonicalPreimage();
        string viaPreimage = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(preimage))).ToLowerInvariant();

        Assert.Equal(manifest.ComputeDigest(), viaPreimage);
        // Exact canonical form: a/first.txt sorts before b/second.txt (Ordinal), rows are {path}\n{sha}\n.
        Assert.Equal(
            "a/first.txt\n" + new string('b', 64) + "\n" +
            "b/second.txt\n" + new string('a', 64) + "\n",
            preimage);
    }

    // =====================================================================
    // Workflow wiring: the producer emits the manifest via THIS fact and NO LONGER hardcodes the
    // roles-JSON manifest (whose sha256 was the wrong-recipe digest that caused the stale bug).
    // =====================================================================
    [Fact]
    public void Sign_workflow_producer_emits_the_manifest_via_the_gate_emitter()
    {
        string wf = File.ReadAllText(
            TestPaths.RepoFile(".github", "workflows", "p3-determinism-sign.yml"));

        Assert.Contains(FilterTargetName, wf); // invokes the emitter fact
        Assert.Contains(EnvKeyOut, wf);
        Assert.Contains(EnvKeyRepoRoot, wf);
        // The hardcoded roles-JSON manifest (its JSON key) must be GONE — that was the wrong recipe.
        Assert.DoesNotContain("determinism_subject_manifest", wf);
    }

    // =====================================================================
    // Filter-target existence guard (reflection; does NOT nest dotnet test): the workflow
    // --filter substring binds to a real, parameterless [Fact]. Catches a future rename.
    // =====================================================================
    [Fact]
    public void Emit_filter_target_exists_is_a_fact_and_parameterless()
    {
        MethodInfo[] candidates = Assembly.GetExecutingAssembly()
            .GetTypes()
            .Where(t => t.IsPublic || t.IsNestedPublic)
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(m => m.Name.Contains(FilterTargetName, StringComparison.Ordinal))
            .ToArray();

        MethodInfo target = Assert.Single(candidates);
        Assert.True(
            target.GetCustomAttributes().Any(a => a.GetType().Name == "FactAttribute"),
            "INV-018: the EmitSubjectManifestDigest filter target must be an xUnit [Fact].");
        Assert.Empty(target.GetParameters());
    }
}
