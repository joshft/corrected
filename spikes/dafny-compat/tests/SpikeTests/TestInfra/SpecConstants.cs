// Test-suite constant set (RS-002 / INV-009): digests hard-coded HERE, in the
// test suite, so removing or altering a probe, route, or schema field requires
// a three-point change (spec, committed file, test constant) that is auditable.
namespace Corrected.Spike.Tests;

public static class SpecConstants
{
    /// <summary>RS-002: SHA-256 of the committed spec-owned probe manifest (manifest/probe-manifest.json).</summary>
    public const string ProbeManifestSha256 =
        "4956816b40f2cf4316ab2ba3ad9cbb810bb89e0339187c8add7a7d3c2178b0eb";

    /// <summary>INV-009: SHA-256 of the committed evidence schema (schema/evidence-schema.json), anchored beside the manifest digest. (v1 amended in place during RED per TA-B15 then TA-B16/TA-A15 — no evidence was ever emitted under any prior digest; see the registry note.)</summary>
    public const string EvidenceSchemaSha256 =
        "a630b1aa10294b688867ee0cd73574f7c12c15050a2724245b43b3e8b4650259";

    /// <summary>INV-007/RS-018b: SHA-256 of the frozen Option Manifest (manifest/option-manifest.json) — an oracle file, digest-bound. (v2 after TA-A8 per-route canary rows.)</summary>
    public const string OptionManifestSha256 =
        "161d820f5c40321b198048200d89ea6d45a40e5817616f930b9677a381a62090";

    /// <summary>Expected 22 composite (probeID, route) entries: 9 child probes x 2 routes + 2 controller-attested P01 + 2 shared.</summary>
    public const int ExpectedManifestEntryCount = 22;

    /// <summary>Exact family pins (INV-001/PRH-002).</summary>
    public static readonly IReadOnlyDictionary<string, string> FamilyPins = new Dictionary<string, string>
    {
        ["DafnyCore"] = "4.11.0",
        ["DafnyPipeline"] = "4.11.0",
        ["DafnyDriver"] = "4.11.0",
        ["Boogie.ExecutionEngine"] = "3.5.5",
    };

    public const string SystemCommandLinePin = "2.0.0-beta4.22272.1";
    public const string SdkPin = "10.0.302";
    public const string Z3Sha256 = "c5360fd157b0f861ec8780ba3e51e2197e9486798dc93cd878df69a4b0c2b7c5";
    public const string Net8ControlRuntimeVersion = "8.0.29";
    public const string Net8ControlArchiveSha256 = "dba346c5c4357e1befebf14de8c8ee7f09313cc12c7c0015a4cdd4dfd0efba81";

    /// <summary>
    /// INV-008 (codex R3-11): hash-bound whitelist — the negative-compile
    /// fixture files are the SOLE exception to the Dafny/Boogie source grep.
    /// Relative to tests/SpikeTests/fixtures/negative-compile/.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> NegativeCompileFixtureSha256 = new Dictionary<string, string>
    {
        ["PositiveConsumer.cs.txt"] = "113357cb35f1bc6f4aa63c71c8bb244d4386ef2175baba590a75cc69035c917a",
        ["ForbiddenTypeNaming.cs.txt"] = "2292a06468dda414c5b0e9d1c05e75d2d9b2fbf9db8f4fab438ef19a7e979b9c",
        ["ForbiddenPropertyAccess.cs.txt"] = "aeb9490e063b10ef270054aca558248684590fea4723605350cb32de7475d8ee",
        ["ForbiddenInterfaceImpl.cs.txt"] = "2faec7ec0ddf9c88e573fcea6068fc6e9f07ec0388ac794330e4858899b540c1",
        ["ForbiddenGenericConstraint.cs.txt"] = "bcd605d0d53c301d243e6b63b0ebdf8cbeab8dff62bfaeabe6b32872f153c8e4",
    };

    /// <summary>Unsupported-RID message, exact (INV-014/BND-002).</summary>
    public const string RidFailMessage = "RID not supported by this spike; proven RIDs: linux-x64 (see ADR-0001)";
}
