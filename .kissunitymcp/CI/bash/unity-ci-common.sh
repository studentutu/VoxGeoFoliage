#!/usr/bin/env bash
# Shared configuration, exact editor gate, logging, and freshness contract.
[[ -z "${SIMPLE_UNITY_MCP_CI_COMMON_LOADED:-}" ]] || return 0
SIMPLE_UNITY_MCP_CI_COMMON_LOADED=1
set -euo pipefail
CI_BASH_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
source "$CI_BASH_DIR/../../scripts/common.sh"
REPOSITORY_ROOT="$(cd "$CI_BASH_DIR/../.." && pwd -P)"
UNITY_PROJECT_PATH="$(to_unix_path "${UNITY_PROJECT_PATH:-$PWD}")"
[[ -d "$UNITY_PROJECT_PATH" ]] || fail "Project directory not found: $UNITY_PROJECT_PATH. Pass it to scripts/unity.sh."
UNITY_PROJECT_PATH="$(cd "$UNITY_PROJECT_PATH" && pwd -P)"
CONFIG_PATH="$UNITY_PROJECT_PATH/.kissunitymcp/tools.env"
CI_OUTPUT_DIR="$(to_unix_path "${CI_OUTPUT_DIR:-$UNITY_PROJECT_PATH/Logs/kissunitymcp}")"
require_command() { command -v "$1" >/dev/null 2>&1 || fail "Required Bash/Git utility not found: $1"; }
require_unity_project() { require_project "$UNITY_PROJECT_PATH"; }
unity_version() { require_unity_project; printf '%s\n' "$PROJECT_VERSION"; }

load_tool_config() {
  local line key value
  [[ -f "$CONFIG_PATH" ]] || return 0
  while IFS= read -r line || [[ -n "$line" ]]; do
    line="${line%$'\r'}"
    [[ -z "$line" || "$line" == \#* ]] && continue
    [[ "$line" == *=* ]] || fail "Expected KEY=value in $CONFIG_PATH: $line"
    key="${line%%=*}"; value="${line#*=}"
    case "$key" in UNITY_HUB_EDITOR_ROOT|UNITY_EDITOR_PATH|RIDER_ROOT|RIDER_MSBUILD|MSBUILD_RUNTIME|UNITY_SOLUTION_PATH) ;;
      *) fail "Unknown setting $key in $CONFIG_PATH";; esac
    if [[ "$value" == \"*\" || "$value" == \'*\' ]]; then value="${value:1:${#value}-2}"; fi
    if [[ -z "${!key:-}" ]]; then printf -v "$key" '%s' "$value"; fi
  done < "$CONFIG_PATH"
}
load_tool_config

resolve_unity_editor() {
  local requested root suffix candidate installed
  requested="$(unity_version)"
  case "$(uname -s)" in
    MINGW*|MSYS*|CYGWIN*) root='/c/Program Files/Unity/Hub/Editor'; suffix=Editor/Unity.exe;;
    Darwin*) root=/Applications/Unity/Hub/Editor; suffix=Unity.app/Contents/MacOS/Unity;;
    Linux*) root="$HOME/Unity/Hub/Editor"; suffix=Editor/Unity;;
    *) fail "Unsupported platform: $(uname -s)";;
  esac
  root="$(to_unix_path "${UNITY_HUB_EDITOR_ROOT:-$root}")"
  printf 'Required Unity: %s\nHub editor root: %s\nInstalled editors:\n' "$requested" "$root" >&2
  for installed in "$root"/*; do [[ ! -d "$installed" ]] || printf '  %s\n' "${installed##*/}" >&2; done
  candidate="$root/$requested/$suffix"
  [[ -x "$candidate" ]] || fail "Exact Unity editor missing: $candidate. Install Unity $requested or edit UNITY_HUB_EDITOR_ROOT in $CONFIG_PATH."
  if [[ -n "${UNITY_EDITOR_PATH:-}" ]]; then
    candidate="$(to_unix_path "$UNITY_EDITOR_PATH")"
    [[ -x "$candidate" && "$candidate" == */"$requested"/"$suffix" ]] || fail "UNITY_EDITOR_PATH must point to the exact $requested/$suffix executable: $candidate. Edit $CONFIG_PATH."
  fi
  printf '%s\n' "$candidate"
}

resolve_msbuild() {
  local root candidate roots=() candidates=()
  if [[ -n "${RIDER_MSBUILD:-}" ]]; then
    candidate="$(to_unix_path "$RIDER_MSBUILD")"
    [[ -f "$candidate" ]] || fail "RIDER_MSBUILD not found: $candidate. Edit $CONFIG_PATH."
    printf '%s\n' "$candidate"; return
  fi
  shopt -s nullglob
  if [[ -n "${RIDER_ROOT:-}" ]]; then roots=("$(to_unix_path "$RIDER_ROOT")")
  else
    case "$(uname -s)" in
      MINGW*|MSYS*|CYGWIN*) roots=(/c/Program\ Files/JetBrains/JetBrains\ Rider\ *);;
      Darwin*) roots=(/Applications/Rider*.app "$HOME"/Applications/Rider*.app);;
      *) roots=(/opt/JetBrains/Rider* /opt/rider* "$HOME"/.local/share/JetBrains/Toolbox/apps/rider/*);;
    esac
  fi
  for root in "${roots[@]}"; do
    for candidate in "$root/tools/MSBuild/Current/Bin/amd64/MSBuild.exe" "$root/tools/MSBuild/Current/Bin/MSBuild.exe" "$root/Contents/bin/msbuild" "$root/bin/msbuild" "$root/Contents/lib/ReSharperHost/Current/Bin/MSBuild.dll" "$root/lib/ReSharperHost/Current/Bin/MSBuild.dll"; do
      if [[ -f "$candidate" ]]; then candidates+=("$candidate"); break; fi
    done
  done
  shopt -u nullglob
  if (( ${#candidates[@]} != 1 )); then
    printf 'MSBuild candidates (%s):\n' "${#candidates[@]}" >&2
    if (( ${#candidates[@]} )); then printf '  %s\n' "${candidates[@]}" >&2; fi
    fail "Set RIDER_ROOT or RIDER_MSBUILD in $CONFIG_PATH. Copy the tool path from Rider Settings > Build, Execution, Deployment > Toolset and Build. A DLL also needs MSBUILD_RUNTIME."
  fi
  printf '%s\n' "${candidates[0]}"
}
resolve_solution() {
  local candidates=() configured
  if [[ -n "${UNITY_SOLUTION_PATH:-}" ]]; then
    configured="$(to_unix_path "$UNITY_SOLUTION_PATH")"
    [[ -f "$configured" ]] || fail "Solution missing: $configured. Edit UNITY_SOLUTION_PATH in $CONFIG_PATH."
    printf '%s\n' "$configured"; return
  fi
  shopt -s nullglob; candidates=("$UNITY_PROJECT_PATH"/*.sln); shopt -u nullglob
  (( ${#candidates[@]} == 1 )) || fail "Expected one .sln in $UNITY_PROJECT_PATH; found ${#candidates[@]}. Run unity-import-long-compile or set UNITY_SOLUTION_PATH in $CONFIG_PATH."
  printf '%s\n' "${candidates[0]}"
}
# Existing C# contents may change for the fast path; file names and all other
# inputs may not. Hash resolved packages and generated solution files as well.
project_fingerprint() (
  cd "$UNITY_PROJECT_PATH"
  native_project="$UNITY_PROJECT_PATH"
  if command -v cygpath >/dev/null 2>&1; then native_project="$(cygpath -m "$UNITY_PROJECT_PATH")"; fi
  printf '%s\n' "$(unity_version)"
  for folder in Assets Packages ProjectSettings Library/PackageCache; do
    [[ -d "$folder" ]] || continue
    [[ -z "$(find "$folder" -type l -print)" ]] || fail "Fast-build snapshots do not support links under $folder. Run unity-import-long-compile for authoritative verification."
    find "$folder" -type f ! -path 'Packages/.kissunitymcp-*/*' | LC_ALL=C sort
    find "$folder" -type f ! -name '*.cs' ! -path 'Packages/.kissunitymcp-*/*' | LC_ALL=C sort | \
      ROOT="$native_project" awk '{print ENVIRON["ROOT"] "/" $0}' | git hash-object --no-filters --stdin-paths || exit 1
  done
  for file in ./*.sln ./*.csproj; do [[ ! -f "$file" ]] || git hash-object --no-filters -- "$file"; done
)
write_import_snapshot() {
  project_fingerprint | git hash-object --stdin > "$CI_OUTPUT_DIR/ImportSnapshot.pending" || fail 'Could not snapshot Unity inputs'
  mv "$CI_OUTPUT_DIR/ImportSnapshot.pending" "$CI_OUTPUT_DIR/ImportSnapshot.txt"
}
require_import_snapshot() {
  local current
  [[ -s "$CI_OUTPUT_DIR/ImportSnapshot.txt" ]] || fail "No verified Unity import snapshot. Run scripts/unity.sh unity-import-long-compile first."
  current="$(project_fingerprint | git hash-object --stdin)"
  [[ "$current" == "$(cat "$CI_OUTPUT_DIR/ImportSnapshot.txt")" ]] || fail "Unity inputs or generated projects changed. Run scripts/unity.sh unity-import-long-compile before fast MSBuild. Full log: $CI_OUTPUT_DIR/UnityCompile.log"
}
acquire_run_lock() {
  mkdir -p "$CI_OUTPUT_DIR"
  RUN_LOCK="$UNITY_PROJECT_PATH/ProjectSettings/.kissunitymcp-run.lock"
  mkdir "$RUN_LOCK" 2>/dev/null || fail "Another verification is running or left a lock: $RUN_LOCK. Check active processes before removing it."
  trap 'rmdir "$RUN_LOCK"' EXIT
}
require_closed_editor() {
  [[ ! -e "$UNITY_PROJECT_PATH/Temp/UnityLockfile" ]] || fail "Close Unity for $UNITY_PROJECT_PATH before a headless run. Lock: $UNITY_PROJECT_PATH/Temp/UnityLockfile. If stale, confirm Unity has exited before removing it."
}
prepare_output_file() { mkdir -p -- "$(dirname -- "$1")"; : > "$1"; }
require_nonempty_file() { [[ -s "$1" ]] || fail "$2 missing or empty: $1"; }
extract_unity_diagnostics() {
  prepare_output_file "$2"
  [[ -f "$1" ]] || return 0
  awk '
    NR==FNR { if($0 ~ /Successfully (updated license|resolved entitlement details)/) licenseRecovered=1; next }
    { sub(/\r$/, ""); line=tolower($0); history[FNR%8]=$0 }
    licenseRecovered && line ~ /^\[licensing::(client|module)\] error: (handshakeresponse reported an error:|failed to handshake to channel:|access token is unavailable; failed to update)/ { next }
    # NamedPipeServer emits this closed-connection exception at warning severity.
    # The same exception without its explicit warning header remains an error.
    line ~ /^[[:space:]]+exception occured while accepting client connection: system.io.ioexception: the pipe is being closed\./ &&
      history[(FNR-1)%8] ~ /^warn: Unity\.AspNetCore\.NamedPipeSupport\.NamedPipeServer\[0\]/ { next }
    line ~ /error (cs|bc)[0-9]+|shader error in|scripts have compiler errors|compilation failed|aborting batchmode due to failure|unhandled exception|fatal error|crash!!!/ ||
    line ~ /executemethod.*(could not be found|threw exception|failed)/ ||
    line ~ /(^|[[:space:]])(error|exception):|[a-z]+exception:|importer.*failed|import(er|ing| worker)? (failed|error):|failed to (import|load|resolve)|error (importing|loading|resolving)|asset import failed/ ||
    line ~ /unityengine.debug:log(error|exception)/ ||
    line ~ /simple_unity_mcp_ci:.*failed|## script compilation error/ {
      for(i=FNR-7;i<FNR;i++) if(i>0) print i ": " history[i%8]
      print FNR ": " $0; context=8; next
    }
    context>0 { print FNR ": " $0; context-- }
  ' "$1" "$1" | awk '!seen[$0]++' > "$2"
}
print_context() {
  printf 'Unity project: %s\nConfiguration: %s\nFull logs:     %s\nUnity editor:  %s\n' "$UNITY_PROJECT_PATH" "$CONFIG_PATH" "$CI_OUTPUT_DIR" "${1:-not required}"
}
