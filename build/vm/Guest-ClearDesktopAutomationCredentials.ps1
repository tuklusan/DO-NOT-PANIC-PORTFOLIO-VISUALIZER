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
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$winlogonPath = 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon'

Remove-ItemProperty -Path $winlogonPath -Name DefaultPassword -ErrorAction SilentlyContinue
Remove-ItemProperty -Path $winlogonPath -Name DefaultUserName -ErrorAction SilentlyContinue
Set-ItemProperty -Path $winlogonPath -Name AutoAdminLogon -Value '0'

$state = Get-ItemProperty -Path $winlogonPath
[pscustomobject]@{
    AutoAdminLogon = $state.AutoAdminLogon
    DefaultPasswordPresent = $null -ne $state.PSObject.Properties['DefaultPassword']
} | ConvertTo-Json -Compress
