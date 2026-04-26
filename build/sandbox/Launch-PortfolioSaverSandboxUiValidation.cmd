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
