// fixtures/resolve-error.dfy — spike-only unresolvable fixture (INV-011 / P09).
// Parses cleanly; must fail at stage=resolution (unknown identifier).
method UsesUndefined() returns (x: int)
{
  x := NoSuchFunction(5);
}
