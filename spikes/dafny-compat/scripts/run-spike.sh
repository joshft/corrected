#!/usr/bin/env bash
# scripts/run-spike.sh — the canonical entry point: bootstrap controller
# (INV-006 ownership topology; PRH-004 layer 1).
#
# INVOCATION CONTRACT (PRH-004, codex R4-08, TA-B9):
#   env -i HOME="$HOME" bash -p scripts/run-spike.sh
# Hardening is self-enforcing: when invoked WITHOUT the hardened contract
# (e.g. plain `bash scripts/run-spike.sh` with a hostile BASH_ENV), the
# controller must either re-exec itself under `env -i bash -p` or refuse to
# run (fail-closed, nonzero exit) — it never proceeds under a poisoned shell.
# The applied environment of every launch class is written to the env-audit
# receipt (receipts/env-audit.json) so tests verify immunity from the FILE,
# not from script-emitted markers.
# curl always runs with --disable as its FIRST option; every dotnet
# restore/build/test invocation passes -noAutoResponse.
#
# PRH-004 LAYER-1 DISPATCH (TA-B8): every external command launch goes through
# run_cmd; the first argument must be in ALLOWLIST. The static test parses the
# ALLOWLIST array below, verifies it equals BootstrapAllowlist.Commands, scans
# every run_cmd call site's first argument, and deny-scans the script for
# direct fetch/interpreter invocations (wget, python*, perl, nc, ruby, node)
# and for bare curl/dotnet outside run_cmd.
ALLOWLIST=(bash dotnet curl sha256sum tar unzip git mkdir mktemp mv chmod setsid kill sleep)

run_cmd() {
  local cmd="$1"; shift
  local ok=""
  local allowed
  for allowed in "${ALLOWLIST[@]}"; do
    if [ "$cmd" = "$allowed" ]; then ok=1; fi
  done
  if [ -z "$ok" ]; then
    echo "run-spike: DENY non-allowlisted command: $cmd (PRH-004)" >&2
    exit 20
  fi
  command "$cmd" "$@"
}

# RUN-CONTEXT CONTRACT (DD-008, TA-B13): the controller mints run_id, creates
# out/<run_id>/, builds all artifacts with output paths redirected beneath the
# run root, writes <run-root>/run-context.json:
#   { "run_id", "run_root", "artifacts": { "RouteAHarness": {"path","sha256"},
#     "RouteBHarness": ..., "RouteAControl": ..., "RouteBControl": ...,
#     "SpikeAggregator": ..., "control_dotnet_host": ... } }
# and exports SPIKE_RUN_CONTEXT=<run-root>/run-context.json before invoking
# `dotnet test`. Integration tests refuse to launch anything without it and
# verify each artifact digest test-side against this receipt.
#
# RUN-ROOT LAYOUT (committed in Corrected.Spike.Contracts.RunLayout):
#   sentinel/ledger.json        nonce ledger, pre-created at count zero (RS-003d)
#   receipts/aggregation.json   run_id + consumed_report_paths (performed launches only)
#   receipts/env-audit.json     applied per-launch environments (EA-008)
#   receipts/build-receipt.json output/deps/runtimeconfig/generated-asset digests
#   reports/                    per-route + run-level evidence reports
#   solver/z3-4.12.1/bin/z3     provisioned solver (INV-003)
#
# GENERIC OPERATOR PHASES (test-visible surface; no test-only fault flags —
# faults are constructed BY tests in test-owned scratch roots):
#   --phase restore --project <csproj>   locked restore of one project
#                                        (--configfile NuGet.Config, -noAutoResponse)
#   --phase preprocess-audit             emits one "IMPORT <path>" line per
#                                        resolved MSBuild import of the spike build
#   --phase negative-compile --consumer-fixture <file> --scratch <dir>
#                                        builds the consumer against the Route A seam
#   --config <file>                      alternate run-config.json (committed shape)
#   --solver <path>                      explicit solver path override, recorded as
#                                        variance in evidence
#   --run-root <dir>                     pre-created run root (tests own faults there)
#
# GREEN-phase responsibilities (owned HERE, not by the managed aggregator):
#   1. mint unpredictable run_id; create out/<run_id>/ (DD-008)
#   2. hold ONE absolute monotonic deadline read from /proc/uptime
#      (bash SECONDS is system-clock-derived, codex R4-05) from run mint
#      through final aggregation; on expiry record the synthetic INCOMPLETE
#      with the delivered signal (config/run-config.json bound)
#   3. run every phase in its own setsid process group under an asynchronous
#      supervisor with TERM->KILL escalation; the outer watchdog is the PARENT
#      of this controller, not a test executed by it
#   4. phases: provision -> locked restore -> build -> test -> aggregate;
#      emit nonce-bound restore/build receipts binding locks, exact restore
#      argv, exit results, and generated assets to run_id
#   5. environment profiles per launch class from config/env-profiles.json
#      (MSBUILDDISABLENODEREUSE=1, UseSharedCompilation=false in-profile)
#   6. P01 attestation per route partition (codex R4-07)

set -euo pipefail

# STUB:TDD — RED phase: the controller is not implemented yet. Tests that
# launch this script assert on the receipts/artifacts above and must FAIL
# until GREEN.
echo "run-spike.sh: STUB:TDD — bootstrap controller not implemented (RED phase)" >&2
exit 20
