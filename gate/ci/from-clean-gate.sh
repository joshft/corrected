#!/usr/bin/env bash
# gate/ci/from-clean-gate.sh — the OUT-OF-SUITE from-clean execution harness
# (INV-013/INV-017, the spike DF-001 LIVE half). It runs the readiness gate from a
# CLEAN checkout and asserts it produced a status banner on stdout.
#
# WHY OUT-OF-SUITE: it is NEVER invoked by an in-suite xUnit test — an in-suite test
# executing <GATE-SCRIPT> (which itself runs `dotnet test`) would recurse (EXT6-01).
# The in-suite side only CHARTERS this harness's existence + CI wiring (INV-017
# Ci_wires_the_from_clean_harness). CI invokes THIS harness directly; the harness is
# the verbatim from-clean execution evidence, replacing the PMB-001 doc-grep trap.
set -uo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "${REPO_ROOT}"

# From-clean: remove the gitignored spike out/ tree — the CORRECT rm path (EXT2-11).
# There is no top-level out/; a fresh clone is already out-clean.
rm -rf spikes/dafny-compat/out/

# The sentinel MUST be unset so the OUTER <GATE-SCRIPT> executes fully (the harness is
# NOT itself run under the sentinel, which would no-op the run, INV-017/EXT7-02).
unset CORRECTED_GATE_INNER

# P3 real-cosign provisioning + EXECUTION net (INV-010/INV-015 / RS-014, B1b). The from-clean job
# MUST provision the pinned cosign binary + trusted root and PROVE the real binary is present and
# is the pinned version BEFORE it invokes the gate — an EXECUTION assertion (`cosign version`), not
# a grep — so a fresh clone genuinely exercises the real cosign path (else it is a phantom, AP-013).
# The real-cosign path is linux-x64-only (EA-003); off-RID records a typed rid-platform-mismatch.
COSIGN_RID="linux-x64"
FC_OS="$(uname -s)"
FC_ARCH="$(uname -m)"
if [ "${FC_OS}" = "Linux" ] && { [ "${FC_ARCH}" = "x86_64" ] || [ "${FC_ARCH}" = "amd64" ]; }; then
  COSIGN_CACHE="${HOME}/.cache/cosign/v3.1.2/cosign-linux-amd64"
  bash gate/tools/provision-cosign.sh "${COSIGN_RID}" "${COSIGN_CACHE}"
  export COSIGN_BIN="${COSIGN_CACHE}"
  export TRUSTED_ROOT="${REPO_ROOT}/gate/tools/trusted_root.json"

  # B1b execution net: the provisioned binary is executable, the trusted root is present, and the
  # binary REPORTS the pinned v3.1.2 (a real subprocess, not a grep of a provision line).
  if [ ! -x "${COSIGN_BIN}" ]; then
    echo "[from-clean-gate] FAIL: provisioned \$COSIGN_BIN is not executable: ${COSIGN_BIN}"
    exit 3
  fi
  if [ ! -f "${TRUSTED_ROOT}" ]; then
    echo "[from-clean-gate] FAIL: provisioned \$TRUSTED_ROOT is missing: ${TRUSTED_ROOT}"
    exit 3
  fi
  cosign_ver="$(${COSIGN_BIN} version 2>&1)"
  if ! printf '%s' "${cosign_ver}" | grep -q 'v3.1.2'; then
    echo "[from-clean-gate] FAIL: provisioned cosign is not the pinned v3.1.2 (got: ${cosign_ver})"
    exit 3
  fi
  echo "[from-clean-gate] provisioned + verified pinned cosign v3.1.2 execution net (COSIGN_BIN + TRUSTED_ROOT)."
else
  echo "[from-clean-gate] rid-platform-mismatch: host ${FC_OS}/${FC_ARCH} is not the pinned cosign RID ${COSIGN_RID} (EA-003); the P3 real-cosign path records rid-platform-mismatch (RS-015), not a silent skip."
fi

out="$(bash gate/run-readiness-gate.sh 2>&1)"
rc=$?
printf '%s\n' "${out}"

# INV-012 both-paths visibility: a PASS/BLOCKED/FAIL banner must reach stdout.
if ! printf '%s' "${out}" | grep -Eq 'PASS|BLOCKED|FAIL'; then
  echo "[from-clean-gate] MISSING status banner on stdout (INV-012 green-path visibility)"
  exit 2
fi

exit "${rc}"
