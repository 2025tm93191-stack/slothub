# Assignment 2 demo video script (6–8 minutes)

**Project:** SlotHub — Containerization and Deployment  
**Target edited duration:** **7 minutes 35 seconds**  
**Format:** Each presenter records their own screen and voice remotely; the clips are joined in the order below. Run the preparation commands on **your own local machine**, not on another presenter's terminal.

The Assignment 2 brief asks for a real build and deployment, healthy containers, the main booking workflow, a deployment problem with diagnosis and fix, and the advanced Kubernetes deployment/scaling/self-healing demonstration.Each presenter should understand the Dockerfiles, Compose configuration, troubleshooting commands, or Kubernetes manifests they show.

| Time in final video | Segment | Speaker | Speaking time |
|---|---|---|---:|
| **0:00–0:25** | 1. Introduction, architecture, team lead | **Subhodip Banerjee** | 0:25 |
| **0:25–2:50** | 2. Task 1: Docker containerization, image build and healthy deployment | **Ria** | **2:25** |
| **2:50–5:15** | 3. Task 2: Compose, main workflow; Task 3: troubleshooting and recovery | **J K Shekar** | **2:25** |
| **5:15–7:35** | 4. Advanced task: Minikube, scaling, self-healing and conclusion | **Subhodip Banerjee** | **2:20** |
| | | **Subhodip total** | **2:45** |

## Before recording — all presenters (not part of the 7:35 video)

1. Pull the latest merged code, select your repository root, and verify that `.env` contains working local database passwords. **Do not show `.env` or Secret values in your recording.**
2. Install Docker and Compose; Subhodip also needs Minikube and `kubectl`. Have `jq`, `curl`, and `lsof` available where required.
3. Use a dark, wide terminal with font **16 pt or larger**; show a visible `date -u` and `clear` before your segment. Record at 1080p if possible, with clear microphone audio.
4. Do a complete dry run. The long verification scripts may exceed their speaking slots; let them finish **before** recording and preserve their real evidence logs. During recording, run the short, genuine live commands and display the relevant saved evidence where indicated. Never imply a saved log is a live run.
5. Keep database data unless a reset is specifically needed. `docker compose down` preserves the volume, while `docker compose down -v` **deletes local PostgreSQL data** and is not required here.
6. Use the Assignment 2 report PDF (`docs/report-a2/SlotHub-Deployment-Report.pdf`) only for brief architecture context. The majority of the video should be terminal-based.

### Suggested clip filenames

- `01-subhodip-introduction.mp4`
- `02-ria-containerization.mp4`
- `03-shekar-compose-troubleshooting.mp4`
- `04-subhodip-kubernetes.mp4`

Each person records independently on their own Docker/Minikube setup. **No booking ID, shell variable or command output needs to pass from one presenter's computer to another.**

---

## 0:00–0:25 · 1. Introduction and architecture · Subhodip Banerjee

**Before recording:** Have the Assignment 2 report open on the **Compose and Kubernetes architecture diagrams**. The Docker and Kubernetes demos are recorded separately, so this introduction does not need a running terminal.

**Show:** Title slide/report Figure 1 (Compose) and Figure 2 (Kubernetes), briefly. Do not spend time reading the full report.

**Say:**

> "Hello, we are the SlotHub team. I'm Subhodip Banerjee, the repository owner and project lead. SlotHub uses four independently deployable services—Scheduling, Booking, Payment and Notification—with PostgreSQL databases separated by service. For Assignment 2, we containerized the application, deployed and verified it using Docker Compose, reproduced and resolved a real deployment issue, and demonstrated Kubernetes scaling and self-healing. Ria will show our containerization work."

---

## 0:25–2:50 · 2. Task 1: Docker containerization and healthy deployment · Ria

**Prepare on Ria's machine (before recording):**

```bash
cd /path/to/slothub
minikube stop 2>/dev/null || true    # only if Minikube was previously running
# The .env file must already exist and contain valid local-only passwords.
docker compose down                 # keep the PostgreSQL volume
docker compose build                # warm the image cache for the recorded build
docker compose up -d --wait         # verify healthy before recording
```

**Recording commands — run in this order:**

```bash
clear
date -u

# Show the actual Dockerfile and the example configuration only.
sed -n '1,85p' services/booking/Dockerfile
sed -n '1,55p' .env.example

# Demonstrate the image build using the warmed cache.
docker compose build

# Show separate service images and all five healthy containers.
docker image ls --filter 'reference=slothub*'
docker compose up -d --wait
docker compose ps
```

**Say while showing the Dockerfile:**

> "I'll demonstrate Task 1, Docker containerization. Each SlotHub microservice has its own Docker image. This Dockerfile uses a multi-stage .NET build: the SDK image compiles the application, while the smaller ASP.NET runtime image runs it. We also configure health checks and run the application without unnecessary root privileges."

**Say while showing `.env.example` and the build:**

> "The Compose configuration reads database passwords from a local environment file rather than hard-coding credentials in the images. I'm only showing the example file, not our actual passwords. Here the four service images build successfully."

**Say while showing `docker compose ps`:**

> "Compose starts PostgreSQL and the four services on a shared network. The health checks confirm that all five containers are healthy. The API ports are 5101 for Scheduling, 5102 for Booking, 5103 for Payment and 5104 for Notification."

**Handover:**

> "Shekar will now verify the deployed services end to end and demonstrate how we diagnosed and fixed a port conflict."

**Timing tip:** A genuinely uncached .NET build may take several minutes. Record the **cached live build**, then show the complete original build evidence in `evidence/a2-task1-containerization/` if necessary. Do not cut a command in the middle or claim the cache output is a fresh uncached build.

---

## 2:50–5:15 · 3. Task 2: Compose, main workflow; Task 3: troubleshooting · J K Shekar

**Prepare on Shekar's machine (before recording):**

```bash
cd /path/to/slothub
unset SCHEDULING_URL BOOKING_URL PAYMENT_URL NOTIFICATION_URL PG_EXEC
# If Minikube was running, stop its tunnel and/or stop Minikube first.
docker compose up -d --wait
docker compose ps

# Pre-run both demonstrations, confirm their final RESULT: PASS,
# and keep the resulting timestamped files in evidence/a2-task2-compose/
# and evidence/a2-task3-troubleshooting/.
./scripts/a2-verify-deployment.sh
./scripts/a2-troubleshoot-demo.sh
```

The troubleshooting script **intentionally brings down and recreates Compose containers** to reproduce a host-port conflict; do not run any other local demo simultaneously. It uses a temporary listener, changes `POSTGRES_PORT` for the fix, then restores the default configuration.

**Recording commands — run in this order:**

```bash
clear
date -u

# Task 2: healthy Compose services and a real API call.
docker compose ps
curl -fsS http://localhost:5102/health/ready | jq '{service, status, checks}'

# Run the cross-service trace / end-to-end verification.
./scripts/a2-verify-deployment.sh | grep -E '^==|Correlation ID|booking confirmed|traced request|PASS|FAIL|Checks|RESULT'

# Task 3: show the port conflict, diagnosis, fix, and restored health.
./scripts/a2-troubleshoot-demo.sh | grep -E '^==|Cause:|port|healthy|booking|PASS|FAIL|Checks|RESULT'

docker compose ps
```

**If either full script is too slow for the 2:25 recording:** run it to completion before recording and show the genuine saved log with its run timestamp using:

```bash
ls -lt evidence/a2-task2-compose/*.txt evidence/a2-task3-troubleshooting/*.txt | head
# Replace <log-file> with the actual filename shown above:
grep -E '^==|Correlation ID|Cause:|PASS|FAIL|Checks|RESULT' <log-file> | tail -35
```

Showing a previous actual log is acceptable **as supporting evidence**, but include the live `docker compose ps`, readiness API call, and a live Docker diagnostic command such as `docker compose ps -a`. Do **not** label pre-recorded output as live. To reproduce the port conflict on camera, run the full troubleshooting script and leave enough time to capture the complete recovery.

**Say during Task 2:**

> "I'm J K Shekar. For Task 2, we verified that the Docker Compose deployment isn't just running—it also supports the complete booking workflow. The verification script checks container health, calls all four readiness endpoints, creates a booking, takes payment, confirms the hold and verifies the notification. A correlation ID lets us follow the same request through Booking, Scheduling, Payment and Notification logs. The end-to-end suite also checks failure and recovery paths."

**Say during Task 3:**

> "We also reproduced a deployment problem caused by PostgreSQL's host port already being occupied. The containers could not start correctly even though the application itself was working. We diagnosed it with `docker compose ps -a`, `docker inspect` and `lsof`. The fix was to configure a free host port using `POSTGRES_PORT`, while services continued to use `postgres:5432` inside Docker. After the fix, the containers became healthy and the booking workflow succeeded again."

**Handover:**

> "Subhodip will now show how we deployed the same services on Kubernetes, scaled them and verified automatic pod recovery."

**Timing tip:** The two full scripts can take longer than 2:25 together. Prioritize the live health/API call, the cross-service trace and the real port-conflict diagnosis/fix summary. The full raw logs must be preserved in the repository for the evaluator.

---

## 5:15–7:35 · 4. Advanced task: Minikube, scaling, self-healing, close · Subhodip Banerjee

**Prepare on Subhodip's machine (before recording):**

```bash
cd /path/to/slothub
# Ensure the four slothub-*:1.0.0 images already exist locally.
docker image ls --filter 'reference=slothub*'
docker compose stop
./scripts/k8s-deploy.sh --skip-build
```

Open **a second terminal** and leave this running:

```bash
minikube tunnel
```

**Important for Linux + Docker-driver Minikube:** LoadBalancer services may be assigned `10.x.x.x` external IP addresses rather than `localhost` ports. Set the API URLs from the actual Kubernetes Services before running verification scripts:

```bash
export SCHEDULING_URL="http://$(kubectl -n slothub get svc scheduling-external -o jsonpath='{.status.loadBalancer.ingress[0].ip}'):5101"
export BOOKING_URL="http://$(kubectl -n slothub get svc booking-external -o jsonpath='{.status.loadBalancer.ingress[0].ip}'):5102"
export PAYMENT_URL="http://$(kubectl -n slothub get svc payment-external -o jsonpath='{.status.loadBalancer.ingress[0].ip}'):5103"
export NOTIFICATION_URL="http://$(kubectl -n slothub get svc notification-external -o jsonpath='{.status.loadBalancer.ingress[0].ip}'):5104"
export PG_EXEC='kubectl -n slothub exec -i postgres-0 --'

# If a network proxy is configured, exempt the service IPs.
IPS="$(printf '%s\n' "$SCHEDULING_URL" "$BOOKING_URL" "$PAYMENT_URL" "$NOTIFICATION_URL" | sed -E 's#http://([^:]+):.*#\1#' | paste -sd, -)"
export NO_PROXY="${NO_PROXY:+$NO_PROXY,}$IPS,localhost,127.0.0.1"
export no_proxy="$NO_PROXY"

curl --noproxy '*' -fsS "$BOOKING_URL/health/ready"
./scripts/a2-k8s-verify.sh          # verify in advance; should end RESULT: PASS
./scripts/a2-k8s-scale-heal.sh      # complete test; it also restores replica counts
```

Confirm that the earlier Booking trace-log assertion fix in `scripts/a2-k8s-verify.sh` is present in your branch before claiming that the Kubernetes verification has passed. The earlier `grep -q`/`pipefail` check could produce a false failure even when a Booking trace was logged.

The `a2-k8s-scale-heal.sh` script can take several minutes, as it includes an expiry wait and PostgreSQL readiness recovery. **Run it before recording**, keep its timestamped real log, and demonstrate selected Kubernetes operations live during the clip.

**Recording commands — run in this order:**

```bash
clear
date -u

# Show the actual deployed Kubernetes objects.
kubectl get nodes
kubectl -n slothub get deployments,statefulsets,jobs,pvc
kubectl -n slothub get pods,svc

# Live horizontal scaling: one replica -> three for two services.
kubectl -n slothub scale deployment scheduling booking --replicas=3
kubectl -n slothub rollout status deployment/scheduling --timeout=180s
kubectl -n slothub rollout status deployment/booking --timeout=180s
kubectl -n slothub get deployments

# Live self-healing: delete one Booking pod; Kubernetes replaces it.
victim=$(kubectl -n slothub get pods -l app.kubernetes.io/name=booking -o jsonpath='{.items[0].metadata.name}')
echo "Deleting Booking pod: $victim"
kubectl -n slothub delete pod "$victim" --wait=false
kubectl -n slothub rollout status deployment/booking --timeout=120s
kubectl -n slothub get pods -l app.kubernetes.io/name=booking

# Restore the original replica counts after the demo.
kubectl -n slothub scale deployment scheduling booking --replicas=1
```

**Show the full-scale verification evidence (recorded during preparation):**

```bash
ls -lt evidence/a2-advanced-k8s/*.txt | head
# Replace <verification-log> and <scale-heal-log> with actual filenames:
grep -E '^==|PASS|FAIL|Checks|RESULT' <verification-log> | tail -18
grep -E '^==|PASS|FAIL|Checks|RESULT' <scale-heal-log> | tail -25
```

**Say while showing the cluster:**

> "I'm Subhodip, and I'll demonstrate our advanced Kubernetes deployment. We deployed the same four microservices on Minikube. PostgreSQL runs as a StatefulSet with persistent storage; application services run as Deployments, database migrations are Jobs, and the APIs are exposed through Services. Kubernetes Secrets hold database credentials without exposing them in source code."

**Say while scaling and deleting the pod:**

> "Here I'm scaling Scheduling and Booking to three replicas. Because service state is held in PostgreSQL, requests can be distributed across pods without losing correctness. When I delete a Booking pod, Kubernetes automatically creates a replacement to maintain the desired replica count. The full validation also checks concurrent reservations, hold expiry and service readiness during disruptions."

**Closing statement (last 15–20 seconds):**

> "We successfully demonstrated containerization, healthy Compose deployment, end-to-end service communication, port-conflict diagnosis, and Kubernetes scaling and self-healing. The reproducible setup and complete results are available in our GitHub repository and the Assignment 2 evidence folders. Thank you."

**After recording** (not in the video): Stop `minikube tunnel` with `Ctrl+C`. If you no longer need the Kubernetes cluster, `./scripts/k8s-teardown.sh --stop` removes the SlotHub Kubernetes namespace and its persistent data; then `docker compose up -d --wait` restores Compose. Only tear down when the evidence is safely recorded.

---

## Final editing and submission checklist

- [ ] Combine clips in the order **Subhodip intro → Ria → Shekar → Subhodip Kubernetes**.
- [ ] Keep the full video between **6:00 and 8:00**; target **7:35**. Avoid long silent waits; speed up or trim only inactive waiting periods, never manufacture or conceal a test outcome.
- [ ] Show a visible timestamp and a real command/output for every technical task.
- [ ] Check that the four microservices and the PostgreSQL pod/containers are healthy in the appropriate segments.
- [ ] Show a real Compose booking workflow and a clearly explained port-conflict diagnosis and fix.
- [ ] Show a live Kubernetes scale/delete/replacement operation and identify the passing saved validation evidence as recorded evidence.
- [ ] Verify `RESULT: PASS` in the full Task 2, Task 3 and Kubernetes evidence logs before claiming success.
- [ ] Make sure any **fourth officially listed team member** speaks in the final combined video; if necessary, revise this timing plan.
- [ ] Review the final edit for readable text, clear audio and no visible passwords, tokens or other secrets.
- [ ] Upload the combined video to the agreed Google Drive folder and add its share link to `docs/report-a2/report.html`.

**Related files:** `docs/report/demo-video-script.md` (Assignment 1 format); `docs/report-a2/report.html`; `scripts/a2-verify-deployment.sh`; `scripts/a2-troubleshoot-demo.sh`; `scripts/k8s-deploy.sh`; `scripts/a2-k8s-verify.sh`; `scripts/a2-k8s-scale-heal.sh`.
