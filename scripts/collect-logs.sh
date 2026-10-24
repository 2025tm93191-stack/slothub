#!/usr/bin/env bash
# Saves each service's raw structured (JSON) logs to evidence/logs/<service>-<timestamp>.log
#   ./scripts/collect-logs.sh [--since 30m]       (services must be running in Docker)
set -euo pipefail
since="30m"
[[ "${1:-}" == "--since" ]] && since="$2"
root="$(cd "$(dirname "$0")/.." && pwd)"
ts="$(date -u +%Y%m%dT%H%M%SZ)"
mkdir -p "$root/evidence/logs"
for service in scheduling booking payment notification; do
  file="$root/evidence/logs/$service-$ts.log"
  docker logs --since "$since" "slothub-$service" 2>&1 | grep '^{' >"$file" || true
  echo "evidence/logs/$service-$ts.log  ($(wc -l <"$file" | tr -d ' ') lines)"
done
