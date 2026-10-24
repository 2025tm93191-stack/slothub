#!/usr/bin/env bash
# Exports each running service's OpenAPI document to docs/openapi/<service>.json
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
mkdir -p "$root/docs/openapi"
for pair in scheduling:5101 booking:5102 payment:5103 notification:5104; do
  name="${pair%%:*}" port="${pair##*:}"
  curl -sf "http://localhost:$port/openapi/v1.json" | jq . >"$root/docs/openapi/$name.json"
  echo "docs/openapi/$name.json  ($(jq '.paths | length' "$root/docs/openapi/$name.json") paths)"
done
