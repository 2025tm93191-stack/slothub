#!/usr/bin/env bash
# Assignment 2 — Task 3 (situated learning): reproduce, diagnose and fix a genuine deployment problem.
#
# The problem we actually hit in Assignment 1: the PostgreSQL container would not start because another
# PostgreSQL on the developer's machine already used the host port (5432, then 5433). The application
# worked in development (`dotnet run` against that local database) but `docker compose up` failed.
#
# This script recreates it safely: a throw-away listener occupies the database's host port, then
#   1. Problem    — docker compose up fails
#   2. Diagnosis  — docker compose ps -a, docker inspect, docker compose logs, lsof
#   3. Fix        — publish PostgreSQL on a free host port via POSTGRES_PORT (configuration, not code)
#   4. Validation — all containers healthy, main workflow works
#   5. Clean-up   — listener stopped, default port restored
#
#   ./scripts/a2-troubleshoot-demo.sh       → evidence/a2-task3-troubleshooting/
SCRIPT_COMMAND="scripts/a2-troubleshoot-demo.sh"
. "$(dirname "$0")/lib.sh"
start_evidence a2-task3-troubleshooting troubleshoot
cd "$ROOT"

port="$(grep -E '^POSTGRES_PORT=' .env 2>/dev/null | cut -d= -f2)"
port="${port:-5440}"
fixed_port=$((port + 1))
squatter_pid=""
cleanup() {
  if [[ -n "$squatter_pid" ]]; then
    kill "$squatter_pid" 2>/dev/null || true
    wait "$squatter_pid" 2>/dev/null || true
  fi
}
trap 'cleanup; rm -rf "$WORK"' EXIT

section "0. Starting point: deployment stopped, another program takes host port $port"
docker compose down 2>&1 | grep -E 'Removed' | sort -u || true
# Dual-stack (IPv4 + IPv6) listener, like a locally installed PostgreSQL. An IPv4-only listener does not collide
# with Docker Desktop on macOS, which publishes ports on [::] — an extra lesson we learned while scripting this.
python3 -c "
import socket, time
s = socket.socket(socket.AF_INET6); s.setsockopt(socket.IPPROTO_IPV6, socket.IPV6_V6ONLY, 0)
s.bind(('::', $port)); s.listen(); time.sleep(3600)" &
squatter_pid=$!
sleep 1
echo "A local process (pid $squatter_pid) now listens on port $port — like the developer's own PostgreSQL did."

section "1. Problem: docker compose up fails"
set +e
docker compose up -d --wait >"$WORK/up.log" 2>&1
up_status=$?
set -e
sed 's/^/  /' "$WORK/up.log" | grep -vE '^\s*$' | tail -8
check "docker compose up exits with an error" true "$([[ $up_status -ne 0 ]] && echo true || echo false)"
check "error is about the port" true "$(grep -qiE 'port is already allocated|address already in use|ports are not available' "$WORK/up.log" && echo true || echo false)"

section "2. Diagnosis"
echo "\$ docker compose ps -a        # which containers exist and in what state?"
docker compose ps -a --format 'table {{.Name}}\t{{.State}}\t{{.Status}}' | sed 's/^/  /'
echo
echo "\$ docker inspect slothub-postgres --format '{{.State.Status}} | exit={{.State.ExitCode}} | {{.State.Error}}'"
docker inspect slothub-postgres --format '  {{.State.Status}} | exit={{.State.ExitCode}} | {{.State.Error}}'
pg_state="$(docker inspect slothub-postgres --format '{{.State.Status}}')"
check "postgres container never reached running" true "$([[ "$pg_state" != running ]] && echo true || echo false)"
echo
echo "\$ docker inspect slothub-postgres --format '{{json .HostConfig.PortBindings}}'   # what does it try to bind?"
docker inspect slothub-postgres --format '  {{json .HostConfig.PortBindings}}'
echo
echo "\$ docker compose logs postgres   # empty: the process never started, so the cause is not inside the container"
logs_lines="$(docker compose logs --no-log-prefix postgres 2>/dev/null | wc -l | tr -d ' ')"
echo "  ($logs_lines log lines)"
echo
echo "\$ lsof -nP -iTCP:$port -sTCP:LISTEN   # who holds the host port?"
lsof -nP -iTCP:"$port" -sTCP:LISTEN 2>/dev/null | sed 's/^/  /' || echo "  (lsof not available)"
check "a non-Docker process holds port $port" true "$(lsof -nP -iTCP:"$port" -sTCP:LISTEN 2>/dev/null | grep -q "$squatter_pid" && echo true || echo false)"
echo
echo "Cause: the host port in the 'ports:' mapping (\${POSTGRES_PORT}:5432) is taken by a program outside Docker."
echo "The services themselves reach the database on the internal network as postgres:5432, which is not affected;"
echo "the host port only exists so developers' tools (psql, scripts) can reach the database."

section "3. Fix: publish PostgreSQL on a free host port (configuration only, no code or image change)"
echo "\$ POSTGRES_PORT=$fixed_port docker compose up -d --wait     # permanent fix: set POSTGRES_PORT in .env"
docker compose down >/dev/null 2>&1 || true
POSTGRES_PORT=$fixed_port docker compose up -d --wait 2>&1 | grep -E 'Healthy' | sort -u | sed 's/^/  /'
docker compose ps --format 'table {{.Name}}\t{{.Status}}\t{{.Ports}}' | sed 's/^/  /'

section "4. Validation"
for c in postgres scheduling booking payment notification; do
  check "slothub-$c healthy" healthy "$(docker inspect --format '{{.State.Health.Status}}' "slothub-$c")"
done
seed="$(seed_slot 1 50000)"
api POST "$BOOKING_URL/api/v1/bookings" "{\"tenantId\":\"$(jq -r .tenantId <<<"$seed")\",\"slotId\":\"$(jq -r '.slotIds[0]' <<<"$seed")\",\"customer\":$(customer_json fix)}" "$(uuid)"
booking="$(field .id)"
api POST "$BOOKING_URL/api/v1/bookings/$booking/payment" "" "$(uuid)"
check "main workflow after the fix" CONFIRMED "$(field .status)"
check "database reachable from the host on the new port" true \
  "$(python3 -c "import socket; socket.create_connection(('127.0.0.1', $fixed_port), 2)" 2>/dev/null && echo true || echo false)"

section "5. Clean-up: stop the listener, return to the default port"
cleanup
squatter_pid=""
docker compose up -d --wait 2>&1 | grep -E 'Healthy' | sort -u | sed 's/^/  /'
check "back on the default port $port and healthy" healthy "$(docker inspect --format '{{.State.Health.Status}}' slothub-postgres)"

finish
