using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Corrected.Gate;

/// <summary>
/// P3 determinism-attestation spec INV-014 — "the cosign subprocess seam is hardened"
/// (spec ~546-559). DISTINCT from the carrier spec's same-numbered INV-014
/// (the documented-command invariant, <c>Inv014DocumentedCommandTests</c>): this type
/// is the hardened out-of-process seam that invokes a SUPPLIED cosign executable under
/// an absolute pinned path, an argv ARRAY (no interpolation — AP-008), a fixed working
/// directory, a clean environment (no ambient HOME/TUF/config passthrough), regular-file
/// / no-symlink input checks, size caps on captured stdout/stderr, a process timeout with
/// process-TREE termination, and an exact typed exit/error taxonomy (no raw passthrough,
/// no response-file/config injection).
///
/// The REAL cosign version/argv pin is INV-015's <see cref="CosignPin"/> — referenced by
/// the production caller, but THIS type is the seam, not the pin.
/// </summary>
public enum CosignOutcome
{
    /// <summary>Child exited 0 with in-bounds output.</summary>
    Ok,

    /// <summary>Child exceeded the configured timeout; the process tree was terminated.</summary>
    Timeout,

    /// <summary>Captured stdout/stderr exceeded the configured cap.</summary>
    OversizeOutput,

    /// <summary>Child exited with a defined non-zero code (carried in <see cref="CosignRunResult.ExitCode"/>).</summary>
    NonZeroExit,

    /// <summary>An input (executable path or a file input) was rejected before invoking cosign.</summary>
    InputRejected,

    /// <summary>The child process could not be started.</summary>
    LaunchFailed,
}

/// <summary>The typed result of one hardened cosign invocation. No raw passthrough.</summary>
public sealed record CosignRunResult(
    CosignOutcome Outcome,
    int? ExitCode,
    string StdOut,
    string StdErr,
    bool OutputTruncated,
    string? RejectReason);

/// <summary>
/// The hardening contract for one invocation. The caller supplies the argv ARRAY and the
/// absolute executable path; the wrapper adds nothing (no response-file, no --config).
/// </summary>
public sealed class CosignRunOptions
{
    /// <summary>Absolute pinned executable path. A non-absolute path is rejected.</summary>
    public required string ExecutablePath { get; init; }

    /// <summary>The exact argv array, element-by-element (no interpolation — AP-008).</summary>
    public required IReadOnlyList<string> Argv { get; init; }

    /// <summary>Fixed working directory for the child.</summary>
    public required string WorkingDirectory { get; init; }

    /// <summary>Receipt/bundle/root file inputs; each must be a regular file (no symlink).</summary>
    public IReadOnlyList<string> FileInputs { get; init; } = Array.Empty<string>();

    /// <summary>Process timeout; on expiry the process TREE is terminated.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Cap on captured stdout bytes.</summary>
    public long StdOutCapBytes { get; init; } = 1_048_576;

    /// <summary>Cap on captured stderr bytes.</summary>
    public long StdErrCapBytes { get; init; } = 1_048_576;

    /// <summary>
    /// Cap on the LENGTH (in bytes) of any single file input. A receipt/bundle/root larger
    /// than this is rejected BEFORE launch (an unbounded input read is a DoS hole). Checked
    /// via the file's declared length only — the file is never read here. 64 MiB is far above
    /// any security-reasonable receipt/bundle/root while still rejecting an adversarial input.
    /// </summary>
    public long InputCapBytes { get; init; } = 67_108_864;

    /// <summary>Explicit env allowlist; everything else is cleared (no ambient passthrough).</summary>
    public IReadOnlyList<string> EnvAllowlist { get; init; } = Array.Empty<string>();
}

/// <summary>
/// The hardened cosign subprocess seam (INV-014). See <see cref="CosignOutcome"/>.
/// </summary>
public static class CosignRunner
{
    // Bounded budget for the post-exit / post-kill drain of the capped read tasks. After the
    // child exits (or the tree is killed) the pipes reach EOF; this bounds the wait so a
    // lingering writer can never hang the seam UNBOUNDED (mirrors ClosureBuildRunner's bounded
    // drain, but here BOTH streams are capped — the reference reads stderr unbounded).
    private const int DrainBudgetMs = 10_000;

    // Read-chunk size for the incremental (bounded) stream reads.
    private const int ReadChunkBytes = 8192;

    /// <summary>
    /// Run the supplied executable under the hardening contract, returning a typed result.
    /// Order is deliberate and fail-closed: validate the executable path and every file input
    /// BEFORE any process starts, then launch under a hardened <see cref="ProcessStartInfo"/>
    /// (argv array, no shell, cleared+allowlisted env, fixed cwd), then read stdout/stderr under
    /// strict byte caps while enforcing a timeout with process-TREE termination, and finally map
    /// the observed result onto the exact typed taxonomy (no raw/undefined passthrough).
    /// </summary>
    public static CosignRunResult Run(CosignRunOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        // 1) Absolute pinned executable path. A relative/bare/empty path is rejected here, BEFORE
        //    any launch; the reject reason attributes the rejection to non-absoluteness.
        string exe = options.ExecutablePath;
        if (string.IsNullOrWhiteSpace(exe) || !Path.IsPathRooted(exe))
        {
            return Rejected(
                "executable path must be an absolute path; a relative / non-absolute path is rejected.");
        }

        // 2) Regular-file / no-symlink / size checks on every file input, BEFORE launch. Any
        //    failure short-circuits: cosign is NOT invoked (a marker test proves non-invocation).
        foreach (string input in options.FileInputs)
        {
            string? reason = ValidateFileInput(input, options.InputCapBytes);
            if (reason is not null)
            {
                return Rejected(reason);
            }
        }

        // 3) Hardened launch surface: an argv ARRAY (execve-style, no interpolation — AP-008), no
        //    shell, a cleared+allowlisted environment (no ambient HOME/TUF/config passthrough), and
        //    a fixed working directory. NOTHING is added to argv (no response-file, no --config).
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            WorkingDirectory = options.WorkingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (string arg in options.Argv)
        {
            psi.ArgumentList.Add(arg);
        }

        // Clear the inherited block, then re-add ONLY the allowlisted keys from the parent. This
        // happens unconditionally — an empty allowlist yields an empty child environment (a
        // wrapper that only clears when the allowlist is non-empty would be a fail-open bug).
        psi.Environment.Clear();
        foreach (string key in options.EnvAllowlist)
        {
            string? value = Environment.GetEnvironmentVariable(key);
            if (value is not null)
            {
                psi.Environment[key] = value;
            }
        }

        // 4) Launch. An absolute path that cannot be executed (missing file, or a non-executable
        //    regular file) throws here => the typed LaunchFailed (never a raw throw, a NonZeroExit,
        //    or a generic InputRejected).
        Process proc;
        try
        {
            Process? started = Process.Start(psi);
            if (started is null)
            {
                return new CosignRunResult(
                    CosignOutcome.LaunchFailed, null, string.Empty, string.Empty, false,
                    "cosign process could not be started.");
            }
            proc = started;
        }
        catch (Exception ex)
        {
            return new CosignRunResult(
                CosignOutcome.LaunchFailed, null, string.Empty, string.Empty, false,
                "cosign process launch failed: " + ex.GetType().Name);
        }

        using (proc)
        using (var cts = new CancellationTokenSource())
        {
            // Start the BOUNDED reads BEFORE waiting so the child never blocks on a full pipe
            // (draining concurrently), and so a spew never accumulates unbounded in memory: each
            // read stores at most its cap and only tracks the total to flag an overflow.
            Task<CappedRead> outTask = ReadCappedAsync(proc.StandardOutput.BaseStream, options.StdOutCapBytes, cts.Token);
            Task<CappedRead> errTask = ReadCappedAsync(proc.StandardError.BaseStream, options.StdErrCapBytes, cts.Token);

            int timeoutMs = (int)Math.Clamp(options.Timeout.TotalMilliseconds, 0d, int.MaxValue);

            bool exited;
            try { exited = proc.WaitForExit(timeoutMs); }
            catch { exited = false; }

            if (!exited)
            {
                // Timeout: terminate the ENTIRE process tree so a backgrounded grandchild dies too,
                // then unblock + drain the reads within a bounded wall-time and report Timeout.
                KillTree(proc);
                cts.Cancel();
                DrainBounded(outTask, errTask);
                CappedRead tOut = SafeResult(outTask);
                CappedRead tErr = SafeResult(errTask);
                return new CosignRunResult(
                    CosignOutcome.Timeout, null,
                    Decode(tOut), Decode(tErr),
                    tOut.Exceeded || tErr.Exceeded, null);
            }

            // Child exited within the timeout: finish the bounded drain so all in-flight output is
            // captured. If the drain itself does not complete (a lingering writer), kill the tree
            // and give up on further capture rather than hang.
            if (!DrainBounded(outTask, errTask))
            {
                KillTree(proc);
                cts.Cancel();
                DrainBounded(outTask, errTask);
            }

            CappedRead readOut = SafeResult(outTask);
            CappedRead readErr = SafeResult(errTask);
            bool truncated = readOut.Exceeded || readErr.Exceeded;
            int exitCode = SafeExitCode(proc);

            // Exact taxonomy. Oversize output (a DoS spew on EITHER stream) outranks the exit code;
            // otherwise a defined non-zero code maps to NonZeroExit carrying that exact code; a
            // clean zero exit with in-bounds output maps to Ok(0).
            if (truncated)
            {
                return new CosignRunResult(
                    CosignOutcome.OversizeOutput, exitCode,
                    Decode(readOut), Decode(readErr), true, null);
            }
            if (exitCode != 0)
            {
                return new CosignRunResult(
                    CosignOutcome.NonZeroExit, exitCode,
                    Decode(readOut), Decode(readErr), false, null);
            }
            return new CosignRunResult(
                CosignOutcome.Ok, exitCode,
                Decode(readOut), Decode(readErr), false, null);
        }
    }

    private static CosignRunResult Rejected(string reason)
        => new(CosignOutcome.InputRejected, null, string.Empty, string.Empty, false, reason);

    /// <summary>
    /// Validate one file input against the no-symlink / regular-file / size-cap policy. Returns a
    /// non-null attributed reject reason on any violation, or <c>null</c> if the input is a valid
    /// in-bounds regular file. Every check runs BEFORE cosign is launched.
    /// </summary>
    private static string? ValidateFileInput(string path, long inputCap)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "file input path is empty; a regular file is required.";
        }

        FileInfo fi;
        try { fi = new FileInfo(path); }
        catch { return "file input is not a regular file: " + Safe(path); }

        // Symlink (or any reparse point) => reject. LinkTarget is the robust check (it is non-null
        // for a link even when the target is missing, and does not throw). The reparse-point
        // attribute is a belt-and-suspenders second check.
        if (fi.LinkTarget is not null)
        {
            return "file input is a symlink, not a regular file: " + Safe(path);
        }
        try
        {
            if ((fi.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                return "file input is a symlink / reparse point, not a regular file: " + Safe(path);
            }
        }
        catch
        {
            // Attributes unreadable (e.g. a broken path) — the existence check below fails closed.
        }

        // FileInfo.Exists is false for a missing path AND for a directory, so both are rejected.
        if (!fi.Exists)
        {
            return "file input is missing or not a regular file: " + Safe(path);
        }

        long length;
        try { length = fi.Length; }
        catch { return "file input is not a regular file: " + Safe(path); }

        if (length > inputCap)
        {
            return "file input exceeds the maximum input size cap of " + inputCap + " bytes: " + Safe(path);
        }

        return null;
    }

    /// <summary>Captured (bounded) bytes from one stream plus whether the cap was exceeded.</summary>
    private readonly struct CappedRead
    {
        public CappedRead(byte[] bytes, bool exceeded)
        {
            Bytes = bytes;
            Exceeded = exceeded;
        }

        public byte[] Bytes { get; }

        /// <summary>True iff the stream produced strictly MORE than the cap (a &gt; comparison).</summary>
        public bool Exceeded { get; }
    }

    /// <summary>
    /// Read a stream to EOF while storing at most <paramref name="cap"/> bytes. The whole stream
    /// is drained (so the child never blocks on a full pipe) but memory is bounded: bytes past the
    /// cap are counted, never retained. Exceeded is set only when the TOTAL is strictly &gt; the
    /// cap, so output exactly at the cap is NOT flagged. Never throws: on cancellation (timeout
    /// tree-kill) or a pipe error it returns whatever was captured so far.
    /// </summary>
    private static async Task<CappedRead> ReadCappedAsync(Stream stream, long cap, CancellationToken ct)
    {
        using var captured = new MemoryStream();
        byte[] buffer = new byte[ReadChunkBytes];
        long total = 0;
        bool exceeded = false;
        try
        {
            int n;
            while ((n = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct).ConfigureAwait(false)) > 0)
            {
                total += n;
                long remaining = cap - captured.Length;
                if (remaining > 0)
                {
                    int toStore = (int)Math.Min(remaining, (long)n);
                    captured.Write(buffer, 0, toStore);
                }
                if (total > cap)
                {
                    exceeded = true;
                }
            }
        }
        catch
        {
            // Cancelled or pipe error — return the bounded bytes captured so far (never rethrow).
        }

        return new CappedRead(captured.ToArray(), exceeded);
    }

    private static bool DrainBounded(params Task[] tasks)
    {
        try { return Task.WaitAll(tasks, DrainBudgetMs); }
        catch { return true; } // ReadCappedAsync never faults, but treat any surprise as drained.
    }

    private static CappedRead SafeResult(Task<CappedRead> task)
    {
        if (task.IsCompleted)
        {
            try { return task.GetAwaiter().GetResult(); }
            catch { return new CappedRead(Array.Empty<byte>(), false); }
        }
        // Not complete after the bounded drain: report nothing rather than block.
        return new CappedRead(Array.Empty<byte>(), false);
    }

    private static void KillTree(Process proc)
    {
        try { proc.Kill(entireProcessTree: true); }
        catch { /* already gone / not started */ }
        try { proc.WaitForExit(DrainBudgetMs); }
        catch { /* best effort reap */ }
    }

    private static int SafeExitCode(Process proc)
    {
        try { return proc.ExitCode; }
        catch { return -1; }
    }

    private static string Decode(CappedRead read) => Encoding.UTF8.GetString(read.Bytes);

    /// <summary>Reduce a path to a bare filename so a reject reason never leaks an absolute path.</summary>
    private static string Safe(string candidate)
    {
        if (string.IsNullOrEmpty(candidate))
        {
            return candidate;
        }
        string norm = candidate.Replace('\\', '/');
        return norm.Contains('/') ? norm.Substring(norm.LastIndexOf('/') + 1) : norm;
    }
}
