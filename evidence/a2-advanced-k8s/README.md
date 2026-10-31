# Part B — Kubernetes on Minikube (2 marks) + scaling and self-healing (1 mark)

Deploy: `./scripts/k8s-deploy.sh`, then `minikube tunnel` in another terminal (APIs on `localhost:5101–5104`).
Manifests: [`deploy/k8s/`](../../deploy/k8s).

| File | Produced by | Shows |
|---|---|---|
| `run-*.txt` from **verify** | `scripts/a2-k8s-verify.sh` | 17 checks. (1) Cluster objects: 4 Deployments available, PostgreSQL StatefulSet ready with a PersistentVolumeClaim, 4 migration Jobs `Completed`, ClusterIP + LoadBalancer Services, Secret keys listed (values never printed). (2) Readiness of each service through its Kubernetes Service. (3) **One booking traced across pods**: the same correlation ID in the logs of the Booking, Scheduling, Payment and Notification pods (`kubectl logs`). (4) **Main workflow through Kubernetes**: full `e2e-booking.sh` (26 checks: happy path, declined payment, gateway outage + circuit breaker). (5) **Challenge 1 through Kubernetes**: 20 simultaneous holds on capacity 1 → exactly 1 succeeds, DB agrees |
| `run-*.txt` from **scale-heal** | `scripts/a2-k8s-scale-heal.sh` | 17 checks. (A) `kubectl scale … --replicas=3` for Scheduling and Booking; Challenge 1 with 30 simultaneous requests through the load-balanced Service is still exactly 1 success, while the requests were spread over all 3 Scheduling pods (per-pod log counts). (B) With 3 hold-expiry workers running at once, each of 5 expired holds was released by exactly one of them (`FOR UPDATE SKIP LOCKED`). (C) **Self-healing**: a Booking pod deleted while 6 bookings were being made → all 6 `CONFIRMED`, a replacement pod created, back to 3 ready; the only Payment pod deleted → recreated, flow works. (D) **Readiness**: PostgreSQL scaled to 0 → all service pods `0/1` and their Service endpoints `ready=false`; database back → pods ready, data intact on the PVC, flow works. (E) back to 1 replica |

**Design points to explain**

- **Images** are built with Docker and streamed into the Minikube node's containerd (`docker save | docker exec -i minikube ctr images import`). No registry is needed, and `imagePullPolicy: IfNotPresent` uses them.
- **Secrets:** `slothub-db` is created by the deploy script from `.env` (`kubectl create secret --from-env-file`). It is never in a file in the repo. Deployments read the password with `secretKeyRef` and build the connection string with `$(DB_PASSWORD)`.
- **Migrations** run once per service in a Job (`MIGRATE_ONLY=true`, same image) before the Deployments start, so several replicas never migrate concurrently. Deployments set `DB_MIGRATE_ON_STARTUP=false`.
- **Probes:** `startupProbe` and `livenessProbe` on `/health` (restart a stuck process), `readinessProbe` on `/health/ready` (take a pod out of the Service when its database is unreachable).
- **Services:** `<name>` (ClusterIP, port 8080) for calls inside the cluster. Booking calls `http://scheduling:8080` and the request is load-balanced across replicas. `<name>-external` (LoadBalancer, ports 5101–5104) for the host via `minikube tunnel`.
- **Why scaling is safe:** capacity is enforced by one atomic SQL update (Challenge 1), idempotency by a database unique key (Challenge 2), and hold expiry by `SKIP LOCKED` (Challenge 3B). No in-memory state depends on which pod serves a request.

**Problem met while deploying (Kubernetes).** The first deployment used `--skip-build` and loaded images built before
the `MIGRATE_ONLY` change. The migration Jobs never completed: `kubectl get pods` showed them `Running` for minutes, and
`kubectl logs` showed a web server starting and `relation "holds" does not exist`. The cause was stale images in the node. The fix was to rebuild and re-import
the images (`./scripts/k8s-deploy.sh` without `--skip-build`). The lesson: tag images with a version or revision per build, so
a stale image is visible instead of silently reused.

**Teardown:** `./scripts/k8s-teardown.sh` (namespace) · `--stop` (stop Minikube) · `--delete` (remove the cluster).
