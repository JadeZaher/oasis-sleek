<#
.SYNOPSIS
    Export a full SurrealDB backup (DDL + data) to a .surql file.

.DESCRIPTION
    Wraps `surreal export` (via docker/podman exec into the oasis-surrealdb
    container) to produce a self-contained replay file containing all table
    definitions and INSERT statements for the target namespace and database.

    - Auto-detects docker (preferred) or podman as the container runtime.
    - Creates the output directory if it does not exist.
    - Prefers the SURREAL_ROOT_PASS environment variable over the -Pass
      parameter so secrets never need to be passed on the command line in CI.
    - Prints the absolute output path and file size on success.

.PARAMETER OutputPath
    Path to write the backup file. Defaults to
    ./backups/oasis-<yyyyMMdd-HHmmss>.surql relative to the repository root.

.PARAMETER Endpoint
    HTTP endpoint of the SurrealDB instance. Default: http://localhost:8442.

.PARAMETER Namespace
    SurrealDB namespace to export. Default: oasis.

.PARAMETER Database
    SurrealDB database to export. Default: oasis.

.PARAMETER User
    SurrealDB root username. Default: root.

.PARAMETER Pass
    SurrealDB root password. Default: oasis-surreal-root.
    Override via env var SURREAL_ROOT_PASS in production.

.EXAMPLE
    pwsh scripts/surrealdb/backup.ps1
    # writes ./backups/oasis-20260522-143012.surql

.EXAMPLE
    pwsh scripts/surrealdb/backup.ps1 -OutputPath C:\backups\oasis-manual.surql

.EXAMPLE
    $env:SURREAL_ROOT_PASS = "my-secret"; pwsh scripts/surrealdb/backup.ps1
#>
[CmdletBinding()]
param(
    [string]$OutputPath = '',
    [string]$Endpoint   = 'http://localhost:8442',
    [string]$Namespace  = 'oasis',
    [string]$Database   = 'oasis',
    [string]$User       = 'root',
    [string]$Pass       = 'oasis-surreal-root'  # Override via env var SURREAL_ROOT_PASS in production
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

# Resolve output path
if (-not $OutputPath) {
    $ScriptDir  = Split-Path -Parent $MyInvocation.MyCommand.Path
    $RepoRoot   = Split-Path -Parent (Split-Path -Parent $ScriptDir)
    $Timestamp  = (Get-Date).ToString('yyyyMMdd-HHmmss')
    $OutputPath = Join-Path $RepoRoot "backups\oasis-${Timestamp}.surql"
}

# Ensure output directory exists
$OutputDir = Split-Path -Parent $OutputPath
if ($OutputDir -and -not (Test-Path $OutputDir)) {
    Write-Host "Creating output directory: $OutputDir"
    New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
}

Write-Host "Backing up $Namespace/$Database ..."
Write-Host "  Endpoint  : $Endpoint"
Write-Host "  Output    : $OutputPath"
Write-Host "  Runtime   : $ContainerRuntime"

# {docker|podman} exec: surreal export writes DDL+data to stdout; capture and save to file
$exportArgs = @(
    'exec', 'oasis-surrealdb',
    'surreal', 'export',
    '--conn',  $Endpoint,
    '--user',  $User,
    '--pass',  $Pass,
    '--ns',    $Namespace,
    '--db',    $Database,
    '-'
)

$output = & $ContainerRuntime @exportArgs 2>&1
$ExitCode = $LASTEXITCODE

if ($ExitCode -ne 0) {
    Write-Error "surreal export failed (exit $ExitCode): $output"
    exit $ExitCode
}

[System.IO.File]::WriteAllText($OutputPath, ($output | Out-String))

$AbsPath = (Resolve-Path $OutputPath).Path
$SizeKB  = [math]::Round((Get-Item $AbsPath).Length / 1KB, 1)
Write-Host ""
Write-Host "Backup written: $AbsPath ($SizeKB KB)"
exit 0
