#!/usr/bin/env bash
# End-to-end test of the main transaction across all four services:
#   create booking (Scheduling hold) → pay (Payment) → confirm (Scheduling) → notify (Notification via outbox)
# plus two failure paths: payment declined (slot released) and payment gateway down (retry is safe).
#
#   ./scripts/e2e-booking.sh        output saved to evidence/tests/
SCRIPT_COMMAND="scripts/e2e-booking.sh"
. "$(dirname "$0")/lib.sh"
start_evidence tests e2e

section "0. All services healthy"
for url in "$SCHEDULING_URL" "$BOOKING_URL" "$PAYMENT_URL" "$NOTIFICATION_URL"; do
  api GET "$url/health/ready"
  check "$(field .service) ready" "HEALTHY" "$(field .status)"
done
set_chaos payment 0 0 DECLINE >/dev/null

section "1. Happy path: hold → pay → confirm → notify"
seed="$(seed_slot 1 50000)"
tenant="$(jq -r .tenantId <<<"$seed")"
slot="$(jq -r '.slotIds[0]' <<<"$seed")"
api POST "$BOOKING_URL/api/v1/bookings" "{\"tenantId\":\"$tenant\",\"slotId\":\"$slot\",\"customer\":$(customer_json e2e1)}" "$(uuid)"
check "create booking → 201" 201 "$HTTP_STATUS"
check "booking status" HELD "$(field .status)"
booking="$(field .id)"
hold="$(field .holdId)"

api POST "$BOOKING_URL/api/v1/bookings/$booking/payment" "" "$(uuid)"
check "pay → 200" 200 "$HTTP_STATUS"
check "booking status" CONFIRMED "$(field .status)"
check "amount charged" 50000 "$(field .amountMinor)"

api GET "$SCHEDULING_URL/api/v1/holds/$hold"
check "hold status in Scheduling" CONFIRMED "$(field .status)"
api GET "$SCHEDULING_URL/api/v1/slots/$slot"
check "slot reserved" 1 "$(field .reserved)"
api GET "$PAYMENT_URL/api/v1/payments?bookingId=$booking"
check "payments for booking" 1 "$(field length)"
check "payment status" SUCCEEDED "$(field '.[0].status')"

notification_sent() {
  curl -s "$NOTIFICATION_URL/api/v1/notifications?bookingId=$booking" |
    jq '[.items[] | select(.eventType == "BOOKING_CONFIRMED" and .status == "SENT")] | length == 2'
}
wait_for "confirmation SMS + email sent" 20 notification_sent
api GET "$NOTIFICATION_URL/api/v1/notifications?bookingId=$booking"
check "BOOKING_CONFIRMED notifications sent (SMS + EMAIL)" 2 "$(field '[.items[] | select(.eventType == "BOOKING_CONFIRMED" and .status == "SENT")] | length')"
echo "  message: $(field '.items[0].body')"

section "2. Failure path: payment declined → booking FAILED, slot released"
seed="$(seed_slot 1 50000)"
tenant="$(jq -r .tenantId <<<"$seed")"
slot="$(jq -r '.slotIds[0]' <<<"$seed")"
api POST "$BOOKING_URL/api/v1/bookings" "{\"tenantId\":\"$tenant\",\"slotId\":\"$slot\",\"customer\":$(customer_json e2e2)}" "$(uuid)"
booking="$(field .id)"
api GET "$SCHEDULING_URL/api/v1/slots/$slot"
check "slot reserved while HELD" 1 "$(field .reserved)"
set_chaos payment 1 0 DECLINE
api POST "$BOOKING_URL/api/v1/bookings/$booking/payment" "" "$(uuid)"
check "pay → 402" 402 "$HTTP_STATUS"
check "error code" PAYMENT_DECLINED "$(field .code)"
set_chaos payment 0 0 DECLINE
api GET "$BOOKING_URL/api/v1/bookings/$booking"
check "booking status" FAILED "$(field .status)"
api GET "$SCHEDULING_URL/api/v1/slots/$slot"
check "slot capacity restored" 0 "$(field .reserved)"
api GET "$PAYMENT_URL/api/v1/payments?bookingId=$booking"
check "declined payment recorded" FAILED "$(field '.[0].status')"

section "3. Failure path: payment gateway down → 503, booking stays HELD; retry with the same key succeeds once"
seed="$(seed_slot 1 50000)"
tenant="$(jq -r .tenantId <<<"$seed")"
slot="$(jq -r '.slotIds[0]' <<<"$seed")"
api POST "$BOOKING_URL/api/v1/bookings" "{\"tenantId\":\"$tenant\",\"slotId\":\"$slot\",\"customer\":$(customer_json e2e3)}" "$(uuid)"
booking="$(field .id)"
pay_key="$(uuid)"
set_chaos payment 1 0 ERROR
api POST "$BOOKING_URL/api/v1/bookings/$booking/payment" "" "$pay_key"
check "pay while gateway down → 503 (after retries)" 503 "$HTTP_STATUS"
api GET "$BOOKING_URL/api/v1/bookings/$booking"
check "booking still HELD" HELD "$(field .status)"
set_chaos payment 0 0 DECLINE
api POST "$BOOKING_URL/api/v1/bookings/$booking/payment" "" "$pay_key"
if [[ "$HTTP_STATUS" == 503 ]]; then
  # The repeated failures opened Booking's circuit breaker to Payment: calls fail fast instead of piling onto
  # a struggling gateway. After the break (15 s) one trial call is let through and the circuit closes again.
  check "immediately after recovery the circuit is still open" DOWNSTREAM_CIRCUIT_OPEN "$(field .code)"
  echo "  waiting 16 s for the circuit breaker to half-open..."
  sleep 16
  api POST "$BOOKING_URL/api/v1/bookings/$booking/payment" "" "$pay_key"
fi
check "retry same key after recovery → 200" 200 "$HTTP_STATUS"
check "booking status" CONFIRMED "$(field .status)"
api GET "$PAYMENT_URL/api/v1/payments?bookingId=$booking"
check "exactly one successful charge" 1 "$(field '[.[] | select(.status == "SUCCEEDED")] | length')"

finish
