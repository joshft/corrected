#!/usr/bin/env bash
# gate/tools/render-status.fail.sh — a DELIBERATE always-non-zero renderer DOUBLE for
# INV-014 wrapper fixture 4 (EXT8-01). It is NOT a RED stub GREEN will fill in:
# unlike gate/tools/render-status.sh (which GREEN implements to exit 0 on the green
# path), THIS renderer is a PERMANENT test double that ALWAYS exits non-zero, so the
# wrapper's render_rc term is forced to fail INDEPENDENTLY of test_rc/trx_rc. Fixture 4
# (test_rc==0, trx happy, renderer non-zero) drives it; fixture 5 uses the real
# renderer and asserts PASS — so the two fixtures are now DISTINCT and can BOTH go
# green (previously both arms named render-status.sh, an unsatisfiable contradiction).
# It is never wired into <GATE-SCRIPT>; only fixture 4 points GATE_RENDERER at it.
set -uo pipefail
echo "[render-status.fail] deliberate non-zero renderer double (forces render_rc != 0)"
exit 7
