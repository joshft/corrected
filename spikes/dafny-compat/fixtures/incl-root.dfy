// fixtures/incl-root.dfy — spike-only include-pair root (INV-012 / P12).
// Includes remain rejected by Phase 0.1 intake; this pair exists solely to
// prove closure recovery is real (DESIGN.md bullet 3 "complete source set").
include "incl-leaf.dfy"

method RootUsesLeaf() returns (r: int)
  ensures r == 5
{
  r := LeafAdd(2, 3);
}
