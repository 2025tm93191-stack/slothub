#!/usr/bin/env bash
# Assignment 2 — Task 1 evidence: build one image per service, start the containers, show them healthy,
# and prove that no password or .env file is inside an image or committed to the repository.
#
#   ./scripts/a2-build-evidence.sh [--no-cache]     output saved to evidence/a2-task1-containerization/
no_cache=""
[[ "${1:-}" == "--no-cache" ]] && no_cache="--no-cache"
SCRIPT_COMMAND="scripts/a2-build-evidence.sh ${no_cache}"
. "$(dirname "$0")/lib.sh"
start_evidence a2-task1-containerization build
cd "$ROOT"
services=(scheduling booking payment notification)
version="$(grep -E '^SLOTHUB_VERSION=' .env 2>/dev/null | cut -d= -f2)"
version="${version:-1.0.0}"
export GIT_REVISION="$(git rev-parse --short HEAD 2>/dev/null || echo unknown)"

section "1. Build one image per service (docker compose build ${no_cache})"
start=$(date +%s)
build_log="${EVIDENCE_FILE%.txt}-docker-build.log"
docker compose --progress plain build $no_cache >"$build_log" 2>&1
build_status=$?
grep -E '^#[0-9]+ \[[a-z]+ (build|stage-1) +[0-9]+/[0-9]+\]' "$build_log" | sed -E 's/^#[0-9]+ //' | sort -u | head -40
grep -E 'naming to|exporting layers|ERROR' "$build_log" | sed -E 's/^#[0-9]+ //' | sort -u
echo "  full build log: ${build_log#"$ROOT"/}"
check "build finished" 0 "$build_status"
echo "  build took $(( $(date +%s) - start )) s"

section "2. Images (separate image per service)"
docker images --format 'table {{.Repository}}:{{.Tag}}\t{{.ID}}\t{{.Size}}\t{{.CreatedSince}}' | grep -E 'REPOSITORY|slothub-'
for s in "${services[@]}"; do
  labels="$(docker image inspect "slothub-$s:$version" --format '{{index .Config.Labels "org.opencontainers.image.version"}} {{index .Config.Labels "org.opencontainers.image.revision"}} user={{.Config.User}} health={{json .Config.Healthcheck.Test}}')"
  echo "  slothub-$s:$version  version/revision: $labels"
  check "slothub-$s:$version exists" true "$(docker image inspect "slothub-$s:$version" >/dev/null 2>&1 && echo true || echo false)"
done

section "3. Start the containers and wait until healthy (docker compose up -d --wait)"
docker compose up -d --wait 2>&1 | grep -E 'Container .* (Started|Healthy|Running)' | sort -u
docker compose ps --format 'table {{.Name}}\t{{.Image}}\t{{.Status}}\t{{.Ports}}'
for s in postgres "${services[@]}"; do
  check "slothub-$s health" healthy "$(docker inspect --format '{{.State.Health.Status}}' "slothub-$s")"
done

section "4. No secrets inside the images"
for s in "${services[@]}"; do
  image="slothub-$s:$version"
  baked_env="$(docker image inspect "$image" --format '{{range .Config.Env}}{{println .}}{{end}}' | grep -ciE 'password|connectionstrings' || true)"
  history_hits="$(docker history --no-trunc --format '{{.CreatedBy}}' "$image" | grep -ciE 'password|\.env\b' || true)"
  env_files="$(docker run --rm --entrypoint sh "$image" -c 'find / -xdev -name ".env" -o -xdev -name "*.env" 2>/dev/null | wc -l' | tr -d ' ')"
  echo "  $image: env vars with passwords=$baked_env, history lines mentioning passwords/.env=$history_hits, .env files in filesystem=$env_files"
  check "$image has no baked-in password env var" 0 "$baked_env"
  check "$image has no .env file" 0 "$env_files"
done
echo "  Passwords reach the containers only at run time from .env, e.g. (value masked):"
docker inspect slothub-booking --format '{{range .Config.Env}}{{println .}}{{end}}' | grep ConnectionStrings | sed -E 's/Password=[^;]*/Password=****/'

section "5. No secrets committed to the repository"
check ".env is git-ignored" ".env" "$(git check-ignore .env || echo NOT-IGNORED)"
committed_env="$(git ls-files | { grep -E '(^|/)\.env$' || true; } | wc -l | tr -d ' ')"
check "no .env file tracked by git" 0 "$committed_env"
echo "  .env.example files contain placeholders only:"
{ git grep -hE 'PASSWORD=|Password=' -- '*.env.example' || true; } | sed -E 's/.*(PASSWORD=|Password=)([^;]*).*/    \2/' | sort -u
check "docker-compose.yml has no fallback passwords" 0 "$(grep -cE '_PASSWORD:-' docker-compose.yml || true)"
check ".dockerignore excludes .env" true "$(grep -qxE '\*\*/\.env' .dockerignore && echo true || echo false)"

finish
