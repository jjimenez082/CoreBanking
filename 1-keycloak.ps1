# 1-keycloak.ps1 - Levanta Keycloak de desarrollo (Docker) y espera a que el realm "dev" responda.
# Uso:  .\1-keycloak.ps1            (levantar)
#       .\1-keycloak.ps1 -Detener   (apagar)
param([switch]$Detener)
$ErrorActionPreference = 'Stop'
$dir = Join-Path $PSScriptRoot 'infraestructure\keycloak-dev'

if (-not (Get-Command docker -ErrorAction SilentlyContinue)) { throw 'Docker no esta instalado o no esta en el PATH.' }
docker info *> $null
if ($LASTEXITCODE -ne 0) { throw 'Docker Desktop no esta corriendo. Abralo y vuelva a ejecutar.' }

Push-Location $dir
try {
    if ($Detener) { docker compose down; Write-Host 'Keycloak detenido.' -ForegroundColor Yellow; return }
    docker compose up -d
    if ($LASTEXITCODE -ne 0) { throw 'docker compose fallo.' }
} finally { Pop-Location }

Write-Host 'Esperando a Keycloak (puede tardar ~30-60 s la primera vez)...' -ForegroundColor Cyan
$ok = $false
for ($i = 0; $i -lt 60; $i++) {
    try {
        Invoke-RestMethod 'http://localhost:8080/realms/dev/.well-known/openid-configuration' -TimeoutSec 3 | Out-Null
        $ok = $true; break
    } catch { Start-Sleep -Seconds 3 }
}
if ($ok) {
    Write-Host 'Keycloak listo.' -ForegroundColor Green
    Write-Host '  Consola admin: http://localhost:8080  (admin / admin)'
    Write-Host '  Realm dev:     http://localhost:8080/realms/dev'
} else {
    Write-Host 'Keycloak no respondio a tiempo. Revise: docker logs keycloak-corebancario-dev' -ForegroundColor Red
}
