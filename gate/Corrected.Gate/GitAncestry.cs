using System;
using System.Diagnostics;

namespace Corrected.Gate;

/// <summary>
/// The impure <c>attested_commit</c>-vs-HEAD ancestry producer (INV-012/019). Git ancestry is an
/// I/O fact computed OUTSIDE the pure layer-1 classifier and handed in as a typed
/// <see cref="AncestryStatus"/>. Runs <c>git merge-base --is-ancestor &lt;commit&gt; HEAD</c>:
///   * exit 0 =&gt; <see cref="AncestryStatus.Ancestor"/>;
///   * exit 1 =&gt; <see cref="AncestryStatus.NotAncestor"/>;
///   * any other exit / a bad or absent commit / a shallow or non-git tree / a launch fault
///     =&gt; <see cref="AncestryStatus.Uncomputable"/> (fail-closed — NEVER a silent Ancestor, RS-013).
/// </summary>
public static class GitAncestry
{
    /// <summary>Classify <paramref name="attestedCommit"/> against HEAD under <paramref name="repoRoot"/>.</summary>
    public static AncestryStatus Classify(string repoRoot, string attestedCommit)
    {
        ArgumentNullException.ThrowIfNull(repoRoot);

        // A null/empty/whitespace commit can never be proven an ancestor — fail closed.
        if (string.IsNullOrWhiteSpace(attestedCommit))
        {
            return AncestryStatus.Uncomputable;
        }

        try
        {
            var psi = new ProcessStartInfo("git")
            {
                WorkingDirectory = repoRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            psi.ArgumentList.Add("merge-base");
            psi.ArgumentList.Add("--is-ancestor");
            psi.ArgumentList.Add(attestedCommit);
            psi.ArgumentList.Add("HEAD");

            using Process? proc = Process.Start(psi);
            if (proc is null)
            {
                return AncestryStatus.Uncomputable;
            }

            proc.StandardOutput.ReadToEnd();
            proc.StandardError.ReadToEnd();
            proc.WaitForExit();

            return proc.ExitCode switch
            {
                0 => AncestryStatus.Ancestor,
                1 => AncestryStatus.NotAncestor,
                // exit 128 (bad object / not a repo) and every other code fail closed.
                _ => AncestryStatus.Uncomputable,
            };
        }
        catch
        {
            return AncestryStatus.Uncomputable;
        }
    }
}
