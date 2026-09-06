#!/usr/bin/env bash
set -euo pipefail
TOOLS_SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
fail() { printf 'ERROR: %s\n' "$*" >&2; exit 1; }
to_unix_path() {
  if command -v cygpath >/dev/null 2>&1; then cygpath -u "$1"; else printf '%s\n' "$1"; fi
}
json_get() { LC_ALL=C QUERY="${2:-}" MODE="${3:-value}" awk -f "$TOOLS_SCRIPT_DIR/json.awk" "$1"; }
json_quote() {
  LC_ALL=C awk 'BEGIN { printf "\""; for(i=1;i<32;i++) esc[sprintf("%c",i)]=sprintf("\\u%04x",i) }
    { if(NR>1) printf "\\n"; for(i=1;i<=length($0);i++) { c=substr($0,i,1); if(c=="\\" || c=="\"") printf "\\%s",c; else if(c in esc) printf "%s",esc[c]; else printf "%s",c } }
    END { printf "\"" }'
}
quote() { printf '%s' "$1" | json_quote; }
require_project() {
  local part
  [[ -d "$1" ]] || fail "Unity project does not exist: $1"
  PROJECT="$(cd -- "$1" && pwd -P)"
  for part in Assets Packages ProjectSettings; do
    [[ -d "$PROJECT/$part" && ! -L "$PROJECT/$part" ]] || fail "Expected a real directory: $PROJECT/$part"
  done
  [[ -f "$PROJECT/ProjectSettings/ProjectVersion.txt" ]] || fail "Missing $PROJECT/ProjectSettings/ProjectVersion.txt"
  PROJECT_VERSION="$(sed -n 's/^m_EditorVersion:[[:space:]]*//p' "$PROJECT/ProjectSettings/ProjectVersion.txt" | tr -d '\r')"
  [[ "$PROJECT_VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+[abfp][0-9]+$ ]] || fail "Invalid m_EditorVersion in $PROJECT/ProjectSettings/ProjectVersion.txt"
}
# Git is already required by marketplace installs and Git Bash. Hash bytes, not
# timestamps or Git filters. Reject links and special files before any copy.
tree_digest() (
  cd -- "$1"
  [[ -z "$(find . ! -type d ! -type f -print)" ]] || fail "Links/special files are unsupported in $1"
  [[ -z "$(find . -name '*
*' -print)" ]] || fail "Newlines in filenames are unsupported in $1"
  while IFS= read -r file; do
    [[ "$file" != *$'\r'* ]] || fail "Unsupported filename: $file"
    printf '%s\0' "$file"
    git hash-object --no-filters -- "$file" || exit 1
  done < <(find . -type f | LC_ALL=C sort) | git hash-object --stdin
)
