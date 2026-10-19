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
Set-Location src/frontend/admin-dashboard
npm run build
Set-Location ../../..

Write-Host "`n=== 4. Building Frontend POS App (PWA) ===" -ForegroundColor Cyan
Set-Location src/frontend/pos-app
npm run build
Set-Location ../../..

Write-Host "`n=== ALL BUILDS & TESTS PASSED SUCCESSFULLY! ===" -ForegroundColor Green
