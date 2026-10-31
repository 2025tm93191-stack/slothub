# Assignment 2 demo video script (6–8 minutes)

The brief asks for: build and deployment, running containers, the main workflow, the deployment problem with diagnosis and fix,
and the advanced task. Real terminal output, every member speaks. The speaker split below is a suggestion: give each
segment to the member who worked on it, and make sure they can explain the files they show.

## Before recording

```bash
minikube stop 2>/dev/null; docker compose down            # clean start (keep the volume)
cp -n .env.example .env                                    # passwords already set
docker compose build                                       # warm the build cache, so the live build is short
```

- Terminal font ≥ 16 pt; `clear` before each segment; a second terminal for `minikube tunnel` later.
- Do one dry run with a stopwatch. Long waits (the 30 s hold TTL) can be cut, with a caption.

## 0:00–0:40 · Introduction · [member]

"SlotHub is our Assignment 1 booking platform: Scheduling, Booking, Payment, Notification, each with its own database.
In Assignment 2 we containerized it, deployed it with Docker Compose and then on Minikube."
Show report Figure 1 (Compose) and Figure 2 (Kubernetes).

## 0:40–2:00 · Task 1: containerization · [member]

```bash
cat services/booking/Dockerfile          # explain: two stages, restore layer, non-root USER, HEALTHCHECK, labels
cat .env.example                         # placeholders only; .env is git-ignored
docker compose up --build                # images build, containers created and started (let it run ~20 s, then Ctrl+C)
docker compose up -d --wait              # start in the background
docker images | grep slothub             # one image per service
docker compose ps                        # 5 containers (healthy)
```

## 2:00–3:30 · Task 2: Compose deployment and main workflow · [member]

```bash
./scripts/a2-verify-deployment.sh | sed -n '/== 3/,/== 4/p'      # one booking traced across the 4 services' logs
./scripts/e2e-booking.sh | grep -E '^==|PASS|FAIL|RESULT'          # hold → pay → confirm → notify, plus failures
docker compose logs --tail 5 booking                                # structured logs with CorrelationId
docker compose down && docker compose up -d --wait                  # stop/restart
curl -s localhost:5102/health/ready | jq                            # works again; data kept in the volume
```

## 3:30–5:00 · Task 3: deployment problem, diagnosis, fix · [member]

```bash
./scripts/a2-troubleshoot-demo.sh
```

Talk over it:
1. **Problem:** in Assignment 1 `docker compose up` failed with "port is already allocated / address already in use", although `dotnet run` worked.
2. **Diagnosis:** `ps -a` shows every container `Created`; `inspect` shows the host port binding; postgres logs are **empty** (it never ran); `lsof` names the process holding the port.
3. **Fix:** the host port is configuration (`POSTGRES_PORT`), not hard-coded; services use `postgres:5432` internally.
4. **Validation:** everything healthy, booking confirmed.
5. **Learning:** host ports are shared, so publish few and keep them configurable; diagnose from the outside in.

## 5:00–7:30 · Part B: Minikube, scaling, self-healing · [member]

```bash
docker compose stop
./scripts/k8s-deploy.sh --skip-build         # images already built; show the Secret from .env, the Jobs and the rollouts
#   second terminal: minikube tunnel
kubectl -n slothub get pods,svc,jobs,pvc
./scripts/a2-k8s-verify.sh | grep -E '^==|PASS|FAIL|RESULT'          # workflow, e2e and Challenge 1 through Kubernetes
kubectl -n slothub scale deployment scheduling booking --replicas=3
kubectl -n slothub get pods -w                                        # Ctrl+C once 3/3
kubectl -n slothub delete pod <a booking pod>                         # replaced automatically
kubectl -n slothub get pods
./scripts/a2-k8s-scale-heal.sh | grep -E '^==|log lines|PASS|FAIL|RESULT'   # or show the saved evidence file
```

Say: "Scaling is safe because no state is in a pod. Capacity is one atomic SQL update, idempotency a database key, and expiry
uses SKIP LOCKED, so three replicas still give exactly one success and release each hold once."

## 7:30–8:00 · Close · [member]

"Everything is reproducible from the README: `docker compose up --build` for Compose, `./scripts/k8s-deploy.sh` for
Minikube. Evidence and raw outputs are in `evidence/a2-*`."
