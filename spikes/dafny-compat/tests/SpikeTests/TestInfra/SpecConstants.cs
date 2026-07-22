// Test-suite constant set (RS-002 / INV-009): digests hard-coded HERE, in the
// test suite, so removing or altering a probe, route, or schema field requires
// a three-point change (spec, committed file, test constant) that is auditable.
namespace Corrected.Spike.Tests;

public static class SpecConstants
{
    /// <summary>RS-002: SHA-256 of the committed spec-owned probe manifest (manifest/probe-manifest.json).</summary>
    public const string ProbeManifestSha256 =
        "4956816b40f2cf4316ab2ba3ad9cbb810bb89e0339187c8add7a7d3c2178b0eb";

    /// <summary>INV-009: SHA-256 of the committed evidence schema (schema/evidence-schema.json), anchored beside the manifest digest. v2 appended in QA fix round 1 (QA-002 solver_archive_sha256 field + QA-006 suite_status_mask block); the in-place-v1 window closed once evidence existed under the v1 digest, so v2 is a NEW registry row.</summary>
    public const string EvidenceSchemaSha256 =
        "c872c710dd390ff8d8050c059077d0eb7d6ef4f2352fc7bf375403014ac18509";

    /// <summary>The retired v1 digest — its registry row is append-only-frozen; a test asserts it is never removed or altered (INV-009/codex R3-6).</summary>
    public const string EvidenceSchemaV1Sha256 =
        "a630b1aa10294b688867ee0cd73574f7c12c15050a2724245b43b3e8b4650259";

    /// <summary>Current schema version emitted by every report (registry row for this version carries EvidenceSchemaSha256).</summary>
    public const int EvidenceSchemaVersion = 2;

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

    /// <summary>QA-002: digest of the bin/z3 binary extracted from the pinned release asset (recorded by provisioning in solver/z3-4.12.1/binary.sha256; P04 re-verifies the installed binary against it).</summary>
    public const string Z3BinarySha256 = "06883e4d3fee816537ae1141ca4fff727f8820d05b0db5587aa540e380a3a8bf";
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
