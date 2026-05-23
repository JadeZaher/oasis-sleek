<#
.SYNOPSIS
    Replay a SurrealDB backup file into the target namespace and database.

.DESCRIPTION
    Wraps `surreal import` (via docker/podman exec into the oasis-surrealdb
    container) to replay a .surql backup file produced by backup.ps1.

    - Auto-detects docker (preferred) or podman as the container runtime.
    - Verifies the input file exists.
    - Prompts for confirmation unless -Force is supplied.
    - Prefers the SURREAL_ROOT_PASS environment variable over the -Pass
      parameter so secrets never need to be passed on the command line in CI.
    - Preserves the surreal CLI exit code on failure.

    For a full disaster-recovery restore:
      1. Stop the OASIS API.
      2. Start a fresh SurrealDB container pointed at a clean data volume.
      3. Run this script with -Force.
      4. Restart the API.

.PARAMETER InputPath
    Path to the .surql backup file to import. Required.

.PARAMETER Endpoint
    HTTP endpoint of the SurrealDB instance. Default: http://localhost:8442.

.PARAMETER Namespace
    SurrealDB namespace to import into. Default: oasis.

.PARAMETER Database
    SurrealDB database to import into. Default: oasis.

.PARAMETER User
    SurrealDB root username. Default: root.

.PARAMETER Pass
    SurrealDB root password. Default: oasis-surreal-root.
    Override via env var SURREAL_ROOT_PASS in production.

.PARAMETER Force
    Skip the interactive confirmation prompt. Use in CI or scripted recovery.

.EXAMPLE
    pwsh scripts/surrealdb/restore.ps1 -InputPath ./backups/oasis-20260522-143012.surql

.EXAMPLE
    pwsh scripts/surrealdb/restore.ps1 -InputPath ./backups/oasis-20260522-143012.surql -Force

.EXAMPLE
    $env:SURREAL_ROOT_PASS = "my-secret"
    pwsh scripts/surrealdb/restore.ps1 -InputPath ./backups/oasis-20260522-143012.surql -Force
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$InputPath,

    [string]$Endpoint   = 'http://localhost:8442',
    [string]$Namespace  = 'oasis',
    [string]$Database   = 'oasis',
    [string]$User       = 'root',
    [string]$Pass       = 'oasis-surreal-root',  # Override via env var SURREAL_ROOT_PASS in production
    [switch]$Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Apply env-var override for password
if ($env:SURREAL_ROOT_PASS) {
    $Pass = $env:SURREAL_ROOT_PASS
}

# ── Detect container runtime (docker preferred, podman fallback) ─────────────
# Mirrors the detection in scripts/surrealdb/start-test-container.ps1 so
# podman-only hosts (e.g. CLOSEOUT Stream E G5 gate runner) Just Work.
function Find-ContainerRuntime {
    if (Get-Command docker -ErrorAction SilentlyContinue) {
        try {
            $null = docker version 2>&1
            if ($LASTEXITCODE -eq 0) { return 'docker' }
        } catch { }
    }
    if (Get-Command podman -ErrorAction SilentlyContinue) {
        try {
            $null = podman version 2>&1
            if ($LASTEXITCODE -eq 0) { return 'podman' }
        } catch { }
    }
    Write-Error "No container runtime found. Install Docker Desktop or Podman."
    exit 1
}
$ContainerRuntime = Find-ContainerRuntime

# Validate input file exists
if (-not (Test-Path -LiteralPath $InputPath -PathType Leaf)) {
    Write-Error "Input file not found or is not a file: $InputPath"
    exit 1
}

$AbsInput = (Resolve-Path -LiteralPath $InputPath).Path

# Confirmation prompt unless -Force
if (-not $Force) {
    Write-Host ""
    Write-Host "WARNING: this will replay statements into NS=$Namespace DB=$Database."
    Write-Host "  Endpoint : $Endpoint"
    Write-Host "  File     : $AbsInput"
    Write-Host ""
    $Confirmation = Read-Host "Continue? (yes/NO)"
    if ($Confirmation -ne 'yes') {
        Write-Host "Aborted."
        exit 0
    }
}

Write-Host "Restoring $Namespace/$Database ..."
Write-Host "  Endpoint  : $Endpoint"
Write-Host "  Input     : $AbsInput"
Write-Host "  Runtime   : $ContainerRuntime"

# Pipe file contents through stdin to {docker|podman} exec -i
# surreal import with '-' reads from stdin
$importArgs = @(
    'exec', '-i', 'oasis-surrealdb',
    'surreal', 'import',
    '--conn',      $Endpoint,
    '--user',      $User,
    '--pass',      $Pass,
    '--ns',        $Namespace,
    '--db',        $Database,
    '-'
)

Get-Content -LiteralPath $AbsInput -Raw | & $ContainerRuntime @importArgs
$ExitCode = $LASTEXITCODE

if ($ExitCode -ne 0) {
    Write-Error "surreal import failed (exit $ExitCode)."
    exit $ExitCode
}

Write-Host ""
Write-Host "Restored from: $AbsInput"
exit 0
