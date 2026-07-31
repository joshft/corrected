#!/usr/bin/env bash
# <GATE-SCRIPT> — the canonical operator + CI readiness-gate command
# (readiness-gate-carrier INV-006/012/014/017, EXT6-01/EXT7-01/EXT7-02).
#
# Contract (EXT7-02 combined-exit state machine):
#   1. run `dotnet test <AGGREGATOR> --logger "trx;LogFileName=gate.trx"` from the
#      repo root on a clean checkout, capturing test_rc WITHOUT `set -e`
#      short-circuiting (so a nonzero dotnet test does NOT skip the renderer);
#   2. compute trx_rc from the out-of-suite executed-count / named-fixture guard;
#   3. ALWAYS render the INV-012 status (PASS-BLOCKED banner on green, FAIL text on
#      any failure), capturing render_rc;
#   4. exit 0 iff test_rc==0 && trx_rc==0 && render_rc==0, else non-zero.
#
# Recursion guard (INV-017/EXT7-02): the OUTER script starts with the sentinel
# CORRECTED_GATE_INNER UNSET and EXPORTS it =1 ONLY for the child dotnet test, so
# any gate-invoking helper running INSIDE the discovered suite no-ops.
#
# RED SCAFFOLD: this is a working-but-stub wrapper. The trx-guard and renderer
# steps are delegated to gate/tools/*.sh stubs that currently return non-zero, so
# the wrapper contract's three terms are exercised and the RED suite is red. GREEN
# implements the guard/renderer bodies.
set -uo pipefail

# Re-entry no-op: if we are already inside a gate invocation, do nothing (INV-017).
if [ "${CORRECTED_GATE_INNER:-}" = "1" ]; then
  echo "[gate] CORRECTED_GATE_INNER=1 set — inner invocation no-ops (INV-017 recursion guard)."
  exit 0
fi

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
cd "${REPO_ROOT}"

# <AGGREGATOR> single referenced constant (INV-014): .slnx iff it builds on
# 10.0.302, else the .sln fallback.
if [ -f gate/Corrected.Gate.slnx ]; then
  AGGREGATOR="gate/Corrected.Gate.slnx"
else
  AGGREGATOR="gate/Corrected.Gate.sln"
fi

TRX_NAME="gate.trx"
TRX_PATH="${REPO_ROOT}/gate/Corrected.Gate.Tests/TestResults/${TRX_NAME}"

# Restore is a distinct step so the config file / -noAutoResponse are NOT forwarded
# to `dotnet test` (which passes unknown switches to MSBuild).
dotnet restore "${AGGREGATOR}" --configfile gate/NuGet.Config -noAutoResponse --locked-mode

# Clean-current-run evidence: remove any prior gate.trx BEFORE the run so a dotnet
# failure-to-launch (e.g. the SDK absent) can never leave a STALE TRX for the guard
# to read as THIS run's evidence — the guard binds to the current run only (AP-021).
rm -f "${TRX_PATH}"

# Step 0 — P3 real-cosign provisioning (INV-010/INV-017 / RS-014 / EA-008). Provision the pinned
# cosign binary AND obtain the pinned trusted root BEFORE the offline verify runs inside
# `dotnet test`, and EXPORT the COSIGN_BIN + TRUSTED_ROOT seam the in-suite layer-2 real-cosign
# tests read — else the real cosign path never runs on a fresh clone (a phantom, AP-013). The
# real-cosign path is linux-x64-only (EA-003); on a non-linux-x64 host the P3 verify path records
# a typed rid-platform-mismatch and does NOT silently skip (RS-015).
COSIGN_RID="linux-x64"
HOST_OS="$(uname -s)"
HOST_ARCH="$(uname -m)"
case "${HOST_OS}/${HOST_ARCH}" in
  Linux/x86_64|Linux/amd64)
    COSIGN_CACHE="${HOME}/.cache/cosign/v3.1.2/cosign-linux-amd64"
    # Online provisioning phase: fetch + digest-validate the pinned per-RID binary (cached).
    bash "${SCRIPT_DIR}/tools/provision-cosign.sh" "${COSIGN_RID}" "${COSIGN_CACHE}"
    export COSIGN_BIN="${COSIGN_CACHE}"
    # Trusted root: the committed FROZEN root (INV-016 versioned immutable), digest-verified
    # against the committed pin before it anchors the offline verify. Kept offline for the root.
    ROOT_PATH="${SCRIPT_DIR}/tools/trusted_root.json"
    ROOT_PIN="$(grep -o '[0-9a-f]\{64\}' "${SCRIPT_DIR}/tools/trusted-root-pin.json" | head -1)"
    if printf '%s  %s\n' "${ROOT_PIN}" "${ROOT_PATH}" | sha256sum --check --status; then
      export TRUSTED_ROOT="${ROOT_PATH}"
      echo "[gate] provisioned cosign ${COSIGN_RID} + trusted root (digest-pinned) for the offline verify."
    else
      echo "[gate] FAIL: committed trusted root digest does not match the pin — refusing to anchor the verify."
      export TRUSTED_ROOT=""
    fi
    ;;
  *)
    # RS-015: off-RID is a typed rid-platform-mismatch, NEVER a silent skip. The gate stays
    # runnable + honest off linux-x64; the in-suite layer-2 cells record a typed non-Verified
    # reject via the real Verify path (they do not claim a green P3 they could not verify).
    echo "[gate] rid-platform-mismatch: host ${HOST_OS}/${HOST_ARCH} is not the pinned cosign RID ${COSIGN_RID} (EA-003); the P3 real-cosign path records rid-platform-mismatch (RS-015), not a silent skip."
    export COSIGN_BIN=""
    export TRUSTED_ROOT=""
    ;;
esac

# Step 1 — run the discovered suite; sentinel exported ONLY for this child.
CORRECTED_GATE_INNER=1 dotnet test "${AGGREGATOR}" \
  --logger "trx;LogFileName=${TRX_NAME}"
test_rc=$?

# Step 2 — out-of-suite TRX executed-count / named-fixture guard.
bash "${SCRIPT_DIR}/tools/trx-guard.sh" "${TRX_PATH}"
trx_rc=$?

# Step 3 — ALWAYS render the INV-012 status regardless of test_rc.
bash "${SCRIPT_DIR}/tools/render-status.sh" "${test_rc}" "${trx_rc}"
render_rc=$?

# Step 4 — combined exit (SINGLE definition shared with gate-wrapper.sh, QA-006).
source "${SCRIPT_DIR}/tools/combined-exit.sh"
gate_combined_exit "${test_rc}" "${trx_rc}" "${render_rc}" "gate"
exit $?
