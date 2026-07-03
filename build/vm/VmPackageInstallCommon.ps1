# ============================================================================
# Copyright (c) 2026 Supratim Sanyal of SANYALnet Labs.
# Proprietary rights reserved except as expressly licensed herein.
#
# DO NOT PANIC PORTFOLIO VISUALIZER
# This file is governed by the SANYALnet Labs Non-Commercial License in the
# root LICENSE file. Non-Commercial use is permitted; Commercial Use and use
# for AI/ML model training are prohibited unless separately authorized.
#
# Attribution is required: "Based on original work by Supratim Sanyal of
# SANYALnet Labs." See LICENSE for full terms, warranty disclaimer, termination,
# patent, trademark, and governing-law provisions.
# ============================================================================

Set-StrictMode -Version Latest

function Invoke-DnppvCommandWithRetry {
    param(
        [Parameter(Mandatory = $true)]
        [scriptblock]$ScriptBlock,
        [int]$MaxAttempts = 3,
        [int]$DelaySeconds = 5,
        [string]$Operation = 'operation',
        [Parameter(Mandatory = $true)]
        [bool]$CheckLastExitCode,
        [scriptblock]$WarningSink
    )

    for ($attempt = 1; $attempt -le $MaxAttempts; $attempt++) {
        try {
            $previousErrorActionPreference = $ErrorActionPreference
            $ErrorActionPreference = 'Stop'
            try {
                $global:LASTEXITCODE = 0
                & $ScriptBlock
            }
            finally {
                $ErrorActionPreference = $previousErrorActionPreference
            }
            if (-not $CheckLastExitCode -or $LASTEXITCODE -eq 0) { return }
            throw "$Operation exited with $LASTEXITCODE."
        }
        catch {
            if ($attempt -ge $MaxAttempts) { throw }
            $warning = "{0} failed on attempt {1} of {2}: {3}" -f $Operation, $attempt, $MaxAttempts, $_.Exception.Message
            Write-Warning $warning
            if ($null -ne $WarningSink) {
                & $WarningSink $warning
            }
            # Linear backoff keeps VM bootstrap predictable while avoiding immediate retry storms.
            Start-Sleep -Seconds ([Math]::Min(30, $DelaySeconds * $attempt))
        }
    }
}

function Test-DnppvChocoPackageInstalled {
    param(
        [Parameter(Mandatory = $true)]
        [string]$PackageName,
        [string]$ChocoPath = 'choco.exe'
    )

    $chocoExecutable = $null
    $chocoCommand = Get-Command $ChocoPath -ErrorAction SilentlyContinue
    if ($null -ne $chocoCommand) {
        $chocoExecutable = $chocoCommand.Source
    }
    elseif ((Test-Path -LiteralPath $ChocoPath) -and [string]::Equals([System.IO.Path]::GetExtension($ChocoPath), '.exe', [StringComparison]::OrdinalIgnoreCase)) {
        $chocoExecutable = $ChocoPath
    }
    else {
        return $false
    }

    try {
        $line = & $chocoExecutable list --local-only --exact --limit-output $PackageName 2>$null | Select-Object -First 1
        return ($line -match ('^{0}\|' -f [regex]::Escape($PackageName)))
    }
    catch {
        Write-Verbose "Unable to query choco package '$PackageName'; treating as not installed. $($_.Exception.Message)"
        return $false
    }
}

function Install-DnppvChocoPackage {
    param(
        [Parameter(Mandatory = $true)]
        [string]$PackageName,
        [string]$ChocoPath = 'choco.exe'
    )

    if (Test-DnppvChocoPackageInstalled -PackageName $PackageName -ChocoPath $ChocoPath) {
        Write-Host "Skipping already-installed choco package: $PackageName"
        return 'present'
    }

    Invoke-DnppvCommandWithRetry -Operation "choco install $PackageName" -CheckLastExitCode $true -ScriptBlock {
        & $ChocoPath install $PackageName -y --no-progress --limit-output
    }

    return 'installed'
}
