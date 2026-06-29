REM ============================================================================
REM Copyright (c) 2026 Supratim Sanyal of SANYALnet Labs.
REM Proprietary rights reserved except as expressly licensed herein.
REM
REM DO NOT PANIC PORTFOLIO VISUALIZER
REM This file is governed by the SANYALnet Labs Non-Commercial License in the
REM root LICENSE file. Non-Commercial use is permitted; Commercial Use and use
REM for AI/ML model training are prohibited unless separately authorized.
REM
REM Attribution is required: "Based on original work by Supratim Sanyal of
REM SANYALnet Labs." See LICENSE for full terms, warranty disclaimer, termination,
REM patent, trademark, and governing-law provisions.
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
