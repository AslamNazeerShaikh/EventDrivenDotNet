#!/usr/bin/env bash
# sdd.sh: shortcuts for the task-by-task build of docs/03-implementation-plan.md.
#
# Wraps the superpowers subagent-driven-development helper scripts when they
# are installed, and falls back to plain git and awk when they are not
# (e.g. under opencode). Artifacts go to the git-ignored workspace
# .superpowers/sdd/03-implementation-plan/.
#
# Usage:
#   src/scripts/sdd/sdd.sh brief <N>             extract task N's text (e.g. 16, 7b, R1) to task-N-brief.md
#   src/scripts/sdd/sdd.sh review <BASE> <HEAD>  write review-<BASE>..<HEAD>.diff (commits + stat + diff)
#   src/scripts/sdd/sdd.sh log "<line>"          append a line to the ledger (progress.md)
#   src/scripts/sdd/sdd.sh constraints           refresh global-constraints.md from doc 03's Global Constraints
#   src/scripts/sdd/sdd.sh tail [N]              show the last N ledger lines (default 15)
#   src/scripts/sdd/sdd.sh dir                   print the workspace directory
#
# Each command prints the path it wrote, so it can be pasted into a dispatch.
#
# Examples:
#   src/scripts/sdd/sdd.sh brief 16
#   src/scripts/sdd/sdd.sh review f3e0975 bbb47ca
#   src/scripts/sdd/sdd.sh log "- Task 16: complete (commits a..b, review clean)"
set -euo pipefail

usage() { sed -n '2,25p' "$0"; exit "${1:-0}"; }
[[ $# -eq 0 || "$1" == "-h" || "$1" == "--help" ]] && usage

ROOT=$(git -C "$(dirname "$0")" rev-parse --show-toplevel)
PLAN="$ROOT/docs/03-implementation-plan.md"
WS="$ROOT/.superpowers/sdd/03-implementation-plan"
SK="${SDD_SKILL_DIR:-$HOME/.claude/plugins/cache/superpowers-dev/superpowers/7.0.2/skills/subagent-driven-development}"
mkdir -p "$WS"
[[ -f "$ROOT/.superpowers/sdd/.gitignore" ]] || printf '*\n' > "$ROOT/.superpowers/sdd/.gitignore"

cmd="$1"; shift
case "$cmd" in
  brief)
    n="${1:?task number}"
    if [[ -x "$SK/scripts/task-brief" || -f "$SK/scripts/task-brief" ]]; then
      bash "$SK/scripts/task-brief" "$PLAN" "$n"
    else
      out="$WS/task-$n-brief.md"
      # From "### Task N:" (or "### Stretch N:"/"### Task RN:") to the next "### " or "## " heading.
      awk -v n="$n" '
        $0 ~ "^### (Task|Stretch) " n ":" { on = 1; print; next }
        on && /^##+ / { exit }
        on { print }' "$PLAN" > "$out"
      [[ -s "$out" ]] || { echo "sdd brief: no task '$n' in $PLAN" >&2; rm -f "$out"; exit 1; }
      echo "wrote $out: $(wc -l < "$out" | tr -d ' ') lines"
    fi ;;
  review)
    base="${1:?BASE}"; head="${2:?HEAD}"
    if [[ -f "$SK/scripts/review-package" ]]; then
      (cd "$ROOT" && bash "$SK/scripts/review-package" "$PLAN" "$base" "$head")
    else
      out="$WS/review-$base..$head.diff"
      { git -C "$ROOT" log --oneline "$base..$head"; echo; git -C "$ROOT" diff --stat "$base..$head"; echo; git -C "$ROOT" diff -U10 "$base..$head"; } > "$out"
      echo "wrote $out: $(git -C "$ROOT" rev-list --count "$base..$head") commit(s), $(wc -c < "$out" | tr -d ' ') bytes"
    fi ;;
  log)
    printf '%s\n' "${1:?line}" >> "$WS/progress.md"; echo "logged to $WS/progress.md" ;;
  constraints)
    awk '/^## Global Constraints/{f=1} /^## Parallel lanes/{f=0} f' "$PLAN" > "$WS/global-constraints.md"
    echo "wrote $WS/global-constraints.md: $(wc -l < "$WS/global-constraints.md" | tr -d ' ') lines" ;;
  tail)
    tail -n "${1:-15}" "$WS/progress.md" ;;
  dir)
    echo "$WS" ;;
  *) echo "sdd: unknown command '$cmd'" >&2; usage 2 ;;
esac
