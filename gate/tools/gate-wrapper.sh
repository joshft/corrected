#!/usr/bin/env bash
# gate/tools/gate-wrapper.sh — the EXTRACTED combined-exit state machine of
# <GATE-SCRIPT> (INV-014/EXT7-02), factored out so the five wrapper self-test
# fixtures can drive it over a STUBBED dotnet test + TRX WITHOUT invoking the
# enclosing gate/run-readiness-gate.sh (which would recurse, INV-017/EXT6-01).
#
# DECISION: the spec's "five fixtures drive the outer script" is satisfied by
# driving this extracted wrapper (identical combined-exit logic), not the literal
# enclosing script — this reconciles EXT7-02 (script-level wrapper self-tests) with
# INV-017 ("no in-suite xUnit test ever executes <GATE-SCRIPT>").
#
# Injected env (fixtures set these):
#   GATE_TEST_RC   — simulated dotnet test exit code
#   GATE_TRX_PATH  — path to a synthetic TRX
#   GATE_TRX_GUARD — guard script (default gate/tools/trx-guard.sh)
#   GATE_RENDERER  — renderer script (default gate/tools/render-status.sh)
#
# Contract: capture test_rc; compute trx_rc from the guard; ALWAYS render (even on
# nonzero test_rc/trx_rc); if the renderer itself exits non-zero, a SHELL-OWNED
# fallback FAIL line is emitted even though the renderer could not (EXT8-01); exit 0
# iff test_rc==0 && trx_rc==0 && render_rc==0.
#
set -uo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
test_rc="${GATE_TEST_RC:-0}"
trx_path="${GATE_TRX_PATH:-}"
guard="${GATE_TRX_GUARD:-${SCRIPT_DIR}/trx-guard.sh}"
renderer="${GATE_RENDERER:-${SCRIPT_DIR}/render-status.sh}"

# Compute trx_rc from the out-of-suite guard.
bash "${guard}" "${trx_path}"
trx_rc=$?

# ALWAYS render (even on nonzero test_rc/trx_rc) so INV-012 both-paths visibility holds.
bash "${renderer}" "${test_rc}" "${trx_rc}"
render_rc=$?

# If the renderer itself could not run, a SHELL-OWNED fallback FAIL line is emitted
# even though the renderer could not (EXT8-01 — forces the render_rc term).
if [ "${render_rc}" -ne 0 ]; then
  echo "[gate-wrapper] FAIL: renderer step exited ${render_rc} (render_rc term; shell-owned fallback)"
fi

# Combined exit — SINGLE definition shared with run-readiness-gate.sh (QA-006):
# 0 iff test_rc==0 && trx_rc==0 && render_rc==0, else non-zero.
source "${SCRIPT_DIR}/combined-exit.sh"
gate_combined_exit "${test_rc}" "${trx_rc}" "${render_rc}" "gate-wrapper"
exit $?
