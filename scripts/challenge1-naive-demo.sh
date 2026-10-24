#!/usr/bin/env bash
# Challenge 1 counter-example: why a plain check-then-update in application code fails.
# Starts a second Scheduling container with CONCURRENCY_MODE=naive on port 5191 (same database),
# fires the same parallel load as challenge1-concurrency.sh at it, then removes the container.
#
#   ./scripts/challenge1-naive-demo.sh [--capacity 1] [--requests 20]      (requires: docker compose up)
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
container=slothub-scheduling-naive

docker rm -f "$container" >/dev/null 2>&1 || true
docker run -d --name "$container" --network slothub_default -p 5191:8080 \
  -e "ConnectionStrings__Default=Host=postgres;Port=5432;Database=scheduling_db;Username=scheduling_user;Password=${SCHEDULING_DB_PASSWORD:-scheduling_pw};GSS Encryption Mode=Disable" \
  -e DB_MIGRATE_ON_STARTUP=false -e HOLD_EXPIRY_WORKER_ENABLED=false -e CONCURRENCY_MODE=naive \
  slothub-scheduling >/dev/null
trap 'docker rm -f "$container" >/dev/null 2>&1 || true' EXIT

for _ in $(seq 1 30); do curl -sf localhost:5191/health >/dev/null && break; sleep 1; done
SCHEDULING_URL=http://localhost:5191 "$root/scripts/challenge1-concurrency.sh" --naive "$@"
