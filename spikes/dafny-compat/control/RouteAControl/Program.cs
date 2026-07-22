// GREEN — Route A net8.0 control executable (BND-004/INV-013). Two duties:
// (1) --identity-probe: emit control evidence carrying the digest + location
//     identity of the hostfxr and System.Private.CoreLib actually selected —
//     "ran on some 8.x" is insufficient; the PINNED runtime must be proven to
//     have run (codex R3-3/TA-B12).
// (2) --probe <id> --fixture <f> --solver <path>: re-run a probe from the
//     IDENTICAL normalized seam source (SeamCommon is one shared file compiled
//     into both TFMs; TFM-conditional logic is scan-prohibited, codex R2-2)
//     for three-cell adjudication.
return Corrected.Spike.Contracts.ControlCore.Run("A", new Corrected.Spike.RouteA.RouteAAdapter(), args);
