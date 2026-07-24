#!/usr/bin/env bash
# scripts/run-spike.sh — the canonical entry point: bootstrap controller
# (INV-006 ownership topology; PRH-004 layer 1).
#
# INVOCATION CONTRACT (PRH-004, codex R4-08, TA-B9):
#   env -i HOME="$HOME" bash -p scripts/run-spike.sh
# Two hardening mechanisms actually RUN at entry (MA-HI-1 — the earlier
# "self-enforcing" claim overstated what was enforced):
#  (1) PRIVILEGED-MODE refusal: invoked WITHOUT -p (plain
#      `bash scripts/run-spike.sh`) the controller refuses (exit 30) — it never
#      proceeds under a shell that may already have executed a poisoned BASH_ENV.
#  (2) CLEAN-ENVIRONMENT re-exec: `bash -p` only neutralizes BASH_ENV/ENV;
#      every OTHER inherited variable survives, and toolchain-steering ones
#      (DOTNET_ROOT selects the SDK muxer resolve_sdk uses first — a
#      version-spoofing wrapper would void the AP-015 exact-SDK pin, a
#      FALSE-COMPATIBLE threat, TB-004) must not be honored from an inherited
#      environment. The controller re-execs itself under `env -i` so only an
#      explicit allowlist survives, then asserts nothing outside it is present
#      (a forged re-exec sentinel is caught by the assertion, fail-closed).
#
# PRH-004 LAYER-1 DISPATCH (TA-B8): every external command launch goes through
# run_cmd; the first argument must resolve into ALLOWLIST (stated resolution
# rule: the BASENAME of the command must be an allowlist member — this is how
# the BND-004 private net8 host, an absolute path whose basename matches the
# SDK muxer name, is launched; a bare SDK muxer name resolves to the pinned
# user-local install, never PATH). The static test parses the ALLOWLIST array,
# verifies it equals BootstrapAllowlist.Commands, audits run_cmd call sites,
# and deny-scans for direct fetch/interpreter invocations.
#
# OWNERSHIP (codex F7/R2-4/R3-5): this controller mints the run_id, creates
# out/<run_id>/, holds the single absolute /proc/uptime monotonic deadline from
# run mint through final aggregation, runs the inner controller in its own
# setsid session under an asynchronous TERM->KILL supervisor (the outer
# watchdog is the PARENT of the inner controller, not a test executed by it),
# and emits nonce-bound restore/build receipts that the managed aggregator
# VALIDATES (it never claims to have performed them).
#
# PHASES: provision -> locked restore -> build -> [suite, canonical runs only]
# -> probes -> aggregation. Nested/variance runs (any of --run-root, --out,
# --config, --solver present — the form every in-suite launch uses) skip the
# suite phase and record final_suite_status=unknown, so a route verdict of
# COMPATIBLE is only ever computable for a canonical operator run whose suite
# exit is success (codex R4-02) and nested runs cannot recurse.
#
# RUN-ROOT LAYOUT (Corrected.Spike.Contracts.RunLayout):
#   run-context.json            artifact receipt; SPIKE_RUN_CONTEXT points here
#   sentinel/ledger.json        nonce ledger, pre-created at count zero (RS-003d)
#   receipts/restore-receipt.json  nonce-bound locked-restore attestation (P01)
#   receipts/build-receipt.json    nonce-bound build attestation (P02) + artifact digests
#   receipts/env-audit.json     the EXACT dictionary passed at each launch (EA-008)
#   receipts/aggregation.json   run_id + consumed_report_paths (performed launches only)
#   reports/                    route-a.json, route-b.json, control-*.json, run-report.json
#   solver/z3-4.12.1/bin/z3     provisioned solver (INV-003)
#   build/<Project>/...         ALL build outputs (DD-008; stale repo-local bin unreachable)
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
    echo "run-spike: DENY non-allowlisted command: $cmd (PRH-004)" >&2
    exit 20
  fi
  if [ "$base" = "$cmd" ] && [ "$base" = "dotnet" ]; then
    cmd="$SPIKE_DOTNET_BIN"
  fi
  command "$cmd" "$@"
}

# Fail-closed hardening check (codex R4-08): noninteractive bash executes
# $BASH_ENV before the first script line UNLESS started in privileged mode.
case "$-" in
  *p*) : ;;
  *)
    echo "run-spike: refusing the unhardened invocation — use: env -i HOME=\"\$HOME\" bash -p scripts/run-spike.sh (PRH-004/codex R4-08; a poisoned BASH_ENV may already have executed)" >&2
    exit 30 ;;
esac

# --- MA-HI-1: CLEAN-ENVIRONMENT enforcement for the canonical entry -----------
# On the FIRST (non-re-exec) entry we re-exec ourselves under `env -i` so only an
# explicit allowlist survives — every toolchain/resolution-steering variable
# (DOTNET_ROOT, DOTNET_HOST_PATH, NUGET_PACKAGES, SSL_CERT_*, GIT_*, …) is
# stripped. SPIKE_PARENT_DEADLINE (deadline inheritance, QA-018) and
# SPIKE_RID_OVERRIDE (the provisioning fault-injection hook the committed
# QA-005 test exercises) are SANCTIONED and can only ever fail CLOSED (a
# spurious INCOMPLETE, never a false COMPATIBLE), so they are preserved across
# the re-exec; the false-COMPATIBLE vector (DOTNET_ROOT et al.) is closed. The
# sentinel proves the re-exec ran; the assertion below catches a forged sentinel
# that skipped it (a poisoned variable would still be present) and fails closed.
PATH="/usr/bin:/bin"; export PATH
if [ -z "${SPIKE_REEXEC_SENTINEL:-}" ]; then
  reexec_argv=(/usr/bin/env -i HOME="${HOME:-}" PATH="/usr/bin:/bin" SPIKE_REEXEC_SENTINEL=clean)
  case "${SPIKE_PARENT_DEADLINE:-}" in
    ''|*[!0-9]*) : ;;
    *) reexec_argv+=("SPIKE_PARENT_DEADLINE=${SPIKE_PARENT_DEADLINE}") ;;
  esac
  if [ -n "${SPIKE_RID_OVERRIDE:-}" ]; then reexec_argv+=("SPIKE_RID_OVERRIDE=${SPIKE_RID_OVERRIDE}"); fi
  exec "${reexec_argv[@]}" bash -p -- "$0" "$@"
fi
for reexec_var in $(compgen -e); do
  case "$reexec_var" in
    HOME|PATH|OLDPWD|PWD|SHLVL|_|SPIKE_REEXEC_SENTINEL|SPIKE_PARENT_DEADLINE|SPIKE_RID_OVERRIDE) : ;;
    SPIKE_CANONICAL|SPIKE_PID_FILE|SPIKE_DEADLINE) : ;;  # set by our own watchdog->inner dispatch
    *)
      echo "run-spike: refusing — inherited variable '$reexec_var' present at canonical entry; the clean-environment contract strips toolchain-steering inheritance like DOTNET_ROOT via env -i (use: env -i HOME=\"\$HOME\" bash -p scripts/run-spike.sh) (MA-HI-1/TB-004)" >&2
      exit 30 ;;
  esac
done

set -euo pipefail
PATH="/usr/bin:/bin"
export PATH

SCRIPT_SELF="${BASH_SOURCE[0]}"
SCRIPT_DIR="$(cd -- "${SCRIPT_SELF%/*}" && pwd)"
# Canonicalize SCRIPT_SELF to an ABSOLUTE path. It is re-passed to the inner
# controller relaunch (`setsid bash -p -- "$SCRIPT_SELF"`, below) AFTER the
# controller cd's into SPIKE_ROOT; a relative BASH_SOURCE — e.g. the documented
# root-README invocation `bash -p spikes/dafny-compat/scripts/run-spike.sh` from
# the repo root — would no longer resolve post-cd, so the inner would fail to
# launch (exit 127). SCRIPT_DIR is already absolute (cd+pwd), so join the basename.
SCRIPT_SELF="$SCRIPT_DIR/${SCRIPT_SELF##*/}"
SPIKE_ROOT="$(cd -- "$SCRIPT_DIR/.." && pwd)"
REPO_ROOT="$(cd -- "$SPIKE_ROOT/../.." && pwd)"
CACHE_DIR="$SPIKE_ROOT/out/cache"

# MA-UX-3: an EXPLICIT `--dotnet-root <path>` argument re-establishes DOTNET_ROOT
# for SDK resolution. The clean-environment contract (MA-HI-1) strips an AMBIENT
# DOTNET_ROOT env var to close the toolchain-hijack vector, so a user whose SDK
# lives outside HOME/.dotnet (e.g. a system-wide install) passes it as an
# argument — explicit intent that cannot be ambiently inherited — rather than as
# an env var that the re-exec would strip. Pre-scanned here (read-only) so it is
# honored BEFORE resolve_sdk runs; the main arg parser consumes it below.
prev_arg=""
for scan_arg in "$@"; do
  # set (do NOT export) so resolve_sdk in THIS shell honors it while the value
  # never leaks into the inner dispatch (which would then trip the MA-HI-1
  # clean-environment assertion); the inner resolves via the cached pointer.
  if [ "$prev_arg" = "--dotnet-root" ]; then DOTNET_ROOT="$scan_arg"; fi
  prev_arg="$scan_arg"
done

# --- .NET SDK resolution (stated rule): DOTNET_ROOT (env or --dotnet-root),
# --- then the HOME-local install, then the pointer a previous controller run
# --- cached under out/.
SPIKE_DOTNET_BIN=""
SPIKE_DOTNET_ROOT=""
resolve_sdk() {
  local candidate cached=""
  if [ -f "$CACHE_DIR/dotnet-root" ]; then
    read -r cached < "$CACHE_DIR/dotnet-root" || true
  fi
  for candidate in "${DOTNET_ROOT:-/nonexistent}/dotnet" "${HOME:-/nonexistent}/.dotnet/dotnet" "${cached:-/nonexistent}/dotnet"; do
    if [ -x "$candidate" ]; then
      SPIKE_DOTNET_BIN="$candidate"
      break
    fi
  done
  if [ -z "$SPIKE_DOTNET_BIN" ]; then
    echo "run-spike: no pinned .NET SDK found (checked DOTNET_ROOT, HOME/.dotnet, out/cache/dotnet-root) — install SDK 10.0.302 per README.md. System-wide installs (/usr/share/dotnet, PATH) are NOT auto-discovered; the clean-environment contract strips an ambient DOTNET_ROOT (MA-HI-1), so name it EXPLICITLY: env -i HOME=\"\$HOME\" bash -p scripts/run-spike.sh --dotnet-root <sdk-root> (MA-UX-3)" >&2
    exit 20
  fi
  SPIKE_DOTNET_ROOT="${SPIKE_DOTNET_BIN%/*}"
  if [ ! -f "$CACHE_DIR/dotnet-root" ]; then
    run_cmd mkdir -p -- "$CACHE_DIR"
    printf '%s\n' "$SPIKE_DOTNET_ROOT" > "$CACHE_DIR/dotnet-root.tmp"
    run_cmd mv -- "$CACHE_DIR/dotnet-root.tmp" "$CACHE_DIR/dotnet-root"
  fi
}
resolve_sdk

# MA-HI-3: escape JSON control characters, not only \ and ". Path-input- and
# tool-output-derived values (a --run-root with a raw newline, a hijacked
# `dotnet --version`) would otherwise emit invalid JSON the aggregator cannot
# parse. Backslash first, then quote, then the C0 controls that realistically
# appear (newline/CR/tab/backspace/formfeed); rarer C0 bytes are left raw.
json_escape() {
  local s="${1//\\/\\\\}"
  s="${s//\"/\\\"}"
  s="${s//$'\t'/\\t}"
  s="${s//$'\r'/\\r}"
  s="${s//$'\n'/\\n}"
  s="${s//$'\b'/\\b}"
  s="${s//$'\f'/\\f}"
  printf '%s' "$s"
}

sha_of() {
  local file="$1" digest rest
  read -r digest rest < <(run_cmd sha256sum -- "$file")
  printf '%s' "$digest"
}

# QA-014(2)/QA-007 shared helper: find the session-leader descendant of a pid
# (the phase's pgid) by walking /proc with bash builtins only.
find_session_leader() { # ancestor-pid -> prints leader pid or nothing
  local ancestor="$1" statfile content rest pid ppid
  local -A parent=()
  for statfile in /proc/[0-9]*/stat; do
    [ -r "$statfile" ] || continue
    content="$(< "$statfile")" 2>/dev/null || continue
    pid="${content%% *}"
    rest="${content##*) }"
    read -r _ ppid _ _ <<< "$rest"
    parent[$pid]="$ppid"
  done
  local candidate cur depth
  for candidate in "${!parent[@]}"; do
    cur="$candidate"
    depth=0
    while [ -n "${parent[$cur]:-}" ] && [ "$depth" -lt 32 ]; do
      if [ "${parent[$cur]}" = "$ancestor" ] || [ "$cur" = "$ancestor" ]; then
        # is the candidate a session leader? (sid == pid, field 6 of stat)
        if [ -r "/proc/$candidate/stat" ]; then
          content="$(< "/proc/$candidate/stat")" 2>/dev/null || break
          rest="${content##*) }"
          read -r _ _ _ sid _ <<< "$rest"
          # rest fields: state ppid pgrp session ...
          read -r _ _ pgrp sess _ <<< "$rest"
          if [ "$sess" = "$candidate" ]; then
            printf '%s' "$candidate"
            return 0
          fi
        fi
        break
      fi
      cur="${parent[$cur]}"
      depth=$((depth + 1))
    done
  done
  return 1
}

# MA-UX-1/MA-RB-4: enumerate EVERY session leader descended from an ancestor
# (the inner controller session PLUS each per-phase setsid session). On a
# cancellation signal a plain kill of the inner controller's own group would
# orphan the active phase's detached setsid session; sweeping all descendant
# sessions while they are still parented under the wrapper closes that gap.
list_descendant_sessions() { # ancestor-pid -> prints unique session-leader pids
  local ancestor="$1" statfile content rest pid ppid sess
  local -A parent=() sid=()
  for statfile in /proc/[0-9]*/stat; do
    [ -r "$statfile" ] || continue
    content="$(< "$statfile")" 2>/dev/null || continue
    pid="${content%% *}"
    rest="${content##*) }"
    read -r _ ppid _ sess _ <<< "$rest"
    parent[$pid]="$ppid"
    sid[$pid]="$sess"
  done
  local candidate cur depth
  local -A emitted=()
  for candidate in "${!parent[@]}"; do
    cur="$candidate"
    depth=0
    while [ -n "${parent[$cur]:-}" ] && [ "$depth" -lt 64 ]; do
      if [ "$cur" = "$ancestor" ] || [ "${parent[$cur]}" = "$ancestor" ]; then
        local leader="${sid[$candidate]:-}"
        if [ -n "$leader" ] && [ -z "${emitted[$leader]:-}" ]; then
          emitted[$leader]=1
          printf '%s\n' "$leader"
        fi
        break
      fi
      cur="${parent[$cur]}"
      depth=$((depth + 1))
    done
  done
}

# MA-RB-R2-1..4 (concurrency backbone): ONE coarse out/-level advisory lock,
# shared by every destructive/publishing operation over the shared out/
# substrate — the start-of-run prune (prune_run_roots) AND the shared-package
# cache seed publish (publish_cache_staged). Two concurrent canonical runs are
# an anticipated state (the cache is hardened for it); without a lock, run B's
# start-prune can rm -rf run A's live root, and two cold seeders can nest the
# cache. Mechanism: an atomic `mkdir` lock dir (allowlist-safe — no flock/fcntl),
# holder pid recorded so a crashed/killed holder's lock is reclaimed (stale
# detection via `kill -0`). acquire returns nonzero on timeout (caller SKIPS the
# operation — never blocks a run indefinitely).
OUT_LOCK_DIR="$SPIKE_ROOT/out/.lock"
acquire_out_lock() { # timeout-seconds -> 0 acquired, 1 contended/timed-out
  local timeout="${1:-30}" waited=0 holder=""
  run_cmd mkdir -p -- "$SPIKE_ROOT/out"
  while :; do
    if run_cmd mkdir -- "$OUT_LOCK_DIR" 2>/dev/null; then
      printf '%s\n' "$$" > "$OUT_LOCK_DIR/pid"
      return 0
    fi
    holder=""
    [ -f "$OUT_LOCK_DIR/pid" ] && read -r holder < "$OUT_LOCK_DIR/pid" 2>/dev/null || true
    # Stale-holder reclaim: a crashed/killed holder cannot hold the lock forever.
    if [ -n "$holder" ] && ! kill -0 "$holder" 2>/dev/null; then
      run_cmd rm -rf -- "$OUT_LOCK_DIR" 2>/dev/null || true
      continue
    fi
    if [ "$waited" -ge "$timeout" ]; then
      return 1
    fi
    run_cmd sleep 1
    waited=$((waited + 1))
  done
}
release_out_lock() {
  local holder=""
  [ -f "$OUT_LOCK_DIR/pid" ] && read -r holder < "$OUT_LOCK_DIR/pid" 2>/dev/null || true
  if [ -z "$holder" ] || [ "$holder" = "$$" ]; then
    run_cmd rm -rf -- "$OUT_LOCK_DIR" 2>/dev/null || true
  fi
}

# MA-RB-R2-4: resolve the run root the out/current pointer TARGETS (not just the
# out/current directory name) so a keep-N sweep never dangles the dev-loop
# pointer. Reads out/current/spike.runsettings's SPIKE_RUN_CONTEXT
# (<run-root>/run-context.json) and strips the file name to the run root.
resolve_out_current_referent() { # out-dir -> prints protected run-root path or nothing
  local out_dir="$1" rs="$1/current/spike.runsettings" line ctx
  [ -f "$rs" ] || return 0
  while IFS= read -r line; do
    case "$line" in
      *'<SPIKE_RUN_CONTEXT>'*'</SPIKE_RUN_CONTEXT>'*)
        ctx="${line#*<SPIKE_RUN_CONTEXT>}"
        ctx="${ctx%%</SPIKE_RUN_CONTEXT>*}"
        printf '%s' "${ctx%/run-context.json}"
        return 0 ;;
    esac
  done < "$rs"
  return 0
}

# MA-UX-2/MA-RB-1 + MA-RB-R2-1/R2-4 + MA-UC-R2-1: keep-last-N prune of
# out/runid-* (old canonical run roots, ~1GB each). NEVER prunes: the explicit
# live run root (protect), the out/current pointer's TARGET (referent), or any
# root whose .inner-pid names a LIVE controller (a concurrent canonical run).
# Emits one stderr line naming the count removed + keep-count (a silent
# destructive default is invisible on a first-post-upgrade mass deletion).
# Newest-N kept by mtime comparison (bash -nt; no ls/stat/find — not on the
# layer-1 allowlist). Callers wrap this in acquire_out_lock so two concurrent
# prunes never race over the shared substrate.
prune_run_roots() { # out-dir keep-count [keep-this-root]
  local out_dir="$1" keep="$2" protect="${3:-}" d other newer dirs=() referent holder removed=0
  referent="$(resolve_out_current_referent "$out_dir")"
  for d in "$out_dir"/runid-*; do
    [ -d "$d" ] || continue
    # NEVER prune the live run root (defensive: it is the newest so keep-N would
    # retain it anyway, but an explicit skip removes any mtime-ordering risk).
    [ -n "$protect" ] && [ "$d" = "$protect" ] && continue
    # MA-RB-R2-4: NEVER prune the out/current pointer's target.
    [ -n "$referent" ] && [ "$d" = "$referent" ] && continue
    # MA-RB-R2-1: NEVER prune a LIVE run's root — its .inner-pid names a running
    # controller (a concurrent canonical run mid-flight). A concurrent run B's
    # start-prune must not rm -rf run A's live root out from under it.
    if [ -f "$d/.inner-pid" ]; then
      holder=""
      read -r holder < "$d/.inner-pid" 2>/dev/null || true
      if [ -n "$holder" ] && kill -0 "$holder" 2>/dev/null; then continue; fi
    fi
    dirs+=("$d")
  done
  for d in "${dirs[@]}"; do
    newer=0
    for other in "${dirs[@]}"; do
      [ "$other" = "$d" ] && continue
      [ "$other" -nt "$d" ] && newer=$((newer + 1))
    done
    if [ "$newer" -ge "$keep" ]; then
      run_cmd rm -rf -- "$d"
      removed=$((removed + 1))
    fi
  done
  # MA-UC-R2-1: announce the destructive prune so a first-post-upgrade mass
  # deletion (~100 roots) is visible at runtime (mirrors clean-runs.sh).
  if [ "$removed" -gt 0 ]; then
    echo "run-spike: pruned $removed old run root(s) under $out_dir, keeping the newest $keep (never out/current's target, out/cache, or a live run)" >&2
  fi
}

# MA-UX-5/MA-RB-2 + MA-ID-R2-1: the shared package cache is TRUSTED only when its
# completion marker AND package dir are both present. A torn seed (deadline KILL
# / ENOSPC / SIGKILL mid-populate) leaves a marker-less dir this predicate
# rejects, so it is ignored (the run restores fresh) instead of being copied
# into every subsequent run root forever. Sole guard for both the production
# consume path and the cache-consume test phase (no drift).
shared_cache_is_trusted() { # cache-dir
  [ -f "$1/packages.complete" ] && [ -d "$1/packages" ]
}

# MA-RB-R2-3: publish a staged package cache into place ATOMICALLY and without
# nesting. The whole publish is serialized under the out/-level lock, re-checks
# the marker after acquiring (another cold seeder may have won the race while we
# staged), rotates any existing dir to a PID-UNIQUE .old (never a shared name),
# and swaps with `mv -T`/--no-target-directory so a stray dir can never be moved
# INSIDE an existing packages/ (the doubled/nested-cache-marked-complete state).
# The completion marker is written LAST. Lock contention -> drop our staged copy
# (the winning run publishes the cache).
publish_cache_staged() { # cache-dir staged-dir
  local cache_dir="$1" staged="$2"
  local pkgs="$cache_dir/packages" marker="$cache_dir/packages.complete" old="$cache_dir/packages.$$.old"
  if acquire_out_lock 30; then
    if [ -f "$marker" ]; then
      run_cmd rm -rf -- "$staged"   # another cold seeder already published — one clean copy
    else
      run_cmd rm -rf -- "$old"
      [ -d "$pkgs" ] && run_cmd mv -- "$pkgs" "$old"
      run_cmd mv -T -- "$staged" "$pkgs"
      printf 'seeded\n' > "$marker.tmp"
      run_cmd mv -- "$marker.tmp" "$marker"
      run_cmd rm -rf -- "$old"
    fi
    release_out_lock
  else
    run_cmd rm -rf -- "$staged"
  fi
}

# MA-RB-R2-2: reap leaked cache staging at controller start. A deadline-KILL or
# SIGKILL mid-seed leaks packages.staged.<pid> (hundreds of MB) in the
# never-pruned out/cache; nothing else globs it. Reap staged/.old dirs whose
# pid-suffix names a DEAD process (a LIVE pid is a concurrent seeder mid-flight
# — never clobbered) plus any legacy bare packages.old from the pre-R2-3 impl.
reap_cache_staging() { # cache-dir
  local d holder
  [ -d "$1" ] || return 0
  for d in "$1"/packages.staged.* "$1"/packages.*.old; do
    [ -e "$d" ] || continue
    case "$d" in
      *.old) holder="${d##*/packages.}"; holder="${holder%.old}" ;;
      *)     holder="${d##*.}" ;;
    esac
    case "$holder" in
      ''|*[!0-9]*) run_cmd rm -rf -- "$d" 2>/dev/null || true; continue ;;
    esac
    if ! kill -0 "$holder" 2>/dev/null; then
      run_cmd rm -rf -- "$d" 2>/dev/null || true
    fi
  done
  [ -d "$1/packages.old" ] && run_cmd rm -rf -- "$1/packages.old" 2>/dev/null || true
}


# PR-005: resolve a duplicate JSON key to its LAST occurrence (JSON last-wins),
# never the first — a hostile duplicate can no longer silently steer the
# effective value to the attacker's first entry. Scans the whole file and keeps
# the last numeric value seen for the key.
config_int() { # file key default
  local line value result="$3" found=""
  while IFS= read -r line; do
    case "$line" in
      *"\"$2\""*)
        value="${line##*: }"
        value="${value%%,*}"
        case "$value" in
          ''|*[!0-9]*) : ;;
          *) result="$value"; found=1 ;;
        esac ;;
    esac
  done < "$1"
  printf '%s' "$result"
  [ -n "$found" ] || return 0
}

# --- argument parsing --------------------------------------------------------
PHASE=""
PROJECT=""
CONSUMER_FIXTURE=""
SCRATCH=""
CONFIG_FILE="$SPIKE_ROOT/config/run-config.json"
SOLVER_OVERRIDE=""
RUN_ROOT=""
OUT_PATH=""
KEEP_ARG=""
OUT_DIR_ARG=""
INNER=0
CANONICAL=1
SPIKE_RUN_ID="${SPIKE_RUN_ID:-}"
SPIKE_NONCE="${SPIKE_NONCE:-}"
while [ $# -gt 0 ]; do
  case "$1" in
    __inner) INNER=1; shift ;;
    --phase) PHASE="$2"; shift 2 ;;
    --project) PROJECT="$2"; shift 2 ;;
    --consumer-fixture) CONSUMER_FIXTURE="$2"; shift 2 ;;
    --scratch) SCRATCH="$2"; shift 2 ;;
    --config) CONFIG_FILE="$2"; CANONICAL=0; shift 2 ;;
    --solver) SOLVER_OVERRIDE="$2"; CANONICAL=0; shift 2 ;;
    --run-root) RUN_ROOT="$2"; CANONICAL=0; shift 2 ;;
    --out) OUT_PATH="$2"; CANONICAL=0; shift 2 ;;
    --run-id) SPIKE_RUN_ID="$2"; shift 2 ;;
    --nonce) SPIKE_NONCE="$2"; shift 2 ;;
    --keep) KEEP_ARG="$2"; shift 2 ;;         # MA-ID-R2-3: prune-run-roots test phase only
    --out-dir) OUT_DIR_ARG="$2"; shift 2 ;;   # MA-RB-R2-*: prune/cache test phases only (isolated substrate)
    --dotnet-root) shift 2 ;;  # MA-UX-3: pre-scanned above for resolve_sdk; NOT a variance override
    *) echo "run-spike: unknown argument '$1'" >&2; exit 20 ;;
  esac
done
if [ -n "$SOLVER_OVERRIDE" ]; then
  SOLVER_DIR="${SOLVER_OVERRIDE%/*}"
  SOLVER_BASE="${SOLVER_OVERRIDE##*/}"
  SOLVER_OVERRIDE="$(cd -- "${SOLVER_DIR:-/}" && pwd)/$SOLVER_BASE"
fi

cd -- "$SPIKE_ROOT"

# --- generic operator phases (test-visible surface; faults are constructed BY
# --- tests in test-owned scratch roots, never by SUT flags) -------------------

# MA-ID-R2-3/MA-RB-R2-1/MA-RB-R2-4/MA-UC-R2-1: drive the REAL prune_run_roots
# (the canonical-start auto-prune, NOT a re-implementation) against an isolated
# --out-dir with an explicit --keep, so keep-N boundedness + live-PID +
# out/current-referent protection + the announcement are exercised
# behaviorally, not source-scanned.
if [ "$PHASE" = "prune-run-roots" ]; then
  local_out="${OUT_DIR_ARG:-$SPIKE_ROOT/out}"
  keep_n="${KEEP_ARG:-5}"
  case "$keep_n" in ''|*[!0-9]*) echo "run-spike: --keep must be a non-negative integer (prune-run-roots)" >&2; exit 20 ;; esac
  if [ "$keep_n" -eq 0 ]; then echo "run-spike: --keep 0 refused — prune must retain at least one root (MA-RB-R2-4)" >&2; exit 20; fi
  if acquire_out_lock 30; then
    prune_run_roots "$local_out" "$keep_n" ""
    release_out_lock
  else
    echo "run-spike: prune skipped (out/ lock held by a concurrent run)" >&2
  fi
  exit 0
fi

# MA-RB-R2-3/MA-RB-R2-2: drive the REAL publish_cache_staged against an isolated
# --out-dir cache dir with a --project source tree, so the lock + marker
# re-check + mv -T no-nesting swap are exercised behaviorally.
if [ "$PHASE" = "cache-publish" ]; then
  if [ -z "$OUT_DIR_ARG" ] || [ -z "$PROJECT" ]; then
    echo "run-spike: --phase cache-publish requires --out-dir and --project (source tree)" >&2; exit 20
  fi
  CP_STAGED="$OUT_DIR_ARG/packages.staged.$$"
  run_cmd mkdir -p -- "$OUT_DIR_ARG" "$CP_STAGED"
  run_cmd bash -p -c 'set -euo pipefail; tar -C "$1" -cf - . | tar -C "$2" -xf -' _ "$PROJECT" "$CP_STAGED"
  publish_cache_staged "$OUT_DIR_ARG" "$CP_STAGED"
  exit 0
fi

# MA-ID-R2-1: drive the REAL shared_cache_is_trusted consume guard against an
# isolated --out-dir cache dir, so a marker-less/torn cache is behaviorally
# proven to be IGNORED (never consumed) and a marker+dir cache is trusted.
if [ "$PHASE" = "cache-consume" ]; then
  if [ -z "$OUT_DIR_ARG" ]; then echo "run-spike: --phase cache-consume requires --out-dir" >&2; exit 20; fi
  if shared_cache_is_trusted "$OUT_DIR_ARG"; then
    echo "cache-consume: trusted (marker present) — would copy into the run root" >&2
  else
    echo "cache-consume: ignored (no completion marker / no package dir) — restoring fresh (MA-UX-5/AP-016)" >&2
  fi
  exit 0
fi

# MA-RB-R2-2: drive the REAL reap_cache_staging against an isolated --out-dir
# cache dir, so a leaked staged dir with a DEAD pid is reaped while a LIVE
# seeder's staged dir is preserved.
if [ "$PHASE" = "reap-cache-staging" ]; then
  if [ -z "$OUT_DIR_ARG" ]; then echo "run-spike: --phase reap-cache-staging requires --out-dir" >&2; exit 20; fi
  reap_cache_staging "$OUT_DIR_ARG"
  exit 0
fi

if [ "$PHASE" = "restore" ]; then
  if [ -z "$PROJECT" ]; then echo "run-spike: --phase restore requires --project" >&2; exit 20; fi
  PROJ_DIR="$(cd -- "${PROJECT%/*}" && pwd)"
  PROJ_FILE="${PROJECT##*/}"
  CFG_DIR="$PROJ_DIR"
  while [ "$CFG_DIR" != "" ] && [ ! -f "$CFG_DIR/NuGet.Config" ]; do CFG_DIR="${CFG_DIR%/*}"; done
  if [ ! -f "$CFG_DIR/NuGet.Config" ]; then echo "run-spike: no NuGet.Config above the project (INV-001)" >&2; exit 20; fi
  export NUGET_PACKAGES="$CACHE_DIR/packages"
  export DOTNET_CLI_TELEMETRY_OPTOUT=1 MSBUILDDISABLENODEREUSE=1 UseSharedCompilation=false
  export DOTNET_ROOT="$SPIKE_DOTNET_ROOT" DOTNET_HOST_PATH="$SPIKE_DOTNET_BIN"
  run_cmd mkdir -p -- "$NUGET_PACKAGES"
  cd -- "$PROJ_DIR"
  # --force defeats NuGet's no-op up-to-date shortcut: scratch copies carry a
  # prior assets file, and the behavioral stale-lock negative test needs the
  # locked-mode consistency check to actually re-evaluate (RS-014a).
  run_cmd dotnet restore "$PROJ_FILE" --force --configfile "$CFG_DIR/NuGet.Config" -noAutoResponse
  exit $?
fi

if [ "$PHASE" = "preprocess-audit" ]; then
  export DOTNET_CLI_TELEMETRY_OPTOUT=1 MSBUILDDISABLENODEREUSE=1 UseSharedCompilation=false
  export DOTNET_ROOT="$SPIKE_DOTNET_ROOT" DOTNET_HOST_PATH="$SPIKE_DOTNET_BIN"
  run_cmd mkdir -p -- "$SPIKE_ROOT/out"
  PP_FILE="$(run_cmd mktemp "$SPIKE_ROOT/out/preprocess.XXXXXX.xml")"
  # MA-RB-5: the ~1-3MB MSBuild dump must not leak under out/ (missed by both the
  # RUN_ROOT junk glob and the test-scratch sweep) — trap EXIT so refusal paths
  # clean up too.
  trap 'run_cmd rm -f "$PP_FILE" 2>/dev/null || true' EXIT
  run_cmd dotnet msbuild "$SPIKE_ROOT/harness/RouteAHarness/RouteAHarness.csproj" -noAutoResponse -preprocess:"$PP_FILE" >&2
  while IFS= read -r line; do
    line="${line%$'\r'}"
    case "$line" in
      /*.props|/*.targets|/*.rsp) printf 'IMPORT %s\n' "$line" ;;
    esac
  done < "$PP_FILE"
  exit 0
fi

if [ "$PHASE" = "negative-compile" ]; then
  if [ -z "$CONSUMER_FIXTURE" ] || [ -z "$SCRATCH" ]; then
    echo "run-spike: --phase negative-compile requires --consumer-fixture and --scratch" >&2; exit 20
  fi
  FIXTURE_ABS="$SPIKE_ROOT/$CONSUMER_FIXTURE"
  if [ ! -f "$FIXTURE_ABS" ]; then FIXTURE_ABS="$CONSUMER_FIXTURE"; fi
  FIXTURE_BASE="${FIXTURE_ABS##*/}"
  CS_NAME="${FIXTURE_BASE%.txt}"
  PROJ_DIR="$SCRATCH/negcompile"
  run_cmd mkdir -p -- "$PROJ_DIR"
  printf '%s\n' "$(< "$FIXTURE_ABS")" > "$PROJ_DIR/$CS_NAME"
  {
    printf '<Project Sdk="Microsoft.NET.Sdk">\n'
    printf '  <PropertyGroup>\n'
    printf '    <TargetFramework>net10.0</TargetFramework>\n'
    printf '    <Nullable>disable</Nullable>\n'
    printf '    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>\n'
    printf '  </PropertyGroup>\n'
    printf '  <ItemGroup><Compile Include="%s" /></ItemGroup>\n' "$CS_NAME"
    printf '  <ItemGroup><ProjectReference Include="%s/adapters/SpikeDafnyAdapter.RouteA/SpikeDafnyAdapter.RouteA.csproj" /></ItemGroup>\n' "$SPIKE_ROOT"
    printf '</Project>\n'
  } > "$PROJ_DIR/NegativeCompileConsumer.csproj"
  export NUGET_PACKAGES="$CACHE_DIR/packages"
  export DOTNET_CLI_TELEMETRY_OPTOUT=1 MSBUILDDISABLENODEREUSE=1 UseSharedCompilation=false
  export DOTNET_ROOT="$SPIKE_DOTNET_ROOT" DOTNET_HOST_PATH="$SPIKE_DOTNET_BIN"
  run_cmd mkdir -p -- "$NUGET_PACKAGES"
  run_cmd dotnet restore "$PROJ_DIR/NegativeCompileConsumer.csproj" --configfile "$SPIKE_ROOT/NuGet.Config" -noAutoResponse >&2
  run_cmd dotnet build "$PROJ_DIR/NegativeCompileConsumer.csproj" --no-restore -noAutoResponse
  exit $?
fi

# --- full pipeline -----------------------------------------------------------
# PR-004: structural validation of the committed run-config BEFORE the pipeline
# runs, so a corrupt/truncated config gets a fast typed refusal instead of
# config_int silently falling open to the 1800s default and failing slow via the
# end-of-run suite (>9 min). No jq/python (deny-set); a lightweight structural
# check: present, a JSON object (starts { / ends }, balanced braces), and a
# numeric wall_clock_bound_seconds key actually present.
validate_run_config() { # file
  local f="$1" content trimmed opens closes
  if [ ! -f "$f" ]; then
    echo "run-spike: run-config missing at $f — a committed run config is required (PR-004)" >&2
    exit 20
  fi
  content="$(< "$f")"
  trimmed="${content#"${content%%[![:space:]]*}"}"      # ltrim
  trimmed="${trimmed%"${trimmed##*[![:space:]]}"}"       # rtrim
  case "$trimmed" in
    '{'*'}') : ;;
    *) echo "run-spike: run-config at $f is not a JSON object (truncated/corrupt) — fast typed refusal (PR-004)" >&2; exit 20 ;;
  esac
  opens="${content//[!\{]/}"; closes="${content//[!\}]/}"
  if [ "${#opens}" != "${#closes}" ]; then
    echo "run-spike: run-config at $f has unbalanced braces (truncated/corrupt) — fast typed refusal (PR-004)" >&2
    exit 20
  fi
  case "$content" in
    *'"wall_clock_bound_seconds"'*) : ;;
    *) echo "run-spike: run-config at $f is missing wall_clock_bound_seconds — refusing to fall open to the default (PR-004)" >&2; exit 20 ;;
  esac
  # the value must be numeric on its own line (config_int would otherwise skip it silently)
  local line hit=""
  while IFS= read -r line; do
    case "$line" in
      *'"wall_clock_bound_seconds"'*:*)
        local v="${line##*: }"; v="${v%%,*}"
        case "$v" in ''|*[!0-9]*) : ;; *) hit=1 ;; esac ;;
    esac
  done < "$f"
  if [ -z "$hit" ]; then
    echo "run-spike: run-config wall_clock_bound_seconds is non-numeric or malformed (corrupt) — fast typed refusal (PR-004)" >&2
    exit 20
  fi
}
validate_run_config "$CONFIG_FILE"
WALL_BOUND="$(config_int "$CONFIG_FILE" wall_clock_bound_seconds 1800)"
SCHEMA_SHA="$(sha_of "$SPIKE_ROOT/schema/evidence-schema.json")"
MANIFEST_SHA="$(sha_of "$SPIKE_ROOT/manifest/probe-manifest.json")"
# MA-XC-7: the synthetic write_failed_report must not carry a hardcoded schema
# version that a future schema bump would leave stale beside a fresh digest.
# Read the version from the schema file (already read for its digest above).
EVIDENCE_SCHEMA_VERSION=2
while IFS= read -r line; do
  case "$line" in
    *'"evidence_schema_version"'*:\ [0-9]*)
      value="${line##*: }"; value="${value%%,*}"
      case "$value" in ''|*[!0-9]*) : ;; *) EVIDENCE_SCHEMA_VERSION="$value"; break ;; esac ;;
  esac
done < "$SPIKE_ROOT/schema/evidence-schema.json"
SDK_PIN="unknown"
while IFS= read -r line; do
  case "$line" in
    *'"version"'*) SDK_PIN="${line##*: \"}"; SDK_PIN="${SDK_PIN%\"*}"; break ;;
  esac
done < "$SPIKE_ROOT/global.json"
GIT_COMMIT="${GIT_COMMIT:-unknown}"
GIT_DIRTY="${GIT_DIRTY:-false}"
RUN_DIR_REL=""

write_failed_report() { # path cause detail-prefix detail-suffix
  local target="$1" cause="$2" detail="$3" sigtext="$4"
  local target_dir="${target%/*}"
  if [ "$target_dir" != "$target" ]; then run_cmd mkdir -p -- "$target_dir"; fi
  {
    printf '{\n'
    printf '  "evidence_schema_version": %s,\n' "$EVIDENCE_SCHEMA_VERSION"
    printf '  "evidence_schema_sha256": "%s",\n' "$SCHEMA_SHA"
    printf '  "probe_manifest_sha256": "%s",\n' "$MANIFEST_SHA"
    printf '  "run_id": "%s",\n' "$SPIKE_RUN_ID"
    printf '  "kind": "run-report",\n'
    printf '  "binding_identity": { "run_directory": "%s", "git_commit_id": "%s", "git_dirty_flag": %s, "host_rid": "linux-x64", "sdk_version": "%s", "actual_runtime_version": "unknown" },\n' \
      "$(json_escape "$RUN_DIR_REL")" "$(json_escape "$GIT_COMMIT")" "$GIT_DIRTY" "$(json_escape "$SDK_PIN")"
    printf '  "deterministic": {\n'
    printf '    "route_verdicts": [\n'
    printf '      { "route": "A", "state": "INCOMPLETE", "verdict_reason": { "variant": "prerequisite-failure", "cause": "%s", "detail": "%s%s" } },\n' "$(json_escape "$cause")" "$(json_escape "$detail")" "$(json_escape "$sigtext")"
    printf '      { "route": "B", "state": "INCOMPLETE", "verdict_reason": { "variant": "prerequisite-failure", "cause": "%s", "detail": "%s%s" } }\n' "$(json_escape "$cause")" "$(json_escape "$detail")" "$(json_escape "$sigtext")"
    printf '    ],\n'
    printf '    "per_probe_results": [],\n'
    printf '    "final_suite_status": "unknown"\n'
    printf '  },\n'
    printf '  "volatile": {}\n'
    printf '}\n'
  } > "$target.tmp"
  run_cmd mv -- "$target.tmp" "$target"
}

if [ "$INNER" = 0 ]; then
  # OUTER WATCHDOG — the parent of the (inner) controller, not a test executed
  # by it (RS-015). Mints run_id + run root, then supervises the setsid'd inner
  # controller session against ONE absolute /proc/uptime deadline (bash SECONDS
  # is system-clock-derived and not monotonic — codex R4-05).
  SPIKE_RUN_ID="runid-$(printf '%08x%08x' "$SRANDOM" "$SRANDOM")"
  SPIKE_NONCE="nonce-$(printf '%08x%08x' "$SRANDOM" "$SRANDOM")"
  if [ -z "$RUN_ROOT" ]; then
    RUN_ROOT="$SPIKE_ROOT/out/$SPIKE_RUN_ID"
  fi
  run_cmd mkdir -p -- "$RUN_ROOT"
  RUN_ROOT="$(cd -- "$RUN_ROOT" && pwd)"
  case "$RUN_ROOT" in
    "$SPIKE_ROOT"/*) RUN_DIR_REL="${RUN_ROOT#"$SPIKE_ROOT"/}" ;;
    *) RUN_DIR_REL="$RUN_ROOT" ;;
  esac
  if [ -z "$OUT_PATH" ]; then
    OUT_PATH="$RUN_ROOT/reports/run-report.json"
  fi

  # MA-UX-2/MA-RB-1: bound out/ growth — a canonical operator run prunes old
  # runid-* roots (keeping the newest few, this fresh one included) so a dev
  # tree cannot silently fill to ENOSPC across many runs. Variance/nested runs
  # never prune (they run inside the suite and must not delete a sibling's
  # root); scripts/clean-runs.sh is the explicit operator tool.
  if [ "$CANONICAL" = 1 ]; then
    # MA-RB-R2-1: prune under the coarse out/-level lock so a concurrent
    # canonical run's start-prune cannot race this one over the shared
    # substrate; live roots + the out/current referent are additionally skipped.
    if acquire_out_lock 30; then
      prune_run_roots "$SPIKE_ROOT/out" 5 "$RUN_ROOT"
      release_out_lock
    else
      echo "run-spike: start-of-run prune skipped (out/ lock held by a concurrent run) — clean-runs.sh reclaims later" >&2
    fi
  fi

  read -r UPTIME_START _ < /proc/uptime
  UPTIME_START="${UPTIME_START%.*}"
  DEADLINE=$((UPTIME_START + WALL_BOUND))
  # QA-018(b): nested controller runs inherit the PARENT's remaining budget —
  # SPIKE_PARENT_DEADLINE (an absolute /proc/uptime deadline, injected into the
  # suite-phase environment and propagated by the test launcher) caps this
  # run's deadline to min(own bound, parent remaining), so a killed parent can
  # never leave nested inner controllers running on fresh 30-minute deadlines.
  case "${SPIKE_PARENT_DEADLINE:-}" in
    ''|*[!0-9]*) : ;;
    *) if [ "$SPIKE_PARENT_DEADLINE" -lt "$DEADLINE" ]; then DEADLINE="$SPIKE_PARENT_DEADLINE"; fi ;;
  esac
  # QA-007: the inner controller's per-phase supervisor owns the exact deadline
  # and writes a phase-specific synthetic report (e.g. "build phase exceeded").
  # The OUTER watchdog is the BACKSTOP for a wedged inner that cannot
  # self-supervise, so it waits a grace window PAST the deadline before acting
  # — letting the inner's per-phase kill + report win the normal case.
  OUTER_GRACE=20
  OUTER_DEADLINE=$((DEADLINE + OUTER_GRACE))

  PID_FILE="$RUN_ROOT/.inner-pid"
  INNER_ARGS=(__inner --config "$CONFIG_FILE" --run-root "$RUN_ROOT" --out "$OUT_PATH" --run-id "$SPIKE_RUN_ID" --nonce "$SPIKE_NONCE")
  if [ -n "$SOLVER_OVERRIDE" ]; then INNER_ARGS+=(--solver "$SOLVER_OVERRIDE"); fi
  if [ "$CANONICAL" = 1 ]; then export SPIKE_CANONICAL=1; else export SPIKE_CANONICAL=0; fi
  export SPIKE_PID_FILE="$PID_FILE"
  export SPIKE_DEADLINE="$DEADLINE"
  run_cmd setsid -w bash -p -- "$SCRIPT_SELF" "${INNER_ARGS[@]}" &
  WRAPPER_PID=$!

  # MA-UX-1/MA-RB-4: Ctrl-C / SIGTERM / terminal-close must STOP the run, not
  # silently orphan the setsid'd inner controller (which would keep provisioning,
  # building, aggregating — and, for a canonical run, republish out/current —
  # minutes after the operator thinks it is dead). The outer watchdog is the
  # parent of the inner session, so it traps the operator signals, sweeps every
  # descendant session (inner controller + the active phase's detached setsid
  # session) with TERM->KILL, writes a typed OperatorCancelled synthetic
  # INCOMPLETE, and exits nonzero. out/current is published only AFTER aggregation
  # succeeds, which the swept inner never reaches, so the shared pointer is left
  # untouched.
  on_operator_signal() { # signal-name
    local sig="$1" leader delivered="" leaders
    trap - INT TERM HUP  # idempotent: a second signal must not re-enter
    leaders="$(list_descendant_sessions "$WRAPPER_PID" || true)"
    if [ -z "$leaders" ] && [ -f "$PID_FILE" ]; then
      read -r leader < "$PID_FILE" 2>/dev/null && leaders="$leader" || true
    fi
    if [ -n "$leaders" ]; then
      for leader in $leaders; do kill -TERM -- "-$leader" 2>/dev/null || true; done
      delivered="SIGTERM"
      run_cmd sleep 3
      for leader in $leaders; do
        if kill -0 -- "-$leader" 2>/dev/null; then
          kill -KILL -- "-$leader" 2>/dev/null || true
          delivered="SIGTERM then SIGKILL"
        fi
      done
    fi
    kill -KILL "$WRAPPER_PID" 2>/dev/null || true
    GIT_COMMIT="$(run_cmd git -C "$REPO_ROOT" rev-parse HEAD 2>/dev/null || printf 'unknown')"
    write_failed_report "$OUT_PATH" OperatorCancelled "INCOMPLETE: run cancelled by operator (SIG$sig); the inner session was swept " "$delivered (MA-UX-1/MA-RB-4)"
    if [ "$OUT_PATH" != "$RUN_ROOT/reports/run-report.json" ]; then
      write_failed_report "$RUN_ROOT/reports/run-report.json" OperatorCancelled "INCOMPLETE: run cancelled by operator (SIG$sig); the inner session was swept " "$delivered (MA-UX-1/MA-RB-4)"
    fi
    echo "run-spike: INCOMPLETE — run cancelled by operator (SIG$sig); inner session terminated ($delivered)" >&2
    exit 20
  }
  trap 'on_operator_signal INT' INT
  trap 'on_operator_signal TERM' TERM
  trap 'on_operator_signal HUP' HUP

  DELIVERED=""
  TIMED_OUT=0
  while :; do
    if ! kill -0 "$WRAPPER_PID" 2>/dev/null; then
      break
    fi
    read -r NOW _ < /proc/uptime
    NOW="${NOW%.*}"
    if [ "$NOW" -ge "$OUTER_DEADLINE" ]; then
      TIMED_OUT=1
      INNER_PID=""
      if [ -f "$PID_FILE" ]; then read -r INNER_PID < "$PID_FILE" || true; fi
      # QA-014(2): if the PID file has not landed yet (race), fall back to the
      # session leader descended from the wrapper so the sweep still hits the
      # whole inner process group.
      if [ -z "$INNER_PID" ]; then
        INNER_PID="$(find_session_leader "$WRAPPER_PID" || true)"
      fi
      if [ -n "$INNER_PID" ]; then
        kill -TERM -- "-$INNER_PID" 2>/dev/null || true
        DELIVERED="SIGTERM"
        run_cmd sleep 5
        if kill -0 "$INNER_PID" 2>/dev/null; then
          kill -KILL -- "-$INNER_PID" 2>/dev/null || true
          DELIVERED="SIGTERM then SIGKILL"
        fi
      fi
      kill -KILL "$WRAPPER_PID" 2>/dev/null || true
      break
    fi
    run_cmd sleep 1
  done
  set +e
  wait "$WRAPPER_PID"
  INNER_EXIT=$?
  set -e

  if [ "$TIMED_OUT" = 1 ]; then
    GIT_COMMIT="$(run_cmd git -C "$REPO_ROOT" rev-parse HEAD 2>/dev/null || printf 'unknown')"
    write_failed_report "$OUT_PATH" WallClockExpiry "INCOMPLETE: wall-clock bound of ${WALL_BOUND}s exceeded on the /proc/uptime monotonic deadline; supervisor delivered " "$DELIVERED (RS-015)"
    if [ "$OUT_PATH" != "$RUN_ROOT/reports/run-report.json" ]; then
      write_failed_report "$RUN_ROOT/reports/run-report.json" WallClockExpiry "INCOMPLETE: wall-clock bound of ${WALL_BOUND}s exceeded on the /proc/uptime monotonic deadline; supervisor delivered " "$DELIVERED (RS-015)"
    fi
    echo "run-spike: INCOMPLETE — wall-clock bound exceeded; supervisor delivered $DELIVERED" >&2
    exit 20
  fi
  exit "$INNER_EXIT"
fi

# --- INNER CONTROLLER ----------------------------------------------------------
RUN_ROOT="$(cd -- "$RUN_ROOT" && pwd)"
# QA-014(2): the PID file is written FIRST, before any slow work, so the outer
# watchdog can always find (and TERM->KILL) this session; the outer also has a
# session-leader fallback if it reads before this line lands.
printf '%s\n' "$$" > "${SPIKE_PID_FILE:-$RUN_ROOT/.inner-pid}"
if [ -z "$SPIKE_RUN_ID" ] || [ -z "$SPIKE_NONCE" ]; then
  echo "run-spike: inner controller requires --run-id and --nonce from the watchdog parent" >&2
  exit 20
fi

# QA-007: the inner controller holds the SAME single absolute /proc/uptime
# deadline the outer watchdog was minted with (passed via SPIKE_DEADLINE), and
# supervises EVERY phase in its own setsid group against it — so a hang in a
# NON-probe phase (provision, restore, build, test, aggregation) is
# independently killable, not only a hung probe. The outer watchdog remains the
# ultimate backstop parenting this session.
: "${SPIKE_DEADLINE:?inner requires SPIKE_DEADLINE from the watchdog parent}"

# MA-XC-5: classify a wall-clock timeout by the OUT-OF-BAND supervisor flag
# (SPIKE_PHASE_TIMED_OUT), never by the exit code alone — a phase child that
# itself exits 124 (timeout(1) convention, conceivable from a tool wrapper) must
# not be misread as WallClockExpiry. run_with_env resets the flag per phase and
# supervise_phase sets it only on a real deadline kill.
phase_guard() { # phase-name exit-code
  if [ "${SPIKE_PHASE_TIMED_OUT:-0}" = 1 ]; then
    fail_run WallClockExpiry "$1 phase exceeded the ${WALL_BOUND}s wall-clock bound on the /proc/uptime monotonic deadline; per-phase supervisor delivered ${SPIKE_PHASE_SIGNALS:-SIGKILL} (QA-007/RS-015/MA-XC-5)"
  fi
}
case "$RUN_ROOT" in
  "$SPIKE_ROOT"/*) RUN_DIR_REL="${RUN_ROOT#"$SPIKE_ROOT"/}" ;;
  *) RUN_DIR_REL="$RUN_ROOT" ;;
esac
run_cmd mkdir -p -- "$RUN_ROOT/reports" "$RUN_ROOT/receipts" "$RUN_ROOT/sentinel" "$RUN_ROOT/decoys" "$RUN_ROOT/tmp" "$RUN_ROOT/dotnet-cli-home" "$RUN_ROOT/packages"

# Run-mint artifacts: git state + the pre-created zero-count sentinel ledger.
GIT_COMMIT="$(run_cmd git -C "$REPO_ROOT" rev-parse HEAD 2>/dev/null || printf 'unknown')"
GIT_STATUS_FIRST=""
while IFS= read -r line; do GIT_STATUS_FIRST="$line"; break; done < <(run_cmd git -C "$REPO_ROOT" status --porcelain 2>/dev/null)
if [ -n "$GIT_STATUS_FIRST" ]; then GIT_DIRTY=true; else GIT_DIRTY=false; fi
printf '{ "commit": "%s", "dirty": %s }\n' "$(json_escape "$GIT_COMMIT")" "$GIT_DIRTY" > "$RUN_ROOT/git-state.json.tmp"
run_cmd mv -- "$RUN_ROOT/git-state.json.tmp" "$RUN_ROOT/git-state.json"
printf '{ "nonce": "%s", "entries": [] }\n' "$SPIKE_NONCE" > "$RUN_ROOT/sentinel/ledger.json.tmp"
run_cmd mv -- "$RUN_ROOT/sentinel/ledger.json.tmp" "$RUN_ROOT/sentinel/ledger.json"

# MA-RB-R2-2: reap leaked cache staging (packages.staged.<dead-pid> / .old) at
# controller start — the SIGKILL-mid-seed backstop for the in-line staged
# cleanup, before the cache consume/seed below touch out/cache. Live seeders'
# staged dirs (pid alive) are never clobbered.
reap_cache_staging "$CACHE_DIR"

# --- per-launch environments constructed from the committed profiles (EA-008);
# --- the EXACT dictionary passed at each launch is recorded in env-audit.json.
AUDIT_ENTRIES=""
LAUNCH_KEYS=()
LAUNCH_VALS=()
build_profile_env() { # class
  local class="$1" in_class=0 line key val
  LAUNCH_KEYS=()
  LAUNCH_VALS=()
  while IFS= read -r line; do
    case "$line" in
      *"\"$class\": {"*) in_class=1; continue ;;
    esac
    if [ "$in_class" = 1 ]; then
      case "$line" in
        *"}"*) break ;;
      esac
      key="${line%%:*}"
      key="${key#*\"}"
      key="${key%\"*}"
      val="${line#*: \"}"
      val="${val%\"*}"
      case "$val" in
        '<constructed:decoy-first>') val="$RUN_ROOT/decoys:/usr/bin:/bin" ;;
        '<passthrough>')
          val="$(eval "printf '%s' \"\${$key:-}\"")"
          if [ -z "$val" ]; then continue; fi ;;
        *'<run-root>'*) val="${val//<run-root>/$RUN_ROOT}" ;;
      esac
      LAUNCH_KEYS+=("$key")
      LAUNCH_VALS+=("$val")
    fi
  done < "$SPIKE_ROOT/config/env-profiles.json"
}

require_env_key() { # key — profile regression guard (build-server disables IN-profile)
  local key="$1" i found=""
  for i in "${!LAUNCH_KEYS[@]}"; do
    if [ "${LAUNCH_KEYS[$i]}" = "$key" ]; then found=1; fi
  done
  if [ -z "$found" ]; then
    echo "run-spike: launch profile is missing required key $key (EA-008)" >&2
    exit 20
  fi
}

audit_launch() { # class argv0
  local class="$1" argv0="$2" i entry env_json=""
  for i in "${!LAUNCH_KEYS[@]}"; do
    if [ -n "$env_json" ]; then env_json="$env_json, "; fi
    env_json="$env_json\"$(json_escape "${LAUNCH_KEYS[$i]}")\": \"$(json_escape "${LAUNCH_VALS[$i]}")\""
  done
  entry="    { \"class\": \"$(json_escape "$class")\", \"argv0\": \"$(json_escape "$argv0")\", \"env\": { $env_json } }"
  if [ -n "$AUDIT_ENTRIES" ]; then
    AUDIT_ENTRIES="$AUDIT_ENTRIES,
$entry"
  else
    AUDIT_ENTRIES="$entry"
  fi
  {
    printf '{\n  "run_id": "%s",\n  "launches": [\n' "$SPIKE_RUN_ID"
    printf '%s\n' "$AUDIT_ENTRIES"
    printf '  ]\n}\n'
  } > "$RUN_ROOT/receipts/env-audit.json.tmp"
  run_cmd mv -- "$RUN_ROOT/receipts/env-audit.json.tmp" "$RUN_ROOT/receipts/env-audit.json"
}

# QA-007: every phase runs in its OWN setsid session; this supervisor tracks
# the per-phase PGID (written deterministically to a pidfile by the phase's own
# session leader — no /proc walking) and enforces the single absolute
# /proc/uptime deadline with TERM->KILL escalation, so a hang in ANY phase
# (provision, restore, build, test, probes, aggregation) is independently
# killable even though each phase is detached in its own session.
SPIKE_PHASE_TIMED_OUT=0
SPIKE_PHASE_SIGNALS=""
supervise_phase() { # wrapper-pid pgid-file
  local wrapper="$1" pgidfile="$2" pgid="" now
  while :; do
    if ! kill -0 "$wrapper" 2>/dev/null; then
      return 0
    fi
    read -r now _ < /proc/uptime
    now="${now%.*}"
    if [ -n "${SPIKE_DEADLINE:-}" ] && [ "$now" -ge "$SPIKE_DEADLINE" ]; then
      SPIKE_PHASE_TIMED_OUT=1
      pgid=""
      [ -f "$pgidfile" ] && read -r pgid < "$pgidfile" || true
      # QA-018(a): pgid-file race — if the deadline fires between mktemp and
      # the phase leader writing its pid, killing only the wrapper would leave
      # the detached setsid phase session running unbounded (the inner then
      # exits promptly and the outer never sweeps). Poll briefly for the pid,
      # then fall back to the session leader descended from the wrapper.
      if [ -z "$pgid" ]; then
        local retry
        for retry in 1 2 3 4 5; do
          run_cmd sleep 1
          [ -f "$pgidfile" ] && read -r pgid < "$pgidfile" || true
          [ -n "$pgid" ] && break
        done
      fi
      if [ -z "$pgid" ]; then
        pgid="$(find_session_leader "$wrapper" || true)"
      fi
      if [ -n "$pgid" ]; then
        kill -TERM -- "-$pgid" 2>/dev/null || true
        SPIKE_PHASE_SIGNALS="SIGTERM"
        run_cmd sleep 5
        if kill -0 -- "-$pgid" 2>/dev/null; then
          kill -KILL -- "-$pgid" 2>/dev/null || true
          SPIKE_PHASE_SIGNALS="SIGTERM then SIGKILL"
        fi
      fi
      kill -KILL "$wrapper" 2>/dev/null || true
      return 124
    fi
    run_cmd sleep 1
  done
}

run_with_env() { # class + a full dispatch invocation (audited statically)
  local class="$1"; shift
  if [ "$1" = "run_cmd" ]; then shift; fi
  build_profile_env "$class"
  # MA-XC-5: reset the out-of-band timeout flag per phase so phase_guard reads
  # THIS phase's supervisor state, not a prior phase's.
  SPIKE_PHASE_TIMED_OUT=0
  case "$class" in
    restore|build|test)
      # Documented SDK injections (EA-008): enumerated, never contradicting the
      # only-profile-keys rule.
      LAUNCH_KEYS+=(DOTNET_ROOT DOTNET_HOST_PATH)
      LAUNCH_VALS+=("$SPIKE_DOTNET_ROOT" "$SPIKE_DOTNET_BIN")
      require_env_key MSBUILDDISABLENODEREUSE
      require_env_key UseSharedCompilation
      ;;
    provision)
      # PR-006: provisioning must never launch under an empty/unconstructed
      # environment (no decoy-first PATH, no run-root TMPDIR) — guard the minimum
      # profile keys so a deleted/renamed provision class fails closed BEFORE the
      # launch, not one phase later at restore.
      require_env_key PATH
      require_env_key TMPDIR
      # QA-017: the SPIKE_RID_OVERRIDE fault hook (provision-z3.sh: INV-014
      # fail-closed test only) must still reach it when the operator/test set it.
      if [ -n "${SPIKE_RID_OVERRIDE:-}" ]; then
        LAUNCH_KEYS+=(SPIKE_RID_OVERRIDE)
        LAUNCH_VALS+=("$SPIKE_RID_OVERRIDE")
      fi
      ;;
    fetch)
      # PR-006 (fetch sibling): the BND-004 curl/tar legs must not launch under
      # an unconstructed environment either.
      require_env_key PATH
      require_env_key TMPDIR
      ;;
  esac
  if [ "$class" = "test" ]; then
    LAUNCH_KEYS+=(SPIKE_RUN_CONTEXT)
    LAUNCH_VALS+=("$RUN_ROOT/run-context.json")
    # QA-018(b): hand the suite phase this run's absolute /proc/uptime deadline
    # so nested controller launches performed by tests are budget-capped
    # (controller-injected, like SPIKE_RUN_CONTEXT — not an SDK injection).
    LAUNCH_KEYS+=(SPIKE_PARENT_DEADLINE)
    LAUNCH_VALS+=("$SPIKE_DEADLINE")
  fi
  audit_launch "$class" "$1"

  local cmd="$1"; shift
  local base="${cmd##*/}"
  local ok="" allowed
  for allowed in "${ALLOWLIST[@]}"; do
    if [ "$base" = "$allowed" ]; then ok=1; fi
  done
  if [ -z "$ok" ]; then
    echo "run-spike: DENY non-allowlisted command: $cmd (PRH-004)" >&2
    exit 20
  fi
  if [ "$base" = "$cmd" ] && [ "$base" = "dotnet" ]; then
    cmd="$SPIKE_DOTNET_BIN"
  fi

  local pgidfile
  pgidfile="$(run_cmd mktemp "$RUN_ROOT/receipts/phase-pgid.XXXXXX")"
  (
    local i v keep
    for i in "${!LAUNCH_KEYS[@]}"; do
      export "${LAUNCH_KEYS[$i]}=${LAUNCH_VALS[$i]}"
    done
    for v in $(compgen -e); do
      keep=""
      for i in "${!LAUNCH_KEYS[@]}"; do
        if [ "${LAUNCH_KEYS[$i]}" = "$v" ]; then keep=1; fi
      done
      case "$v" in PWD|SHLVL|_|PATH) keep=1 ;; esac
      if [ -z "$keep" ]; then unset "$v" 2>/dev/null || true; fi
    done
    # Per-phase session (QA-007): setsid makes the inner bash the session /
    # process-group leader; it records its own PID ($$ == PGID) so the
    # supervisor can TERM->KILL the entire group deterministically, then execs
    # the phase command in that group.
    exec setsid -w bash -c 'echo $$ > "$1"; shift 1; exec "$@"' _ "$pgidfile" "$cmd" "$@"
  ) &
  local wrapper=$!
  # run_with_env is always invoked under the caller's `set +e`; keep it that
  # way internally so a killed-phase wait or a benign rm never aborts the
  # controller before phase_guard can classify the timeout (QA-007).
  local sup_rc=0
  supervise_phase "$wrapper" "$pgidfile" || sup_rc=$?
  wait "$wrapper" 2>/dev/null
  local phase_rc=$?
  run_cmd rm -f "$pgidfile" 2>/dev/null || true
  if [ "$sup_rc" = 124 ]; then
    return 124
  fi
  return "$phase_rc"
}

fail_run() { # cause detail — QA-005: each site names its true typed cause
  local cause="$1" detail="$2"
  write_failed_report "$OUT_PATH" "$cause" "INCOMPLETE: $detail" ""
  if [ "$OUT_PATH" != "$RUN_ROOT/reports/run-report.json" ]; then
    write_failed_report "$RUN_ROOT/reports/run-report.json" "$cause" "INCOMPLETE: $detail" ""
  fi
  echo "run-spike: INCOMPLETE — $detail" >&2
  exit 20
}

# MA-RB-6/MA-XC-4: the shared-package-cache tar copies run as ONE supervised
# phase (launch class "cache") under setsid + the /proc/uptime deadline, so a
# wedge there is independently killable with typed attribution instead of being
# caught only by the outer watchdog's generic +20s backstop. The tar|tar copy
# idiom runs inside a single allowlisted `bash` so the phase is one process
# group.
cache_copy() { # src-dir dst-dir
  run_with_env cache run_cmd bash -p -c '
    set -euo pipefail
    tar -C "$1" -cf - . | tar -C "$2" -xf -
  ' cache-copy "$1" "$2"
}

# --- phase: provision ----------------------------------------------------------
# QA-017: provisioning runs in its OWN setsid session under supervise_phase
# (run_with_env), exactly like restore/build/test/probes/aggregation — a hang
# here (stalled fetch, blocked digest read) is independently killable at the
# single /proc/uptime deadline with typed per-phase attribution, never left to
# the outer watchdog's generic +20s backstop.
set +e
run_with_env provision run_cmd bash -p "$SCRIPT_DIR/provision-z3.sh" --run-root "$RUN_ROOT" >&2
PROVISION_EXIT=$?
set -e
phase_guard provision "$PROVISION_EXIT"
if [ "$PROVISION_EXIT" -ne 0 ]; then
  fail_run PrerequisiteFailure "provisioning failed (exit $PROVISION_EXIT) — run provisioning first: scripts/provision-z3.sh (BND-002 fail-closed)"
fi

# --- phase: locked restore (evidentiary; spike-local packages folder, EA-008) ---
# MA-UX-5/MA-RB-2: the shared package cache is trusted ONLY when its completion
# marker is present. A torn seed (deadline KILL / ENOSPC mid-populate) leaves a
# partial dir with NO marker, which is treated as no-cache (the run rebuilds
# from restore) instead of being copied into every subsequent run root forever.
CACHE_PKGS="$CACHE_DIR/packages"
CACHE_MARKER="$CACHE_DIR/packages.complete"
if shared_cache_is_trusted "$CACHE_DIR"; then
  set +e
  cache_copy "$CACHE_PKGS" "$RUN_ROOT/packages"
  CACHE_COPY_EXIT=$?
  set -e
  phase_guard cache "$CACHE_COPY_EXIT"
elif [ -d "$CACHE_PKGS" ]; then
  echo "run-spike: shared package cache at $CACHE_PKGS has no completion marker (partial/torn seed) — ignoring it and restoring fresh; recover a wedged cache with: rm -rf out/cache (MA-UX-5/AP-016)" >&2
fi
RESTORE_ARGV=(dotnet restore DafnyCompatSpike.sln --configfile NuGet.Config -noAutoResponse -p:SpikeRunRootRel="$RUN_DIR_REL")
set +e
run_with_env restore run_cmd dotnet restore DafnyCompatSpike.sln --configfile NuGet.Config -noAutoResponse -p:SpikeRunRootRel="$RUN_DIR_REL" >&2
RESTORE_EXIT=$?
set -e
phase_guard restore "$RESTORE_EXIT"
# QA-009: P01 is derived PER PARTITION, never a single solution-wide exit
# stamped onto both. On a clean solution restore both partitions pass; on a
# failure, each route's own project graph is re-validated independently in
# locked mode so a Route-B-only lock fault attributes only to P01(B) — the
# R4-07 non-veto property holds at the attestation level (the run still fails
# closed overall, but the receipt records which partition actually broke).
RESTORE_FAILED=""
p01_route_restore() { # csproj-list... -> 0 all ok, 1 any failed
  local proj rc=0
  for proj in "$@"; do
    set +e
    run_with_env restore run_cmd dotnet restore "$SPIKE_ROOT/$proj" --configfile NuGet.Config -noAutoResponse -p:SpikeRunRootRel="$RUN_DIR_REL" >/dev/null 2>&1
    [ $? -ne 0 ] && rc=1
    set -e
  done
  printf '%s' "$rc"
}
if [ "$RESTORE_EXIT" -eq 0 ]; then
  P01_A_EXIT=0
  P01_B_EXIT=0
else
  P01_A_EXIT="$(p01_route_restore adapters/SpikeDafnyAdapter.RouteA/SpikeDafnyAdapter.RouteA.csproj harness/RouteAHarness/RouteAHarness.csproj control/RouteAControl/RouteAControl.csproj)"
  P01_B_EXIT="$(p01_route_restore adapters/SpikeDafnyAdapter.RouteB/SpikeDafnyAdapter.RouteB.csproj harness/RouteBHarness/RouteBHarness.csproj control/RouteBControl/RouteBControl.csproj)"
  [ "$P01_A_EXIT" != 0 ] && RESTORE_FAILED="${RESTORE_FAILED}A "
  [ "$P01_B_EXIT" != 0 ] && RESTORE_FAILED="${RESTORE_FAILED}B "
fi
# MA-UX-5/MA-RB-2 + MA-RB-R2-2/R2-3: seed the shared cache ATOMICALLY — tar into
# a private PID-unique staged dir, then publish (under the out/-level lock, with
# mv -T no-nesting swap + the completion marker written LAST) via
# publish_cache_staged, so a crash mid-seed can only ever leave (a) the previous
# complete cache, or (b) a marker-less staged/partial dir the consume guard
# above ignores — never a torn dir presented as complete, and never a nested
# cache under two concurrent cold seeders.
if [ ! -f "$CACHE_MARKER" ] && [ "$RESTORE_EXIT" -eq 0 ]; then
  run_cmd mkdir -p -- "$CACHE_DIR"
  CACHE_STAGED="$CACHE_DIR/packages.staged.$$"
  run_cmd rm -rf -- "$CACHE_STAGED"
  run_cmd mkdir -p -- "$CACHE_STAGED"
  set +e
  cache_copy "$RUN_ROOT/packages" "$CACHE_STAGED"
  CACHE_SEED_EXIT=$?
  set -e
  # MA-RB-R2-2: drop the staged copy on ANY non-success BEFORE phase_guard can
  # fail_run/exit — otherwise a deadline-kill (CACHE_SEED_EXIT=124) leaks
  # packages.staged.$$ in the never-pruned out/cache. reap_cache_staging is the
  # backstop for a SIGKILL between mkdir and here.
  [ "$CACHE_SEED_EXIT" -ne 0 ] && { run_cmd rm -rf -- "$CACHE_STAGED" 2>/dev/null || true; }
  phase_guard cache "$CACHE_SEED_EXIT"
  if [ "$CACHE_SEED_EXIT" -eq 0 ]; then
    publish_cache_staged "$CACHE_DIR" "$CACHE_STAGED"
  fi
fi

RESTORE_ARGV_JSON=""
for a in "${RESTORE_ARGV[@]}"; do
  if [ -n "$RESTORE_ARGV_JSON" ]; then RESTORE_ARGV_JSON="$RESTORE_ARGV_JSON, "; fi
  RESTORE_ARGV_JSON="$RESTORE_ARGV_JSON\"$(json_escape "$a")\""
done
LOCK_DIGESTS=""
for lock in contracts/SpikeContracts adapters/SpikeDafnyAdapter.RouteA adapters/SpikeDafnyAdapter.RouteB harness/RouteAHarness harness/RouteBHarness aggregator/SpikeAggregator control/RouteAControl control/RouteBControl tests/SpikeTests; do
  if [ -f "$SPIKE_ROOT/$lock/packages.lock.json" ]; then
    if [ -n "$LOCK_DIGESTS" ]; then LOCK_DIGESTS="$LOCK_DIGESTS, "; fi
    LOCK_DIGESTS="$LOCK_DIGESTS\"$(json_escape "$lock")\": \"$(sha_of "$SPIKE_ROOT/$lock/packages.lock.json")\""
  fi
done
{
  printf '{\n'
  printf '  "run_id": "%s",\n  "nonce": "%s",\n  "exit": %s,\n' "$SPIKE_RUN_ID" "$SPIKE_NONCE" "$RESTORE_EXIT"
  printf '  "argv": [ %s ],\n' "$RESTORE_ARGV_JSON"
  printf '  "p01_partitions": {\n'
  printf '    "A": { "projects": ["SpikeDafnyAdapter.RouteA", "RouteAHarness", "RouteAControl", "SpikeContracts", "SpikeAggregator", "SpikeTests"], "exit": %s },\n' "$P01_A_EXIT"
  printf '    "B": { "projects": ["SpikeDafnyAdapter.RouteB", "RouteBHarness", "RouteBControl", "SpikeContracts", "SpikeAggregator", "SpikeTests"], "exit": %s }\n' "$P01_B_EXIT"
  printf '  },\n'
  printf '  "lock_sha256": { %s }\n' "$LOCK_DIGESTS"
  printf '}\n'
} > "$RUN_ROOT/receipts/restore-receipt.json.tmp"
run_cmd mv -- "$RUN_ROOT/receipts/restore-receipt.json.tmp" "$RUN_ROOT/receipts/restore-receipt.json"
if [ "$RESTORE_EXIT" -ne 0 ]; then
  fail_run PrerequisiteFailure "locked restore failed (failing projects: $RESTORE_FAILED; NU1004/NU1102-class locked-mode mismatches are fail-closed, INV-001; if the shared package cache is corrupt, recover with: rm -rf out/cache — MA-UX-5)"
fi

# --- phase: build (everything beneath the run root — DD-008) --------------------
# MA-XC-2: capture `dotnet --version` via a TEMP FILE, not a command
# substitution — a `$(run_with_env build …)` runs audit_launch in a subshell,
# whose env-audit.json write is then overwritten by the next parent-side
# audit_launch rebuilding from the parent's stale AUDIT_ENTRIES, silently
# dropping the build-class launch from the receipt. Running run_with_env in the
# parent shell (output redirected to a file) keeps the audit entry.
set +e
SDK_VER_FILE="$RUN_ROOT/receipts/.sdk-version"
run_with_env build run_cmd dotnet --version > "$SDK_VER_FILE"
SDK_VER_RC=$?
set -e
SDK_ACTUAL=""
[ -f "$SDK_VER_FILE" ] && read -r SDK_ACTUAL < "$SDK_VER_FILE" || true
run_cmd rm -f "$SDK_VER_FILE" 2>/dev/null || true
phase_guard build "$SDK_VER_RC"   # a deadline already passed here is a build-phase timeout
set +e
run_with_env build run_cmd dotnet build DafnyCompatSpike.sln --no-restore -noAutoResponse -p:SpikeRunRootRel="$RUN_DIR_REL" >&2
BUILD_EXIT=$?
set -e
phase_guard build "$BUILD_EXIT"

ART_ROUTE_A="build/RouteAHarness/bin/Debug/net10.0/RouteAHarness.dll"
ART_ROUTE_B="build/RouteBHarness/bin/Debug/net10.0/RouteBHarness.dll"
ART_AGG="build/SpikeAggregator/bin/Debug/net10.0/SpikeAggregator.dll"
ART_CTL_A="build/RouteAControl/bin/Debug/net8.0/RouteAControl.dll"
ART_CTL_B="build/RouteBControl/bin/Debug/net8.0/RouteBControl.dll"

ARTIFACT_ENTRIES=""
artifact_entry() { # rel
  local rel="$1"
  if [ ! -f "$RUN_ROOT/$rel" ]; then
    fail_run PrerequisiteFailure "built artifact missing beneath the run root: $rel (DD-008)"
  fi
  if [ -n "$ARTIFACT_ENTRIES" ]; then
    ARTIFACT_ENTRIES="$ARTIFACT_ENTRIES,
"
  fi
  ARTIFACT_ENTRIES="$ARTIFACT_ENTRIES    { \"path\": \"$(json_escape "$rel")\", \"sha256\": \"$(sha_of "$RUN_ROOT/$rel")\" }"
}
if [ "$BUILD_EXIT" -eq 0 ]; then
  for rel in "$ART_ROUTE_A" "$ART_ROUTE_B" "$ART_AGG" "$ART_CTL_A" "$ART_CTL_B"; do
    artifact_entry "$rel"
    artifact_entry "${rel%.dll}.deps.json"
    artifact_entry "${rel%.dll}.runtimeconfig.json"
  done
fi
{
  printf '{\n'
  printf '  "run_id": "%s",\n  "nonce": "%s",\n  "exit": %s,\n  "sdk_version": "%s",\n' "$SPIKE_RUN_ID" "$SPIKE_NONCE" "$BUILD_EXIT" "$(json_escape "$SDK_ACTUAL")"
  printf '  "artifacts": [\n%s\n  ]\n' "$ARTIFACT_ENTRIES"
  printf '}\n'
} > "$RUN_ROOT/receipts/build-receipt.json.tmp"
run_cmd mv -- "$RUN_ROOT/receipts/build-receipt.json.tmp" "$RUN_ROOT/receipts/build-receipt.json"
if [ "$BUILD_EXIT" -ne 0 ]; then
  fail_run PrerequisiteFailure "build failed (exit $BUILD_EXIT)"
fi

# --- BND-004: net8 control runtime (digest-verified, installed in the run root) --
NET8_URL="https://builds.dotnet.microsoft.com/dotnet/Runtime/8.0.29/dotnet-runtime-8.0.29-linux-x64.tar.gz"
NET8_SHA="dba346c5c4357e1befebf14de8c8ee7f09313cc12c7c0015a4cdd4dfd0efba81"
NET8_ARCHIVE_NAME="dotnet-runtime-8.0.29-linux-x64.tar.gz"
NET8_ARCHIVE="$CACHE_DIR/$NET8_ARCHIVE_NAME"
# QA-017: the BND-004 fetch/extract legs run in their own setsid sessions
# under supervise_phase (launch class "fetch"), so a stalled download or a
# wedged extraction is independently killable with typed phase attribution.
if [ ! -f "$NET8_ARCHIVE" ] || [ "$(sha_of "$NET8_ARCHIVE")" != "$NET8_SHA" ]; then
  run_cmd mkdir -p -- "$CACHE_DIR"
  # MA-UX-7: announce the fetch (source + approx size) so first-run dead air on
  # a slow link is attributable, and confirm completion.
  echo "run-spike: fetching net8 control runtime (~28MB) from builds.dotnet.microsoft.com ..." >&2
  set +e
  run_with_env fetch run_cmd curl --disable -fsSL -o "$NET8_ARCHIVE.download" "$NET8_URL"
  FETCH_EXIT=$?
  set -e
  phase_guard fetch "$FETCH_EXIT"
  if [ "$FETCH_EXIT" -ne 0 ]; then
    fail_run PrerequisiteFailure "net8 control-runtime fetch failed (exit $FETCH_EXIT) (BND-004 fail-closed; host/TFM adjudication blocked)"
  fi
  if [ "$(sha_of "$NET8_ARCHIVE.download")" != "$NET8_SHA" ]; then
    fail_run PrerequisiteFailure "net8 control-runtime archive does not match the pinned SHA-256 (BND-004 fail-closed; host/TFM adjudication blocked)"
  fi
  run_cmd mv -- "$NET8_ARCHIVE.download" "$NET8_ARCHIVE"
  echo "run-spike: net8 control runtime fetched and digest-verified." >&2
fi
CONTROL_ROOT="$RUN_ROOT/control-runtime"
run_cmd mkdir -p -- "$CONTROL_ROOT"
set +e
run_with_env fetch run_cmd tar -xzf "$NET8_ARCHIVE" -C "$CONTROL_ROOT"
EXTRACT_EXIT=$?
set -e
phase_guard fetch "$EXTRACT_EXIT"
if [ "$EXTRACT_EXIT" -ne 0 ]; then
  fail_run PrerequisiteFailure "net8 control-runtime extraction failed (exit $EXTRACT_EXIT) (BND-004 fail-closed; host/TFM adjudication blocked)"
fi

# --- QA-013: source-set content digest — a canonical run stamps the digest of
# --- the exact source tree it built so a direct `dotnet test` consumer can
# --- detect stale binaries (uncommitted edits leave HEAD unchanged). Cheap:
# --- hash the sorted manifest of tracked source-file digests (git ls-files).
SRC_MANIFEST="$RUN_ROOT/.source-manifest"
: > "$SRC_MANIFEST"
# git ls-files emits paths in deterministic (sorted) order, so no external
# sort is needed — the manifest is stable across runs on the same tree.
while IFS= read -r f; do
  [ -f "$SPIKE_ROOT/$f" ] && printf '%s  %s\n' "$(sha_of "$SPIKE_ROOT/$f")" "$f" >> "$SRC_MANIFEST"
done < <(run_cmd git -C "$SPIKE_ROOT" ls-files -- '*.cs' '*.csproj' '*.sln' '*.dfy' 'schema' 'manifest' 'config' 'scripts' 'Directory.Build.props' 'Directory.Build.targets' 'Directory.Build.rsp' 'Directory.Packages.props' 'global.json' 'NuGet.Config' 2>/dev/null)
SRC_DIGEST="$(sha_of "$SRC_MANIFEST")"
[ -z "$SRC_DIGEST" ] && SRC_DIGEST="unknown"

# --- run-context receipt (DD-008/TA-B13) + atomically published runsettings -----
{
  printf '{\n'
  printf '  "run_id": "%s",\n' "$SPIKE_RUN_ID"
  printf '  "run_root": "%s",\n' "$(json_escape "$RUN_ROOT")"
  printf '  "source_set_sha256": "%s",\n' "$SRC_DIGEST"
  printf '  "artifacts": {\n'
  printf '    "RouteAHarness": { "path": "%s", "sha256": "%s" },\n' "$ART_ROUTE_A" "$(sha_of "$RUN_ROOT/$ART_ROUTE_A")"
  printf '    "RouteBHarness": { "path": "%s", "sha256": "%s" },\n' "$ART_ROUTE_B" "$(sha_of "$RUN_ROOT/$ART_ROUTE_B")"
  printf '    "SpikeAggregator": { "path": "%s", "sha256": "%s" },\n' "$ART_AGG" "$(sha_of "$RUN_ROOT/$ART_AGG")"
  printf '    "RouteAControl": { "path": "%s", "sha256": "%s" },\n' "$ART_CTL_A" "$(sha_of "$RUN_ROOT/$ART_CTL_A")"
  printf '    "RouteBControl": { "path": "%s", "sha256": "%s" },\n' "$ART_CTL_B" "$(sha_of "$RUN_ROOT/$ART_CTL_B")"
  printf '    "control_dotnet_host": { "path": "control-runtime/dotnet", "sha256": "%s" }\n' "$(sha_of "$CONTROL_ROOT/dotnet")"
  printf '  }\n'
  printf '}\n'
} > "$RUN_ROOT/run-context.json.tmp"
run_cmd mv -- "$RUN_ROOT/run-context.json.tmp" "$RUN_ROOT/run-context.json"

# --- run-local test settings (bug #3 fix) ---------------------------------------
# The in-controller suite binds to THIS run's run-context through a settings file
# INSIDE the run root — never the shared out/current pointer. Publishing
# out/current (the dev-loop's "last COMPLETED run" pointer) here, BEFORE the
# probes and suite, left it aimed at aborted/failed/concurrent runs that never
# finished; it is now published only AFTER aggregation succeeds (below). Only
# canonical runs execute the suite, so only they need the run-local settings.
RUN_LOCAL_SETTINGS="$RUN_ROOT/spike.runsettings"
if [ "${SPIKE_CANONICAL:-0}" = 1 ]; then
  {
    printf '<?xml version="1.0" encoding="utf-8"?>\n'
    printf '<RunSettings>\n  <RunConfiguration>\n    <EnvironmentVariables>\n'
    printf '      <SPIKE_RUN_CONTEXT>%s</SPIKE_RUN_CONTEXT>\n' "$RUN_ROOT/run-context.json"
    printf '    </EnvironmentVariables>\n  </RunConfiguration>\n</RunSettings>\n'
  } > "$RUN_LOCAL_SETTINGS.tmp"
  run_cmd mv -- "$RUN_LOCAL_SETTINGS.tmp" "$RUN_LOCAL_SETTINGS"
fi

# --- phase: route probes (route children own P03, P05-P12 only) ------------------
# Sentinel-configured probes run FIRST: after a real Boogie+Z3 session, the
# recording stub's pipe death can leave Boogie's completion surface unsettled
# (the P05 verification leg is additionally time-bounded in the harness — the
# ledger FILE is the observable, RS-004b).
ROUTE_PROBES="P05,P08,P09,P03,P06,P07,P10,P11,P12"
SOLVER_ARGS=()
if [ -n "$SOLVER_OVERRIDE" ]; then SOLVER_ARGS=(--solver "$SOLVER_OVERRIDE"); fi
set +e
run_with_env harness run_cmd dotnet exec "$RUN_ROOT/$ART_ROUTE_A" --run-id "$SPIKE_RUN_ID" --probe "$ROUTE_PROBES" --out "$RUN_ROOT/reports/route-a.json" "${SOLVER_ARGS[@]}"
EXIT_A=$?
run_with_env harness run_cmd dotnet exec "$RUN_ROOT/$ART_ROUTE_B" --run-id "$SPIKE_RUN_ID" --probe "$ROUTE_PROBES" --out "$RUN_ROOT/reports/route-b.json" "${SOLVER_ARGS[@]}"
EXIT_B=$?
# BND-004 control identity cells via the explicit private host argv.
run_with_env control run_cmd "$CONTROL_ROOT/dotnet" exec --fx-version 8.0.29 "$RUN_ROOT/$ART_CTL_A" --identity-probe --run-root "$RUN_ROOT" --out "$RUN_ROOT/reports/control-a.json"
CTL_EXIT_A=$?
run_with_env control run_cmd "$CONTROL_ROOT/dotnet" exec --fx-version 8.0.29 "$RUN_ROOT/$ART_CTL_B" --identity-probe --run-root "$RUN_ROOT" --out "$RUN_ROOT/reports/control-b.json"
CTL_EXIT_B=$?
set -e
echo "run-spike: control identity cells exited A=$CTL_EXIT_A B=$CTL_EXIT_B (BND-004; recorded, non-gating for the run report)" >&2

# --- phase: suite (canonical operator runs only; variance runs record unknown) ---
SUITE_STATUS="unknown"
SUITE_EXIT=0
if [ "${SPIKE_CANONICAL:-0}" = 1 ]; then
  set +e
  # -p:RunSettingsFilePath binds the suite to the RUN-LOCAL settings (SPIKE_RUN_CONTEXT
  # -> this run's run-context.json), independent of the shared out/current pointer
  # (which is not published until after aggregation — bug #3). Setting the property
  # explicitly also suppresses the Directory.Build.props out/current fallback.
  run_with_env test run_cmd dotnet test DafnyCompatSpike.sln --no-build -noAutoResponse -p:SpikeRunRootRel="$RUN_DIR_REL" -p:RunSettingsFilePath="$RUN_LOCAL_SETTINGS"
  SUITE_EXIT=$?
  set -e
  phase_guard test "$SUITE_EXIT"
  if [ "$SUITE_EXIT" -eq 0 ]; then SUITE_STATUS="success"; else SUITE_STATUS="failure"; fi
  # MA-VI-6 (coordination item A): emit the NONCE-BOUND suite receipt the
  # aggregator DERIVES final_suite_status from (RunLayout.SuiteReceiptRelativePath).
  # Only canonical runs reach here (the suite phase actually ran); variance runs
  # emit nothing and final_suite_status stays "unknown". Without this receipt the
  # aggregator downgrades an unvalidated --suite-status=success to "unknown", so
  # a COMPATIBLE verdict is only ever computable once this receipt is present and
  # its run_id/nonce bind to the current run. Atomic (write-temp-then-mv).
  SUITE_RECEIPT="$RUN_ROOT/receipts/suite-receipt.json"
  printf '{ "run_id": "%s", "nonce": "%s", "suite_exit": %s, "source_manifest_sha256": "%s" }\n' \
    "$SPIKE_RUN_ID" "$SPIKE_NONCE" "$SUITE_EXIT" "$(json_escape "${SRC_DIGEST:-unknown}")" > "$SUITE_RECEIPT.tmp"
  run_cmd mv -- "$SUITE_RECEIPT.tmp" "$SUITE_RECEIPT"
fi

# --- phase: aggregation (the managed aggregator VALIDATES the receipts) ----------
SOLVER_FOR_AGG="$RUN_ROOT/solver/z3-4.12.1/bin/z3"
if [ -n "$SOLVER_OVERRIDE" ]; then SOLVER_FOR_AGG="$SOLVER_OVERRIDE"; fi
set +e
run_with_env harness run_cmd dotnet exec "$RUN_ROOT/$ART_AGG" --manifest "$SPIKE_ROOT/manifest/probe-manifest.json" --schema "$SPIKE_ROOT/schema/evidence-schema.json" --registry "$SPIKE_ROOT/schema/schema-version-registry.json" --run-id "$SPIKE_RUN_ID" --nonce "$SPIKE_NONCE" --run-root "$RUN_ROOT" --restore-receipt "$RUN_ROOT/receipts/restore-receipt.json" --build-receipt "$RUN_ROOT/receipts/build-receipt.json" --suite-status "$SUITE_STATUS" --solver "$SOLVER_FOR_AGG" --report "A=$RUN_ROOT/reports/route-a.json:$EXIT_A" --report "B=$RUN_ROOT/reports/route-b.json:$EXIT_B" --out "$RUN_ROOT/reports/run-report.json" --out-copy "$OUT_PATH" --aggregation-receipt "$RUN_ROOT/receipts/aggregation.json"
AGG_EXIT=$?
set -e
phase_guard aggregation "$AGG_EXIT"
if [ ! -f "$OUT_PATH" ]; then
  fail_run MissingReport "aggregation produced no run-level report (exit $AGG_EXIT) — a missing report is its own state, never pass (AP-009)"
fi

# --- publish out/current (bug #3 fix — deferred to post-aggregation) -------------
# QA-021: ONLY canonical operator runs publish the shared out/current pointer;
# nested/variance runs (--run-root/--out/--config/--solver — the form every
# in-suite launch uses) must never mutate cross-run state outside their own run
# root. It is published HERE — after the suite ran AND the aggregator produced a
# run report — so the dev-loop's "last completed run" pointer only ever advances
# to a run that finished its full pipeline. A run swept mid-flight (operator
# cancel, phase timeout, crash) exits above without reaching this point, leaving
# the previous pointer intact. The pointer content is identical to the run-local
# settings; it is regenerated here (not copied — `cp` is not in the PRH-004
# run_cmd allowlist) and swapped in atomically (write-temp-then-mv).
if [ "${SPIKE_CANONICAL:-0}" = 1 ]; then
  run_cmd mkdir -p -- "$SPIKE_ROOT/out/current"
  {
    printf '<?xml version="1.0" encoding="utf-8"?>\n'
    printf '<RunSettings>\n  <RunConfiguration>\n    <EnvironmentVariables>\n'
    printf '      <SPIKE_RUN_CONTEXT>%s</SPIKE_RUN_CONTEXT>\n' "$RUN_ROOT/run-context.json"
    printf '    </EnvironmentVariables>\n  </RunConfiguration>\n</RunSettings>\n'
  } > "$SPIKE_ROOT/out/current/spike.runsettings.tmp"
  run_cmd mv -- "$SPIKE_ROOT/out/current/spike.runsettings.tmp" "$SPIKE_ROOT/out/current/spike.runsettings"
fi

# QA-014(1): housekeeping — drop provisioning scratch (extract dirs, .corrupt,
# .download stragglers) now that everything under the run root is captured.
for junk in "$RUN_ROOT"/z3-extract.* "$RUN_ROOT"/*.corrupt "$RUN_ROOT"/*.download; do
  [ -e "$junk" ] && run_cmd chmod -R u+rwx "$junk" 2>/dev/null
  [ -e "$junk" ] && run_cmd rm -rf "$junk"
done

# QA-008: the CONTROLLER owns the run-level exit contract for CI wiring — the
# exit channel is fail-closed to match the report/summary. Nonzero on: suite
# failure (canonical runs), any route child probe-failure/INCOMPLETE, or an
# aggregator nonzero. A clean canonical run (or a variance run whose children
# all passed) exits 0.
CONTROLLER_EXIT=0
if [ "$SUITE_STATUS" = "failure" ]; then CONTROLLER_EXIT=1; fi
if [ "$AGG_EXIT" -ne 0 ]; then CONTROLLER_EXIT=1; fi
if [ "$EXIT_A" -ne 0 ] || [ "$EXIT_B" -ne 0 ]; then CONTROLLER_EXIT=1; fi

echo "run-spike: run $SPIKE_RUN_ID complete; report: $OUT_PATH (suite: $SUITE_STATUS; route children A=$EXIT_A B=$EXIT_B; aggregator exit $AGG_EXIT; controller exit $CONTROLLER_EXIT)" >&2
exit "$CONTROLLER_EXIT"
