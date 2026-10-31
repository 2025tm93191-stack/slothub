# Task 3 — Deployment troubleshooting (situated learning)

## Problem (0.5)

**When:** Assignment 1, Sprint 1 (24 Oct 2026), the first time the team ran the database with `docker compose up`.

**Symptom:** the application worked in development, where `dotnet run` connected to a PostgreSQL already installed on the
laptop. `docker compose up` failed before any service started:

```
Error response from daemon: ports are not available: exposing port TCP 0.0.0.0:5432 -> 127.0.0.1:0:
listen tcp 0.0.0.0:5432: bind: address already in use
```

Changing the port to 5433 in `docker-compose.yml` failed the same way. All five containers stayed in `Created`; nothing was
reachable on 5101–5104.

**Impact:** no part of the containerized system could run on that machine, although the code was correct.

## Diagnosis (1.0)

`scripts/a2-troubleshoot-demo.sh` recreates the situation safely: a throw-away process occupies the database's host port,
then the same commands we used to diagnose it are run. The output is in `run-*.txt`.

| Command | What it showed | Conclusion |
|---|---|---|
| `docker compose up -d --wait` | `ports are not available … bind: address already in use` | Docker cannot publish a host port |
| `docker compose ps -a` | every container `Created`, none `running` | the deployment stopped at container start, not inside an application |
| `docker inspect slothub-postgres --format '{{.State.Status}} … {{json .HostConfig.PortBindings}}'` | `created`, binding `5432/tcp → host 5440` | the failing step is publishing PostgreSQL's port on the host |
| `docker compose logs postgres` | 0 lines | PostgreSQL never ran, so the cause is outside the container |
| `lsof -nP -iTCP:<port> -sTCP:LISTEN` (and `nc -z`, `docker ps` at the time) | a non-Docker process listening on the port. Originally: a locally installed PostgreSQL on 5432, and 5433/5434 also taken by other projects' databases (5434 was another project's `postgres` container) | the host port is already owned by another program |

**Root cause:** the Compose file hard-coded the host side of the port mapping (`5432:5432`). That host port belonged to
the developer's own PostgreSQL. The services do **not** need that port: they reach the database on the Compose network
as `postgres:5432`. The host mapping exists only for developer tools (psql, scripts).

## Solution and validation (1.0)

**Change:** the host port became configuration instead of a hard-coded value, with a default nobody else uses:

```yaml
ports:
  - "${POSTGRES_PORT:-5440}:5432"     # docker-compose.yml
```

`POSTGRES_PORT` is set in `.env` (see `.env.example`). Host-side connection strings (`services/*/.env.example`, used
with `dotnet run`) point to that port; container-side connection strings keep using `postgres:5432`. No code or image
changed.

**Validation** (sections 3–4 of `run-*.txt`): with `POSTGRES_PORT` set to a free port, `docker compose up -d --wait`
succeeds. All 5 containers report `healthy`, a booking is held, paid and `CONFIRMED`, and the database answers on the new
host port. The script then frees the port and returns to the default. The full workflow is also verified by
`../a2-task2-compose/` (`a2-verify-deployment.sh`, `a2-restart-demo.sh`).

## Learning reflection (0.5)

- **Host ports are a shared, scarce resource; container ports are not.** Inside the Compose network every service can use
  its natural port (`5432`, `8080`). Only what is published to the host can collide, so published ports should be few,
  configurable and documented. In production we would not publish the database at all.
- **"Works on my machine" often means "uses something on my machine".** Development silently used a database installed
  on the laptop. Containerizing exposed that hidden dependency, and the fix was to make the environment explicit through configuration.
- **Diagnose from the outside in.** `ps -a` (what state?), `inspect` (what was Docker trying to do?), `logs` (did the
  process even start?), then host tools (`lsof`). Empty logs were the key clue: the problem was not in the application.
- **Platform details matter.** While scripting the reproduction we found that an IPv4-only listener does *not* clash
  with Docker Desktop on macOS, which publishes on `[::]`, but a dual-stack listener (like PostgreSQL) does.
- **How we would apply it:** a pre-flight check for required free ports in deployment scripts; ports from environment
  variables with sensible defaults; health-gated start-up (`depends_on: service_healthy`, readiness probes) so a failing
  dependency is visible immediately; in Kubernetes, ClusterIP Services so internal components never need host ports.

## Other genuine problems we met (not graded; context)

| Problem | Diagnosis | Fix |
|---|---|---|
| `libgssapi_krb5.so.2: cannot open shared object file` logged by every service **only inside containers** | `docker compose logs`: Npgsql tried GSS/Kerberos encryption, and the library is absent from the slim runtime image | `GSS Encryption Mode=Disable` in the container connection strings |
| Database did not come back after Docker Desktop restarted | `docker ps -a`: `slothub-postgres Exited (0)` while services were `Up` | `restart: unless-stopped` on `postgres` |
| Docker daemon hung; builds failed with `input/output error` | `df -h`: 169 MB free on the host; `docker system df` | freed disk, `docker builder prune`; checked free disk before builds |

**Reproduce:** `./scripts/a2-troubleshoot-demo.sh` (about 1 minute; it restores the normal deployment at the end).
