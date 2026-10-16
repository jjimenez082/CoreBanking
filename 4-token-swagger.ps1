# 4-token-swagger.ps1 - Pide un token a Keycloak, lo copia al portapapeles y abre Swagger.
# Uso:  .\4-token-swagger.ps1
#       .\4-token-swagger.ps1 -Usuario otro.usuario
# En Swagger: Authorize > Logout (si hay token viejo) > pegar (Ctrl+V) en Value > Authorize.
# El token dura ~5 minutos; si Swagger responde 401, vuelva a ejecutar este script.
param(
    [string]$Usuario = 'qa.arquitecto',
    [string]$Cliente = 'swagger-dev',
    [string]$Realm   = 'dev',
    [string]$Clave,
    [switch]$SinNavegador
)
$ErrorActionPreference = 'Stop'

if (-not $Clave) {
    $sec  = Read-Host "Clave de $Usuario" -AsSecureString
    $bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($sec)
    try { $Clave = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr) }
    finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr) }
}

$uri = "http://localhost:8080/realms/$Realm/protocol/openid-connect/token"
try {
    $t = (Invoke-RestMethod -Method Post -Uri $uri -Body @{
        grant_type = 'password'; client_id = $Cliente; username = $Usuario; password = $Clave
    }).access_token
} catch {
    Write-Host 'No se pudo obtener el token. Verifique que Keycloak este arriba (1-keycloak.ps1) y la clave.' -ForegroundColor Red
    throw
}

$p = $t.Split('.')[1].Replace('-', '+').Replace('_', '/')
while ($p.Length % 4) { $p += '=' }
$claims = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($p)) | ConvertFrom-Json
$claims | Select-Object preferred_username, compania, aud | Format-List

if (-not $claims.compania) { Write-Host 'ATENCION: el token NO trae el claim "compania" (el API respondera token_sin_compania).' -ForegroundColor Red }

$t | Set-Clipboard
Write-Host 'Token copiado al portapapeles.' -ForegroundColor Green
if (-not $SinNavegador) { Start-Process 'http://localhost:5003/swagger' }
