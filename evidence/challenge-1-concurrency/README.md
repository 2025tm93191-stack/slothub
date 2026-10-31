# Challenge 1 — Concurrency control (BR1)

**Claim.** Simultaneous hold requests for the same slot never take more than its capacity. Exactly *N* succeed, the rest get
`409 SLOT_UNAVAILABLE`, and the database agrees.

**Mechanism.** One atomic conditional update in `HoldRepository.ReserveAsync`
([Repositories.cs](../../services/scheduling/src/Scheduling.Infrastructure/Persistence/Repositories.cs)):
`UPDATE slots SET reserved_count = reserved_count + 1 WHERE id = @slot AND reserved_count < capacity AND start_at > now()`.
The hold is inserted in the same transaction. A `CHECK (reserved_count <= capacity)` constraint is a second line of defence.

| File | Shows |
|---|---|
| `run-20261031T125849Z-capacity1.txt` | 20 requests released at the same instant (FIFO barrier) on a slot with capacity 1 → **1 × 201, 19 × 409 `SLOT_UNAVAILABLE`**; `slots.reserved_count = 1`, one `HELD` row in `holds` |
| `run-20261031T125850Z-capacity5.txt` | 30 simultaneous requests on capacity 5 → **5 × 201, 25 × 409**; `reserved_count = 5`, five `HELD` rows |
| `run-20261031T125853Z-capacity1-naive.txt` | **Counter-example**: the same load against a Scheduling instance started with `CONCURRENCY_MODE=naive` (read → check in C# → write) → **20 holds created for capacity 1** while `reserved_count` says 1 (lost updates) |

Each file contains the per-request table (HTTP status, latency, hold ID or error code), the API view of the slot and the
`psql` output of `slots` and `holds` from `scheduling_db`.

**Why check-then-update fails** (the naive file): every request reads `reserved_count = 0` before any of them writes, so
every request passes the `reserved < capacity` test in application code, and each then writes `0 + 1`. The increments
overwrite each other and twenty holds exist for one seat. The check and the write must be one atomic step in the database.

**Also covered by** `Scheduling.IntegrationTests/HoldConcurrencyTests.BR1_ParallelHolds_NeverExceedCapacity` (20 and 50
parallel reservations against a real PostgreSQL).

**Reproduce.** `./scripts/challenge1-concurrency.sh --capacity 1 --requests 20`, `--capacity 5 --requests 30`,
`./scripts/challenge1-naive-demo.sh`.

**Limitations.** A single PostgreSQL row is the serialisation point, so a single extremely hot slot is limited by row-lock
throughput. That is acceptable for appointment slots; numbered seats would need one row per seat.
