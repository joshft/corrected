#!/usr/bin/env bash
# gate/tools/render-status.sh — the INV-012 status renderer step of <GATE-SCRIPT>,
# run AFTER dotnet test (green-path visibility, RS-290/EXT6-01). Emits the
# PASS-BLOCKED banner on green or the FAIL-violation text on any failure, plus the
# INV-011 "no production surface (src/ empty)" notice — to stdout of the canonical
# command, not merely as a swallowed xUnit assertion.
set -uo pipefail

TEST_RC="${1:-1}"
TRX_RC="${2:-1}"

echo "[render-status] INV-012 readiness-gate status"
echo "no production surface (src/ empty): the shipped closure resolves to zero project files while BLOCKED; the production-code ban is vacuously satisfied."

if [ "${TEST_RC}" = "0" ] && [ "${TRX_RC}" = "0" ]; then
  echo "PASS: readiness gate consistent; BLOCKED is the expected Phase-0.1 state (P2/P3 not yet dischargeable)."
  echo "validator-deferred: expected while BLOCKED; not yet dischargeable (DF-003 remediation lane + the DD-002 manifest schema)."
  exit 0
fi

echo "FAIL: readiness gate violation (test_rc=${TEST_RC} trx_rc=${TRX_RC})."
exit 1
