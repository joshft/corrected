// INV-006 / INV-022. GENERIC (reusable) DSSE envelope contract: the Statement is
// base64-wrapped as a DSSE payload before the Sigstore bundle signs it. Signing/bundle
// minting itself is INV-007/009 (other tracks); this track only pins the payload-
// wrapping edge of INV-006's object graph (Statement -> DSSE payload).
using System;
using System.Collections.Generic;

namespace Corrected.Provenance.InToto;

/// <summary>
/// A DSSE (Dead Simple Signing Envelope). INV-006's graph node between the in-toto
/// Statement and the Sigstore bundle: the payload is the base64 of the Statement
/// JSON, under the pinned in-toto payload media type.
/// </summary>
public sealed class DsseEnvelope
{
    /// <summary>
    /// The fixed upstream in-toto JSON DSSE media type — the ONLY value the pinned
    /// <see cref="PayloadType"/> may carry (single source of truth; A4 mitigation).
    /// </summary>
    public const string InTotoJsonPayloadType = "application/vnd.in-toto+json";

    /// <summary>Pinned DSSE payloadType — the in-toto JSON media type.</summary>
    public string PayloadType { get; init; } = "";

    /// <summary>Base64 of the exact Statement JSON bytes.</summary>
    public string Payload { get; init; } = "";

    public IReadOnlyList<DsseSignature> Signatures { get; init; } = Array.Empty<DsseSignature>();
}

/// <summary>A DSSE signature entry (populated by the signer — INV-007/009).</summary>
public sealed class DsseSignature
{
    public string Sig { get; init; } = "";

    public string? Keyid { get; init; }
}
