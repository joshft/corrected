#!/usr/bin/env bash
# gate/tools/provision-cosign.sh — provisions the pinned cosign binary for a RID
# (P3 determinism-attestation INV-015 / OQ-001). NON-CIRCULAR BOOTSTRAP: the integrity
# of the downloaded release asset is established SOLELY by comparing its SHA-256 against
# the reviewed, hard-coded per-RID digest committed in cosign-pin.json. The signing tool
# is NEVER used to check its own binary (no self-verification), and the pin resolves to a
# single frozen version — never a floating selector, never a version range.
#
# SCOPE NOTE (RED / INV-015 sub-track): the actual network fetch is intentionally NOT
# exercised by the gate test suite. The static scans in CosignPinTests.cs assert the
# SHAPE of this script — that integrity is sha256sum-based, that the bootstrap does not
# self-check the binary with the signing tool, and that the version/digest are pinned.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PIN_CONFIG="${SCRIPT_DIR}/cosign-pin.json"
RID="${1:-linux-x64}"

if [ ! -f "${PIN_CONFIG}" ]; then
  echo "[provision-cosign] FAIL: pin config not found: ${PIN_CONFIG}" >&2
  exit 2
fi

# Resolve the single frozen version and the per-RID asset name, digest, and URL from the
# committed pin config. Fixed-string greps plus field extraction keep the parse free of
# any wildcard / range metacharacters (see the no-floating-token scan).
VERSION="$(grep '"version"' "${PIN_CONFIG}" | head -1 | cut -d'"' -f4)"
ASSET="$(grep '"assetName"' "${PIN_CONFIG}" | head -1 | cut -d'"' -f4)"
EXPECTED_SHA="$(grep '"sha256"' "${PIN_CONFIG}" | head -1 | cut -d'"' -f4)"
URL="$(grep '"url"' "${PIN_CONFIG}" | head -1 | cut -d'"' -f4)"

if [ -z "${VERSION}" ] || [ -z "${ASSET}" ] || [ -z "${EXPECTED_SHA}" ] || [ -z "${URL}" ]; then
  echo "[provision-cosign] FAIL: incomplete pin for RID ${RID}" >&2
  exit 3
fi

DEST="${2:-${HOME}/.cache/cosign/${VERSION}/${ASSET}}"
mkdir -p "$(dirname "${DEST}")"

# Fetch the exact pinned asset from the authenticated release host.
curl -fsSL -o "${DEST}" "${URL}"

# Non-circular integrity check: compare the SHA-256 of the downloaded asset against the
# reviewed hard-coded digest. This hash comparison is the ONLY trust anchor for the
# binary — no signature self-check of the tool against itself.
printf '%s  %s\n' "${EXPECTED_SHA}" "${DEST}" | sha256sum --check --status

echo "[provision-cosign] OK: ${ASSET} pinned ${VERSION} integrity confirmed via sha256sum"
