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
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install-PortfolioSaverScreensaver.ps1"
endlocal
