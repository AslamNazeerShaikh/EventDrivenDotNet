#!/usr/bin/env bash
# test-backend.sh: run the .NET test suites and build, with a flaky-test finder.
#
# Runs from src/backend/, where global.json pins SDK 11.0.100 and selects
# Microsoft.Testing.Platform. Prints one summary line per run, plus the names
# of any failing tests.
#
# Usage:
#   src/scripts/build/test-backend.sh [unit|integration|build|all] [--repeat N]
#
#   unit         tests/EventDrivenDesign.Tests (fast, no Docker)
#   integration  tests/EventDrivenDesign.IntegrationTests (needs Docker: Testcontainers)
#   build        dotnet build EventDrivenDesign.slnx (warnings are errors)
#   all          build, then unit, then integration (the default)
#   --repeat N   run the chosen suite N times. Use it to catch a flaky test;
#                the names of tests that fail in any run are listed at the end.
#
# Exit code: 0 when every run passed; otherwise 1.
#
# Examples:
#   src/scripts/build/test-backend.sh unit
#   src/scripts/build/test-backend.sh unit --repeat 5
#   src/scripts/build/test-backend.sh all
set -euo pipefail

[[ "${1:-}" == "-h" || "${1:-}" == "--help" ]] && { sed -n '2,25p' "$0"; exit 0; }

what="${1:-all}"; shift || true
repeat=1
if [[ "${1:-}" == "--repeat" ]]; then repeat="${2:?count}"; fi

ROOT=$(git -C "$(dirname "$0")" rev-parse --show-toplevel)
cd "$ROOT/src/backend"

UNIT=tests/EventDrivenDesign.Tests/EventDrivenDesign.Tests.csproj
INTEG=tests/EventDrivenDesign.IntegrationTests/EventDrivenDesign.IntegrationTests.csproj
status=0
failed_names=""

run_suite() { # $1 = label, $2 = project
  local i out
  for ((i = 1; i <= repeat; i++)); do
    out=$(dotnet test --project "$2" 2>&1) || status=1
    printf '%-12s run %d: %s\n' "$1" "$i" "$(printf '%s\n' "$out" | grep -E 'total:|failed:|succeeded:' | tr -s ' ' | tr '\n' ' ')"
    failed_names+=$(printf '%s\n' "$out" | grep -E '^[[:space:]]*failed ' | sed -E 's/^[[:space:]]*failed //' || true)$'\n'
  done
}

case "$what" in
  build) dotnet build EventDrivenDesign.slnx -nologo 2>&1 | tail -3 || status=1 ;;
  unit) run_suite unit "$UNIT" ;;
  integration) run_suite integration "$INTEG" ;;
  all)
    dotnet build EventDrivenDesign.slnx -nologo 2>&1 | tail -3 || status=1
    run_suite unit "$UNIT"; run_suite integration "$INTEG" ;;
  *) echo "test-backend: unknown target '$what'" >&2; exit 2 ;;
esac

names=$(printf '%s' "$failed_names" | grep -v '^\s*$' | sort | uniq -c || true)
if [[ -n "$names" ]]; then echo "failing tests (count across runs):"; echo "$names"; status=1; fi
exit $status
