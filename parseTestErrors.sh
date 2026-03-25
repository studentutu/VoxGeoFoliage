#!/usr/bin/env bash

set -e

# "C:\Program Files\Unity\Hub\Editor\6000.1.12f1\Editor\Unity.exe"
UNITY="/c/Program Files/Unity/Hub/Editor/6000.3.7f1/Editor/Unity.exe"
PROJECT_PATH="$(pwd)"

# C:\Users\admin\Documents\UnityProjects\xxx_unityProject\CI\CITestOutput.xml
UNITY_LOG_FILE="$PROJECT_PATH/CI/UnityLogs.log"
TEST_FILE="$PROJECT_PATH/CI/CITestOutput.xml"
UNITY_COMPILE_ERROR_FILE="$PROJECT_PATH/CI/CompileErrorsAfterUnityRun.txt"

echo
echo "Unity editor path:"
echo "$UNITY"
echo

echo "Project path:"
echo "$PROJECT_PATH"
echo

echo "TEST_FILE path:"
echo "$TEST_FILE"
echo

echo "UnityCOMPILEError_FILE path:"
echo "$UNITY_COMPILE_ERROR_FILE"
echo

echo "UnityLOG_FILE path:"
echo "$UNITY_LOG_FILE"
echo

: > "$UNITY_COMPILE_ERROR_FILE"

awk '
  { sub(/\r$/, "", $0) }
  printing { print }
  $0 == "##### ExitCode" { printing=1; print; next }
' "$UNITY_LOG_FILE" > "$UNITY_COMPILE_ERROR_FILE"

if [[ ! -s "$UNITY_COMPILE_ERROR_FILE" ]]; then
  echo "Compile succeeded: no compile time errors."
fi

echo "Check possible compile errors (if not empty): $UNITY_COMPILE_ERROR_FILE"
echo

# actual failed test (<failure>) has <stack-trace> child node as well as <message> child node
gawk -v RS='</test-case>' '
  index($0, "<failure>") == 0 { next }
  index($0, "<stack-trace>") == 0 { next }

  {
    name = "(unknown)"
    if (match($0, /fullname="([^"]*)"/, a)) name = a[1]

    msg = ""
    if (match($0, /<message><!\[CDATA\[(.*?)\]\]><\/message>/, m)) msg = m[1]
    else if (match($0, /<message>([^<]*)<\/message>/, m)) msg = m[1]

    st = ""
    if (match($0, /<stack-trace><!\[CDATA\[(.*?)\]\]><\/stack-trace>/, s)) st = s[1]
    else if (match($0, /<stack-trace>([^<]*)<\/stack-trace>/, s)) st = s[1]

    printf "-- %s --\n%s\n%s\n\n", name, msg, st
    failing++
  }

  END {
    if (failing)
      printf "❌  %d real test failure(s) found\n", failing > "/dev/stderr"
    exit (failing ? 1 : 0)
  }
' "$TEST_FILE"

# ──  count <stack-trace> (opening tag = one failure) ───
# grep returns exit code 1 when it finds zero matches, which is the success path here.
FAILS=$(grep -c '<stack-trace>' "$TEST_FILE" || true)

if [[ $FAILS -ne 0 ]]; then
  echo "$FAILS test(s) failed; details above"
  exit 2
fi

echo "All tests passed  ✔"
exit 0
