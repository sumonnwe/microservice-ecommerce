#!/bin/sh
set -e

DB_HOST="${POSTGRES_HOST:-postgres}"
DB_PORT="${POSTGRES_PORT:-5432}"
DB_USER="${POSTGRES_USER:-postgres}"
APP_DLL="UserService.dll"

echo "Waiting for Postgres at ${DB_HOST}:${DB_PORT}..."
until pg_isready -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" >/dev/null 2>&1; do
  echo "Postgres is not ready - sleeping 2s"
  sleep 2
done
echo "Postgres ready."

echo "Running migrations (migrate-only)..."
dotnet "$APP_DLL" --migrate

echo "Starting application..."
exec dotnet "$APP_DLL"