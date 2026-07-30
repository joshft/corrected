// PR1 (Group A) qa-r1 hardening tests — emitter CLI robustness + atomic-write hygiene.
//
// EH-F2/IB (E): a MISSING value for a known emitter flag must exit 2 (the documented
// argument-validation code in the README exit table), NEVER crash uncaught (~exit 134).
// The arg-parse loop's Next() throw is now caught inside the loop's try and mapped to 2.
//
// RLT-2 (F): AtomicWrite must not orphan its `.tmp-<pid>` staging file if File.Move
// fails — the try/finally deletes it while preserving the original write fault.
using Corrected.Spike.Contracts;
using Xunit;

namespace Corrected.Spike.Tests;

public class Pr1EmitterHardeningTests
{
    // Tests [unit] (EH-F2/IB): a trailing known flag with no following value makes
    // Next() throw ArgumentException; that must be mapped to the documented exit 2,
    // not propagate as an unhandled crash. RunCli is a public CLI entrypoint.
    [Fact]
    public void RunCli_MissingFlagValue_ReturnsTwo_NotCrash()
    {
        Assert.Equal(2, DeterminismReceiptWriter.RunCli(new[] { "--r1" }));
        Assert.Equal(2, DeterminismReceiptWriter.RunCli(new[] { "--schema", "s", "--out" }));
    }

    // Tests [unit] (EH-F2): the sibling regen CLI has the same arg-parse shape and the
    // same fix — a missing flag value exits 2, never crashes.
    [Fact]
    public void PrintProjectionImplDigestCli_MissingFlagValue_ReturnsTwo_NotCrash()
    {
        Assert.Equal(2, DeterminismReceiptWriter.PrintProjectionImplDigestCli(new[] { "--schema" }));
        Assert.Equal(2, DeterminismReceiptWriter.PrintProjectionImplDigestCli(new[] { "--vector" }));
    }

    // Tests [unit] (RLT-2): when File.Move fails (destination is an existing directory),
    // AtomicWrite must still throw (the fault is never swallowed) AND leave no orphaned
    // `.tmp-<pid>` staging file beside the destination.
    [Fact]
    public void AtomicWrite_MoveFails_LeavesNoOrphanTempFile()
    {
        var scratch = Directory.CreateTempSubdirectory("corrected-atomicwrite-").FullName;
        try
        {
            // Make the destination an existing DIRECTORY so File.Move(temp, dest) throws.
            var dest = Path.Combine(scratch, "receipt.json");
            Directory.CreateDirectory(dest);

            Assert.ThrowsAny<Exception>(() => DeterminismReceiptWriter.AtomicWrite(dest, "{}"));

            var strays = Directory.GetFiles(scratch, "receipt.json.tmp-*");
            Assert.True(strays.Length == 0,
                "AtomicWrite orphaned a temp file after a failed Move: " + string.Join(", ", strays));
        }
        finally { Directory.Delete(scratch, recursive: true); }
    }
}
