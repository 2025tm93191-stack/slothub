# Demo video script (8–10 minutes)

The brief requires real terminal and API calls (not only slides), with every member speaking. Each member presents the
parts they own (report Appendix A) and must be able to explain the code and output they show.

| Time | Segment | Speaker |
|---|---|---|
| 0:00–2:30 | 1. Team, application and architecture | Subhodip Banerjee |
| 2:30–5:00 | 2. Payment service, main flow and a failure | Ria Mishra |
| 5:00–7:00 | 3. Notification, idempotency (Challenge 2) and tests | J K Shekar |
| 7:00–9:30 | 4. Concurrency (Challenge 1), time-bound holds (Challenge 3B) and close | Subhodip Banerjee |

Each member can record their segment separately (screen recording with voice) and the parts are joined afterwards. Run
the setup below before each recording.

## Before recording (5 minutes)

```bash
docker compose down -v && docker compose up -d --build --wait   # clean database, all services healthy
```

- Terminal font ≥ 16 pt, dark theme, window wide enough for one JSON line. Run `clear` before each segment.
- Second terminal (segments 2–3): `docker compose logs -f booking payment notification | jq -Rr 'fromjson? | "\(.["@t"][11:19]) \(.Service) \(.CorrelationId // "-") \(.["@m"])"'`.
- Browser tabs: `docs/report/SlotHub-Design-Report.pdf` (Figures 1–3), `http://localhost:5102/scalar`.
- Do one dry run with a stopwatch. Re-record a segment rather than cutting mid-command.

## 0:00–2:30 · 1. Team, application and architecture · Subhodip Banerjee

- "We are Subhodip, Shekar and Ria. We built **SlotHub**, a multi-tenant slot booking platform: clinics, salons and turfs
  publish slots; customers hold a slot, pay and get confirmed."
- Who owned what: Subhodip: Scheduling, Booking, the shared building blocks and Challenges 1 and 3B. Ria: the Payment service.
  Shekar: Payment and Notification, the tests and the Challenge 2 evidence.
- Show Figure 1 (components), then Figure 2/3 (sequences) from the report PDF.
  - One sentence per service (report §2 table). Catalog and availability are **one** service because they change together.
  - Booking is the only orchestrator; the others are small idempotent capabilities. Notifications go through an outbox, so
    an SMS outage never touches a booking.
- Database-per-service, live:

  ```bash
  docker exec slothub-postgres psql -U booking_user -d scheduling_db -c 'select 1'   # fails: no CONNECT privilege
  ```

- State machine: point at the booking state diagram; invalid transitions return 409.
- Hand over: "Ria will now run a booking end to end through the Payment service."

## 2:30–5:00 · 2. Payment service, main flow and a failure · Ria Mishra

- Briefly show `services/payment/src/Payment.Infrastructure/Gateway/MockPaymentGateway.cs`: the mock gateway reads the chaos
  settings, so DECLINE returns 402 and ERROR simulates the gateway being down (503).

```bash
seed=$(./scripts/seed.sh --capacity 1); tenant=$(jq -r .tenantId <<<"$seed"); slot=$(jq -r '.slotIds[0]' <<<"$seed")

# 1. Hold (point at status HELD and holdExpiresAt)
booking=$(curl -s -X POST localhost:5102/api/v1/bookings -H 'Content-Type: application/json' -H "Idempotency-Key: demo-1" \
  -d "{\"tenantId\":\"$tenant\",\"slotId\":\"$slot\",\"customer\":{\"id\":\"c-1\",\"phone\":\"+919800000001\"}}" \
  | tee /dev/tty | jq -r .id)

# 2. Pay → CONFIRMED (watch the log terminal: charge, confirm, outbox)
curl -s -X POST localhost:5102/api/v1/bookings/$booking/payment -H "Idempotency-Key: demo-2" | jq '{status, paymentId, history: [.history[].to]}'
curl -s "localhost:5103/api/v1/payments?bookingId=$booking" | jq '.[] | {status, amountMinor, currency}'
```

**Failure case:** payment gateway down.

```bash
seed=$(./scripts/seed.sh --capacity 1); tenant=$(jq -r .tenantId <<<"$seed"); slot=$(jq -r '.slotIds[0]' <<<"$seed")
b=$(curl -s -X POST localhost:5102/api/v1/bookings -H 'Content-Type: application/json' -H "Idempotency-Key: demo-3" \
  -d "{\"tenantId\":\"$tenant\",\"slotId\":\"$slot\",\"customer\":{\"id\":\"c-2\",\"phone\":\"+919800000002\"}}" | jq -r .id)
curl -s -X PUT localhost:5103/api/v1/admin/chaos -H 'Content-Type: application/json' -d '{"failureRate":1,"latencyMs":0,"mode":"ERROR"}'
curl -s -X POST localhost:5102/api/v1/bookings/$b/payment -H "Idempotency-Key: demo-4" | jq '{status, code}'   # 503 after retries
curl -s localhost:5102/api/v1/bookings/$b | jq .status                                                       # still HELD
curl -s -X PUT localhost:5103/api/v1/admin/chaos -H 'Content-Type: application/json' -d '{"failureRate":0,"latencyMs":0,"mode":"DECLINE"}'
sleep 16   # explain: the circuit breaker is open for 15 s to protect the recovering gateway
curl -s -X POST localhost:5102/api/v1/bookings/$b/payment -H "Idempotency-Key: demo-4" | jq '{status, paymentId}'  # CONFIRMED
curl -s "localhost:5103/api/v1/payments?bookingId=$b" | jq 'length'                                             # 1 — no double charge
```

Say: "Retries with backoff and jitter, then the circuit opened; the booking stayed HELD; retrying with the same key charged
exactly once because Booking sends `pay-{bookingId}` as the idempotency key."

Hand over: "Shekar will show the notification for this booking and how idempotency is proven."

## 5:00–7:00 · 3. Notification, idempotency (Challenge 2) and tests · J K Shekar

Use the same terminal as segment 2 so `$booking` is still set (or create a booking with the commands above).

```bash
curl -s "localhost:5104/api/v1/notifications?bookingId=$booking" | jq '.items[] | {eventType, status, body}'   # confirmation SMS
./scripts/challenge2-idempotency.sh | grep -E "^== |PASS|FAIL|RESULT"        # replay, 422, 10 simultaneous → 1 booking, 1 charge
dotnet test services/payment/tests/Payment.UnitTests                         # payment domain: refunds, partial refunds, validation
dotnet test services/notification/tests/Notification.UnitTests               # templates, dedup, retries
```

- Notification: delivered from Booking's outbox, deduplicated per event, retried on failure.
- C2: the insert of the key is the lock; same key with a different body → 422; a 5xx releases the key so the retry
  succeeds. Point at the `idempotency_keys` and `payments` tables in the output.
- Tests: 101 automated tests across the solution (unit + Testcontainers integration against real PostgreSQL); screenshots
  and raw runs are in `evidence/tests/`. Mention the script hang you fixed (bare `wait` → wait on the request PIDs).
- Hand over: "Subhodip will show the concurrency and hold-expiry challenges."

## 7:00–9:30 · 4. Challenge 1, Challenge 3B and close · Subhodip Banerjee

Start the C3B script first and talk over its ~45 s wait.

```bash
./scripts/challenge1-naive-demo.sh | tail -12                                # 20 holds for capacity 1 — why check-then-update fails
./scripts/challenge1-concurrency.sh --capacity 1 --requests 20 | tail -16    # 1 × 201, 19 × 409, DB agrees
./scripts/challenge3b-holds.sh --ttl 30 | grep -E "^== |PASS|FAIL|RESULT"    # ~45 s: expiry, run twice, paid kept, late payment refunded
```

- C1: the atomic `UPDATE … WHERE reserved_count < capacity`; contrast with the naive run's lost updates.
- C3B: `FOR UPDATE SKIP LOCKED` in the expiry worker; confirm and expire both update only `status='HELD'`, so only one can
  win; a payment that arrives after expiry is refunded by the reconciler.
- Close: "All 101 automated tests and every script pass; evidence and raw logs are in `evidence/`, and the README runs
  everything from a fresh clone."
