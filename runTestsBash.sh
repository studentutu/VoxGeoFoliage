#!/usr/bin/env bash

# set -x
set -e

# C:\Program Files\Unity\Hub\Editor\6000.1.12f1\Editor
# "C:\Program Files\Unity\Hub\Editor\6000.1.12f1\Editor\Unity.exe"
UNITY="/c/Program Files/Unity/Hub/Editor/6000.3.7f1/Editor/Unity.exe"

PROJECT_PATH="$(pwd)" 
TEST_FILE="$PROJECT_PATH/CI/CITestOutput.xml"
UnityLOG_FILE="$PROJECT_PATH/CI/UnityLogs.log"
UnityCOMPILEError_FILE="$PROJECT_PATH/CI/CompileErrorsAfterUnityRun.txt"

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

echo "UnityLOG_FILE path:"
echo "$UnityLOG_FILE"
echo

echo "UnityCOMPILEError_FILE path:"
echo "$UnityCOMPILEError_FILE"
echo

# clear files
: > "$TEST_FILE"
: > "$UnityLOG_FILE"
: > "$UnityCOMPILEError_FILE"

# run unity.exe editor tests
"$UNITY" \
  -runTests \
  -batchmode \
  -nographics \
  -projectPath "$PROJECT_PATH" \
  -logFile "$UnityLOG_FILE" \
  -testPlatform EditMode \
  -testResultsVersion 2 \
  -testResults "$TEST_FILE";

# Somehow when tests are failed, Unity will not finish process, so we need to look into the actual TEST_FILE output for failed tests
UNITY_EXIT_CODE=$?

echo
echo "Unity exit code $UNITY_EXIT_CODE"
echo

# ── 3. count <stack-trace> (opening tag = one failure) ───────────────────────────
FAILS=$(grep -c '<stack-trace>' "$TEST_FILE")
echo " $FAILS test(s) failed"
echo

if [[ $FAILS -ne 0 ]]; then
  # print all the failures
  bash ./parseTestErrors.sh
  echo "$FAILS test(s) failed – details above"
  echo

  exit 2 # Unity has exit code 2 for failed tests
fi

echo "All tests passed ✔"
echo

exit $UNITY_EXIT_CODE