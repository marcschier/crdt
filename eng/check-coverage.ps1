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

if (-not (Test-Path $CoverageFile -PathType Leaf))
{
    Write-Host "Coverage file not found: $CoverageFile" -ForegroundColor Red
    exit 1
}

[xml]$report = Get-Content $CoverageFile
foreach ($attribute in @('line-rate', 'branch-rate', 'lines-valid', 'lines-covered'))
{
    if ([string]::IsNullOrWhiteSpace($report.coverage.$attribute))
    {
        Write-Host "Coverage report is missing '$attribute': $CoverageFile" -ForegroundColor Red
        exit 1
    }
}

$culture = [System.Globalization.CultureInfo]::InvariantCulture
$lineRate = [double]::Parse($report.coverage.'line-rate', $culture)
$branchRate = [double]::Parse($report.coverage.'branch-rate', $culture)
$linesValid = [long]::Parse($report.coverage.'lines-valid', $culture)
$linesCovered = [long]::Parse($report.coverage.'lines-covered', $culture)

foreach ($rate in @($lineRate, $branchRate, $MinLineRate))
{
    if ([double]::IsNaN($rate) -or [double]::IsInfinity($rate) -or $rate -lt 0 -or $rate -gt 1)
    {
        Write-Host "Coverage rates must be finite fractions between 0 and 1." -ForegroundColor Red
        exit 1
    }
}

if ($linesValid -le 0 -or $linesCovered -lt 0 -or $linesCovered -gt $linesValid)
{
    Write-Host "Coverage report must contain a nonempty, valid line count." -ForegroundColor Red
    exit 1
}

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
