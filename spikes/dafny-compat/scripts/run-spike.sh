#!/usr/bin/env bash
# scripts/run-spike.sh — the canonical entry point: bootstrap controller
# (INV-006 ownership topology; PRH-004 layer 1).
#
# INVOCATION CONTRACT (PRH-004, codex R4-08, TA-B9):
#   env -i HOME="$HOME" bash -p scripts/run-spike.sh
# Hardening is self-enforcing: privileged mode makes bash ignore BASH_ENV; when
# invoked WITHOUT it (plain `bash scripts/run-spike.sh`), the controller
# refuses to run (fail-closed, nonzero exit) — it never proceeds under a shell
# that may already have executed a poisoned BASH_ENV.
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

set -euo pipefail
PATH="/usr/bin:/bin"
export PATH

SCRIPT_SELF="${BASH_SOURCE[0]}"
SCRIPT_DIR="$(cd -- "${SCRIPT_SELF%/*}" && pwd)"
SPIKE_ROOT="$(cd -- "$SCRIPT_DIR/.." && pwd)"
REPO_ROOT="$(cd -- "$SPIKE_ROOT/../.." && pwd)"
CACHE_DIR="$SPIKE_ROOT/out/cache"

# --- .NET SDK resolution (stated rule): DOTNET_ROOT, then the HOME-local
# --- install, then the pointer a previous controller run cached under out/.
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
    echo "run-spike: no pinned .NET SDK found (checked DOTNET_ROOT, HOME/.dotnet, out/cache/dotnet-root) — install SDK 10.0.302 per README.md" >&2
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

json_escape() {
  local s="${1//\\/\\\\}"
  s="${s//\"/\\\"}"
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


config_int() { # file key default
  local line value
  while IFS= read -r line; do
    case "$line" in
      *"\"$2\""*)
        value="${line##*: }"
        value="${value%%,*}"
        case "$value" in
          ''|*[!0-9]*) : ;;
          *) printf '%s' "$value"; return 0 ;;
        esac ;;
    esac
  done < "$1"
  printf '%s' "$3"
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
WALL_BOUND="$(config_int "$CONFIG_FILE" wall_clock_bound_seconds 1800)"
SCHEMA_SHA="$(sha_of "$SPIKE_ROOT/schema/evidence-schema.json")"
MANIFEST_SHA="$(sha_of "$SPIKE_ROOT/manifest/probe-manifest.json")"
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
    printf '  "evidence_schema_version": 2,\n'
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

phase_guard() { # phase-name exit-code
  if [ "$2" = 124 ]; then
    fail_run WallClockExpiry "$1 phase exceeded the ${WALL_BOUND}s wall-clock bound on the /proc/uptime monotonic deadline; per-phase supervisor delivered ${SPIKE_PHASE_SIGNALS:-SIGKILL} (QA-007/RS-015)"
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
      # QA-017: provisioning now runs under the constructed profile too; the
      # SPIKE_RID_OVERRIDE fault hook (provision-z3.sh: INV-014 fail-closed
      # test only) must still reach it when the operator/test set it.
      if [ -n "${SPIKE_RID_OVERRIDE:-}" ]; then
        LAUNCH_KEYS+=(SPIKE_RID_OVERRIDE)
        LAUNCH_VALS+=("$SPIKE_RID_OVERRIDE")
      fi
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
if [ -d "$CACHE_DIR/packages" ]; then
  run_cmd tar -C "$CACHE_DIR/packages" -cf - . | run_cmd tar -C "$RUN_ROOT/packages" -xf -
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
if [ ! -d "$CACHE_DIR/packages" ] && [ "$RESTORE_EXIT" -eq 0 ]; then
  run_cmd mkdir -p -- "$CACHE_DIR/packages"
  run_cmd tar -C "$RUN_ROOT/packages" -cf - . | run_cmd tar -C "$CACHE_DIR/packages" -xf -
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
  fail_run PrerequisiteFailure "locked restore failed (failing projects: $RESTORE_FAILED; NU1004/NU1102-class locked-mode mismatches are fail-closed, INV-001)"
fi

# --- phase: build (everything beneath the run root — DD-008) --------------------
set +e
SDK_ACTUAL="$(run_with_env build run_cmd dotnet --version)"
SDK_VER_RC=$?
set -e
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
done < <(run_cmd git -C "$SPIKE_ROOT" ls-files -- '*.cs' '*.csproj' '*.dfy' 'schema' 'manifest' 'config' 'scripts' 'Directory.Build.props' 'Directory.Build.targets' 'Directory.Packages.props' 'global.json' 'NuGet.Config' 2>/dev/null)
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

# QA-021: ONLY canonical operator runs publish the out/current pointer.
# Nested/variance runs (--run-root/--out/--config/--solver — the form every
# in-suite launch uses) must never mutate shared cross-run state outside their
# own run root: republishing out/current from a nested test run left the dev
# loop's "latest run" pointing at a mutable test-fixture scratch root.
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
  run_with_env test run_cmd dotnet test DafnyCompatSpike.sln --no-build -noAutoResponse -p:SpikeRunRootRel="$RUN_DIR_REL"
  SUITE_EXIT=$?
  set -e
  phase_guard test "$SUITE_EXIT"
  if [ "$SUITE_EXIT" -eq 0 ]; then SUITE_STATUS="success"; else SUITE_STATUS="failure"; fi
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
