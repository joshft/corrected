using System;
using System.Linq;
using System.Reflection;
using Corrected.Gate;
using Corrected.Gate.Kernel;
using Xunit;

namespace Corrected.Gate.Tests;

/// <summary>
/// INV-003: validated construction — private DTO -> validate -> immutable domain
/// type via a single public static TryCreate; private instance ctor; pinned type
/// homes (EXT9-08). All [unit].
/// </summary>
public class Inv003ValidatedConstructionTests
{
    public static readonly (Type Type, string Assembly)[] ProtectedTypes =
    {
        (typeof(ReadinessBlock), "Corrected.Gate.Kernel"),
        (typeof(ProbeResult), "Corrected.Gate.Kernel"),
        (typeof(AdrLintBlock), "Corrected.Gate"),
    };

    // Tests INV-003 [unit]: pinned type homes — ReadinessBlock/ProbeResult in
    // Corrected.Gate.Kernel, AdrLintBlock in Corrected.Gate (EXT9-08).
    [Fact]
    public void Type_homes_are_pinned()
    {
        foreach (var (t, asm) in ProtectedTypes)
        {
            Assert.Equal(asm, t.Assembly.GetName().Name);
        }
    }

    // Tests INV-003 [unit]: GetConstructors(Instance | Public) is EMPTY for all
    // three (NOT Public alone, which omits instance ctors and vacuously passes —
    // EXT9-07). So no public constructor / no `with` reaches an invalid state.
    [Fact]
    public void No_public_instance_constructor()
    {
        foreach (var (t, _) in ProtectedTypes)
        {
            var ctors = t.GetConstructors(BindingFlags.Instance | BindingFlags.Public);
            Assert.Empty(ctors);
        }
    }

    // Tests INV-003 [unit]: exactly ONE public static TryCreate per type — the
    // single chosen form for ALL THREE (no internal+InternalsVisibleTo variant so
    // implementations cannot diverge; EXT9-08).
    [Fact]
    public void Single_public_static_TryCreate_per_type()
    {
        foreach (var (t, _) in ProtectedTypes)
        {
            var tryCreate = t.GetMethods(BindingFlags.Static | BindingFlags.Public)
                             .Where(m => m.Name == "TryCreate")
                             .ToArray();
            Assert.Single(tryCreate);
        }
    }

    // Tests INV-003 [unit]: no property is publicly settable (no init/set), so a
    // `with` cannot reach an invalid state (RS-T-17).
    [Fact]
    public void No_publicly_settable_property()
    {
        foreach (var (t, _) in ProtectedTypes)
        {
            foreach (var p in t.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                var setter = p.GetSetMethod(nonPublic: false);
                Assert.Null(setter);
            }
        }
    }

    // Tests INV-003 [unit]: invalid input CANNOT construct — TryCreate returns null
    // (does not silently build an invalid ProbeResult). Behavioral; RED against the
    // stub which throws NotImplemented.
    [Fact]
    public void Invalid_input_cannot_construct_ProbeResult()
    {
        ProbeResult? r = ProbeResult.TryCreate(satisfied: true, reason: "", referenceResolution: ReferenceResolution.Resolved);
        Assert.Null(r); // empty reason is invalid
    }

    // Tests INV-003 [unit]: a valid ProbeResult DOES construct through TryCreate.
    [Fact]
    public void Valid_input_constructs_ProbeResult()
    {
        ProbeResult? r = ProbeResult.TryCreate(satisfied: false, reason: ProbeReasons.ValidatorDeferred, referenceResolution: ReferenceResolution.Resolved);
        Assert.NotNull(r);
        Assert.False(r!.Satisfied);
        Assert.Equal(ProbeReasons.ValidatorDeferred, r.Reason);
    }
}
