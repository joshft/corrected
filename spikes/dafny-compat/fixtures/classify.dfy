// fixtures/classify.dfy — resolved-AST classification fixture (INV-007 / P10).
// Contains all four Phase 0.1 editable-proof forms (assert, calc, loop
// invariant, decreases), compiled statements (assignment, conditional, loop
// body), an expect statement as the compiled contrast case, explicitly ghost
// and compiled declarations, and one variable with NO `ghost` keyword whose
// ghostness is resolver-INFERRED (assigned from a ghost function).

ghost function GhostDouble(x: int): int { x + x }

function Inc(x: int): int { x + 1 }

ghost const GhostSeven: int := 7

const CompiledSeven: int := 7

method Classify(n: nat) returns (r: nat)
{
  var inferredGhost := GhostDouble(n); // no `ghost` keyword: resolver-inferred ghost
  var bound := Inc(0) + 3;             // compiled assignment
  r := 0;
  var i := 0;
  while i < bound
    invariant 0 <= i <= bound
    invariant r == i
    decreases bound - i
  {
    r := r + 1;                        // compiled loop body
    i := i + 1;
  }
  if r > 0 {
    r := r + 0;                        // compiled conditional branch
  }
  assert r == bound;                   // proof node: assert statement
  assert inferredGhost == GhostDouble(n);
  calc {                               // proof node: calc statement
    r + 0;
    ==
    r;
  }
  expect r >= 0;                       // compiled: expect (contrast case)
}
