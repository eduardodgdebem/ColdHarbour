#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "$0")" && pwd)"
BACKEND="$ROOT/ColdHarbourBackend"
FRONTEND="$ROOT/ColdHarbourFrontend"
API_PROJECT="$BACKEND/src/ColdHarbour.Api"
SESSION="coldharbour"

# ── Require tmux ─────────────────────────────────────────────────────────────
if ! command -v tmux &>/dev/null; then
  echo "⚠  tmux is not installed. Install it with: brew install tmux" >&2
  exit 1
fi

# ── Load .env (POSTGRES_USER / POSTGRES_PASSWORD / POSTGRES_DB) ──────────────
if [[ -f "$ROOT/.env" ]]; then
  set -a; source "$ROOT/.env"; set +a
else
  echo "⚠  No .env found — copy .env.example and fill in values" >&2
  exit 1
fi

DB_CONN="Host=localhost;Port=5432;Database=${POSTGRES_DB};Username=${POSTGRES_USER};Password=${POSTGRES_PASSWORD}"

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

# ── 3. Launch tmux session ────────────────────────────────────────────────────
tmux kill-session -t "$SESSION" 2>/dev/null || true

# Window: db (live postgres logs)
tmux new-session -d -s "$SESSION" -n "db" -x 220 -y 50
tmux send-keys -t "$SESSION:db" "docker compose logs -f db" Enter

# Window: api
tmux new-window -t "$SESSION" -n "api"
tmux send-keys -t "$SESSION:api" \
  "ASPNETCORE_ENVIRONMENT=Development \
   ASPNETCORE_URLS=http://localhost:8080 \
   ConnectionStrings__DefaultConnection=\"$DB_CONN\" \
   COLDHARBOUR_CONTENT_ROOT=\"$ROOT/content\" \
   dotnet watch run --project \"$API_PROJECT\" --no-hot-reload" \
  Enter

# Window: ui
tmux new-window -t "$SESSION" -n "ui"
tmux send-keys -t "$SESSION:ui" "cd \"$FRONTEND\" && npx ng serve" Enter

# Focus api window on attach
tmux select-window -t "$SESSION:api"

echo ""
echo "  tmux session : $SESSION"
echo "  windows      : db | api | ui   (Ctrl-B + 0/1/2 to switch)"
echo "  API          : http://localhost:8080"
echo "  UI           : http://localhost:4200"
echo "  stop all     : tmux kill-session -t $SESSION"
echo ""

tmux attach-session -t "$SESSION"
