# Task 2 — Deploy the complete application with Docker Compose

Deployment: `cp .env.example .env` (set passwords) → `docker compose up --build`.

| File | Produced by | Shows |
|---|---|---|
| `run-*.txt` from **verify** | `scripts/a2-verify-deployment.sh` | 15 checks. `docker compose ps` with all 5 containers `healthy`; `/health/ready` of each service. **Inter-service communication:** one booking sent with `X-Correlation-Id: trace-…` and found in the logs of all four services — Booking (held, confirmed), Scheduling (hold reserved, confirmed), Payment (gateway approved, idempotency key stored) and Notification (two messages queued via Booking's outbox). `docker inspect` health-check details. **Main workflow:** the full `e2e-booking.sh` (26 checks: happy path, declined payment, gateway outage with retries and circuit breaker) |
| `run-*.txt` from **restart** | `scripts/a2-restart-demo.sh` | 9 checks. A booking confirmed before stopping survives: (A) `docker compose stop` → API unreachable → `up` → healthy, booking still `CONFIRMED`, new flow works; (B) `docker compose down` (containers and network removed, volume kept) → `up` → same; (C) `docker compose restart booking` → healthy in ~5 s, new flow works |

**Start-up ordering.** Services wait for PostgreSQL to be **healthy**. Booking waits for Scheduling and Payment to be
**healthy** (`depends_on: condition: service_healthy`). It only waits for Notification to *start*, because notifications go
through the outbox and an SMS outage must never block bookings.

**Logs.** `docker compose logs -f <service>` shows structured JSON (Serilog). Every line carries the `CorrelationId`,
which is forwarded on HTTP calls and through the outbox, so one request can be followed across containers:

```bash
docker compose logs --no-log-prefix booking scheduling payment notification | grep '"trace-<run-id>"'
```
