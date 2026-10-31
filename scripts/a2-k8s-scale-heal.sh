#!/usr/bin/env bash
# Assignment 2 — Part B: Kubernetes scaling and self-healing.
#   A. scale Scheduling and Booking to 3 replicas; Challenge 1 through the load-balanced Service is still exactly
#      capacity, and the requests are spread over the 3 Scheduling pods
#   B. 3 hold-expiry workers at once: every expired hold released exactly once (FOR UPDATE SKIP LOCKED)
#   C. self-healing: delete a Booking pod while bookings keep flowing → replaced; delete the only Payment pod → recreated
#   D. readiness: PostgreSQL scaled to 0 → service pods NotReady and removed from Service endpoints → back when it returns
#   E. scale back to 1 replica
#
#   ./scripts/a2-k8s-scale-heal.sh      (cluster deployed, `minikube tunnel` running)  → evidence/a2-advanced-k8s/
export PG_EXEC="${PG_EXEC:-kubectl -n slothub exec -i postgres-0 --}"
SCRIPT_COMMAND="scripts/a2-k8s-scale-heal.sh"
. "$(dirname "$0")/lib.sh"
start_evidence a2-advanced-k8s scale-heal
ns=slothub
k() { kubectl -n "$ns" "$@"; }
pods() { k get pods -l "app.kubernetes.io/name=$1" -o wide --no-headers | awk '{printf "  %-34s %-8s %-10s %s\n", $1, $2, $3, $6}'; }
ready_count() { local n; n="$(k get deploy "$1" -o jsonpath='{.status.readyReplicas}')"; echo "${n:-0}"; }   # field is omitted when 0

new_booking() { # new_booking <label> → prints booking id (HELD)
  local seed
  seed="$(seed_slot 1 50000)"
  curl -s -X POST "$BOOKING_URL/api/v1/bookings" -H 'Content-Type: application/json' -H "Idempotency-Key: $(uuid)" \
    -d "{\"tenantId\":\"$(jq -r .tenantId <<<"$seed")\",\"slotId\":\"$(jq -r '.slotIds[0]' <<<"$seed")\",\"customer\":$(customer_json "$1")}" | jq -r .id
}
pay() { curl -s -m 30 -X POST "$BOOKING_URL/api/v1/bookings/$1/payment" -H "Idempotency-Key: $(uuid)" | jq -r '.status // .code'; }

section "A. Scale Scheduling and Booking to 3 replicas"
echo "\$ kubectl scale deployment scheduling booking --replicas=3"
k scale deployment scheduling booking --replicas=3
k rollout status deployment/scheduling --timeout=180s
k rollout status deployment/booking --timeout=180s
pods scheduling; pods booking
check "scheduling ready replicas" 3 "$(ready_count scheduling)"
check "booking ready replicas" 3 "$(ready_count booking)"

echo
echo "Challenge 1 through the Service (LoadBalancer → kube-proxy → any of the 3 Scheduling pods):"
c1_start="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
c1_out="$("$ROOT/scripts/challenge1-concurrency.sh" --capacity 1 --requests 30)"
run_id="$(grep -m1 '^Run:' <<<"$c1_out" | awk '{print $2}')"
grep -E '^(201|409) |RESULT' <<<"$c1_out" | sed 's/^/  │ /'
grep -E '201 Created|409 Conflict' <<<"$c1_out" | sed 's/^/  │ /'
check "challenge 1 with 3 replicas: exactly 1 of 30" true "$(grep -q 'RESULT: PASS' <<<"$c1_out" && echo true || echo false)"
echo "  Requests of run $run_id served per Scheduling pod (kubectl logs):"
spread=0
for p in $(k get pods -l app.kubernetes.io/name=scheduling -o name); do
  n="$(k logs "$p" --since-time="$c1_start" 2>/dev/null | grep -c "\"$run_id-" || true)"
  printf '    %-40s %s log lines\n' "${p#pod/}" "$n"
  [[ "$n" -gt 0 ]] && spread=$((spread + 1))
done
check "requests reached more than one Scheduling pod" true "$([[ "$spread" -gt 1 ]] && echo true || echo false)"

section "B. Hold expiry with 3 Scheduling replicas (3 sweepers at once)"
b_start="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
holds=()
for i in 1 2 3 4 5; do
  id="$(new_booking "expiry-$i")"
  holds+=("$(curl -s "$BOOKING_URL/api/v1/bookings/$id" | jq -r .holdId)")
done
echo "  created 5 unpaid holds; waiting for TTL (30 s) + sweep..."
sleep 40
for h in "${holds[@]}"; do
  st="$(curl -s "$SCHEDULING_URL/api/v1/holds/$h" | jq -r .status)"
  n="$(k logs -l app.kubernetes.io/name=scheduling --since-time="$b_start" --tail=-1 2>/dev/null | grep -c "Hold $h for booking .* expired" || true)"
  echo "  hold $h: $st, expired by $n sweeper(s)"
  check "hold $h released exactly once" "EXPIRED 1" "$st $n"
done
echo "  Expiry log lines per pod:"
for p in $(k get pods -l app.kubernetes.io/name=scheduling -o name); do
  printf '    %-40s %s holds expired\n' "${p#pod/}" "$(k logs "$p" --since-time="$b_start" 2>/dev/null | grep -c 'expired; capacity returned' || true)"
done

section "C. Self-healing"
echo "\$ kubectl delete pod <one booking pod>   (while bookings keep flowing)"
victim="$(k get pods -l app.kubernetes.io/name=booking -o name | head -1)"
echo "  deleting $victim"
before="$(k get pods -l app.kubernetes.io/name=booking -o name | sort)"
ok=0
for i in $(seq 1 6); do
  if [[ $i -eq 2 ]]; then k delete "$victim" --wait=false >/dev/null; fi
  [[ "$(pay "$(new_booking "heal-$i")")" == CONFIRMED ]] && ok=$((ok + 1))
done
k rollout status deployment/booking --timeout=120s >/dev/null
after="$(k get pods -l app.kubernetes.io/name=booking -o name | sort)"
pods booking
echo "  replacement pod(s): $(comm -13 <(echo "$before") <(echo "$after") | tr '\n' ' ')"
check "booking flows confirmed during the pod deletion" 6 "$ok"
check "booking back to 3 ready replicas" 3 "$(ready_count booking)"
check "a new booking pod replaced the deleted one" true "$([[ -n "$(comm -13 <(echo "$before") <(echo "$after"))" ]] && echo true || echo false)"

echo
echo "\$ kubectl delete pod <the only payment pod>"
pay_before="$(k get pods -l app.kubernetes.io/name=payment -o jsonpath='{.items[0].metadata.name}')"
k delete pod "$pay_before" --wait=false >/dev/null
k rollout status deployment/payment --timeout=120s >/dev/null
k wait --for=condition=ready pod -l app.kubernetes.io/name=payment --timeout=120s >/dev/null
pay_after="$(k get pods -l app.kubernetes.io/name=payment -o jsonpath='{.items[0].metadata.name}')"
echo "  payment pod $pay_before → $pay_after"
pods payment
check "payment pod recreated" true "$([[ "$pay_before" != "$pay_after" ]] && echo true || echo false)"
check "booking flow after payment pod replaced" CONFIRMED "$(pay "$(new_booking heal-payment)")"

section "D. Readiness probes protect traffic"
echo "\$ kubectl scale statefulset postgres --replicas=0"
k scale statefulset postgres --replicas=0 >/dev/null
k wait --for=delete pod/postgres-0 --timeout=60s >/dev/null 2>&1 || true
sleep 15
k get pods -l 'app.kubernetes.io/name in (scheduling,payment,notification)' --no-headers | awk '{printf "  %-34s %s\n", $1, $2}'
check "scheduling pods NotReady without their database" 0 "$(ready_count scheduling)"
echo "  Service endpoints of scheduling (ready addresses): $(k get endpointslices -l kubernetes.io/service-name=scheduling -o jsonpath='{range .items[*].endpoints[*]}{.conditions.ready}{" "}{end}')"
echo "\$ kubectl scale statefulset postgres --replicas=1"
k scale statefulset postgres --replicas=1 >/dev/null
k rollout status statefulset/postgres --timeout=180s >/dev/null
k wait --for=condition=available deployment --all --timeout=180s >/dev/null
check "scheduling ready again" 3 "$(ready_count scheduling)"
check "booking flow after the database returned (data on the PVC)" CONFIRMED "$(pay "$(new_booking after-db)")"

section "E. Scale back to 1 replica"
k scale deployment scheduling booking --replicas=1
k rollout status deployment/scheduling --timeout=120s >/dev/null
k rollout status deployment/booking --timeout=120s >/dev/null
k get pods --no-headers | awk '{printf "  %-34s %-8s %s\n", $1, $2, $3}'

finish
