#!/usr/bin/env bash
# scan-private.sh: scan outgoing commits for private data before a push.
#
# The repository is public. This blocks e-mail addresses, phone numbers,
# confidentiality notices and private keys from leaving the machine.
# The project's commit trailer address (noreply@anthropic.com) is allowed.
#
# Usage:
#   src/scripts/git/scan-private.sh [RANGE]
#
# RANGE defaults to the commits not yet on the upstream branch
# (@{upstream}..HEAD). Without an upstream it falls back to
# origin/development..HEAD.
#
# Exit codes:  0 clean,  1 something found (matching lines are printed),
#              2 usage error.
#
# Examples:
#   src/scripts/git/scan-private.sh
#   src/scripts/git/scan-private.sh origin/development..HEAD
set -euo pipefail

if [[ "${1:-}" == "-h" || "${1:-}" == "--help" ]]; then sed -n '2,23p' "$0"; exit 0; fi

ROOT=$(git -C "$(dirname "$0")" rev-parse --show-toplevel)
cd "$ROOT"

RANGE="${1:-}"
if [[ -z "$RANGE" ]]; then
  if git rev-parse --abbrev-ref --symbolic-full-name '@{upstream}' >/dev/null 2>&1; then
    RANGE='@{upstream}..HEAD'
  else
    RANGE='origin/development..HEAD'
  fi
fi

# Only added lines can leak something new. This file is excluded because it
# necessarily contains the patterns it searches for.
added=$(git diff "$RANGE" -- . ':(exclude)src/scripts/git/scan-private.sh' | grep -E '^\+' | grep -v -E '^\+\+\+ ' || true)

patterns='[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.(com|net|org|io|in|co|dev)'
patterns+='|\+91[ -]?[0-9]{5}'
patterns+='|confidential'
patterns+='|BEGIN [A-Z ]*PRIVATE KEY'
patterns+='|AKIA[0-9A-Z]{16}'

hits=$(printf '%s\n' "$added" | grep -n -i -E "$patterns" | grep -v -i 'noreply@anthropic\.com' || true)

if [[ -n "$hits" ]]; then
  echo "scan-private: possible private data in $RANGE:" >&2
  printf '%s\n' "$hits" | head -20 >&2
  exit 1
fi
echo "scan-private: clean ($RANGE)"
