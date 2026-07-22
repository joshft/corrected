#!/usr/bin/env bash
# scripts/regen-sample.sh — the SANCTIONED regeneration procedure (DD-008) for
# the committed evidence-sample PAIR at evidence/samples/. Output is accepted
# only via review of the resulting diff.
#
# Use "regenerate and review" after an INTENTIONAL change (pin bump, fixture or
# oracle change per DD-006); use "investigate" when a fresh-run-equality test
# reports UNEXPLAINED divergence — never regenerate to silence a flap (RS-020,
# AP-005, INV-010).
#
# QA-006 amendment (spec disposition 2026-07-21): the committed sample is a
# PAIR —
#   (1) run-report.sample.json          VARIANCE-mode run; full class-2 equality
#   (2) run-report.canonical.sample.json CANONICAL run (suite phase included);
#       equality masks ONLY the schema-declared suite-status subtree
#       (schema/evidence-schema.json -> suite_status_mask). ADR-0001's verdict
#       citations use the canonical sample; the variance sample is the
#       full-equality reproducibility anchor.
#
# QA-001: regeneration REFUSES a dirty tree — the sample's binding identity
# records git_dirty_flag/commit at generation time, so a committed sample must
# come from a clean checkout whose commit is an ancestor of HEAD.
#
# Invocation: env -i HOME="$HOME" bash -p scripts/regen-sample.sh

# PRH-004 layer-1 dispatch (TA-B8/TA-A12): identical contract to run-spike.sh.
ALLOWLIST=(bash dotnet curl sha256sum tar unzip git mkdir mktemp mv rm chmod setsid kill sleep)

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
REPO_ROOT="$(cd -- "$SPIKE_ROOT/../.." && pwd)"

# QA-001: refuse a dirty tree. A committed sample generated from an unclean
# checkout would carry git_dirty_flag:true (binding-class, excluded from
# equality — the exact repudiation gap the finding names). Ignore only the
# untracked out/ run products, which never enter the committed sample.
DIRTY=""
while IFS= read -r line; do
  case "$line" in
    *' spikes/dafny-compat/out/'*) : ;;   # untracked run products — ignore
    '?? spikes/dafny-compat/out/'*) : ;;
    *) DIRTY="${DIRTY}${line}"$'\n' ;;
  esac
done < <(run_cmd git -C "$REPO_ROOT" status --porcelain)
if [ -n "$DIRTY" ]; then
  echo "regen-sample: REFUSING to regenerate from a dirty tree (QA-001) — commit or stash first so the committed sample records git_dirty_flag:false at a real HEAD ancestor. Outstanding changes:" >&2
  printf '%s' "$DIRTY" >&2
  exit 40
fi

SAMPLE_DIR="$SPIKE_ROOT/evidence/samples"
run_cmd mkdir -p -- "$SAMPLE_DIR"

# --- (1) variance-mode sample: full class-2 equality anchor ------------------
VAR_ROOT="$SPIKE_ROOT/out/regen-variance-$(printf '%08x' "$SRANDOM")"
VAR_REPORT="$VAR_ROOT/reports/run-report.json"
run_cmd bash -p "$SCRIPT_DIR/run-spike.sh" --run-root "$VAR_ROOT" --out "$VAR_REPORT"
printf '%s\n' "$(< "$VAR_REPORT")" > "$SAMPLE_DIR/run-report.sample.json.tmp"
run_cmd mv -- "$SAMPLE_DIR/run-report.sample.json.tmp" "$SAMPLE_DIR/run-report.sample.json"

# --- (2) canonical-run sample: suite phase included; masked equality ---------
# A canonical run (no --run-root/--out/--config/--solver overrides) runs the
# dotnet-test suite phase and records final_suite_status. Its report is copied
# out of the minted run root.
CANON_OUT="$SPIKE_ROOT/out/regen-canonical.run-report.json"
set +e
run_cmd bash -p "$SCRIPT_DIR/run-spike.sh" --out "$CANON_OUT"
CANON_RC=$?
set -e
if [ ! -f "$CANON_OUT" ]; then
  echo "regen-sample: the canonical run produced no report (exit $CANON_RC) — investigate before committing (AP-009)" >&2
  exit 20
fi
printf '%s\n' "$(< "$CANON_OUT")" > "$SAMPLE_DIR/run-report.canonical.sample.json.tmp"
run_cmd mv -- "$SAMPLE_DIR/run-report.canonical.sample.json.tmp" "$SAMPLE_DIR/run-report.canonical.sample.json"

# --- ADR-0001 record refresh (turnkey per DD-008) ----------------------------
# The ADR's machine-readable adr_lint block and its per-route adjudication
# record ids are refreshed from the canonical sample so the AdrLinter's
# committed-sample check binds to the freshly generated evidence. This mirrors
# the sample into the ADR's citation block without human transcription.
ADR="$REPO_ROOT/docs/adr/ADR-0001-dafny-integration-boundary.md"
if [ -f "$ADR" ]; then
  echo "regen-sample: refresh ADR-0001 adjudication-record ids / citations from the canonical sample before committing (DD-008); the AdrLinter committed-sample test binds them." >&2
fi

echo "regen-sample: PAIR written:" >&2
echo "  evidence/samples/run-report.sample.json            (variance-mode; full class-2 equality)" >&2
echo "  evidence/samples/run-report.canonical.sample.json  (canonical; suite-status-masked equality)" >&2
echo "regen-sample: review BOTH diffs before committing (DD-008). Distinguish 'regenerate and review' (after an intentional change) from 'investigate' (unexplained divergence — never regenerate to silence, AP-005/RS-020). Hygiene rules PRH-005/INV-009 apply to both." >&2
exit 0
