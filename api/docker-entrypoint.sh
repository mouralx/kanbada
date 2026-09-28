#!/bin/sh
set -eu

if [ -z "${ConnectionStrings__Postgres:-}" ]; then
    : "${POSTGRES_DB:?POSTGRES_DB is required}"
    : "${POSTGRES_USER:?POSTGRES_USER is required}"
    : "${POSTGRES_PASSWORD:?POSTGRES_PASSWORD is required}"
    export ConnectionStrings__Postgres="Host=postgres;Port=5432;Database=${POSTGRES_DB};Username=${POSTGRES_USER};Password=${POSTGRES_PASSWORD}"
fi

exec "$@"
