#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "$0")" && pwd)"
BACKEND="$ROOT/ColdHarbourBackend"
FRONTEND="$ROOT/ColdHarbourFrontend"
API_PROJECT="$BACKEND/src/ColdHarbour.Api"

# ── Load .env (POSTGRES_USER / POSTGRES_PASSWORD / POSTGRES_DB) ──────────────
if [[ -f "$ROOT/.env" ]]; then
  set -a; source "$ROOT/.env"; set +a
else
  echo "⚠  No .env found — copy .env.example and fill in values" >&2
  exit 1
fi

DB_CONN="Host=localhost;Port=5432;Database=${POSTGRES_DB};Username=${POSTGRES_USER};Password=${POSTGRES_PASSWORD}"

# ── Cleanup: kill all children on Ctrl-C ────────────────────────────────────
PIDS=()
cleanup() {
  echo ""
  echo "→ Stopping..."
  for pid in "${PIDS[@]}"; do
    kill "$pid" 2>/dev/null || true
  done
  docker compose stop db 2>/dev/null || true
  exit 0
}
trap cleanup INT TERM

# ── 1. Start Postgres ─────────────────────────────────────────────────────────
echo "→ Starting Postgres..."
docker compose up -d db

echo -n "→ Waiting for Postgres"
until docker compose exec -T db pg_isready -U "$POSTGRES_USER" -d "$POSTGRES_DB" -q 2>/dev/null; do
  echo -n "."
  sleep 1
done
echo " ready"

# ── 2. Run EF migrations ──────────────────────────────────────────────────────
echo "→ Applying migrations..."
ConnectionStrings__DefaultConnection="$DB_CONN" \
  dotnet ef database update \
    --project "$BACKEND/src/ColdHarbour.Infrastructure" \
    --startup-project "$API_PROJECT" \
    --no-build 2>/dev/null \
  || (
    echo "→ Building first (migrations need a compiled project)..."
    dotnet build "$BACKEND/ColdHarbour.slnx" -c Debug --nologo -v quiet
    ConnectionStrings__DefaultConnection="$DB_CONN" \
      dotnet ef database update \
        --project "$BACKEND/src/ColdHarbour.Infrastructure" \
        --startup-project "$API_PROJECT"
  )

# ── 3. Start API in watch mode ────────────────────────────────────────────────
echo "→ Starting API (dotnet watch) on http://localhost:8080 ..."
ASPNETCORE_ENVIRONMENT=Development \
  ASPNETCORE_URLS=http://localhost:8080 \
  ConnectionStrings__DefaultConnection="$DB_CONN" \
  COLDHARBOUR_CONTENT_ROOT="$ROOT/content" \
  dotnet watch run \
    --project "$API_PROJECT" \
    --no-hot-reload \
  &
PIDS+=($!)

# ── 4. Start Angular in watch mode ────────────────────────────────────────────
echo "→ Starting Angular (ng serve) on http://localhost:4200 ..."
(cd "$FRONTEND" && npx ng serve --open) &
PIDS+=($!)

echo ""
echo "  API  →  http://localhost:8080"
echo "  UI   →  http://localhost:4200"
echo ""
echo "  Ctrl-C to stop everything"
echo ""

# ── Wait for any child to exit (signals a crash) ─────────────────────────────
wait -n "${PIDS[@]}" 2>/dev/null || true
cleanup
