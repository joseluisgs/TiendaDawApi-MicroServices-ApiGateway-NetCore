# ============================================================
# Start-Api.ps1 - arranque COMPLETO de desarrollo en orden
# ============================================================
# Pasos: parar servicios previos -> build -> docker (si hace
# falta) -> reset de BDs -> servicios uno a uno -> gateway.
#
# Uso:
#   .\Start-Api.ps1               # limpio: para, compila, resetea BDs y arranca
#   .\Start-Api.ps1 -SinReset     # sin borrar datos de las BDs
#   .\Start-Api.ps1 -SinBuild     # rearranque rapido (usa lo ya compilado)
#   .\Start-Api.ps1 -Parar        # solo para los servicios
#
# Orden de arranque: identity, storage, historical (espera a
# Cassandra), email, catalog, orders, statistics, backup,
# graphql y por ultimo el gateway.
# ============================================================
param(
    [switch]$SinReset,
    [switch]$SinBuild,
    [switch]$Parar
)

$ErrorActionPreference = "Continue"
$repo = $PSScriptRoot
if (-not $repo) { $repo = (Get-Location).Path }
Set-Location $repo

$SERVICIOS = @(
    @{ n = "identity";   p = 5101; d = "Services\Identity";   esp = 90 },
    @{ n = "storage";    p = 5102; d = "Services\Storage";    esp = 60 },
    @{ n = "historical"; p = 5103; d = "Services\Historical"; esp = 150 },
    @{ n = "email";      p = 5104; d = "Services\Email";      esp = 60 },
    @{ n = "catalog";    p = 5105; d = "Services\Catalog";    esp = 60 },
    @{ n = "orders";     p = 5106; d = "Services\Orders";     esp = 60 },
    @{ n = "statistics"; p = 5107; d = "Services\Statistics"; esp = 60 },
    @{ n = "backup";     p = 5108; d = "Services\Backup";     esp = 60 },
    @{ n = "graphql";    p = 5109; d = "Services\GraphQL";    esp = 60 }
)
$NOMBRES = @("Gateway", "Identity", "Storage", "Historical", "Email", "Catalog", "Orders", "Statistics", "Backup", "GraphQL")
$LOGS = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) "Temp\tienda-logs"

function Log($m) { Write-Host ("  " + $m) }

function Parar-Servicios {
    $parados = 0
    foreach ($nombre in $NOMBRES) {
        $procs = Get-Process $nombre -ErrorAction SilentlyContinue
        foreach ($p in $procs) {
            Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
            $parados++
        }
    }
    if ($parados -gt 0) { Start-Sleep -Seconds 2 }
    return $parados
}

function Test-Puerto($puerto) {
    try {
        $c = New-Object Net.Sockets.TcpClient
        $c.Connect("localhost", $puerto)
        $c.Close()
        return $true
    } catch { return $false }
}

function Esperar-Puerto($puerto, $segundos) {
    $t0 = Get-Date
    while (((Get-Date) - $t0).TotalSeconds -lt $segundos) {
        if (Test-Puerto $puerto) { return $true }
        Start-Sleep -Seconds 2
    }
    return $false
}

function Esperar-Health($puerto, $segundos) {
    $t0 = Get-Date
    while (((Get-Date) - $t0).TotalSeconds -lt $segundos) {
        try {
            $r = Invoke-WebRequest "http://localhost:$puerto/health" -UseBasicParsing -TimeoutSec 3
            if ($r.StatusCode -eq 200) { return $true }
        } catch { Start-Sleep -Milliseconds 500 }
    }
    return $false
}

function Mostrar-Error-Log($servicio) {
    $err = Join-Path $LOGS ($servicio + "-run.err.log")
    $out = Join-Path $LOGS ($servicio + "-run.log")
    $fichero = $err
    if (-not (Test-Path $fichero) -or (Get-Item $fichero).Length -eq 0) { $fichero = $out }
    if (Test-Path $fichero) {
        Write-Host ("    ultimas lineas de " + (Split-Path $fichero -Leaf) + ":")
        Get-Content $fichero -Tail 12 | ForEach-Object { Write-Host ("      " + $_) }
    }
}

# cqlsh dentro del contenedor = nodo listo de VERDAD (el puerto TCP 9042
# se abre antes de que Cassandra sirva consultas - comprobado en pruebas)
function Esperar-Cassandra($segundos) {
    $t0 = Get-Date
    while (((Get-Date) - $t0).TotalSeconds -lt $segundos) {
        docker exec tienda-local-cassandra cqlsh -e "SELECT cluster_name FROM system.local;" *> $null
        if ($LASTEXITCODE -eq 0) { return $true }
        Start-Sleep -Seconds 3
    }
    return $false
}

# ---------- 0) parar servicios previos (determinismo) ----------
Write-Host ""
Write-Host "=== 0) PARAR SERVICIOS EXISTENTES ==="
$n = Parar-Servicios
if ($n -gt 0) { Log ("parados " + $n + " procesos") } else { Log "ninguno corriendo" }

if ($Parar) {
    Write-Host ""
    Write-Host "  modo -Parar: servicios detenidos. Adios."
    exit 0
}

# ---------- 1) build ----------
if (-not $SinBuild) {
    Write-Host ""
    Write-Host "=== 1) BUILD (dotnet build TiendaApi.slnx) ==="
    dotnet build TiendaApi.slnx -v q -nologo
    if ($LASTEXITCODE -ne 0) { Write-Host "  FALLO: build con errores (ver arriba)"; exit 1 }
    Log "build OK"
} else {
    Write-Host ""
    Write-Host "=== 1) BUILD (omitido por -SinBuild) ==="
}

# ---------- 2) docker ----------
Write-Host ""
Write-Host "=== 2) DOCKER ==="
$dockerOk = $false
docker info *> $null
if ($LASTEXITCODE -eq 0) { $dockerOk = $true }
if (-not $dockerOk) {
    Log "el demonio Docker no responde - arrancando Docker Desktop..."
    $exe = "C:\Program Files\Docker\Docker\Docker Desktop.exe"
    if (Test-Path $exe) { Start-Process $exe } else { Log ("AVISO: no encontrado en " + $exe) }
    $t0 = Get-Date
    while (((Get-Date) - $t0).TotalSeconds -lt 180) {
        Start-Sleep -Seconds 5
        docker info *> $null
        if ($LASTEXITCODE -eq 0) { $dockerOk = $true; break }
    }
}
if (-not $dockerOk) { Write-Host "  FALLO: Docker no disponible tras 180s"; exit 1 }
Log "Docker OK"

# ---------- 3) bases de datos ----------
Write-Host ""
if ($SinReset) {
    Write-Host "=== 3) BASES DE DATOS (sin reset: solo levantar) ==="
    docker compose -f docker-compose.local.yml up -d
    if ($LASTEXITCODE -ne 0) { Write-Host "  FALLO: docker compose up"; exit 1 }
    foreach ($puerto in @(5432, 27017, 9042)) {
        if (Esperar-Puerto $puerto 90) { Log (":" + $puerto + " OK") }
        else { Write-Host ("  FALLO: :" + $puerto + " no responde"); exit 1 }
    }
} else {
    Write-Host "=== 3) BASES DE DATOS (reset completo con Reset-Database.ps1) ==="
    & (Join-Path $repo "Reset-Database.ps1")
    if ($LASTEXITCODE -ne 0) { Write-Host ("  AVISO: Reset-Database devolvio " + $LASTEXITCODE) }
    foreach ($puerto in @(5432, 27017)) {
        if (Esperar-Puerto $puerto 90) { Log (":" + $puerto + " OK") }
        else { Write-Host ("  FALLO: :" + $puerto + " no responde"); exit 1 }
    }
}

# ---------- 4) servicios uno a uno ----------
Write-Host ""
Write-Host "=== 4) SERVICIOS (uno a uno, en orden) ==="
New-Item -ItemType Directory -Force -Path $LOGS | Out-Null
# entorno local: NUNCA Production (carga appsettings.Production.json con nombres Docker)
$env:ASPNETCORE_ENVIRONMENT = "Development"
Remove-Item Env:ASPNETCORE_URLS -ErrorAction SilentlyContinue

$fallidos = @()
foreach ($s in $SERVICIOS) {
    if (Test-Puerto $s.p) { Log ($s.n.PadRight(12) + " :" + $s.p + " - ya responde, se omite"); continue }

    # historical: la JVM de Cassandra tarda y el servicio NO reintenta si
    # arranca antes (aborta en 1s) -> esperar cqlsh real + hasta 3 intentos
    $intentos = 1
    if ($s.n -eq "historical") {
        Log "esperando a Cassandra (cqlsh en el contenedor)..."
        if (-not (Esperar-Cassandra 240)) {
            Write-Host "  FALLO: Cassandra no acepta cqlsh - historical no se lanza"
            $fallidos += "historical (sin Cassandra)"
            continue
        }
        Start-Sleep -Seconds 3
        Log "Cassandra lista"
        $intentos = 3
    }

    $out = Join-Path $LOGS ($s.n + "-run.log")
    $err = Join-Path $LOGS ($s.n + "-run.err.log")
    $exe = $s.n.Substring(0,1).ToUpper() + $s.n.Substring(1)
    if ($exe -eq "GraphQl") { $exe = "GraphQL" }
    $ok = $false

    for ($intento = 1; $intento -le $intentos; $intento++) {
        Start-Process -FilePath "dotnet" -ArgumentList "run","--project",$s.d,"--no-launch-profile" `
            -WorkingDirectory $repo -RedirectStandardOutput $out -RedirectStandardError $err -WindowStyle Hidden
        $extra = ""
        if ($intentos -gt 1) { $extra = " (intento " + $intento + "/" + $intentos + ")" }
        Log ($s.n.PadRight(12) + " :" + $s.p + " lanzado" + $extra + " - esperando /health...")

        # con reintentos el primer intento es corto: si muere, se nota pronto
        $esp = $s.esp
        if ($intentos -gt 1 -and $intento -eq 1) { $esp = 60 }
        if (Esperar-Health $s.p $esp) {
            Log ($s.n.PadRight(12) + " OK")
            $ok = $true
            break
        }

        if ($intento -lt $intentos) {
            Get-Process $exe -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
            Log ($s.n + " no levanto - esperando 20s a que Cassandra asiente y reintentando...")
            Start-Sleep -Seconds 20
            continue
        }
        Write-Host ("  AVISO: " + $s.n + " no responde - se continua con el resto")
        Mostrar-Error-Log $s.n
    }
    if (-not $ok) { $fallidos += $s.n }
}

# ---------- 5) gateway (el ultimo) ----------
Write-Host ""
Write-Host "=== 5) GATEWAY (ultimo) ==="
if (Test-Puerto 5000) {
    Log "gateway :5000 - ya responde, se omite"
} else {
    $out = Join-Path $LOGS "gateway-run.log"
    $err = Join-Path $LOGS "gateway-run.err.log"
    Start-Process -FilePath "dotnet" -ArgumentList "run","--project","Gateway","--no-launch-profile" `
        -WorkingDirectory $repo -RedirectStandardOutput $out -RedirectStandardError $err -WindowStyle Hidden
    Log "gateway   :5000 lanzado - esperando /health (60s max)..."
    if (Esperar-Health 5000 60) {
        Log "gateway   OK"
    } else {
        Write-Host "  AVISO: gateway no responde"
        $fallidos += "gateway"
        Mostrar-Error-Log "gateway"
    }
}

# ---------- resumen ----------
Write-Host ""
Write-Host "=== RESUMEN ==="
try {
    $r = Invoke-WebRequest "http://localhost:5000/health" -UseBasicParsing -TimeoutSec 15
    $j = $r.Content | ConvertFrom-Json
    Log ("/health -> " + $r.StatusCode + "  " + $j.summary)
} catch {
    Log ("/health no responde: " + $_.Exception.Message)
}
Log ("API:      http://localhost:5000")
Log ("Swagger:  http://localhost:5000/swagger")
Log ("Logs:     " + $LOGS)
if ($fallidos.Count -gt 0) {
    Write-Host ""
    Write-Host ("  SERVICIOS SIN RESPONDER: " + ($fallidos -join ", "))
} else {
    Write-Host ""
    Write-Host "  TODO EN VERDE"
}
Write-Host ""
Write-Host "  Para pararlo todo:  .\Start-Api.ps1 -Parar"
Write-Host ""
