<#
.SYNOPSIS
    Runs the OASIS.WebAPI .NET test suites.

.DESCRIPTION
    Single entry point for every automated test in the repo. By default it runs
    the xUnit unit + integration suites via `dotnet test`. Optional switches add
    the live HTTP harness and Stryker mutation testing.

.PARAMETER Configuration
    Build configuration. Default: Debug.

.PARAMETER Live
    Also run the live HTTP harness (OASIS.WebAPI.LiveTests). Requires a running
    API; pass -LiveUrl to target a specific host.

.PARAMETER LiveUrl
    Base URL for the live harness. Default: https://localhost:5001.

.PARAMETER Mutation
    Run Stryker.NET mutation testing instead of the normal suites. Output is
    written to tests/StrykerOutput (kept out of git).

.EXAMPLE
    ./tests/run-tests.ps1
    ./tests/run-tests.ps1 -Configuration Release
    ./tests/run-tests.ps1 -Live -LiveUrl https://localhost:5001
    ./tests/run-tests.ps1 -Mutation
#>
[CmdletBinding()]
param(
    [string]$Configuration = "Debug",
    [switch]$Live,
    [string]$LiveUrl = "https://localhost:5001",
    [switch]$Mutation
)

$ErrorActionPreference = "Stop"

# Repo root = parent of this tests/ directory, regardless of CWD.
$TestsDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot = Split-Path -Parent $TestsDir

# Integration tests run against a PERSISTENT local Postgres (data survives
# between runs). Bring the container up if needed, but never recreate a
# running/existing one (that would wipe persisted data). Mirrors the
# image/ports/credentials in docker-compose.yml + appsettings.json (5441).
function Initialize-IntegrationPostgres {
    $runtime = if (Get-Command podman -ErrorAction SilentlyContinue) { "podman" }
               elseif (Get-Command docker -ErrorAction SilentlyContinue) { "docker" }
               else { $null }
    if (-not $runtime) {
        Write-Host "==> No podman/docker; skipping Postgres bring-up (integration tests will fail without a DB on localhost:5441)." -ForegroundColor Yellow
        return
    }

    $name = "oasis-postgres"
    $state = (& $runtime ps -a --filter "name=$name" --format "{{.State}}" 2>$null | Select-Object -First 1)
    if ($state -eq "running") {
        Write-Host "==> Postgres '$name' already running (persistent data kept)." -ForegroundColor DarkGray
    }
    elseif ($state) {
        Write-Host "==> Starting existing Postgres '$name' (persistent data kept)..." -ForegroundColor Cyan
        & $runtime start $name | Out-Null
    }
    else {
        Write-Host "==> Creating Postgres '$name' ($runtime, 5441->5432)..." -ForegroundColor Cyan
        & $runtime run -d --name $name `
            -e POSTGRES_DB=oasis -e POSTGRES_USER=oasis -e POSTGRES_PASSWORD=oasis123 `
            -p 5441:5432 postgres:16-alpine | Out-Null
    }

    $deadline = (Get-Date).AddSeconds(40)
    do {
        Start-Sleep -Milliseconds 800
        $ready = (& $runtime exec $name pg_isready -U oasis -d oasis 2>&1) -match "accepting connections"
    } while (-not $ready -and (Get-Date) -lt $deadline)
    if (-not $ready) { throw "Postgres '$name' did not become ready on localhost:5441 within 40s." }
    Write-Host "==> Postgres ready on localhost:5441 (db 'oasis')." -ForegroundColor Green
}

Push-Location $RepoRoot
try {
    if ($Mutation) {
        Write-Host "==> Stryker.NET mutation testing (output: tests/StrykerOutput)" -ForegroundColor Cyan
        dotnet stryker --output tests/StrykerOutput
        exit $LASTEXITCODE
    }

    Initialize-IntegrationPostgres

    $testProjects = @(
        "tests/OASIS.WebAPI.Tests/OASIS.WebAPI.Tests.csproj",
        "tests/OASIS.WebAPI.IntegrationTests/OASIS.WebAPI.IntegrationTests.csproj"
    )

    foreach ($proj in $testProjects) {
        Write-Host "==> dotnet test $proj ($Configuration)" -ForegroundColor Cyan
        dotnet test $proj --configuration $Configuration
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    }

    if ($Live) {
        Write-Host "==> Live HTTP harness against $LiveUrl" -ForegroundColor Cyan
        dotnet run --project tests/OASIS.WebAPI.LiveTests --configuration $Configuration -- --url $LiveUrl
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    }

    Write-Host "==> All requested test suites passed." -ForegroundColor Green
}
finally {
    Pop-Location
}
