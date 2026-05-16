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
Push-Location $RepoRoot
try {
    if ($Mutation) {
        Write-Host "==> Stryker.NET mutation testing (output: tests/StrykerOutput)" -ForegroundColor Cyan
        dotnet stryker --output tests/StrykerOutput
        exit $LASTEXITCODE
    }

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
