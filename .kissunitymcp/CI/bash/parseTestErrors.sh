#!/usr/bin/env bash

set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
# shellcheck source=unity-ci-common.sh
source "$SCRIPT_DIR/unity-ci-common.sh"

TEST_RESULTS="$(to_unix_path "${UNITY_TEST_RESULTS_PATH:-$CI_OUTPUT_DIR/CITestOutput.xml}")"
UNITY_LOG="$(to_unix_path "${UNITY_TEST_LOG_PATH:-$CI_OUTPUT_DIR/UnityTests.log}")"
DIAGNOSTICS_FILE="$(to_unix_path "${UNITY_DIAGNOSTICS_PATH:-$CI_OUTPUT_DIR/CompileErrorsAfterUnityRun.txt}")"

while (( $# > 0 )); do
  case "$1" in
    --test-results)
      (( $# >= 2 )) || fail "--test-results requires a path"
      TEST_RESULTS="$(to_unix_path "$2")"
      shift 2
      ;;
    --unity-log)
      (( $# >= 2 )) || fail "--unity-log requires a path"
      UNITY_LOG="$(to_unix_path "$2")"
      shift 2
      ;;
    *)
      fail "Unknown argument: $1"
      ;;
  esac
done

require_command awk
require_command sed
require_nonempty_file "$UNITY_LOG" "Unity test log"
require_nonempty_file "$TEST_RESULTS" "Unity test results"

extract_unity_diagnostics "$UNITY_LOG" "$DIAGNOSTICS_FILE"

root_line="$(awk -f "$SCRIPT_DIR/nunit-summary.awk" "$TEST_RESULTS")" || fail "Malformed, truncated, or inconsistent NUnit XML: $TEST_RESULTS"
[[ -n "$root_line" ]] || fail "The test result does not contain an NUnit <test-run> root: $TEST_RESULTS"

read_attribute() {
  local name="$1"
  printf '%s\n' "$root_line" | sed -n "s/.*[[:space:]]$name=\"\([^\"]*\)\".*/\1/p"
}

result="$(read_attribute result)"
total="$(read_attribute total)"
passed="$(read_attribute passed)"
failed="$(read_attribute failed)"
inconclusive="$(read_attribute inconclusive)"
skipped="$(read_attribute skipped)"

[[ "$total" =~ ^[0-9]+$ ]] || fail "Invalid or missing total count in $TEST_RESULTS"
[[ "$passed" =~ ^[0-9]+$ ]] || fail "Invalid or missing passed count in $TEST_RESULTS"
[[ "$failed" =~ ^[0-9]+$ ]] || fail "Invalid or missing failed count in $TEST_RESULTS"
[[ "$inconclusive" =~ ^[0-9]+$ ]] || fail "Invalid or missing inconclusive count in $TEST_RESULTS"
[[ "$skipped" =~ ^[0-9]+$ ]] || fail "Invalid or missing skipped count in $TEST_RESULTS"

printf 'Test summary: result=%s total=%s passed=%s failed=%s inconclusive=%s skipped=%s\n' \
  "$result" "$total" "$passed" "$failed" "$inconclusive" "$skipped"

if (( failed > 0 )) || [[ "$result" == "Failed" ]]; then
  awk '
    function decode(value) {
      gsub(/<!\[CDATA\[/, "", value)
      gsub(/\]\]>/, "", value)
      gsub(/&lt;/, "<", value)
      gsub(/&gt;/, ">", value)
      gsub(/&quot;/, "\"", value)
      gsub(/&amp;/, "\\&", value)
      return value
    }
    function between(text, opening, closing, start, tail, finish) {
      start=index(text, opening)
      if (!start) return ""
      tail=substr(text, start + length(opening))
      finish=index(tail, closing)
      return finish ? substr(tail, 1, finish - 1) : tail
    }
    function report_case(text, name,message,stack) {
      if (text !~ /result="Failed"/) return
      name="(unknown test)"
      if (match(text, /fullname="[^"]*"/))
        name=substr(text, RSTART + 10, RLENGTH - 11)
      message=decode(between(text, "<message>", "</message>"))
      stack=decode(between(text, "<stack-trace>", "</stack-trace>"))
      printf "\n-- %s --\n%s\n%s\n", name, message, stack
      found++
    }
    { document=document $0 "\n" }
    END {
      while (match(document, /<test-case[[:space:]]/)) {
        document=substr(document, RSTART)
        finish=index(document, "</test-case>")
        if (!finish) break
        report_case(substr(document, 1, finish-1))
        document=substr(document, finish+12)
      }
      if (!found)
        print "\nFailure details were not attached to a test-case; inspect the complete XML result."
    }
  ' "$TEST_RESULTS"
fi

status=0

if ! grep -q 'Test run completed\. Exiting with code' "$UNITY_LOG"; then
  printf 'ERROR: Unity log has no test completion marker: %s\n' "$UNITY_LOG" >&2
  status=1
fi

if [[ -s "$DIAGNOSTICS_FILE" ]]; then
  printf 'ERROR: Unity diagnostics found in %s:\n' "$DIAGNOSTICS_FILE" >&2
  sed -n '1,200p' "$DIAGNOSTICS_FILE" >&2
  status=1
fi

if (( total == 0 )); then
  printf 'ERROR: Unity completed without discovering tests. Add tests and ensure the project includes Unity Test Framework.\n' >&2
  status=1
fi

if (( failed > 0 )) || (( inconclusive > 0 )) || [[ "$result" != "Passed" ]]; then
  if (( status == 0 )); then status=2; fi
fi

if (( skipped > 0 )) && [[ "${FAIL_ON_SKIPPED:-0}" == "1" ]]; then
  printf 'ERROR: %d skipped test(s) are disallowed by FAIL_ON_SKIPPED=1.\n' "$skipped" >&2
  status=2
fi

if (( status == 0 )); then
  printf 'Test result and Unity log are valid.\n'
fi

exit "$status"
