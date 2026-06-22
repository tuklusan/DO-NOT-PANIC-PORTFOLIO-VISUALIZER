REM ============================================================================
REM Copyright (c) 2026 Supratim Sanyal of SANYALnet Labs.
REM Proprietary rights reserved except as expressly licensed herein.
REM
REM DO NOT PANIC PORTFOLIO VIEWER
REM This software and its derivatives are licensed for STRICTLY NON-COMMERCIAL,
REM personal, educational, or hobbyist use only. Commercial exploitation,
REM corporate internal operations, or AI model training are strictly forbidden.
REM
REM ATTRIBUTION & DEPENDENCIES: This application incorporates the YFinance library,
REM which is licensed under the Apache License, Version 2.0. A copy of the Apache
REM License is provided within the distribution environment.
REM
REM FINANCIAL DISCLAIMER: This software is a passive visualization tool only.
REM It does not provide financial, investment, legal, or tax advice. All data
REM calculation and scraping outputs are provided 'AS IS' with zero guarantee
REM of real-time accuracy or upstream availability.
REM
REM This file is subject to the terms and conditions defined in the LICENSE
REM file located in the root directory of this source code repository.
REM Removal or modification of this legal notice constitutes copyright infringement.
REM ============================================================================
@echo off
setlocal

set "WORKSPACE=C:\Users\WDAGUtilityAccount\Desktop\PortfolioSaverWorkspace"
set "MODE=%~1"
if "%MODE%"=="" set "MODE=Online"

cd /d "%WORKSPACE%"

set "BOOTSTRAP_DIR=%TEMP%\PortfolioSaverSandboxBootstrap"
set "BOOTSTRAP_LOG=%BOOTSTRAP_DIR%\bootstrap-%MODE%.log"
set "WORKSPACE_BOOTSTRAP_DIR=%WORKSPACE%\build\sandbox\results"
set "WORKSPACE_BOOTSTRAP_LOG=%WORKSPACE_BOOTSTRAP_DIR%\bootstrap-%MODE%.log"
if not exist "%BOOTSTRAP_DIR%" mkdir "%BOOTSTRAP_DIR%"
if not exist "%WORKSPACE_BOOTSTRAP_DIR%" mkdir "%WORKSPACE_BOOTSTRAP_DIR%"

echo [%DATE% %TIME%] Launching sandbox UI validation in %MODE% mode.>"%BOOTSTRAP_LOG%"
echo [%DATE% %TIME%] Working directory is %CD%.>>"%BOOTSTRAP_LOG%"
echo [%DATE% %TIME%] Launching sandbox UI validation in %MODE% mode.>"%WORKSPACE_BOOTSTRAP_LOG%"
echo [%DATE% %TIME%] Working directory is %CD%.>>"%WORKSPACE_BOOTSTRAP_LOG%"

if /I "%MODE%"=="Offline" (
  echo [%DATE% %TIME%] Disabling network adapters for offline validation.>>"%BOOTSTRAP_LOG%"
  powershell.exe -ExecutionPolicy Bypass -Command "Get-NetAdapter -ErrorAction SilentlyContinue ^| Where-Object { $_.Status -eq 'Up' } ^| Disable-NetAdapter -Confirm:\$false -ErrorAction SilentlyContinue" >>"%BOOTSTRAP_LOG%" 2>&1
)

powershell.exe -ExecutionPolicy Bypass -File "%WORKSPACE%\build\sandbox\Run-PortfolioSaverSandboxUiValidation.ps1" -Mode %MODE% >>"%BOOTSTRAP_LOG%" 2>&1
type "%BOOTSTRAP_LOG%" > "%WORKSPACE_BOOTSTRAP_LOG%"

echo [%DATE% %TIME%] Sandbox UI validation process exited with code %ERRORLEVEL%.>>"%BOOTSTRAP_LOG%"
echo [%DATE% %TIME%] Sandbox UI validation process exited with code %ERRORLEVEL%.>>"%WORKSPACE_BOOTSTRAP_LOG%"
exit /b %ERRORLEVEL%
