namespace Corrected.Gate;

/// <summary>
/// The NAMED fail-closed reason for a readiness-block extraction failure (INV-001,
/// B5/RED). Distinguishes zero/duplicate blocks from an over-cap file/block so a
/// test asserts the SPECIFIC reason — not a bare <c>ThrowsAny</c> that would pass
/// on ANY exception incl. a NullReferenceException from a half-built parser
/// (AP-014). Inline-prose mentions are IGNORED (RS-261), so an inline mention with
/// no column-0-in-fence block yields <see cref="NoReadinessBlock"/>, same as a truly
/// empty file.
/// </summary>
public enum ReadinessExtractionReason
{
    /// <summary>Zero column-0-in-`yaml`-fence blocks (inline prose IGNORED, RS-261).</summary>
    NoReadinessBlock,

    /// <summary>Two or more in-fence blocks (a duplicate block is a TB-006 tamper).</summary>
    MultipleReadinessBlocks,

    /// <summary>File exceeds MaxFileBytes AFTER LF/UTF-8 normalization (RS-264).</summary>
    FileTooLarge,

    /// <summary>The extracted block exceeds MaxBlockBytes.</summary>
    BlockTooLarge,
}

/// <summary>
/// Typed fail-closed extraction exception carrying a NAMED reason (INV-001 / B5).
/// <see cref="ReadinessBlockParser.ExtractSingleBlock"/> throws THIS (not a bare
/// <see cref="System.Exception"/>) so the fail-closed path is asserted precisely
/// (a bare ThrowsAny would pass even on a NullReferenceException from a half-built
/// parser — AP-014). It carries only the named reason; the extraction/validation
/// logic lives in ReadinessBlockParser.
/// </summary>
public sealed class ReadinessExtractionException : System.Exception
{
    public ReadinessExtractionException(ReadinessExtractionReason reason, string message)
        : base(message)
    {
        Reason = reason;
    }

    /// <summary>The NAMED fail-closed reason (INV-001).</summary>
    public ReadinessExtractionReason Reason { get; }
}
