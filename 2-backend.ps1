# 2-backend.ps1 - Levanta el API (ASP.NET Core 8) en modo Development con Swagger.
# Uso:  .\2-backend.ps1
# Queda corriendo en esta ventana; Ctrl+C para detener.
$ErrorActionPreference = 'Stop'
$proj = Join-Path $PSScriptRoot 'backend\CoreBancario.Api'
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw 'No se encontro dotnet (SDK 8).' }

$env:ASPNETCORE_ENVIRONMENT = 'Development'
Write-Host 'API:     http://localhost:5003' -ForegroundColor Cyan
Write-Host 'Swagger: http://localhost:5003/swagger' -ForegroundColor Cyan
Push-Location $proj
try { dotnet run --launch-profile http } finally { Pop-Location }
