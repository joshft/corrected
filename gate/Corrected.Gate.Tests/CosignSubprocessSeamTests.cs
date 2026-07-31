using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Corrected.Gate;
using Xunit;

namespace Corrected.Gate.Tests;

/// <summary>
/// P3 determinism-attestation spec INV-014 (the cosign subprocess seam is hardened),
/// DISTINCT from the carrier's same-numbered documented-command invariant
/// (<c>Inv014DocumentedCommandTests</c>). These exercise the hardened <see cref="CosignRunner"/>
/// seam against a FAKE cosign (a shell script written to a per-test temp dir) so the
/// contract is provable WITHOUT the real, pinned cosign binary. Every clause of the
/// invariant is named on the test that encodes it.
///
/// The execution cells are REAL out-of-process subprocess tests (mirroring
/// <c>ClosureBuildRunner</c>'s temp-dir + ProcessStartInfo hygiene): short timeouts keep
/// the suite fast, and every test cleans up its temp dir + any spawned process so nothing
/// leaks. Linux-only helpers (/proc, File.CreateSymbolicLink) are used deliberately; if an
/// environment cannot honor them the test fails LOUDLY rather than being silently weakened.
///
/// A7 RESIDUAL (spec 554, "atomic output handling"): this seam is CAPTURE-ONLY — it returns
/// bounded stdout/stderr + a typed outcome to the caller and writes NO output artifact of its
/// own. Atomic write-to-file of a verification RESULT (temp-file + rename / fsync) belongs to
/// the caller that persists the receipt/verdict, OUTSIDE this seam, so it is not covered here.
/// This is an explicit scoped residual, not a silent omission; the persisting layer's own
/// invariant must carry the atomicity test. Do not treat the absence of an atomic-output cell
/// in this file as coverage of clause 554.
///
/// Fakes use an ABSOLUTE bash shebang (so bash itself starts regardless of the wrapper's env
/// policy); cells whose fake calls EXTERNAL commands (sleep/head/tr/touch/env) allowlist PATH
/// so those commands resolve, while the empty-allowlist clean-env cell uses ONLY bash builtins
/// (export -p) and needs no PATH.
/// </summary>
[Collection("Subprocess")]
public class CosignSubprocessSeamTests
{
    // Resolved once: bash's absolute path (fakes use it as their shebang so they launch
    // independent of the wrapper's PATH policy). Fails LOUDLY at class load if bash is absent.
    private static readonly string BashAbs = ResolveBashAbsolute();

    // ----- Fake cosign script BODIES (shebang is prepended by MakeFake). -----

    private const string BodyHang = """
        # Spawn a grandchild sleeper, record its PID to the pidfile ($1), then block so the
        # tree stays intact when the wrapper process-tree-kills us on timeout.
        sleep 300 &
        echo "$!" > "$1"
        sleep 300
        """;

    private const string BodySpewOut = """
        head -c "$1" /dev/zero | tr '\0' 'A'
        """;

    private const string BodySpewErr = """
        head -c "$1" /dev/zero | tr '\0' 'A' >&2
        """;

    private const string BodyExit = """
        exit "$1"
        """;

    private const string BodyOk = """
        echo "verified-ok"
        exit 0
        """;

    private const string BodyEchoArgv = """
        for a in "$@"; do
          printf 'ARG:%s\n' "$a"
        done
        """;

    // Proves NON-invocation: touches the marker path in $1. If the wrapper rejects the input
    // BEFORE invoking cosign, this never runs and the marker is never created.
    private const string BodyTouchMarker = """
        touch "$1"
        exit 0
        """;

    private const string BodyReached = """
        echo "reached-cosign"
        exit 0
        """;

    // Dumps the child's exported environment using a bash BUILTIN only (no external command,
    // so it runs even with an empty allowlist / no PATH).
    private const string BodyExportEnv = """
        export -p
        """;

    // Proves the fixed working directory: the unique marker file exists ONLY in the intended
    // working dir, so finding it via a RELATIVE path means the child's cwd == WorkingDirectory.
    private const string BodyWorkingDir = """
        if [ -e "wd-marker" ]; then echo "WD-MARKER-PRESENT"; else echo "WD-MARKER-ABSENT"; fi
        pwd
        """;

    private const long Mb = 1_048_576;

    // ============================================================================
    // Clause: process timeout + process-TREE termination.
    // ============================================================================
    [Fact]
    public void Hung_cosign_is_killed_timeout_and_process_tree_terminated_integration()
    {
        // Tests INV-014 [integration]: a hung cosign is killed within a bounded wall-time AND
        // a grandchild it spawned is dead afterwards (process-tree termination). A1: a POSITIVE
        // liveness baseline is asserted while the child is running, so ProcAlive's fail-open
        // (returns "dead" on a /proc read error) cannot yield a vacuous "it was killed" pass.
        string dir = NewTempDir();
        int? capturedGc = null;
        try
        {
            string pidfile = Path.Combine(dir, "grandchild.pid");
            string fake = MakeFake(dir, "fake-hang.sh", BodyHang);

            var opts = new CosignRunOptions
            {
                ExecutablePath = fake,
                Argv = new[] { pidfile },
                WorkingDirectory = dir,
                Timeout = TimeSpan.FromSeconds(2),
                EnvAllowlist = new[] { "PATH" }, // fake calls `sleep` (external)
            };

            var sw = Stopwatch.StartNew();
            Task<CosignRunResult> runTask = Task.Run(() => CosignRunner.Run(opts));

            // While the wrapper is (in GREEN) running the hung fake, capture the grandchild PID.
            while (sw.Elapsed < TimeSpan.FromSeconds(2) && !runTask.IsCompleted)
            {
                if (File.Exists(pidfile))
                {
                    string txt = File.ReadAllText(pidfile).Trim();
                    if (txt.Length > 0)
                    {
                        capturedGc = int.Parse(txt);
                        break;
                    }
                }
                Thread.Sleep(50);
            }

            // A1 positive baseline: if captured, the grandchild MUST be alive right now (proves
            // ProcAlive can observe a live process — not a fail-open dead read). On the deny stub
            // the fake never ran, so capturedGc stays null and the Outcome assertion below is the
            // RED driver.
            if (capturedGc is int liveGc)
            {
                Assert.True(ProcAlive(liveGc), $"baseline: grandchild {liveGc} should be alive while cosign runs.");
            }

            CosignRunResult result = runTask.GetAwaiter().GetResult();
            sw.Stop();

            Assert.Equal(CosignOutcome.Timeout, result.Outcome);
            Assert.True(
                sw.Elapsed < TimeSpan.FromSeconds(30),
                $"wrapper must not hang for the fake's 300s sleep; it returned in {sw.Elapsed}.");

            Assert.True(
                capturedGc is int,
                "ENVIRONMENT/CONTRACT: grandchild PID never recorded — cannot verify tree-kill.");
            int gc = capturedGc!.Value;
            bool dead = PollUntil(() => !ProcAlive(gc), TimeSpan.FromSeconds(8));
            Assert.True(dead, $"grandchild PID {gc} survived — the process TREE was not terminated.");
        }
        finally
        {
            // Defensive: if a GREEN run left the grandchild alive, do not leak it.
            if (capturedGc is int g)
            {
                try { using var p = Process.GetProcessById(g); p.Kill(entireProcessTree: true); }
                catch { /* already gone */ }
            }
            Cleanup(dir);
        }
    }

    // ============================================================================
    // Clause: size caps on captured stdout.
    // ============================================================================
    [Fact]
    public void Oversized_stdout_is_capped_and_flagged_integration()
    {
        // Tests INV-014 [integration]: a cosign that spews past the stdout cap yields the
        // OversizeOutput outcome and the captured buffer never exceeds the cap.
        RunSpew(toStderr: false, spewBytes: 200_000, stdOutCap: 4096, stdErrCap: Mb, out CosignRunResult r);
        Assert.Equal(CosignOutcome.OversizeOutput, r.Outcome);
        Assert.True(r.StdOut.Length <= 4096, $"captured stdout ({r.StdOut.Length}) exceeded the cap (4096).");
        Assert.True(r.OutputTruncated, "OutputTruncated must be set when the cap is exceeded.");
    }

    // ============================================================================
    // Clause: size caps on captured STDERR (B2i — stderr, not just stdout).
    // ============================================================================
    [Fact]
    public void Oversized_stderr_is_capped_and_flagged_integration()
    {
        // Tests INV-014 [integration]: the reference ClosureBuildRunner reads stderr UNBOUNDED;
        // a hardened seam must cap stderr too, or it is an unbounded-memory DoS hole. A cosign
        // that spews past the stderr cap yields OversizeOutput and a bounded stderr buffer.
        RunSpew(toStderr: true, spewBytes: 200_000, stdOutCap: Mb, stdErrCap: 4096, out CosignRunResult r);
        Assert.Equal(CosignOutcome.OversizeOutput, r.Outcome);
        Assert.True(r.StdErr.Length <= 4096, $"captured stderr ({r.StdErr.Length}) exceeded the cap (4096).");
        Assert.True(r.OutputTruncated, "OutputTruncated must be set when the stderr cap is exceeded.");
    }

    // ============================================================================
    // Clause: size caps — off-by-one boundary (A5).
    // ============================================================================
    [Fact]
    public void Stdout_at_or_under_cap_is_Ok_and_not_truncated_integration()
    {
        // Tests INV-014 [integration]: output EXACTLY at the cap (and just under) is Ok and is
        // NOT flagged/truncated — a > (not >=) comparison, so legitimate at-cap output survives.
        const long cap = 1024;

        RunSpew(toStderr: false, spewBytes: cap - 1, stdOutCap: cap, stdErrCap: Mb, out CosignRunResult under);
        Assert.Equal(CosignOutcome.Ok, under.Outcome);
        Assert.False(under.OutputTruncated, "under-cap output must not be flagged truncated.");
        Assert.Equal((int)(cap - 1), under.StdOut.Length);

        RunSpew(toStderr: false, spewBytes: cap, stdOutCap: cap, stdErrCap: Mb, out CosignRunResult at);
        Assert.Equal(CosignOutcome.Ok, at.Outcome);
        Assert.False(at.OutputTruncated, "output exactly AT the cap must not be flagged truncated (off-by-one).");
        Assert.Equal((int)cap, at.StdOut.Length);
    }

    [Fact]
    public void Stdout_one_byte_over_cap_is_flagged_integration()
    {
        // Tests INV-014 [integration]: cap+1 bytes is over — OversizeOutput, capped buffer.
        const long cap = 1024;
        RunSpew(toStderr: false, spewBytes: cap + 1, stdOutCap: cap, stdErrCap: Mb, out CosignRunResult over);
        Assert.Equal(CosignOutcome.OversizeOutput, over.Outcome);
        Assert.True(over.StdOut.Length <= cap, $"captured stdout ({over.StdOut.Length}) exceeded the cap ({cap}).");
    }

    // ============================================================================
    // Clause: exact exit-code / error taxonomy (typed outcomes, no raw passthrough).
    // ============================================================================
    [Fact]
    public void Nonzero_exit_maps_to_typed_NonZeroExit_carrying_the_code_integration()
    {
        // Tests INV-014 [integration]: a specific non-zero exit maps to NonZeroExit and carries
        // the exact code — never an undefined/raw passthrough.
        string dir = NewTempDir();
        try
        {
            string fake = MakeFake(dir, "fake-exit.sh", BodyExit);
            CosignRunResult result = CosignRunner.Run(new CosignRunOptions
            {
                ExecutablePath = fake,
                Argv = new[] { "7" },
                WorkingDirectory = dir,
                Timeout = TimeSpan.FromSeconds(10),
                EnvAllowlist = new[] { "PATH" },
            });

            Assert.Equal(CosignOutcome.NonZeroExit, result.Outcome);
            Assert.Equal(7, result.ExitCode);
        }
        finally { Cleanup(dir); }
    }

    [Fact]
    public void Zero_exit_with_valid_output_maps_to_Ok_integration()
    {
        // Tests INV-014 [integration]: exit 0 with in-bounds output maps to Ok(0).
        string dir = NewTempDir();
        try
        {
            string fake = MakeFake(dir, "fake-ok.sh", BodyOk);
            CosignRunResult result = CosignRunner.Run(new CosignRunOptions
            {
                ExecutablePath = fake,
                Argv = Array.Empty<string>(),
                WorkingDirectory = dir,
                Timeout = TimeSpan.FromSeconds(10),
                StdOutCapBytes = Mb,
                EnvAllowlist = new[] { "PATH" },
            });

            Assert.Equal(CosignOutcome.Ok, result.Outcome);
            Assert.Equal(0, result.ExitCode);
            Assert.Contains("verified-ok", result.StdOut);
        }
        finally { Cleanup(dir); }
    }

    // ============================================================================
    // Clause: exact taxonomy — LaunchFailed is distinct from NonZeroExit/InputRejected/throw (A4).
    // ============================================================================
    [Fact]
    public void Absolute_but_unlaunchable_exe_maps_to_LaunchFailed_integration()
    {
        // Tests INV-014 [integration]: an ABSOLUTE path that passes the absolute-path check but
        // cannot be executed (missing file, or a non-executable regular file) maps to the typed
        // LaunchFailed — never a raw throw, a NonZeroExit, or a generic InputRejected.
        string dir = NewTempDir();
        try
        {
            // (a) absolute path to a file that does not exist.
            string missing = Path.Combine(dir, "cosign-does-not-exist-" + Guid.NewGuid().ToString("N"));
            CosignRunResult r1 = CosignRunner.Run(new CosignRunOptions
            {
                ExecutablePath = missing,
                Argv = Array.Empty<string>(),
                WorkingDirectory = dir,
                Timeout = TimeSpan.FromSeconds(5),
                EnvAllowlist = new[] { "PATH" },
            });
            Assert.Equal(CosignOutcome.LaunchFailed, r1.Outcome);

            // (b) an absolute path to a real, regular, NON-executable file.
            string notExec = Path.Combine(dir, "not-executable.bin");
            File.WriteAllText(notExec, "not a program");
            File.SetUnixFileMode(notExec, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            CosignRunResult r2 = CosignRunner.Run(new CosignRunOptions
            {
                ExecutablePath = notExec,
                Argv = Array.Empty<string>(),
                WorkingDirectory = dir,
                Timeout = TimeSpan.FromSeconds(5),
                EnvAllowlist = new[] { "PATH" },
            });
            Assert.Equal(CosignOutcome.LaunchFailed, r2.Outcome);
        }
        finally { Cleanup(dir); }
    }

    // ============================================================================
    // Clause: clean environment (no ambient HOME/TUF/config passthrough) — A2.
    // ============================================================================
    [Fact]
    public void Ambient_environment_is_not_inherited_path_allowlist_integration()
    {
        // Tests INV-014 [integration]: with an explicit allowlist, ambient env set in the PARENT
        // must NOT reach the child.
        //
        // DECISION (robustness): a literal "child env keys are a SUBSET of the allowlist" assert
        // is NOT used because this environment's bash sources a startup rc that RE-INJECTS keys
        // into a cleared-env child (empirically: NVM_DIR, and PATH re-set to the parent value) —
        // verified via `env -i PATH=... bash -c env`. So a key-subset check would be flaky. The
        // robust, stronger equivalent is used instead: (1) distinctive GUID-tagged sentinels
        // (keys AND values) are proven absent; (2) real ambient HOME/USER/LOGNAME values are
        // proven not present under their own key; (3) a leak sweep proves NO non-exempt child
        // key carries its PARENT value. Exempt keys are shell/rc-runtime only (PATH/PWD/OLDPWD/
        // SHLVL/_/NVM_DIR/LS_COLORS) — never HOME/TUF/SIGSTORE/COSIGN/XDG/cloud creds.
        RunCleanEnv(new[] { "PATH" }, out CosignRunResult result, out Dictionary<string, string> childEnv,
            out string dump, out string sentinelKey, out string sentinelVal, out string tufVal);

        Assert.Equal(CosignOutcome.Ok, result.Outcome);
        Assert.Contains("declare", dump); // proves export -p actually ran

        // Sentinels: distinctive keys AND guid values gone.
        Assert.False(childEnv.ContainsKey(sentinelKey), "custom sentinel key leaked into the cosign child.");
        Assert.False(childEnv.ContainsKey("TUF_ROOT"), "ambient TUF_ROOT leaked into the cosign child.");
        Assert.DoesNotContain(sentinelVal, dump);
        Assert.DoesNotContain(tufVal, dump);

        // Real ambient config values must not appear under their own key.
        AssertAmbientKeyValueAbsent(childEnv, "HOME");
        AssertAmbientKeyValueAbsent(childEnv, "USER");
        AssertAmbientKeyValueAbsent(childEnv, "LOGNAME");

        // Comprehensive leak sweep: no non-exempt child key carries its parent value.
        var exempt = new HashSet<string>(StringComparer.Ordinal)
            { "PATH", "PWD", "OLDPWD", "SHLVL", "_", "NVM_DIR", "LS_COLORS" };
        foreach (KeyValuePair<string, string> kv in childEnv)
        {
            if (exempt.Contains(kv.Key))
            {
                continue;
            }
            string? parentVal = Environment.GetEnvironmentVariable(kv.Key);
            if (!string.IsNullOrEmpty(parentVal))
            {
                Assert.False(
                    string.Equals(parentVal, kv.Value, StringComparison.Ordinal),
                    $"ambient env '{kv.Key}' leaked into the cosign child with its parent value.");
            }
        }
    }

    [Fact]
    public void Ambient_environment_is_not_inherited_empty_allowlist_integration()
    {
        // Tests INV-014 [integration]: the strip happens even with the DEFAULT/empty allowlist —
        // a wrapper that only clears env when an allowlist is non-empty would be a fail-open bug.
        // Builtin-only fake (export -p) so it needs no PATH.
        RunCleanEnv(Array.Empty<string>(), out CosignRunResult result, out Dictionary<string, string> childEnv,
            out string dump, out string sentinelKey, out string sentinelVal, out string tufVal);

        Assert.Equal(CosignOutcome.Ok, result.Outcome);
        Assert.Contains("declare", dump);

        Assert.False(childEnv.ContainsKey(sentinelKey), "custom sentinel key leaked (empty allowlist).");
        Assert.False(childEnv.ContainsKey("TUF_ROOT"), "ambient TUF_ROOT leaked (empty allowlist).");
        Assert.DoesNotContain(sentinelVal, dump);
        Assert.DoesNotContain(tufVal, dump);
        AssertAmbientKeyValueAbsent(childEnv, "HOME");
        AssertAmbientKeyValueAbsent(childEnv, "USER");
        AssertAmbientKeyValueAbsent(childEnv, "LOGNAME");
    }

    // ============================================================================
    // Clause: argv array (AP-008 no interpolation) + no response-file/config injection + A8.
    // ============================================================================
    [Fact]
    public void Argv_is_passed_verbatim_as_an_array_with_no_injected_elements_integration()
    {
        // Tests INV-014 [integration]: the wrapper passes EXACTLY the supplied argv array, in
        // order, with NOTHING injected (no @response-file, no --config), an embedded-space
        // element kept intact, AND shell metacharacters (A8) delivered as ONE uninterpreted arg
        // each (proving execve-style argv, not shell interpolation).
        string dir = NewTempDir();
        try
        {
            string fake = MakeFake(dir, "fake-echo-argv.sh", BodyEchoArgv);
            var argv = new[]
            {
                "verify-blob",
                "--bundle", "receipt bundle.json", // embedded space MUST survive as ONE element
                "--certificate-identity", "https://issuer/subject",
                "$(id)",                            // command substitution — must NOT be evaluated
                "a;b|c",                            // separators/pipe — must NOT be split/interpreted
                "*",                                // glob — must NOT be expanded
                "--new-bundle-format",
            };
            CosignRunResult result = CosignRunner.Run(new CosignRunOptions
            {
                ExecutablePath = fake,
                Argv = argv,
                WorkingDirectory = dir,
                Timeout = TimeSpan.FromSeconds(10),
                StdOutCapBytes = Mb,
                EnvAllowlist = new[] { "PATH" },
            });

            Assert.Equal(CosignOutcome.Ok, result.Outcome);
            string[] echoed = result.StdOut
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Where(l => l.StartsWith("ARG:", StringComparison.Ordinal))
                .Select(l => l.Substring(4))
                .ToArray();

            // Exactly the supplied argv — nothing added, nothing split, nothing expanded.
            Assert.Equal(argv, echoed);
            Assert.Contains("$(id)", echoed);   // literal, un-substituted
            Assert.Contains("a;b|c", echoed);   // one element, not three
            Assert.DoesNotContain("uid=", result.StdOut); // `id` never actually ran
        }
        finally { Cleanup(dir); }
    }

    // ============================================================================
    // Clause: absolute pinned executable path (a non-absolute path is rejected) + A6 attribution.
    // ============================================================================
    [Theory]
    [InlineData("cosign")]
    [InlineData("./cosign")]
    [InlineData("bin/cosign")]
    [InlineData("")]
    [InlineData("   ")]
    public void Non_absolute_executable_path_is_rejected_with_attributed_reason(string exePath)
    {
        // Tests INV-014: only an absolute pinned executable path is accepted; a relative, bare,
        // or empty path is rejected BEFORE any process starts, and (A6) the RejectReason
        // attributes the rejection to non-absoluteness — a reject for an unrelated reason fails.
        string dir = NewTempDir();
        try
        {
            CosignRunResult result = CosignRunner.Run(new CosignRunOptions
            {
                ExecutablePath = exePath,
                Argv = Array.Empty<string>(),
                WorkingDirectory = dir,
                Timeout = TimeSpan.FromSeconds(5),
            });

            Assert.Equal(CosignOutcome.InputRejected, result.Outcome);
            AssertReasonMentions(result.RejectReason, "absolute", "relative", "path");
        }
        finally { Cleanup(dir); }
    }

    // ============================================================================
    // Clause: regular-file / no-symlink checks on file inputs (B1 non-invocation + A6).
    // ============================================================================
    [Fact]
    public void Symlinked_file_input_is_rejected_before_invoking_cosign_integration()
    {
        // Tests INV-014 [integration]: a SYMLINK passed as a receipt/bundle/root input is
        // rejected by the regular-file check BEFORE cosign is invoked. B1: the fake touches a
        // marker; after the wrapper returns the marker must NOT exist — proving cosign never
        // ran (so a GREEN that runs cosign THEN notices the symlink cannot pass). A6: the
        // RejectReason attributes the rejection to symlink-ness / non-regular-file.
        string dir = NewTempDir();
        try
        {
            string real = Path.Combine(dir, "receipt.json");
            File.WriteAllText(real, "{}");
            string link = Path.Combine(dir, "receipt.link.json");
            File.CreateSymbolicLink(link, real); // fails LOUDLY if the environment forbids symlinks

            string marker = Path.Combine(dir, "invoked.marker");
            string fake = MakeFake(dir, "fake-touch.sh", BodyTouchMarker);

            CosignRunResult result = CosignRunner.Run(new CosignRunOptions
            {
                ExecutablePath = fake,
                Argv = new[] { marker },      // the fake touches this IF it ever runs
                WorkingDirectory = dir,
                FileInputs = new[] { link },  // the SYMLINK is the input
                Timeout = TimeSpan.FromSeconds(5),
                EnvAllowlist = new[] { "PATH" },
            });

            Assert.Equal(CosignOutcome.InputRejected, result.Outcome);
            Assert.False(File.Exists(marker), "cosign was INVOKED before the symlink input was rejected (B1).");
            AssertReasonMentions(result.RejectReason, "symlink", "regular");
        }
        finally { Cleanup(dir); }
    }

    [Fact]
    public void Regular_file_input_is_accepted_and_reaches_cosign_integration()
    {
        // Tests INV-014 [integration]: the no-symlink check does NOT over-reject a genuine
        // regular-file input — it reaches cosign and yields Ok. (Positive control.)
        string dir = NewTempDir();
        try
        {
            string real = Path.Combine(dir, "receipt.json");
            File.WriteAllText(real, "{}");
            string fake = MakeFake(dir, "fake-reached.sh", BodyReached);
            CosignRunResult result = CosignRunner.Run(new CosignRunOptions
            {
                ExecutablePath = fake,
                Argv = Array.Empty<string>(),
                WorkingDirectory = dir,
                FileInputs = new[] { real },
                Timeout = TimeSpan.FromSeconds(10),
                StdOutCapBytes = Mb,
                EnvAllowlist = new[] { "PATH" },
            });

            Assert.Equal(CosignOutcome.Ok, result.Outcome);
            Assert.Contains("reached-cosign", result.StdOut);
        }
        finally { Cleanup(dir); }
    }

    // ============================================================================
    // Clause: size caps on INPUT files (B2ii).
    // ============================================================================
    [Fact]
    public void Oversized_input_file_is_rejected_before_invoking_cosign_integration()
    {
        // Tests INV-014 [integration]: an oversized regular-file input (receipt/bundle/root)
        // must be rejected BEFORE cosign is invoked (an unbounded input read is a DoS hole).
        //
        // DECISION (B2ii): I chose the COMPILING behavioral test over referencing a not-yet-
        // existent CosignRunOptions.InputCapBytes. The coordinator's constraint "do NOT touch
        // CosignRunner.cs" makes referencing a non-existent property a COMPILE error, which
        // would break acceptance criteria (a) reds-as-assertions and (b) no non-INV014 test
        // flips red (the whole assembly would fail to build). Instead this uses a 512 MiB SPARSE
        // regular file (logical size 512 MiB, ~0 physical bytes) on the EXISTING option surface.
        // It passes fail-closed on the deny stub via Outcome, and in GREEN forces an input-size
        // cap: a wrapper WITHOUT one accepts the file, invokes the marker fake, and returns Ok —
        // failing this test. 512 MiB exceeds any security-reasonable receipt/bundle/root size, so
        // whatever finite cap GREEN picks (constant or a new option — name at GREEN's discretion)
        // rejects it. The B1 non-invocation + A6 attribution asserts make the reject specific.
        string dir = NewTempDir();
        try
        {
            string big = Path.Combine(dir, "oversized-receipt.json");
            using (var fs = new FileStream(big, FileMode.CreateNew, FileAccess.Write))
            {
                fs.SetLength(512L * 1024 * 1024); // sparse: cheap on disk, 512 MiB logical
            }

            string marker = Path.Combine(dir, "invoked.marker");
            string fake = MakeFake(dir, "fake-touch.sh", BodyTouchMarker);

            CosignRunResult result = CosignRunner.Run(new CosignRunOptions
            {
                ExecutablePath = fake,
                Argv = new[] { marker },
                WorkingDirectory = dir,
                FileInputs = new[] { big },
                Timeout = TimeSpan.FromSeconds(5),
                EnvAllowlist = new[] { "PATH" },
            });

            Assert.Equal(CosignOutcome.InputRejected, result.Outcome);
            Assert.False(File.Exists(marker), "cosign was INVOKED before the oversized input was rejected.");
            AssertReasonMentions(result.RejectReason, "size", "large", "cap", "bytes", "big");
        }
        finally { Cleanup(dir); }
    }

    // ============================================================================
    // Clause: fixed working directory (A3).
    // ============================================================================
    [Fact]
    public void Child_runs_in_the_fixed_working_directory_integration()
    {
        // Tests INV-014 [integration]: the child's cwd equals WorkingDirectory. A uniquely-named
        // marker file exists ONLY in the intended dir; the fake finds it via a RELATIVE path, so
        // a hit proves cwd == WorkingDirectory (symlink-path-string comparison is avoided).
        string dir = NewTempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "wd-marker"), "x");
            string fake = MakeFake(dir, "fake-wd.sh", BodyWorkingDir);
            CosignRunResult result = CosignRunner.Run(new CosignRunOptions
            {
                ExecutablePath = fake,
                Argv = Array.Empty<string>(),
                WorkingDirectory = dir,
                Timeout = TimeSpan.FromSeconds(10),
                StdOutCapBytes = Mb,
                EnvAllowlist = new[] { "PATH" },
            });

            Assert.Equal(CosignOutcome.Ok, result.Outcome);
            Assert.Contains("WD-MARKER-PRESENT", result.StdOut);
        }
        finally { Cleanup(dir); }
    }

    // ============================================================================
    // Clause: argv array (AP-008) — code scan of the wrapper source.
    // ============================================================================
    [Fact]
    public void Source_scan_argv_is_passed_as_a_list_not_an_interpolated_command()
    {
        // Tests INV-014: the wrapper builds argv element-by-element via ProcessStartInfo's
        // ArgumentList — proof it does NOT compose a single interpolated command string.
        string src = ReadRunnerSource();
        Assert.Contains("ArgumentList", src);
    }

    [Fact]
    public void Source_scan_no_shell_interpolation_or_shell_execute()
    {
        // Tests INV-014: no shell interpretation of a composed command line (AP-008) — no
        // UseShellExecute=true, no `bash -c`/`sh -c`, no invocation via a shell.
        string src = ReadRunnerSource();
        Assert.DoesNotContain("UseShellExecute = true", src);
        Assert.DoesNotContain("UseShellExecute=true", src);
        Assert.DoesNotContain("bash -c", src);
        Assert.DoesNotContain("sh -c", src);
        Assert.DoesNotContain("/bin/sh", src);
        Assert.DoesNotContain("\"-c\"", src);
    }

    [Fact]
    public void Source_scan_no_response_file_or_config_injection_added_by_wrapper()
    {
        // Tests INV-014: the wrapper adds NOTHING to argv — no cosign @response-file and no
        // injected --config flag of its own; it forwards only the caller-supplied argv.
        string src = ReadRunnerSource();
        Assert.DoesNotContain("ArgumentList.Add(\"@", src);
        Assert.DoesNotContain("ArgumentList.Add(\"--config", src);
        Assert.DoesNotContain("\"@\" +", src);
    }

    // ----- Helpers -----

    private static string ReadRunnerSource()
        => File.ReadAllText(TestPaths.RepoFile("gate", "Corrected.Gate", "CosignRunner.cs"));

    /// <summary>Spew helper: run a fake that writes <paramref name="spewBytes"/> to stdout or stderr.</summary>
    private static void RunSpew(bool toStderr, long spewBytes, long stdOutCap, long stdErrCap, out CosignRunResult result)
    {
        string dir = NewTempDir();
        try
        {
            string fake = MakeFake(dir, "fake-spew.sh", toStderr ? BodySpewErr : BodySpewOut);
            result = CosignRunner.Run(new CosignRunOptions
            {
                ExecutablePath = fake,
                Argv = new[] { spewBytes.ToString() },
                WorkingDirectory = dir,
                Timeout = TimeSpan.FromSeconds(15),
                StdOutCapBytes = stdOutCap,
                StdErrCapBytes = stdErrCap,
                EnvAllowlist = new[] { "PATH" }, // fake calls head/tr (external)
            });
        }
        finally { Cleanup(dir); }
    }

    /// <summary>
    /// Clean-env helper: set distinctive sentinels in the parent, run the export-p fake under
    /// <paramref name="allowlist"/>, and hand back the parsed child environment + raw dump.
    /// </summary>
    private static void RunCleanEnv(
        string[] allowlist,
        out CosignRunResult result,
        out Dictionary<string, string> childEnv,
        out string dump,
        out string sentinelKey,
        out string sentinelVal,
        out string tufVal)
    {
        sentinelKey = "COSIGN_SEAM_SENTINEL";
        string guid = Guid.NewGuid().ToString("N");
        sentinelVal = "seam-sentinel-" + guid;
        tufVal = "/leak/tuf/" + guid;

        string dir = NewTempDir();
        string? priorSentinel = Environment.GetEnvironmentVariable(sentinelKey);
        string? priorTuf = Environment.GetEnvironmentVariable("TUF_ROOT");
        try
        {
            Environment.SetEnvironmentVariable(sentinelKey, sentinelVal);
            Environment.SetEnvironmentVariable("TUF_ROOT", tufVal);

            string fake = MakeFake(dir, "fake-env.sh", BodyExportEnv);
            result = CosignRunner.Run(new CosignRunOptions
            {
                ExecutablePath = fake,
                Argv = Array.Empty<string>(),
                WorkingDirectory = dir,
                Timeout = TimeSpan.FromSeconds(10),
                StdOutCapBytes = Mb,
                EnvAllowlist = allowlist,
            });
            dump = result.StdOut;
            childEnv = ParseExportP(dump);
        }
        finally
        {
            Environment.SetEnvironmentVariable(sentinelKey, priorSentinel);
            Environment.SetEnvironmentVariable("TUF_ROOT", priorTuf);
            Cleanup(dir);
        }
    }

    private static void AssertAmbientKeyValueAbsent(Dictionary<string, string> childEnv, string key)
    {
        string? parentVal = Environment.GetEnvironmentVariable(key);
        if (!string.IsNullOrEmpty(parentVal) && childEnv.TryGetValue(key, out string? childVal))
        {
            Assert.NotEqual(parentVal, childVal);
        }
    }

    /// <summary>Assert the reject reason attributes to at least one expected concept (case-insensitive).</summary>
    private static void AssertReasonMentions(string? reason, params string[] tokens)
    {
        Assert.NotNull(reason);
        string lower = reason!.ToLowerInvariant();
        Assert.True(
            tokens.Any(t => lower.Contains(t, StringComparison.Ordinal)),
            $"RejectReason '{reason}' does not attribute the rejection to any of: {string.Join(", ", tokens)}.");
    }

    /// <summary>Parse `export -p` output (`declare -x KEY="value"` / `declare -x KEY`) to a map.</summary>
    private static Dictionary<string, string> ParseExportP(string dump)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string raw in dump.Split('\n'))
        {
            string line = raw.Trim();
            if (!line.StartsWith("declare ", StringComparison.Ordinal))
            {
                continue;
            }
            Match m = Regex.Match(line, @"^declare\s+-\S+\s+([A-Za-z_][A-Za-z0-9_]*)(?:=(.*))?$");
            if (!m.Success)
            {
                continue;
            }
            string key = m.Groups[1].Value;
            string val = m.Groups[2].Success ? m.Groups[2].Value : "";
            if (val.Length >= 2 && val[0] == '"' && val[val.Length - 1] == '"')
            {
                val = val.Substring(1, val.Length - 2);
            }
            map[key] = val;
        }
        return map;
    }

    private static string ResolveBashAbsolute()
    {
        foreach (string c in new[] { "/bin/bash", "/usr/bin/bash", "/usr/local/bin/bash" })
        {
            if (File.Exists(c))
            {
                return c;
            }
        }
        throw new FileNotFoundException("bash not found at a known absolute path — cosign-seam fakes cannot run.");
    }

    private static string NewTempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "inv014-cosign-seam-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string MakeFake(string dir, string name, string body)
    {
        string path = Path.Combine(dir, name);
        string script = "#!" + BashAbs + "\n" + body.Replace("\r\n", "\n").TrimStart('\n');
        if (!script.EndsWith("\n", StringComparison.Ordinal))
        {
            script += "\n";
        }
        File.WriteAllText(path, script);
        File.SetUnixFileMode(
            path,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
            UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
            UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        return path;
    }

    private static void Cleanup(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
        catch
        {
            // best effort — OS temp cleanup is the backstop
        }
    }

    /// <summary>Linux /proc liveness: false if the pid is gone or in a zombie/dead state.</summary>
    private static bool ProcAlive(int pid)
    {
        try
        {
            string stat = File.ReadAllText($"/proc/{pid}/stat");
            int close = stat.LastIndexOf(')');
            if (close < 0 || close + 2 >= stat.Length)
            {
                return false;
            }
            char state = stat[close + 2];
            return state != 'Z' && state != 'X' && state != 'x';
        }
        catch
        {
            return false; // no /proc entry => not alive
        }
    }

    private static bool PollUntil(Func<bool> cond, TimeSpan timeout)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            if (cond())
            {
                return true;
            }
            Thread.Sleep(50);
        }
        return cond();
    }
}
