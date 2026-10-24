# SlotHub — Product Backlog

> Agile product backlog for SlotHub (see [solution-architecture.md](solution-architecture.md)).
> Hierarchy: **Epic → Feature → Product Backlog Item (PBI)**. Tasks are created under PBIs during sprint planning.
> Order: all **implementation** epics first (E1–E8), then documentation and submission (E9), then the unscheduled future backlog (E10).

| Item | Value |
|---|---|
| Sprint length | 10 days |
| Estimation | Story points (Fibonacci: 1, 2, 3, 5, 8) |
| Priority | **Must** / **Should** / **Could** (Could items are dropped first if the sprint is overloaded) |
| PBI states | New → Ready → In Progress → In Review → Done |
| Owner | Assign one owner per service epic (needed for individual marks); `TBD` until sprint planning |

---

## Contents

1. [Sprint plan](#1-sprint-plan)
2. [Definition of Ready and Done](#2-definition-of-ready-and-done)
3. [Epic overview](#3-epic-overview)
4. [E1 — Platform Foundation](#e1--platform-foundation)
5. [E2 — Scheduling Service](#e2--scheduling-service)
6. [E3 — Booking Service](#e3--booking-service)
7. [E4 — Payment Service (mock)](#e4--payment-service-mock)
8. [E5 — Notification Service (mock)](#e5--notification-service-mock)
9. [E6 — Engineering Challenges](#e6--engineering-challenges)
10. [E7 — Testing and Quality](#e7--testing-and-quality)
11. [E8 — Developer Experience](#e8--developer-experience)
12. [E9 — Evidence, Report and Submission](#e9--evidence-report-and-submission)
13. [E10 — Future Backlog (unscheduled)](#e10--future-backlog-unscheduled)
14. [Business rule traceability](#business-rule-traceability)

---

## 1. Sprint plan

| Sprint | Dates (10 days) | Sprint goal | Points | Status |
|---|---|---|---|---|
| **Sprint 1** | Mon 26 Oct – Wed 4 Nov 2026 | Foundation in place; Scheduling holds slots without over-allocation; Payment and Notification mocks running; Challenge 1 script passes. | 93 (+6 Docker) | ✅ Done |
| **Sprint 2** | Thu 5 Nov – Sat 14 Nov 2026 | Booking runs the full transaction end to end, including failures; idempotency and time-bound holds work; Challenge 2 and 3B scripts pass. | 92 | ✅ Done |
| **Sprint 3** | Sun 15 Nov – Tue 24 Nov 2026 | Hardening (integration tests, README, Postman); evidence captured; report, video and submission done. | 52 | In Progress (all implementation ✅; team actions remain) |

> **Scope:** Assignment 1 only. Assignment 2 work (Minikube/Kubernetes) is out of scope; only the Dockerfiles + compose file were pulled in, for local API testing.

> ⚠️ Three 10-day sprints = 30 days, which is longer than the assignment's 3-week duration. Adjust the dates to your submission deadline, or compress Sprint 3 into the last days of Sprint 2.

**Ceremonies per sprint:** planning (day 1), daily stand-up (15 min), mid-sprint backlog refinement (day 5), review + retrospective (day 10).

### Sprint 1 scope
PBI-001 → PBI-024 (E1, E2), PBI-039 → PBI-045 (E4, E5), PBI-047, PBI-062.

### Sprint 1 review (done)
- Delivered: PBI-001 → PBI-024, PBI-039 → PBI-045, PBI-047, PBI-062, plus PBI-065, PBI-083, PBI-084 (Docker for local API testing).
- 43 unit tests pass (Scheduling 24, Payment 8, Notification 11). Challenge 1 passes: capacity 1 / 20 requests and capacity 5 / 30 requests (`evidence/challenge-1-concurrency/`).
- Open follow-ups (team actions, not code): create the GitHub remote and give every member push access (PBI-001); CI workflow (PBI-014) is written but has not run on GitHub yet; nothing is committed yet.
- BR1 is enforced in SQL, so its proof is the Challenge 1 script now and the Testcontainers test PBI-048 in Sprint 2.

### Sprint 2 review (done)
- Delivered: PBI-025 → PBI-038 (Booking), PBI-048 → PBI-051, PBI-053 → PBI-057, PBI-059.
- 94 automated tests pass: 90 unit + 4 Testcontainers integration (`evidence/tests/unit-and-integration-*.txt`).
- Scripts pass against the Docker stack: `e2e-booking.sh` (26 checks), `challenge2-idempotency.sh` (20), `challenge3b-holds.sh` (18), Challenge 1 re-run.
- Beyond plan: Booking ↔ Payment circuit breaker (Challenge 3A bonus, shown in the e2e failure path); late-payment compensation (refund) demonstrated in the 3B script.
- Design notes: per-booking operation lease (`locked_until`) serialises pay/cancel/reconcile; Payment finishes an in-flight charge even if the caller disconnects so the retry gets the stored result.
- Known limitations: a refund that fails after a late payment is only logged (manual follow-up); retrying a cancellation across the 24 h boundary would compute a different refund and get 422 from Payment.

### Sprint 2 scope
PBI-025 → PBI-038 (E3), PBI-048 → PBI-051, PBI-053 → PBI-057, PBI-059.

### Sprint 3 review (implementation done; team actions open)
- Delivered: PBI-046 (naive counter-example: 20 holds on capacity 1), PBI-052 (key cleanup job), PBI-058 (5 Payment API idempotency integration tests), PBI-060 (Postman: 38 requests, 71 assertions via newman), PBI-061 (clean-copy run from README), PBI-063, PBI-064 (`docs/openapi/`), PBI-066/070 (evidence READMEs, raw logs), PBI-071–076 (report `docs/report/SlotHub-Design-Report.pdf`, 10 pages), video script.
- 99 automated tests pass. `scripts/run-all-checks.sh` regenerates every evidence file.
- **Team actions left:** member names/IDs and contribution shares (Appendix A), reflections (B), confirm AI declaration (C), GitHub + Drive links (§10), screenshots next to the evidence files, rehearse and record the video, commit history per member, pre-submission checklist, submit.

### Sprint 3 scope
PBI-046, PBI-052, PBI-058, PBI-060, PBI-061, PBI-063, PBI-064, PBI-066 → PBI-082 (E9).

---

## 2. Definition of Ready and Done

**Definition of Ready (PBI can enter a sprint)**
- Has a clear title, acceptance criteria and an estimate.
- Dependencies are Done or scheduled earlier in the same sprint.
- Linked business rule (BR) noted where relevant.

**Definition of Done (PBI can be closed)**
- Code merged to `main` via pull request reviewed by another team member.
- Builds with no warnings; all tests pass in CI.
- Business logic sits in the Domain project; unit test added for every BR touched.
- Endpoints: versioned under `/api/v1`, validated, return Problem Details on error, appear in OpenAPI.
- Configuration via environment variables only; `.env.example` updated.
- Logs are structured and include the correlation ID.

---

## 3. Epic overview

| Epic | Name | Goal | Sprint(s) | Points | Status |
|---|---|---|---|---|---|
| E1 | Platform Foundation | Repo, solution, local Postgres, shared cross-cutting library, CI | S1 | 31 | ✅ Done |
| E2 | Scheduling Service | Catalog, slots and concurrency-safe holds | S1 | 36 | ✅ Done |
| E3 | Booking Service | Booking lifecycle and orchestration across services | S2 | 52 | ✅ Done |
| E4 | Payment Service (mock) | Charge/refund with failure injection (idempotency keys in E6) | S1 | 9 | ✅ Done |
| E5 | Notification Service (mock) | Deduplicated, retried message delivery | S1 | 10 | ✅ Done |
| E6 | Engineering Challenges | C1 concurrency, C2 idempotency, C3B time-bound holds | S1–S3 | 41 | ✅ Done |
| E7 | Testing and Quality | Unit, integration and end-to-end tests | S2–S3 | 17 | ✅ Done |
| E8 | Developer Experience | Seed/run scripts, Docker for local testing, README, OpenAPI export | S1–S3 | 14 | ✅ Done |
| E9 | Evidence, Report and Submission | Evidence folders, PDF report, video, submission | S3 | 35 | In Progress (evidence + report ✅; team actions remain) |
| E10 | Future Backlog | Assignment 2 (Minikube) and product growth | Unscheduled | — | Out of scope |

**Status values:** New · In Progress · ✅ Done · Out of scope. An epic is marked ✅ Done only when all its PBIs are Done.

---

## E1 — Platform Foundation

### F1.1 Repository and solution

| ID | PBI | Acceptance criteria | SP | Pri | Sprint | Depends | Status |
|---|---|---|---|---|---|---|---|
| PBI-001 | Create repository and folder layout | Git repo with `docs/`, `services/`, `shared/`, `scripts/`, `evidence/`, `postman/`<br>`.gitignore` (.NET), `.editorconfig`, README skeleton<br>All members have push access | 2 | Must | S1 | — | ✅ Done |
| PBI-002 | Create solution and 4 service skeletons | `SlotHub.slnx` (single solution) with `Api`, `Domain`, `Infrastructure`, `UnitTests` projects per service<br>Each service runs alone with `dotnet run`<br>Project references point inwards (Api/Infrastructure → Domain) | 3 | Must | S1 | 001 | ✅ Done |
| PBI-003 | Central build settings | `Directory.Build.props` (net10.0, nullable, warnings as errors)<br>`Directory.Packages.props` for central package versions | 2 | Should | S1 | 002 | ✅ Done |

### F1.2 Local infrastructure and configuration

| ID | PBI | Acceptance criteria | SP | Pri | Sprint | Depends | Status |
|---|---|---|---|---|---|---|---|
| PBI-004 | PostgreSQL with 4 databases and 4 users | `docker-compose.yml` runs PostgreSQL 17<br>Init script creates `scheduling_db`, `booking_db`, `payment_db`, `notification_db` with separate users<br>Each user can connect **only** to its own DB (verified by a failed cross-DB login) | 3 | Must | S1 | 001 | ✅ Done |
| PBI-005 | Environment-based configuration | All ports, URLs, connection strings and tunables read from env vars<br>Options validated at startup; service fails fast with a clear message if missing<br>`.env.example` per service | 2 | Must | S1 | 002 | ✅ Done |
| PBI-006 | EF Core setup per service | DbContext + initial migration per service<br>Migrations applied on startup when `DB_MIGRATE_ON_STARTUP=true` | 2 | Must | S1 | 004 | ✅ Done |

### F1.3 BuildingBlocks (cross-cutting library)

| ID | PBI | Acceptance criteria | SP | Pri | Sprint | Depends | Status |
|---|---|---|---|---|---|---|---|
| PBI-007 | Correlation ID propagation | Middleware reads `X-Correlation-Id` or generates one<br>Added to log scope and response header<br>`DelegatingHandler` adds it to all outgoing `HttpClient` calls | 3 | Must | S1 | 002 | ✅ Done |
| PBI-008 | Structured logging | Serilog compact JSON to stdout<br>Enriched with `service`, `correlationId`, request path, status, duration | 2 | Must | S1 | 007 | ✅ Done |
| PBI-009 | Problem Details error model | Global exception handler maps domain exceptions → status + stable `code`<br>Body includes `correlationId`<br>Error code catalogue in `BuildingBlocks` | 3 | Must | S1 | 002 | ✅ Done |
| PBI-010 | Request validation | FluentValidation wired in; invalid input → `400 VALIDATION_FAILED` with field `errors` | 2 | Must | S1 | 009 | ✅ Done |
| PBI-011 | Health checks | `GET /health` (liveness) and `GET /health/ready` (DB check) on every service | 2 | Must | S1 | 006 | ✅ Done |
| PBI-012 | OpenAPI and versioned routes | All endpoints under `/api/v1`<br>OpenAPI at `/openapi/v1.json`, Scalar UI at `/scalar`<br>Error responses documented | 2 | Must | S1 | 002 | ✅ Done |
| PBI-013 | Injectable clock | `TimeProvider` registered; fake clock helper for tests | 1 | Must | S1 | 002 | ✅ Done |

### F1.4 Continuous integration

| ID | PBI | Acceptance criteria | SP | Pri | Sprint | Depends | Status |
|---|---|---|---|---|---|---|---|
| PBI-014 | CI pipeline | GitHub Actions runs restore, build and unit tests on every PR and push to `main`<br>PR cannot merge if the pipeline fails | 2 | Should | S1 | 002 | ✅ Done |

---

## E2 — Scheduling Service

### F2.1 Catalog

| ID | PBI | Acceptance criteria | SP | Pri | Sprint | Depends | Status |
|---|---|---|---|---|---|---|---|
| PBI-015 | Tenants | `POST /tenants`, `GET /tenants/{id}`<br>Name required, timezone valid IANA id<br>`404 TENANT_NOT_FOUND` | 3 | Must | S1 | 006, 009 | ✅ Done |
| PBI-016 | Resources | `POST/GET /tenants/{tenantId}/resources`<br>Resource belongs to an existing tenant | 2 | Must | S1 | 015 | ✅ Done |
| PBI-017 | Service offerings | `POST/GET /tenants/{tenantId}/offerings`<br>Duration > 0, price ≥ 0 in minor units, currency ISO-4217 | 3 | Must | S1 | 015 | ✅ Done |

### F2.2 Slots

| ID | PBI | Acceptance criteria | SP | Pri | Sprint | Depends | Status |
|---|---|---|---|---|---|---|---|
| PBI-018 | Create slots (batch) | `POST /slots` creates many slots for one resource and offering<br>`end_at > start_at`; length = offering duration; capacity ≥ 1<br>Overlapping slots for the same resource rejected (`409 SLOT_OVERLAP`)<br>DB `CHECK (0 ≤ reserved_count ≤ capacity)` | 5 | Must | S1 | 016, 017 | ✅ Done |
| PBI-019 | Search and get slots | `GET /slots?tenantId&resourceId&from&to&available` with paging<br>`GET /slots/{id}` returns `capacity`, `reserved`, `remaining` | 3 | Must | S1 | 018 | ✅ Done |

### F2.3 Holds

| ID | PBI | Acceptance criteria | SP | Pri | Sprint | Depends | Status |
|---|---|---|---|---|---|---|---|
| PBI-020 | Hold state machine (domain) | `Hold` class allows only HELD→CONFIRMED/RELEASED/EXPIRED and CONFIRMED→RELEASED<br>Invalid transition throws `InvalidStateTransition`<br>Unit tests for every allowed and one disallowed transition | 3 | Must | S1 | 013 | ✅ Done |
| PBI-021 | Hold a slot atomically (**BR1, BR6**) | `POST /holds {slotId, bookingId}` → `201 {holdId, expiresAt, price}`<br>Single conditional `UPDATE … WHERE reserved_count < capacity AND start_at > now()` + hold insert in one transaction<br>No capacity → `409 SLOT_UNAVAILABLE`; past slot → `409 SLOT_IN_PAST`<br>Same `bookingId` again returns the existing hold (unique constraint), no extra capacity used | 8 | Must | S1 | 018, 020 | ✅ Done |
| PBI-022 | Confirm a hold (**BR2**) | `POST /holds/{id}/confirm` succeeds only if `status = HELD AND expires_at > now()` (single conditional update)<br>Expired → `409 HOLD_EXPIRED`; already confirmed → `200` (idempotent) | 5 | Must | S1 | 021 | ✅ Done |
| PBI-023 | Release a hold | `POST /holds/{id}/release` from HELD or CONFIRMED → RELEASED, capacity returned once<br>Releasing an already released/expired hold → `200`, no capacity change | 3 | Must | S1 | 021 | ✅ Done |
| PBI-024 | Get a hold | `GET /holds/{id}` returns status, slot, expiry | 1 | Must | S1 | 021 | ✅ Done |

---

## E3 — Booking Service

### F3.1 Booking domain

| ID | PBI | Acceptance criteria | SP | Pri | Sprint | Depends | Status |
|---|---|---|---|---|---|---|---|
| PBI-025 | Booking entity and history | `bookings` and `booking_status_history` tables + migration<br>Every status change writes a history row with reason and correlation ID | 3 | Must | S2 | 006 | ✅ Done |
| PBI-026 | Booking state machine | Allowed: PENDING→HELD/REJECTED; HELD→CONFIRMED/FAILED/EXPIRED/CANCELLED; CONFIRMED→CANCELLED<br>Other transitions → `409 INVALID_STATE_TRANSITION`<br>Unit tests for each transition | 3 | Must | S2 | 025 | ✅ Done |
| PBI-027 | Refund policy (**BR5**) | `RefundPolicy.Calculate(amount, slotStart, now)`: ≥ 24 h → 100 %, < 24 h → 50 %, after start → not allowed<br>Boundary tests at exactly 24 h and at start time | 2 | Must | S2 | 013 | ✅ Done |

### F3.2 Downstream integration

| ID | PBI | Acceptance criteria | SP | Pri | Sprint | Depends | Status |
|---|---|---|---|---|---|---|---|
| PBI-028 | Typed HTTP clients | Clients for Scheduling, Payment, Notification; base URLs from env<br>Correlation handler attached<br>Downstream Problem Details mapped to typed results | 3 | Must | S2 | 007 | ✅ Done |
| PBI-029 | Resilience pipeline | Per-attempt timeout, retry with exponential backoff + jitter, circuit breaker (values from env)<br>Retries only on timeouts/5xx, never on 4xx<br>Exhausted → `503 DOWNSTREAM_UNAVAILABLE` / `504 DOWNSTREAM_TIMEOUT` | 5 | Must | S2 | 028 | ✅ Done |

### F3.3 Create booking

| ID | PBI | Acceptance criteria | SP | Pri | Sprint | Depends | Status |
|---|---|---|---|---|---|---|---|
| PBI-030 | Create booking (hold slot) | `POST /bookings` creates PENDING, calls `POST /holds`<br>Success → HELD with price snapshot and `holdExpiresAt`, `201`<br>`409` from Scheduling → REJECTED, same `409` returned | 5 | Must | S2 | 021, 026, 028 | ✅ Done |
| PBI-031 | Query bookings | `GET /bookings/{id}`; `GET /bookings?customerId&status` with paging | 2 | Must | S2 | 025 | ✅ Done |

### F3.4 Pay and confirm

| ID | PBI | Acceptance criteria | SP | Pri | Sprint | Depends | Status |
|---|---|---|---|---|---|---|---|
| PBI-032 | Pay and confirm — happy path (**BR3**) | `POST /bookings/{id}/payment` only when HELD and hold expires later than `HOLD_SAFETY_MARGIN_SECONDS`<br>Charges Payment with key `pay-{bookingId}`, then confirms hold<br>Booking → CONFIRMED with `paymentId`; `200` | 8 | Must | S2 | 030, 022, 039, 029 | ✅ Done |
| PBI-033 | Payment failure handling | Declined (`402`) → release hold, booking FAILED, return `402 PAYMENT_DECLINED`<br>Gateway unavailable after retries → booking stays HELD, return `503`; client may retry (no double charge thanks to `pay-{bookingId}`) | 5 | Must | S2 | 032 | ✅ Done |
| PBI-034 | Compensation for late payment (**BR3**) | Payment succeeded but confirm returns `409 HOLD_EXPIRED` → refund via `refund-{bookingId}`<br>Booking → EXPIRED with `refunded_minor` set; `409 HOLD_EXPIRED` returned | 3 | Must | S2 | 032, 040 | ✅ Done |

### F3.5 Cancellation

| ID | PBI | Acceptance criteria | SP | Pri | Sprint | Depends | Status |
|---|---|---|---|---|---|---|---|
| PBI-035 | Cancel booking (**BR5**) | `POST /bookings/{id}/cancel`<br>HELD → release hold → CANCELLED<br>CONFIRMED → refund per BR5 → release hold → CANCELLED<br>After slot start → `409 CANCELLATION_NOT_ALLOWED` | 5 | Must | S2 | 027, 023, 040 | ✅ Done |

### F3.6 Background processing

| ID | PBI | Acceptance criteria | SP | Pri | Sprint | Depends | Status |
|---|---|---|---|---|---|---|---|
| PBI-036 | Booking expiry sync | Background job marks HELD bookings past `hold_expires_at` as EXPIRED<br>Safe to run concurrently / twice (conditional update on status) | 2 | Must | S2 | 026 | ✅ Done |
| PBI-037 | Transactional outbox | Status changes to CONFIRMED / CANCELLED / EXPIRED / FAILED write an outbox message in the **same** DB transaction | 3 | Must | S2 | 025 | ✅ Done |
| PBI-038 | Outbox dispatcher | Background job sends pending outbox messages to Notification with backoff<br>Marks processed on `202`; Notification being down never affects booking state | 3 | Must | S2 | 037, 042 | ✅ Done |

---

## E4 — Payment Service (mock)

| ID | PBI | Acceptance criteria | SP | Pri | Sprint | Depends | Status |
|---|---|---|---|---|---|---|---|
| PBI-039 | Charge payment | `POST /payments {bookingId, amount, currency}` → `201 SUCCEEDED` or `402 PAYMENT_DECLINED`<br>`GET /payments/{id}`<br>Status lifecycle PENDING → SUCCEEDED/FAILED in domain class | 3 | Must | S1 | 006, 009 | ✅ Done |
| PBI-040 | Refunds | `POST /payments/{id}/refunds {amount}`; total refunds ≤ charged amount, else `409 REFUND_EXCEEDS_PAYMENT`<br>Status → PARTIALLY_REFUNDED / REFUNDED | 3 | Must | S1 | 039 | ✅ Done |
| PBI-041 | Chaos settings | `CHAOS_FAILURE_RATE` and `CHAOS_LATENCY_MS` from env<br>`PUT /admin/chaos` changes them at runtime for the demo<br>Failures returned as `402` (declined) or `503` (gateway error), configurable | 3 | Must | S1 | 039 | ✅ Done |

---

## E5 — Notification Service (mock)

| ID | PBI | Acceptance criteria | SP | Pri | Sprint | Depends | Status |
|---|---|---|---|---|---|---|---|
| PBI-042 | Accept notification | `POST /notifications {bookingId, eventType, channel, recipient, data}` → `202`<br>`UNIQUE (booking_id, event_type, channel)`; duplicates return `202` with the existing message, nothing new sent | 3 | Must | S1 | 006, 009 | ✅ Done |
| PBI-043 | Message templates | Templates for BookingConfirmed, BookingCancelled, BookingExpired, PaymentFailed | 2 | Should | S1 | 042 | ✅ Done |
| PBI-044 | Delivery worker with retries | Mock provider "sends" (logs) the message; failures retried with backoff<br>After `NOTIFY_MAX_ATTEMPTS` → DEAD<br>Each attempt stored in `delivery_attempts` | 3 | Must | S1 | 042 | ✅ Done |
| PBI-045 | Query and chaos | `GET /notifications?bookingId`<br>`PUT /admin/chaos` for provider failure rate and latency | 2 | Must | S1 | 044 | ✅ Done |

---

## E6 — Engineering Challenges

### F6.1 Challenge 1 — Concurrency control

| ID | PBI | Acceptance criteria | SP | Pri | Sprint | Depends | Status |
|---|---|---|---|---|---|---|---|
| PBI-046 | Naive mode for comparison | `CONCURRENCY_MODE=naive` switches holds to read-check-then-update (demo only, off by default)<br>Running the C1 script in naive mode shows over-allocation | 3 | Could | S3 | 021 | ✅ Done |
| PBI-047 | Challenge 1 script | `scripts/challenge1-concurrency` seeds a slot with capacity N (1 and 5)<br>Fires ≥ 20 parallel hold requests<br>Prints counts per status (exactly N × `201`, rest `409`) and dumps `slots`/`holds` rows<br>Writes timestamped output to `evidence/challenge-1-concurrency/` | 5 | Must | S1 | 021, 062 | ✅ Done |
| PBI-048 | Concurrency integration test | Testcontainers test runs 50 parallel holds on capacity 5 → exactly 5 holds, `reserved_count = 5` | 3 | Must | S2 | 021 | ✅ Done |

### F6.2 Challenge 2 — Idempotency (**BR4**)

| ID | PBI | Acceptance criteria | SP | Pri | Sprint | Depends | Status |
|---|---|---|---|---|---|---|---|
| PBI-049 | Idempotency store | `idempotency_keys` table per service (Booking, Payment): key + scope PK, request hash, state, response status/body, expiry | 2 | Must | S2 | 006 | ✅ Done |
| PBI-050 | Idempotency endpoint filter | `[Idempotent]` filter in BuildingBlocks: SHA-256 request fingerprint; **insert-first** as the lock<br>Same key + same hash + COMPLETED → replay with `Idempotency-Replayed: true`<br>Same key + different hash → `422 IDEMPOTENCY_KEY_MISMATCH`<br>IN_PROGRESS → `409 REQUEST_IN_PROGRESS` + `Retry-After`<br>5xx responses are not stored (key released for retry)<br>Missing header on protected endpoint → `400` | 8 | Must | S2 | 049, 009 | ✅ Done |
| PBI-051 | Apply idempotency to unsafe endpoints | Booking: `POST /bookings`, `/payment`, `/cancel`<br>Payment: `POST /payments`, `/refunds` | 2 | Must | S2 | 050 | ✅ Done |
| PBI-052 | Idempotency key cleanup | Background job deletes keys older than `IDEMPOTENCY_TTL_HOURS` | 1 | Could | S3 | 049 | ✅ Done |
| PBI-053 | Challenge 2 script | Same key twice → identical response, one DB row<br>Same key, different body → `422`<br>10 parallel identical requests → one effect, others replay/`409`<br>Saves outputs, replay log lines and DB queries to `evidence/challenge-2-idempotency/` | 5 | Must | S2 | 051, 032 | ✅ Done |

### F6.3 Challenge 3B — Time-bound holds (**BR2**)

| ID | PBI | Acceptance criteria | SP | Pri | Sprint | Depends | Status |
|---|---|---|---|---|---|---|---|
| PBI-054 | Hold expiry worker | `BackgroundService` every `HOLD_SWEEP_INTERVAL_SECONDS`<br>Batch with `FOR UPDATE SKIP LOCKED`, only `status = HELD AND expires_at <= now()`<br>Holds → EXPIRED and capacity released in one transaction<br>Never touches CONFIRMED holds; second run is a no-op | 5 | Must | S2 | 021, 022 | ✅ Done |
| PBI-055 | Manual sweep trigger | `POST /api/v1/admin/holds/expire-now` runs one sweep and returns the count (demo "run twice") | 2 | Should | S2 | 054 | ✅ Done |
| PBI-056 | Challenge 3B script | Hold → wait > TTL → hold EXPIRED, capacity restored<br>Trigger sweep twice → no change second time<br>Confirmed hold survives expiry; cancel releases immediately<br>Output to `evidence/challenge-3b-holds/` | 5 | Must | S2 | 054, 055, 035 | ✅ Done |

---

## E7 — Testing and Quality

| ID | PBI | Acceptance criteria | SP | Pri | Sprint | Depends | Status |
|---|---|---|---|---|---|---|---|
| PBI-057 | Business rule unit tests | At least one unit test per BR1–BR6, named `BRn_…`<br>Traceability table (below) filled with real file paths | 3 | Must | S2 | 021, 027, 032, 050, 054 | ✅ Done |
| PBI-058 | Integration tests | Testcontainers tests for hold expiry worker and idempotency filter (incl. parallel identical requests) | 5 | Should | S3 | 050, 054 | ✅ Done |
| PBI-059 | End-to-end script | `scripts/e2e-booking` seeds data, creates booking, pays, verifies CONFIRMED and notification sent<br>Also runs one failure (payment declined → FAILED, capacity restored)<br>Exits non-zero on any failed check | 5 | Must | S2 | 032, 033, 038 | ✅ Done |
| PBI-060 | Postman collection | Collection + environment covering every endpoint, with example requests and failure cases | 2 | Should | S3 | 035 | ✅ Done |
| PBI-061 | Fresh-clone verification | Another member clones on a clean machine and runs everything from README only; gaps fixed | 2 | Must | S3 | 063 | ✅ Done (clean-copy check passed; repeat on a teammate's machine) |

---

## E8 — Developer Experience

| ID | PBI | Acceptance criteria | SP | Pri | Sprint | Depends | Status |
|---|---|---|---|---|---|---|---|
| PBI-062 | Seed script | `scripts/seed` creates a tenant, resource, offering and slots (configurable capacity); prints IDs | 2 | Must | S1 | 018 | ✅ Done |
| PBI-063 | Complete README | What it is, prerequisites, how to start DB and each service, ports, env vars, how to run tests and each script | 3 | Must | S3 | all E1–E6 | ✅ Done |
| PBI-064 | Export OpenAPI specs | `docs/openapi/{service}.json` exported from each running service | 1 | Must | S3 | 012 | ✅ Done |
| PBI-065 | Run-all script | One command starts PostgreSQL and all 4 services (and stops them) | 2 | Should | S2 | 004 | ✅ Done (`docker compose up -d --build --wait` / `down`) |
| PBI-083 | Dockerfile per service (local API testing) | Multi-stage build from repo root, non-root runtime user, listens on 8080<br>Pulled in from Assignment 2 **only** so the APIs can be tested locally | 3 | Should | S1 | 002 | ✅ Done |
| PBI-084 | Compose file for all services + DB | `docker compose up -d --build --wait` runs PostgreSQL + 4 services on localhost:5101–5104<br>`docker compose up -d --wait postgres` runs only the DB for `dotnet run` development | 3 | Should | S1 | 083 | ✅ Done |

---

## E9 — Evidence, Report and Submission

### F9.1 Evidence

| ID | PBI | Acceptance criteria | SP | Pri | Sprint | Depends | Status |
|---|---|---|---|---|---|---|---|
| PBI-066 | Evidence structure | `evidence/challenge-1-concurrency/`, `challenge-2-idempotency/`, `challenge-3b-holds/`, `tests/`, each with a README linking every file to a claim | 2 | Must | S3 | — | ✅ Done |
| PBI-067 | Challenge 1 evidence | Script output, final DB state, raw logs (.txt), screenshots with command + output + timestamp; naive-mode comparison if PBI-046 done | 2 | Must | S3 | 047 | In Progress (text evidence ✅; screenshots by team) |
| PBI-068 | Challenge 2 evidence | First vs repeated call outputs, mismatch `422`, parallel run, replay log lines, DB showing single record | 2 | Must | S3 | 053 | In Progress (text evidence ✅; screenshots by team) |
| PBI-069 | Challenge 3B evidence | Expiry, double run, confirmed-not-released, cancel-releases outputs and logs | 2 | Must | S3 | 056 | In Progress (text evidence ✅; screenshots by team) |
| PBI-070 | Test evidence | Unit test report, e2e output including the failure case | 1 | Must | S3 | 057, 059 | ✅ Done |

### F9.2 Design and technical report (PDF, ≤ 15 pages)

| ID | PBI | Acceptance criteria | SP | Pri | Sprint | Depends | Status |
|---|---|---|---|---|---|---|---|
| PBI-071 | §1–2 Problem, scope, BRs, decomposition | Responsibility table, boundaries, rejected alternatives, ownership | 3 | Must | S3 | — | ✅ Done (draft) |
| PBI-072 | §3 Architecture diagrams | Component diagram + sequence diagram with happy path and a failure path; arrows labelled with direction and protocol | 3 | Must | S3 | — | ✅ Done |
| PBI-073 | §4 Data design | ER diagram per service, ownership table, cross-service references | 2 | Must | S3 | — | ✅ Done |
| PBI-074 | §5 API design | Endpoint table, sample requests/responses, error model, links to OpenAPI files | 2 | Must | S3 | 064 | ✅ Done |
| PBI-075 | §6 Engineering challenges | Problem, design choice, alternatives, evidence links, limitations for C1, C2, C3B | 3 | Must | S3 | 067–069 | ✅ Done |
| PBI-076 | §7–9 Testing, decisions, limitations | Test strategy and results; ≥ 3 trade-off decisions; limitations and future work | 3 | Must | S3 | 070 | ✅ Done |
| PBI-077 | Appendices | Contribution table, half-page reflection per student, AI-use declaration | 2 | Must | S3 | — | In Progress (templates; team fills names, shares, reflections, confirms AI declaration) |
| PBI-078 | §10 Links and PDF export | Google Drive link (video + docs); PDF exported; page count checked | 1 | Must | S3 | 080 | In Progress (PDF builds, 10 pages; Drive/GitHub links by team) |

### F9.3 Demo video (8–10 min)

| ID | PBI | Acceptance criteria | SP | Pri | Sprint | Depends | Status |
|---|---|---|---|---|---|---|---|
| PBI-079 | Video script and rehearsal | Script follows outline (1 min team, 2 min architecture, 3 min live flow + failure, 3 min challenge scripts); every member has a speaking part | 2 | Must | S3 | 059, 047, 053, 056 | In Progress (script ✅ `docs/report/demo-video-script.md`; rehearsal by team) |
| PBI-080 | Record and publish video | Real terminal and API calls; 8–10 min; uploaded to Drive with link access | 3 | Must | S3 | 079 | New (team) |

### F9.4 Submission

| ID | PBI | Acceptance criteria | SP | Pri | Sprint | Depends | Status |
|---|---|---|---|---|---|---|---|
| PBI-081 | Pre-submission checklist | Every item in the assignment checklist ticked; every member has commits | 1 | Must | S3 | all | New (team) |
| PBI-082 | Submit | GitHub link + report PDF + video link submitted | 1 | Must | S3 | 081 | New (team) |

---

## E10 — Future Backlog (unscheduled)

**Out of scope — not worked on during Assignment 1.** Kept here so the product can grow without redesign.
(PBI-083 and PBI-084 — Dockerfiles and compose — were moved to E8 for local API testing only.)

| ID | PBI | Notes | SP | Target | Status |
|---|---|---|---|---|---|
| PBI-085 | Minikube manifests: Deployments, Services, probes, ConfigMaps, Secrets | Assignment 2 | 8 | A2 | Out of scope |
| PBI-086 | API gateway (YARP): routing, rate limiting | Product | 5 | Later | Out of scope |
| PBI-087 | RabbitMQ via MassTransit: outbox publishes booking events | Product | 8 | Later | Out of scope |
| PBI-088 | Authentication (OIDC) with tenant claims | Product | 8 | Later | Out of scope |
| PBI-089 | OpenTelemetry tracing and metrics | Product | 5 | Later | Out of scope |
| PBI-090 | Generate slots from recurring schedules | Product | 5 | Later | Out of scope |
| PBI-091 | Waitlist service | Product | 8 | Later | Out of scope |
| PBI-092 | BR7 — max 2 active holds per customer per tenant | Product | 3 | Later | Out of scope |
| PBI-093 | Real payment gateway (Razorpay/Stripe) behind Payment API | Product | 8 | Later | Out of scope |
| PBI-094 | Real SMS/WhatsApp/email provider behind Notification API | Product | 5 | Later | Out of scope |

---

## Business rule traceability

| BR | Rule (short) | PBI | Code | Tests |
|---|---|---|---|---|
| BR1 | No over-allocation of slot capacity | PBI-021 | `services/scheduling/src/Scheduling.Infrastructure/Persistence/Repositories.cs` (`HoldRepository.ReserveAsync`: single conditional UPDATE) + DB check constraint | `Scheduling.IntegrationTests/HoldConcurrencyTests.BR1_ParallelHolds_NeverExceedCapacity`; `Booking.UnitTests/BookingServiceTests.BR1_Create_IsRejected_WhenSlotIsFull`; `scripts/challenge1-concurrency.sh` |
| BR2 | Hold expires after TTL; capacity returned once; confirmed holds never expired | PBI-022, PBI-054 | `Scheduling.Domain/Holds/Hold.cs` (`Confirm`, `Expire`), `Scheduling.Infrastructure/Persistence/Repositories.cs` (`ExpireDueAsync`, SKIP LOCKED), `Scheduling.Infrastructure/Holds/HoldExpiryWorker.cs` | `Scheduling.UnitTests/HoldTests.BR2_*` (7 tests); `HoldConcurrencyTests.BR2_ExpirySweep_*`, `BR2_ConcurrentSweepers_*`; `scripts/challenge3b-holds.sh` |
| BR3 | Confirm only after payment and while hold active; late payment refunded | PBI-032, PBI-034 | `Booking.Domain/Bookings/Booking.cs` (`EnsurePayable`, `Confirm`), `Booking.Domain/Bookings/BookingService.cs` (`PayAsync`) | `Booking.UnitTests/BookingTests.BR3_*`, `BookingServiceTests.BR3_*`; `scripts/challenge3b-holds.sh` (section E) |
| BR4 | Same key + same payload → replay; different payload → 422; concurrent duplicates → one effect | PBI-050, PBI-051 | `shared/BuildingBlocks/Idempotency/IdempotencyMiddleware.cs`, `IdempotencyStore.cs`, `RequestFingerprint.cs`; deterministic keys in `Booking.Infrastructure/Clients/PaymentClient.cs` | `BuildingBlocks.UnitTests/RequestFingerprintTests.BR4_*`; `BookingServiceTests.Pay_Twice_DoesNotChargeTwice`; `scripts/challenge2-idempotency.sh` |
| BR5 | Refund 100 % (≥ 24 h) / 50 % (< 24 h) / not allowed after start | PBI-027, PBI-035 | `Booking.Domain/Bookings/RefundPolicy.cs`, `BookingService.CancelAsync` | `Booking.UnitTests/RefundPolicyTests.BR5_*`, `BookingServiceTests.BR5_CancelConfirmed_Within24Hours_RefundsHalf_AndReleasesSlot` |
| BR6 | Past slots cannot be held | PBI-021 | `Scheduling.Domain/Slots/Slot.cs` (`HasStarted`, `CanBeHeld`), `HoldService.CreateAsync`, SQL `start_at > now` | `Scheduling.UnitTests/SlotTests.BR6_*` |
