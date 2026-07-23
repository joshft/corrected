// fixtures/syntax-error.dfy — spike-only malformed fixture (INV-011 / P08).
// Must fail at stage=parse; sentinel-configured runs record zero solver
// invocations and zero verification targets; the planted error token is here:
method Broken( {
