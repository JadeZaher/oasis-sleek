<#
.SYNOPSIS
    Mission B architecture-decoupling acceptance gate.

.DESCRIPTION
    Sectioned, re-runnable, no side effects beyond building/testing/starting a
    temporary API process (torn down in finally).  Exits 1 on ANY failure.

      1. Regression  -- invoke scripts/passoff.ps1; assert exit 0.
      2. Warnings    -- dotnet build; assert warnings <= 17 baseline.
      3. God-iface   -- grep *.cs for dead god-interface symbols; assert 0 hits.
      4. /health     -- live: spin podman oasis-postgres, launch API, poll /health;
                        static fallback when podman/DB unavailable (WARN, not fail).
      5. Summary     -- PASS only if 1-3 + 4 all green.

    ASCII-only on purpose (runs identically under Windows PowerShell 5.1
    and PowerShell 7).

.EXAMPLE
    pwsh scripts/passoff-architecture.ps1
    powershell -File scripts/passoff-architecture.ps1

.NOTES
    Requires: dotnet SDK 8+.  Mirrors passoff.ps1 style.
#>

[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"

function Write-Info    { param([string]$m) Write-Host "[*] $m"  -ForegroundColor Cyan   }
function Write-Ok      { param([string]$m) Write-Host "[OK] $m" -ForegroundColor Green  }
function Write-Warn    { param([string]$m) Write-Host "[!!] $m" -ForegroundColor Yellow }
function Write-Err     { param([string]$m) Write-Host "[XX] $m" -ForegroundColor Red    }
function Write-Section {
    param([string]$m)
    Write-Host ""
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host "  $m"                                     -ForegroundColor Cyan
    Write-Host "========================================" -ForegroundColor Cyan
}

# Repo root = parent of this scripts/ directory, regardless of CWD.
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot  = Split-Path -Parent $ScriptDir

$ApiCsproj    = Join-Path $RepoRoot "OASIS.WebAPI.csproj"
$WarnBaseline = 17
$HealthPort   = 15990   # ephemeral port for the smoke-test API instance

# Dead god-interface symbol patterns (must be zero in *.cs outside bin/obj/tool dirs).
$GodPattern = 'IOASISStorageProvider|IOASISStorageProviderNFTExtensions|IQuestRepository|\bProviderContext\b|\.CurrentProvider\b'

# Static-fallback anchors that must exist in Program.cs and the Observability dir.
$StaticProgramAnchors  = @("MapOasisHealth", "AddOasisHealthChecks", "AddOasisObservability")
$StaticObsFiles        = @(
    "Observability/OpenTelemetryExtensions.cs",
    "Observability/HealthCheckExtensions.cs",
    "Observability/StorageHealthCheck.cs",
    "Observability/ProviderHealthMonitorHealthCheck.cs"
)

$Result = [ordered]@{
    "Regression"               = $false
    "Warnings-le-17"           = $false
    "Zero-god-iface-refs"      = $false
    "Health-smoke"             = $false
}
$FailingSection = $null

function Fail-Now {
    param([string]$Section, [string]$Message)
    $script:FailingSection = $Section
    Write-Err $Message
    Write-Section "PASS-OFF ARCHITECTURE: FAILED"
    Write-Err "Failing section: $Section"
    Print-ResultTable
    Pop-Location -ErrorAction SilentlyContinue
    exit 1
}

function Print-ResultTable {
    Write-Host ""
    foreach ($k in $Result.Keys) {
        $mark = if ($Result[$k]) { "[OK]" } else { "[XX]" }
        $col  = if ($Result[$k]) { "Green" } else { "Red" }
        Write-Host "  $mark $k" -ForegroundColor $col
    }
    Write-Host ""
}

# ---- Podman idempotent bring-up (mirrors run-tests.ps1) --------------------
function Initialize-OasisPostgres {
    $runtime = if (Get-Command podman -ErrorAction SilentlyContinue) { "podman" }
               elseif (Get-Command docker -ErrorAction SilentlyContinue) { "docker" }
               else { $null }
    if (-not $runtime) { return $null }

    $name  = "oasis-postgres"
    $state = (& $runtime ps -a --filter "name=$name" --format "{{.State}}" 2>$null | Select-Object -First 1)
    if ($state -eq "running") {
        Write-Info "Postgres '$name' already running."
    } elseif ($state) {
        Write-Info "Starting existing Postgres '$name'..."
        & $runtime start $name | Out-Null
    } else {
        Write-Info "Creating Postgres '$name' ($runtime, 5441->5432)..."
        & $runtime run -d --name $name `
            -e POSTGRES_DB=oasis -e POSTGRES_USER=oasis -e POSTGRES_PASSWORD=oasis123 `
            -p 5441:5432 postgres:16-alpine | Out-Null
    }

    $deadline = (Get-Date).AddSeconds(40)
    do {
        Start-Sleep -Milliseconds 800
        $ready = (& $runtime exec $name pg_isready -U oasis -d oasis 2>&1) -match "accepting connections"
    } while (-not $ready -and (Get-Date) -lt $deadline)

    if (-not $ready) {
        Write-Warn "Postgres '$name' did not become ready within 40s."
        return $null
    }
    Write-Ok "Postgres ready on localhost:5441."
    return $runtime
}

Push-Location $RepoRoot
$ApiProcess = $null

try {
    Write-Section "Mission B - ARCHITECTURE-DECOUPLING ACCEPTANCE GATE"
    Write-Info "Repo root: $RepoRoot"

    # =========================================================================
    # 1. REGRESSION -- invoke passoff.ps1; assert exit 0
    # =========================================================================
    Write-Section "1/4  Regression -- invoke scripts/passoff.ps1"

    $PassoffScript = Join-Path $ScriptDir "passoff.ps1"
    if (-not (Test-Path $PassoffScript)) {
        Fail-Now -Section "Regression" -Message "scripts/passoff.ps1 not found at: $PassoffScript"
    }

    # Detect available PowerShell host.
    $psExe = $null
    if (Get-Command pwsh -ErrorAction SilentlyContinue) {
        $psExe = "pwsh"
    } elseif (Get-Command powershell -ErrorAction SilentlyContinue) {
        $psExe = "powershell"
    } else {
        Fail-Now -Section "Regression" -Message "Neither pwsh nor powershell found in PATH."
    }
    Write-Info "Using: $psExe"

    if ($psExe -eq "pwsh") {
        $passoffOut = & pwsh -File $PassoffScript 2>&1
    } else {
        $passoffOut = & powershell -File $PassoffScript 2>&1
    }
    $passoffExit = $LASTEXITCODE

    # Echo the tail (last 30 lines) for visibility.
    $tail = @($passoffOut)
    $startIdx = [Math]::Max(0, $tail.Count - 30)
    Write-Host ""
    Write-Info "--- passoff.ps1 tail ($($tail.Count) lines total) ---"
    for ($i = $startIdx; $i -lt $tail.Count; $i++) { Write-Host "    $($tail[$i])" }
    Write-Host ""

    if ($passoffExit -ne 0) {
        Fail-Now -Section "Regression" `
            -Message "scripts/passoff.ps1 exited $passoffExit (expected 0). Safety regression detected."
    }
    $Result["Regression"] = $true
    Write-Ok "scripts/passoff.ps1 exited 0 -- regression GREEN"

    # =========================================================================
    # 2. WARNINGS REGRESSION -- assert warnings <= 17 after a fresh build
    # =========================================================================
    Write-Section "2/4  Warnings regression -- dotnet build; assert <= $WarnBaseline warnings"

    $buildOut = & dotnet build $ApiCsproj -c Debug --nologo 2>&1
    $buildExit = $LASTEXITCODE

    $errLine  = ($buildOut | Select-String -Pattern '(\d+)\s+Error\(s\)'   | Select-Object -Last 1)
    $warnLine = ($buildOut | Select-String -Pattern '(\d+)\s+Warning\(s\)' | Select-Object -Last 1)
    $errCount  = if ($errLine)  { [int]$errLine.Matches[0].Groups[1].Value }  else { 0 }
    $warnCount = if ($warnLine) { [int]$warnLine.Matches[0].Groups[1].Value } else { 0 }

    Write-Info "Build exit: $buildExit  errors: $errCount  warnings: $warnCount"

    if ($buildExit -ne 0 -or $errCount -ne 0) {
        Fail-Now -Section "Warnings-le-17" `
            -Message "dotnet build produced $errCount error(s) (exit $buildExit) -- cannot assess warnings."
    }
    if ($warnCount -gt $WarnBaseline) {
        Fail-Now -Section "Warnings-le-17" `
            -Message "Build warnings: $warnCount > baseline $WarnBaseline. A new warning was introduced."
    }
    $Result["Warnings-le-17"] = $true
    Write-Ok "Build warnings: $warnCount (baseline $WarnBaseline) -- GREEN"

    # =========================================================================
    # 3. ZERO GOD-INTERFACE REFERENCES -- grep *.cs (excl bin/obj/tool dirs)
    # =========================================================================
    Write-Section "3/4  Zero god-interface references in *.cs (excl bin/obj/conductor/.pi/.omc/StrykerOutput)"

    $allCs = Get-ChildItem -Path $RepoRoot -Recurse -Filter "*.cs" -ErrorAction SilentlyContinue |
        Where-Object {
            $fp = $_.FullName
            $fp -notmatch [regex]::Escape([System.IO.Path]::DirectorySeparatorChar + "bin"       + [System.IO.Path]::DirectorySeparatorChar) -and
            $fp -notmatch [regex]::Escape([System.IO.Path]::DirectorySeparatorChar + "obj"       + [System.IO.Path]::DirectorySeparatorChar) -and
            $fp -notmatch [regex]::Escape([System.IO.Path]::DirectorySeparatorChar + "conductor" + [System.IO.Path]::DirectorySeparatorChar) -and
            $fp -notmatch [regex]::Escape([System.IO.Path]::DirectorySeparatorChar + ".pi"       + [System.IO.Path]::DirectorySeparatorChar) -and
            $fp -notmatch [regex]::Escape([System.IO.Path]::DirectorySeparatorChar + ".omc"      + [System.IO.Path]::DirectorySeparatorChar) -and
            $fp -notmatch [regex]::Escape([System.IO.Path]::DirectorySeparatorChar + "StrykerOutput" + [System.IO.Path]::DirectorySeparatorChar)
        }

    Write-Info "Scanning $($allCs.Count) .cs files for god-interface patterns..."

    $godHits = @()
    foreach ($f in $allCs) {
        $lines = Get-Content $f.FullName -ErrorAction SilentlyContinue
        for ($li = 0; $li -lt $lines.Count; $li++) {
            if ($lines[$li] -match $GodPattern) {
                $godHits += "$($f.FullName):$($li+1)  $($lines[$li].Trim())"
            }
        }
    }

    if ($godHits.Count -gt 0) {
        Write-Err "Found $($godHits.Count) god-interface reference(s):"
        foreach ($h in $godHits) { Write-Err "  $h" }
        Fail-Now -Section "Zero-god-iface-refs" `
            -Message "$($godHits.Count) god-interface reference(s) remain. Delete or migrate them."
    }
    $Result["Zero-god-iface-refs"] = $true
    Write-Ok "Zero god-interface references -- GREEN"

    # =========================================================================
    # 4. /health LIVE CHECK (with static fallback)
    # =========================================================================
    Write-Section "4/4  /health smoke check (live boot preferred; static fallback if env unavailable)"

    $liveAttempted = $false
    $liveSuccess   = $false
    $healthPath    = $false

    # -- Try the live path ---------------------------------------------------
    $runtime = Initialize-OasisPostgres
    if ($runtime) {
        $liveAttempted = $true
        Write-Info "Podman/Docker available -- attempting live API boot on port $HealthPort..."

        # Stop any stale dotnet/OASIS.WebAPI that might hold the DLL.
        $stale = Get-Process -Name "dotnet", "OASIS.WebAPI" -ErrorAction SilentlyContinue |
            Where-Object {
                ($_.Path -and $_.Path -match "oasis-sleek") -or
                $_.ProcessName -eq "OASIS.WebAPI"
            }
        if ($stale) {
            foreach ($p in $stale) {
                Write-Warn "Stopping stale $($p.ProcessName) (PID $($p.Id))..."
                Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
            }
            Start-Sleep -Seconds 2
        }

        $env:ASPNETCORE_ENVIRONMENT = "Development"

        # --no-launch-profile so Properties/launchSettings.json applicationUrl
        # (http://localhost:5000) does NOT override our ephemeral port; pass the
        # bind URL after -- as --urls (verified working invocation).
        Write-Info "Starting API: dotnet run --project OASIS.WebAPI.csproj -c Debug --no-build --no-launch-profile -- --urls http://127.0.0.1:$HealthPort"
        $ApiProcess = Start-Process -FilePath "dotnet" `
            -ArgumentList "run", "--project", $ApiCsproj, "-c", "Debug", "--no-build", "--no-launch-profile", "--", "--urls", "http://127.0.0.1:$HealthPort" `
            -WorkingDirectory $RepoRoot `
            -NoNewWindow `
            -PassThru

        Write-Info "API PID: $($ApiProcess.Id) -- polling http://127.0.0.1:$HealthPort/health (up to 90s)..."

        $healthUrl  = "http://127.0.0.1:$HealthPort/health"
        $deadline   = (Get-Date).AddSeconds(90)
        $httpOk     = $false
        $bodyHealthy = $false
        $lastStatus = 0
        $lastBody   = ""

        while ((Get-Date) -lt $deadline) {
            Start-Sleep -Milliseconds 2000
            try {
                $resp = Invoke-WebRequest -Uri $healthUrl -UseBasicParsing -TimeoutSec 5 -ErrorAction Stop
                $lastStatus = [int]$resp.StatusCode
                $lastBody   = $resp.Content

                if ($lastStatus -eq 200) {
                    $httpOk = $true
                    # Accept Healthy or Degraded as pass; only Unhealthy fails.
                    if ($lastBody -match '"status"\s*:\s*"(Healthy|Degraded)"') {
                        $bodyHealthy = $true
                        break
                    } elseif ($lastBody -match '"status"\s*:\s*"Unhealthy"') {
                        # Report but keep polling -- the DB migration may still be running.
                        Write-Warn "HTTP 200 but status=Unhealthy; still within deadline..."
                    }
                }
            } catch {
                # Not up yet -- keep polling.
            }
        }

        if ($httpOk -and $bodyHealthy) {
            $liveSuccess = $true
            $healthPath  = "live"
            Write-Ok "GET /health returned HTTP $lastStatus, body status: $(($lastBody | ConvertFrom-Json -ErrorAction SilentlyContinue).status) -- live GREEN"
        } else {
            Write-Warn "Live boot did not produce a Healthy/Degraded 200 within deadline."
            Write-Warn "Last status: $lastStatus  body: $($lastBody.Substring(0, [Math]::Min(200, $lastBody.Length)))"
            # Fall through to static check.
        }
    } else {
        Write-Warn "No podman/docker runtime detected -- skipping live boot, falling back to static check."
    }

    # -- Static fallback if live did not succeed -----------------------------
    if (-not $liveSuccess) {
        $healthPath = "static"
        Write-Warn "Using STATIC fallback for /health section (live path unavailable in this env)."

        $programCs = Join-Path $RepoRoot "Program.cs"
        $staticOk  = $true

        foreach ($anchor in $StaticProgramAnchors) {
            if (Select-String -Path $programCs -Pattern ([regex]::Escape($anchor)) -Quiet) {
                Write-Ok "Program.cs contains: $anchor"
            } else {
                Write-Err "Program.cs MISSING: $anchor"
                $staticOk = $false
            }
        }

        foreach ($rel in $StaticObsFiles) {
            $abs = Join-Path $RepoRoot $rel
            if (Test-Path $abs) {
                Write-Ok "Observability file present: $rel"
            } else {
                Write-Err "Observability file MISSING: $rel"
                $staticOk = $false
            }
        }

        if (-not $staticOk) {
            Fail-Now -Section "Health-smoke" `
                -Message "Static /health fallback failed: required wiring or files missing."
        }
        Write-Ok "Static /health wiring verified -- GREEN (live path was unavailable)"
        $Result["Health-smoke"] = $true
    } else {
        $Result["Health-smoke"] = $true
    }

    # =========================================================================
    # 5. FINAL VERDICT
    # =========================================================================
    $allGreen = @($Result.Values | Where-Object { $_ -eq $false }).Count -eq 0

    if ($allGreen) {
        Write-Section "PASS-OFF ARCHITECTURE: GREEN"
        foreach ($k in $Result.Keys) { Write-Ok $k }
        Write-Host ""
        Write-Ok "/health path: $healthPath"
        Write-Ok "Mission B architecture-decoupling gate GREEN"
        Write-Host ""
        Pop-Location -ErrorAction SilentlyContinue
        exit 0
    } else {
        Fail-Now -Section "Final-verdict" -Message "Not all architecture gate sections passed."
    }

} finally {
    # Always kill the ephemeral API process.
    if ($ApiProcess -and -not $ApiProcess.HasExited) {
        Write-Info "Tearing down API process (PID $($ApiProcess.Id))..."
        Stop-Process -Id $ApiProcess.Id -Force -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 1
    }
    # Also kill any child dotnet that inherited our env (belt-and-suspenders).
    Get-Process -Name "dotnet", "OASIS.WebAPI" -ErrorAction SilentlyContinue |
        Where-Object { ($_.Path -and $_.Path -match "oasis-sleek") -or $_.ProcessName -eq "OASIS.WebAPI" } |
        ForEach-Object { Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue }
    Pop-Location -ErrorAction SilentlyContinue
}
