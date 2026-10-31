# Challenge 2 — Idempotency (BR4)

**Claim.** Repeating an unsafe request with the same `Idempotency-Key` returns the same result with no duplicate effect.
The same key with a different payload is rejected. This holds for identical requests arriving at the same moment, not
only for sequential repeats.

**Mechanism.** [`IdempotencyMiddleware`](../../shared/BuildingBlocks/Idempotency/IdempotencyMiddleware.cs), used by Booking and Payment:
1. It takes a SHA-256 fingerprint of method + path + canonical JSON body.
2. It runs `INSERT … ON CONFLICT DO NOTHING` into the service's own `idempotency_keys` table. The primary key `(key, scope)` is the lock.
3. The response (2xx/4xx) is stored and replayed with `Idempotency-Replayed: true`. A different fingerprint gets `422`. A request still in progress gets `409` + `Retry-After`. 5xx responses release the key so the client may retry.

Booking calls Payment with derived keys `pay-{bookingId}` and `refund-{bookingId}`, so Booking's own retries can never
charge twice.

Files: `run-20261025T074439Z.txt` and `run-20261031T125855Z.txt` (20/20 checks each), screenshots in `screenshots/`.

| Section | Shows |
|---|---|
| A | Same key + same body sent 3 times → all `201`; 2nd and 3rd carry `Idempotency-Replayed: true`; bodies identical (same SHA-256); **1 row** in `booking_db.bookings` |
| B | Same key, different customer → `422 IDEMPOTENCY_KEY_MISMATCH`; no extra booking |
| C | No key → `400 IDEMPOTENCY_KEY_REQUIRED` |
| D | **10 identical `POST /bookings` released at the same instant** → 1 × `201`, 9 × `409 REQUEST_IN_PROGRESS`; **1 booking row, 1 hold** in `scheduling_db` |
| E | **10 identical `POST /bookings/{id}/payment` at the same instant** → booking `CONFIRMED`; **1 row in `payment_db.payments`**, 50000 charged once; a late repeat is replayed; paying again with a *new* key still charges nothing |
| F | `psql` of `idempotency_keys` (both databases), `payments`, `bookings`; log lines showing "Idempotency replay", "still being processed", "different payload" with correlation IDs |

**Also covered by** `Payment.IntegrationTests/IdempotencyTests` (real API + PostgreSQL): replay, 422, 400, 20 simultaneous
identical charges → one payment, 5xx releases the key. `BuildingBlocks.UnitTests/RequestFingerprintTests` covers the fingerprint.

**Reproduce.** `./scripts/challenge2-idempotency.sh`

**Limitations.** Keys are scoped per endpoint path, not per client (there is no authentication). Records are kept for
`IDEMPOTENCY_TTL_HOURS` (24 h) and then deleted by a cleanup job, so the same key can be reused after that.
