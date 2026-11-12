#!/usr/bin/env bash
# Failure drills and the proof command (game plan §10). Works from any directory.
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")/.."

API=http://localhost:3000/api
PROOF_TIMEOUT=300 # seconds

usage() {
  cat >&2 <<USAGE
usage: drill.sh <command>
  central-down          kill mm2-1, mm2-2 + consumers, stop central db + kafka
  central-up            start central kafka, db, consumers, mm2-1 and mm2-2
  wan-cut               disconnect mm2-1 and mm2-2 from the plant network
  wan-restore           reconnect mm2-1 and mm2-2 to the plant network
  plant-down            stop plant-kafka
  plant-up              start plant-kafka
  kill-gateway <SHOP>   kill the shop's active gateway, then start it
  lose-gateway-disk <SHOP>  recreate the active gateway's disk, then start it: compose rm -sf, volume rm, up -d
  failover <SHOP>       kill the active instance; the standby takes over, then the killed one returns as a standby
  kill-consumer <N>     kill consumer 1 or 2, start it again after 10 s
  pause | resume        pause or resume every gateway instance
  proof                 pause, wait for the drain, print the reconciliation; exit 0 on PASS
USAGE
  exit 2
}

# Echo the command so the panel sees what is being broken, then run it.
run() { printf '\033[1m▶ %s\033[0m\n' "$*"; "$@"; }

# The shop's active instance from /api/overview; fails when there is none.
active_instance() { # $1 SHOP (any case)
  local shop inst
  shop=$(printf '%s' "$1" | tr '[:upper:]' '[:lower:]')
  inst=$(curl -fsS --max-time 5 "$API/overview" | python3 -c '
import json, sys
shop = sys.argv[1].lower()
for g in json.load(sys.stdin)["gateways"]:
    if g["shop"].lower() == shop and g.get("instance"):
        print(g["instance"])
        break
' "$shop")
  [[ -n $inst ]] || { echo "no active gateway for $1 (see /api/overview)" >&2; return 1; }
  printf '%s\n' "$inst"
}

# The shop's active instance and its health from /api/overview, "<instance> <$type>" or empty.
active_health() { # $1 SHOP (any case)
  local shop
  shop=$(printf '%s' "$1" | tr '[:upper:]' '[:lower:]')
  curl -fsS --max-time 5 "$API/overview" | python3 -c '
import json, sys
shop = sys.argv[1].lower()
for g in json.load(sys.stdin)["gateways"]:
    if g["shop"].lower() == shop:
        print((g.get("instance") or "") + " " + g["health"]["$type"])
        break
' "$shop"
}

proof() {
  run curl -fsS --max-time 10 -X POST "$API/plant/pause"; echo
  local deadline=$((SECONDS + PROOF_TIMEOUT)) json="" drained=false
  printf '▶ curl -fsS --max-time 5 %s/reconciliation?full=true  (every 2 s, up to %s s, until drained)\n' "$API" "$PROOF_TIMEOUT"
  while ((SECONDS < deadline)); do
    # re-send the pause at the top of every poll: an instance that restarted during the proof is paused before the next verdict.
    # A failed send is a retry, not fatal.
    curl -fsS --max-time 10 -X POST "$API/plant/pause" > /dev/null 2>&1 || true
    if json=$(curl -fsS --max-time 5 "$API/reconciliation?full=true" 2>/dev/null); then
      drained=$(python3 -c 'import json,sys; print(str(json.load(sys.stdin)["drained"]).lower())' <<<"$json" 2>/dev/null) || drained=false
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

failover() { # $1 SHOP (any case) — drill 9: kill the active instance; the standby takes over; the killed one returns as a standby.
  local shop instance other deadline n inst type
  shop=$(printf '%s' "$1" | tr '[:upper:]' '[:lower:]')
  instance=$(active_instance "$shop")
  case "$instance" in
    *-a) other="gateway-$shop-b" ;;
    *-b) other="gateway-$shop-a" ;;
    *) echo "failover: unexpected instance '$instance'" >&2; return 2 ;;
  esac
  echo "▶ failover $(printf '%s' "$shop" | tr '[:lower:]' '[:upper:]'): killing $instance"
  run docker compose kill "$instance"
  deadline=$((SECONDS + 30)); n=0
  printf '▶ polling %s/overview every 1 s, up to 30 s, until %s is the shop’s active one and Healthy\n' "$API" "$other"
  while ((SECONDS < deadline)); do
    n=$((n + 1))
    if read -r inst type <<<"$(active_health "$shop" 2>/dev/null)" && [[ $inst == "$other" && $type == Healthy ]]; then
      printf '%s took over after %d s\n' "$other" "$n"
      break
    fi
    sleep 1
  done
  [[ ${inst:-} == "$other" && ${type:-} == Healthy ]] || { echo "failover: $other did not take over within 30 s" >&2; return 4; }

  echo "▶ starting $instance"
  run docker compose start "$instance"
  deadline=$((SECONDS + 30)); n=0
  printf '▶ polling %s/overview every 1 s, up to 30 s, until %s is back as a Healthy standby\n' "$API" "$instance"
  while ((SECONDS < deadline)); do
    n=$((n + 1))
    # cooperative-sticky keeps the current owner: back in the overview, the shop’s tile stays on $other,
    # and the restarted container is running — it returns as the standby, with no second switch.
    if read -r inst type <<<"$(active_health "$shop" 2>/dev/null)" \
        && [[ $inst == "$other" && $type == Healthy ]] \
        && [[ -n "$(docker compose ps --status running --quiet "$instance")" ]]; then
      printf '%s is back as the standby after %d s (no second switch: %s stays active)\n' "$instance" "$n" "$other"
      return 0
    fi
    sleep 1
  done
  echo "failover: $instance did not return as a Healthy standby within 30 s" >&2
  return 4
}

[[ $# -ge 1 ]] || usage
cmd=$1
case $cmd in
  central-down)
    run docker compose kill mm2-1 mm2-2 consumer-1 consumer-2
    run docker compose stop central-db central-kafka ;;
  central-up)
    run docker compose start central-kafka central-db consumer-1 consumer-2 mm2-1 mm2-2 ;;
  wan-cut)
    run docker network disconnect edd_plant edd-mm2-1-1
    run docker network disconnect edd_plant edd-mm2-2-1 ;;
  wan-restore)
    run docker network connect edd_plant edd-mm2-1-1
    run docker network connect edd_plant edd-mm2-2-1 ;;
  plant-down)  run docker compose stop plant-kafka ;;
  plant-up)    run docker compose start plant-kafka ;;
  kill-gateway)
    shop=$(printf '%s' "${2:-}" | tr '[:upper:]' '[:lower:]')
    case $shop in prs|bdy|pnt|mch|asm) ;; *) usage ;; esac # bash 3.2 (macOS): no ${var,,}
    instance=$(active_instance "$shop")
    run docker compose kill "$instance"
    run docker compose start "$instance" ;;
  lose-gateway-disk)
    shop=$(printf '%s' "${2:-}" | tr '[:upper:]' '[:lower:]')
    case $shop in prs|bdy|pnt|mch|asm) ;; *) usage ;; esac # bash 3.2 (macOS): no ${var,,}
    instance=$(active_instance "$shop")
    run docker compose rm -sf "$instance"
    run docker volume rm "edd_${instance}-data"
    run docker compose up -d "$instance" ;;
  failover)
    shop=$(printf '%s' "${2:-}" | tr '[:upper:]' '[:lower:]')
    case $shop in prs|bdy|pnt|mch|asm) ;; *) usage ;; esac
    failover "$shop" ;;
  kill-consumer)
    [[ ${2:-} =~ ^[12]$ ]] || usage
    run docker compose kill "consumer-$2"
    echo "(waiting 10 s)"; sleep 10
    run docker compose start "consumer-$2" ;;
  pause)  run curl -fsS --max-time 10 -X POST "$API/plant/pause"; echo ;;
  resume) run curl -fsS --max-time 10 -X POST "$API/plant/resume"; echo ;;
  proof)  proof ;;
  *) usage ;;
esac
