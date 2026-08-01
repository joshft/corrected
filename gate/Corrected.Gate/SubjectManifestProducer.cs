using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace Corrected.Gate;

/// <summary>
/// The IMPURE HEAD-side subject-manifest producer (INV-018 / INV-019, TB-006). It enumerates the
/// committed repo tree under an injected root, applies the PURE <see cref="SubjectClassifier"/>,
/// hashes each subject file, builds the <see cref="SubjectManifest"/>, and derives the INV-019
/// staleness verdict against a signed digest. It takes the <see cref="SubjectPolicy"/> as a
/// parameter (the pinned production policy lives in <see cref="SubjectClassificationPolicy"/>), so
/// this producer stays independent of the policy CONTENT and is driven by synthetic policies in
/// unit tests.
///
/// Fail-closed (AP-001/AP-022): <see cref="IsStale"/> returns STALE on a null/empty signed digest,
/// a missing root, or any I/O fault — never a silent non-stale. A stale baseline that reads "fresh"
/// is the INV-019 fail-open this guards.
/// </summary>
public static class SubjectManifestProducer
{
    /// <summary>
    /// Enumerate the committed, repo-relative (forward-slash) files under <paramref name="repoRoot"/>.
    /// Prefers <c>git ls-files</c> (the committed tree — the correct manifest domain); falls back to
    /// an on-disk recursive walk (skipping <c>.git/</c>) for a non-git tree (e.g. an injected temp
    /// root in tests). Returns an empty list for a missing root.
    /// </summary>
    public static IReadOnlyList<string> EnumerateRepoFiles(string repoRoot)
    {
        ArgumentNullException.ThrowIfNull(repoRoot);
        if (!Directory.Exists(repoRoot))
        {
            return Array.Empty<string>();
        }

        IReadOnlyList<string>? tracked = TryGitLsFiles(repoRoot);
        if (tracked is not null && tracked.Count > 0)
        {
            return tracked;
        }

        return WalkOnDisk(repoRoot);
    }

    /// <summary>
    /// Build the subject manifest from the real tree (INV-018): discover the subject set via the
    /// pure classifier, then hash each subject file's bytes (lowercase-hex SHA-256).
    /// </summary>
    public static SubjectManifest BuildFromRepo(SubjectPolicy policy, string repoRoot)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(repoRoot);

        IReadOnlyList<string> subject = SubjectClassifier.DiscoverSubjectSet(policy, EnumerateRepoFiles(repoRoot));

        var entries = new List<SubjectManifestEntry>(subject.Count);
        foreach (string rel in subject)
        {
            string abs = Path.Combine(repoRoot, Path.Combine(rel.Split('/')));
            byte[] bytes = File.ReadAllBytes(abs);
            string sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            entries.Add(new SubjectManifestEntry(rel, sha));
        }

        return new SubjectManifest(entries);
    }

    /// <summary>The current HEAD manifest digest = <see cref="BuildFromRepo"/>().ComputeDigest().</summary>
    public static string ComputeHeadManifestDigest(SubjectPolicy policy, string repoRoot)
        => BuildFromRepo(policy, repoRoot).ComputeDigest();

    /// <summary>
    /// The INV-019 staleness verdict: STALE (true) unless the signed subject-manifest digest equals
    /// the live HEAD manifest digest. Fail-closed — a null/empty signed digest, a missing root, or
    /// any I/O fault is STALE, never silently non-stale.
    /// </summary>
    public static bool IsStale(string? signedManifestDigest, SubjectPolicy policy, string repoRoot)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(repoRoot);

        if (string.IsNullOrWhiteSpace(signedManifestDigest) || !Directory.Exists(repoRoot))
        {
            return true;
        }

        try
        {
            string head = ComputeHeadManifestDigest(policy, repoRoot);
            return !string.Equals(signedManifestDigest, head, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            // Any enumeration/read/hash fault degrades to STALE (fail closed) — never non-stale.
            return true;
        }
    }

    /// <summary>
    /// Run <c>git ls-files -z</c> under <paramref name="repoRoot"/>. Returns the repo-relative,
    /// forward-slash committed paths, or <c>null</c> when git is unavailable / errors / the dir is
    /// not a work tree (the caller falls back to the on-disk walk).
    /// </summary>
    private static IReadOnlyList<string>? TryGitLsFiles(string repoRoot)
    {
        try
        {
            var psi = new ProcessStartInfo("git")
            {
                WorkingDirectory = repoRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            psi.ArgumentList.Add("ls-files");
            psi.ArgumentList.Add("-z");

            using Process? proc = Process.Start(psi);
            if (proc is null)
            {
                return null;
            }

            string stdout = proc.StandardOutput.ReadToEnd();
            proc.StandardError.ReadToEnd();
            proc.WaitForExit();
            if (proc.ExitCode != 0)
            {
                return null;
            }

            return stdout.Split('\0', StringSplitOptions.RemoveEmptyEntries).ToList();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Recursive on-disk walk under <paramref name="repoRoot"/>, repo-relative + forward-slash, skipping <c>.git/</c>.</summary>
    private static IReadOnlyList<string> WalkOnDisk(string repoRoot)
    {
        string full = Path.GetFullPath(repoRoot);
        var result = new List<string>();
        foreach (string abs in Directory.EnumerateFiles(full, "*", SearchOption.AllDirectories))
        {
            string rel = Path.GetRelativePath(full, abs).Replace('\\', '/');
            if (rel == ".git" || rel.StartsWith(".git/", StringComparison.Ordinal))
            {
                continue;
            }
            result.Add(rel);
        }
        return result;
    }
}
