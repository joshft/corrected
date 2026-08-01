using System;
using Corrected.Gate;
using Corrected.Gate.Kernel;
using Xunit;

namespace Corrected.Gate.Tests;

/// <summary>
/// MA-H (mini-audit, upgrade-compat/totality lens): six security enums place their ACCEPTING value at
/// ordinal 0, so <c>default(T)</c> is the accepting member — DeterminismVerifyOutcome.Verified,
/// CosignOutcome.Ok, ScanOutcome.Pass, LifecycleVerdict.Success, AncestryStatus.Ancestor,
/// EntryIntegrity.Verified. That is safe ONLY because each verdict is carried by a REFERENCE type,
/// whose default is <c>null</c> (an NRE, never a silent accept) — not a value type that would default
/// to the accepting enum. This guard LOCKS that mitigation: converting any verdict carrier to a value
/// type (record struct / struct) fails here, BEFORE a default-accept trap could ship. (AncestryStatus
/// is additionally pinned to a fail-closed default on its request DTO; EntryIntegrity is a raw enum
/// arg whose default-trap is closed in the entry-receipt verifier producer, INV-030.)
/// </summary>
public class MaEnumDefaultTrapGuardTests
{
    [Theory]
    [InlineData(typeof(DeterminismVerifyResult))]
    [InlineData(typeof(CosignRunResult))]
    [InlineData(typeof(LifecycleGateResult))]
    [InlineData(typeof(ScanResult))]
    public void Security_verdict_carriers_stay_reference_types(Type carrier)
    {
        Assert.False(carrier.IsValueType,
            $"{carrier.Name} must stay a reference type: its verdict enum defaults to the ACCEPTING " +
            "member (ordinal 0), so a value-type carrier would default to accept (MA-H).");
    }

    // The premise the guard rests on: the accepting enum value really is the zero-value. If a future
    // edit reorders the enum so the accepting member is no longer ordinal 0, this documents that the
    // reference-type guard above is what carries the safety (and flags the reorder as a reviewable diff).
    [Fact]
    public void Accepting_enum_members_are_the_zero_value_documenting_the_trap()
    {
        Assert.Equal(0, (int)DeterminismVerifyOutcome.Verified);
        Assert.Equal(0, (int)CosignOutcome.Ok);
        Assert.Equal(0, (int)ScanOutcome.Pass);
        Assert.Equal(0, (int)LifecycleVerdict.Success);
        Assert.Equal(0, (int)AncestryStatus.Ancestor);
    }

    // MA-C / MA-H: EntryIntegrity is a RAW enum argument (not wrapped in a required-field carrier
    // whose default is a null reference), so — unlike the five carriers above — its zero-value must
    // itself be FAIL-CLOSED. Absent is pinned to ordinal 0, so default(EntryIntegrity) is Absent
    // (ban-active / no-activation / LifecycleVerdict Fail), never the accepting Verified. Reordering
    // the enum so Verified became the default would flip a raw-enum default to accept and fail here.
    [Fact]
    public void EntryIntegrity_raw_enum_default_is_fail_closed_absent_not_verified()
    {
        Assert.Equal(EntryIntegrity.Absent, default(EntryIntegrity));
        Assert.NotEqual(EntryIntegrity.Verified, default(EntryIntegrity));
        Assert.Equal(0, (int)EntryIntegrity.Absent);
        Assert.NotEqual(0, (int)EntryIntegrity.Verified);
    }
}
