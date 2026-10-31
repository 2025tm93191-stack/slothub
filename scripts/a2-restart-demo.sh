#!/usr/bin/env bash
# Assignment 2 — Task 2: stop and restart the deployment and verify that it works again.
#   A. docker compose stop  → start   (containers kept)
#   B. docker compose down  → up      (containers and network removed, database volume kept)
#   C. docker compose restart booking (one service)
# After each step: a booking confirmed before the stop is still there, and a new booking flow succeeds.
#
#   ./scripts/a2-restart-demo.sh     → evidence/a2-task2-compose/
SCRIPT_COMMAND="scripts/a2-restart-demo.sh"
. "$(dirname "$0")/lib.sh"
start_evidence a2-task2-compose restart
cd "$ROOT"

# flow <label>: hold a fresh slot, pay, print the final booking status
flow() {
  local seed tenant slot booking
  seed="$(seed_slot 1 50000)"
  tenant="$(jq -r .tenantId <<<"$seed")"
  slot="$(jq -r '.slotIds[0]' <<<"$seed")"
  booking="$(curl -s -X POST "$BOOKING_URL/api/v1/bookings" -H 'Content-Type: application/json' -H "Idempotency-Key: $(uuid)" \
    -d "{\"tenantId\":\"$tenant\",\"slotId\":\"$slot\",\"customer\":$(customer_json "$1")}" | jq -r .id)"
  curl -s -X POST "$BOOKING_URL/api/v1/bookings/$booking/payment" -H "Idempotency-Key: $(uuid)" | jq -r .status
}

status_of() { curl -s -m 5 "$BOOKING_URL/api/v1/bookings/$1" | jq -r '.status // "unreachable"' 2>/dev/null || echo unreachable; }
ps_table() { docker compose ps -a --format 'table {{.Name}}\t{{.Status}}'; }

section "0. Before: create and confirm a booking"
seed="$(seed_slot 1 50000)"
api POST "$BOOKING_URL/api/v1/bookings" "{\"tenantId\":\"$(jq -r .tenantId <<<"$seed")\",\"slotId\":\"$(jq -r '.slotIds[0]' <<<"$seed")\",\"customer\":$(customer_json before)}" "$(uuid)"
before="$(field .id)"
api POST "$BOOKING_URL/api/v1/bookings/$before/payment" "" "$(uuid)"
check "booking $before" CONFIRMED "$(field .status)"

section "A. docker compose stop → start"
docker compose stop 2>&1 | grep -E 'Stopped' | sort -u
ps_table
check "API unreachable while stopped" unreachable "$(curl -s -m 3 -o /dev/null -w '%{http_code}' "$BOOKING_URL/health" | sed 's/^000$/unreachable/')"
docker compose up -d --wait 2>&1 | grep -E 'Healthy' | sort -u
ps_table
check "booking from before the stop" CONFIRMED "$(status_of "$before")"
check "new booking flow after start" CONFIRMED "$(flow after-start)"

section "B. docker compose down → up (containers removed, volume kept)"
docker compose down 2>&1 | grep -E 'Removed' | sort -u
echo "Containers left: $(docker compose ps -a -q | wc -l | tr -d ' '); volume: $(docker volume ls --format '{{.Name}}' | grep -c '^slothub_postgres-data$') x slothub_postgres-data"
check "no containers after down" 0 "$(docker compose ps -a -q | wc -l | tr -d ' ')"
docker compose up -d --wait 2>&1 | grep -E 'Healthy' | sort -u
ps_table
check "booking from before the down (data in the volume)" CONFIRMED "$(status_of "$before")"
check "new booking flow after up" CONFIRMED "$(flow after-up)"

section "C. docker compose restart booking"
docker compose restart booking 2>&1 | grep -E 'Started|Restarted' | sort -u
wait_for "booking healthy again" 60 bash -c "[[ \$(docker inspect --format '{{.State.Health.Status}}' slothub-booking) == healthy ]] && echo true"
check "booking container health" healthy "$(docker inspect --format '{{.State.Health.Status}}' slothub-booking)"
check "new booking flow after restart" CONFIRMED "$(flow after-restart)"

finish
