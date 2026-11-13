#!/usr/bin/env bash
# rehearse.sh: run the live demo once, end to end, and print a results table.
#
# Follows the demo script (doc 00 §14) on the running stack and checks each step:
#   1 steady state: 21 containers, 5 healthy shops with an active gateway, about 500 readings/s
#   2 central-down for OUTAGE seconds: the plant keeps writing, mirror lag grows
#   3 central-up, then an alarm injected during the drain: its time to reach central
#   4 the drain: time until the consumer lag is back near zero
#   5 kill-gateway BDY: a gateway is active again for BDY within 30 s
#   6 failover BDY: the standby takes over, the killed one returns as the standby
#   7 kill-consumer 1
#   8 drill.sh proof: PASS with 0 missing, 0 gaps, 0 over and 0 duplicate rows
#   9 a dashboard screenshot of the result, then resume
#
# Usage:
#   src/scripts/live/rehearse.sh [OUTAGE_SECONDS] [SCREENSHOT.png]
#
#   OUTAGE_SECONDS  how long central stays down (default 120, as in the demo)
#   SCREENSHOT.png  where to save the dashboard after the proof (default: none)
#
# Exit code: 0 when every step passed; otherwise 1. The plant is resumed at the end either way.
#
# Example:
#   src/scripts/live/rehearse.sh 120 /tmp/rehearsal.png
set -uo pipefail

[[ "${1:-}" == "-h" || "${1:-}" == "--help" ]] && { sed -n '2,22p' "$0"; exit 0; }
OUTAGE=${1:-120}
SHOT=${2:-}
ROOT=$(git -C "$(dirname "$0")" rev-parse --show-toplevel)
DRILL="$ROOT/src/deployment/scripts/drill.sh"
API=http://localhost:3000/api
results=()
fails=0

ov() { curl -fsS --max-time 5 "$API/overview" | python3 -c "$1"; }
ts() { date +%H:%M:%S; }
record() { # $1 step, $2 PASS|FAIL, $3 evidence
  results+=("$1|$2|$3"); [[ $2 == PASS ]] || fails=$((fails + 1))
  printf '\033[1m[%s] %-34s %s\033[0m  %s\n' "$(ts)" "$1" "$2" "$3"
}
healthy_shops='import json,sys; o=json.load(sys.stdin); print(sum(1 for g in o["gateways"] if g.get("instance") and g["health"]["$type"]=="Healthy"))'
live_rate='import json,sys; print(int(json.load(sys.stdin)["liveRate"]))'
lag='import json,sys; print(json.load(sys.stdin)["consumerLag"])'
mirror='import json,sys; print(json.load(sys.stdin)["mirrorLagSeconds"])'
wait_rate() { local d=$((SECONDS + 60)); while ((SECONDS < d)); do (($(ov "$live_rate" 2>/dev/null || echo 0) >= 400)) && return 0; sleep 1; done; return 1; }
trap '"$DRILL" resume > /dev/null 2>&1' EXIT

# 1 steady state
"$DRILL" resume > /dev/null 2>&1
running=$(docker compose -f "$ROOT/src/deployment/compose.yaml" ps --status running --quiet | wc -l | tr -d ' ')
topics=$(docker compose -f "$ROOT/src/deployment/compose.yaml" ps -a plant-topics --format '{{.Status}}')
wait_rate; rate=$(ov "$live_rate"); shops=$(ov "$healthy_shops")
[[ $running == 20 && $topics == Exited\ \(0\)* && $shops == 5 && $rate -ge 400 ]] && s=PASS || s=FAIL
record "1 Steady state" $s "$running running + plant-topics ${topics%% *} 0; $shops/5 shops healthy; $rate readings/s"

# 2 central down
"$DRILL" central-down > /dev/null 2>&1
t_down=$SECONDS; min_rate=999999
while ((SECONDS - t_down < OUTAGE)); do r=$(ov "$live_rate" 2>/dev/null || echo 0); ((r < min_rate)) && min_rate=$r; sleep 5; done
ml=$(ov "$mirror")
[[ $min_rate -ge 400 ]] && s=PASS || s=FAIL
record "2 Central down ${OUTAGE} s" $s "plant kept writing (lowest ${min_rate}/s); mirror lag ${ml} s"

# 3 central up, alarm during the drain
"$DRILL" central-up > /dev/null 2>&1
t_up=$SECONDS
since=$(date -u +%Y-%m-%dT%H:%M:%S)
curl -fsS --max-time 10 -X POST -H 'Content-Type: application/json' -d '{"kind":"Vibration"}' \
  "$API/machines/P1-MCH-C017/faults" > /dev/null 2>&1
t_fault=$SECONDS; alarm=""
while ((SECONDS - t_fault < 180)); do
  alarm=$(curl -fsS --max-time 5 "$API/alarms" 2>/dev/null | python3 -c '
import json, sys
for a in json.load(sys.stdin):
    if a["machineId"] == "P1-MCH-C017" and a["storedAt"][:19] >= sys.argv[1]:
        print(a["kind"]); break' "$since" 2>/dev/null)
  [[ -n $alarm ]] && break; sleep 1
done
[[ -n $alarm ]] && s=PASS || s=FAIL
record "3 Alarm during the drain" $s "${alarm:-none} alarm stored $((SECONDS - t_fault)) s after the fault (central had just come back)"

# 4 the drain
t_d=$SECONDS; l=999999
while ((SECONDS - t_d < OUTAGE * 3 + 300)); do l=$(ov "$lag" 2>/dev/null || echo 999999); ((l < 2000)) && break; sleep 3; done
((l < 2000)) && s=PASS || s=FAIL
record "4 Drain after central-up" $s "consumer lag $l after $((SECONDS - t_up)) s (outage was ${OUTAGE} s)"

# 5 kill-gateway BDY
"$DRILL" kill-gateway BDY > /dev/null 2>&1
t_k=$SECONDS; n=0
while ((SECONDS - t_k < 30)); do n=$(ov "$healthy_shops" 2>/dev/null || echo 0); [[ $n == 5 ]] && break; sleep 1; done
[[ $n == 5 ]] && s=PASS || s=FAIL
record "5 Kill a gateway (BDY)" $s "BDY active again after $((SECONDS - t_k)) s; $n/5 shops healthy"

# 6 failover BDY
wait_rate
out=$("$DRILL" failover BDY 2>&1); rc=$?
took=$(grep -o 'took over after [0-9]* s' <<<"$out"); back=$(grep -o 'back as the standby after [0-9]* s' <<<"$out")
[[ $rc == 0 ]] && s=PASS || s=FAIL
record "6 Failover (BDY)" $s "${took:-no takeover}; ${back:-did not return as standby}"

# 7 kill-consumer 1
"$DRILL" kill-consumer 1 > /dev/null 2>&1 && s=PASS || s=FAIL
record "7 Kill a consumer" $s "consumer-1 killed, restarted after 10 s"

# 8 the proof
proof=$("$DRILL" proof 2>&1); rc=$?
get() { grep -E "^ *$1:" <<<"$proof" | sed 's/.*: //'; }
[[ $rc == 0 && $(get missing) == 0 && $(get gaps) == 0 && $(get over) == 0 && $(get 'duplicate rows') == 0 ]] && s=PASS || s=FAIL
record "8 Proof" $s "$(get verdict): generated $(get generated) = stored $(get stored); missing $(get missing), gaps $(get gaps), over $(get over), duplicate rows $(get 'duplicate rows'), duplicates absorbed $(get 'duplicates absorbed')"

# 9 screenshot, then resume (the trap resumes too)
if [[ -n $SHOT ]]; then
  python3 "$ROOT/src/scripts/diagrams/screenshot.py" http://localhost:3000 "$SHOT" --full --wait 4000 > /dev/null 2>&1 \
    && record "9 Dashboard screenshot" PASS "$SHOT" || record "9 Dashboard screenshot" FAIL "screenshot.py failed"
fi
"$DRILL" resume > /dev/null 2>&1

echo; echo "| # | Step | Result | Evidence |"; echo "| --- | --- | --- | --- |"
for r in "${results[@]}"; do IFS='|' read -r step res ev <<<"$r"; echo "| ${step%% *} | ${step#* } | $res | $ev |"; done
echo; ((fails == 0)) && echo "REHEARSAL PASSED" || echo "REHEARSAL FAILED: $fails step(s)"
exit $((fails > 0))
