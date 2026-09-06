#!/usr/bin/env bash

set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
# shellcheck source=unity-ci-common.sh
source "$SCRIPT_DIR/unity-ci-common.sh"

require_unity_project

UNITY_EDITOR="$(resolve_unity_editor)"
export UNITY_EDITOR_PATH="$UNITY_EDITOR"
require_closed_editor
acquire_run_lock
UNITY_LOG="$(to_unix_path "${UNITY_SHADER_LOG_PATH:-$CI_OUTPUT_DIR/UnityShaders.log}")"
DIAGNOSTICS_FILE="$(to_unix_path "${UNITY_SHADER_DIAGNOSTICS_PATH:-$CI_OUTPUT_DIR/ShaderCompileErrors.txt}")"
SUCCESS_MARKER="SIMPLE_UNITY_MCP_CI:SHADER_COMPILATION_PASSED"

prepare_output_file "$UNITY_LOG"
prepare_output_file "$DIAGNOSTICS_FILE"

print_context "$UNITY_EDITOR"
printf 'Unity log:     %s\n' "$UNITY_LOG"
printf 'Diagnostics:   %s\n\n' "$DIAGNOSTICS_FILE"

set +e
"$UNITY_EDITOR" \
  -batchmode \
  -nographics \
  -stackTraceLogType Full \
  -projectPath "$UNITY_PROJECT_PATH" \
  -logFile "$UNITY_LOG" \
  -executeMethod SimpleUnityMCP.Editor.ShaderCompileTool.CompileAllProjectShaders
unity_exit_code=$?
set -e

printf '\nUnity exit code: %d\n' "$unity_exit_code"
require_nonempty_file "$UNITY_LOG" "Unity shader log"
extract_unity_diagnostics "$UNITY_LOG" "$DIAGNOSTICS_FILE"

status=0

if (( unity_exit_code != 0 )); then
  printf 'ERROR: Unity shader compilation failed with exit code %d.\n' "$unity_exit_code" >&2
  status=1
fi

if (( unity_exit_code == 0 )) && ! grep -qF "$SUCCESS_MARKER" "$UNITY_LOG"; then
  printf 'ERROR: Shader compiler did not emit its success marker.\n' >&2
  status=1
fi

if [[ -s "$DIAGNOSTICS_FILE" ]]; then
  printf 'ERROR: Shader diagnostics found in %s:\n' "$DIAGNOSTICS_FILE" >&2
  sed -n '1,200p' "$DIAGNOSTICS_FILE" >&2
  status=1
fi

if (( status != 0 )); then
  printf 'Full Unity log: %s\n' "$UNITY_LOG" >&2
  exit "$status"
fi

printf 'Unity shader compilation passed.\n'
