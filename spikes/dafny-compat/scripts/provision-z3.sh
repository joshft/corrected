#!/usr/bin/env bash
# scripts/provision-z3.sh — digest-verified, idempotent, partial-state-safe Z3
# provisioning (INV-003 / BND-002 / INV-014 / AP-016).
#
# Pin lives in config/z3-pin.json and is mirrored here. CHANGING THIS REQUIRES
# RE-RUNNING THE SPIKE SUITE AND COMMITTING THE EVIDENCE DIFF — see DD-006 /
# docs/adr/ADR-0001-dafny-integration-boundary.md.
Z3_VERSION="4.12.1"
Z3_URL="https://github.com/Z3Prover/z3/releases/download/z3-4.12.1/z3-4.12.1-x64-glibc-2.35.zip"
Z3_SHA256="c5360fd157b0f861ec8780ba3e51e2197e9486798dc93cd878df69a4b0c2b7c5"
SUPPORTED_RID="linux-x64"
RID_FAIL_MESSAGE="RID not supported by this spike; proven RIDs: linux-x64 (see ADR-0001)"

# PRH-004 layer-1 dispatch (TA-B8/TA-A12): identical contract to run-spike.sh —
# every external command goes through run_cmd; curl always runs with --disable
# as its first option. The static test audits every scripts/*.sh.
ALLOWLIST=(bash dotnet curl sha256sum tar unzip git mkdir mktemp mv chmod setsid kill sleep)

run_cmd() {
  local cmd="$1"; shift
  local ok=""
  local allowed
  for allowed in "${ALLOWLIST[@]}"; do
    if [ "$cmd" = "$allowed" ]; then ok=1; fi
  done
  if [ -z "$ok" ]; then
    echo "provision-z3: DENY non-allowlisted command: $cmd (PRH-004)" >&2
    exit 20
  fi
  command "$cmd" "$@"
}

# GREEN-phase contract:
#   - fail closed on any RID other than linux-x64 with exactly $RID_FAIL_MESSAGE
#     BEFORE any network fetch (BND-002: never an unverified fetch)
#   - curl runs with --disable as its first option
#   - verify SHA-256 against $Z3_SHA256 before install; mismatch aborts
#   - install to <run-root>/solver/z3-4.12.1/bin/z3 — OUTSIDE every ambient
#     discovery location; NEVER at the assembly-adjacent {output}/z3/bin/z3-4.12.1
#     fallback path Dafny searches (RS-003a)
#   - idempotent + partial-state-safe: safe from clean, partial (truncated or
#     corrupt file present), and full states; double-run produces identical
#     state (AP-016); prerequisite-failure messages name the remediation step

set -euo pipefail

# STUB:TDD — RED phase: provisioning is not implemented yet. The three-state +
# double-run tests and the unsupported-RID test must FAIL until GREEN.
echo "provision-z3.sh: STUB:TDD — provisioning not implemented (RED phase)" >&2
exit 20
