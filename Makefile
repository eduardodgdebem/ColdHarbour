# ──────────────────────────────────────────────────────────────────────────────
# ColdHarbour – local dev helpers
# ──────────────────────────────────────────────────────────────────────────────
ROOT          := $(shell pwd)
BACKEND       := $(ROOT)/ColdHarbourBackend
FRONTEND      := $(ROOT)/ColdHarbourFrontend
API_PROJECT   := $(BACKEND)/src/ColdHarbour.Api
INFRA_PROJECT := $(BACKEND)/src/ColdHarbour.Infrastructure
LOCAL_IP      := $(shell ipconfig getifaddr en0 2>/dev/null || ipconfig getifaddr en1 2>/dev/null || hostname -I 2>/dev/null | awk '{print $$1}')

# Load .env for POSTGRES_* vars (silently skipped if absent)
-include .env
export

DB_CONN = Host=localhost;Port=5432;Database=$(POSTGRES_DB);Username=$(POSTGRES_USER);Password=$(POSTGRES_PASSWORD)

.PHONY: db-start db-stop db-migrate db-reset build test dev logs help

## Start the Postgres service via Docker Compose
db-start:
	@echo "▶ Starting Postgres (docker compose)..."
	docker compose up -d db
	@echo "⏳ Waiting for Postgres to be ready..."
	@until docker compose exec -T db pg_isready -U $(POSTGRES_USER) -d $(POSTGRES_DB) -q 2>/dev/null; do \
		printf "."; sleep 1; \
	done
	@echo ""
	@echo "✅ Postgres is ready"

## Stop the Postgres service (data is preserved)
db-stop:
	docker compose stop db

## Run all pending EF Core migrations
db-migrate: db-start
	@echo "▶ Applying EF Core migrations..."
	@ConnectionStrings__DefaultConnection="$(DB_CONN)" \
	  dotnet ef database update \
	    --project "$(INFRA_PROJECT)" \
	    --startup-project "$(API_PROJECT)" \
	    --no-build 2>/dev/null \
	  || ( \
	    echo "→ Building first (migrations need a compiled project)..."; \
	    dotnet build "$(BACKEND)/ColdHarbour.slnx" -c Debug --nologo -v quiet; \
	    ConnectionStrings__DefaultConnection="$(DB_CONN)" \
	      dotnet ef database update \
	        --project "$(INFRA_PROJECT)" \
	        --startup-project "$(API_PROJECT)"; \
	  )

## Nuke the DB service and re-run migrations from scratch (destructive!)
db-reset:
	docker compose rm -sf db
	$(MAKE) db-migrate

## Build the backend solution
build:
	dotnet build "$(BACKEND)/ColdHarbour.slnx" -c Debug --nologo

## Run all backend tests
test:
	dotnet test "$(BACKEND)/ColdHarbour.slnx" --nologo -v minimal

## Launch backend (dotnet watch) + frontend (ng serve) concurrently
dev: db-migrate
	@if [ ! -f "$(ROOT)/.env" ]; then \
	    echo "⚠  No .env found — copy .env.example and fill in values" >&2; exit 1; \
	fi
	@lsof -ti:8080 | xargs kill -9 2>/dev/null || true
	@lsof -ti:4200 | xargs kill -9 2>/dev/null || true
	@echo ""
	@echo "  API  → http://localhost:8080"
	@echo "  UI   → http://localhost:4200"
	@echo "  UI   → http://$(LOCAL_IP):4200  (LAN)"
	@echo "  Ctrl-C stops both processes"
	@echo ""
	@trap 'kill 0' INT TERM EXIT; \
	  ASPNETCORE_ENVIRONMENT=Development \
	  ASPNETCORE_URLS=http://localhost:8080 \
	  ConnectionStrings__DefaultConnection="$(DB_CONN)" \
	  COLDHARBOUR_CONTENT_ROOT="$(ROOT)/content" \
	  dotnet watch run --project "$(API_PROJECT)" --no-hot-reload & \
	  cd "$(FRONTEND)" && npx ng serve \
	    --host 0.0.0.0 \
	    --proxy-config proxy.conf.json & \
	  wait

## Stream Postgres container logs
logs:
	docker compose logs -f db

help:
	@echo ""
	@echo "  make db-start    start Postgres via Docker Compose (creates volume if absent)"
	@echo "  make db-stop     stop Postgres (data is kept)"
	@echo "  make db-migrate  run db-start then EF Core migrations"
	@echo "  make db-reset    destroy the DB service and re-migrate from scratch"
	@echo "  make build       build the backend solution"
	@echo "  make test        run all backend tests"
	@echo "  make dev         start Postgres + API (dotnet watch) + UI (ng serve) concurrently"
	@echo "  make logs        stream Postgres container logs"
	@echo ""
