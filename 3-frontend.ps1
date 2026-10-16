# 3-frontend.ps1 - Levanta el Backoffice (React + Vite) en http://localhost:3000
# Uso:  .\3-frontend.ps1
# Instala dependencias solo si falta node_modules. Ctrl+C para detener.
$ErrorActionPreference = 'Stop'
$dir = Join-Path $PSScriptRoot 'frontend\core-bancario-backoffice'
if (-not (Get-Command npm -ErrorAction SilentlyContinue)) { throw 'No se encontro npm (Node.js).' }

Push-Location $dir
try {
    if (-not (Test-Path 'node_modules')) { Write-Host 'Instalando dependencias...' -ForegroundColor Cyan; npm install }
    Write-Host 'Backoffice: http://localhost:3000' -ForegroundColor Cyan
    npm run dev
} finally { Pop-Location }
