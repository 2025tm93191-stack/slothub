#!/usr/bin/env bash
# Challenge 1 — Concurrency control (BR1).
# Fires REQUESTS parallel hold requests, each for a different booking, at ONE slot with capacity CAPACITY.
# Pass criteria: exactly CAPACITY × 201, the rest 409 SLOT_UNAVAILABLE, and the database agrees.
#
#   ./scripts/challenge1-concurrency.sh [--capacity 1] [--requests 20]
#
# Output is printed and saved to evidence/challenge-1-concurrency/run-<timestamp>-capacity<N>.txt
set -euo pipefail

capacity=1
requests=20
naive=false
while [[ $# -gt 0 ]]; do
  case "$1" in
    --capacity) capacity="$2"; shift 2 ;;
    --requests) requests="$2"; shift 2 ;;
    --naive) naive=true; shift ;; # target a Scheduling instance running CONCURRENCY_MODE=naive (see challenge1-naive-demo.sh)
    *) echo "Unknown option: $1" >&2; exit 2 ;;
  esac
done

root="$(cd "$(dirname "$0")/.." && pwd)"
url="${SCHEDULING_URL:-http://localhost:5101}"
pg_container="${PG_CONTAINER:-slothub-postgres}"
run_id="c1-$(date -u +%Y%m%dT%H%M%SZ)"
out_dir="$root/evidence/challenge-1-concurrency"
suffix=""
[[ "$naive" == true ]] && suffix="-naive"
out_file="$out_dir/run-${run_id#c1-}-capacity${capacity}${suffix}.txt"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
mkdir -p "$out_dir"
exec > >(tee "$out_file") 2>&1

echo "== Challenge 1: concurrency control (BR1) =="
echo "Run:        $run_id"
echo "Started:    $(date -u +%Y-%m-%dT%H:%M:%SZ)"
echo "Command:    scripts/challenge1-concurrency.sh --capacity $capacity --requests $requests${suffix:+ --naive}"
if [[ "$naive" == true ]]; then
  echo "Mode:       NAIVE check-then-update (demonstrates the failure; not the real implementation)"
fi
echo "Target:     POST $url/api/v1/holds"
echo

seed="$("$root/scripts/seed.sh" --capacity "$capacity" --slots 1)"
slot_id="$(jq -r '.slotIds[0]' <<<"$seed")"
echo "Seeded slot $slot_id with capacity $capacity"
echo

# Barrier: every request blocks on a FIFO and all are released at the same instant.
mkfifo "$work/go"
for i in $(seq 1 "$requests"); do
  (
    booking_id="$(uuidgen | tr '[:upper:]' '[:lower:]')"
    read -r _ <"$work/go" || true
    curl -s -o "$work/$i.json" -w '%{http_code} %{time_total}\n' \
      -H 'Content-Type: application/json' -H "X-Correlation-Id: $run_id-$i" \
      -X POST "$url/api/v1/holds" -d "{\"slotId\":\"$slot_id\",\"bookingId\":\"$booking_id\"}" >"$work/$i.status"
  ) &
done
sleep 1
echo "Releasing $requests requests simultaneously at $(date -u +%H:%M:%S)..."
exec 3>"$work/go"
exec 3>&-
wait

printf '\n%-4s %-6s %-9s %s\n' "#" "HTTP" "time(s)" "result"
for i in $(seq 1 "$requests"); do
  read -r status seconds <"$work/$i.status"
  if [[ "$status" == "201" ]]; then
    result="HELD hold=$(jq -r .id "$work/$i.json")"
  else
    result="$(jq -r '.code // "?"' "$work/$i.json")"
  fi
  printf '%-4s %-6s %-9s %s\n' "$i" "$status" "$seconds" "$result"
done

created=$(cat "$work"/*.status | awk '$1 == 201' | wc -l | tr -d ' ')
rejected=$(cat "$work"/*.status | awk '$1 == 409' | wc -l | tr -d ' ')
unavailable=$(jq -r '.code // empty' "$work"/*.json | grep -c '^SLOT_UNAVAILABLE$' || true)
slot="$(curl -s "$url/api/v1/slots/$slot_id")"
reserved="$(jq -r .reserved <<<"$slot")"

echo
echo "== Summary =="
echo "201 Created (held):            $created   (expected $capacity)"
echo "409 Conflict:                  $rejected   (expected $((requests - capacity)), all SLOT_UNAVAILABLE: $unavailable)"
echo "Slot via API:                  $(jq -c '{capacity, reserved, remaining}' <<<"$slot")"

echo
echo "== Database state (scheduling_db) =="
docker exec "$pg_container" psql -U scheduling_user -d scheduling_db -c \
  "SELECT id, capacity, reserved_count, version FROM slots WHERE id = '$slot_id';" -c \
  "SELECT status, count(*) FROM holds WHERE slot_id = '$slot_id' GROUP BY status;" \
  || echo "(could not query database container '$pg_container')"

echo
if [[ "$naive" == true ]]; then
  holds=$(docker exec "$pg_container" psql -U scheduling_user -d scheduling_db -At -c "SELECT count(*) FROM holds WHERE slot_id = '$slot_id';")
  echo "Holds created: $holds for capacity $capacity; reserved_count says $reserved."
  if [[ "$holds" -gt "$capacity" ]]; then
    echo "RESULT: OVER-ALLOCATED (expected in naive mode) — concurrent requests all read the old count, all passed"
    echo "        the capacity check in C#, and each wrote 'old + 1', so increments were lost."
  else
    echo "RESULT: no over-allocation observed this run (the race is timing-dependent; run again)"
  fi
  echo "Saved to:   ${out_file#"$root"/}"
  exit 0
fi
if [[ "$created" == "$capacity" && "$rejected" == "$((requests - capacity))" && "$unavailable" == "$rejected" && "$reserved" == "$capacity" ]]; then
  echo "RESULT: PASS — exactly $capacity of $requests concurrent requests succeeded; no over-allocation."
  echo "Finished:   $(date -u +%Y-%m-%dT%H:%M:%SZ)"
  echo "Saved to:   ${out_file#"$root"/}"
else
  echo "RESULT: FAIL"
  echo "Saved to:   ${out_file#"$root"/}"
  exit 1
fi
