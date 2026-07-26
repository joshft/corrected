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

out="$(bash gate/run-readiness-gate.sh 2>&1)"
rc=$?
printf '%s\n' "${out}"

# INV-012 both-paths visibility: a PASS/BLOCKED/FAIL banner must reach stdout.
if ! printf '%s' "${out}" | grep -Eq 'PASS|BLOCKED|FAIL'; then
  echo "[from-clean-gate] MISSING status banner on stdout (INV-012 green-path visibility)"
  exit 2
fi

exit "${rc}"
