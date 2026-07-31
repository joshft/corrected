#!/usr/bin/env bash
# gate/tools/sign-determinism.sh — the EXTRACTED signer operator surface for the
# P3 determinism-attestation T4 SIGNER slice (INV-007/008/009/024, RS-028/RS-036),
# AFTER the Statement-builder reconciliation.
#
# The two-job signing workflow (.github/workflows/p3-determinism-sign.yml) invokes
# this committed script VERBATIM (AP-020/PMB-001 — never inline `run:` steps a grep
# can only reconstruct). It RE-VALIDATES the same-run producer hand-off before any
# cosign call and REFUSES on any mismatch, enforces the run_attempt==1 guard, and —
# only on a fully-valid hand-off — invokes the single pinned cosign with the
# transcript-frozen `attest-blob --statement` argv (DD-002 / OQ-001).
#
# DOCUMENTED INVOCATION (run from the repo root; argv[0] is the RELATIVE path):
#   GITHUB_SHA=<40hex> GITHUB_RUN_ID=<digits> GITHUB_RUN_ATTEMPT=1 COSIGN_BIN=<abs cosign> \
#     bash gate/tools/sign-determinism.sh \
#       --artifacts-dir <DIR> --manifest <MANIFEST_FILE> --out <BUNDLE_OUT>
#
# HAND-OFF (<DIR>, same-run @actions/artifact contents — RS-032/EA-010). The SIGNED
# SUBJECT is the determinism RunReceipt, and the Statement is CORRECTED-BUILT (this
# signer CONSUMES it, never hand-rolls it):
#   * determinism-receipt.json         — the determinism RunReceipt (the SIGNED SUBJECT).
#   * receipt.sha256                    — the producer-DECLARED digest of the receipt bytes.
#   * determinism-statement.json        — the Corrected-built in-toto Statement
#         (DeterminismAttestation.SerializeStatementJson) the signer signs; NEVER built here.
#   * ci-context.json                   — { run_id, run_attempt, producing_job_result }: the
#         CI-run metadata that is NOT part of a RunReceipt.
#   * <MANIFEST_FILE> (via --manifest)  — the determinism-subject manifest.
#
# The cosign version/digest are SINGLE-SOURCED from gate/tools/cosign-pin.json (never
# a divergent in-script literal); the new-bundle-format `=false` contingency is NOT
# taken (DD-002 / RS-008 resolved). This slice NEVER produces a real signature: in the
# gate tests a fake cosign is injected via COSIGN_BIN; in CI a digest-validated pinned
# binary is provisioned first. No network / OIDC / Rekor work happens in this script.

set -euo pipefail

# Resolve our own directory ROBUSTLY (AP-020): capture it from BASH_SOURCE BEFORE any
# `cd`, so the documented cwd + relative argv[0] form resolves the co-located pin file.
SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"

# The frozen Corrected determinism contract literals the class-7 statement check binds
# to INDEPENDENTLY (never trusting a value echoed back from the statement itself).
FROZEN_PREDICATE_TYPE="https://correctless.org/attestations/determinism/v1"
CANONICAL_SUBJECT_NAME="determinism-run-receipt"

# --- fail-closed diagnostics --------------------------------------------------------

# INV-007 re-check refusal: non-zero exit, a "REFUSE" line on stderr, NO cosign call.
refuse() {
  echo "REFUSE (INV-007): $1 — the signer re-check failed; not invoking cosign." >&2
  exit 7
}

# INV-008 / RS-036 attempt-guard refusal: a rerun (or an env<->ci-context attempt
# disagreement) mints NOTHING. The exact wording is asserted by the guard test.
refuse_attempt() {
  echo "REFUSE (INV-008/RS-036): $1 — re-runs never mint a new attestation; push a new reviewed commit to re-attest at run_attempt 1." >&2
  exit 8
}

# --- argument parsing ---------------------------------------------------------------

ARTIFACTS_DIR=""
MANIFEST_FILE=""
OUT_BUNDLE=""
while [ $# -gt 0 ]; do
  case "$1" in
    --artifacts-dir) ARTIFACTS_DIR="${2:-}"; shift 2 ;;
    --manifest)      MANIFEST_FILE="${2:-}"; shift 2 ;;
    --out)           OUT_BUNDLE="${2:-}"; shift 2 ;;
    *) echo "REFUSE (usage): unknown argument '$1' (expected --artifacts-dir/--manifest/--out)." >&2; exit 2 ;;
  esac
done

[ -n "$ARTIFACTS_DIR" ] || refuse "--artifacts-dir is required"
[ -n "$MANIFEST_FILE" ] || refuse "--manifest is required"
[ -n "$OUT_BUNDLE" ]    || refuse "--out is required"

RECEIPT="${ARTIFACTS_DIR}/determinism-receipt.json"
DECLARED_FILE="${ARTIFACTS_DIR}/receipt.sha256"
STATEMENT="${ARTIFACTS_DIR}/determinism-statement.json"
CI_CONTEXT="${ARTIFACTS_DIR}/ci-context.json"

[ -f "$RECEIPT" ]       || refuse "producer receipt missing: determinism-receipt.json"
[ -f "$DECLARED_FILE" ] || refuse "producer declared digest missing: receipt.sha256"
[ -f "$CI_CONTEXT" ]    || refuse "producer CI context missing: ci-context.json"
[ -f "$MANIFEST_FILE" ] || refuse "subject manifest missing at --manifest"

# --- single-sourced cosign pin ------------------------------------------------------

# READ the frozen version from the committed pin (single source of truth — never a
# divergent in-script literal). The default provisioned binary path is derived from
# it; an injected COSIGN_BIN (the test/CI seam) overrides the resolved binary.
PIN_CONFIG="${SCRIPT_DIR}/cosign-pin.json"
[ -f "$PIN_CONFIG" ] || refuse "cosign pin config missing: cosign-pin.json"
PINNED_COSIGN_VERSION="$(grep '"version"' "$PIN_CONFIG" | head -1 | cut -d'"' -f4)"
PINNED_COSIGN_ASSET="$(grep '"assetName"' "$PIN_CONFIG" | head -1 | cut -d'"' -f4)"
[ -n "$PINNED_COSIGN_VERSION" ] || refuse "cosign pin config has no version"

COSIGN="${COSIGN_BIN:-}"
if [ -z "$COSIGN" ]; then
  # The default provisioned path (provision-cosign.sh DEST default). Used only when
  # no injected binary is supplied; the per-RID digest governs THAT bootstrap path.
  COSIGN="${HOME}/.cache/cosign/${PINNED_COSIGN_VERSION}/${PINNED_COSIGN_ASSET}"
fi
[ -x "$COSIGN" ] || refuse "resolved cosign binary is not executable: ${COSIGN##*/}"

# --- field extraction (BCL-free; each field is a "key": "value" string) -------------

json_str() {
  # $1 = file, $2 = key; echoes the string value of "key": "value" (empty if absent).
  sed -nE "s/.*\"$2\"[[:space:]]*:[[:space:]]*\"([^\"]*)\".*/\1/p" "$1" | head -1
}

# Receipt SUBJECT fields (RunReceipt shape — NO run_id / run_attempt live here).
RCPT_COMMIT="$(json_str "$RECEIPT" attested_commit)"
RCPT_MANIFEST_DIGEST="$(json_str "$RECEIPT" subject_manifest_digest)"
RCPT_POLICY_VERSION="$(json_str "$RECEIPT" policy_version)"

# CI-run metadata (NOT part of the RunReceipt) — now carried in ci-context.json.
CTX_RUN_ID="$(json_str "$CI_CONTEXT" run_id)"
CTX_ATTEMPT="$(json_str "$CI_CONTEXT" run_attempt)"
CTX_JOB_RESULT="$(json_str "$CI_CONTEXT" producing_job_result)"

# --- INV-007 re-check: refuse (before cosign) on ANY hand-off mismatch --------------

# (1) Artifact digest: the recomputed SHA-256 of the receipt bytes must equal the
#     producer-DECLARED digest (tamper re-check).
DECLARED_DIGEST="$(tr -d '[:space:]' < "$DECLARED_FILE")"
ACTUAL_DIGEST="$(sha256sum "$RECEIPT" | cut -d' ' -f1)"
[ "$DECLARED_DIGEST" = "$ACTUAL_DIGEST" ] \
  || refuse "artifact digest mismatch (declared receipt.sha256 != actual SHA-256 of determinism-receipt.json)"

# (2) Schema: the receipt must be a well-formed determinism RunReceipt — the required
#     subject-bound fields subject_manifest_digest + policy_version must be present.
[ -n "$RCPT_MANIFEST_DIGEST" ] \
  || refuse "receipt is not a well-formed determinism RunReceipt (missing subject_manifest_digest)"
[ -n "$RCPT_POLICY_VERSION" ] \
  || refuse "receipt is not a well-formed determinism RunReceipt (missing policy_version)"

# (3) attested_commit must equal the trusted trigger SHA.
[ "$RCPT_COMMIT" = "${GITHUB_SHA:-}" ] \
  || refuse "attested_commit '$RCPT_COMMIT' != GITHUB_SHA"

# (4) run_id (ci-context) must equal the current run.
[ "$CTX_RUN_ID" = "${GITHUB_RUN_ID:-}" ] \
  || refuse "ci-context run_id '$CTX_RUN_ID' != GITHUB_RUN_ID"

# (5) The producing job (ci-context) must have succeeded.
[ "$CTX_JOB_RESULT" = "success" ] \
  || refuse "ci-context producing_job_result '$CTX_JOB_RESULT' != success"

# (6) subject-manifest-at-attested_commit: the receipt digest must bind the real
#     manifest checked out at attested_commit.
MANIFEST_DIGEST="$(sha256sum "$MANIFEST_FILE" | cut -d' ' -f1)"
[ "$RCPT_MANIFEST_DIGEST" = "$MANIFEST_DIGEST" ] \
  || refuse "subject_manifest_digest != SHA-256(manifest-at-attested_commit)"

# (7) Statement-binds-subject: the Corrected-built Statement must exist and bind THIS
#     receipt subject. The signer signs THIS file and NEVER builds its own — so it fails
#     CLOSED if the Statement is absent or tampered. Every expectation is INDEPENDENT: a
#     fresh SHA-256 of the receipt + the frozen predicate-type URI + canonical subject
#     name — never a value echoed from the Statement.
[ -f "$STATEMENT" ] \
  || refuse "Corrected-built determinism-statement.json is absent (the signer must not build its own)"
STMT_SUBJECT_SHA="$(json_str "$STATEMENT" sha256)"
STMT_PREDICATE_TYPE="$(json_str "$STATEMENT" predicateType)"
STMT_SUBJECT_NAME="$(json_str "$STATEMENT" name)"
[ "$STMT_SUBJECT_SHA" = "$ACTUAL_DIGEST" ] \
  || refuse "statement subject sha256 != SHA-256(receipt bytes) — the Statement does not bind the subject"
[ "$STMT_PREDICATE_TYPE" = "$FROZEN_PREDICATE_TYPE" ] \
  || refuse "statement predicateType '$STMT_PREDICATE_TYPE' != the frozen determinism URI"
[ "$STMT_SUBJECT_NAME" = "$CANONICAL_SUBJECT_NAME" ] \
  || refuse "statement subject name '$STMT_SUBJECT_NAME' != $CANONICAL_SUBJECT_NAME"

# --- INV-008 attempt guard: a rerun mints nothing (RS-036) --------------------------

ATTEMPT_ENV="${GITHUB_RUN_ATTEMPT:-}"
# A missing/empty attempt fails CLOSED — never silently treated as 1.
[ -n "$ATTEMPT_ENV" ] \
  || refuse_attempt "GITHUB_RUN_ATTEMPT is missing/empty (fail-closed; not treated as 1)"
[ "$ATTEMPT_ENV" = "1" ] \
  || refuse_attempt "GITHUB_RUN_ATTEMPT=$ATTEMPT_ENV is a re-run (must be 1)"
# The env and the producer-recorded ci-context attempt must AGREE (a ci-context attempt
# > 1 under a spoofed env=1 is still a rerun product).
[ "$CTX_ATTEMPT" = "1" ] \
  || refuse_attempt "ci-context run_attempt=$CTX_ATTEMPT disagrees with env attempt=1"

# Record the observed attempt ATTRIBUTABLY (INV-008 — the attempt is recorded).
echo "[sign-determinism] recorded run_attempt=${ATTEMPT_ENV}"

# --- INV-009 signing seam: the transcript-frozen cosign argv (DD-002) ---------------

# Corrected OWNS the Statement semantics; the producer wrote determinism-statement.json
# through DeterminismAttestation.SerializeStatementJson (the single canonical byte-source
# the future verifier reconstructs). cosign constructs/signs the DSSE envelope and obtains
# the Fulcio cert as transport. The receipt bytes are the attested blob (trailing positional).
echo "[sign-determinism] signing with pinned cosign (${COSIGN##*/}) — attest-blob --statement" >&2
"$COSIGN" attest-blob --statement "$STATEMENT" --bundle "$OUT_BUNDLE" --new-bundle-format=true --yes "$RECEIPT"

echo "[sign-determinism] OK: signed bundle written to ${OUT_BUNDLE##*/}"
exit 0
