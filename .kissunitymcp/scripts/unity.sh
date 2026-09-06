#!/usr/bin/env bash
set -euo pipefail
SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
source "$SCRIPT_DIR/common.sh"
action="${1:-help}"
if [[ "$action" == help || "$action" == --help ]]; then
  printf 'Usage: bash %s <doctor|unity-import-long-compile|build|tests|shaders|parse-tests> [Unity project]\n' "$0"
  printf 'Edit <project>/.kissunitymcp/tools.env to select Unity Hub, Unity editor and Rider/MSBuild.\n'
  exit 0
fi
shift
if [[ $# -gt 0 ]]; then UNITY_PROJECT_PATH="$(to_unix_path "$1")"; shift
elif [[ -f "$SCRIPT_DIR/../../ProjectSettings/ProjectVersion.txt" ]]; then UNITY_PROJECT_PATH="$(cd "$SCRIPT_DIR/../.." && pwd -P)"
else UNITY_PROJECT_PATH="$PWD"; fi
export UNITY_PROJECT_PATH
case "$action" in
  doctor) script=doctor.sh;;
  unity-import-long-compile) script=rebuildSolutionFromUnityItself.sh;;
  build) script=rebuildSolutionWithRiderMsBuild.sh;;
  tests) script=runTestsBash.sh;;
  shaders) script=compileShaders.sh;;
  parse-tests) script=parseTestErrors.sh;;
  *) fail "Unknown action: $action. Run bash $0 help";;
esac
exec bash "$SCRIPT_DIR/../CI/bash/$script" "$@"
