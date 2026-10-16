# CoreBancario.Api — API mínima (.NET 8)

Primer backend real del Core Bancario. Valida JWT emitidos por Keycloak
(realm `dev`), resuelve el tenant vía la BD Directorio (`CoreBancarioDir`) y
expone los primeros endpoints de negocio contra `CoreBancario_DEV`.

## Qué incluye

- **Autenticación JWT Bearer** contra el JWKS del realm `dev`
  (`http://localhost:8080/realms/dev`). El core no depende de Keycloak en
  particular — cualquier IdP compatible OAuth2/OIDC sirve (SEG-009/ARQ-016).
- **`TenantResolutionMiddleware`**: lee el claim `compania` del token, lo
  busca en `dir.CLIENTE_SAAS` (BD Directorio) y arma la cadena de conexión
  hacia la base de datos del tenant correspondiente.
- **`ActorResolutionMiddleware`**: resuelve quién llama dentro de ese
  tenant — por `sub` contra `sec.USUARIO` (login interactivo) o por
  `azp`/`client_id` contra `sec.USUARIO_INTEGRACION` (Client Credentials).
- **Endpoints**:
  - `GET /health` — público, sin autenticación.
  - `POST /api/interlocutores` — alta de Persona Física (`cli.INTERLOCUTOR` +
    `cli.INTERLOCUTOR_FISICA`).
  - `GET /api/interlocutores/{id}` — consulta.
  - `GET /api/gestiones/pendientes` — bandeja de `apr.GESTION` en estado
    "Pendiente de Aprobación" para la compañía del actor autenticado.

## Cómo correrlo (en tu máquina, con internet normal)

Este proyecto se escribió en un sandbox cuya política de red bloquea
`api.nuget.org` (confirmado con `NU1301` / 403 al intentar `dotnet build`
aquí), así que el `restore` no se pudo verificar en este entorno. El código
es correcto y compila con un `dotnet restore` normal:

```bash
cd backend/CoreBancario.Api
dotnet restore
dotnet run
```

Por defecto corre en `http://localhost:5000` (o el puerto que asigne
`dotnet run`). Antes de levantarlo:

1. Keycloak debe estar corriendo (`infra/keycloak-dev`, `docker compose up -d`).
2. SQL Server debe tener `CoreBancarioDir` y `CoreBancario_DEV` ya creadas
   (scripts en `database/00_directorio` y `database/01_tenant`).
3. Revisa `appsettings.Development.json` / `appsettings.json` si tu SQL
   Server no usa autenticación de Windows (`Trusted_Connection=True`) o corre
   en otro host/puerto.

## Dos pendientes conocidos antes de probar endpoints de negocio

1. **Atributo `compania` en la cuenta de servicio de Keycloak.** El cliente
   `core-bancario-backend` (Client Credentials) tiene `serviceAccountsEnabled`,
   pero su usuario de cuenta de servicio (`service-account-core-bancario-backend`)
   no trae el atributo `compania` seteado — sin eso, un token de Client
   Credentials no traerá el claim y `TenantResolutionMiddleware` lo rechazará
   con `token_sin_compania`. Para probar esa vía: Admin Console → Users →
   `service-account-core-bancario-backend` → Attributes → agregar
   `compania = DEV` → Save. Para probar sin tocar eso, usa el flujo
   interactivo del usuario `qa.arquitecto` (ya enlazado a `sec.USUARIO`).

2. **`gen.LISTA_VALORES_ITEM` sin seed data.** `POST /api/interlocutores`
   pide `idTipoIdentificacion` e `idPaisRegulador` como IDs reales (son FK
   directas). `gen.PAIS_REGULADOR` ya tiene la fila de Costa Rica (ver
   `enlazar_keycloak_dev.sql`), pero `gen.LISTA_VALORES_ITEM` todavía está
   vacía en `CoreBancario_DEV` — hay que sembrar ahí el tipo/categoría
   `TIPO_IDENTIFICACION_FISICA` (por ejemplo "Cédula Física") antes de poder
   dar de alta un interlocutor real. Puedo generar ese script de seed en el
   siguiente paso si quieres probar el endpoint de una vez.

## Ejemplo de request (una vez resueltos los dos puntos anteriores)

```http
POST /api/interlocutores
Authorization: Bearer <token de qa.arquitecto>
Content-Type: application/json

{
  "idTipoIdentificacion": 1,
  "numeroIdentificacion": "1-2345-6789",
  "idPaisRegulador": 1,
  "nombre1": "Juan",
  "nombre2": null,
  "apellido1": "Pérez",
  "apellido2": "Gómez",
  "fechaNacimiento": "1990-05-14"
}
```

## Comandos de PowerShell para probar (ya verificados)

**1. Obtener un token (login interactivo de `qa.arquitecto`)**

Requiere tener **"Direct access grants"** habilitado en el cliente
`core-bancario-frontend` (Keycloak Admin Console → Clients → ese cliente →
Capability config) y que la contraseña de `qa.arquitecto` no tenga el flag
`temporary` activo (Admin Console → Users → qa.arquitecto → Credentials).

```powershell
$body = @{
  grant_type = "password"
  client_id  = "core-bancario-frontend"
  username   = "qa.arquitecto"
  password   = "Password123!"
}

$response = Invoke-RestMethod -Method Post `
  -Uri "http://localhost:8080/realms/dev/protocol/openid-connect/token" `
  -Body $body

$token = $response.access_token
```

El token dura pocos minutos — si un endpoint empieza a devolver `401`, es
que expiró; vuelve a correr este bloque.

**2. `GET /health` (público, sin token)**

```powershell
Invoke-RestMethod -Uri "http://localhost:5003/health"
```

**3. `GET /api/gestiones/pendientes`**

Para peticiones GET con solo headers, `curl.exe -i` es la forma más rápida
de ver el status code y el cuerpo completo:

```powershell
curl.exe -i http://localhost:5003/api/gestiones/pendientes `
  -H "Authorization: Bearer $token"
```

**4. `POST /api/interlocutores` (alta de Persona Física)**

Para POST con cuerpo JSON, usar `Invoke-RestMethod` — **no** `curl.exe -d`,
porque PowerShell corrompe las comillas del JSON al invocar un ejecutable
nativo (da `JsonException: 'f' is an invalid start of a property name`).

```powershell
$body2 = @{
    idTipoIdentificacion = 1   # id real de gen.LISTA_VALORES_ITEM (ej. CEDULA_FISICA)
    numeroIdentificacion = "1-2345-6789"
    idPaisRegulador = 1        # id real de gen.PAIS_REGULADOR (ej. Costa Rica)
    nombre1 = "Juan"
    nombre2 = $null
    apellido1 = "Perez"
    apellido2 = "Gomez"
    fechaNacimiento = "1990-05-14"
} | ConvertTo-Json

try {
    $resultado = Invoke-RestMethod -Method Post `
      -Uri "http://localhost:5003/api/interlocutores" `
      -Headers @{ Authorization = "Bearer $token" } `
      -ContentType "application/json" `
      -Body $body2

    $resultado
} catch {
    Write-Host "Status code:" $_.Exception.Response.StatusCode.value__
    $reader = New-Object System.IO.StreamReader($_.Exception.Response.GetResponseStream())
    Write-Host $reader.ReadToEnd()
}
```

Devuelve `201 Created` con el interlocutor recién insertado, incluyendo su
`idInterlocutor`.

**5. `GET /api/interlocutores/{id}` (consulta)**

```powershell
Invoke-RestMethod -Uri "http://localhost:5003/api/interlocutores/1" `
  -Headers @{ Authorization = "Bearer $token" }
```

**Patrón general para depurar un error de la API:** capturar el error con
`try/catch` y leer el stream de la respuesta (`GetResponseStream()`), en
vez de dejar que `Invoke-RestMethod` solo muestre "Error en el servidor
remoto" sin detalle:

```powershell
try {
    Invoke-RestMethod -Uri "<url>" -Headers @{ Authorization = "Bearer $token" }
} catch {
    Write-Host "Status code:" $_.Exception.Response.StatusCode.value__
    $reader = New-Object System.IO.StreamReader($_.Exception.Response.GetResponseStream())
    Write-Host $reader.ReadToEnd()
}
```

## Estructura

```
CoreBancario.Api/
├── Program.cs                          # wiring: auth JWT, middlewares, endpoints
├── appsettings.json / .Development.json
├── Infraestructura/
│   ├── TenantContext.cs                # scoped: tenant resuelto del request
│   ├── ActorContext.cs                 # scoped: identidad resuelta del request
│   ├── DirectorioService.cs            # consulta dir.CLIENTE_SAAS
│   ├── TenantResolutionMiddleware.cs
│   └── ActorResolutionMiddleware.cs
├── Modelos/
│   └── InterlocutorModelos.cs          # DTOs de request/response
├── Datos/
│   ├── InterlocutorRepositorio.cs      # Dapper contra cli.INTERLOCUTOR(_FISICA)
│   └── GestionRepositorio.cs           # Dapper contra apr.GESTION
└── Endpoints/
    ├── InterlocutorEndpoints.cs
    └── GestionEndpoints.cs
```
