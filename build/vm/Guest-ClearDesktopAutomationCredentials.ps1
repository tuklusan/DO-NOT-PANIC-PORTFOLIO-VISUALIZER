# ============================================================================
# Copyright (c) 2026 Supratim Sanyal of SANYALnet Labs.
# Proprietary rights reserved except as expressly licensed herein.
#
# DO NOT PANIC PORTFOLIO VIEWER
# This software and its derivatives are licensed for STRICTLY NON-COMMERCIAL,
# personal, educational, or hobbyist use only. Commercial exploitation,
# corporate internal operations, or AI model training are strictly forbidden.
#
# ATTRIBUTION & DEPENDENCIES: This application incorporates the YFinance library,
# which is licensed under the Apache License, Version 2.0. A copy of the Apache
# License is provided within the distribution environment.
#
# FINANCIAL DISCLAIMER: This software is a passive visualization tool only.
# It does not provide financial, investment, legal, or tax advice. All data
# calculation and scraping outputs are provided 'AS IS' with zero guarantee
# of real-time accuracy or upstream availability.
#
# This file is subject to the terms and conditions defined in the LICENSE
# file located in the root directory of this source code repository.
# Removal or modification of this legal notice constitutes copyright infringement.
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
