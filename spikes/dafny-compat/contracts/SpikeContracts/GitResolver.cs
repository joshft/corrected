// MA-XC-1/MA-VI-5/MA-UC-3 (merge group "gitfile", expanding PR-001): the ONE
// shared, gitfile-aware repo-root/HEAD resolver. Every production and test-side
// git discovery routes through this class — a linked worktree's `.git` regular
// FILE (containing `gitdir: <path>`) resolves exactly like a `.git` directory,
// and HEAD/ref resolution honors `commondir` for shared refs/packed-refs.
//
// A source-scan test asserts the literal ".git" appears in NO other spike
// source file, so a new ad-hoc walk cannot silently reintroduce the class
// (AP-007 fix-round checklist).

namespace Corrected.Spike.Contracts;

public static class GitResolver
{
    /// <summary>
    /// Walks up from <paramref name="startDir"/> to the first directory whose
    /// `.git` entry is a directory OR a regular gitfile — both are repo
    /// boundaries (linked worktrees use a file). Returns null when no boundary
    /// exists; callers FAIL CLOSED on null (never a spike-root fallback —
    /// MA-UC-3).
    /// </summary>
    public static string? FindRepoRoot(string startDir)
    {
        var dir = new DirectoryInfo(Path.GetFullPath(startDir));
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, ".git");
            if (Directory.Exists(candidate) || File.Exists(candidate))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }
        return null;
    }

    /// <summary>
    /// Resolves the ACTUAL git directory for a repo root: a `.git` directory is
    /// itself; a `.git` FILE is dereferenced via its `gitdir: &lt;path&gt;` line
    /// (absolute, or relative to the worktree root).
    /// </summary>
    public static string ResolveGitDir(string repoRoot)
    {
        var entry = Path.Combine(repoRoot, ".git");
        if (Directory.Exists(entry))
        {
            return entry;
        }
        if (!File.Exists(entry))
        {
            throw new InvalidOperationException($"no .git entry at {repoRoot} — not a repo root (gitfile resolver)");
        }
        var text = File.ReadAllText(entry).Trim();
        const string prefix = "gitdir:";
        if (!text.StartsWith(prefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($".git file at {repoRoot} carries no 'gitdir:' pointer — refusing to guess (fail closed)");
        }
        var target = text[prefix.Length..].Trim();
        return Path.GetFullPath(Path.IsPathRooted(target) ? target : Path.Combine(repoRoot, target));
    }

    /// <summary>
    /// The `commondir` for a git dir: linked-worktree private git dirs carry a
    /// `commondir` file pointing (usually relatively) at the shared main git
    /// dir that owns refs/ and packed-refs. A git dir without one IS its own
    /// common dir.
    /// </summary>
    public static string ResolveCommonDir(string gitDir)
    {
        var commonFile = Path.Combine(gitDir, "commondir");
        if (!File.Exists(commonFile))
        {
            return gitDir;
        }
        var target = File.ReadAllText(commonFile).Trim();
        return Path.GetFullPath(Path.IsPathRooted(target) ? target : Path.Combine(gitDir, target));
    }

    /// <summary>
    /// Reads the commit id of HEAD for <paramref name="repoRoot"/>, worktree-
    /// correctly: HEAD comes from the worktree's OWN git dir; symbolic refs
    /// resolve first against that git dir, then against the commondir's loose
    /// refs, then both packed-refs files. Throws (fail closed) when the ref
    /// cannot be resolved.
    /// </summary>
    public static string ReadHeadCommit(string repoRoot)
    {
        var gitDir = ResolveGitDir(repoRoot);
        var head = File.ReadAllText(Path.Combine(gitDir, "HEAD")).Trim();
        if (!head.StartsWith("ref: ", StringComparison.Ordinal))
        {
            return head; // detached HEAD: the commit id itself
        }
        var refName = head["ref: ".Length..].Trim();
        var commonDir = ResolveCommonDir(gitDir);
        foreach (var baseDir in new[] { gitDir, commonDir }.Distinct(StringComparer.Ordinal))
        {
            var refFile = Path.Combine(baseDir, refName.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(refFile))
            {
                return File.ReadAllText(refFile).Trim();
            }
        }
        foreach (var baseDir in new[] { gitDir, commonDir }.Distinct(StringComparer.Ordinal))
        {
            var packed = Path.Combine(baseDir, "packed-refs");
            if (!File.Exists(packed))
            {
                continue;
            }
            foreach (var line in File.ReadAllLines(packed))
            {
                if (line.EndsWith(" " + refName, StringComparison.Ordinal))
                {
                    return line.Split(' ')[0];
                }
            }
        }
        throw new InvalidOperationException($"cannot resolve git ref {refName} (gitfile resolver; searched {gitDir} and its commondir)");
    }
}
