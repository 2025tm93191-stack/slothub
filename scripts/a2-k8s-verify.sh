#!/usr/bin/env bash
# Assignment 2 — Part B: SlotHub deployed on Minikube, and the main workflow working THROUGH Kubernetes.
#   1. cluster objects (Deployments, StatefulSet, Jobs, Services, PVC, pods)   2. readiness through the Services
#   3. one booking traced across pods by correlation ID (kubectl logs)        4. e2e-booking.sh  5. Challenge 1
#
#   ./scripts/k8s-deploy.sh, then `minikube tunnel` in another terminal, then:
#   ./scripts/a2-k8s-verify.sh        → evidence/a2-advanced-k8s/
export PG_EXEC="${PG_EXEC:-kubectl -n slothub exec -i postgres-0 --}"
SCRIPT_COMMAND="scripts/a2-k8s-verify.sh"
. "$(dirname "$0")/lib.sh"
start_evidence a2-advanced-k8s verify
ns=slothub

section "1. Kubernetes objects"
kubectl get nodes -o wide
echo
kubectl -n "$ns" get deployments,statefulsets,jobs,pvc
echo
kubectl -n "$ns" get pods -o wide
echo
kubectl -n "$ns" get services
for d in scheduling booking payment notification; do
  check "deployment/$d available" true "$([[ "$(kubectl -n "$ns" get deploy "$d" -o jsonpath='{.status.availableReplicas}')" -ge 1 ]] && echo true || echo false)"
done
check "statefulset/postgres ready" 1 "$(kubectl -n "$ns" get statefulset postgres -o jsonpath='{.status.readyReplicas}')"
check "migration jobs completed" 4 "$(kubectl -n "$ns" get jobs -o jsonpath='{range .items[*]}{.status.succeeded}{"\n"}{end}' | grep -c '^1$')"
echo
echo "Secret slothub-db keys (values never printed): $(kubectl -n "$ns" get secret slothub-db -o jsonpath='{.data}' | jq -r 'keys | join(", ")')"

section "2. Readiness through the Kubernetes Services (minikube tunnel → LoadBalancer → pods)"
for url in "$SCHEDULING_URL" "$BOOKING_URL" "$PAYMENT_URL" "$NOTIFICATION_URL"; do
  api GET "$url/health/ready"
  check "$(field .service) ready" HEALTHY "$(field .status)"
done

section "3. One booking across pods, traced by correlation ID"
trace="trace-$RUN_ID"
seed="$(seed_slot 1 50000)"
booking="$(curl -s -X POST "$BOOKING_URL/api/v1/bookings" -H 'Content-Type: application/json' -H "X-Correlation-Id: $trace" \
  -H "Idempotency-Key: $(uuid)" -d "{\"tenantId\":\"$(jq -r .tenantId <<<"$seed")\",\"slotId\":\"$(jq -r '.slotIds[0]' <<<"$seed")\",\"customer\":$(customer_json k8s)}" | jq -r .id)"
status="$(curl -s -X POST "$BOOKING_URL/api/v1/bookings/$booking/payment" -H "X-Correlation-Id: $trace" -H "Idempotency-Key: $(uuid)" | jq -r .status)"
check "booking $booking through Kubernetes" CONFIRMED "$status"
sleep 4
for app in booking scheduling payment notification; do
  echo "-- $app (kubectl logs -l app.kubernetes.io/name=$app --prefix)"
  kubectl -n "$ns" logs -l "app.kubernetes.io/name=$app" --prefix --tail=-1 2>/dev/null |
    awk -v t="\"$trace\"" 'index($0, t)' |
    sed -E 's/^\[pod\/([^/]+)\/[^]]+\] /\1 /' |
    while read -r pod json; do printf '  %-32s %s\n' "$pod" "$(jq -r '.["@m"]' <<<"$json" 2>/dev/null | cut -c1-120)"; done |
    grep -vE 'Execution attempt|HTTP "GET"' | head -4
  check "$app pod logged the traced request" true \
    "$(kubectl -n "$ns" logs -l "app.kubernetes.io/name=$app" --tail=-1 2>/dev/null | grep -q "\"$trace\"" && echo true || echo false)"
done

section "4. Main workflow end to end through Kubernetes (scripts/e2e-booking.sh)"
if "$ROOT/scripts/e2e-booking.sh" | sed 's/^/  │ /'; then
  check "e2e-booking.sh against Kubernetes" PASS PASS
else
  check "e2e-booking.sh against Kubernetes" PASS FAIL
fi

section "5. Challenge 1 through Kubernetes (20 simultaneous holds, capacity 1)"
if "$ROOT/scripts/challenge1-concurrency.sh" --capacity 1 --requests 20 | sed -n '/== Summary/,$p' | sed 's/^/  │ /'; then
  check "challenge1 against Kubernetes" PASS PASS
else
  check "challenge1 against Kubernetes" PASS FAIL
fi

finish
