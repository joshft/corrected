#!/usr/bin/env bash
# gate/tools/trx-guard.sh — the OUT-OF-SUITE executed-count / named-fixture guard
# (INV-014). Parses the TRX and FAILS (non-zero) on zero discovery or a below-floor
# executed count, and asserts the specific fixtures (representative INV-005 rows,
# INV-008 P1 cases, the committed-state test) each ran — so dropping
# Corrected.Gate.Tests from the aggregator cannot silently green the run.
set -uo pipefail

TRX_PATH="${1:-}"
if [ -z "${TRX_PATH}" ] || [ ! -f "${TRX_PATH}" ]; then
  echo "[trx-guard] FAIL: TRX not found: ${TRX_PATH}"
  exit 2
fi

# Executed count from <Counters ... executed="N" ...>.
executed="$(grep -oE 'executed="[0-9]+"' "${TRX_PATH}" | head -1 | grep -oE '[0-9]+')"
executed="${executed:-0}"

FLOOR=50
if [ "${executed}" -eq 0 ]; then
  echo "[trx-guard] FAIL: zero discovery (executed=0) — dropped test project?"
  exit 3
fi
if [ "${executed}" -lt "${FLOOR}" ]; then
  echo "[trx-guard] FAIL: below-floor executed count (${executed} < ${FLOOR})"
  exit 3
fi

# Named fixtures that MUST be present (each is an INV-005/006/008 anchor test); their
# absence means the discovered suite is not the readiness-gate suite.
named=(
  "Inv005VerdictTableTests.Null_false_true_is_Fail"
  "Inv005VerdictTableTests.Null_false_false_is_consistent"
  "Inv008P1ProbeTests.PreMigration_adr_is_schema_incomplete"
  "Inv006OrchestrationTests.StageB_committed_block_is_Pass_BLOCKED"
)
for name in "${named[@]}"; do
  if ! grep -q "${name}" "${TRX_PATH}"; then
    echo "[trx-guard] FAIL: required named fixture absent from TRX: ${name}"
    exit 4
  fi
done

echo "[trx-guard] OK: executed=${executed} (>= ${FLOOR}); all required named fixtures present."
exit 0
