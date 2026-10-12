# Start all services with Docker Compose
Write-Host "Starting Docker Compose (PostgreSQL, Redis, PgAdmin, Backend, Admin, POS)..." -ForegroundColor Green
docker compose up -d
docker compose ps
