#!/usr/bin/env bash
# Incremental C# follow-up. Unity import remains authoritative for assets.
set -euo pipefail
source "$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)/unity-ci-common.sh"
require_unity_project
UNITY_EDITOR_PATH="$(resolve_unity_editor)"; export UNITY_EDITOR_PATH
SOLUTION="$(resolve_solution)"
MSBUILD="$(resolve_msbuild)"
command_args=("$MSBUILD")
case "$MSBUILD" in
  *.dll)
    [[ -n "${MSBUILD_RUNTIME:-}" ]] || fail "Set MSBUILD_RUNTIME in $CONFIG_PATH for $MSBUILD"
    runtime="$(to_unix_path "$MSBUILD_RUNTIME")"
    [[ -x "$runtime" ]] || fail "MSBuild runtime not executable: $runtime. Edit $CONFIG_PATH."
    command_args=("$runtime" "$MSBUILD");;
  *.exe)
    case "$(uname -s)" in
      MINGW*|MSYS*|CYGWIN*) ;;
      *) [[ -n "${MSBUILD_RUNTIME:-}" ]] || fail "MSBuild.exe needs a compatible Mono runtime on this platform. Set MSBUILD_RUNTIME in $CONFIG_PATH."
         command_args=("$(to_unix_path "$MSBUILD_RUNTIME")" "$MSBUILD");;
    esac;;
  *) [[ -x "$MSBUILD" ]] || fail "MSBuild launcher not executable: $MSBUILD";;
esac
acquire_run_lock
require_import_snapshot
RIDER_LOG="$CI_OUTPUT_DIR/RiderMsBuild.log"
CONSOLE_LOG="$CI_OUTPUT_DIR/RiderMsBuild.console.log"
DIAGNOSTICS_FILE="$CI_OUTPUT_DIR/MsBuildErrors.txt"
prepare_output_file "$RIDER_LOG"; prepare_output_file "$CONSOLE_LOG"; prepare_output_file "$DIAGNOSTICS_FILE"
SOLUTION_NATIVE="$SOLUTION"; LOG_NATIVE="$RIDER_LOG"
if command -v cygpath >/dev/null 2>&1; then
  SOLUTION_NATIVE="$(cygpath -w "$SOLUTION")"; LOG_NATIVE="$(cygpath -w "$RIDER_LOG")"
  # Keep the executable in Git Bash form; native tools only need native arguments.
  if (( ${#command_args[@]} > 1 )); then command_args[1]="$(cygpath -w "${command_args[1]}")"; fi
fi
print_context "$UNITY_EDITOR_PATH"
printf 'Solution: %s\nMSBuild: %s\nFull diagnostic log: %s\n' "$SOLUTION" "$MSBUILD" "$RIDER_LOG"
set +e
MSYS2_ARG_CONV_EXCL='*' "${command_args[@]}" "$SOLUTION_NATIVE" /t:Build /m /v:minimal /nologo \
  /p:Configuration=Debug '/p:Platform=Any CPU' /fl "/flp:LogFile=$LOG_NATIVE;Verbosity=diagnostic;Encoding=UTF-8" \
  2>&1 | tee "$CONSOLE_LOG"
pipeline_status=("${PIPESTATUS[@]}")
set -e
printf 'MSBuild process exit: %s; console capture exit: %s\n' "${pipeline_status[0]}" "${pipeline_status[1]}"
require_nonempty_file "$RIDER_LOG" 'Full MSBuild diagnostic log'
awk '{ sub(/\r$/, ""); line=tolower($0) } line ~ /: error [a-z]+[0-9]+:|error msb[0-9]+:|^build failed/ {print}' "$RIDER_LOG" > "$DIAGNOSTICS_FILE"
if (( pipeline_status[0] != 0 || pipeline_status[1] != 0 )) || [[ -s "$DIAGNOSTICS_FILE" ]]; then
  sed -n '1,160p' "$DIAGNOSTICS_FILE" >&2
  fail "MSBuild verification failed. Full log: $RIDER_LOG; console: $CONSOLE_LOG"
fi
grep -q 'Build succeeded\.' "$RIDER_LOG" || fail "MSBuild success summary missing from $RIDER_LOG"
printf 'SIMPLE_UNITY_MCP_CI:MSBUILD_PASSED\n'
