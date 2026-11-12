#!/usr/bin/env bash
# api.sh: talk to the running control-room API from the host.
#
# Goes through the dashboard's nginx on http://localhost:3000, which proxies
# /api/* to the API. Override with API_BASE (e.g. API_BASE=http://localhost:8080).
# JSON is pretty-printed with python3, so jq is not needed.
#
# Usage:
#   src/scripts/live/api.sh overview                  pipeline tiles, lag, ETA (compact)
#   src/scripts/live/api.sh proof [full]              reconciliation summary ("full" adds the duplicate-row scan)
#   src/scripts/live/api.sh alarms                    the latest central alarms
#   src/scripts/live/api.sh pause | resume            pause or resume all five gateways
#   src/scripts/live/api.sh fault <MACHINE> <KIND>    inject a fault, e.g. P1-BDY-W042 Overheat
#   src/scripts/live/api.sh watch-proof <SECONDS>     sample the verdict every second, then count PASS/WAIT/FAIL
#   src/scripts/live/api.sh raw <PATH>                GET any path and print the JSON as-is
#
# Exit code: 0 on success; non-zero on HTTP or connection errors.
#
# Examples:
#   src/scripts/live/api.sh pause && sleep 40 && src/scripts/live/api.sh proof full
#   src/scripts/live/api.sh watch-proof 60      # the verdict must stay PASS while settled
set -euo pipefail

[[ $# -eq 0 || "$1" == "-h" || "$1" == "--help" ]] && { sed -n '2,24p' "$0"; exit 0; }
BASE="${API_BASE:-http://localhost:3000}"
get()  { curl -fsS --max-time 10 "$BASE$1"; }
post() { curl -fsS --max-time 10 -X POST -H 'Content-Type: application/json' --data "${2:-}" "$BASE$1" -o /dev/null -w '%{http_code}\n'; }

summary_py='
import json, sys
d = json.load(sys.stdin)
keys = ["verdict","paused","drained","generated","stored","missing","gaps","duplicateRows","duplicatesAbsorbed"]
print("  ".join(f"{k}={d.get(k)}" for k in keys))'

overview_py='
import json, sys
o = json.load(sys.stdin)
h = lambda t: (t or {}).get("health", {}).get("$type", "?")
gw = " ".join(f"{g[\"shop\"]}:{g[\"health\"][\"$type\"][0]}{\"(p)\" if g[\"paused\"] else \"\"}/{g[\"bufferedReadings\"]}" for g in o["gateways"])
print(f"gateways {gw}")
print(f"plantKafka={h(o[\"plantKafka\"])} centralKafka={h(o[\"centralKafka\"])} database={h(o[\"database\"])} consumers={[c[\"health\"][\"$type\"] for c in o[\"consumers\"]]}")
print(f"liveRate={o[\"liveRate\"]:.1f} drainRate={o[\"drainRate\"]:.1f} mirrorLag={o[\"mirrorLagSeconds\"]} consumerLag={o[\"consumerLag\"]} drainEta={o[\"drainEtaSeconds\"]}")'

case "$1" in
  overview) get /api/overview | python3 -c "$overview_py" ;;
  proof) q=""; [[ "${2:-}" == "full" ]] && q="?full=true"; get "/api/reconciliation$q" | python3 -c "$summary_py" ;;
  alarms) get /api/alarms | python3 -c 'import json,sys
for a in json.load(sys.stdin)[:15]: print(a["storedAt"][11:19], a["machineId"], a["kind"], a["severity"], f"{a[\"latencySeconds\"]:.1f}s")' ;;
  pause|resume) printf 'POST /api/plant/%s -> ' "$1"; post "/api/plant/$1" ;;
  fault) printf 'POST fault %s %s -> ' "${2:?machine}" "${3:?kind}"; post "/api/machines/$2/faults" "{\"kind\":\"$3\"}" ;;
  watch-proof)
    n="${2:?seconds}"; tally=""
    for ((i = 1; i <= n; i++)); do
      v=$(get /api/reconciliation 2>/dev/null | python3 -c 'import json,sys; print(json.load(sys.stdin)["verdict"])' 2>/dev/null || echo ERROR)
      printf '%3d %s\n' "$i" "$v"; tally+="$v"$'\n'; sleep 1
    done
    echo "counts:"; printf '%s' "$tally" | sort | uniq -c ;;
  raw) get "${2:?path}"; echo ;;
  *) echo "api.sh: unknown command '$1'" >&2; exit 2 ;;
esac
