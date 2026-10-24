#!/usr/bin/env bash
# Runs every automated check against the running Docker stack and collects the logs.
# Use it before the demo / submission to regenerate all evidence in one go (~3 minutes).
#
#   docker compose up -d --build --wait && ./scripts/run-all-checks.sh
set -uo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
cd "$root"
results=()

run() {
  local name="$1"
  shift
  echo
  echo "################ $name ################"
  if "$@"; then results+=("PASS  $name"); else results+=("FAIL  $name"); fi
}

run "Unit + integration tests" bash -c 'ts=$(date -u +%Y%m%dT%H%M%SZ); dotnet test SlotHub.slnx 2>&1 | grep -E "Passed!|Failed!" | tee evidence/tests/unit-and-integration-$ts.txt; ! grep -q "Failed!" evidence/tests/unit-and-integration-$ts.txt'
run "End-to-end (main transaction + failures)" ./scripts/e2e-booking.sh
run "Challenge 1 capacity 1 / 20 requests" ./scripts/challenge1-concurrency.sh --capacity 1 --requests 20
run "Challenge 1 capacity 5 / 30 requests" ./scripts/challenge1-concurrency.sh --capacity 5 --requests 30
run "Challenge 1 naive counter-example" ./scripts/challenge1-naive-demo.sh --capacity 1 --requests 20
run "Challenge 2 idempotency" ./scripts/challenge2-idempotency.sh
run "Challenge 3B time-bound holds" ./scripts/challenge3b-holds.sh
run "Postman collection (newman)" bash -c 'ts=$(date -u +%Y%m%dT%H%M%SZ); npx -y newman@6 run postman/SlotHub.postman_collection.json -e postman/SlotHub.local.postman_environment.json --reporters cli --color off 2>/dev/null | tee evidence/tests/postman-newman-$ts.txt | tail -20; exit ${PIPESTATUS[0]}'
run "Collect service logs" ./scripts/collect-logs.sh --since 15m

echo
echo "################ Summary ################"
printf '%s\n' "${results[@]}"
! printf '%s\n' "${results[@]}" | grep -q '^FAIL'
