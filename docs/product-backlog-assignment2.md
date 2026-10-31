# SlotHub — Product Backlog, Assignment 2 (Containerization and Deployment)

> Continues [product-backlog.md](product-backlog.md) (Assignment 1, E1–E10, PBI-001–094). Same hierarchy:
> **Epic → Feature → PBI**, same Definition of Ready/Done, same status values (New · In Progress · ✅ Done · Out of scope).
> Brief: 12 marks. Part A core = 9 (Tasks 1–3), Part B advanced = max 3. Deliverables: repo, deployment report (≤ 5 pages), demo video (6–8 min).

| Item | Value |
|---|---|
| Sprint length | 10 days |
| Estimation | Story points (Fibonacci) |
| Priority | **Must** / **Should** / **Could** / **Won't** (this assignment) |
| Advanced choice | **Minikube deployment (2) + scaling / self-healing (1) = the 3-mark cap.** Other Part B options are listed as Won't: no extra marks beyond the cap |
| Owner | Set at sprint planning; each member must be able to explain the Dockerfiles, Compose, troubleshooting and Kubernetes files they present |

---

## 1. Sprint plan

| Sprint | Dates (10 days) | Sprint goal | Points | Status |
|---|---|---|---|---|
| **Sprint 4** | Mon 2 Nov – Wed 11 Nov 2026 | Part A complete: hardened images with health checks and no committed secrets; one-command Compose deployment demonstrated (workflow, logs, health, restart); troubleshooting case written up with a reproducible demo. | 38 | ✅ Done (PBI-102 fresh-clone check moves to S5) |
| **Sprint 5** | Thu 12 Nov – Sat 21 Nov 2026 | Part B: SlotHub on Minikube with the main workflow working through Kubernetes, plus scaling and self-healing; deployment report, video, submission. | 52 | Not started |

### Sprint 4 scope
E11 (PBI-095 → PBI-101), E12 (PBI-102 → PBI-106), E13 (PBI-107 → PBI-110).

### Sprint 4 review
- Images: health check (`infra/docker/healthcheck.sh` via bash `/dev/tcp`, no curl), OCI labels, version tags `1.0.0`; Compose refuses to start without `.env` (`${VAR:?}`), `.env.example` placeholders only; evidence: 22/22 (`evidence/a2-task1-containerization/`).
- Compose: health-gated start-up order; correlation ID now also forwarded through the outbox (new migration `AddOutboxCorrelationId`) so one booking is traceable in all 4 services' logs; verify 15/15, restart 9/9 (`evidence/a2-task2-compose/`).
- Troubleshooting: real port-conflict failure reproduced and diagnosed (12/12), write-up with reflection (`evidence/a2-task3-troubleshooting/README.md`).

### Sprint 5 scope
E14 (PBI-111 → PBI-118), E15 (PBI-119 → PBI-121), E17 (PBI-126 → PBI-132).

---

## 2. Epic overview

| Epic | Name | Brief | Sprint | Points | Status |
|---|---|---|---|---|---|
| E11 | Containerization | Part A Task 1 (3 marks) | S4 | 14 | ✅ Done |
| E12 | Docker Compose deployment | Part A Task 2 (3 marks) | S4 | 14 | In Progress (fresh-clone foreground run pending) |
| E13 | Deployment troubleshooting (situated learning) | Part A Task 3 (3 marks) | S4 | 10 | ✅ Done |
| E14 | Kubernetes on Minikube | Part B (2 marks) | S5 | 29 | New |
| E15 | Scaling and self-healing | Part B (1 mark) | S5 | 6 | New |
| E16 | Other advanced options | Part B (beyond the 3-mark cap) | — | — | Won't |
| E17 | Deployment report, video and submission | Deliverables B, C | S5 | 17 | New |

---

## E11 — Containerization (Task 1, 3 marks)

### F11.1 Service images

| ID | PBI | Acceptance criteria | SP | Pri | Sprint | Status |
|---|---|---|---|---|---|---|
| PBI-095 | Review and document the Dockerfiles | One multi-stage Dockerfile per service (SDK build → ASP.NET runtime), restore layer cached, non-root `USER $APP_UID`, port 8080<br>Each team member can explain every line; short notes in the report | 2 | Must | S4 | ✅ Done |
| PBI-096 | Container health checks for every service | `HEALTHCHECK` (or Compose `healthcheck`) calls `/health/ready` without adding heavy tools to the runtime image<br>`docker compose ps` shows **healthy** for all 5 containers; a stopped database turns the services unhealthy | 3 | Must | S4 | ✅ Done |
| PBI-097 | Image naming, tags and labels | Images tagged `slothub-<service>:<version>` plus `latest`; OCI labels (source, version, revision)<br>`docker images` lists 4 separate service images with sizes | 1 | Should | S4 | ✅ Done |

### F11.2 Configuration and secrets

| ID | PBI | Acceptance criteria | SP | Pri | Sprint | Status |
|---|---|---|---|---|---|---|
| PBI-098 | No passwords in `docker-compose.yml` | Remove the fallback passwords (`${..._PASSWORD:-scheduling_pw}`); use `${VAR:?set it in .env}` so Compose fails fast with a clear message<br>`.env.example` holds placeholders only; README says `cp .env.example .env` | 2 | Must | S4 | ✅ Done |
| PBI-099 | Prove no secrets are in images or the repo | `docker history` / `docker inspect` of each image shows no `.env` and no passwords; `.dockerignore` excludes `.env`<br>Repo scan (e.g. `gitleaks detect` or `git grep`) clean; output saved as evidence | 2 | Must | S4 | ✅ Done |
| PBI-100 | Configuration inventory | Table of every environment variable per service (name, purpose, example, secret yes/no) in README and report; all URLs and connection strings come from env vars | 1 | Must | S4 | ✅ Done |

### F11.3 Evidence

| ID | PBI | Acceptance criteria | SP | Pri | Sprint | Status |
|---|---|---|---|---|---|---|
| PBI-101 | Build and start evidence | `scripts/a2-build-evidence.sh`: `docker compose build --no-cache`, `docker images`, `docker compose up -d`, `docker compose ps` → timestamped output in `evidence/a2-task1-containerization/` + screenshots | 3 | Must | S4 | ✅ Done |

---

## E12 — Docker Compose deployment (Task 2, 3 marks)

### F12.1 One-command, reproducible deployment

| ID | PBI | Acceptance criteria | SP | Pri | Sprint | Status |
|---|---|---|---|---|---|---|
| PBI-102 | `docker compose up --build` from a clean clone | Fresh clone + `cp .env.example .env` + `docker compose up --build` starts PostgreSQL and the 4 services with no other steps; documented in README | 2 | Must | S4 | In Progress (works; foreground run from a fresh clone checked in PBI-132) |
| PBI-103 | Start-up ordering on health | Services wait for PostgreSQL **healthy**; Booking waits for Scheduling, Payment and Notification **healthy** (`depends_on: condition: service_healthy`); no connection errors in start-up logs | 2 | Must | S4 | ✅ Done |

### F12.2 Demonstration

| ID | PBI | Acceptance criteria | SP | Pri | Sprint | Status |
|---|---|---|---|---|---|---|
| PBI-104 | Deployment verification script | `scripts/a2-verify-deployment.sh`: `docker compose ps` (all healthy), `/health/ready` of each service, runs `e2e-booking.sh`, then prints one booking's correlation ID found in the Booking, Scheduling, Payment and Notification logs (proves service-to-service calls)<br>Output to `evidence/a2-task2-compose/` | 5 | Must | S4 | ✅ Done |
| PBI-105 | Stop and restart demonstration | `docker compose stop` → `start`, and `down` → `up` (volume kept): earlier booking still readable, new booking flow passes; `docker compose restart booking` recovers<br>Evidence saved | 3 | Must | S4 | ✅ Done |
| PBI-106 | Logs and health evidence | `docker compose logs` excerpt following one request across services; `docker inspect --format '{{json .State.Health}}'` for a service | 2 | Must | S4 | ✅ Done |

---

## E13 — Deployment troubleshooting (Task 3, 3 marks)

Genuine problems met while containerizing in Assignment 1 (Sprint 1–3):
1. **PostgreSQL container failed to start:** host ports 5432 and 5433 were already taken by other PostgreSQL instances (`bind: address already in use`), fixed by making the host port configurable (`POSTGRES_PORT`, default 5440).
2. **Works locally, fails in the container:** Npgsql logged `libgssapi_krb5.so.2: cannot open shared object file` only inside containers; fixed with `GSS Encryption Mode=Disable`.
3. **Database did not come back after a Docker restart:** no restart policy on `postgres`; fixed with `restart: unless-stopped`.
4. **Docker daemon hung, builds failed with I/O errors:** host disk full; diagnosed with `df -h` / `docker system df`, fixed by pruning build cache.

### F13.1 Case study (primary: problem 1; secondary: problem 2)

| ID | PBI | Acceptance criteria | SP | Pri | Sprint | Status |
|---|---|---|---|---|---|---|
| PBI-107 | Problem statement (0.5) | Describes the observed symptom exactly as seen (error text, which container, when), why it worked in development, impact | 1 | Must | S4 | ✅ Done |
| PBI-108 | Reproducible problem demo | `scripts/a2-troubleshoot-demo.sh` recreates the failure safely (occupies the DB port with a throw-away container/listener), runs `docker compose up`, shows the failure | 3 | Must | S4 | ✅ Done |
| PBI-109 | Diagnosis (1.0) and fix + validation (1.0) | Evidence of `docker compose ps -a` (exited container), `docker compose logs postgres`, `docker inspect` (port bindings, exit code), `lsof -iTCP:5432` / `docker ps` (who holds the port)<br>Fix applied (`POSTGRES_PORT`), redeploy, `a2-verify-deployment.sh` passes; all saved to `evidence/a2-task3-troubleshooting/` | 5 | Must | S4 | ✅ Done |
| PBI-110 | Learning reflection (0.5) | Half a page: what this taught about containerized deployment (host vs container ports, config over hard-coding, health-gated start-up) and how we'd apply it (port policy, pre-flight checks, readiness probes) | 1 | Must | S4 | ✅ Done |

---

## E14 — Kubernetes on Minikube (Part B, 2 marks)

Brief: "simply showing pods in Running state is not sufficient; the main application workflow must work through the Kubernetes deployment."

### F14.1 Cluster and images

| ID | PBI | Acceptance criteria | SP | Pri | Sprint | Status |
|---|---|---|---|---|---|---|
| PBI-111 | Minikube setup | Docs for `minikube start --driver=docker` with CPU/memory/disk settings; pre-check of free disk (≥ 15 GB); images built into Minikube (`eval $(minikube docker-env)` or `minikube image load`) | 2 | Must | S5 | New |

### F14.2 Manifests (`deploy/k8s/`)

| ID | PBI | Acceptance criteria | SP | Pri | Sprint | Status |
|---|---|---|---|---|---|---|
| PBI-112 | Namespace, ConfigMaps and Secret | Namespace `slothub`; ConfigMap per service with non-secret env vars; DB passwords in a Secret created from `.env` by a script (`kubectl create secret --from-env-file`), **never committed** | 3 | Must | S5 | New |
| PBI-113 | PostgreSQL in Kubernetes | StatefulSet + PVC + headless Service; init script from a ConfigMap creates 4 databases and 4 users; data survives pod restart | 5 | Must | S5 | New |
| PBI-114 | Service Deployments with probes | Deployment per service: image, env from ConfigMap/Secret, liveness `/health`, readiness `/health/ready`, resource requests/limits | 5 | Must | S5 | New |
| PBI-115 | Services and access | ClusterIP Service per service; Booking's downstream URLs use Kubernetes DNS names; external access via NodePort / `minikube service` / port-forward documented | 3 | Must | S5 | New |
| PBI-116 | Database migrations in Kubernetes | Migrations run once per service before traffic (Kubernetes Job or init container), not concurrently from several replicas | 3 | Must | S5 | New |

### F14.3 Workflow through Kubernetes

| ID | PBI | Acceptance criteria | SP | Pri | Sprint | Status |
|---|---|---|---|---|---|---|
| PBI-117 | Run the scripts against Minikube | `lib.sh` database helper works with `kubectl exec` as well as `docker exec`; `e2e-booking.sh` and the Challenge 1 script pass against the Minikube URLs; evidence in `evidence/a2-advanced-k8s/` | 5 | Must | S5 | New |
| PBI-118 | Deploy and teardown scripts | `scripts/k8s-deploy.sh` (build images, create secret, apply manifests, wait for rollout) and `scripts/k8s-teardown.sh` | 3 | Must | S5 | New |

---

## E15 — Scaling and self-healing (Part B, 1 mark)

| ID | PBI | Acceptance criteria | SP | Pri | Sprint | Status |
|---|---|---|---|---|---|---|
| PBI-119 | Multiple replicas | Scheduling and Booking at 3 replicas (`kubectl scale`); log lines show requests served by different pods; Challenge 1 still exactly *N* successes (atomic DB update) and the hold-expiry worker on 3 replicas still releases each hold once (`SKIP LOCKED`) | 3 | Must | S5 | New |
| PBI-120 | Self-healing | `kubectl delete pod` of a service → new pod created and ready (`kubectl get pods -w`); a booking flow run during/after the deletion succeeds | 2 | Must | S5 | New |
| PBI-121 | Readiness protects traffic | Stopping PostgreSQL makes pods NotReady (removed from Service endpoints) and they return when it is back | 1 | Should | S5 | New |

---

## E16 — Other advanced options (Won't: the 3-mark cap is reached by E14 + E15)

| ID | PBI | Notes | SP | Pri | Status |
|---|---|---|---|---|---|
| PBI-122 | Prometheus + Grafana metrics | `/metrics` via OpenTelemetry/prometheus-net; dashboard of request rate, latency, error rate | 8 | Won't | Out of scope |
| PBI-123 | OpenTelemetry + Jaeger tracing | One booking traced across Booking → Scheduling → Payment | 8 | Won't | Out of scope |
| PBI-124 | Horizontal Pod Autoscaler | metrics-server + HPA on Booking, demonstrated under load | 5 | Won't | Out of scope |
| PBI-125 | Rolling update and rollback | New image version, `kubectl rollout status/undo` | 3 | Could (fallback if E15 fails) | New |

---

## E17 — Deployment report, video and submission

| ID | PBI | Acceptance criteria | SP | Pri | Sprint | Status |
|---|---|---|---|---|---|---|
| PBI-126 | Container and deployment architecture diagram | Compose view (5 containers, network, volume, ports) and Kubernetes view (Deployments, Services, StatefulSet, ConfigMap/Secret) | 2 | Must | S5 | New |
| PBI-127 | Deployment report (≤ 5 pages) | Sections: overview + diagram; containerization and Compose; troubleshooting (symptom, diagnosis, solution, validation, learning); advanced task + evidence; individual contribution summary and AI-use declaration<br>Built to PDF like the A1 report | 5 | Must | S5 | New |
| PBI-128 | Evidence structure | `evidence/a2-task1-containerization/`, `a2-task2-compose/`, `a2-task3-troubleshooting/`, `a2-advanced-k8s/`, each with a README linking files to claims + screenshots | 2 | Must | S5 | New |
| PBI-129 | Video script (6–8 min) | Build and deploy → running containers → main workflow → problem, diagnosis, fix → Minikube workflow, scaling, pod deletion; every member speaks | 2 | Must | S5 | New |
| PBI-130 | Record and publish video | Real terminal, 6–8 min, uploaded with link access | 3 | Must | S5 | New (team) |
| PBI-131 | README deployment instructions | Compose and Minikube steps reproducible from a fresh clone; troubleshooting table | 2 | Must | S5 | New |
| PBI-132 | Pre-submission check and submit | Fresh clone deploys with documented steps; no secrets committed; every member has commits; report, video and repo links submitted | 1 | Must | S5 | New (team) |

---

## Mark coverage

| Brief component | Marks | Epics / PBIs |
|---|---|---|
| Containerization | 3 | E11 (095–101) |
| Docker Compose deployment and demonstration | 3 | E12 (102–106) |
| Troubleshooting: problem 0.5, diagnosis 1.0, solution 1.0, reflection 0.5 | 3 | E13 (107, 108–109, 110) |
| Advanced: Minikube 2 + scaling/self-healing 1 | 3 | E14 (111–118), E15 (119–121) |
| Deliverables | — | E17 (126–132) |
