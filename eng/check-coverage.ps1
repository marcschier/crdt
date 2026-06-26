#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.
<#
.SYNOPSIS
    Enforces a minimum line-coverage threshold from a Cobertura coverage report.

.PARAMETER CoverageFile
    Path to the Cobertura XML report.

.PARAMETER MinLineRate
    Minimum acceptable line coverage as a fraction (default: 0.85).
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$CoverageFile,
    [double]$MinLineRate = 0.85
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path $CoverageFile))
{
    Write-Host "Coverage file not found: $CoverageFile" -ForegroundColor Red
    exit 1
}

[xml]$report = Get-Content $CoverageFile
$lineRate = [double]$report.coverage.'line-rate'
$branchRate = [double]$report.coverage.'branch-rate'

Write-Host ("Line coverage:   {0:P2}" -f $lineRate)
Write-Host ("Branch coverage: {0:P2}" -f $branchRate)
Write-Host ("Required line coverage: {0:P0}" -f $MinLineRate)

if ($lineRate -lt $MinLineRate)
{
    Write-Host ("Coverage gate FAILED: {0:P2} < {1:P0}" -f $lineRate, $MinLineRate) -ForegroundColor Red
    exit 1
}

Write-Host "Coverage gate passed." -ForegroundColor Green
exit 0
