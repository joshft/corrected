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
Z3_ARCHIVE_NAME="z3-4.12.1-x64-glibc-2.35.zip"
SUPPORTED_RID="linux-x64"
RID_FAIL_MESSAGE="RID not supported by this spike; proven RIDs: linux-x64 (see ADR-0001)"

# PRH-004 layer-1 dispatch (TA-B8/TA-A12): identical contract to run-spike.sh —
# every external command goes through run_cmd. The static test audits every
# scripts/*.sh: allowlist array equality, call-site first arguments, deny-set
# scan, and option adjacency on the fetch tool.
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

set -euo pipefail

# Deterministic tool resolution even under `env -i` (empty environment).
PATH="/usr/bin:/bin"
export PATH

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
SPIKE_ROOT="$(cd -- "$SCRIPT_DIR/.." && pwd)"

RUN_ROOT=""
while [ $# -gt 0 ]; do
  case "$1" in
    --run-root) RUN_ROOT="$2"; shift 2 ;;
    *) echo "provision-z3: unknown argument '$1'" >&2; exit 20 ;;
  esac
done
if [ -z "$RUN_ROOT" ]; then
  echo "provision-z3: --run-root <dir> is required (DD-008: all run products live under the run root)" >&2
  exit 20
fi

# BND-002: fail closed on any unsupported RID BEFORE any network fetch —
# never an unverified download. SPIKE_RID_OVERRIDE exists for the INV-014
# fail-closed test only.
HOST_RID=""
case "${SPIKE_RID_OVERRIDE:-}" in
  "")
    case "${OSTYPE:-}" in
      linux*)
        case "${HOSTTYPE:-}" in
          x86_64) HOST_RID="linux-x64" ;;
          *) HOST_RID="linux-${HOSTTYPE:-unknown}" ;;
        esac ;;
      *) HOST_RID="${OSTYPE:-unknown}" ;;
    esac ;;
  *) HOST_RID="$SPIKE_RID_OVERRIDE" ;;
esac
if [ "$HOST_RID" != "$SUPPORTED_RID" ]; then
  echo "$RID_FAIL_MESSAGE" >&2
  exit 21
fi

run_cmd mkdir -p -- "$RUN_ROOT"
RUN_ROOT="$(cd -- "$RUN_ROOT" && pwd)"
INSTALL_DIR="$RUN_ROOT/solver/z3-$Z3_VERSION/bin"
INSTALL_PATH="$INSTALL_DIR/z3"
MARKER="$RUN_ROOT/solver/z3-$Z3_VERSION/.provisioned-from"
ARCHIVE="$RUN_ROOT/$Z3_ARCHIVE_NAME"
CACHE_DIR="$SPIKE_ROOT/out/cache"
CACHED_ARCHIVE="$CACHE_DIR/$Z3_ARCHIVE_NAME"

sha_of() {
  local file="$1" digest rest
  read -r digest rest < <(run_cmd sha256sum -- "$file")
  printf '%s' "$digest"
}

# FULL state: a prior successful install is left untouched (AP-016 double-run
# identical state). The marker records the archive digest the install came
# from; anything inconsistent is treated as partial state and rebuilt.
if [ -x "$INSTALL_PATH" ] && [ -f "$MARKER" ]; then
  if [ "$(< "$MARKER")" = "$Z3_SHA256" ]; then
    echo "provision-z3: already provisioned at solver/z3-$Z3_VERSION/bin/z3 (archive pin $Z3_SHA256); no changes" >&2
    exit 0
  fi
fi

# Acquire a digest-valid archive: reuse a valid staged/cached copy, else fetch.
archive_valid() {
  [ -f "$1" ] && [ "$(sha_of "$1")" = "$Z3_SHA256" ]
}

if ! archive_valid "$ARCHIVE"; then
  if [ -f "$ARCHIVE" ]; then
    echo "provision-z3: staged archive failed the pinned digest check (partial/corrupt state) — refetching (AP-016)" >&2
    run_cmd mv -- "$ARCHIVE" "$ARCHIVE.corrupt"
  fi
  if archive_valid "$CACHED_ARCHIVE"; then
    run_cmd tar -C "$CACHE_DIR" -cf - "$Z3_ARCHIVE_NAME" | run_cmd tar -C "$RUN_ROOT" -xf -
  else
    TMP_DL="$ARCHIVE.download"
    run_cmd curl --disable -fsSL -o "$TMP_DL" "$Z3_URL"
    if [ "$(sha_of "$TMP_DL")" != "$Z3_SHA256" ]; then
      echo "provision-z3: downloaded asset does not match the pinned SHA-256 $Z3_SHA256 — aborting fail-closed (BND-002)" >&2
      exit 22
    fi
    run_cmd mv -- "$TMP_DL" "$ARCHIVE"
    if ! archive_valid "$CACHED_ARCHIVE"; then
      run_cmd mkdir -p -- "$CACHE_DIR"
      run_cmd tar -C "$RUN_ROOT" -cf - "$Z3_ARCHIVE_NAME" | run_cmd tar -C "$CACHE_DIR" -xf -
    fi
  fi
fi
if ! archive_valid "$ARCHIVE"; then
  echo "provision-z3: no digest-valid archive available — aborting fail-closed (BND-002)" >&2
  exit 22
fi

# Install: extract to a temp area, then move the binary into the
# NON-DISCOVERABLE install location (INV-003/RS-003a: never on PATH, never at
# the assembly-adjacent {output}/z3/bin/z3-4.12.1 fallback Dafny searches).
EXTRACT_DIR="$(run_cmd mktemp -d "$RUN_ROOT/z3-extract.XXXXXX")"
run_cmd unzip -q -o "$ARCHIVE" "z3-$Z3_VERSION-x64-glibc-2.35/bin/z3" -d "$EXTRACT_DIR"
run_cmd mkdir -p -- "$INSTALL_DIR"
run_cmd mv -f -- "$EXTRACT_DIR/z3-$Z3_VERSION-x64-glibc-2.35/bin/z3" "$INSTALL_PATH"
run_cmd chmod 0755 "$INSTALL_PATH"
printf '%s\n' "$Z3_SHA256" > "$MARKER.tmp"
run_cmd mv -- "$MARKER.tmp" "$MARKER"
BINARY_SHA="$(sha_of "$INSTALL_PATH")"
printf '%s\n' "$BINARY_SHA" > "$INSTALL_DIR/../binary.sha256.tmp"
run_cmd mv -- "$INSTALL_DIR/../binary.sha256.tmp" "$INSTALL_DIR/../binary.sha256"

echo "provision-z3: installed Z3 $Z3_VERSION at solver/z3-$Z3_VERSION/bin/z3 (archive pin $Z3_SHA256; binary sha256 $BINARY_SHA)" >&2
exit 0
