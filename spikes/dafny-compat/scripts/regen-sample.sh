#!/usr/bin/env bash
# scripts/regen-sample.sh — the SANCTIONED regeneration procedure (DD-008) for
# the committed evidence sample at evidence/samples/. Output is accepted only
# via review of the resulting diff.
#
# Use "regenerate and review" after an INTENTIONAL change (pin bump, fixture or
# oracle change per DD-006); use "investigate" when the fresh-run-equality test
# reports UNEXPLAINED divergence — never regenerate to silence a flap (RS-020,
# AP-005, INV-010).

# PRH-004 layer-1 dispatch (TA-B8/TA-A12): identical contract to run-spike.sh.
ALLOWLIST=(bash dotnet curl sha256sum tar unzip git mkdir mktemp mv chmod setsid kill sleep)

run_cmd() {
  local cmd="$1"; shift
  local ok=""
  local allowed
  for allowed in "${ALLOWLIST[@]}"; do
    if [ "$cmd" = "$allowed" ]; then ok=1; fi
  done
  if [ -z "$ok" ]; then
    echo "regen-sample: DENY non-allowlisted command: $cmd (PRH-004)" >&2
    exit 20
  fi
  command "$cmd" "$@"
}

set -euo pipefail

# STUB:TDD — RED phase: regeneration is not implemented yet. GREEN runs the
# bootstrap controller, then copies the run-level report's committed-sample
# form (deterministic projection intact, hygiene-clean) into evidence/samples/.
echo "regen-sample.sh: STUB:TDD — not implemented (RED phase)" >&2
exit 20
