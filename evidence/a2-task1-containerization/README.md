# Task 1 — Containerize the application

Produced by `./scripts/a2-build-evidence.sh --no-cache`.

| File | Shows |
|---|---|
| `run-*.txt` | 22 checks. (1) One image built per service from its own Dockerfile (`docker compose build --no-cache`, every build stage listed). (2) `docker images`: `slothub-scheduling`, `slothub-booking`, `slothub-payment`, `slothub-notification` at `1.0.0`, with OCI version/revision labels, non-root user `1654`, built-in health check. (3) `docker compose up -d --wait`: containers created and started, all 5 `healthy`. (4) No secrets inside the images: no password in image env vars, none in `docker history`, no `.env` file in the filesystem; passwords arrive only at run time (shown masked). (5) No secrets in the repository: `.env` is git-ignored and untracked, `.env.example` files hold `CHANGE_ME` placeholders, `docker-compose.yml` has no fallback passwords, `.dockerignore` excludes `.env` |
| `run-*-docker-build.log` | Raw `docker compose --progress plain build --no-cache` output |

**What to look at in the code**

- `services/<name>/Dockerfile`: two stages. The SDK stage restores (cached layer) and publishes; the runtime stage holds only the ASP.NET runtime and the published app. It runs as non-root, exposes 8080 and has a `HEALTHCHECK`.
- `infra/docker/healthcheck.sh`: HTTP check of `/health/ready` over bash's `/dev/tcp` (the runtime image has no curl). Healthy = service up **and** its database reachable.
- `docker-compose.yml`: every URL, connection string and setting comes from environment variables. Passwords use `${VAR:?…}`, so Compose refuses to start without `.env`.
- PostgreSQL runs as a container (`postgres:17-alpine`) with one database and one login per service.

**Note on history.** Before Assignment 2, `docker-compose.yml` contained development-only fallback passwords
(`scheduling_pw`, …) in its first commits. They were placeholders for a local container, never used anywhere else. They have
been removed, and fresh values belong in each team member's own `.env`.
