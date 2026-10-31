#!/usr/bin/env bash
# Container health check for the SlotHub services.
# The .NET runtime image has no curl/wget, so this sends a plain HTTP GET over bash's built-in /dev/tcp.
# Healthy (exit 0) only when /health/ready answers 200: the service is up AND its own database is reachable.
set -eu

port="${HEALTH_PORT:-8080}"
path="${HEALTH_PATH:-/health/ready}"

exec 3<>"/dev/tcp/127.0.0.1/${port}"
printf 'GET %s HTTP/1.1\r\nHost: localhost\r\nConnection: close\r\n\r\n' "$path" >&3
IFS= read -r -t 3 status_line <&3

[[ "$status_line" == *" 200 "* ]]
