using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Corrected.Gate;
using Xunit;

namespace Corrected.Gate.Tests;

/// <summary>
/// P3 determinism-attestation spec INV-018 / INV-019 (~628-690), TB-006 — the IMPURE HEAD-side
/// producer <see cref="SubjectManifestProducer"/>. It enumerates the committed repo tree under an
/// injected root, applies the PURE <see cref="SubjectClassifier"/>, hashes each subject file, builds
/// the <see cref="SubjectManifest"/>, and derives the INV-019 staleness verdict against a signed
/// digest. Driven over a TEMP tree (an injected repo root, no git) so it is hermetic and needs no
/// committed fixtures.
///
/// FAIL-CLOSED is the load-bearing property (AP-001/AP-022): staleness must default to STALE on a
/// null/empty signed digest, a missing root, or any I/O fault — never silently non-stale. A stale
/// baseline that reads "fresh" is the INV-019 fail-open this guards.
/// </summary>
public class Inv019SubjectManifestProducerTests
{
    private const string Root = "gate/Corrected.Gate/";
    private const string Excluded = "gate/Corrected.Gate/Gen.cs";

    private static SubjectPolicy Policy() => new(
        OwnedRoots: new[] { Root },
        Anchors: new[] { "run-spike.sh" },
        Exclusions: new[] { Excluded });

    /// <summary>Materialize a synthetic repo tree under a fresh temp root; returns the root.</summary>
    private static string MakeTree(params (string Rel, string Content)[] files)
    {
        string root = Path.Combine(Path.GetTempPath(), "p3-manifest-" + Guid.NewGuid().ToString("N"));
        foreach ((string rel, string content) in files)
        {
            string abs = Path.Combine(root, Path.Combine(rel.Split('/')));
            Directory.CreateDirectory(Path.GetDirectoryName(abs)!);
            File.WriteAllText(abs, content);
        }
        return root;
    }

    private static string Sha256Hex(string content)
        => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(content)))
            .ToLowerInvariant();

    // Tests INV-018 [integration]: BuildFromRepo discovers exactly the subject set (relevant, minus
    // exclusions) and each row's Sha256 is the REAL SHA-256 of the file bytes. The excluded and the
    // unrelated files are absent. RED: SubjectManifestProducer does not exist yet (compile-RED).
    [Fact]
    public void BuildFromRepo_hashes_the_discovered_subject_files_only()
    {
        string root = MakeTree(
            ("gate/Corrected.Gate/a.cs", "AAA"),
            ("gate/Corrected.Gate/sub/b.cs", "BBB"),
            ("run-spike.sh", "#!/bin/sh\n"),
            (Excluded, "GENERATED"),          // excluded -> must NOT appear
            ("docs/readme.md", "hello"));      // unrelated -> must NOT appear
        try
        {
            SubjectManifest m = SubjectManifestProducer.BuildFromRepo(Policy(), root);

            Assert.Equal(
                new[] { "gate/Corrected.Gate/a.cs", "gate/Corrected.Gate/sub/b.cs", "run-spike.sh" }
                    .ToHashSet(),
                m.Paths.ToHashSet());
            Assert.DoesNotContain(Excluded, m.Paths);
            Assert.DoesNotContain("docs/readme.md", m.Paths);

            // Each row carries the real content hash.
            SubjectManifestEntry a = m.Entries.Single(e => e.Path == "gate/Corrected.Gate/a.cs");
            Assert.Equal(Sha256Hex("AAA"), a.Sha256);
        }
        finally { Cleanup(root); }
    }

    // Tests INV-019 [integration]: the HEAD manifest digest equals the built manifest's own digest —
    // the producer feeds the same canonical digest the pure layer computes.
    [Fact]
    public void ComputeHeadManifestDigest_equals_the_built_manifest_digest()
    {
        string root = MakeTree(("gate/Corrected.Gate/a.cs", "AAA"), ("run-spike.sh", "x"));
        try
        {
            string head = SubjectManifestProducer.ComputeHeadManifestDigest(Policy(), root);
            Assert.Equal(SubjectManifestProducer.BuildFromRepo(Policy(), root).ComputeDigest(), head);
        }
        finally { Cleanup(root); }
    }

    // Tests INV-019 [integration]: staleness is FALSE only when the signed digest equals the live
    // HEAD digest. The matching case is the sole non-stale cell.
    [Fact]
    public void IsStale_false_only_when_signed_digest_matches_head()
    {
        string root = MakeTree(("gate/Corrected.Gate/a.cs", "AAA"), ("run-spike.sh", "x"));
        try
        {
            string head = SubjectManifestProducer.ComputeHeadManifestDigest(Policy(), root);
            Assert.False(SubjectManifestProducer.IsStale(head, Policy(), root));
            Assert.True(SubjectManifestProducer.IsStale("deadbeef", Policy(), root));
        }
        finally { Cleanup(root); }
    }

    // Tests INV-019 [integration] (THE FAIL-CLOSED GUARD): a null or empty signed digest is STALE,
    // never silently non-stale. A caller that lost the signed digest must fail closed.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void IsStale_true_on_null_or_empty_signed_digest(string? signed)
    {
        string root = MakeTree(("gate/Corrected.Gate/a.cs", "AAA"));
        try
        {
            Assert.True(SubjectManifestProducer.IsStale(signed, Policy(), root));
        }
        finally { Cleanup(root); }
    }

    // Tests INV-019 [integration] (FAIL-CLOSED): a missing repo root yields STALE, not a throw and
    // not a silent non-stale. Any I/O fault degrades to stale.
    [Fact]
    public void IsStale_true_when_repo_root_is_missing()
    {
        string missing = Path.Combine(Path.GetTempPath(), "p3-absent-" + Guid.NewGuid().ToString("N"));
        Assert.True(SubjectManifestProducer.IsStale("deadbeef", Policy(), missing));
    }

    // Tests INV-019 [integration] (the whole point of INV-019): once a subject file CHANGES, the
    // baseline computed against the OLD signed digest goes stale — proving IsStale recomputes from
    // the LIVE tree, not a cached value.
    [Fact]
    public void IsStale_true_after_a_subject_file_changes()
    {
        string root = MakeTree(("gate/Corrected.Gate/a.cs", "AAA"), ("run-spike.sh", "x"));
        try
        {
            string signed = SubjectManifestProducer.ComputeHeadManifestDigest(Policy(), root);
            Assert.False(SubjectManifestProducer.IsStale(signed, Policy(), root));

            // Mutate a subject file -> the head digest moves -> the old signed digest is now stale.
            File.WriteAllText(Path.Combine(root, "gate", "Corrected.Gate", "a.cs"), "AAA-CHANGED");
            Assert.True(SubjectManifestProducer.IsStale(signed, Policy(), root));
        }
        finally { Cleanup(root); }
    }

    // Tests INV-018 [integration]: EnumerateRepoFiles returns repo-relative, forward-slash paths (no
    // backslashes, no absolute/root prefix) so they feed the pure classifier directly.
    [Fact]
    public void EnumerateRepoFiles_returns_repo_relative_forward_slash_paths()
    {
        string root = MakeTree(("gate/Corrected.Gate/a.cs", "A"), ("docs/readme.md", "b"));
        try
        {
            var files = SubjectManifestProducer.EnumerateRepoFiles(root).ToList();
            Assert.Contains("gate/Corrected.Gate/a.cs", files);
            Assert.Contains("docs/readme.md", files);
            Assert.All(files, f =>
            {
                Assert.DoesNotContain("\\", f);
                Assert.False(f.StartsWith('/'), $"'{f}' must be repo-relative");
                Assert.False(Path.IsPathRooted(f), $"'{f}' must not be absolute");
            });
        }
        finally { Cleanup(root); }
    }

    private static void Cleanup(string root)
    {
        try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
        catch { /* OS temp cleanup is the backstop */ }
    }
}
