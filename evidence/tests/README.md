# Tests

| File | Shows |
|---|---|
| `unit-and-integration-20261031T125803Z.txt` | `dotnet test SlotHub.slnx`: **101 tests pass** — Scheduling 24, Booking 42, Payment 8, Notification 13, BuildingBlocks 5 (unit) + Scheduling 4 and Payment 5 (Testcontainers integration on real PostgreSQL) |
| `run-20261031T125826Z.txt` | `scripts/e2e-booking.sh`, **26 checks**. (1) Happy path across all four services: hold → pay → confirm → SMS + email sent. (2) **Failure:** payment declined → `402`, booking `FAILED`, slot capacity restored, declined payment recorded. (3) **Failure:** payment gateway down → `503` after retries, booking stays `HELD`; the circuit breaker is still open right after recovery (`DOWNSTREAM_CIRCUIT_OPEN`); after 15 s the retry with the same key succeeds with exactly one charge |
| `postman-newman-20261031T125933Z.txt` | The Postman collection ([postman/](../../postman)) run with newman: **38 requests, 71 assertions, 0 failures**, including validation errors, slot overlap, slot full, key mismatch, missing key, declined payment, invalid transition and over-refund |

Unit tests are named after the business rule they prove (`BR1_…` to `BR6_…`). The mapping to code is in the traceability
table at the end of [docs/product-backlog.md](../../docs/product-backlog.md#business-rule-traceability).

**Reproduce.** `dotnet test SlotHub.slnx` (Docker required for the integration tests), `./scripts/e2e-booking.sh`,
`npx newman run postman/SlotHub.postman_collection.json -e postman/SlotHub.local.postman_environment.json`.
