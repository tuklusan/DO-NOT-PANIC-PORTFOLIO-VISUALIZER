// ============================================================================
// Copyright (c) 2026 Supratim Sanyal of SANYALnet Labs.
// Proprietary rights reserved except as expressly licensed herein.
//
// DO NOT PANIC PORTFOLIO VISUALIZER
// This file is governed by the SANYALnet Labs Non-Commercial License in the
// root LICENSE file. Non-Commercial use is permitted; Commercial Use and use
// for AI/ML model training are prohibited unless separately authorized.
//
// Attribution is required: "Based on original work by Supratim Sanyal of
// SANYALnet Labs." See LICENSE for full terms, warranty disclaimer, termination,
// patent, trademark, and governing-law provisions.
// ============================================================================
using Xunit;

namespace PortfolioSaver.Tests.Services;

public sealed class InnoInstallerScriptTests
{
    private static readonly Lazy<string> RepoRoot = new(FindRepoRoot);

    [Fact]
    public void InnoScript_RequiresLicenseAndElevatedProgramFilesInstall()
    {
        string script = ReadRepoText("build", "installer", "DoNotPanicPortfolioVisualizer.iss");

        Assert.Contains("PrivilegesRequired=admin", script, StringComparison.Ordinal);
        Assert.DoesNotContain("PrivilegesRequiredOverridesAllowed", script, StringComparison.Ordinal);
        Assert.Contains("#error AppVersion must be supplied by build/publish-inno-installer.ps1.", script, StringComparison.Ordinal);
        Assert.Contains("LicenseFile={#LicenseFile}", script, StringComparison.Ordinal);
        Assert.Contains(@"DefaultDirName={autopf}\{#AppPublisher}\{#AppFolderName}", script, StringComparison.Ordinal);
        Assert.Contains("#define AppPublisher \"SANYALnet Labs\"", script, StringComparison.Ordinal);
        Assert.Contains("ArchitecturesInstallIn64BitMode=x64compatible", script, StringComparison.Ordinal);
        Assert.Contains("DisableDirPage=yes", script, StringComparison.Ordinal);
        Assert.Contains(@"DefaultGroupName={#AppPublisher}\{#AppName}", script, StringComparison.Ordinal);
        Assert.Contains(@"Source: ""{#SourceRoot}\*""; DestDir: ""{app}""", script, StringComparison.Ordinal);
        Assert.Contains("Cleanup-DoNotPanicPortfolioVisualizer.ps1", script, StringComparison.Ordinal);
        Assert.Contains("-AllUsers", script, StringComparison.Ordinal);
        Assert.Contains("skipifdoesntexist", script, StringComparison.Ordinal);
        Assert.Contains("function InitializeUninstall(): Boolean;", script, StringComparison.Ordinal);
        Assert.Contains("if not UninstallSilent then", script, StringComparison.Ordinal);
        Assert.DoesNotContain("if not WizardSilent then", script, StringComparison.Ordinal);
        Assert.Contains("[UninstallDelete]", script, StringComparison.Ordinal);
        Assert.DoesNotContain(@"Type: files; Name: ""{app}\unins*.exe""", script, StringComparison.Ordinal);
        Assert.Contains(@"Type: dirifempty; Name: ""{app}""", script, StringComparison.Ordinal);
        Assert.Contains(@"Type: dirifempty; Name: ""{autopf}\{#AppPublisher}""", script, StringComparison.Ordinal);
        Assert.Contains("DoNotPanicPortfolioVisualizer for local Windows user profiles", script, StringComparison.Ordinal);
        Assert.Contains("CR-133: the public installer creates a standard all-users desktop shortcut by default.", script, StringComparison.Ordinal);
        Assert.Contains(@"Name: ""desktopicon""; Description: ""Create a desktop shortcut""", script, StringComparison.Ordinal);
        Assert.DoesNotContain(@"Name: ""desktopicon""; Description: ""Create a desktop shortcut""; GroupDescription: ""Additional shortcuts:""; Flags: unchecked", script, StringComparison.Ordinal);
        Assert.Contains(@"Name: ""{group}\DO NOT PANIC PORTFOLIO VISUALIZER""; Filename: ""{app}\PortfolioSaver.Desktop.exe""", script, StringComparison.Ordinal);
        Assert.Contains(@"Name: ""{group}\Settings""; Filename: ""{app}\PortfolioSaver.Config.exe""", script, StringComparison.Ordinal);
        Assert.Contains(@"Name: ""{group}\License""; Filename: ""{app}\LICENSE""", script, StringComparison.Ordinal);
        Assert.Contains(@"Name: ""{autodesktop}\DO NOT PANIC PORTFOLIO VISUALIZER""; Filename: ""{app}\PortfolioSaver.Desktop.exe""; WorkingDir: ""{app}""; Tasks: desktopicon", script, StringComparison.Ordinal);

        string cleanupScript = ReadRepoText("build", "installer", "Cleanup-DoNotPanicPortfolioVisualizer.ps1");
        Assert.Contains("function Test-IsSafeProgramFilesInstallRoot", cleanupScript, StringComparison.Ordinal);
        Assert.Contains("function Start-DelayedInstallRootCleanup", cleanupScript, StringComparison.Ordinal);
        Assert.Contains("ConvertTo-Json -InputObject $Path", cleanupScript, StringComparison.Ordinal);
        Assert.Contains("-EncodedCommand", cleanupScript, StringComparison.Ordinal);
        Assert.Contains("[IO.FileAttributes]::ReparsePoint", cleanupScript, StringComparison.Ordinal);
        Assert.Contains("Start-Sleep -Seconds 5", cleanupScript, StringComparison.Ordinal);
        Assert.Contains("AddSeconds(45)", cleanupScript, StringComparison.Ordinal);
        Assert.Contains("Could not schedule delayed install-root cleanup", cleanupScript, StringComparison.Ordinal);
        Assert.Contains("$normalizedInstallRoot.Equals($normalizedExpectedRoot", cleanupScript, StringComparison.Ordinal);

        string cycleScript = ReadRepoText("build", "installer", "Test-InnoInstallCycle.ps1");
        Assert.Contains("/SUPPRESSMSGBOXES", cycleScript, StringComparison.Ordinal);
        Assert.Contains("#requires -Version 7.0", cycleScript, StringComparison.Ordinal);
        Assert.Contains("ArgumentList.Add", cycleScript, StringComparison.Ordinal);
        Assert.Contains("function Get-InstalledInnoUninstallerPath", cycleScript, StringComparison.Ordinal);
        Assert.Contains("function Wait-InstallRootRemoved", cycleScript, StringComparison.Ordinal);
        Assert.Contains("AddSeconds(90)", cycleScript, StringComparison.Ordinal);
        Assert.Contains("UninstallString", cycleScript, StringComparison.Ordinal);
        Assert.Contains("Could not locate Inno uninstaller path from registry", cycleScript, StringComparison.Ordinal);
        Assert.DoesNotContain(@"Join-Path $uninstallRoot 'unins000.exe'", cycleScript, StringComparison.Ordinal);
        Assert.Contains(@"HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{B0839D4C-1D29-4D9C-95E3-C88E4D8E37E5}_is1", cycleScript, StringComparison.Ordinal);
        Assert.Contains("Inno uninstaller stub still present after uninstall", cycleScript, StringComparison.Ordinal);
        Assert.Contains("Install root still present after uninstall", cycleScript, StringComparison.Ordinal);
        Assert.Contains("Join-Path $repoRoot 'build\\validation\\artifacts\\inno-install-cycle'", cycleScript, StringComparison.Ordinal);
        Assert.Contains("GetFolderPath('CommonPrograms')", cycleScript, StringComparison.Ordinal);
        Assert.Contains("'SANYALnet Labs\\DO NOT PANIC PORTFOLIO VISUALIZER'", cycleScript, StringComparison.Ordinal);
        Assert.Contains("GetFolderPath('CommonDesktopDirectory')", cycleScript, StringComparison.Ordinal);
        Assert.Contains("Expected installed shortcut missing", cycleScript, StringComparison.Ordinal);
        Assert.Contains("Shortcut still present after uninstall", cycleScript, StringComparison.Ordinal);
    }

    [Fact]
    public void InnoPublisher_UsesSafeTempPayloadAndSupportsPerUserIscc()
    {
        string script = ReadRepoText("build", "publish-inno-installer.ps1");

        Assert.Contains("publish-safe-temp.ps1", script, StringComparison.Ordinal);
        Assert.Contains("if (-not $?)", script, StringComparison.Ordinal);
        Assert.Contains("exit code $LASTEXITCODE", script, StringComparison.Ordinal);
        Assert.DoesNotContain("publish-safe-temp.ps1 failed with exit code $LASTEXITCODE", script, StringComparison.Ordinal);
        Assert.Contains(@"$env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'", script, StringComparison.Ordinal);
        Assert.Contains("local-name()=\"PortfolioSaverVersion\"", script, StringComparison.Ordinal);
        Assert.Contains("PortfolioSaver.Desktop.exe", script, StringComparison.Ordinal);
        Assert.Contains("PortfolioSaver.Config.exe", script, StringComparison.Ordinal);
        Assert.Contains("PortfolioSaver.Screensaver.scr", script, StringComparison.Ordinal);
        Assert.Contains("THIRD-PARTY-LICENSES\\APACHE-2.0.txt", script, StringComparison.Ordinal);
        Assert.Contains("$serverRoot = Join-Path $safeTempRoot 'server'", script, StringComparison.Ordinal);
        Assert.Contains("Copy-DirectoryContents -Source $serverRoot -Destination (Join-Path $payloadRoot 'YFinanceServer')", script, StringComparison.Ordinal);
        Assert.Contains("YFinanceServer\\YFinance.NET.Server.dll", script, StringComparison.Ordinal);
        Assert.Contains("New-InstallerLicenseDisplayFile -SourceLicensePath $licensePath -DestinationLicensePath $installerLicensePath", script, StringComparison.Ordinal);
        Assert.Contains("/DLicenseFile=$installerLicensePath", script, StringComparison.Ordinal);
        Assert.Contains("LICENSE-INSTALLER-DISPLAY.txt", script, StringComparison.Ordinal);
        Assert.Contains("$warrantyPattern", script, StringComparison.Ordinal);
        Assert.Contains("^7\\. No Warranty\\.", script, StringComparison.Ordinal);
        Assert.Contains("'7. No Warranty. '", script, StringComparison.Ordinal);
        Assert.DoesNotContain("OUT OF OR IN CONNECTION WITH THE SOFTWARE", script, StringComparison.Ordinal);
        Assert.Contains("Installer license display workaround did not find the warranty paragraph", script, StringComparison.Ordinal);
        Assert.Contains("-Encoding UTF8BOM", script, StringComparison.Ordinal);
        Assert.Contains("https://timestamp.digicert.com", script, StringComparison.Ordinal);
        Assert.Contains("Code-signing timestamp URL must be absolute HTTPS and must not be empty.", script, StringComparison.Ordinal);
        Assert.Contains("Code-signing timestamp URL must be absolute HTTPS", script, StringComparison.Ordinal);
        Assert.Contains("DNPPV_CODESIGN_THUMBPRINT", script, StringComparison.Ordinal);
        Assert.Contains("DNPPV_CODESIGN_EXPECTED_CN", script, StringComparison.Ordinal);
        Assert.Contains("Resolve-SignTool", script, StringComparison.Ordinal);
        Assert.Contains("Get-CertificateSubjectCommonName", script, StringComparison.Ordinal);
        Assert.Contains("signtool.exe", script, StringComparison.Ordinal);
        Assert.Contains("@('x64', 'x86')", script, StringComparison.Ordinal);
        Assert.DoesNotContain("-Recurse -Filter signtool.exe", script, StringComparison.Ordinal);
        Assert.Contains("Get-AuthenticodeSignature", script, StringComparison.Ordinal);
        Assert.Contains("Signed setup thumbprint did not match requested signing certificate", script, StringComparison.Ordinal);
        Assert.Contains("[string]::Equals($actualCommonName, $ExpectedCommonName", script, StringComparison.Ordinal);
        Assert.Contains("INNO_SETUP_UNSIGNED", script, StringComparison.Ordinal);
        Assert.Contains("INNO_SETUP_SIGNED", script, StringComparison.Ordinal);
        Assert.Contains("-ExpectedCommonName $CodeSigningExpectedCommonName", script, StringComparison.Ordinal);
        Assert.Contains("-RequireSigning:$RequireCodeSigning", script, StringComparison.Ordinal);
        Assert.Contains("Release manifest generator not found", script, StringComparison.Ordinal);
        Assert.Contains("Safe-temp publish directory missing", script, StringComparison.Ordinal);
        Assert.Contains("release-manifest.json", script, StringComparison.Ordinal);
        Assert.Contains("Manifest generation failed for Inno payload", script, StringComparison.Ordinal);
        Assert.Contains("'*.pdb','*.nupkg'", script, StringComparison.Ordinal);
        Assert.DoesNotContain("'*.xml'", script, StringComparison.Ordinal);

        string safeTempScript = ReadRepoText("build", "publish-safe-temp.ps1");
        Assert.Contains("The Inno installer pipeline packages this canonical server publish", safeTempScript, StringComparison.Ordinal);
        Assert.Contains("$serverOut = Join-Path $publishRoot \"server\"", safeTempScript, StringComparison.Ordinal);
        Assert.Contains("$serverProject = \".\\YFinance.net\\YFinance.NET.Server\\YFinance.NET.Server.csproj\"", safeTempScript, StringComparison.Ordinal);
        Assert.Contains("Publishing YFinance server", safeTempScript, StringComparison.Ordinal);
        Assert.Contains("@{ From = $serverTempPublish; To = $serverOut }", safeTempScript, StringComparison.Ordinal);
    }

    [Fact]
    public void InnoCleanup_RemovesOnlyProductLocalAppDataRoots()
    {
        string script = ReadRepoText("build", "installer", "Cleanup-DoNotPanicPortfolioVisualizer.ps1");

        Assert.Contains("DoNotPanicPortfolioVisualizer", script, StringComparison.Ordinal);
        Assert.Contains("Test-IsSafeProfileLocalAppDataRoot", script, StringComparison.Ordinal);
        Assert.Contains("AppData\\Local\\DoNotPanicPortfolioVisualizer", script, StringComparison.Ordinal);
        Assert.Contains("[switch]$AllUsers", script, StringComparison.Ordinal);
        Assert.Contains("#requires -Version 5.1", script, StringComparison.Ordinal);
        Assert.Contains("CloseMainWindow", script, StringComparison.Ordinal);
        Assert.Contains("Get-CimInstance Win32_UserProfile", script, StringComparison.Ordinal);
        Assert.Contains("Could not enumerate all Windows user profiles for cleanup", script, StringComparison.Ordinal);
        Assert.DoesNotContain("SystemDrive 'Users'", script, StringComparison.Ordinal);
        Assert.Contains("*\\YFinanceServer\\YFinance.NET.Server.dll*", script, StringComparison.Ordinal);
        Assert.Contains("Invoke-CimMethod -InputObject $serverHost -MethodName Terminate", script, StringComparison.Ordinal);
        Assert.Contains("ReparsePoint", script, StringComparison.Ordinal);
        Assert.DoesNotContain("AppData\\Local\\PortfolioSaver", script, StringComparison.Ordinal);
        Assert.DoesNotContain("$env:APPDATA", script, StringComparison.Ordinal);
    }

    private static string ReadRepoText(params string[] relativeParts)
    {
        string path = Path.Combine(RepoRoot.Value, Path.Combine(relativeParts));
        return File.ReadAllText(path);
    }

    private static string FindRepoRoot()
    {
        string? overrideRoot = Environment.GetEnvironmentVariable("REPO_ROOT");
        if (!string.IsNullOrWhiteSpace(overrideRoot) && File.Exists(Path.Combine(overrideRoot, "PortfolioScreensaver.sln")))
            return Path.GetFullPath(overrideRoot);

        string? directory = AppContext.BaseDirectory;
        while (!string.IsNullOrWhiteSpace(directory))
        {
            if (File.Exists(Path.Combine(directory, "PortfolioScreensaver.sln")))
                return directory;

            directory = Directory.GetParent(directory)?.FullName;
        }

        throw new InvalidOperationException($"Repository root was not found from '{AppContext.BaseDirectory}'. Set REPO_ROOT to the repository root when running tests from a shadow-copy or detached output directory.");
    }
}
