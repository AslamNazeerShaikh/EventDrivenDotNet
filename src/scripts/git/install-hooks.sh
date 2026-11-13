#!/usr/bin/env bash
# install-hooks.sh: use the repo's versioned git hooks (src/scripts/git/hooks/).
#
# Sets core.hooksPath for this clone, which activates:
#   pre-commit  CSharpier formats the staged .cs files and re-stages them
#   pre-push    blocks a push while any C# file is unformatted
# Idempotent. Run it once after cloning, in every clone (Claude Code, opencode, CI).
#
# Usage:
#   src/scripts/git/install-hooks.sh          install
#   src/scripts/git/install-hooks.sh --check  exit 0 when installed, 1 when not
set -euo pipefail

[[ "${1:-}" == "-h" || "${1:-}" == "--help" ]] && { sed -n '2,12p' "$0"; exit 0; }

ROOT=$(git -C "$(dirname "$0")" rev-parse --show-toplevel)
want="src/scripts/git/hooks"

if [[ "${1:-}" == "--check" ]]; then
  [[ "$(git -C "$ROOT" config --get core.hooksPath || true)" == "$want" ]]
  exit $?
fi

chmod +x "$ROOT/$want"/*
git -C "$ROOT" config core.hooksPath "$want"
echo "install-hooks: core.hooksPath = $want (pre-commit formats C#, pre-push checks it)"
