using System;
using System.Collections.Generic;
using Corrected.Gate;
using Corrected.Gate.Kernel;
using Xunit;

namespace Corrected.Gate.Tests;

/// <summary>
/// INV-005: the TOTAL verdict table — reference resolution + declared-vs-actual
/// cross-check. One [unit] fixture per table row, driven with CALLER-SUPPLIED
/// inputs so every branch is reachable.
/// </summary>
public class Inv005VerdictTableTests
{
    private static ReadinessBlock BuildBlock(ReadinessStatus status, (PreconditionId id, bool satisfied, string? evidence) target)
    {
        var pcs = new List<ReadinessPrecondition>
        {
            ReadinessPrecondition.Create(PreconditionId.P1, "p1", false, null, Array.Empty<string>()),
            ReadinessPrecondition.Create(PreconditionId.P2, "p2", false, null, Array.Empty<string>()),
            ReadinessPrecondition.Create(PreconditionId.P3, "p3", false, null, Array.Empty<string>()),
        };
        pcs[(int)target.id] = ReadinessPrecondition.Create(target.id, target.id.ToString(), target.satisfied, target.evidence, Array.Empty<string>());
        return ReadinessBlock.TryCreate(1, status, "P1 AND P2 AND P3", pcs)!;
    }

    private static IReadOnlyDictionary<PreconditionId, ProbeResult> BuildProbes(
        (PreconditionId id, bool actual, ReferenceResolution rr) target)
    {
        ProbeResult Consistent() => ProbeResult.TryCreate(false, ProbeReasons.EvidenceSchemaIncomplete, ReferenceResolution.Resolved)!;
        var map = new Dictionary<PreconditionId, ProbeResult>
        {
            [PreconditionId.P1] = Consistent(),
            [PreconditionId.P2] = Consistent(),
            [PreconditionId.P3] = Consistent(),
        };
        map[target.id] = ProbeResult.TryCreate(target.actual, "probe", target.rr)!;
        return map;
    }

    // Tests INV-005 [unit]: evidence==null AND declared false AND actual false -> consistent.
    [Fact]
    public void Null_false_false_is_consistent()
    {
        var v = ReadinessGate.EvaluateReadiness(
            BuildBlock(ReadinessStatus.BLOCKED, (PreconditionId.P1, false, null)),
            BuildProbes((PreconditionId.P1, false, ReferenceResolution.Resolved)));
        Assert.Equal(VerdictKind.Pass, v.Kind);
    }

    // Tests INV-005 [unit]: evidence==null AND declared true -> Fail.
    [Fact]
    public void Null_declared_true_is_Fail()
    {
        var v = ReadinessGate.EvaluateReadiness(
            BuildBlock(ReadinessStatus.BLOCKED, (PreconditionId.P1, true, null)),
            BuildProbes((PreconditionId.P1, true, ReferenceResolution.Resolved)));
        Assert.Equal(VerdictKind.Fail, v.Kind);
        Assert.Equal(PreconditionId.P1, v.OffendingPrecondition);
    }

    // Tests INV-005 [unit]: evidence==null AND declared false AND actual TRUE -> Fail
    // (BLOCKED-but-actually-satisfied) — the cell making the P1 flip mandatory.
    [Fact]
    public void Null_false_true_is_Fail_blocked_but_actually_satisfied()
    {
        var v = ReadinessGate.EvaluateReadiness(
            BuildBlock(ReadinessStatus.BLOCKED, (PreconditionId.P1, false, null)),
            BuildProbes((PreconditionId.P1, true, ReferenceResolution.Resolved)));
        Assert.Equal(VerdictKind.Fail, v.Kind);
        Assert.Equal(PreconditionId.P1, v.OffendingPrecondition);
    }

    // Tests INV-005 [unit]: evidence!=null AND Unresolvable -> hard Fail (declared false).
    [Fact]
    public void Nonnull_unresolvable_hard_fail_declared_false()
    {
        var v = ReadinessGate.EvaluateReadiness(
            BuildBlock(ReadinessStatus.BLOCKED, (PreconditionId.P1, false, "some-id")),
            BuildProbes((PreconditionId.P1, false, ReferenceResolution.Unresolvable)));
        Assert.Equal(VerdictKind.Fail, v.Kind);
    }

    // Tests INV-005 [unit]: evidence!=null AND Unresolvable -> hard Fail (declared true).
    [Fact]
    public void Nonnull_unresolvable_hard_fail_declared_true()
    {
        var v = ReadinessGate.EvaluateReadiness(
            BuildBlock(ReadinessStatus.READY, (PreconditionId.P1, true, "some-id")),
            BuildProbes((PreconditionId.P1, true, ReferenceResolution.Unresolvable)));
        Assert.Equal(VerdictKind.Fail, v.Kind);
    }

    // Tests INV-005 [unit]: evidence!=null AND Resolved -> cross-check; mismatch -> Fail.
    [Fact]
    public void Nonnull_resolved_mismatch_is_Fail()
    {
        var v = ReadinessGate.EvaluateReadiness(
            BuildBlock(ReadinessStatus.READY, (PreconditionId.P1, true, "gate-id")),
            BuildProbes((PreconditionId.P1, false, ReferenceResolution.Resolved)));
        Assert.Equal(VerdictKind.Fail, v.Kind);
    }

    // Tests INV-005 [unit]: BLOCKED + all probes true -> Fail.
    [Fact]
    public void Blocked_all_probes_true_is_Fail()
    {
        var block = BuildBlock(ReadinessStatus.BLOCKED, (PreconditionId.P1, false, null));
        var probes = new Dictionary<PreconditionId, ProbeResult>
        {
            [PreconditionId.P1] = ProbeResult.TryCreate(true, "probe", ReferenceResolution.Resolved)!,
            [PreconditionId.P2] = ProbeResult.TryCreate(true, "probe", ReferenceResolution.Resolved)!,
            [PreconditionId.P3] = ProbeResult.TryCreate(true, "probe", ReferenceResolution.Resolved)!,
        };
        var v = ReadinessGate.EvaluateReadiness(block, probes);
        Assert.Equal(VerdictKind.Fail, v.Kind);
    }

    // Tests INV-005 [unit]: READY + all-true + all-resolved -> Pass.
    [Fact]
    public void Ready_all_true_all_resolved_is_Pass()
    {
        var pcs = new List<ReadinessPrecondition>
        {
            ReadinessPrecondition.Create(PreconditionId.P1, "p1", true, "gate-id-1", Array.Empty<string>()),
            ReadinessPrecondition.Create(PreconditionId.P2, "p2", true, "gate-id-2", Array.Empty<string>()),
            ReadinessPrecondition.Create(PreconditionId.P3, "p3", true, "gate-id-3", Array.Empty<string>()),
        };
        var block = ReadinessBlock.TryCreate(1, ReadinessStatus.READY, "P1 AND P2 AND P3", pcs)!;
        var probes = new Dictionary<PreconditionId, ProbeResult>
        {
            [PreconditionId.P1] = ProbeResult.TryCreate(true, "probe", ReferenceResolution.Resolved)!,
            [PreconditionId.P2] = ProbeResult.TryCreate(true, "probe", ReferenceResolution.Resolved)!,
            [PreconditionId.P3] = ProbeResult.TryCreate(true, "probe", ReferenceResolution.Resolved)!,
        };
        var v = ReadinessGate.EvaluateReadiness(block, probes);
        Assert.Equal(VerdictKind.Pass, v.Kind);
    }

    // Tests INV-005 [unit]: status indeterminate -> Fail (and INV-011 ban stays active).
    [Fact]
    public void Indeterminate_is_Fail()
    {
        var v = ReadinessGate.EvaluateReadiness(
            ReadinessBlock.Indeterminate(),
            BuildProbes((PreconditionId.P1, false, ReferenceResolution.Resolved)));
        Assert.Equal(VerdictKind.Fail, v.Kind);
    }
}
