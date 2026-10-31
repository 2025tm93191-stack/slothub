#!/usr/bin/env bash
# Removes SlotHub from Minikube.
#   ./scripts/k8s-teardown.sh            delete the slothub namespace (all workloads, Services, Secret, PVC data)
#   ./scripts/k8s-teardown.sh --stop     also stop Minikube (frees CPU/memory; images and cluster kept)
#   ./scripts/k8s-teardown.sh --delete   also delete the Minikube cluster (frees its disk space)
set -euo pipefail
kubectl delete namespace slothub --ignore-not-found --wait=true
case "${1:-}" in
  --stop) minikube stop ;;
  --delete) minikube delete ;;
esac
