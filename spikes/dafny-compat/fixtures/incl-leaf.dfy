// fixtures/incl-leaf.dfy — spike-only include-pair leaf (INV-012 / P12).
function LeafAdd(x: int, y: int): int { x + y }

lemma LeafAddCommutes(x: int, y: int)
  ensures LeafAdd(x, y) == LeafAdd(y, x)
{
}
