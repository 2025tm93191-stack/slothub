# Challenge 3B — Time-bound holds (BR2, BR3)

**Claim.** Held slots are released automatically after `HOLD_TTL_SECONDS` (30 s). Cancellation releases them too. The
expiry job is safe to run twice and never releases a paid slot.

**Mechanism.** [`HoldRepository.ExpireDueAsync`](../../services/scheduling/src/Scheduling.Infrastructure/Persistence/Repositories.cs)
selects only `status = 'HELD' AND expires_at <= now` with `FOR UPDATE SKIP LOCKED`, applies the domain rule
`Hold.Expire()` and returns capacity, all in one transaction. It is run every 5 s by `HoldExpiryWorker` and on demand by
`POST /api/v1/admin/holds/expire-now`. Confirm uses a row lock plus `expires_at > now`, so confirm and expiry can never both win.
Booking's reconciler asks Scheduling for the hold before marking a booking EXPIRED.

| File | Result |
|---|---|
| `run-20261031T125859Z.txt` | **18/18 PASS** (latest) |
| `run-20261025T161347Z.txt` | 18/18 PASS, with screenshots in `screenshots/` |
| `run-20261025T160701Z.txt` | **13 passed, 5 failed — kept on purpose.** The late-payment case (section E) used a fixed delay, so on that machine the payment completed *before* the hold expired and the booking was confirmed instead of refunded. The script was then changed to start paying 6 s before the hold's own `holdExpiresAt`, with a 9 s gateway delay, which makes the case deterministic (later runs pass) |

All four bookings are created at the same time, each on its own capacity-1 slot:

| Section | Shows |
|---|---|
| D — cancel | Booking `CANCELLED`, hold `RELEASED`, `reserved` 1 → **0 immediately** |
| E — late payment (BR3 compensation) | Payment starts 6 s before expiry; gateway latency 9 s. The first attempt times out, retries poll `409 REQUEST_IN_PROGRESS`, then the stored charge is replayed. Confirm fails `HOLD_EXPIRED`, so it is **refunded automatically**. Booking `EXPIRED`, `refundedMinor = 50000`, payment `REFUNDED` (one charge) |
| A — auto-expiry | Unpaid hold → `EXPIRED` by the worker; `reserved` back to 0; booking reconciled to `EXPIRED`; `BOOKING_EXPIRED` notification queued |
| B — run twice | Two manual sweeps after the worker: second run expires **0**; `reserved` still 0 (no double release) |
| C — paid hold | Confirmed hold still `CONFIRMED`, `reserved` still 1, booking `CONFIRMED` after the TTL and sweeps |
| DB + logs | `holds` + `slots` rows (scheduling_db), `bookings` rows (booking_db); worker, reconciler, retry and refund log lines with correlation IDs |

**Also covered by** `Scheduling.IntegrationTests/HoldConcurrencyTests.BR2_ExpirySweep_*` (run twice, confirmed untouched)
and `BR2_ConcurrentSweepers_ReleaseEachHoldExactlyOnce` (4 sweepers at once release each of 10 holds exactly once), plus
`Scheduling.UnitTests/HoldTests.BR2_*`.

**Reproduce.** `./scripts/challenge3b-holds.sh --ttl 30` (takes about 45 s).

**Limitations.** Expiry granularity is the sweep interval (5 s), so a hold may live up to TTL + 5 s; confirmation itself
enforces the exact TTL. If the refund after a late payment fails, it is only logged and needs manual follow-up.
