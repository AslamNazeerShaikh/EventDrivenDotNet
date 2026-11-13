#!/usr/bin/env bash
# test-frontend.sh: run the dashboard's unit tests and its strict build.
#
# Runs `bun test` (the pure logic in src/frontend/src/format.ts and derive.ts), then
# `bun run build` (tsc -b with "strict": true, then the Vite build). Installs
# dependencies from bun.lock first if node_modules is missing.
#
# Usage:
#   src/scripts/build/test-frontend.sh [test|build|all]   (default: all)
#
# Exit code: 0 when everything passed; otherwise the failing step's code.
#
# Examples:
#   src/scripts/build/test-frontend.sh
#   src/scripts/build/test-frontend.sh test
set -euo pipefail

[[ "${1:-}" == "-h" || "${1:-}" == "--help" ]] && { sed -n '2,16p' "$0"; exit 0; }

ROOT=$(git -C "$(dirname "$0")" rev-parse --show-toplevel)
cd "$ROOT/src/frontend"
[[ -d node_modules ]] || bun install --frozen-lockfile

case "${1:-all}" in
  test) bun test ;;
  build) bun run build ;;
  all) bun test && bun run build ;;
  *) echo "test-frontend: unknown target '$1'" >&2; exit 2 ;;
esac
