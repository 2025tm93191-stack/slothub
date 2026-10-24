#!/bin/sh
# Creates one database and one login per service (database-per-service).
# Runs once, on first start of an empty data volume.
set -eu

create_service_db() {
  db="$1"; user="$2"; password="$3"
  echo "Creating database ${db} owned by ${user}"
  psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname postgres <<-SQL
    CREATE ROLE ${user} LOGIN PASSWORD '${password}';
    CREATE DATABASE ${db} OWNER ${user};
    REVOKE ALL ON DATABASE ${db} FROM PUBLIC;
    GRANT CONNECT, TEMPORARY ON DATABASE ${db} TO ${user};
SQL
}

create_service_db scheduling_db   scheduling_user   "$SCHEDULING_DB_PASSWORD"
create_service_db booking_db      booking_user      "$BOOKING_DB_PASSWORD"
create_service_db payment_db      payment_user      "$PAYMENT_DB_PASSWORD"
create_service_db notification_db notification_user "$NOTIFICATION_DB_PASSWORD"

# Scheduling uses an exclusion constraint on (resource_id, time range) to prevent overlapping slots.
psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname scheduling_db \
  -c "CREATE EXTENSION IF NOT EXISTS btree_gist;"
