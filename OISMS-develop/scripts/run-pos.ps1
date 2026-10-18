# Run POS Application (PWA)
Write-Host "Starting POS App on http://localhost:5174..." -ForegroundColor Magenta
Set-Location src/frontend/pos-app
npm run dev -- --port 5174
