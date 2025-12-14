#!/bin/sh
set -e

DB_HOST="${POSTGRES_HOST:-postgres}"
DB_PORT="${POSTGRES_PORT:-5432}"
DB_USER="${POSTGRES_USER:-postgres}"

echo "Waiting for Postgres at ${DB_HOST}:${DB_PORT}..."
until pg_isready -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" >/dev/null 2>&1; do
  echo "Postgres is not ready - sleeping 2s"
  sleep 2
done
echo "Postgres is ready."

# run migrations using the app's --migrate mode (Program.cs must handle --migrate)
echo "Applying EF Core migrations..."
dotnet OrderService.dll --migrate

# start the app normally
echo "Starting OrderService..."
dotnet OrderService.dll