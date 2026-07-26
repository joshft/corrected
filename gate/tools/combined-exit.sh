#!/usr/bin/env bash
# gate/tools/combined-exit.sh — the SINGLE definition of the EXT7-02 combined-exit
# decision, sourced by BOTH gate/run-readiness-gate.sh (the real operator/CI command)
# and gate/tools/gate-wrapper.sh (the fixture-driven self-test). QA-006: eliminates the
# hand-copied duplicate so a future edit to the decision (e.g. dropping the render_rc
# term) cannot silently diverge between the tested wrapper and the untested real script.
#
#   gate_combined_exit <test_rc> <trx_rc> <render_rc> [label]
#     returns 0 iff all three terms are 0; otherwise echoes
#     "[<label>] FAIL: test_rc=.. trx_rc=.. render_rc=.." and returns 1.

gate_combined_exit() {
  local test_rc="$1" trx_rc="$2" render_rc="$3" label="${4:-gate}"
  if [ "${test_rc}" -eq 0 ] && [ "${trx_rc}" -eq 0 ] && [ "${render_rc}" -eq 0 ]; then
    return 0
  fi
  echo "[${label}] FAIL: test_rc=${test_rc} trx_rc=${trx_rc} render_rc=${render_rc}"
  return 1
}
