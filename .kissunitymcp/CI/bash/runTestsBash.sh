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
TEST_RESULTS="$(to_unix_path "${UNITY_TEST_RESULTS_PATH:-$CI_OUTPUT_DIR/CITestOutput.xml}")"
UNITY_LOG="$(to_unix_path "${UNITY_TEST_LOG_PATH:-$CI_OUTPUT_DIR/UnityTests.log}")"

prepare_output_file "$TEST_RESULTS"
prepare_output_file "$UNITY_LOG"

print_context "$UNITY_EDITOR"
printf 'Test results:  %s\n' "$TEST_RESULTS"
printf 'Unity log:     %s\n\n' "$UNITY_LOG"

set +e
"$UNITY_EDITOR" \
  -batchmode \
  -nographics \
  -stackTraceLogType Full \
  -runTests \
  -projectPath "$UNITY_PROJECT_PATH" \
  -logFile "$UNITY_LOG" \
  -testPlatform "${UNITY_TEST_PLATFORM:-EditMode}" \
  -testResultsVersion 2 \
  -testResults "$TEST_RESULTS"
unity_exit_code=$?
set -e

printf '\nUnity exit code: %d\n' "$unity_exit_code"

set +e
bash "$SCRIPT_DIR/parseTestErrors.sh" \
  --test-results "$TEST_RESULTS" \
  --unity-log "$UNITY_LOG"
parse_exit_code=$?
set -e

if (( parse_exit_code != 0 )); then
  exit "$parse_exit_code"
fi

if (( unity_exit_code != 0 )); then
  fail "Unity returned $unity_exit_code even though the result parser found no test failure. Inspect $UNITY_LOG"
fi

printf 'Unity tests passed.\n'
