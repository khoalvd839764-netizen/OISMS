# Script to build all backend and frontend projects
Write-Host "=== 1. Building Backend (.NET 8 Clean Architecture) ===" -ForegroundColor Cyan
dotnet build src/backend/OISM.sln
if ($LASTEXITCODE -ne 0) {
    Write-Host "Backend build failed!" -ForegroundColor Red
    exit 1
}

Write-Host "`n=== 2. Running Unit & Integration Tests ===" -ForegroundColor Cyan
dotnet test src/backend/OISM.sln --no-build
if ($LASTEXITCODE -ne 0) {
    Write-Host "Tests failed!" -ForegroundColor Red
    exit 1
}

Write-Host "`n=== 3. Building Frontend Admin Dashboard ===" -ForegroundColor Cyan
npm run build --prefix src/frontend/admin-dashboard
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "`n=== 4. Building Frontend POS App (PWA) ===" -ForegroundColor Cyan
npm run build --prefix src/frontend/pos-app
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
npm test --prefix src/frontend/pos-app
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "Building and testing Marketplace Simulator" -ForegroundColor Cyan
npm run build --prefix src/frontend/marketplace-simulator
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
npm test --prefix src/frontend/marketplace-simulator
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "`n=== ALL BUILDS & TESTS PASSED SUCCESSFULLY! ===" -ForegroundColor Green
