#!/usr/bin/env bash
# Assignment 2 — Task 2 evidence: the Compose deployment is healthy, the microservices talk to each other,
# and the main workflow works (including failure paths).
#   1. docker compose ps + container health    2. readiness endpoints
#   3. one booking traced by its correlation ID through Booking → Scheduling → Payment → Notification logs
#   4. container health-check details           5. full end-to-end script (happy path + failures)
#
#   docker compose up -d --build --wait && ./scripts/a2-verify-deployment.sh     → evidence/a2-task2-compose/
SCRIPT_COMMAND="scripts/a2-verify-deployment.sh"
. "$(dirname "$0")/lib.sh"
start_evidence a2-task2-compose verify
cd "$ROOT"
started_at="$(date -u +%Y-%m-%dT%H:%M:%SZ)"

section "1. Containers (docker compose ps)"
docker compose ps --format 'table {{.Name}}\t{{.Image}}\t{{.Status}}\t{{.Ports}}'
for c in postgres scheduling booking payment notification; do
  check "slothub-$c is healthy" healthy "$(docker inspect --format '{{.State.Health.Status}}' "slothub-$c" 2>/dev/null)"
done

section "2. Readiness endpoints (service up + own database reachable)"
for url in "$SCHEDULING_URL" "$BOOKING_URL" "$PAYMENT_URL" "$NOTIFICATION_URL"; do
  api GET "$url/health/ready"
  check "$(field .service) /health/ready" HEALTHY "$(field .status)"
done

section "3. Inter-service communication, traced by one correlation ID"
trace="trace-$RUN_ID"
seed="$(seed_slot 1 50000)"
tenant="$(jq -r .tenantId <<<"$seed")"
slot="$(jq -r '.slotIds[0]' <<<"$seed")"
echo "Correlation ID for this booking: $trace"
booking="$(curl -s -X POST "$BOOKING_URL/api/v1/bookings" -H 'Content-Type: application/json' -H "X-Correlation-Id: $trace" \
  -H "Idempotency-Key: $(uuid)" -d "{\"tenantId\":\"$tenant\",\"slotId\":\"$slot\",\"customer\":$(customer_json trace)}")"
booking_id="$(jq -r .id <<<"$booking")"
echo "POST /api/v1/bookings          → $(jq -c '{id, status, holdId}' <<<"$booking")"
paid="$(curl -s -X POST "$BOOKING_URL/api/v1/bookings/$booking_id/payment" -H "X-Correlation-Id: $trace" -H "Idempotency-Key: $(uuid)")"
echo "POST /api/v1/bookings/{id}/payment → $(jq -c '{status, paymentId}' <<<"$paid")"
check "booking confirmed" CONFIRMED "$(jq -r .status <<<"$paid")"

delivered() { curl -s "$NOTIFICATION_URL/api/v1/notifications?bookingId=$booking_id" | jq '[.items[] | select(.status == "SENT")] | length > 0'; }
wait_for "confirmation delivered by Notification" 20 delivered

echo
echo "Log lines carrying $trace in each container (docker compose logs):"
for c in booking scheduling payment notification; do
  lines="$(docker compose logs --no-log-prefix --since "$started_at" "$c" 2>/dev/null | grep -c "\"$trace\"" || true)"
  printf '  %-13s %s lines\n' "$c" "$lines"
  check "$c logged the traced request" true "$([[ "$lines" -gt 0 ]] && echo true || echo false)"
done
echo
for c in booking scheduling payment notification; do
  echo "-- $c"
  docker compose logs --no-log-prefix --since "$started_at" "$c" 2>/dev/null |
    jq -Rr --arg t "$trace" 'fromjson? | select(.CorrelationId == $t) | "\(.["@t"][11:23]) \(.["@m"])"' |
    grep -vE '^[0-9:.]+ (Execution attempt|HTTP "GET")' | head -6
done

section "4. Container health-check details (docker inspect)"
docker inspect --format '{{json .State.Health}}' slothub-booking |
  jq '{Status, FailingStreak, last_checks: [.Log[-2:][] | {Start: .Start[11:19], ExitCode}]}'
docker inspect --format '{{json .Config.Healthcheck}}' slothub-booking | jq -c .

section "5. Main workflow end to end (scripts/e2e-booking.sh)"
if "$ROOT/scripts/e2e-booking.sh" | sed 's/^/  │ /'; then
  check "e2e-booking.sh (happy path, declined payment, gateway down)" PASS PASS
else
  check "e2e-booking.sh (happy path, declined payment, gateway down)" PASS FAIL
fi

finish
