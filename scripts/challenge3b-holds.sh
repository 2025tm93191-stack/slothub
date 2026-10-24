#!/usr/bin/env bash
# Challenge 3B — Time-bound holds (BR2), with the BR3 compensation path.
#   A. An unpaid hold expires automatically after HOLD_TTL_SECONDS; capacity returns; booking → EXPIRED; customer notified.
#   B. The expiry job is safe to run twice (second run releases nothing; capacity never double-released).
#   C. A paid (CONFIRMED) hold is never released by expiry.
#   D. Cancellation releases a hold immediately.
#   E. Payment that completes after the hold expired is refunded automatically; booking → EXPIRED.
#
#   ./scripts/challenge3b-holds.sh [--ttl 30]     (must match the Scheduling service's HOLD_TTL_SECONDS)
ttl=30
[[ "${1:-}" == "--ttl" ]] && ttl="$2"
SCRIPT_COMMAND="scripts/challenge3b-holds.sh --ttl $ttl"
. "$(dirname "$0")/lib.sh"
start_evidence challenge-3b-holds c3b
set_chaos payment 0 0 DECLINE >/dev/null
started_at="$(date -u +%Y-%m-%dT%H:%M:%SZ)"

new_booking() { # new_booking <label> → sets BOOKING, HOLD, SLOT
  local seed tenant
  seed="$(seed_slot 1 50000)"
  tenant="$(jq -r .tenantId <<<"$seed")"
  SLOT="$(jq -r '.slotIds[0]' <<<"$seed")"
  api POST "$BOOKING_URL/api/v1/bookings" "{\"tenantId\":\"$tenant\",\"slotId\":\"$SLOT\",\"customer\":$(customer_json "c3b-$1")}" "$(uuid)"
  BOOKING="$(field .id)"
  HOLD="$(field .holdId)"
}

slot_reserved() { curl -s "$SCHEDULING_URL/api/v1/slots/$1" | jq -r .reserved; }
hold_status() { curl -s "$SCHEDULING_URL/api/v1/holds/$1" | jq -r .status; }
booking_status() { curl -s "$BOOKING_URL/api/v1/bookings/$1" | jq -r .status; }

section "Setup at $(date -u +%H:%M:%S): four bookings, each on its own slot (capacity 1), hold TTL ${ttl}s"
t0=$(date +%s)
new_booking a; a_booking=$BOOKING a_hold=$HOLD a_slot=$SLOT
echo "  A (left unpaid):  booking $a_booking, hold expires $(field .holdExpiresAt)"
new_booking c; c_booking=$BOOKING c_hold=$HOLD c_slot=$SLOT
api POST "$BOOKING_URL/api/v1/bookings/$c_booking/payment" "" "$(uuid)"
echo "  C (paid at once): booking $c_booking → $(field .status)"
new_booking d; d_booking=$BOOKING d_hold=$HOLD d_slot=$SLOT
new_booking e; e_booking=$BOOKING e_hold=$HOLD e_slot=$SLOT
echo "  E (pays late):    booking $e_booking"

section "D. Cancellation releases the hold immediately"
check "slot D reserved before cancel" 1 "$(slot_reserved "$d_slot")"
api POST "$BOOKING_URL/api/v1/bookings/$d_booking/cancel" "" "$(uuid)"
check "booking D" CANCELLED "$(field .status)"
check "hold D" RELEASED "$(hold_status "$d_hold")"
check "slot D reserved right after cancel" 0 "$(slot_reserved "$d_slot")"

section "E. Late payment: start paying $((ttl - 4))s after the hold, gateway takes 6s → completes after expiry"
sleep $((t0 + ttl - 4 - $(date +%s)))
set_chaos payment 0 6000 DECLINE
api POST "$BOOKING_URL/api/v1/bookings/$e_booking/payment" "" "$(uuid)"
set_chaos payment 0 0 DECLINE
check "late pay → 409" 409 "$HTTP_STATUS"
check "error code" HOLD_EXPIRED "$(field .code)"
api GET "$BOOKING_URL/api/v1/bookings/$e_booking"
check "booking E" EXPIRED "$(field .status)"
check "booking E refunded amount" 50000 "$(field .refundedMinor)"
api GET "$PAYMENT_URL/api/v1/payments?bookingId=$e_booking"
check "payment E (charged once, then refunded)" "1 REFUNDED" "$(field length) $(field '.[0].status')"

section "A. Unpaid hold expires automatically"
echo "  waiting for the expiry worker (TTL ${ttl}s + sweep interval)..."
wait_for "hold A EXPIRED" $((ttl + 30)) bash -c "[[ \$(curl -s $SCHEDULING_URL/api/v1/holds/$a_hold | jq -r .status) == EXPIRED ]] && echo true"
check "hold A" EXPIRED "$(hold_status "$a_hold")"
check "slot A capacity returned" 0 "$(slot_reserved "$a_slot")"
wait_for "booking A reconciled" 20 bash -c "[[ \$(curl -s $BOOKING_URL/api/v1/bookings/$a_booking | jq -r .status) == EXPIRED ]] && echo true"
check "booking A" EXPIRED "$(booking_status "$a_booking")"
wait_for "expiry notification" 20 bash -c "curl -s '$NOTIFICATION_URL/api/v1/notifications?bookingId=$a_booking' | jq '[.items[] | select(.eventType == \"BOOKING_EXPIRED\")] | length > 0'"
api GET "$NOTIFICATION_URL/api/v1/notifications?bookingId=$a_booking"
check "BOOKING_EXPIRED notification queued" true "$(field '[.items[] | select(.eventType == "BOOKING_EXPIRED")] | length > 0')"

section "B. Expiry job run twice"
api POST "$SCHEDULING_URL/api/v1/admin/holds/expire-now"; run1="$(field .expired)"
api POST "$SCHEDULING_URL/api/v1/admin/holds/expire-now"; run2="$(field .expired)"
echo "  run 1 expired $run1 hold(s) (the background worker had already done the work), run 2 expired $run2"
check "second run expires nothing" 0 "$run2"
check "slot A still 0 reserved (no double release)" 0 "$(slot_reserved "$a_slot")"

section "C. Paid hold is never released by expiry"
check "hold C" CONFIRMED "$(hold_status "$c_hold")"
check "slot C still reserved" 1 "$(slot_reserved "$c_slot")"
check "booking C" CONFIRMED "$(booking_status "$c_booking")"

section "Database state (scheduling_db)"
sql_table scheduling "SELECT h.booking_id, h.status, h.expires_at, s.capacity, s.reserved_count FROM holds h JOIN slots s ON s.id = h.slot_id WHERE h.id IN ('$a_hold', '$c_hold', '$d_hold', '$e_hold') ORDER BY h.created_at;"
section "Database state (booking_db)"
sql_table booking "SELECT id, status, refunded_minor, failure_reason FROM bookings WHERE id IN ('$a_booking', '$c_booking', '$d_booking', '$e_booking') ORDER BY created_at;"

section "Expiry log lines"
log_lines slothub-scheduling "$started_at" "expired|expiry sweep"
log_lines slothub-booking "$started_at" "$a_booking|$e_booking|Payment still processing"
log_lines slothub-payment "$started_at" "$e_booking|Idempotency key \"pay-$e_booking|refund-$e_booking"

finish
