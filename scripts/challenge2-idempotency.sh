#!/usr/bin/env bash
# Challenge 2 — Idempotency (BR4).
#   A. Same key + same body repeated → same response, replayed, one booking row.
#   B. Same key + different body → 422 IDEMPOTENCY_KEY_MISMATCH.   C. No key → 400.
#   D. 10 identical POST /bookings at the same instant → one booking, one hold.
#   E. 10 identical POST /bookings/{id}/payment at the same instant → one charge.
#   F. Logs and database rows proving the replay.
#
#   ./scripts/challenge2-idempotency.sh [--parallel 10]    output saved to evidence/challenge-2-idempotency/
parallel=10
[[ "${1:-}" == "--parallel" ]] && parallel="$2"
SCRIPT_COMMAND="scripts/challenge2-idempotency.sh --parallel $parallel"
. "$(dirname "$0")/lib.sh"
start_evidence challenge-2-idempotency c2
set_chaos payment 0 0 DECLINE >/dev/null
started_at="$(date -u +%Y-%m-%dT%H:%M:%SZ)"

# fire_parallel <n> <method> <url> <body> <key>: releases n identical requests at the same instant (FIFO barrier)
fire_parallel() {
  local n="$1" method="$2" url="$3" body="$4" key="$5"
  local -a worker_pids=()
  rm -f "$WORK"/p.*; mkfifo "$WORK/go"
  for i in $(seq 1 "$n"); do
    (
      read -r _ <"$WORK/go" || true
      args=(-s -o "$WORK/p.$i.json" -w '%{http_code}\n' -X "$method" -H "Idempotency-Key: $key" -H "X-Correlation-Id: $RUN_ID-par-$i" -D "$WORK/p.$i.h")
      [[ -n "$body" ]] && args+=(-H 'Content-Type: application/json' -d "$body")
      curl "${args[@]}" "$url" >"$WORK/p.$i.status"
    ) &
    worker_pids+=("$!")
  done
  sleep 1
  exec 3>"$WORK/go"; exec 3>&-
  wait "${worker_pids[@]}"
  rm -f "$WORK/go"
  printf '  %-4s %-6s %-9s %s\n' "#" "HTTP" "replayed" "result"
  for i in $(seq 1 "$n"); do
    printf '  %-4s %-6s %-9s %s\n' "$i" "$(cat "$WORK/p.$i.status")" \
      "$(grep -qi '^idempotency-replayed: true' "$WORK/p.$i.h" && echo yes || echo no)" \
      "$(jq -c '{id, status, code} | with_entries(select(.value != null))' "$WORK/p.$i.json")"
  done
}

count_status() { cat "$WORK"/p.*.status | grep -c "^$1$" || true; }

cust_a="cust-a-$RUN_ID"; cust_d="cust-d-$RUN_ID"; cust_other="cust-x-$RUN_ID"
seed="$(seed_slot 5 50000)"
tenant="$(jq -r .tenantId <<<"$seed")"
slot="$(jq -r '.slotIds[0]' <<<"$seed")"
echo "Seeded slot $slot (capacity 5, price 50000)"

section "A. Sequential repeat: same key, same body"
key_a="c2-a-$(uuid)"
body_a="{\"tenantId\":\"$tenant\",\"slotId\":\"$slot\",\"customer\":{\"id\":\"$cust_a\",\"phone\":\"+919800000101\"}}"
api POST "$BOOKING_URL/api/v1/bookings" "$body_a" "$key_a"; first="$HTTP_BODY"; first_status="$HTTP_STATUS"
api POST "$BOOKING_URL/api/v1/bookings" "$body_a" "$key_a"; second="$HTTP_BODY"
api POST "$BOOKING_URL/api/v1/bookings" "$body_a" "$key_a"; third="$HTTP_BODY"
check "first call → 201" 201 "$first_status"
check "repeat → same status" 201 "$HTTP_STATUS"
check "repeat marked Idempotency-Replayed" true "$HTTP_REPLAYED"
check "repeat body identical to first (sha256 prefix)" "$(body_hash "$first")" "$(body_hash "$second")"
check "third body identical to first (sha256 prefix)" "$(body_hash "$first")" "$(body_hash "$third")"
booking_a="$(jq -r .id <<<"$first")"
echo "  first response: $(jq -c '{id, status, holdId, amountMinor}' <<<"$first")"
check "booking rows for customer $cust_a" 1 "$(sql booking "SELECT count(*) FROM bookings WHERE customer_id = '$cust_a' AND slot_id = '$slot';")"

section "B. Same key, different body"
api POST "$BOOKING_URL/api/v1/bookings" "${body_a/$cust_a/$cust_other}" "$key_a"
check "different payload → 422" 422 "$HTTP_STATUS"
check "error code" IDEMPOTENCY_KEY_MISMATCH "$(field .code)"
check "no extra booking created" 0 "$(sql booking "SELECT count(*) FROM bookings WHERE customer_id = '$cust_other';")"

section "C. Missing Idempotency-Key"
api POST "$BOOKING_URL/api/v1/bookings" "$body_a"
check "no key → 400" 400 "$HTTP_STATUS"
check "error code" IDEMPOTENCY_KEY_REQUIRED "$(field .code)"

section "D. $parallel identical POST /bookings at the same instant"
key_d="c2-d-$(uuid)"
body_d="{\"tenantId\":\"$tenant\",\"slotId\":\"$slot\",\"customer\":{\"id\":\"$cust_d\",\"phone\":\"+919800000102\"}}"
fire_parallel "$parallel" POST "$BOOKING_URL/api/v1/bookings" "$body_d" "$key_d"
created=$(count_status 201); in_progress=$(count_status 409)
echo "  201 (original or replay): $created, 409 REQUEST_IN_PROGRESS: $in_progress"
check "every response is 201 or 409 in-progress" "$parallel" "$((created + in_progress))"
check "booking rows created" 1 "$(sql booking "SELECT count(*) FROM bookings WHERE customer_id = '$cust_d';")"
booking_d="$(sql booking "SELECT id FROM bookings WHERE customer_id = '$cust_d';")"
check "holds in Scheduling for that booking" 1 "$(sql scheduling "SELECT count(*) FROM holds WHERE booking_id = '$booking_d';")"
api GET "$SCHEDULING_URL/api/v1/slots/$slot"
check "slot reserved (bookings A + D)" 2 "$(field .reserved)"

section "E. $parallel identical POST /bookings/{id}/payment at the same instant"
key_e="c2-e-$(uuid)"
fire_parallel "$parallel" POST "$BOOKING_URL/api/v1/bookings/$booking_d/payment" "" "$key_e"
echo "  200: $(count_status 200), 409: $(count_status 409)"
api GET "$BOOKING_URL/api/v1/bookings/$booking_d"
check "booking confirmed" CONFIRMED "$(field .status)"
check "payment rows for booking (payment_db)" 1 "$(sql payment "SELECT count(*) FROM payments WHERE booking_id = '$booking_d';")"
check "total charged" 50000 "$(sql payment "SELECT sum(amount_minor) FROM payments WHERE booking_id = '$booking_d';")"
api POST "$BOOKING_URL/api/v1/bookings/$booking_d/payment" "" "$key_e"
check "late repeat of the payment → replayed 200" "200 true" "$HTTP_STATUS $HTTP_REPLAYED"
api POST "$BOOKING_URL/api/v1/bookings/$booking_d/payment" "" "c2-e2-$(uuid)"
check "pay again with a NEW key → 200, still no second charge" "200 1" "$HTTP_STATUS $(sql payment "SELECT count(*) FROM payments WHERE booking_id = '$booking_d';")"

section "F. Evidence: database rows"
echo "-- booking_db.idempotency_keys"
sql_table booking "SELECT key, scope, state, response_status, created_at FROM idempotency_keys WHERE key IN ('$key_a', '$key_d', '$key_e') ORDER BY created_at;"
echo "-- payment_db.idempotency_keys (Booking → Payment uses the derived key pay-{bookingId})"
sql_table payment "SELECT key, scope, state, response_status FROM idempotency_keys WHERE key = 'pay-$booking_d';"
echo "-- payment_db.payments"
sql_table payment "SELECT id, booking_id, amount_minor, status, created_at FROM payments WHERE booking_id = '$booking_d';"
echo "-- booking_db.bookings"
sql_table booking "SELECT id, customer_id, status, payment_id FROM bookings WHERE id IN ('$booking_a', '$booking_d');"

section "F. Evidence: replay log lines"
log_lines slothub-booking "$started_at" 'Idempotency|CONFIRMED|HELD'
log_lines slothub-payment "$started_at" 'Idempotency|Gateway'


finish
