# SlotHub

A multi-tenant slot booking platform built as .NET microservices. Any business that sells time (clinics, salons, turfs, tutors) publishes slots; customers hold a slot, pay, and get a confirmation.

Scalable Services (SEZG583) — Assignment 1. Design: [docs/solution-architecture.md](docs/solution-architecture.md) · Backlog: [docs/product-backlog.md](docs/product-backlog.md)

| Service | Port | Responsibility | Database |
|---|---|---|---|
| Scheduling | 5101 | Tenants, resources, offerings, slots and concurrency-safe holds | `scheduling_db` |
| Booking | 5102 | Booking lifecycle; orchestrates hold → pay → confirm → notify | `booking_db` |
| Payment (mock) | 5103 | Charges and refunds with failure injection | `payment_db` |
| Notification (mock) | 5104 | Deduplicated, retried SMS/email delivery | `notification_db` |

Each service has its own database **and its own database user**; a service's user cannot connect to another service's database.

### Main flow (try it)

```bash
seed=$(./scripts/seed.sh --capacity 1)                     # tenant, resource, offering, one slot
tenant=$(jq -r .tenantId <<<"$seed"); slot=$(jq -r '.slotIds[0]' <<<"$seed")

# 1. Hold the slot (HELD for HOLD_TTL_SECONDS)
booking=$(curl -s -X POST localhost:5102/api/v1/bookings -H 'Content-Type: application/json' \
  -H "Idempotency-Key: $(uuidgen)" \
  -d "{\"tenantId\":\"$tenant\",\"slotId\":\"$slot\",\"customer\":{\"id\":\"c-1\",\"phone\":\"+919800000001\"}}" | jq -r .id)

# 2. Pay and confirm
curl -s -X POST localhost:5102/api/v1/bookings/$booking/payment -H "Idempotency-Key: $(uuidgen)" | jq '{status, paymentId}'

# 3. Cancel (refund per BR5)
curl -s -X POST localhost:5102/api/v1/bookings/$booking/cancel -H "Idempotency-Key: $(uuidgen)" | jq '{status, refundedMinor}'
```

Unsafe operations (`POST /bookings`, `/payment`, `/cancel`, `POST /payments`, `/refunds`) require an `Idempotency-Key` header.

## Prerequisites

- .NET SDK 10.0.100 or later (`dotnet --version`)
- Docker with Docker Compose
- `curl`, `jq` and `bash` (for the scripts)

## Run

**Option A — everything in Docker (quickest way to test the APIs)**

```bash
docker compose up -d --build --wait     # PostgreSQL + all 4 services on localhost:5101-5104
docker compose logs -f scheduling       # structured JSON logs of one service
docker compose down                     # stop (add -v to also delete the database volume)
```

**Option B — database in Docker, services with `dotnet run` (for development/debugging)**

```bash
# 1. Database only: one PostgreSQL 17 container with 4 databases / 4 users, on localhost:5440
docker compose up -d --wait postgres

# 2. Each service in its own terminal (creates services/<name>/.env from .env.example on first run)
./scripts/run-service.sh scheduling
./scripts/run-service.sh booking
./scripts/run-service.sh payment
./scripts/run-service.sh notification
```

Use one option at a time; both bind ports 5101–5104.

Migrations are applied on startup (`DB_MIGRATE_ON_STARTUP=true`). Check health:

```bash
curl localhost:5101/health          # liveness
curl localhost:5101/health/ready    # readiness (own database reachable)
```

API docs per service: `http://localhost:<port>/scalar` (UI) and `http://localhost:<port>/openapi/v1.json` (spec).

## Configuration

All configuration comes from environment variables. For local runs each service reads `services/<name>/.env` (copied from `.env.example`); real environment variables always take precedence.

| Variable | Services | Default | Purpose |
|---|---|---|---|
| `ASPNETCORE_URLS` | all | `http://0.0.0.0:510x` | Listen address |
| `ConnectionStrings__Default` | all | see `.env.example` | Own database only |
| `DB_MIGRATE_ON_STARTUP` | all | `true` | Apply EF Core migrations at startup |
| `Serilog__MinimumLevel__Default` | all | `Information` | Log level |
| `HOLD_TTL_SECONDS` | Scheduling | `30` | BR2: hold lifetime |
| `HOLD_SWEEP_INTERVAL_SECONDS` | Scheduling, Booking | `5` | Hold-expiry worker / booking reconciliation interval |
| `Services__Scheduling__BaseUrl`, `Services__Payment__BaseUrl`, `Services__Notification__BaseUrl` | Booking | `http://localhost:510x` | Downstream service URLs |
| `HTTP_ATTEMPT_TIMEOUT_SECONDS` / `HTTP_MAX_RETRIES` / `HTTP_TOTAL_TIMEOUT_SECONDS` | Booking | `2` / `3` / `20` | Resilience: timeout per attempt, retries (exponential backoff + jitter), overall budget; circuit breaker opens at ≥ 50 % failures (min 5 calls / 30 s) for 15 s |
| `HOLD_SAFETY_MARGIN_SECONDS` | Booking | `3` | BR3: don't start a payment if the hold expires sooner |
| `BOOKING_LEASE_SECONDS` | Booking | `60` | Max duration of one pay/cancel operation on a booking |
| `OUTBOX_POLL_INTERVAL_SECONDS` | Booking | `2` | Notification outbox dispatch interval |
| `IDEMPOTENCY_TTL_HOURS` / `IDEMPOTENCY_LOCK_TIMEOUT_SECONDS` | Booking, Payment | `24` / `60` | BR4: key retention / when an in-progress key counts as abandoned |
| `CHAOS_FAILURE_RATE` | Payment, Notification | `0` | Probability (0–1) that a call fails |
| `CHAOS_LATENCY_MS` | Payment, Notification | `0` | Added latency per call |
| `CHAOS_FAILURE_MODE` | Payment, Notification | `DECLINE` / `ERROR` | Payment: `DECLINE` (402) or `ERROR` (503) |
| `NOTIFY_MAX_ATTEMPTS` | Notification | `5` | Delivery attempts before `DEAD` |
| `NOTIFY_RETRY_BASE_SECONDS` | Notification | `2` | Backoff base (doubles per attempt, max 60 s) |
| `NOTIFY_POLL_INTERVAL_SECONDS` | Notification | `2` | Delivery worker poll interval |
| `POSTGRES_PORT` | docker compose | `5440` | Host port for PostgreSQL |

Chaos settings can also be changed at runtime:

```bash
curl -X PUT localhost:5103/api/v1/admin/chaos -H 'Content-Type: application/json' \
  -d '{"failureRate":1,"latencyMs":3000,"mode":"ERROR"}'
```

## Test

```bash
dotnet test SlotHub.slnx                       # unit tests + Testcontainers integration tests (needs Docker)
```

With the stack running (`docker compose up -d --build --wait`):

| Script | What it proves | Evidence folder |
|---|---|---|
| `./scripts/e2e-booking.sh` | Main transaction across all 4 services + declined payment + gateway down (retry, circuit breaker) | `evidence/tests/` |
| `./scripts/challenge1-concurrency.sh --capacity 1 --requests 20` | Challenge 1: N parallel holds, exactly capacity succeed | `evidence/challenge-1-concurrency/` |
| `./scripts/challenge1-naive-demo.sh` | Challenge 1 counter-example: a naive check-then-update instance over-allocates | `evidence/challenge-1-concurrency/` |
| `./scripts/challenge2-idempotency.sh` | Challenge 2: replay, key/payload mismatch, simultaneous identical requests, one charge | `evidence/challenge-2-idempotency/` |
| `./scripts/challenge3b-holds.sh --ttl 30` | Challenge 3B: auto-expiry, run-twice safety, paid holds kept, cancel releases, late payment refunded | `evidence/challenge-3b-holds/` |
| `./scripts/seed.sh --capacity 5 --slots 3` | Creates sample tenant/resource/offering/slots | — |
| `./scripts/run-all-checks.sh` | **Everything above** + unit/integration tests + Postman + raw logs, with a PASS/FAIL summary (~3 min) | all of `evidence/` |
| `./scripts/export-openapi.sh` | Refreshes `docs/openapi/<service>.json` from the running services | `docs/openapi/` |
| `./scripts/collect-logs.sh --since 20m` | Saves each service's raw JSON logs | `evidence/logs/` |

**Postman.** Import `postman/SlotHub.postman_collection.json` and `postman/SlotHub.local.postman_environment.json`, select the
"SlotHub local" environment and run the collection top to bottom (Collection Runner). Or run it headless:
`npx newman run postman/SlotHub.postman_collection.json -e postman/SlotHub.local.postman_environment.json`.
The collection is generated by `postman/generate_collection.py`. Edit that and re-run it rather than editing the JSON by hand.

Each script prints PASS/FAIL per check, exits non-zero on failure and saves timestamped output (including DB rows and log lines) to its evidence folder.

## Troubleshooting

| Symptom | Fix |
|---|---|
| `port is already allocated` for 5440 | Another PostgreSQL uses the port: set `POSTGRES_PORT` in a root `.env` (copy `.env.example`) and the matching `Port=` in `services/*/.env` |
| Ports 5101–5104 busy | Stop the other run mode (Option A and B both bind them): `docker compose down` or stop the `dotnet run` terminals |
| Docker builds fail with `input/output error`, or Docker Desktop stops responding | The disk is full: free space, run `docker builder prune -af`, restart Docker Desktop |
| `dotnet test` integration tests fail with "Failed to connect to Docker endpoint" | Start Docker Desktop. Testcontainers needs it |
| Scripts print `jq: command not found` | `brew install jq` (macOS) or `apt install jq` |
| A challenge script fails right after the e2e script | The Booking→Payment circuit breaker may still be open from a failure test; wait 15 s and re-run |

## Conventions

- Every request may send `X-Correlation-Id`; it is generated if missing, echoed in the response, written on every log line and forwarded to downstream calls.
- Errors are RFC 9457 Problem Details with a stable `code` and the `correlationId`.
- Enums are `UPPER_SNAKE_CASE` in JSON and in the database; amounts are integer minor units (paise).

## Repository layout

```
SlotHub.slnx                 single solution covering every project
docker-compose.yml           PostgreSQL (4 databases, 4 users) + the 4 services
services/<name>/Dockerfile   container image per service (build context = repo root)
infra/postgres/init/         database/user creation script
shared/BuildingBlocks.Domain pure C# kernel: DomainException, error codes, paging
shared/BuildingBlocks        cross-cutting web/infra: logging, errors, correlation, health, chaos, persistence helpers
services/<name>/src          <Name>.Api (HTTP), <Name>.Domain (rules), <Name>.Infrastructure (EF Core, workers)
services/<name>/tests        unit tests (+ Scheduling integration tests with Testcontainers)
shared/tests                 BuildingBlocks unit tests (idempotency fingerprint)
scripts/                     run, seed and challenge scripts
evidence/                    challenge script outputs
postman/                     Postman collection + environment (and the script that generates them)
docs/                        architecture, backlog, OpenAPI exports, report
```
