// fixtures/bad.dfy — refutable fixture (INV-005 / P07). Spike-only (BND-003).
// Contains exactly ONE false assertion; its location is committed in
// fixtures/expected/bad.sidecar.json.

method PlantedFailure()
{
  var x := 3;
  assert x == 4; // PLANTED: the single false assertion (line 8)
}
