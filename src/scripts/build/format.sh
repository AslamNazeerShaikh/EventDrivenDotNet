#!/usr/bin/env bash
# format.sh: format the C# codebase with CSharpier (or only check it).
#
# CSharpier is pinned as a local .NET tool in src/backend/dotnet-tools.json
# (1.4.0, rollForward on, so it runs on the .NET 11 runtime). The first run
# restores it automatically. Defaults are used (no .csharpierrc), and
# bin/ and obj/ are skipped.
#
# The git hooks call this script (src/scripts/git/hooks/):
#   pre-commit  formats the staged .cs files and re-stages them
#   pre-push    runs --check on the whole codebase and blocks the push if
#               anything is unformatted
#
# Usage:
#   src/scripts/build/format.sh [--check] [FILE ...]
#
#   (no args)   format every .cs file under src/backend/
#   --check     report unformatted files and exit 1 if any; changes nothing
#   FILE ...    format (or check) only these files (paths relative to the repo root)
#
# Examples:
#   src/scripts/build/format.sh
#   src/scripts/build/format.sh --check
#   src/scripts/build/format.sh src/backend/EventDrivenDesign.Api/Program.cs
set -euo pipefail

[[ "${1:-}" == "-h" || "${1:-}" == "--help" ]] && { sed -n '2,26p' "$0"; exit 0; }

ROOT=$(git -C "$(dirname "$0")" rev-parse --show-toplevel)
mode=format
if [[ "${1:-}" == "--check" ]]; then mode=check; shift; fi

cd "$ROOT/src/backend"
dotnet tool restore >/dev/null

if [[ $# -eq 0 ]]; then
  targets=(.)
else
  targets=()
  for f in "$@"; do targets+=("$ROOT/$f"); done
fi

if [[ $mode == check ]]; then
  if out=$(dotnet csharpier check "${targets[@]}" 2>&1); then
    echo "format: C# is formatted ($(printf '%s\n' "$out" | grep -E '^Checked' || echo 'ok'))"
  else
    printf '%s\n' "$out" | grep -E '^Error' | sed 's/^Error /unformatted: /' >&2
    echo "format: run src/scripts/build/format.sh, then commit the result" >&2
    exit 1
  fi
else
  dotnet csharpier format "${targets[@]}" | tail -1
fi
