using System.IO;
using Corrected.Gate;
using Xunit;

namespace Corrected.Gate.Tests;

/// <summary>
/// P3 determinism-attestation spec INV-012 / INV-019 — the impure <see cref="GitAncestry"/> producer
/// run against the REAL repo (integration). HEAD and any real ancestor ref resolve to
/// <see cref="AncestryStatus.Ancestor"/>; a bad/absent commit, an empty commit string, and a
/// non-git directory all fail closed to <see cref="AncestryStatus.Uncomputable"/> — NEVER a silent
/// Ancestor (RS-013). Commit strings may be symbolic (git resolves them), so the test needs no
/// hard-coded sha.
/// </summary>
public class Inv019GitAncestryTests
{
    // Tests INV-019 [integration]: HEAD is an ancestor of itself.
    [Fact]
    public void Head_is_an_ancestor_of_head()
    {
        Assert.Equal(AncestryStatus.Ancestor, GitAncestry.Classify(TestPaths.RepoRoot(), "HEAD"));
    }

    // Tests INV-019 [integration]: a real parent commit is an ancestor of HEAD.
    [Fact]
    public void A_real_parent_commit_is_an_ancestor_of_head()
    {
        Assert.Equal(AncestryStatus.Ancestor, GitAncestry.Classify(TestPaths.RepoRoot(), "HEAD~1"));
    }

    // Tests INV-019 [integration] (FAIL-CLOSED): an all-zero (absent) object is Uncomputable, never
    // Ancestor — git exits 128 on a bad object and the producer degrades safely.
    [Fact]
    public void An_absent_commit_is_uncomputable()
    {
        Assert.Equal(
            AncestryStatus.Uncomputable,
            GitAncestry.Classify(TestPaths.RepoRoot(), new string('0', 40)));
    }

    // Tests INV-019 [integration] (FAIL-CLOSED): an empty commit string is Uncomputable.
    [Fact]
    public void An_empty_commit_string_is_uncomputable()
    {
        Assert.Equal(AncestryStatus.Uncomputable, GitAncestry.Classify(TestPaths.RepoRoot(), ""));
    }

    // Tests INV-019 [integration] (FAIL-CLOSED): a non-git directory is Uncomputable, not a throw.
    [Fact]
    public void A_non_git_directory_is_uncomputable()
    {
        string tmp = Path.Combine(Path.GetTempPath(), "p3-nogit-" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            Assert.Equal(AncestryStatus.Uncomputable, GitAncestry.Classify(tmp, "HEAD"));
        }
        finally
        {
            try { Directory.Delete(tmp, recursive: true); } catch { /* backstop */ }
        }
    }
}
