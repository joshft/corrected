#!/usr/bin/env bash
# scripts/determinism-lane.sh — PR1 (P3 determinism-attestation, INV-005/INV-024/
# RS-028): the EXTRACTED serial determinism lane the dedicated CI job invokes
# VERBATIM (AP-020/PMB-001 — an execution surface, never inline YAML `run:` steps
# a grep cannot exercise). It drives TWO nested run-spike.sh runs into per-run
# subroots (<run-root>/r1 and <run-root>/r2, mirroring Inv010's root1/root2),
# then emits a per-run x per-role RunReceipt at <run-root>/receipts/
# determinism-receipt.json carrying the observed platform identity (INV-005) and
# the derived comparison_status (INV-002). It EXITS NON-ZERO on
# comparison_status=different (INV-003). PR1 signs NOTHING (PRH-005): no signer,
# no rerun-into-green, no error-swallowing.
#
# The comparison + receipt emission reuses the ALREADY-BUILT aggregator host
# (dotnet exec ... --emit-determinism-receipt) so the spike's pinned project set
# is unchanged (MiniAudit MA-UC-4) — no new solution project is added.
#
# Invocation (hardened, per PRH-004):
#   env -i HOME="$HOME" bash -p scripts/determinism-lane.sh --run-root DIR [--dotnet-root PATH]

# The bootstrap command allowlist (PRH-004/TA-B8) — equal to BootstrapAllowlist.Commands.
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
    echo "determinism-lane: DENY non-allowlisted command: $cmd (PRH-004)" >&2
    exit 20
  fi
  command "$cmd" "$@"
}

# Fail-closed hardening check (PRH-004/codex R4-08): refuse an unhardened invocation.
case "$-" in
  *p*) : ;;
  *)
    echo "determinism-lane: refusing the unhardened invocation — use: env -i HOME=\"\$HOME\" bash -p scripts/determinism-lane.sh --run-root DIR (PRH-004)" >&2
    exit 20 ;;
esac

set -euo pipefail
PATH="/usr/bin:/bin"
export PATH

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
SPIKE_ROOT="$(cd -- "$SCRIPT_DIR/.." && pwd)"

# SINGLE-SOURCED in-tree run-root guard (RLT-1/AUDIT-ARCH-1) — mirrors
# run-spike.sh's ensure_in_tree_run_root. Creates the root (so `cd && pwd` can
# canonicalize it), canonicalizes it, then refuses an out-of-tree root fail-closed,
# removing the just-created EMPTY dir first so a refusal leaves NO orphan. `rm -d`
# removes ONLY an empty dir (allowlist-safe — never `rm -rf` a caller-supplied path).
# Mutates the global RUN_ROOT.
#
# Why (BLOCKING-1, cverify 2026-07-29): an out-of-tree root makes the nested
# run-spike.sh pass an ABSOLUTE SpikeRunRootRel to MSBuild — build outputs land
# in-tree while the DD-008 completeness check resolves the true absolute root
# (spurious INCOMPLETE) — and leaks an absolute host path into the recorded build
# argv (PRH-005). Refuse it HERE, before any SDK/build work, so the CI lane
# (INV-024/RS-028) can never mis-drive it.
ensure_in_tree_run_root() {
  # LOW-1: `rm -d` the dir on refusal ONLY IF WE JUST CREATED IT — a pre-existing
  # operator dir is left untouched (never delete a dir we did not make).
  local created=""
  [ -d "$RUN_ROOT" ] || created=1
  run_cmd mkdir -p -- "$RUN_ROOT"
  RUN_ROOT="$(cd -- "$RUN_ROOT" && pwd)"
  case "$RUN_ROOT" in
    "$SPIKE_ROOT"/*) return 0 ;;
  esac
  if [ -n "$created" ]; then
    run_cmd rm -d -- "$RUN_ROOT" 2>/dev/null || true
  fi
  echo "determinism-lane: refusing an out-of-tree --run-root '$RUN_ROOT' — it must be within the spike tree ($SPIKE_ROOT) so the nested SpikeRunRootRel stays SPIKE-relative (DD-008 build/output divergence, PRH-005 argv leak). Use an in-tree root, e.g. --run-root out/determinism-lane." >&2
  exit 20
}

RUN_ROOT=""
DOTNET_ROOT_ARG=""
while [ $# -gt 0 ]; do
  case "$1" in
    --run-root) RUN_ROOT="${2:-}"; shift 2 ;;
    --dotnet-root) DOTNET_ROOT_ARG="${2:-}"; shift 2 ;;
    *) echo "determinism-lane: unknown argument '$1'" >&2; exit 20 ;;
  esac
done

if [ -z "$RUN_ROOT" ]; then
  echo "determinism-lane: --run-root is required (the per-run subroots r1/r2 and the receipt live under it)" >&2
  exit 20
fi
ensure_in_tree_run_root

# --- .NET SDK resolution (mirrors run-spike.sh): --dotnet-root / DOTNET_ROOT,
# --- then the HOME-local install, then the pointer a controller run cached.
resolve_sdk_bin() {
  local cand cached
  for cand in \
    "${DOTNET_ROOT_ARG:+$DOTNET_ROOT_ARG/dotnet}" \
    "${DOTNET_ROOT:+$DOTNET_ROOT/dotnet}" \
    "${HOME:+$HOME/.dotnet/dotnet}"; do
    if [ -n "$cand" ] && [ -x "$cand" ]; then printf '%s' "$cand"; return 0; fi
  done
  if [ -f "$SPIKE_ROOT/out/cache/dotnet-root" ]; then
    read -r cached < "$SPIKE_ROOT/out/cache/dotnet-root" || true
    if [ -n "${cached:-}" ] && [ -x "$cached/dotnet" ]; then printf '%s' "$cached/dotnet"; return 0; fi
  fi
  return 1
}
if ! SDK_BIN="$(resolve_sdk_bin)"; then
  echo "determinism-lane: no pinned .NET SDK found (checked --dotnet-root, DOTNET_ROOT, HOME/.dotnet, out/cache/dotnet-root)" >&2
  exit 3
fi

R1="$RUN_ROOT/r1"
R2="$RUN_ROOT/r2"

# Pass --dotnet-root through to the nested runs when we were handed one (the
# clean-environment re-exec strips inherited DOTNET_ROOT, so it must be an ARG).
declare -a NESTED_ARGS=()
if [ -n "$DOTNET_ROOT_ARG" ]; then
  NESTED_ARGS+=(--dotnet-root "$DOTNET_ROOT_ARG")
fi

echo "determinism-lane: nested determinism run 1 -> $R1" >&2
run_cmd bash -p "$SPIKE_ROOT/scripts/run-spike.sh" --run-root "$R1" ${NESTED_ARGS[@]+"${NESTED_ARGS[@]}"}
echo "determinism-lane: nested determinism run 2 -> $R2" >&2
run_cmd bash -p "$SPIKE_ROOT/scripts/run-spike.sh" --run-root "$R2" ${NESTED_ARGS[@]+"${NESTED_ARGS[@]}"}

# The nested run builds the aggregator host under its run root; reuse it (no
# separate build). Prefer r1's, fall back to r2's.
AGG_REL="build/SpikeAggregator/bin/Debug/net10.0/SpikeAggregator.dll"
AGG_DLL=""
for cand in "$R1/$AGG_REL" "$R2/$AGG_REL"; do
  if [ -f "$cand" ]; then AGG_DLL="$cand"; break; fi
done
if [ -z "$AGG_DLL" ]; then
  echo "determinism-lane: aggregator host not found under the run roots ($AGG_REL) — the nested runs did not build (infrastructure fault)" >&2
  exit 3
fi

RECEIPT="$RUN_ROOT/receipts/determinism-receipt.json"
run_cmd mkdir -p -- "$RUN_ROOT/receipts"

# Emit the receipt (per-run/per-role comparison + observed platform identity).
# The emitter exits NON-ZERO on comparison_status=different (INV-003); propagate.
export DOTNET_ROOT="${SDK_BIN%/*}"
set +e
run_cmd "$SDK_BIN" exec "$AGG_DLL" --emit-determinism-receipt \
  --r1 "$R1" \
  --r2 "$R2" \
  --schema "$SPIKE_ROOT/schema/evidence-schema.json" \
  --registry "$SPIKE_ROOT/schema/schema-version-registry.json" \
  --kind-registry "$SPIKE_ROOT/manifest/determinism/schema-kind-registry.json" \
  --role-registry "$SPIKE_ROOT/manifest/determinism/role-registry.json" \
  --policy-map "$SPIKE_ROOT/manifest/determinism/projection-policy-map.json" \
  --out "$RECEIPT"
EMIT_RC=$?
set -e

echo "determinism-lane: receipt at $RECEIPT (emitter exit $EMIT_RC)" >&2
exit "$EMIT_RC"
