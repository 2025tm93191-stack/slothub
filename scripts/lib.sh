#!/usr/bin/env bash
# Shared helpers for the SlotHub test and challenge scripts. Source it: . "$(dirname "$0")/lib.sh"
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SCHEDULING_URL="${SCHEDULING_URL:-http://localhost:5101}"
BOOKING_URL="${BOOKING_URL:-http://localhost:5102}"
PAYMENT_URL="${PAYMENT_URL:-http://localhost:5103}"
NOTIFICATION_URL="${NOTIFICATION_URL:-http://localhost:5104}"
PG_CONTAINER="${PG_CONTAINER:-slothub-postgres}"
# How to run psql inside the database container. Kubernetes: PG_EXEC="kubectl -n slothub exec -i postgres-0 --"
read -r -a PG_EXEC_CMD <<<"${PG_EXEC:-docker exec $PG_CONTAINER}"

WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT
CHECKS_FAILED=0
CHECKS_PASSED=0
REQ=0

# start_evidence <evidence-subdir> <run-name>: tee all output into evidence/<subdir>/<run-name>-<timestamp>.txt
start_evidence() {
  RUN_ID="$2-$(date -u +%Y%m%dT%H%M%SZ)"
  local dir="$ROOT/evidence/$1"
  mkdir -p "$dir"
  EVIDENCE_FILE="$dir/run-${RUN_ID#"$2"-}.txt"
  if [[ -n "${EVIDENCE_NESTED:-}" ]]; then
    EVIDENCE_FILE="(included in the calling script's evidence file)"   # called from another evidence script
  else
    exec > >(tee "$EVIDENCE_FILE") 2>&1
    export EVIDENCE_NESTED=1   # scripts we call print into this file instead of writing their own
  fi
  echo "Run:      $RUN_ID"
  echo "Started:  $(date -u +%Y-%m-%dT%H:%M:%SZ)"
  echo "Command:  ${SCRIPT_COMMAND:-$0}"
  echo
}

uuid() { uuidgen | tr '[:upper:]' '[:lower:]'; }

section() { printf '\n== %s ==\n' "$*"; }

# api <METHOD> <URL> [JSON body] [Idempotency-Key]
# Sets HTTP_STATUS, HTTP_BODY, HTTP_REPLAYED and prints a one-line trace.
api() {
  local method="$1" url="$2" body="${3:-}" key="${4:-}"
  REQ=$((REQ + 1))
  local args=(-s -X "$method" -D "$WORK/h" -o "$WORK/b" -w '%{http_code}' -H "X-Correlation-Id: ${RUN_ID:-script}-$REQ")
  [[ -n "$body" ]] && args+=(-H 'Content-Type: application/json' -d "$body")
  [[ -n "$key" ]] && args+=(-H "Idempotency-Key: $key")
  HTTP_STATUS="$(curl "${args[@]}" "$url")"
  HTTP_BODY="$(cat "$WORK/b")"
  HTTP_REPLAYED="$({ grep -i '^idempotency-replayed:' "$WORK/h" || true; } | tr -d '\r' | awk '{print $2}')"
  printf '%s %s -> %s%s %s\n' "$method" "${url#http://localhost:}" "$HTTP_STATUS" \
    "${HTTP_REPLAYED:+ (replayed)}" "$(jq -c '{status, code, id} | with_entries(select(.value != null))' <<<"$HTTP_BODY" 2>/dev/null || true)"
}

field() { jq -r "$1" <<<"$HTTP_BODY"; }

# check <description> <expected> <actual>
check() {
  if [[ "$2" == "$3" ]]; then
    CHECKS_PASSED=$((CHECKS_PASSED + 1))
    printf '  PASS  %s (= %s)\n' "$1" "$3"
  else
    CHECKS_FAILED=$((CHECKS_FAILED + 1))
    printf '  FAIL  %s: expected %s, got %s\n' "$1" "$2" "$3"
  fi
}

# sql <service> <query>: runs in that service's own database as that service's own user
sql() {
  "${PG_EXEC_CMD[@]}" psql -U "$1_user" -d "$1_db" -At -F ' | ' -c "$2"
}

sql_table() {
  "${PG_EXEC_CMD[@]}" psql -U "$1_user" -d "$1_db" -c "$2"
}

# wait_for <description> <timeout-seconds> <command...>: polls until the command prints "true"
wait_for() {
  local description="$1" timeout="$2"
  shift 2
  for ((i = 0; i < timeout; i++)); do
    if [[ "$("$@")" == "true" ]]; then
      echo "  ... $description after ${i}s"
      return 0
    fi
    sleep 1
  done
  echo "  ... gave up waiting for $description after ${timeout}s"
  return 0
}

body_hash() { jq -S -c . <<<"$1" | shasum -a 256 | cut -c1-16; }

# log_lines <container> <since> <regex>: structured log lines as "time correlationId message"
log_lines() {
  if docker inspect "$1" >/dev/null 2>&1; then
    echo "-- $1"
    docker logs --since "$2" "$1" 2>&1 |
      jq -Rr --arg re "$3" 'fromjson? | select(.["@m"] | test($re)) | "\(.["@t"][11:23]) \(.CorrelationId // "-") \(.["@m"])"' |
      tail -30
  else
    echo "-- $1 is not running in Docker; see that service's console output"
  fi
}

seed_slot() { "$ROOT/scripts/seed.sh" --capacity "${1:-1}" --slots 1 --price "${2:-50000}"; }

customer_json() { echo "{\"id\":\"cust-$1\",\"phone\":\"+9198000$(printf '%05d' $((RANDOM % 100000)))\",\"email\":\"$1@example.com\"}"; }

set_chaos() { # set_chaos <payment|notification> <rate> <latencyMs> <mode>
  local url="$PAYMENT_URL"
  [[ "$1" == "notification" ]] && url="$NOTIFICATION_URL"
  curl -s -o /dev/null -X PUT "$url/api/v1/admin/chaos" -H 'Content-Type: application/json' \
    -d "{\"failureRate\":$2,\"latencyMs\":$3,\"mode\":\"$4\"}"
  echo "  (chaos on $1: failureRate=$2 latencyMs=$3 mode=$4)"
}

finish() {
  echo
  echo "Checks passed: $CHECKS_PASSED, failed: $CHECKS_FAILED"
  echo "Finished: $(date -u +%Y-%m-%dT%H:%M:%SZ)"
  echo "Saved to: ${EVIDENCE_FILE#"$ROOT"/}"
  if [[ "$CHECKS_FAILED" -gt 0 ]]; then
    echo "RESULT: FAIL"
    exit 1
  fi
  echo "RESULT: PASS"
}
