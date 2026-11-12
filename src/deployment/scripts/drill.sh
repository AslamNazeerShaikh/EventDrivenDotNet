#!/usr/bin/env bash
# Failure drills and the proof command (game plan §10). Works from any directory.
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")/.."

API=http://localhost:3000/api
PROOF_TIMEOUT=300 # seconds

usage() {
  cat >&2 <<USAGE
usage: drill.sh <command>
  central-down          kill mm2 + consumers, stop central db + kafka
  central-up            start central kafka, db, consumers, mm2
  wan-cut               disconnect mm2 from the plant network
  wan-restore           reconnect mm2 to the plant network
  plant-down            stop plant-kafka
  plant-up              start plant-kafka
  kill-gateway <SHOP>   kill a gateway (PRS, BDY, PNT, MCH, ASM), then start it
  kill-consumer <N>     kill consumer 1 or 2, start it again after 10 s
  pause | resume        pause or resume the plant simulators
  proof                 pause, wait for the drain, print the reconciliation; exit 0 on PASS
USAGE
  exit 2
}

# Echo the command so the panel sees what is being broken, then run it.
run() { printf '\033[1m▶ %s\033[0m\n' "$*"; "$@"; }

proof() {
  run curl -fsS -X POST "$API/plant/pause"; echo
  local deadline=$((SECONDS + PROOF_TIMEOUT)) json="" drained=false
  printf '▶ curl -fsS %s/reconciliation?full=true  (every 2 s, up to %s s, until drained)\n' "$API" "$PROOF_TIMEOUT"
  while ((SECONDS < deadline)); do
    # a failed poll (API restarting, 503) is retried, not fatal
    if json=$(curl -fsS "$API/reconciliation?full=true" 2>/dev/null); then
      drained=$(python3 -c 'import json,sys; print(str(json.load(sys.stdin)["drained"]).lower())' <<<"$json")
      [[ $drained == true ]] && break
    fi
    sleep 2
  done
  if [[ -z $json ]]; then echo "proof: no answer from $API/reconciliation" >&2; return 3; fi
  python3 -c '
import json, sys
r = json.load(sys.stdin)
for label, key in [("generated", "generated"), ("stored", "stored"), ("missing", "missing"),
                   ("gaps", "gaps"), ("duplicate rows", "duplicateRows"),
                   ("duplicates absorbed", "duplicatesAbsorbed"), ("verdict", "verdict")]:
    print(f"{label:>20}: {r[key]}")
' <<<"$json"
  if [[ $drained != true ]]; then echo "proof: still not drained after ${PROOF_TIMEOUT}s" >&2; return 3; fi
  [[ $(python3 -c 'import json,sys; print(json.load(sys.stdin)["verdict"])' <<<"$json") == PASS ]]
}

[[ $# -ge 1 ]] || usage
cmd=$1
case $cmd in
  central-down)
    run docker compose kill mm2 consumer-1 consumer-2
    run docker compose stop central-db central-kafka ;;
  central-up)
    run docker compose start central-kafka central-db consumer-1 consumer-2 mm2 ;;
  wan-cut)     run docker network disconnect edd_plant edd-mm2-1 ;;
  wan-restore) run docker network connect edd_plant edd-mm2-1 ;;
  plant-down)  run docker compose stop plant-kafka ;;
  plant-up)    run docker compose start plant-kafka ;;
  kill-gateway)
    shop=$(printf '%s' "${2:-}" | tr '[:upper:]' '[:lower:]')
    case $shop in prs|bdy|pnt|mch|asm) ;; *) usage ;; esac # bash 3.2 (macOS): no ${var,,}
    run docker compose kill "gateway-$shop"
    run docker compose start "gateway-$shop" ;;
  kill-consumer)
    [[ ${2:-} =~ ^[12]$ ]] || usage
    run docker compose kill "consumer-$2"
    echo "(waiting 10 s)"; sleep 10
    run docker compose start "consumer-$2" ;;
  pause)  run curl -fsS -X POST "$API/plant/pause"; echo ;;
  resume) run curl -fsS -X POST "$API/plant/resume"; echo ;;
  proof)  proof ;;
  *) usage ;;
esac
