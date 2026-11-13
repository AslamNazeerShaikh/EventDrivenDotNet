#!/usr/bin/env bash
# check.sh: verify diagrams with the diagram-design skill, then export them.
#
# For each diagram (docs/diagrams/<slug>.html) runs the skill's
#   self_check.py       accessible SVG, single-file safety, content in the viewBox, text fit
#   verify_geometry.py  connector rules: orthogonal, label gaps, no overlaps, masks
# and optionally exports the SVG that the docs embed, or renders review PNGs.
# PNGs are for looking at only; never commit them.
#
# Usage:
#   src/scripts/diagrams/check.sh [--export] [--png DIR] [slug ...]
#
#   slug       e.g. 01-plant-to-central (default: every docs/diagrams/*.html)
#   --export   write docs/diagrams/<slug>.svg after the checks pass
#   --png DIR  write DIR/<slug>.png (needs Playwright)
#
# Exit code: 0 when every diagram passes both checks; otherwise 1.
#
# Examples:
#   src/scripts/diagrams/check.sh
#   src/scripts/diagrams/check.sh --export 01-plant-to-central 05-duplicates
#   src/scripts/diagrams/check.sh --png /tmp/review 01-plant-to-central
set -euo pipefail

[[ "${1:-}" == "-h" || "${1:-}" == "--help" ]] && { sed -n '2,23p' "$0"; exit 0; }

ROOT=$(git -C "$(dirname "$0")" rev-parse --show-toplevel)
S="$ROOT/.claude/skills/diagram-design/scripts"
D="$ROOT/docs/diagrams"
export_svg=0; png_dir=""; slugs=()
while [[ $# -gt 0 ]]; do
  case "$1" in
    --export) export_svg=1 ;;
    --png) png_dir="${2:?dir}"; shift ;;
    *) slugs+=("$1") ;;
  esac
  shift
done
if [[ ${#slugs[@]} -eq 0 ]]; then
  for f in "$D"/*.html; do slugs+=("$(basename "$f" .html)"); done
fi

status=0
for s in "${slugs[@]}"; do
  f="$D/$s.html"
  [[ -f "$f" ]] || { echo "missing: $f" >&2; status=1; continue; }
  if python3 "$S/self_check.py" "$f" >/dev/null && python3 "$S/verify_geometry.py" "$f" >/dev/null; then
    echo "ok      $s"
    [[ $export_svg -eq 1 ]] && python3 "$S/export_svg.py" "$f" "$D/$s.svg" >/dev/null && echo "  svg   $D/$s.svg"
    if [[ -n "$png_dir" ]]; then mkdir -p "$png_dir"; python3 "$S/export_png.py" "$f" "$png_dir/$s.png" >/dev/null && echo "  png   $png_dir/$s.png"; fi
  else
    echo "FAILED  $s (run: python3 $S/self_check.py $f; python3 $S/verify_geometry.py $f)"
    status=1
  fi
done
exit $status
