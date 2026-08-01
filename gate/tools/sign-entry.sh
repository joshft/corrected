#!/usr/bin/env bash
# gate/tools/sign-entry.sh — the EXTRACTED signer operator surface for the P3 phase-entry
# attestation (INV-030 / Group G / MA-C part b), the entry analog of sign-determinism.sh.
#
# The entry-signing workflow (.github/workflows/p3-entry-sign.yml — and the throwaway
# fixture-signing workflow used to MINT the layer-2 fixture) invokes this committed script
# VERBATIM (AP-020/PMB-001 — never inline `run:` steps a grep can only reconstruct). It
# RE-VALIDATES the same-run producer hand-off before any cosign call and REFUSES on any
# mismatch, enforces the run_attempt==1 guard, and — only on a fully-valid hand-off —
# invokes the single pinned cosign with the transcript-frozen `attest-blob --statement` argv.
#
# DOCUMENTED INVOCATION (run from the repo root; argv[0] is the RELATIVE path):
#   GITHUB_RUN_ATTEMPT=1 COSIGN_BIN=<abs cosign> \
#     bash gate/tools/sign-entry.sh --artifacts-dir <DIR> --out <BUNDLE_OUT>
#
# HAND-OFF (<DIR>, same-run @actions/artifact contents). The SIGNED SUBJECT is the commit-X
# representation blob (subjects[0]); the Statement is CORRECTED-BUILT (this signer CONSUMES
# it via EntryStatementEmitter, never hand-rolls it):
#   * entry-statement.json   — the Corrected-built in-toto entry Statement
#         (EntryStatementCodec.SerializeEntryStatementJson) the signer signs; NEVER built here.
#   * entry-commit.blob       — the commit-X UTF-8 bytes; sha256(blob) == the statement's
#         subjects[0] digest (the cosign --check-claims anchor the verifier re-binds).
#
# The cosign version/digest are SINGLE-SOURCED from gate/tools/cosign-pin.json (never a
# divergent in-script literal). In the gate tests a fake cosign is injected via COSIGN_BIN;
# in CI a digest-validated pinned binary is provisioned first. No network / OIDC / Rekor work
# happens in this script (cosign does the signing transport).

set -euo pipefail

# Resolve our own directory ROBUSTLY (AP-020): capture it from BASH_SOURCE BEFORE any `cd`.
SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"

# The frozen Corrected phase-entry contract literals the re-validation binds to INDEPENDENTLY
# (never trusting a value echoed back from the statement itself).
FROZEN_PREDICATE_TYPE="https://correctless.org/attestations/phase-entry/v1"
CANONICAL_COMMIT_SUBJECT_NAME="phase-entry-commit"

# --- fail-closed diagnostics --------------------------------------------------------

# INV-030 re-check refusal: non-zero exit, a "REFUSE" line on stderr, NO cosign call.
refuse() {
  echo "REFUSE (INV-030): $1 — the signer re-check failed; not invoking cosign." >&2
  exit 7
}

# Attempt-guard refusal: a rerun (or a missing attempt) mints NOTHING.
refuse_attempt() {
  echo "REFUSE (INV-030/RS-036): $1 — re-runs never mint a new attestation; push a new reviewed commit to re-attest at run_attempt 1." >&2
  exit 8
}

# --- argument parsing ---------------------------------------------------------------

ARTIFACTS_DIR=""
OUT_BUNDLE=""
while [ $# -gt 0 ]; do
  case "$1" in
    --artifacts-dir) ARTIFACTS_DIR="${2:-}"; shift 2 ;;
    --out)           OUT_BUNDLE="${2:-}"; shift 2 ;;
    *) echo "REFUSE (usage): unknown argument '$1' (expected --artifacts-dir/--out)." >&2; exit 2 ;;
  esac
done

[ -n "$ARTIFACTS_DIR" ] || refuse "--artifacts-dir is required"
[ -n "$OUT_BUNDLE" ]    || refuse "--out is required"

STATEMENT="${ARTIFACTS_DIR}/entry-statement.json"
BLOB="${ARTIFACTS_DIR}/entry-commit.blob"

[ -f "$STATEMENT" ] || refuse "Corrected-built entry-statement.json is absent (the signer must not build its own)"
[ -f "$BLOB" ]      || refuse "commit-X blob entry-commit.blob is absent"

# --- single-sourced cosign pin ------------------------------------------------------

PIN_CONFIG="${SCRIPT_DIR}/cosign-pin.json"
[ -f "$PIN_CONFIG" ] || refuse "cosign pin config missing: cosign-pin.json"
PINNED_COSIGN_VERSION="$(grep '"version"' "$PIN_CONFIG" | head -1 | cut -d'"' -f4)"
PINNED_COSIGN_ASSET="$(grep '"assetName"' "$PIN_CONFIG" | head -1 | cut -d'"' -f4)"
[ -n "$PINNED_COSIGN_VERSION" ] || refuse "cosign pin config has no version"

COSIGN="${COSIGN_BIN:-}"
if [ -z "$COSIGN" ]; then
  COSIGN="${HOME}/.cache/cosign/${PINNED_COSIGN_VERSION}/${PINNED_COSIGN_ASSET}"
fi
[ -x "$COSIGN" ] || refuse "resolved cosign binary is not executable: ${COSIGN##*/}"

# --- field extraction (BCL-free; each field is a "key": "value" string) -------------

json_first_str() {
  # $1 = file, $2 = key; echoes the FIRST "key": "value" string value (empty if absent).
  # FIRST-occurrence (grep -o + head), NOT a greedy sed: the entry Statement is MULTI-subject,
  # so it carries MANY "sha256"/"name" pairs; a greedy `.*"key"` would capture the LAST. The
  # canonical wire order is _type, predicateType, subject[], predicate — so the first "sha256"
  # and the first "name" are exactly subjects[0]'s (the commit subject).
  grep -oE "\"$2\"[[:space:]]*:[[:space:]]*\"[^\"]*\"" "$1" | head -1 \
    | sed -E "s/.*:[[:space:]]*\"([^\"]*)\"$/\1/"
}

# The statement's subjects[0] fields (the FIRST sha256 / name in the wire is subjects[0]).
STMT_SUBJECT0_SHA="$(json_first_str "$STATEMENT" sha256)"
STMT_SUBJECT0_NAME="$(json_first_str "$STATEMENT" name)"
STMT_PREDICATE_TYPE="$(json_first_str "$STATEMENT" predicateType)"

# --- INV-030 re-check: refuse (before cosign) on ANY hand-off mismatch --------------

# (1) commit-subject binding: sha256(commit-X blob) must equal the statement's subjects[0]
#     digest — the blob cosign --check-claims will bind, re-checked INDEPENDENTLY here.
BLOB_SHA="$(sha256sum "$BLOB" | cut -d' ' -f1)"
[ "$STMT_SUBJECT0_SHA" = "$BLOB_SHA" ] \
  || refuse "statement subjects[0] sha256 != SHA-256(entry-commit.blob) — the Statement does not bind the commit subject"

# (2) predicate type: the frozen entry URI (a determinism-typed statement is refused here).
[ "$STMT_PREDICATE_TYPE" = "$FROZEN_PREDICATE_TYPE" ] \
  || refuse "statement predicateType '$STMT_PREDICATE_TYPE' != the frozen phase-entry URI"

# (3) commit subject name: the canonical phase-entry-commit.
[ "$STMT_SUBJECT0_NAME" = "$CANONICAL_COMMIT_SUBJECT_NAME" ] \
  || refuse "statement subjects[0] name '$STMT_SUBJECT0_NAME' != $CANONICAL_COMMIT_SUBJECT_NAME"

# --- attempt guard: a rerun mints nothing (RS-036) ----------------------------------

ATTEMPT_ENV="${GITHUB_RUN_ATTEMPT:-}"
# A missing/empty attempt fails CLOSED — never silently treated as 1.
[ -n "$ATTEMPT_ENV" ] \
  || refuse_attempt "GITHUB_RUN_ATTEMPT is missing/empty (fail-closed; not treated as 1)"
[ "$ATTEMPT_ENV" = "1" ] \
  || refuse_attempt "GITHUB_RUN_ATTEMPT=$ATTEMPT_ENV is a re-run (must be 1)"

echo "[sign-entry] recorded run_attempt=${ATTEMPT_ENV}"

# --- signing seam: the transcript-frozen cosign argv --------------------------------

# Corrected OWNS the Statement semantics; the producer wrote entry-statement.json through
# EntryStatementCodec.SerializeEntryStatementJson (the single canonical byte-source the
# verifier parses). cosign constructs/signs the DSSE envelope and obtains the Fulcio cert as
# transport. The commit-X blob is the attested subject (trailing positional).
echo "[sign-entry] signing with pinned cosign (${COSIGN##*/}) — attest-blob --statement" >&2
"$COSIGN" attest-blob --statement "$STATEMENT" --bundle "$OUT_BUNDLE" --new-bundle-format=true --yes "$BLOB"

echo "[sign-entry] OK: signed entry bundle written to ${OUT_BUNDLE##*/}"
exit 0
