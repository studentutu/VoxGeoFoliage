#!/usr/bin/env bash

# make sure all open editor instances of the current project is closed before running this script! 
# set -x
set -e

# "C:\Program Files\Unity\Hub\Editor\6000.1.12f1\Editor\Unity.exe"
UNITY="/c/Program Files/Unity/Hub/Editor/6000.3.7f1/Editor/Unity.exe"

PROJECT_PATH="$(pwd)" 
# .log are ignore by .gitignore, so that git process will not interfere with writing into it (required, otherwise it will lock the file).
UnityLOG_FILE="$PROJECT_PATH/CI/UnityLogs.log"
UnityCOMPILEError_FILE="$PROJECT_PATH/CI/CompileErrorsAfterUnityRun.txt"

echo
echo "Unity editor path:"
echo "$UNITY"
echo

echo "Project path:"
echo "$PROJECT_PATH"
echo

echo "UnityCOMPILEError_FILE path:"
echo "$UnityCOMPILEError_FILE"
echo

echo "UnityLOG_FILE path:"
echo "$UnityLOG_FILE"
echo

: > "$UnityLOG_FILE"
: > "$UnityCOMPILEError_FILE"


# run unity.exe editor tests
"$UNITY" \
  -batchmode \
  -nographics \
  -silent-crashes \
  -ignorecompilererrors \
  -projectPath "$PROJECT_PATH" \
  -logFile "$UnityLOG_FILE" \
  -executeMethod CiTools.RegenerateProjectFilesAndExit \
  -quit;

echo "Waiting log..."
echo "Waiting log..."

# push everything after ##### ExitCode
# Print FROM the exact line "##### ExitCode" (inclusive) to the end of the file.
# Normalize CRLF to LF so matching is reliable on Windows-generated logs.
if [[ ! -f "$UnityLOG_FILE" ]]; then
  echo "Unity log file not found: $UnityLOG_FILE"
  exit 1
fi

if [[ ! -s "$UnityLOG_FILE" ]]; then
  echo "Unity log file is empty: $UnityLOG_FILE"
fi

if [[ ! -f "$UnityCOMPILEError_FILE" ]]; then
  echo "Compile error file not found: $UnityCOMPILEError_FILE"
  exit 1
fi

echo "Compile error file can be found at path: $UnityCOMPILEError_FILE"
echo "Waiting log..."
echo "Waiting log..."

# awk '
#   { sub(/\r$/, "", $0) }                     # remove trailing \r if present
#   printing { print }                         # once we start, print every line
#   $0 == "##### ExitCode" { printing=1; print; next }  # include the marker line too
# ' "$UnityLOG_FILE" > "$UnityCOMPILEError_FILE"

# Extract C# compile errors and Unity script compilation error blocks (CRLF-safe).
awk '
  { sub(/\r$/, "", $0) }
  script_error_lines_remaining > 0 { print; script_error_lines_remaining--; next }
  index($0, "## Script Compilation Error") == 1 { print; script_error_lines_remaining=49; next }
  tolower($0) ~ /error cs/ { print }
' "$UnityLOG_FILE" > "$UnityCOMPILEError_FILE"

echo "Waiting write..."
echo "Waiting write..."

if [[ -s "$UnityCOMPILEError_FILE" ]]; then
  echo "Compile errors found in Unity log; dumped to $UnityCOMPILEError_FILE"
  exit 1
else
  echo "Compile succeeded: no compile time errors."
fi
