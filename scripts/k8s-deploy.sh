#!/usr/bin/env bash
# Deploys SlotHub to Minikube.
#   1. start Minikube if needed            4. PostgreSQL StatefulSet (+ ConfigMaps), wait until ready
#   2. build images, stream them into the  5. migration Jobs, wait until complete
#      Minikube node (no registry needed)  6. Deployments + Services, wait for rollout
#   3. namespace, Secret from .env, DB init ConfigMap
#
#   ./scripts/k8s-deploy.sh [--skip-build]
# Then, in a separate terminal:  minikube tunnel   → APIs on localhost:5101-5104 (stop docker compose first)
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
cd "$root"
ns=slothub
k8s="$root/deploy/k8s"
services=(scheduling booking payment notification)
skip_build=false
[[ "${1:-}" == "--skip-build" ]] && skip_build=true

[[ -f .env ]] || { echo "Missing .env — run: cp .env.example .env (and set the passwords)" >&2; exit 1; }
version="$(grep -E '^SLOTHUB_VERSION=' .env | cut -d= -f2)"
version="${version:-1.0.0}"

step() { printf '\n== %s ==\n' "$*"; }

step "1. Minikube"
if ! minikube status --format '{{.Host}}' 2>/dev/null | grep -q Running; then
  minikube start --driver=docker --cpus=4 --memory=4096
fi
kubectl config use-context minikube >/dev/null
kubectl get nodes

step "2. Images (built with Docker, streamed into the Minikube node's containerd)"
if [[ "$skip_build" == false ]]; then
  GIT_REVISION="$(git rev-parse --short HEAD 2>/dev/null || echo unknown)" docker compose build
fi
for s in "${services[@]}"; do
  image="slothub-$s:$version"
  echo "  loading $image"
  docker save "$image" | docker exec -i minikube ctr -n k8s.io images import --digests - >/dev/null
done
docker exec minikube ctr -n k8s.io images ls -q | grep -E 'slothub-' | sed 's/^/  /'

step "3. Namespace, Secret (from .env) and database init script"
kubectl apply -f "$k8s/00-namespace.yaml"
secret_env="$(mktemp)"
trap 'rm -f "$secret_env"' EXIT
grep -E '^(POSTGRES_ADMIN_USER|[A-Z_]+_PASSWORD)=' .env >"$secret_env"
kubectl -n "$ns" create secret generic slothub-db --from-env-file="$secret_env" --dry-run=client -o yaml | kubectl apply -f -
kubectl -n "$ns" create configmap postgres-init --from-file="$root/infra/postgres/init/01-create-databases.sh" \
  --dry-run=client -o yaml | kubectl apply -f -

step "4. PostgreSQL and configuration"
kubectl apply -f "$k8s/10-postgres.yaml" -f "$k8s/20-config.yaml"
kubectl -n "$ns" rollout status statefulset/postgres --timeout=300s

step "5. Database migrations (one Job per service)"
kubectl -n "$ns" delete job -l app.kubernetes.io/part-of=slothub --ignore-not-found
kubectl apply -f "$k8s/30-migrations.yaml"
kubectl -n "$ns" wait --for=condition=complete job --all --timeout=300s

step "6. Services"
for f in "$k8s"/4*.yaml; do kubectl apply -f "$f"; done
for s in "${services[@]}"; do kubectl -n "$ns" rollout status "deployment/$s" --timeout=300s; done

step "Deployed"
kubectl -n "$ns" get pods,svc -o wide
cat <<EOF

Reach the APIs from this machine:
  1. stop Docker Compose if it is running (same ports):  docker compose stop
  2. in another terminal:                                 minikube tunnel
  3. APIs on http://localhost:5101 (Scheduling), 5102 (Booking), 5103 (Payment), 5104 (Notification)
  Run scripts against Kubernetes with:  export PG_EXEC="kubectl -n $ns exec -i postgres-0 --"
EOF
