#!/usr/bin/env bash
# Creates a tenant, resource, offering and N slots in the Scheduling service and prints their IDs as JSON.
#
#   ./scripts/seed.sh [--capacity 5] [--slots 3] [--price 50000]
#
# Each run creates a fresh tenant and resource, so runs never collide.
set -euo pipefail

capacity=1
slot_count=1
price=50000
while [[ $# -gt 0 ]]; do
  case "$1" in
    --capacity) capacity="$2"; shift 2 ;;
    --slots) slot_count="$2"; shift 2 ;;
    --price) price="$2"; shift 2 ;;
    *) echo "Unknown option: $1" >&2; exit 2 ;;
  esac
done

url="${SCHEDULING_URL:-http://localhost:5101}"

post() {
  curl -sS --fail-with-body -H 'Content-Type: application/json' -H "X-Correlation-Id: seed-$$" -X POST "$url$1" -d "$2"
}

suffix="$(date -u +%H%M%S)-$$"
tenant_id=$(post /api/v1/tenants "{\"name\":\"Seed Clinic $suffix\",\"timeZone\":\"Asia/Kolkata\"}" | jq -r .id)
resource_id=$(post "/api/v1/tenants/$tenant_id/resources" "{\"name\":\"Dr. Seed $suffix\",\"type\":\"person\"}" | jq -r .id)
offering_id=$(post "/api/v1/tenants/$tenant_id/offerings" \
  "{\"name\":\"Consultation\",\"durationMinutes\":30,\"priceMinor\":$price,\"currency\":\"INR\"}" | jq -r .id)

# Slots start tomorrow on the hour, back to back (portable: jq does the date maths on macOS and Linux).
slots_json=$(jq -nc --argjson n "$slot_count" --argjson cap "$capacity" '
  (now + 86400 | floor | . - (. % 3600)) as $start
  | [range(0; $n) | {startAt: (($start + . * 1800) | todate), capacity: $cap}]')

slot_ids=$(post /api/v1/slots "{\"resourceId\":\"$resource_id\",\"offeringId\":\"$offering_id\",\"slots\":$slots_json}" | jq -c '[.[].id]')

jq -n \
  --arg tenantId "$tenant_id" --arg resourceId "$resource_id" --arg offeringId "$offering_id" \
  --argjson slotIds "$slot_ids" --argjson capacity "$capacity" --argjson priceMinor "$price" \
  '{tenantId: $tenantId, resourceId: $resourceId, offeringId: $offeringId, slotIds: $slotIds, capacity: $capacity, priceMinor: $priceMinor}'
