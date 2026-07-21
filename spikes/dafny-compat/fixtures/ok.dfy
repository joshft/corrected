// fixtures/ok.dfy — provable fixture (INV-004 / P06). Reusable by Phase 0.1 by
// reference in place (BND-003): bool/int/nat/seq only; no heap, classes, or
// recursion. Structural purity enforced by the AST-based fixture-hygiene test
// (RS-009): bodied declarations only; no attributes, no assume statements, no
// verification-suppression constructs.

function Add(x: int, y: int): int { x + y }

method MaxOfTwo(a: int, b: int) returns (m: int)
  ensures m >= a && m >= b
  ensures m == a || m == b
{
  if a >= b {
    m := a;
  } else {
    m := b;
  }
}

method SumFirst(s: seq<int>, n: nat) returns (total: int)
  requires n <= |s|
  ensures n == 0 ==> total == 0
{
  total := 0;
  var i := 0;
  while i < n
    invariant 0 <= i <= n
    invariant n == 0 ==> total == 0
  {
    total := Add(total, s[i]);
    i := i + 1;
  }
}
