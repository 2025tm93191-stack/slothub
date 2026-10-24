#!/usr/bin/env bash
# Runs one service locally: ./scripts/run-service.sh scheduling|booking|payment|notification
# Creates services/<name>/.env from .env.example on first run. The service reads it at startup.
set -euo pipefail

name="${1:?usage: run-service.sh <scheduling|booking|payment|notification>}"
root="$(cd "$(dirname "$0")/.." && pwd)"
service_dir="$root/services/$name"
project="$(ls "$service_dir"/src/*.Api/*.Api.csproj 2>/dev/null | head -1)"

if [[ -z "$project" ]]; then
  echo "Unknown service '$name'" >&2
  exit 1
fi

if [[ ! -f "$service_dir/.env" ]]; then
  cp "$service_dir/.env.example" "$service_dir/.env"
  echo "Created $service_dir/.env from .env.example"
fi

exec dotnet run --project "$project" --no-launch-profile
