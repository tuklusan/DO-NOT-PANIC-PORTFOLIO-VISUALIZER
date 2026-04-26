# PortfolioSaver Installer Sandbox Test

1. Double-click `PortfolioSaverInstallerTest.wsb`.
2. In Windows Sandbox, open `C:\Users\WDAGUtilityAccount\Desktop\PortfolioSaverWorkspace\build\artifacts`.
3. Run `PortfolioSaverScreensaverSetup.exe`.
4. Accept the UAC prompt and let the installer finish.
5. Open Screen Saver Settings and verify `PortfolioSaver.Screensaver` appears in the list.
6. In PowerShell inside the sandbox, run:

```powershell
powershell -ExecutionPolicy Bypass -File C:\Users\WDAGUtilityAccount\Desktop\PortfolioSaverWorkspace\build\sandbox\Validate-PortfolioSaverState.ps1 -ExpectedState Installed
```

7. Uninstall it from Programs and Features, or run:

```powershell
powershell -ExecutionPolicy Bypass -File "C:\ProgramData\PortfolioSaverScreensaver\Uninstall-PortfolioSaverScreensaver.ps1"
```

8. Run the validator again:

```powershell
powershell -ExecutionPolicy Bypass -File C:\Users\WDAGUtilityAccount\Desktop\PortfolioSaverWorkspace\build\sandbox\Validate-PortfolioSaverState.ps1 -ExpectedState Uninstalled
```

9. Confirm the screen saver no longer appears after reopening Screen Saver Settings.

Notes:
- Windows Sandbox is available on supported Pro, Enterprise, and Education editions.
- The mapped workspace is read-only inside the sandbox, which is fine for installer testing.
