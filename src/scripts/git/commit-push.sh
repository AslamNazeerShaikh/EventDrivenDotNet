#!/usr/bin/env bash
# commit-push.sh: commit paths with the project trailer, scan, then push.
#
# Used after every build step, so the progress doc and the work reach
# GitHub together (the user's standing rule since 13 November 2026).
#
# Usage:
#   src/scripts/git/commit-push.sh [--no-push] "<conventional subject>" [path ...]
#
#   - The subject follows Conventional Commits, e.g. "docs: update build progress".
#   - Paths default to docs/04-build-progress.md. Only the given paths are
#     staged, never `git add -A`, so another agent's work-in-progress is
#     never swept in. It refuses to run while anything is already staged.
#   - Runs scan-private.sh on the outgoing commits before pushing.
#   - Refuses to push to main or development; feature branches only.
#
# Exit codes:  0 committed (and pushed),  1 nothing to commit, index already
#              staged, or scan failed,
#              2 usage error or protected branch.
#
# Examples:
#   src/scripts/git/commit-push.sh "docs: update build progress (task 16 done)"
#   src/scripts/git/commit-push.sh "chore(scripts): add api probe" src/scripts/live/api.sh
set -euo pipefail

if [[ "${1:-}" == "-h" || "${1:-}" == "--help" || $# -eq 0 ]]; then sed -n '2,23p' "$0"; exit 0; fi

PUSH=1
if [[ "$1" == "--no-push" ]]; then PUSH=0; shift; fi
SUBJECT="$1"; shift
PATHS=("$@")
[[ ${#PATHS[@]} -eq 0 ]] && PATHS=(docs/04-build-progress.md)

ROOT=$(git -C "$(dirname "$0")" rev-parse --show-toplevel)
cd "$ROOT"

BRANCH=$(git branch --show-current)
case "$BRANCH" in
  main|development) echo "commit-push: refusing to work on protected branch '$BRANCH'" >&2; exit 2 ;;
esac

# The versioned hooks format C# on commit and block unformatted pushes.
"$ROOT/src/scripts/git/install-hooks.sh" --check || "$ROOT/src/scripts/git/install-hooks.sh"

# `git commit` takes the whole index, so files someone else staged would ride along.
if ! git diff --cached --quiet; then
  echo "commit-push: files are already staged (another agent's work?); commit or unstage them first:" >&2
  git diff --cached --name-only >&2; exit 1
fi
git add -- "${PATHS[@]}"
if git diff --cached --quiet; then echo "commit-push: nothing staged in: ${PATHS[*]}" >&2; exit 1; fi

git commit -q -F - <<EOF
$SUBJECT

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
echo "committed $(git log --oneline -1)"

if [[ $PUSH -eq 1 ]]; then
  "$ROOT/src/scripts/git/scan-private.sh"
  if git rev-parse --abbrev-ref --symbolic-full-name '@{upstream}' >/dev/null 2>&1; then
    git push -q
  else
    git push -q -u origin "$BRANCH"
  fi
  echo "pushed $BRANCH"
fi
