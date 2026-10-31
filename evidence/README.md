# Evidence

Every file here was produced by a script in [`scripts/`](../scripts) against the running Docker stack
(`docker compose up -d --build --wait`). All of it can be regenerated in about four minutes:

```bash
./scripts/run-all-checks.sh      # tests, e2e, all challenge scripts, Postman, logs
```

Each script output starts with the run ID, UTC start time and the exact command, and ends with PASS/FAIL per check.
Requests carry `X-Correlation-Id: <run-id>-<n>`, so any request in a script output can be found in the raw logs.

| Folder | Claim it supports | Latest result |
|---|---|---|
| [challenge-1-concurrency/](challenge-1-concurrency/README.md) | Concurrent holds never over-allocate a slot; a naive check-then-update does | PASS (1 of 20, 5 of 30); naive: 20 holds on capacity 1 |
| [challenge-2-idempotency/](challenge-2-idempotency/README.md) | Repeated or simultaneous unsafe requests have exactly one effect | 20/20 checks PASS (two runs) |
| [challenge-3b-holds/](challenge-3b-holds/README.md) | Holds expire automatically, safely, never after payment; late payment refunded | 18/18 checks PASS (latest); one earlier failed run kept, see its README |
| [tests/](tests/README.md) | Unit, integration, end-to-end and Postman suites, including failure paths | 101 tests, 26 e2e checks, 71 Postman assertions — all PASS |
| [logs/](logs/README.md) | Raw structured logs (JSON lines) with correlation IDs for all of the above | — |

**Screenshots** (command + output + timestamp) are in each folder's `screenshots/` subfolder, next to the matching text file.
