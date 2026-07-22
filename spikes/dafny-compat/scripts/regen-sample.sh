#!/usr/bin/env bash
# scripts/regen-sample.sh — the SANCTIONED regeneration procedure (DD-008) for
# the committed evidence sample at evidence/samples/. Output is accepted only
# via review of the resulting diff.
#
# Use "regenerate and review" after an INTENTIONAL change (pin bump, fixture or
# oracle change per DD-006); use "investigate" when the fresh-run-equality test
# reports UNEXPLAINED divergence — never regenerate to silence a flap (RS-020,
# AP-005, INV-010).
#
# The sample is produced by a variance-mode controller run (final_suite_status
# unknown — the run mode every in-suite fresh-run launch uses, so the committed
# projection and fresh projections compare like for like). Only the run-level
# report is committed; its binding identity carries repo-relative paths only
# (PRH-005 — the hygiene grep enforces this).
#
# Invocation: env -i HOME="$HOME" bash -p scripts/regen-sample.sh

# PRH-004 layer-1 dispatch (TA-B8/TA-A12): identical contract to run-spike.sh.
ALLOWLIST=(bash dotnet curl sha256sum tar unzip git mkdir mktemp mv chmod setsid kill sleep)

run_cmd() {
  local cmd="$1"; shift
  local base="${cmd##*/}"
  local ok=""
  local allowed
  for allowed in "${ALLOWLIST[@]}"; do
    if [ "$base" = "$allowed" ]; then ok=1; fi
  done
  if [ -z "$ok" ]; then
    echo "regen-sample: DENY non-allowlisted command: $cmd (PRH-004)" >&2
    exit 20
  fi
  command "$cmd" "$@"
}

case "$-" in
  *p*) : ;;
  *)
    echo "regen-sample: refusing the unhardened invocation — use: env -i HOME=\"\$HOME\" bash -p scripts/regen-sample.sh (PRH-004)" >&2
    exit 30 ;;
esac

set -euo pipefail
PATH="/usr/bin:/bin"
export PATH

SCRIPT_SELF="${BASH_SOURCE[0]}"
SCRIPT_DIR="$(cd -- "${SCRIPT_SELF%/*}" && pwd)"
SPIKE_ROOT="$(cd -- "$SCRIPT_DIR/.." && pwd)"

STAMP="regen-$(printf '%08x' "$SRANDOM")"
RUN_ROOT="$SPIKE_ROOT/out/$STAMP"
REPORT="$RUN_ROOT/reports/run-report.json"

run_cmd bash -p "$SCRIPT_DIR/run-spike.sh" --run-root "$RUN_ROOT" --out "$REPORT"

SAMPLE_DIR="$SPIKE_ROOT/evidence/samples"
run_cmd mkdir -p -- "$SAMPLE_DIR"
printf '%s\n' "$(< "$REPORT")" > "$SAMPLE_DIR/run-report.sample.json.tmp"
run_cmd mv -- "$SAMPLE_DIR/run-report.sample.json.tmp" "$SAMPLE_DIR/run-report.sample.json"

echo "regen-sample: committed-sample candidate written to evidence/samples/run-report.sample.json" >&2
echo "regen-sample: review the diff before committing (DD-008); hygiene rules PRH-005/INV-009 apply" >&2
exit 0
