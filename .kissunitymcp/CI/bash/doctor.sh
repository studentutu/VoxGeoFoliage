#!/usr/bin/env bash
set -euo pipefail
source "$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)/unity-ci-common.sh"
require_unity_project
UNITY_EDITOR_PATH="$(resolve_unity_editor)"; export UNITY_EDITOR_PATH
print_context "$UNITY_EDITOR_PATH"
MSBUILD="$(resolve_msbuild)"
printf 'MSBuild:       %s\n' "$MSBUILD"
if [[ "$MSBUILD" == *.dll ]]; then
  [[ -n "${MSBUILD_RUNTIME:-}" && -x "$(to_unix_path "$MSBUILD_RUNTIME")" ]] || fail "MSBuild DLL requires an executable MSBUILD_RUNTIME in $CONFIG_PATH: $MSBUILD"
  printf 'Runtime:       %s\n' "$MSBUILD_RUNTIME"
fi
printf 'TOOL_PATHS_VERIFIED\n'
