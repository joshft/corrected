// fixtures/impure-control.dfy — spike-only NEGATIVE CONTROL for the
// AST-based fixture-hygiene walk (INV-004/RS-009, TA-B6). This fixture is
// deliberately IMPURE: it contains an attribute and an assume statement. The
// hygiene probe must report it impure — a probe that hardcodes "pure" for
// every input fails this differential. Never used as a verification fixture.

method {:verify false} Impure(n: int) returns (r: int)
{
  assume n > 0;
  r := n;
}
