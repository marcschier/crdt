#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.
<#
.SYNOPSIS
    Fails if any tracked C# source line exceeds the configured maximum length.

.DESCRIPTION
    Mirrors the `max_line_length` guideline in .editorconfig as a hard CI gate.
    Scans *.cs files under src/, tests/, and samples/, excluding generated and
    build-output files.

.PARAMETER MaxLength
    Maximum allowed line length (default: 120).

.PARAMETER Root
    Repository root to scan (default: the repo root relative to this script).
#>
[CmdletBinding()]
param(
    [int]$MaxLength = 120,
    [string]$Root = (Resolve-Path (Join-Path $PSScriptRoot '..'))
)

$ErrorActionPreference = 'Stop'

$folders = @('src', 'tests', 'samples') |
    ForEach-Object { Join-Path $Root $_ } |
    Where-Object { Test-Path $_ }

$violations = New-Object System.Collections.Generic.List[string]

foreach ($folder in $folders)
{
    $files = Get-ChildItem -Path $folder -Recurse -File -Filter '*.cs' |
        Where-Object {
            $_.FullName -notmatch '[\\/](bin|obj)[\\/]' -and
            $_.Name -notmatch '\.(g|g\.i|Designer)\.cs$'
        }

    foreach ($file in $files)
    {
        $lineNumber = 0
        foreach ($line in [System.IO.File]::ReadLines($file.FullName))
        {
            $lineNumber++
            if ($line.Length -gt $MaxLength)
            {
                $rel = $file.FullName.Substring($Root.Path.Length).TrimStart('\', '/')
                $violations.Add(("{0}:{1}: {2} chars (max {3})" -f $rel, $lineNumber, $line.Length, $MaxLength))
            }
        }
    }
}

if ($violations.Count -gt 0)
{
    Write-Host "Line-length violations (max $MaxLength):" -ForegroundColor Red
    $violations | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
    Write-Host ("`n{0} violation(s) found." -f $violations.Count) -ForegroundColor Red
    exit 1
}

Write-Host "Line-length check passed (max $MaxLength)." -ForegroundColor Green
exit 0
