// PR1 — route-b determinism flap fix (INV-003). The P3 determinism lane caught a
// real non-determinism: `deterministic.sentinel_ledger_outcomes.invocations_for_this_nonce`
// flapped 2 (r1) vs 1 (r2), flipping the route-b projection SHA -> comparison_status=
// different. ROOT CAUSE: P05 verifies against a RECORDING sentinel stub that dies
// mid-protocol (by design); Boogie restarts the "crashed" prover a TIMING-dependent
// number of times, and each restart appends one ledger entry, so the RAW entry count
// is non-deterministic. The exact restart count is prover-retry noise, not a
// compatibility claim (P05's own invariant is delta>=1, count-INsensitive).
//
// FIX (fix-at-source, mechanism 3): leave the append-only ledger and its MA-RB-3
// no-drop armor 100% untouched; change only how the emitted receipt DERIVES the
// count — from the timing-variable RAW entry count to the DISTINCT sentinel probe-leg
// count, SCOPED TO THIS RUN'S OWN stub tags. Every restart of one probe's stub shares
// that probe's single tag, so N restarts collapse to 1. Scoping to own tags is
// load-bearing: route-a and route-b SHARE one ledger under one nonce in a run root, so
// an unscoped count would let route-b borrow route-a's invocation (read 2 instead of 1).
//
// These are fast synthetic-ledger unit tests — they do NOT consume the DeterminismLaneFixture
// floor-gate and carry NO determinism-lane trait, so they run on the 4-vCPU general gate.
using Corrected.Spike.Contracts;
using Xunit;

namespace Corrected.Spike.Tests;

public class Pr1SentinelInvocationDeterminismTests
{
    private static readonly IReadOnlyDictionary<string, string> StubEnv =
        new Dictionary<string, string> { ["PATH"] = "/usr/bin:/bin" };

    private static string MintLedger(string scratch, string nonce)
    {
        var ledger = Path.Combine(scratch, "sentinel", "ledger.json");
        Directory.CreateDirectory(Path.GetDirectoryName(ledger)!);
        File.WriteAllText(ledger, $"{{ \"nonce\": \"{nonce}\", \"entries\": [] }}\n");
        return ledger;
    }

    private static string WriteStub(string scratch, string ledger, string nonce, string tag)
    {
        var stub = Path.Combine(scratch, "sentinel", $"stub-{tag}");
        File.WriteAllText(stub, HarnessCore.SentinelStubScript(ledger, nonce, tag));
        File.SetUnixFileMode(stub,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
            UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
            UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        return stub;
    }

    private static void Invoke(string stub, string scratch, int argSalt) =>
        Assert.Equal(0, ManagedLauncher.Launch(
            new LaunchRequest(stub, new[] { "-smt2", $"vc{argSalt}" }, scratch, StubEnv, 30)).ExitCode);

    private static int Count(string ledger, string nonce, params string[] ownTags) =>
        HarnessCore.DistinctInvokedNonceTagCount(ledger, nonce, ownTags);

    private static int Raw(string ledger) =>
        HarnessCore.ReadSentinelLedgerDetailed(ledger, null).EntriesForNonce;

    // Tests INV-003 [integration] — the fix's core determinism invariant: the DISTINCT
    // probe-leg count is INVARIANT to restart multiplicity. Invoking ONE probe's stub
    // an arbitrary (timing-dependent) number of times — exactly what Boogie's prover-
    // restart does — must always yield the SAME derived count of 1, even though the raw
    // append-only ledger grows with every invocation. This is the precise 2-vs-1 flap
    // reproduced: 1 launch and 3 launches of the same tag both derive to 1.
    [Fact]
    public void DistinctTagCount_IsInvariantToRestartMultiplicity()
    {
        var scratch = SpikePaths.TestScratch("pr1-inv-determinism-restart");
        const string nonce = "nonce-pr1-restart";
        var ledger = MintLedger(scratch, nonce);
        var own = "probe:P05-restart0001";
        var p05 = WriteStub(scratch, ledger, nonce, "P05-restart0001");

        // One launch (the r2 world): raw=1, derived=1.
        Invoke(p05, scratch, 0);
        Assert.Equal(1, Raw(ledger));
        Assert.Equal(1, Count(ledger, nonce, own));

        // Two MORE launches of the SAME tag (the r1 world, plus one) — Boogie restarting
        // the crashed prover. Raw grows to 3; the DERIVED count must NOT move off 1.
        Invoke(p05, scratch, 1);
        Invoke(p05, scratch, 2);
        Assert.Equal(3, Raw(ledger));                 // armor: nothing dropped
        Assert.Equal(1, Count(ledger, nonce, own));   // STILL 1 — deterministic
    }

    // Tests INV-003 [integration] — CROSS-ROLE SCOPING regression guard (the exact bug an
    // unscoped count had): route-a and route-b SHARE one ledger under one nonce, so a
    // sibling role's fired stub tag is present in this role's ledger. The count MUST scope
    // to this run's OWN stub tags — a sibling's tag must not inflate it. Here the sibling
    // ("roleA") and this run ("roleB") each fire once; scoped to roleB's own tag the count
    // is 1, NOT 2, even though the raw ledger holds both entries.
    [Fact]
    public void DistinctTagCount_ExcludesSiblingRoleTags_InTheSharedLedger()
    {
        var scratch = SpikePaths.TestScratch("pr1-inv-determinism-crossrole");
        const string nonce = "nonce-pr1-shared";
        var ledger = MintLedger(scratch, nonce);
        var siblingStub = WriteStub(scratch, ledger, nonce, "P05-roleA00001"); // route-a's leg
        var ownStub = WriteStub(scratch, ledger, nonce, "P05-roleB00001");     // this run's leg

        Invoke(siblingStub, scratch, 0); // the sibling role fired into the shared ledger first
        Invoke(ownStub, scratch, 0);

        Assert.Equal(2, Raw(ledger)); // both roles' entries live in the shared ledger
        Assert.Equal(1, Count(ledger, nonce, "probe:P05-roleB00001")); // scoped to OWN tag -> 1, not 2
        Assert.Equal(1, Count(ledger, nonce, "probe:P05-roleA00001")); // the sibling's own view is also 1
    }

    // Tests INV-003 [integration]: the derived count is a genuine COUNT of distinct OWN
    // legs, not a hard-coded 1 — a second own probe-leg that fires the real binary raises
    // it to 2. This proves the measure scales structurally (future multi-sentinel plans
    // stay deterministic) rather than being a disguised boolean.
    [Fact]
    public void DistinctTagCount_CountsDistinctOwnLegs_NotRawEntries()
    {
        var scratch = SpikePaths.TestScratch("pr1-inv-determinism-legs");
        const string nonce = "nonce-pr1-legs";
        var ledger = MintLedger(scratch, nonce);
        var p05 = WriteStub(scratch, ledger, nonce, "P05-legs00001");
        var p07 = WriteStub(scratch, ledger, nonce, "P07-legs00002");
        var own = new[] { "probe:P05-legs00001", "probe:P07-legs00002" };

        Invoke(p05, scratch, 0);
        Invoke(p05, scratch, 1); // same leg twice
        Assert.Equal(1, Count(ledger, nonce, own));

        Invoke(p07, scratch, 0); // a second OWN leg fires
        Assert.Equal(3, Raw(ledger));                // raw: 2 + 1
        Assert.Equal(2, Count(ledger, nonce, own));  // distinct own legs: P05, P07
    }

    // Tests INV-003 [unit]: foreign-nonce entries never inflate the derived count — the
    // same scoping the raw reader applies (RS-003d). A ledger whose entries all belong to
    // a DIFFERENT nonce derives to 0 for this run's nonce, even for an own tag.
    [Fact]
    public void DistinctTagCount_ExcludesForeignNonces()
    {
        var scratch = SpikePaths.TestScratch("pr1-inv-determinism-foreign");
        const string thisNonce = "nonce-pr1-mine";
        const string foreignNonce = "nonce-pr1-foreign";
        var ledger = MintLedger(scratch, thisNonce);
        // The stub records under the foreign nonce it was minted with.
        var foreignStub = WriteStub(scratch, ledger, foreignNonce, "P05-foreign0001");
        Invoke(foreignStub, scratch, 0);

        Assert.Equal(1, HarnessCore.ReadSentinelLedgerDetailed(ledger, null).ForeignEntries); // it IS a foreign entry
        Assert.Equal(0, Count(ledger, thisNonce, "probe:P05-foreign0001"));                   // contributes NO leg
    }
}
