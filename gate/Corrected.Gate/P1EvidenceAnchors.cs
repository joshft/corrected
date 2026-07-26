namespace Corrected.Gate;

/// <summary>
/// The THREE compiled content anchors for P1 (INV-008a′ / R3-B2). Each is a
/// review-gated `public const` SHA-256 that relocates the trust root from a silent
/// data-file edit to the reviewable gate source. A coherent multi-file tamper of
/// the data files still fails against these compiled constants.
///
/// RED NOTE: these are placeholder zero-digests so the INV-008 Stage-A positive
/// fixture (SHA256(committed canonical sample) == CanonicalSampleSha256) FAILS
/// (RED). GREEN pins the real SHA-256 of each committed file.
/// </summary>
public static class P1EvidenceAnchors
{
    // SHA-256(spikes/dafny-compat/evidence/samples/run-report.canonical.sample.json)
    public const string CanonicalSampleSha256 =
        "5526b495d1e9761604d6bcc8246120e9dbafdb6c050c6639a04b0721402dc4d4";

    // SHA-256(spikes/dafny-compat/schema/evidence-schema.json)
    public const string EvidenceSchemaSha256 =
        "c872c710dd390ff8d8050c059077d0eb7d6ef4f2352fc7bf375403014ac18509";

    // SHA-256(spikes/dafny-compat/manifest/probe-manifest.json)
    public const string ProbeManifestSha256 =
        "4956816b40f2cf4316ab2ba3ad9cbb810bb89e0339187c8add7a7d3c2178b0eb";

    /// <summary>Recognized evidence-schema versions (INV-008a′); one element today: 2.</summary>
    public static readonly System.Collections.Generic.IReadOnlySet<int> RecognizedSchemaVersions =
        new System.Collections.Generic.HashSet<int> { 2 };
}
