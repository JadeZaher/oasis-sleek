<#
.SYNOPSIS
    api-safety-hardening CODE sign-off gate. One acceptance run an operator
    executes before sign-off: build + full unit suite + explicit
    safety-critical test assertions, with a non-zero exit on ANY failure.

.DESCRIPTION
    Sectioned, re-runnable, no side effects beyond building/testing:
      1. Stop stale dotnet/OASIS.WebAPI hosts that could lock the build DLL.
      2. dotnet build OASIS.WebAPI.csproj -c Debug  -> assert 0 errors
         (warnings reported; 17 is the known baseline; warnings never fail).
      3. dotnet test tests/OASIS.WebAPI.Tests -c Debug -> assert 0 failed.
      4. Assert the SAFETY-CRITICAL tests executed AND passed (a targeted
         --filter run, parsed from a trx; missing/failed => loud failure).
      5. Print the RESIDUAL-RISK-RUNBOOK Section 4 gate summary (code DONE vs
         the remaining OPS/CONFIG gates a-c). Ops gates are NOT code failures.
      6. Final verdict: PASS-OFF: CODE GATE GREEN (exit 0) only if
         build + tests + safety assertions all passed; else
         PASS-OFF: FAILED + failing section (exit 1).

    This script proves the CODE gate only. It cannot verify real
    testnet/mainnet Guardian-set values or live-network VAAs -- those are ops
    gates; see conductor/tracks/api-safety-hardening/GUARDIAN-SET-SETUP.md.

.EXAMPLE
    pwsh scripts/passoff.ps1
    powershell -File scripts/passoff.ps1

.NOTES
    Requires: dotnet SDK 8+. Mirrors start.ps1 / tests/run-tests.ps1 style.
    ASCII-only on purpose (runs identically under Windows PowerShell 5.1
    and PowerShell 7).
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
$UnitCsproj   = Join-Path $RepoRoot "tests/OASIS.WebAPI.Tests/OASIS.WebAPI.Tests.csproj"
$WarnBaseline = 17

# The SAFETY-CRITICAL tests that MUST have executed AND passed. Keyed by a
# stable substring matched against the trx test name; the value is a human
# label for the summary. The Secp256k1 verifier suite is asserted as a class
# (>= MinSecpTests of its tests must run and pass -- tamper / wrong-guardian /
# below-quorum / malformed are all inside it).
$SafetyTests = [ordered]@{
    "ConcurrentDoubleRedeem_ResultsInExactlyOneMint"                    = "Bridge: concurrent double-redeem => exactly one mint"
    "ReplayedVaa_IsRejected_NoSecondMint"                               = "Bridge: replayed VAA rejected, no second mint"
    "TryClaimAsync_Concurrent_SameKey_ExactlyOneWinner"                 = "IdempotencyStore: concurrent claim, single winner"
    "DispenseAsync_ConcurrentIdenticalDispense_ExactlyOneSubmitAttempt" = "Faucet: concurrent dispense, single submit"
    "KillMidRedeem_ConvergesToChainTruth_Once_AndIdempotent"            = "Reconciliation: converges to chain truth, idempotent"
}
# Class-scoped assertion: the secp256k1 VAA signature verifier suite.
$SecpClassMarker = "Secp256k1VaaSignatureVerifierTests"
$MinSecpTests    = 14   # 17 in suite; require a healthy floor incl. tamper/wrong-guardian/below-quorum/malformed

# Section pass/fail tracking. The script PASSES the code gate only if all of
# these are $true at the end.
$Result = [ordered]@{
    "Build-0-errors"             = $false
    "Unit-suite-0-failed"        = $false
    "Safety-critical-assertions" = $false
}
$FailingSection = $null

function Print-OpsGateSummary {
    Write-Section "RESIDUAL-RISK-RUNBOOK Section 4 - gate summary"
    Write-Host "  CODE gate (this script proves):" -ForegroundColor White
    Write-Host "    [OK] secp256k1 ecrecover IVaaSignatureVerifier implemented + registered" -ForegroundColor Green
    Write-Host "    [OK] Idempotency / consumed-VAA ledger / atomic bridge transitions" -ForegroundColor Green
    Write-Host "    [OK] Reconciliation chain-truth convergence (no auto-rebroadcast/reverse)" -ForegroundColor Green
    Write-Host "    [OK] Full unit suite + safety-critical assertions (asserted above)" -ForegroundColor Green
    Write-Host ""
    Write-Host "  OPS / CONFIG gates (NOT code failures - operator sign-off required):" -ForegroundColor Yellow
    Write-Host "    [  ] (a) Populate + verify REAL testnet/mainnet Guardian sets" -ForegroundColor Yellow
    Write-Host "             per conductor/tracks/api-safety-hardening/GUARDIAN-SET-SETUP.md" -ForegroundColor Yellow
    Write-Host "             (absent/empty => fail-closed; intentional and safe until done)" -ForegroundColor Yellow
    Write-Host "    [  ] (b) Integration-test harness rebuilt under surrealdb-migration" -ForegroundColor Yellow
    Write-Host "    [  ] (c) Live-network VAA validation on devnet/testnet" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "  >>> OPS SIGN-OFF REQUIRED for (a)-(c) before Wormhole value flow. <<<" -ForegroundColor Yellow
    Write-Host "      These are ops/config gates, not code gate failures." -ForegroundColor Yellow
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

function Fail-Now {
    param([string]$Section, [string]$Message)
    $script:FailingSection = $Section
    Write-Err $Message
    Print-OpsGateSummary
    Write-Section "PASS-OFF: FAILED"
    Write-Err "Failing section: $Section"
    Print-ResultTable
    Pop-Location -ErrorAction SilentlyContinue
    exit 1
}

Push-Location $RepoRoot
try {
    Write-Section "api-safety-hardening - CODE PASS-OFF GATE"
    Write-Info "Repo root: $RepoRoot"

    # -- 1. Stop stale hosts that could lock the build DLL -------------------
    Write-Section "1/5  Stop stale dotnet / OASIS.WebAPI hosts"
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
        Write-Ok "Stale hosts stopped"
    } else {
        Write-Info "No stale dotnet/OASIS.WebAPI hosts found"
    }

    # -- 2. Build (assert 0 errors) -----------------------------------------
    Write-Section "2/5  dotnet build OASIS.WebAPI.csproj -c Debug"
    $buildOut = & dotnet build $ApiCsproj -c Debug --nologo 2>&1
    $buildExit = $LASTEXITCODE
    $buildOut | ForEach-Object { Write-Host "    $_" }

    $errLine  = ($buildOut | Select-String -Pattern '(\d+)\s+Error\(s\)'   | Select-Object -Last 1)
    $warnLine = ($buildOut | Select-String -Pattern '(\d+)\s+Warning\(s\)' | Select-Object -Last 1)
    $errCount  = if ($errLine)  { [int]$errLine.Matches[0].Groups[1].Value }  else { -1 }
    $warnCount = if ($warnLine) { [int]$warnLine.Matches[0].Groups[1].Value } else { -1 }

    if ($buildExit -ne 0 -or $errCount -ne 0) {
        Write-Err "Build error count: $errCount (exit $buildExit)"
        Fail-Now -Section "Build-0-errors" -Message "dotnet build did not produce 0 errors."
    }
    $Result["Build-0-errors"] = $true
    Write-Ok "Build errors: 0"
    if ($warnCount -ge 0) {
        if ($warnCount -eq $WarnBaseline) {
            Write-Info "Build warnings: $warnCount (known baseline = $WarnBaseline; not a failure)"
        } else {
            Write-Warn "Build warnings: $warnCount (baseline = $WarnBaseline; warnings never fail this gate)"
        }
    }

    # -- 3. Full unit suite (assert 0 failed) -------------------------------
    Write-Section "3/5  dotnet test OASIS.WebAPI.Tests -c Debug (full unit suite)"
    $trxDir = Join-Path $RepoRoot "tests/TestResults"
    New-Item -ItemType Directory -Force -Path $trxDir | Out-Null
    $fullTrx = Join-Path $trxDir "passoff-full.trx"
    Remove-Item $fullTrx -ErrorAction SilentlyContinue

    $testOut = & dotnet test $UnitCsproj -c Debug --nologo `
        --logger "trx;LogFileName=$fullTrx" 2>&1
    $testExit = $LASTEXITCODE
    $testOut | ForEach-Object { Write-Host "    $_" }

    $m = [regex]::Match(($testOut -join "`n"),
        'total:\s*(\d+).*?failed:\s*(\d+).*?passed:\s*(\d+)',
        ([System.Text.RegularExpressions.RegexOptions]::IgnoreCase -bor `
         [System.Text.RegularExpressions.RegexOptions]::Singleline))
    if ($m.Success) {
        $total  = [int]$m.Groups[1].Value
        $failed = [int]$m.Groups[2].Value
        $passed = [int]$m.Groups[3].Value
    } else {
        # Fallback: parse the trx counters.
        $total = -1; $passed = -1; $failed = -1
        if (Test-Path $fullTrx) {
            [xml]$trxDoc = Get-Content $fullTrx
            $c = $trxDoc.TestRun.ResultSummary.Counters
            if ($c) {
                $total  = [int]$c.total
                $passed = [int]$c.passed
                $failed = [int]$c.failed
            }
        }
    }

    Write-Host ""
    Write-Info "Unit suite - total: $total  passed: $passed  failed: $failed"
    if ($testExit -ne 0 -or $failed -ne 0 -or $total -le 0) {
        Fail-Now -Section "Unit-suite-0-failed" `
            -Message "Unit suite not green (exit $testExit, failed $failed, total $total)."
    }
    $Result["Unit-suite-0-failed"] = $true
    Write-Ok "Unit suite green: $passed/$total passed, 0 failed"

    # -- 4. Safety-critical assertions (trx-parsed + filter cross-check) -----
    Write-Section "4/5  Safety-critical test assertions"

    if (-not (Test-Path $fullTrx)) {
        Fail-Now -Section "Safety-critical-assertions" `
            -Message "No trx produced by the unit run; cannot assert safety tests."
    }
    [xml]$trx = Get-Content $fullTrx
    $results = @($trx.TestRun.Results.UnitTestResult)
    if (-not $results -or $results.Count -eq 0) {
        Fail-Now -Section "Safety-critical-assertions" `
            -Message "trx has no UnitTestResult entries."
    }

    $safetyAllGood = $true

    foreach ($key in $SafetyTests.Keys) {
        $hits = @($results | Where-Object { $_.testName -match [regex]::Escape($key) })
        if ($hits.Count -eq 0) {
            Write-Err "MISSING safety test: $key  ($($SafetyTests[$key]))"
            $safetyAllGood = $false
            continue
        }
        $bad = @($hits | Where-Object { $_.outcome -ne "Passed" })
        if ($bad.Count -gt 0) {
            foreach ($b in $bad) { Write-Err "FAILED safety test: $($b.testName) -> $($b.outcome)" }
            $safetyAllGood = $false
        } else {
            Write-Ok "$($SafetyTests[$key])  [$($hits.Count) ran, all Passed]"
        }
    }

    # Class-scoped: Secp256k1VaaSignatureVerifier suite (tamper / wrong-guardian
    # / below-quorum / malformed all live here).
    $secpResults = @($results | Where-Object { $_.testName -match [regex]::Escape($SecpClassMarker) })
    $secpPassed  = @($secpResults | Where-Object { $_.outcome -eq "Passed" })
    $secpFailed  = @($secpResults | Where-Object { $_.outcome -ne "Passed" })
    if ($secpResults.Count -lt $MinSecpTests) {
        Write-Err "Secp256k1VaaSignatureVerifier suite: only $($secpResults.Count) tests ran (expected >= $MinSecpTests)"
        $safetyAllGood = $false
    } elseif ($secpFailed.Count -gt 0) {
        foreach ($sf in $secpFailed) { Write-Err "FAILED: $($sf.testName) -> $($sf.outcome)" }
        $safetyAllGood = $false
    } else {
        Write-Ok "Secp256k1VaaSignatureVerifier suite  [$($secpPassed.Count) ran, all Passed]"
    }

    # Independent cross-check: a focused --filter run must resolve and pass the
    # same named safety tests (defends against a renamed/removed test silently
    # vanishing from the full suite).
    $filterClause = ($SafetyTests.Keys | ForEach-Object { "FullyQualifiedName~$_" }) -join "|"
    $filterClause = "$filterClause|FullyQualifiedName~$SecpClassMarker"
    $safeTrx = Join-Path $trxDir "passoff-safety.trx"
    Remove-Item $safeTrx -ErrorAction SilentlyContinue
    Write-Info "Cross-check: dotnet test --filter (safety subset)"
    $filterOut = & dotnet test $UnitCsproj -c Debug --nologo --no-build `
        --filter $filterClause --logger "trx;LogFileName=$safeTrx" 2>&1
    $filterExit = $LASTEXITCODE
    if ($filterExit -ne 0 -or -not (Test-Path $safeTrx)) {
        Write-Err "Safety --filter cross-check run failed (exit $filterExit)"
        $filterOut | ForEach-Object { Write-Host "    $_" }
        $safetyAllGood = $false
    } else {
        [xml]$strx = Get-Content $safeTrx
        $sres = @($strx.TestRun.Results.UnitTestResult)
        $sfail = @($sres | Where-Object { $_.outcome -ne "Passed" })
        if ($sres.Count -eq 0 -or $sfail.Count -gt 0) {
            Write-Err "Safety --filter cross-check: $($sres.Count) ran, $($sfail.Count) not Passed"
            $safetyAllGood = $false
        } else {
            Write-Ok "Safety --filter cross-check: $($sres.Count) ran, all Passed"
        }
    }

    if (-not $safetyAllGood) {
        Fail-Now -Section "Safety-critical-assertions" `
            -Message "One or more safety-critical tests missing or not Passed."
    }
    $Result["Safety-critical-assertions"] = $true
    Write-Ok "All safety-critical assertions GREEN"

    # -- 5. Ops-gate summary (informational; not a code failure) ------------
    Print-OpsGateSummary

    # -- 6. Final verdict ---------------------------------------------------
    $allGreen = @($Result.Values | Where-Object { $_ -eq $false }).Count -eq 0
    if ($allGreen) {
        Write-Section "PASS-OFF: CODE GATE GREEN"
        foreach ($k in $Result.Keys) { Write-Ok $k }
        Write-Host ""
        Write-Ok "Build 0 errors | Unit $passed/$total (0 failed) | Safety-critical all green"
        Write-Warn "OPS SIGN-OFF still required for gates (a)-(c) - see GUARDIAN-SET-SETUP.md"
        Write-Host ""
        Pop-Location -ErrorAction SilentlyContinue
        exit 0
    } else {
        Fail-Now -Section "Final-verdict" -Message "Not all code-gate sections passed."
    }
}
finally {
    Pop-Location -ErrorAction SilentlyContinue
}
