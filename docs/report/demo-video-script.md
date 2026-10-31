# Demo video script (8–10 minutes)

The brief requires real terminal and API calls (not only slides), with every member speaking. Replace `[Member n]` with
names; the suggested split gives each member the service they own.

## Before recording (5 minutes)

```bash
docker compose down -v && docker compose up -d --build --wait   # clean database, all services healthy
./scripts/seed.sh --capacity 1 > /tmp/seed.json                  # optional warm-up
```

- Terminal font ≥ 16 pt, dark theme, window wide enough for one JSON line. Run `clear` before each segment.
- Second terminal: `docker compose logs -f booking scheduling | jq -Rr 'fromjson? | "\(.["@t"][11:19]) \(.Service) \(.CorrelationId // "-") \(.["@m"])"'`.
- Browser tabs: `docs/report/SlotHub-Design-Report.pdf` (Figures 1–3), `http://localhost:5102/scalar`.
- Do one full dry run with a stopwatch. Re-record a segment rather than cutting mid-command.

## 0:00–1:00 — Team and application · [Member 1]

- "We built **SlotHub**, a multi-tenant slot booking platform: clinics, salons and turfs publish slots; customers hold, pay
  and get confirmed."
- Who owned what: [Member 1] Scheduling and Challenge 1; [Member 2] Booking and Challenge 3B; [Member 3] Payment and
  idempotency; [Member 4] Notification, platform and tests.
- Four services, four databases, .NET 10 + PostgreSQL, Docker Compose.

## 1:00–3:00 — Architecture and why · [Member 2]

Show Figure 1 (components), then Figure 2/3 (sequences) from the report PDF.

- One sentence per service (report §2 table). Catalog and availability are **one** service because they change together.
- Booking is the only orchestrator; the others are small idempotent capabilities. Notifications go through an outbox, so
  an SMS outage never touches a booking.
- Database-per-service: show `docker exec slothub-postgres psql -U booking_user -d scheduling_db` failing with
  "User does not have CONNECT privilege".
- State machine: point at the booking state diagram; invalid transitions return 409.

## 3:00–6:00 — Live main flow and a failure · [Member 3]

```bash
seed=$(./scripts/seed.sh --capacity 1); tenant=$(jq -r .tenantId <<<"$seed"); slot=$(jq -r '.slotIds[0]' <<<"$seed")

# 1. Hold (point at status HELD and holdExpiresAt)
booking=$(curl -s -X POST localhost:5102/api/v1/bookings -H 'Content-Type: application/json' -H "Idempotency-Key: demo-1" \
  -d "{\"tenantId\":\"$tenant\",\"slotId\":\"$slot\",\"customer\":{\"id\":\"c-1\",\"phone\":\"+919800000001\"}}" \
  | tee /dev/tty | jq -r .id)

# 2. Pay → CONFIRMED (watch the log terminal: charge, confirm, outbox, SMS sent)
curl -s -X POST localhost:5102/api/v1/bookings/$booking/payment -H "Idempotency-Key: demo-2" | jq '{status, paymentId, history: [.history[].to]}'
curl -s "localhost:5104/api/v1/notifications?bookingId=$booking" | jq '.items[] | {eventType, status, body}'
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

## 6:00–9:00 — Challenge scripts · [Member 4] (Challenges 1, 2) and [Member 2] (3B)

```bash
./scripts/challenge1-naive-demo.sh --capacity 1 --requests 20 | tail -12     # 20 holds for capacity 1 — why check-then-update fails
./scripts/challenge1-concurrency.sh --capacity 1 --requests 20 | tail -16    # 1 × 201, 19 × 409, DB agrees
./scripts/challenge2-idempotency.sh | grep -E "^== |PASS|FAIL|RESULT"        # replay, 422, 10 simultaneous → 1 booking, 1 charge
./scripts/challenge3b-holds.sh --ttl 30 | grep -E "^== |PASS|FAIL|RESULT"    # ~45 s: expiry, run twice, paid kept, late payment refunded
```

- C1: the atomic `UPDATE … WHERE reserved_count < capacity`; contrast with the naive run's lost updates.
- C2: the insert of the key is the lock; show the `idempotency_keys` and `payments` tables in the output.
- C3B: start it first and talk over the 45 s wait about `FOR UPDATE SKIP LOCKED` and why confirm and expiry cannot both win.

## 9:00–9:30 — Close · [Member 1]

"All 101 automated tests and every script pass; evidence and raw logs are in `evidence/`, and the README runs everything
from a fresh clone."
