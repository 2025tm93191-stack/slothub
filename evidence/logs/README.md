# Raw logs

`<service>-<timestamp>.log` are the services' structured logs (Serilog compact JSON, one event per line), captured with
`./scripts/collect-logs.sh --since 15m` right after the evidence runs.

Useful fields: `@t` (UTC time), `@m` (message), `@l` (level, omitted for Information), `Service`, `CorrelationId`.
Every request in a script output can be traced across services by its correlation ID, for example:

```bash
grep '"c2-20261031T125855Z-' evidence/logs/*.log | head          # one Challenge 2 run, all services
jq -r 'select(.["@m"] | test("Idempotency replay")) | "\(.["@t"]) \(.CorrelationId) \(.["@m"])"' evidence/logs/booking-*.log
```
