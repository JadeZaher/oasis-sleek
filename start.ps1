<#
.SYNOPSIS
    Start/stop the full OASIS Sleek stack (PostgreSQL, API, Frontend).

.DESCRIPTION
    Spin up or tear down all services needed for local development:
      - PostgreSQL container (via Podman or Docker)
      - .NET 8 WebAPI on port 5000
      - Next.js frontend on port 3000

    The API runs database migrations automatically on startup.

.PARAMETER Mode
    "up"   - Start all services
    "down" - Stop all services and containers

.EXAMPLE
    .\start.ps1 -Mode up
    .\start.ps1 -Mode down

.NOTES
    Requires: dotnet SDK 8+, Node.js 18+, Podman or Docker
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("up", "down")]
    [string]$Mode
)

$ErrorActionPreference = "Stop"

function Write-Info    { param([string]$m) Write-Host "[*] $m"              -ForegroundColor Cyan   }
function Write-Ok      { param([string]$m) Write-Host "[OK] $m"             -ForegroundColor Green  }
function Write-Warn    { param([string]$m) Write-Host "[!!] $m"             -ForegroundColor Yellow }
function Write-Err     { param([string]$m) Write-Host "[XX] $m"             -ForegroundColor Red    }
function Write-Divider {                    Write-Host "========================================"   -ForegroundColor Cyan  }

# Config
$ProjectRoot     = $PSScriptRoot
$ApiPort         = 5000
$WebPort         = 3000
$DbDefaultPort   = 5441
$DbContainerPort = 5432

$DbImage         = "postgres:16-alpine"
$DbName          = "oasis-postgres"
$DbUser          = "oasis"
$DbPass          = "oasis123"
$DbDatabase      = "oasis"

$FrontendDir     = Join-Path $ProjectRoot "frontend"

# Helpers
function Test-CommandAvailable {
    param([string]$cmd)
    $null -ne (Get-Command $cmd -ErrorAction SilentlyContinue)
}

function Get-PortsInUse {
    @{} + (Get-NetTCPConnection -State Listen -ErrorAction SilentlyContinue |
        Group-Object -Property LocalPort |
        ForEach-Object { $_.Group[0] } |
        ForEach-Object { $_.LocalPort })
}

function Find-FreePort {
    param([int]$Start, [int]$End)
    foreach ($p in $Start..$End) {
        $inUse = Get-NetTCPConnection -LocalPort $p -State Listen -ErrorAction SilentlyContinue
        if (-not $inUse) { return $p }
    }
    return $null
}

function Kill-ProcessOnPort {
    param([int]$Port, [string]$Label)
    $conns = Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue
    if ($conns) {
        foreach ($c in $conns) {
            $proc = Get-Process -Id $c.OwningProcess -ErrorAction SilentlyContinue
            $procName = if ($proc) { $proc.ProcessName } else { "unknown" }
            Write-Warn "Port $Port in use by $procName (PID $($c.OwningProcess)) - killing..."
            try {
                Stop-Process -Id $c.OwningProcess -Force -ErrorAction Stop
                Start-Sleep -Milliseconds 500
                Write-Ok "Killed $procName on port $Port"
                return $true
            } catch {
                Write-Err "Cannot kill process on port $Port (access denied - running as service?)"
                return $false
            }
        }
    }
    return $null
}

function Wait-ForPort {
    param([int]$Port, [int]$TimeoutSeconds = 30, [string]$Name)
    Write-Info "Waiting for $Name on port $Port (up to ${TimeoutSeconds}s)..."
    $start = Get-Date
    while ($true) {
        try {
            $tcp = New-Object System.Net.Sockets.TcpClient
            $tcp.Connect("localhost", $Port)
            $tcp.Close()
            Write-Ok "$Name is ready on port $Port"
            return $true
        } catch {
            try { $tcp.Dispose() } catch {}
        }
        if (((Get-Date) - $start).TotalSeconds -ge $TimeoutSeconds) {
            Write-Err "Timeout waiting for $Name on port $Port"
            return $false
        }
        Start-Sleep -Milliseconds 500
    }
}

function Wait-ForHttp {
    param([string]$Url, [int]$TimeoutSeconds = 30, [string]$Name, [switch]$UseHead)
    $method = if ($UseHead) { "Head" } else { "Get" }
    Write-Info "Waiting for $Name at $Url (up to ${TimeoutSeconds}s)..."
    $start = Get-Date
    while ($true) {
        try {
            $resp = Invoke-WebRequest -Uri $Url -Method $method -UseBasicParsing -MaximumRedirection 2 -TimeoutSec 2 -ErrorAction Stop
            if ($resp.StatusCode -lt 400) {
                Write-Ok "$Name is ready at $Url"
                return $true
            }
        } catch {
            # Follow redirects (301/302)
            if ($_.Exception.Response -and $_.Exception.Response.StatusCode -in (301, 302)) {
                Write-Ok "$Name is ready at $Url"
                return $true
            }
            # Other errors - keep retrying
        }
        if (((Get-Date) - $start).TotalSeconds -ge $TimeoutSeconds) {
            Write-Err "Timeout waiting for $Name at $Url"
            return $false
        }
        Start-Sleep -Seconds 1
    }
}

# =============================================================================
#  SPIN DOWN
# =============================================================================
function Stop-All {
    Write-Divider
    Write-Info "  Stopping OASIS Sleek stack"
    Write-Divider
    Write-Host ""

    # Kill lingering API process
    $apiKilled = Kill-ProcessOnPort -Port $ApiPort -Label "API"
    if ($apiKilled -eq $null) { Write-Info "No process on port $ApiPort" }
    if ($apiKilled -eq $true) { Start-Sleep -Seconds 1 }

    # Kill lingering frontend process
    $webKilled = Kill-ProcessOnPort -Port $WebPort -Label "Frontend"
    if ($webKilled -eq $null) { Write-Info "No process on port $WebPort" }
    if ($webKilled -eq $true) { Start-Sleep -Seconds 1 }

    # Also clean up stray dotnet/npm processes
    $strayDotnet = Get-Process -Name "dotnet" -ErrorAction SilentlyContinue |
        Where-Object { $_.CommandLine -match "OASIS" -or $_.Path -match "oasis-sleek" }
    if ($strayDotnet) {
        Write-Info "Cleaning up stray dotnet processes..."
        $strayDotnet | Stop-Process -Force -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 1
    }

    $strayNode = Get-Process -Name "node" -ErrorAction SilentlyContinue |
        Where-Object { $_.CommandLine -match "next" -and $_.Path -match "oasis-sleek" }
    if ($strayNode) {
        Write-Info "Cleaning up stray node processes..."
        $strayNode | Stop-Process -Force -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 1
    }

    # Stop database container
    $containerCmd = $null
    if (Test-CommandAvailable "podman")      { $containerCmd = "podman" }
    elseif (Test-CommandAvailable "docker")   { $containerCmd = "docker" }

    if ($containerCmd) {
        $existing = & $containerCmd ps -a --filter "name=$DbName" --format "{{.Names}}" 2>$null
        if ($existing -match $DbName) {
            Write-Info "Stopping database container ($containerCmd)..."
            & $containerCmd stop $DbName 2>$null | Out-Null
            & $containerCmd rm $DbName 2>$null | Out-Null
            Write-Ok "Database container stopped and removed"
        } else {
            Write-Info "No database container found"
        }
    }

    # Clean up log files
    Remove-Item (Join-Path $ProjectRoot "api.log") -ErrorAction SilentlyContinue
    Remove-Item (Join-Path $ProjectRoot "api-error.log") -ErrorAction SilentlyContinue

    Write-Host ""
    Write-Ok "All services stopped."
}

# =============================================================================
#  SPIN UP
# =============================================================================
function Start-All {
    Write-Divider
    Write-Info "  Starting OASIS Sleek stack"
    Write-Divider
    Write-Host ""

    # -- 1. Prerequisite checks ------------------------------------------------
    Write-Info "Step 1/5: Checking prerequisites..."

    if (-not (Test-CommandAvailable "dotnet")) {
        Write-Err "dotnet SDK not found. Install .NET 8 SDK first."
        exit 1
    }
    $dotnetVersion = dotnet --version 2>$null
    Write-Ok "dotnet $dotnetVersion found"

    if (-not (Test-CommandAvailable "node")) {
        Write-Err "node not found. Install Node.js 18+ first."
        exit 1
    }
    $nodeVersion = node --version 2>$null
    Write-Ok "Node.js $nodeVersion found"

    if (-not (Test-CommandAvailable "npm")) {
        Write-Err "npm not found."
        exit 1
    }
    Write-Ok "npm found"

    # Container runtime
    $containerCmd = $null
    if (Test-CommandAvailable "podman")      { $containerCmd = "podman" }
    elseif (Test-CommandAvailable "docker")   { $containerCmd = "docker" }
    else {
        Write-Err "Neither podman nor docker found. Install one for PostgreSQL."
        exit 1
    }
    Write-Ok "$containerCmd found (container runtime)"

    Write-Host ""

    # -- 2. Resolve database port & check for Windows PostgreSQL ---------------
    Write-Info "Step 2/5: Resolving database port..."

    # Warn about Windows PostgreSQL services
    $winPgServices = Get-Service | Where-Object { $_.Name -like '*postgresql*' -and $_.Status -eq 'Running' }
    if ($winPgServices) {
        Write-Warn "Windows PostgreSQL services detected:"
        $winPgServices | ForEach-Object { Write-Warn "  $($_.DisplayName) (running)" }
        Write-Warn "These services block ports 5432/5433. The script will use an alternate port."
    }

    # Find a free port starting from default
    $DbPort = $DbDefaultPort
    if (Get-NetTCPConnection -LocalPort $DbPort -State Listen -ErrorAction SilentlyContinue) {
        Write-Warn "Default DB port $DbPort is in use. Scanning for a free port..."
        $freePort = Find-FreePort -Start 5440 -End 5499
        if (-not $freePort) {
            Write-Err "No free port found in range 5440-5499"
            exit 1
        }
        $DbPort = $freePort
        Write-Ok "Using port $DbPort for PostgreSQL"
    } else {
        Write-Ok "Port $DbPort is available"
    }

    # Remove any existing container
    $existingContainer = & $containerCmd ps -a --filter "name=$DbName" --format "{{.Names}}" 2>$null
    if ($existingContainer -match $DbName) {
        Write-Info "Removing existing container $DbName..."
        & $containerCmd stop $DbName 2>$null | Out-Null
        & $containerCmd rm $DbName 2>$null | Out-Null
    }

    Write-Info "Creating container $DbName on host port $DbPort..."
    & $containerCmd run -d --name $DbName `
        -e "POSTGRES_DB=$DbDatabase" `
        -e "POSTGRES_USER=$DbUser" `
        -e "POSTGRES_PASSWORD=$DbPass" `
        -p "${DbPort}:${DbContainerPort}" `
        $DbImage 2>$null | Out-Null

    if (-not (Wait-ForPort -Port $DbPort -TimeoutSeconds 30 -Name "PostgreSQL")) {
        Write-Err "PostgreSQL failed to start."
        & $containerCmd logs $DbName 2>$null
        exit 1
    }
    Write-Host ""

    # Update appsettings.json with the resolved DB port
    $appSettings = Join-Path $ProjectRoot "appsettings.json"
    if (Test-Path $appSettings) {
        $content = Get-Content $appSettings -Raw
        if ($content -match 'Port=\d+') {
            $newContent = $content -replace 'Port=\d+', "Port=$DbPort"
        } else {
            $newContent = $content -replace 'Host=localhost;Database=oasis', "Host=localhost;Port=$DbPort;Database=oasis"
        }
        if ($content -ne $newContent) {
            Set-Content -Path $appSettings -Value $newContent -NoNewline
            Write-Ok "Updated appsettings.json to use port $DbPort"
        }
    }

    # -- 3. Restore and build API ----------------------------------------------
    Write-Info "Step 3/5: Restoring .NET packages and building API..."

    # Clean up any test directories that pollute the build
    $testConnDir = Join-Path $ProjectRoot "test-conn"
    if (Test-Path $testConnDir) {
        Write-Info "Removing stray test-conn directory..."
        Remove-Item $testConnDir -Recurse -Force
    }

    $csproj = Join-Path $ProjectRoot "OASIS.WebAPI.csproj"
    Push-Location $ProjectRoot
    dotnet restore $csproj 2>&1 | Out-Null
    $buildOut = dotnet build $csproj --no-restore 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Err "dotnet build failed:"
        $buildOut | ForEach-Object { Write-Host "  $_" }
        Pop-Location
        exit 1
    }
    Pop-Location
    Write-Ok "API built successfully"
    Write-Host ""

    # -- 4. Start API ----------------------------------------------------------
    Write-Info "Step 4/5: Starting .NET WebAPI on port $ApiPort..."

    $apiKilled = Kill-ProcessOnPort -Port $ApiPort -Label "API"
    if ($apiKilled -eq $true) { Start-Sleep -Seconds 2 }

    $env:ASPNETCORE_ENVIRONMENT = "Development"

    $apiLogPath = Join-Path $ProjectRoot "api.log"
    $apiErrLogPath = Join-Path $ProjectRoot "api-error.log"

    $csproj = Join-Path $ProjectRoot "OASIS.WebAPI.csproj"
    Push-Location $ProjectRoot
    $apiJob = Start-Process -FilePath "dotnet" `
        -ArgumentList "run", $csproj, "--no-build", "--urls", "http://localhost:$ApiPort" `
        -WindowStyle Hidden `
        -PassThru `
        -RedirectStandardOutput $apiLogPath `
        -RedirectStandardError $apiErrLogPath
    Pop-Location

    Write-Ok "API process started (PID $($apiJob.Id))"

    if (-not (Wait-ForHttp -Url "http://localhost:${ApiPort}/swagger/index.html" -TimeoutSeconds 60 -Name "API")) {
        Write-Err "API failed to start. Check api-error.log:"
        if (Test-Path $apiErrLogPath) {
            Get-Content $apiErrLogPath | Select-Object -Last 20 | ForEach-Object { Write-Host "  $_" }
        }
        exit 1
    }
    Write-Host ""

    # -- 5. Start Frontend -----------------------------------------------------
    Write-Info "Step 5/5: Starting Next.js frontend on port $WebPort..."

    $webKilled = Kill-ProcessOnPort -Port $WebPort -Label "Frontend"
    if ($webKilled -eq $true) { Start-Sleep -Seconds 2 }

    if (-not (Test-Path (Join-Path $FrontendDir "node_modules"))) {
        Write-Info "Installing frontend dependencies (first run)..."
        Push-Location $FrontendDir
        npm.cmd install
        if ($LASTEXITCODE -ne 0) {
            Write-Err "npm install failed"
            Pop-Location
            exit 1
        }
        Pop-Location
        Write-Ok "Frontend dependencies installed"
    }

    $webLogPath = Join-Path $FrontendDir "web.log"
    $webErrLogPath = Join-Path $FrontendDir "web-error.log"

    Push-Location $FrontendDir
    $webJob = Start-Process -FilePath "npm.cmd" `
        -ArgumentList "run", "dev", "--", "-p", "$WebPort" `
        -WindowStyle Hidden `
        -PassThru `
        -RedirectStandardOutput $webLogPath `
        -RedirectStandardError $webErrLogPath
    Pop-Location

    Write-Ok "Frontend process started (PID $($webJob.Id))"

    if (-not (Wait-ForHttp -Url "http://localhost:${WebPort}" -TimeoutSeconds 60 -Name "Frontend")) {
        Write-Err "Frontend failed to start. Check web-error.log:"
        if (Test-Path $webErrLogPath) {
            Get-Content $webErrLogPath | Select-Object -Last 20 | ForEach-Object { Write-Host "  $_" }
        }
        exit 1
    }
    Write-Host ""

    # -- Summary ---------------------------------------------------------------
    Write-Host ""
    Write-Divider
    Write-Ok "  OASIS Sleek is running!"
    Write-Divider
    Write-Host ""
    Write-Host "  Frontend:  http://localhost:$WebPort"            -ForegroundColor White
    Write-Host "  API:       http://localhost:$ApiPort"            -ForegroundColor White
    Write-Host "  Swagger:   http://localhost:$ApiPort/swagger"    -ForegroundColor White
    Write-Host "  Database:  localhost:$DbPort"                    -ForegroundColor White
    Write-Host ""
    Write-Info "  To stop everything: .\start.ps1 -Mode down"
    Write-Host ""
}

# Main
switch ($Mode) {
    "up"   { Start-All  }
    "down" { Stop-All   }
}
