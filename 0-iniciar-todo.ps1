# 0-iniciar-todo.ps1 - Levanta Keycloak, luego abre backend y frontend en ventanas propias, y pide el token.
# Uso:  .\0-iniciar-todo.ps1
$ErrorActionPreference = 'Stop'
& "$PSScriptRoot\1-keycloak.ps1"
Start-Process powershell -ArgumentList '-NoExit', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSScriptRoot\2-backend.ps1`""
Start-Process powershell -ArgumentList '-NoExit', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSScriptRoot\3-frontend.ps1`""
Write-Host 'Esperando ~15 s a que el API arranque...' -ForegroundColor Cyan
Start-Sleep -Seconds 15
& "$PSScriptRoot\4-token-swagger.ps1"
