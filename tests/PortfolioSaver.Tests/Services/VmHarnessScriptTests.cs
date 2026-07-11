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
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Threading;
using Xunit;

namespace PortfolioSaver.Tests.Services;

public sealed class VmHarnessScriptTests
{
    [Fact]
    public void RunVmUxValidation_UsesCurrentConfigTitle_AndReturnsToGeneralTab()
    {
        string script = ReadRepoText("build",
            "vm",
            "Run-VmUxValidation.ps1");

        Assert.Contains("DO NOT PANIC PORTFOLIO VISUALIZER Config - 1.0", script, StringComparison.Ordinal);
        Assert.Contains("Select-Tab -Window $window -Name 'Advanced'", script, StringComparison.Ordinal);
        Assert.Contains("Select-Tab -Window $window -Name 'General'", script, StringComparison.Ordinal);
        Assert.Contains("function Capture-WindowByScreenCrop", script, StringComparison.Ordinal);
        Assert.Contains("RuntimeDesktopResolution", script, StringComparison.Ordinal);
        Assert.Contains("ResolutionChecks", script, StringComparison.Ordinal);
    }

    [Fact]
    public void RunVmUxValidation_RecordsActualCaptureDimensions_AndFlagsFramebufferMismatch()
    {
        string script = ReadRepoText("build",
            "vm",
            "Run-VmUxValidation.ps1");
        string runbook = ReadRepoText("build",
            "vm",
            "VM_OPERATIONS_RUNBOOK.md");

        Assert.Contains("ActualWidth = $mainSize.Width", script, StringComparison.Ordinal);
        Assert.Contains("ActualHeight = $mainSize.Height", script, StringComparison.Ordinal);
        Assert.Contains("ActualWidth = $saver12Size.Width", script, StringComparison.Ordinal);
        Assert.Contains("ActualHeight = $saver12Size.Height", script, StringComparison.Ordinal);
        Assert.Contains("dimension mismatch", script, StringComparison.Ordinal);
        Assert.Contains("verify actual capture dimensions before claiming multi-resolution pass", runbook, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Treat the remote Windows target as a generic machine", runbook, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RunVmUxValidation_AllowsCustomWorkspaceRootAndResultName()
    {
        string script = ReadRepoText("build",
            "vm",
            "Run-VmUxValidation.ps1");

        Assert.Contains("[string]$RootPath", script, StringComparison.Ordinal);
        Assert.Contains("[string]$ResultName", script, StringComparison.Ordinal);
        Assert.Contains("$root = $RootPath", script, StringComparison.Ordinal);
        Assert.Contains("$results = Join-Path $root ('results\\' + $ResultName)", script, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildSafeTemp_DrainsProcessOutputBeforeWaitForExit()
    {
        string script = ReadRepoText("build",
            "build-safe-temp.ps1");

        int startIndex = script.IndexOf("$null = $proc.Start()", StringComparison.Ordinal);
        int beginOutputIndex = script.IndexOf("$proc.BeginOutputReadLine()", StringComparison.Ordinal);
        int beginErrorIndex = script.IndexOf("$proc.BeginErrorReadLine()", StringComparison.Ordinal);
        int timedWaitIndex = script.IndexOf("$proc.WaitForExit($TimeoutSeconds * 1000)", StringComparison.Ordinal);

        Assert.True(startIndex >= 0);
        Assert.True(beginOutputIndex > startIndex);
        Assert.True(beginErrorIndex > startIndex);
        Assert.True(timedWaitIndex > beginOutputIndex);
        Assert.True(timedWaitIndex > beginErrorIndex);
        Assert.Contains("add_OutputDataReceived", script, StringComparison.Ordinal);
        Assert.Contains("add_ErrorDataReceived", script, StringComparison.Ordinal);
        Assert.Contains("ConcurrentQueue[string]", script, StringComparison.Ordinal);
        Assert.Contains("ManualResetEventSlim", script, StringComparison.Ordinal);
        Assert.Contains("$stdoutComplete.Wait([TimeSpan]::FromSeconds(5))", script, StringComparison.Ordinal);
        Assert.Contains("$stderrComplete.Wait([TimeSpan]::FromSeconds(5))", script, StringComparison.Ordinal);
        Assert.DoesNotContain("StandardOutput.ReadToEnd()", script, StringComparison.Ordinal);
        Assert.DoesNotContain("StandardError.ReadToEnd()", script, StringComparison.Ordinal);
        Assert.DoesNotContain("StringBuilder", script, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildSafeTemp_KillsChildProcessOnCancellationOrAbort()
    {
        string script = ReadRepoText("build",
            "build-safe-temp.ps1");

        Assert.Contains("finally {", script, StringComparison.Ordinal);
        Assert.Contains("$started = $false", script, StringComparison.Ordinal);
        Assert.Contains("$started = $true", script, StringComparison.Ordinal);
        Assert.Contains("if ($null -ne $proc -and $started)", script, StringComparison.Ordinal);
        Assert.Contains("if (-not $proc.HasExited)", script, StringComparison.Ordinal);
        Assert.Contains("$proc.CancelOutputRead()", script, StringComparison.Ordinal);
        Assert.Contains("$proc.CancelErrorRead()", script, StringComparison.Ordinal);
        Assert.Contains("$proc.Kill($true)", script, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildSafeTemp_UsesFreshRestoreInsteadOfCopiedObjAssets()
    {
        string script = ReadRepoText("build",
            "build-safe-temp.ps1");

        Assert.Contains("Directory.Build.targets", script, StringComparison.Ordinal);
        Assert.Contains("global.json", script, StringComparison.Ordinal);
        Assert.Contains("YFinance.net", script, StringComparison.Ordinal);
        Assert.Contains("YFinanceServer.targets", script, StringComparison.Ordinal);
        Assert.Contains("build .\\DoNotPanicPortfolioVisualizer.sln -c $Configuration -nodeReuse:false -v minimal", script, StringComparison.Ordinal);
        Assert.DoesNotContain("Seeding NuGet restore assets", script, StringComparison.Ordinal);
        Assert.DoesNotContain("project.assets.json", script, StringComparison.Ordinal);
        Assert.DoesNotContain("--no-restore", script, StringComparison.Ordinal);
        Assert.DoesNotContain("-m:1", script, StringComparison.Ordinal);
    }

    [Fact]
    public void PublishSafeTemp_SeedsImportedYFinanceServerTargets()
    {
        string script = ReadRepoText("build",
            "publish-safe-temp.ps1");

        Assert.Contains("$tempBuildRoot = Join-Path $tempRoot \"build\"", script, StringComparison.Ordinal);
        Assert.Contains("New-Item -ItemType Directory -Force -Path $tempBuildRoot", script, StringComparison.Ordinal);
        Assert.Contains("$yfinanceServerTargets = Join-Path (Join-Path $repoRoot \"build\") \"YFinanceServer.targets\"", script, StringComparison.Ordinal);
        Assert.Contains("Copy-Item -LiteralPath $yfinanceServerTargets -Destination $tempBuildRoot -Force", script, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(GetRepoRoot(), "build", "YFinanceServer.targets")));
        Assert.Contains("restore .\\DoNotPanicPortfolioVisualizer.sln -r $RuntimeIdentifier", script, StringComparison.Ordinal);
        Assert.DoesNotContain("Seeding local obj restore assets", script, StringComparison.Ordinal);
        Assert.DoesNotContain("project.assets.json", script, StringComparison.Ordinal);
        Assert.DoesNotContain("--disable-parallel", script, StringComparison.Ordinal);
        Assert.DoesNotContain("-m:1", script, StringComparison.Ordinal);
    }

    [Fact]
    public void VmPackageInstallScripts_AreIdempotentAndRetryTransientFailures()
    {
        string common = ReadRepoText("build", "vm", "VmPackageInstallCommon.ps1");
        string installChoco = ReadRepoText("build", "vm-tools", "install-choco.ps1");
        string installQaTools = ReadRepoText("build", "vm-tools", "install-vm-qa-tools.ps1");
        string resumeQaTools = ReadRepoText("build", "vm-tools", "install-vm-qa-tools-resume.ps1");
        string guestBootstrap = ReadRepoText("build", "vm", "Guest-BootstrapVmRemoteTools.ps1");
        string pushVmWorkspace = ReadRepoText("build", "vm", "Push-VmWorkspace.ps1");

        Assert.Contains("function Invoke-DnppvCommandWithRetry", common, StringComparison.Ordinal);
        Assert.Contains("function Test-DnppvChocoPackageInstalled", common, StringComparison.Ordinal);
        Assert.Contains("function Install-DnppvChocoPackage", common, StringComparison.Ordinal);
        Assert.Contains("$ErrorActionPreference = 'Stop'", common, StringComparison.Ordinal);
        Assert.Contains("[bool]$CheckLastExitCode", common, StringComparison.Ordinal);
        Assert.Contains("$global:LASTEXITCODE = 0", common, StringComparison.Ordinal);
        Assert.Contains("if (-not $CheckLastExitCode -or $LASTEXITCODE -eq 0) { return }", common, StringComparison.Ordinal);
        Assert.Contains("-CheckLastExitCode $true -ScriptBlock", common, StringComparison.Ordinal);
        Assert.Contains("Start-Sleep -Seconds ([Math]::Min(30, $DelaySeconds * $attempt))", common, StringComparison.Ordinal);
        Assert.Contains("Skipping already-installed choco package", common, StringComparison.Ordinal);
        Assert.Contains("list --local-only --exact --limit-output", common, StringComparison.Ordinal);

        Assert.Contains("VmPackageInstallCommon.ps1", installChoco, StringComparison.Ordinal);
        Assert.Contains("Get-Command choco.exe", installChoco, StringComparison.Ordinal);
        Assert.Contains("Invoke-WebRequest", installChoco, StringComparison.Ordinal);
        Assert.Contains("install-chocolatey.ps1", installChoco, StringComparison.Ordinal);
        Assert.Contains("C:\\Temp\\install-choco.log", installChoco, StringComparison.Ordinal);
        Assert.DoesNotContain("Invoke-Expression", installChoco, StringComparison.Ordinal);

        foreach (string script in new[] { installQaTools, resumeQaTools, guestBootstrap })
        {
            Assert.Contains("VmPackageInstallCommon.ps1", script, StringComparison.Ordinal);
            Assert.DoesNotContain("function Invoke-WithRetry", script, StringComparison.Ordinal);
            Assert.DoesNotContain("function Test-ChocoPackageInstalled", script, StringComparison.Ordinal);
        }

        Assert.Contains("Install-DnppvChocoPackage", installQaTools, StringComparison.Ordinal);
        Assert.Contains("Install-DnppvChocoPackage", resumeQaTools, StringComparison.Ordinal);
        Assert.Contains("Install-DnppvChocoPackage", guestBootstrap, StringComparison.Ordinal);
        Assert.Contains("$chocoCommand.Source", guestBootstrap, StringComparison.Ordinal);
        Assert.Contains("Continuing after failed optional choco package", installQaTools, StringComparison.Ordinal);
        Assert.Contains("FAILED after retries", resumeQaTools, StringComparison.Ordinal);
        Assert.Contains("Trying sysinternals via scoop as fallback.", resumeQaTools, StringComparison.Ordinal);
        Assert.Contains("autohotkey.portable", resumeQaTools, StringComparison.Ordinal);
        Assert.Contains("autohotkey' | Out-Null", resumeQaTools, StringComparison.Ordinal);
        Assert.Contains("Send-VmItem -Bundle $bundle -LocalPath (Join-Path $PSScriptRoot 'VmPackageInstallCommon.ps1')", pushVmWorkspace, StringComparison.Ordinal);
        Assert.Contains("Skipping already-installed global npm package: appium", installQaTools, StringComparison.Ordinal);
        Assert.Contains("Skipping already-installed global npm package: appium", resumeQaTools, StringComparison.Ordinal);
    }

    [Fact]
    public void VmPackageInstallCommon_RetryFunctionRetriesUntilSuccess()
    {
        string commonPath = Path.Combine(RepoRoot.Value, "build", "vm", "VmPackageInstallCommon.ps1");
        string tempScript = Path.Combine(Path.GetTempPath(), $"dnppv-retry-test-{Guid.NewGuid():N}.ps1");
        File.WriteAllText(tempScript, string.Join(Environment.NewLine, new[]
        {
            "$ErrorActionPreference = 'Stop'",
            "$attempts = 0",
            $". '{commonPath.Replace("'", "''")}'",
            "Invoke-DnppvCommandWithRetry -Operation 'test retry' -CheckLastExitCode $false -MaxAttempts 3 -DelaySeconds 0 -ScriptBlock { $script:attempts++; if ($script:attempts -lt 3) { throw 'not yet' } }",
            "if ($attempts -ne 3) { throw \"attempts=$attempts\" }",
            "Write-Output \"RETRY_OK attempts=$attempts\""
        }));

        try
        {
            using Process process = new()
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = ResolvePowerShellExe(),
                    ArgumentList =
                    {
                        "-NoLogo",
                        "-NoProfile",
                        "-ExecutionPolicy",
                        "Bypass",
                        "-File",
                        tempScript
                    },
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                }
            };

            process.Start();
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            if (!process.WaitForExit(15000))
            {
                process.Kill(entireProcessTree: true);
                Assert.Fail("Timed out while testing VmPackageInstallCommon retry behavior.");
            }

            Assert.Equal(0, process.ExitCode);
            Assert.Contains("RETRY_OK attempts=3", output, StringComparison.Ordinal);
            Assert.Contains("test retry failed on attempt 1 of 3", error + output, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(tempScript);
        }
    }

    [Fact]
    public void VmPackageInstallCommon_DoesNotReuseStaleLastExitCode()
    {
        string commonPath = Path.Combine(RepoRoot.Value, "build", "vm", "VmPackageInstallCommon.ps1");
        string tempScript = Path.Combine(Path.GetTempPath(), $"dnppv-last-exit-test-{Guid.NewGuid():N}.ps1");
        File.WriteAllText(tempScript, string.Join(Environment.NewLine, new[]
        {
            "$ErrorActionPreference = 'Stop'",
            "$global:LASTEXITCODE = 0",
            $". '{commonPath.Replace("'", "''")}'",
            "try {",
            "  Invoke-DnppvCommandWithRetry -Operation 'stale exit test' -CheckLastExitCode $true -MaxAttempts 1 -DelaySeconds 0 -ScriptBlock { $global:LASTEXITCODE = 42 }",
            "  throw 'expected failure was not observed'",
            "}",
            "catch {",
            "  if ($_.Exception.Message -notlike '*exited with 42*') { throw }",
            "}",
            "Write-Output 'STALE_EXIT_OK'"
        }));

        try
        {
            using Process process = new()
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = ResolvePowerShellExe(),
                    ArgumentList =
                    {
                        "-NoLogo",
                        "-NoProfile",
                        "-ExecutionPolicy",
                        "Bypass",
                        "-File",
                        tempScript
                    },
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                }
            };

            process.Start();
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            if (!process.WaitForExit(15000))
            {
                process.Kill(entireProcessTree: true);
                Assert.Fail("Timed out while testing VmPackageInstallCommon LASTEXITCODE reset behavior.");
            }

            Assert.True(process.ExitCode == 0, error + output);
            Assert.Contains("STALE_EXIT_OK", output, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(tempScript);
        }
    }

    [Fact]
    public void VmToolInventoryScripts_UseTargetedPathChecksInsteadOfRecursiveScans()
    {
        string inventory = ReadRepoText("build", "vm-tools", "vm-tool-inventory.ps1");
        string verify = ReadRepoText("build", "vm-tools", "verify-vm-tools.ps1");

        foreach (string script in new[] { inventory, verify })
        {
            Assert.DoesNotContain("Get-ChildItem -LiteralPath $root -Recurse", script, StringComparison.Ordinal);
            Assert.DoesNotContain("-Recurse -File", script, StringComparison.Ordinal);
            Assert.Contains("Known Tool", script, StringComparison.Ordinal);
            Assert.Contains("Test-Path -LiteralPath", script, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("Add-Section \"Key Paths\"", verify, StringComparison.Ordinal);
        Assert.Contains("Join-OptionalEnvPath", verify, StringComparison.Ordinal);
    }

    [Fact]
    public void InvokeVmBuildTest_UsesDesktopSessionAgentAndPollsForFinishedSummary()
    {
        string script = ReadRepoText("build",
            "vm",
            "Invoke-VmBuildTest.ps1");

        Assert.Contains("Guest-ConfigureDesktopAutomation.ps1", script, StringComparison.Ordinal);
        Assert.Contains("Guest-ClearDesktopAutomationCredentials.ps1", script, StringComparison.Ordinal);
        Assert.Contains("Guest-ApplyTestSecrets.ps1", script, StringComparison.Ordinal);
        Assert.Contains("[ValidateRange(1, 10080)]", script, StringComparison.Ordinal);
        Assert.Contains("[ValidateSet('Apply', 'Cancel')]", script, StringComparison.Ordinal);
        Assert.Contains("[string]$ValidationCompletionMode = 'Apply'", script, StringComparison.Ordinal);
        Assert.Contains("function Read-VmSharedJsonViaSftp", script, StringComparison.Ordinal);
        Assert.Contains("SFTP session is missing from the VM SSH session bundle.", script, StringComparison.Ordinal);
        Assert.Contains("Get-SFTPContent -SFTPSession $Bundle.SftpSession -Path $sftpPath -Encoding UTF8", script, StringComparison.Ordinal);
        Assert.Contains("$null = $json | ConvertFrom-Json -ErrorAction Stop", script, StringComparison.Ordinal);
        Assert.DoesNotContain("function New-RemoteSharedJsonReadCommand", script, StringComparison.Ordinal);
        Assert.Contains("[int]$DisplayWidth", script, StringComparison.Ordinal);
        Assert.Contains("[int]$DisplayHeight", script, StringComparison.Ordinal);
        Assert.Contains("[string]$DisplayProfile", script, StringComparison.Ordinal);
        Assert.Contains("agent\\agent-status.json", script, StringComparison.Ordinal);
        Assert.Contains("agent\\command-results\\$uxResultName.result.json", script, StringComparison.Ordinal);
        Assert.Contains("commands\\$uxResultName.json", script, StringComparison.Ordinal);
        Assert.Contains("schtasks.exe /Create", script, StringComparison.Ordinal);
        Assert.Contains("schtasks.exe /Run", script, StringComparison.Ordinal);
        Assert.Contains("/IT /RU '$remoteUser'", script, StringComparison.Ordinal);
        Assert.Contains("schtasks /Delete /TN \"PortfolioSaverVmAgent\" /F", script, StringComparison.Ordinal);
        Assert.Contains("Starting desktop-session agent", script, StringComparison.Ordinal);
        Assert.Contains("Queuing UX run through desktop-session agent", script, StringComparison.Ordinal);
        Assert.Contains("taskkill /IM PortfolioSaver.VmAgent.exe /F >nul 2>&1", script, StringComparison.Ordinal);
        Assert.Contains("Desktop-session agent start attempt failed once; retrying.", script, StringComparison.Ordinal);
        Assert.Contains("Set-Content -LiteralPath $localAgentCommandPath -Encoding UTF8", script, StringComparison.Ordinal);
        Assert.Contains("Send-VmItem -Bundle $bundle -LocalPath $localAgentCommandPath", script, StringComparison.Ordinal);
        Assert.Contains("ValidationCompletionMode = $ValidationCompletionMode", script, StringComparison.Ordinal);
        Assert.Contains("Read-VmSharedJsonViaSftp -Bundle $bundle -RemotePath $remoteAgentStatus", script, StringComparison.Ordinal);
        Assert.Contains("Read-VmSharedJsonViaSftp -Bundle $bundle -RemotePath $remoteAgentResult", script, StringComparison.Ordinal);
        Assert.Contains("Read-VmSharedJsonViaSftp -Bundle $bundle -RemotePath $remoteUxSummary", script, StringComparison.Ordinal);
        Assert.Contains("DisplayWidth = if ($DisplayWidth -gt 0) { $DisplayWidth } else { $null }", script, StringComparison.Ordinal);
        Assert.Contains("DisplayHeight = if ($DisplayHeight -gt 0) { $DisplayHeight } else { $null }", script, StringComparison.Ordinal);
        Assert.Contains("DisplayProfile = if (-not [string]::IsNullOrWhiteSpace($DisplayProfile)) { $DisplayProfile } else { $null }", script, StringComparison.Ordinal);
        Assert.Contains("[Alias('GuestScreensaverDurationMinutes')]", script, StringComparison.Ordinal);
        Assert.Contains("VisualizerRuntimeDurationMinutes = $GuestVisualizerRuntimeDurationMinutes", script, StringComparison.Ordinal);
        Assert.Contains("ScreensaverDurationMinutes = $GuestVisualizerRuntimeDurationMinutes", script, StringComparison.Ordinal);
        Assert.Contains("$effectiveCaptureIntervalSeconds = if ($GuestVisualizerRuntimeDurationMinutes -ge 120 -and $CaptureIntervalSeconds -lt 30) { 30 } else { $CaptureIntervalSeconds }", script, StringComparison.Ordinal);
        Assert.Contains("$effectiveUxTimeoutSeconds = [Math]::Max($UxTimeoutSeconds, ($GuestVisualizerRuntimeDurationMinutes * 60) + 1800)", script, StringComparison.Ordinal);
        Assert.Contains("CaptureIntervalSeconds = $effectiveCaptureIntervalSeconds", script, StringComparison.Ordinal);
        Assert.Contains("Using UX timeout budget of $effectiveUxTimeoutSeconds seconds with capture interval $effectiveCaptureIntervalSeconds seconds", script, StringComparison.Ordinal);
        Assert.Contains("PostProcess-ReferenceSpotChecks.ps1", script, StringComparison.Ordinal);
        Assert.Contains("LOCAL_RESULT_DIR=", script, StringComparison.Ordinal);
        Assert.Contains("Timed out waiting for remote desktop-session agent heartbeat", script, StringComparison.Ordinal);
        Assert.Contains("$summary.PSObject.Properties.Name -contains 'FinishedAt'", script, StringComparison.Ordinal);
        Assert.Contains("Clearing remote desktop automation autologon credential", script, StringComparison.Ordinal);
        Assert.Contains("DefaultPasswordPresent", script, StringComparison.Ordinal);
        Assert.DoesNotContain(" -p '$remotePassword'", script, StringComparison.Ordinal);
        Assert.DoesNotContain("$remotePassword", script, StringComparison.Ordinal);
        Assert.DoesNotContain("-Password", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PsExec.exe", script, StringComparison.Ordinal);
    }

    [Fact]
    public void InvokeVmBuildTest_CleansRemoteProcessesWhenAborted()
    {
        string script = ReadRepoText("build",
            "vm",
            "Invoke-VmBuildTest.ps1");
        string helper = ReadRepoText("build",
            "vm",
            "VmSshCommon.ps1");

        Assert.Contains("$runCompleted = $false", script, StringComparison.Ordinal);
        Assert.Contains("$runCompleted = $true", script, StringComparison.Ordinal);
        Assert.Contains("$runFailureReason = $null", script, StringComparison.Ordinal);
        Assert.Contains("$runFailureReason = $_.Exception.Message", script, StringComparison.Ordinal);
        Assert.Contains("Run did not complete; requesting remote harness abort cleanup", script, StringComparison.Ordinal);
        Assert.Contains("$abortReason = if ([string]::IsNullOrWhiteSpace($runFailureReason))", script, StringComparison.Ordinal);
        Assert.Contains("Invoke-VmHarnessAbortCleanup -Bundle $bundle -RootPath $RootPath -Reason $abortReason", script, StringComparison.Ordinal);
        Assert.Contains("Remove-VmSshSessionBundle -Bundle $bundle", script, StringComparison.Ordinal);
        Assert.Contains("function Invoke-VmHarnessAbortCleanup", helper, StringComparison.Ordinal);
        Assert.Contains("'PortfolioSaver.VmAgent'", helper, StringComparison.Ordinal);
        Assert.Contains("'PortfolioSaver.Config'", helper, StringComparison.Ordinal);
        Assert.Contains("'PortfolioSaver.Desktop'", helper, StringComparison.Ordinal);
        Assert.Contains("'WinAppDriver'", helper, StringComparison.Ordinal);
        Assert.Contains("RootPath is not specific enough", helper, StringComparison.Ordinal);
        Assert.Contains("harness-aborted.json", helper, StringComparison.Ordinal);
        Assert.Contains("Result = 'Aborted'", helper, StringComparison.Ordinal);
        Assert.Contains("CleanupFailures = @(`$cleanupFailures)", helper, StringComparison.Ordinal);
        Assert.Contains("Get-CimInstance Win32_Process", helper, StringComparison.Ordinal);
        Assert.Contains("$process = `$_", helper, StringComparison.Ordinal);
        Assert.Contains("Stop-Process -Id `$process.ProcessId", helper, StringComparison.Ordinal);
        Assert.Contains("$_.CommandLine.Contains(`$root)", helper, StringComparison.Ordinal);
        Assert.DoesNotContain("$rootPattern = $root.Replace", helper, StringComparison.Ordinal);
        Assert.Contains("Stop-Process -Force", helper, StringComparison.Ordinal);
    }

    [Fact]
    public void TestAssembly_AllowsParallelExecutionOutsideEnvironmentSerialCollection()
    {
        string assemblyInfo = ReadRepoText("tests", "PortfolioSaver.Tests", "Properties", "AssemblyInfo.cs");
        string environmentSerial = ReadRepoText("tests", "PortfolioSaver.Tests", "Services", "EnvironmentSerialCollection.cs");

        Assert.DoesNotContain("DisableTestParallelization = true", assemblyInfo, StringComparison.Ordinal);
        Assert.Contains("EnvironmentSerial", assemblyInfo, StringComparison.Ordinal);
        Assert.Contains("[CollectionDefinition(\"EnvironmentSerial\", DisableParallelization = true)]", environmentSerial, StringComparison.Ordinal);
    }

    [Fact]
    public void EnvironmentMutatingTests_OptIntoSerialCollection()
    {
        string testsRoot = Path.Combine(GetRepoRoot(), "tests", "PortfolioSaver.Tests");
        string[] mutatingExpressions =
        [
            @"\bEnvironment\.SetEnvironmentVariable\s*\(",
            @"\bEnvironment\.CurrentDirectory\s*=",
            @"\bCultureInfo\.CurrentCulture\s*=",
            @"\bCultureInfo\.CurrentUICulture\s*=",
            @"\bCultureInfo\.DefaultThreadCurrentCulture\s*=",
            @"\bCultureInfo\.DefaultThreadCurrentUICulture\s*=",
            @"\bThread\.CurrentThread\.CurrentCulture\s*=",
            @"\bThread\.CurrentThread\.CurrentUICulture\s*=",
            @"\bConsole\.SetOut\s*\(",
            @"\bConsole\.SetError\s*\(",
            @"\bDirectory\.SetCurrentDirectory\s*\(",
            @"\bTraceLog\.Reset\s*\(",
            @"\bTraceLog\.InfoState\s*\("
        ];

        List<string> offenders = [];
        foreach (string file in Directory.EnumerateFiles(testsRoot, "*.cs", SearchOption.AllDirectories))
        {
            if (IsBuildOutput(file))
            {
                continue;
            }

            string text = File.ReadAllText(file);
            if (!IsTestSource(text))
            {
                continue;
            }

            // This file owns the scanner patterns as test data; it does not mutate
            // process-wide state and is explicitly covered by the parallelism contract test above.
            if (string.Equals(Path.GetFileName(file), nameof(VmHarnessScriptTests) + ".cs", StringComparison.Ordinal))
            {
                continue;
            }

            if (mutatingExpressions.Any(pattern => Regex.IsMatch(text, pattern)) &&
                !text.Contains("[Collection(\"EnvironmentSerial\")]", StringComparison.Ordinal))
            {
                offenders.Add(Path.GetRelativePath(GetRepoRoot(), file));
            }
        }

        Assert.True(offenders.Count == 0, "Environment-mutating tests must opt into EnvironmentSerial: " + string.Join(", ", offenders));

        static bool IsBuildOutput(string file)
        {
            string normalized = file.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            string separator = Path.DirectorySeparatorChar.ToString();
            return normalized.Contains(separator + "bin" + separator, StringComparison.OrdinalIgnoreCase) ||
                   normalized.Contains(separator + "obj" + separator, StringComparison.OrdinalIgnoreCase);
        }

        static bool IsTestSource(string text)
            => text.Contains("[Fact", StringComparison.Ordinal) ||
               text.Contains("[Theory", StringComparison.Ordinal);
    }

    [Fact]
    public void GuestConfigureDesktopAutomation_SetsStartupLauncherAndDisablesScreenSaver()
    {
        string script = ReadRepoText("build",
            "vm",
            "Guest-ConfigureDesktopAutomation.ps1");

        Assert.Contains("PortfolioSaver VmAgent.lnk", script, StringComparison.Ordinal);
        Assert.Contains("Start-PortfolioSaverVmAgent.cmd", script, StringComparison.Ordinal);
        Assert.Contains("if not exist \"$agentPath\" exit /b 0", script, StringComparison.Ordinal);
        Assert.Contains("ScreenSaveActive", script, StringComparison.Ordinal);
        Assert.Contains("AutoAdminLogon", script, StringComparison.Ordinal);
        Assert.Contains("DefaultPassword", script, StringComparison.Ordinal);
        Assert.Contains("Remove-ItemProperty -Path 'HKLM:\\SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\Winlogon' -Name DefaultPassword", script, StringComparison.Ordinal);
        Assert.DoesNotContain("Set-ItemProperty -Path 'HKLM:\\SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\Winlogon' -Name DefaultPassword", script, StringComparison.Ordinal);
    }

    [Fact]
    public void GuestClearDesktopAutomationCredentials_RemovesAutologonPassword()
    {
        string script = ReadRepoText("build",
            "vm",
            "Guest-ClearDesktopAutomationCredentials.ps1");

        Assert.Contains("Remove-ItemProperty -Path $winlogonPath -Name DefaultPassword", script, StringComparison.Ordinal);
        Assert.Contains("Set-ItemProperty -Path $winlogonPath -Name AutoAdminLogon -Value '0'", script, StringComparison.Ordinal);
        Assert.Contains("DefaultPasswordPresent", script, StringComparison.Ordinal);
    }

    [Fact]
    public void GuestUxDeepExercise_LogsAndValidatesPhaseAndVersionChecks()
    {
        string script = ReadRepoText("build",
            "vm",
            "Guest-UxDeepExercise.ps1");

        Assert.Contains("Start-Transcript -Path $logPath -Force", script, StringComparison.Ordinal);
        Assert.Contains("ConfigPhaseStatus", script, StringComparison.Ordinal);
        Assert.Contains("RuntimePhaseStatus", script, StringComparison.Ordinal);
        Assert.Contains("ConfigVersionCheck", script, StringComparison.Ordinal);
        Assert.Contains("RuntimeVersionCheck", script, StringComparison.Ordinal);
        Assert.Contains("DesktopMainWindow", script, StringComparison.Ordinal);
        Assert.Contains("MainWindowTitleFallback", script, StringComparison.Ordinal);
        Assert.Contains("OptionsMenuRoot", script, StringComparison.Ordinal);
        Assert.Contains("ViewFullScreenMenuItem", script, StringComparison.Ordinal);
        Assert.Contains("OptionsSettingsMenuItem", script, StringComparison.Ordinal);
        Assert.Contains("Get-ProcessWindowElement", script, StringComparison.Ordinal);
        Assert.Contains("Invoke-AutomationElement", script, StringComparison.Ordinal);
        Assert.Contains("Expand-AutomationElement", script, StringComparison.Ordinal);
        Assert.Contains("[System.Windows.Forms.SendKeys]::SendWait('%o')", script, StringComparison.Ordinal);
        Assert.Contains("function Find-DescendantByNameAndControlType", script, StringComparison.Ordinal);
        Assert.Contains("function Find-TabItemByName", script, StringComparison.Ordinal);
        Assert.Contains("function Get-RepresentativeExerciseControls", script, StringComparison.Ordinal);
        Assert.Contains("function Send-KeySequence", script, StringComparison.Ordinal);
        Assert.Contains("function Get-ScrollPatternTarget", script, StringComparison.Ordinal);
        Assert.Contains("function Try-ScrollWindowContent", script, StringComparison.Ordinal);
        Assert.Contains("function Invoke-WindowViewportWheelScroll", script, StringComparison.Ordinal);
        Assert.Contains("function Perform-KeyboardScrollPass", script, StringComparison.Ordinal);
        Assert.Contains("function Perform-VisibleScrollSequence", script, StringComparison.Ordinal);
        Assert.Contains("function Perform-VisibleConfigActivity", script, StringComparison.Ordinal);
        Assert.Contains("function Write-ConfigWindowTrace", script, StringComparison.Ordinal);
        Assert.Contains("function Apply-HarnessSettingsOverrides", script, StringComparison.Ordinal);
        Assert.Contains("function Get-TopLevelWindowSnapshot", script, StringComparison.Ordinal);
        Assert.Contains("function Test-ConfigPhaseBudget", script, StringComparison.Ordinal);
        Assert.Contains("function Validate-AndCloseConfigWindow", script, StringComparison.Ordinal);
        Assert.Contains("function Wait-ConfigPrimaryButtonReady", script, StringComparison.Ordinal);
        Assert.Contains("$primaryButton.Current.IsEnabled -and -not $primaryButton.Current.IsOffscreen", script, StringComparison.Ordinal);
        Assert.Contains("Write-ConfigWindowTrace -Event 'PrimaryButtonNotReady'", script, StringComparison.Ordinal);
        Assert.Contains("function Get-ConfigBlockingDialog", script, StringComparison.Ordinal);
        Assert.Contains("function Get-ProcessOwnedWindows", script, StringComparison.Ordinal);
        Assert.Contains("function Close-ConfigWindowIfPresent", script, StringComparison.Ordinal);
        Assert.Contains("function Click-AutomationElementCenter", script, StringComparison.Ordinal);
        Assert.Contains("function Click-ConfigFooterButtonFallback", script, StringComparison.Ordinal);
        Assert.Contains("function Click-ConfigCloseButtonFallback", script, StringComparison.Ordinal);
        Assert.Contains("function Get-ConfigStatusText", script, StringComparison.Ordinal);
        Assert.Contains("$automationId -eq 'DesktopMainWindow'", script, StringComparison.Ordinal);
        Assert.Contains("$title -like 'DO NOT PANIC PORTFOLIO VISUALIZER*'", script, StringComparison.Ordinal);
        Assert.Contains("$windowPattern.Current.IsModal", script, StringComparison.Ordinal);
        Assert.Contains("$window = Find-ConfigWindow -Process $desktop -TimeoutSeconds 2", script, StringComparison.Ordinal);
        Assert.Contains("[System.Windows.Forms.SendKeys]::SendWait(' ')", script, StringComparison.Ordinal);
        Assert.Contains("$selected.Current.IsSelected", script, StringComparison.Ordinal);
        Assert.Contains("Find-DescendantByAutomationId -Root $Window -AutomationId 'ConfigStatusText'", script, StringComparison.Ordinal);
        Assert.Contains("Find-DescendantByAutomationId -Root $Window -AutomationId 'ConfigPrimaryButton'", script, StringComparison.Ordinal);
        Assert.Contains("Find-DescendantByAutomationId -Root $Window -AutomationId 'ConfigOkButton'", script, StringComparison.Ordinal);
        Assert.Contains("Find-DescendantByAutomationId -Root $Window -AutomationId 'ConfigCancelButton'", script, StringComparison.Ordinal);
        Assert.Contains("function Find-ConfigWindowOwned", script, StringComparison.Ordinal);
        Assert.Contains("$validatedStatusReady = -not [string]::IsNullOrWhiteSpace($statusText) -and", script, StringComparison.Ordinal);
        Assert.Contains("$statusText -like '*Validation passed. Click OK to save/apply, or Cancel to discard.*'", script, StringComparison.Ordinal);
        Assert.Contains("Write-ConfigWindowTrace -Event 'ValidateOkReady'", script, StringComparison.Ordinal);
        Assert.Contains("Write-ConfigWindowTrace -Event 'ValidatedKeyboardCloseAttempt'", script, StringComparison.Ordinal);
        Assert.Contains("Write-ConfigWindowTrace -Event 'ValidatedKeyboardCloseSucceeded'", script, StringComparison.Ordinal);
        Assert.Contains("Write-ConfigWindowTrace -Event 'FooterButtonClickFallbackAttempt'", script, StringComparison.Ordinal);
        Assert.Contains("Write-ConfigWindowTrace -Event 'ConfigCloseButtonFallback'", script, StringComparison.Ordinal);
        Assert.Contains("Write-ConfigWindowTrace -Event 'FooterButtonClickFallback'", script, StringComparison.Ordinal);
        Assert.Contains("Write-ConfigWindowTrace -Event 'PrimaryButtonInvoked'", script, StringComparison.Ordinal);
        Assert.Contains("Write-ConfigWindowTrace -Event 'PrimaryButtonMissingOrDisabled'", script, StringComparison.Ordinal);
        Assert.Contains("$invokeEvent = 'OkButtonInvoked'", script, StringComparison.Ordinal);
        Assert.Contains("$invokeEvent = 'CancelButtonInvoked'", script, StringComparison.Ordinal);
        Assert.Contains("throw 'Validate did not close the config window automatically.'", script, StringComparison.Ordinal);
        Assert.Contains("Validate-AndCloseConfigWindow -Process $desktop -Window $window -CompletionMode $ValidationCompletionMode", script, StringComparison.Ordinal);
        Assert.Contains("Close-ConfigWindowIfPresent -Process $desktop -Window $window", script, StringComparison.Ordinal);
        Assert.Contains("[System.Windows.Automation.AutomationElement]::RootElement.FindAll(", script, StringComparison.Ordinal);
        Assert.Contains("[System.Windows.Automation.TreeScope]::Children", script, StringComparison.Ordinal);
        Assert.Contains("[System.Windows.Forms.SendKeys]::SendWait('{ENTER}')", script, StringComparison.Ordinal);
        Assert.Contains("@{ Name = 'Escape'; Key = '{ESC}'; UseMenu = $false }", script, StringComparison.Ordinal);
        Assert.Contains("@{ Name = 'F11'; Key = '{F11}'; UseMenu = $false }", script, StringComparison.Ordinal);
        Assert.Contains("@{ Name = 'MenuToggle'; Key = $null; UseMenu = $true }", script, StringComparison.Ordinal);
        Assert.Contains("Capture-Screen -Path (Join-Path $results (\"config-tab-{0:D3}-{1}-scrolled.png\"", script, StringComparison.Ordinal);
        Assert.Contains("return Perform-VisibleScrollSequence -Window $Window -TabName $TabName -PageDownCount $pageDownCount", script, StringComparison.Ordinal);
        Assert.Contains("Try-ScrollWindowContent -Window $Window -TabName $TabName -PageCount ([Math]::Max(1, [Math]::Min(2, $PageDownCount)))", script, StringComparison.Ordinal);
        Assert.Contains("Invoke-WindowViewportWheelScroll -Window $Window", script, StringComparison.Ordinal);
        Assert.Contains("config-window-events.log", script, StringComparison.Ordinal);
        Assert.Contains("Config phase exceeded 60 seconds", script, StringComparison.Ordinal);
        Assert.Contains("Write-ConfigWindowTrace -Event 'TabActivityComplete'", script, StringComparison.Ordinal);
        Assert.Contains("$script:vmBackgroundChangeSeconds = 120", script, StringComparison.Ordinal);
        Assert.Contains("$settings['BackgroundChangeSeconds'] = $script:vmBackgroundChangeSeconds", script, StringComparison.Ordinal);
        Assert.Contains("$settings['ShuffleBackgrounds'] = $true", script, StringComparison.Ordinal);
        Assert.Contains("HarnessAppDataMigrationApplied", script, StringComparison.Ordinal);
        Assert.Contains("Copy-Item -Path (Join-Path $legacyAppDataRoot '*') -Destination $appDataRoot -Recurse -ErrorAction SilentlyContinue", script, StringComparison.Ordinal);
        Assert.Contains(".portfolio-visualizer-migration-complete", script, StringComparison.Ordinal);
        Assert.Contains("Write-ConfigWindowTrace -Event 'HarnessSettingsOverrideApplied'", script, StringComparison.Ordinal);
        Assert.Contains("$configInteractionStartedAt = $null", script, StringComparison.Ordinal);
        Assert.Contains("$configInteractionStartedAt = [datetime]::UtcNow", script, StringComparison.Ordinal);
        Assert.Contains("Test-ConfigPhaseBudget -StartedAt $configInteractionStartedAt -Stage (\"tab-{0}\" -f $rawTabName)", script, StringComparison.Ordinal);
        Assert.Contains("Test-ConfigPhaseBudget -StartedAt $configInteractionStartedAt -Stage 'validate-close'", script, StringComparison.Ordinal);
        Assert.DoesNotContain("Send-KeySequence -Keys @('{TAB}') -DelayMilliseconds 28", script, StringComparison.Ordinal);
        Assert.DoesNotContain("Send-KeySequence -Keys @('{PGUP}','{HOME}') -DelayMilliseconds 70", script, StringComparison.Ordinal);
        Assert.DoesNotContain("$clickedTab = Click-AutomationElementCenter -Element $tab", script, StringComparison.Ordinal);
        Assert.DoesNotContain("Start-Process -FilePath $configExe", script, StringComparison.Ordinal);
    }

    [Fact]
    public void GuestUxDeepExercise_ConfigInteractionWaitsStayAtOrBelowHalfSecond()
    {
        string script = ReadRepoText("build",
            "vm",
            "Guest-UxDeepExercise.ps1");
        string sharedParser = ReadRepoText("build",
            "vm",
            "VmTraceQuoteEvidence.ps1");

        int start = script.IndexOf("function Perform-VisibleConfigActivity", StringComparison.Ordinal);
        int end = script.IndexOf("function Find-ElementMetadataByProcessId", StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, "Could not isolate config activity function block.");

        string functionBlock = script[start..end];
        foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(
                     functionBlock,
                     "Start-Sleep -Milliseconds (?<value>\\d+)|DelayMilliseconds (?<delay>\\d+)"))
        {
            string raw = match.Groups["value"].Success ? match.Groups["value"].Value : match.Groups["delay"].Value;
            if (!int.TryParse(raw, out int milliseconds))
                continue;

            Assert.True(milliseconds <= 500, $"Found config interaction wait above 500 ms: {milliseconds}");
        }
    }

    [Fact]
    public void GuestUxDeepExercise_SummaryWritesUseBoundedRetryWithoutChangingHelperDefaults()
    {
        string script = ReadRepoText("build",
            "vm",
            "Guest-UxDeepExercise.ps1");

        Assert.Contains("$summaryWriteAttempts = 3", script, StringComparison.Ordinal);
        Assert.Contains("$summaryWriteRetryDelayMilliseconds = 50", script, StringComparison.Ordinal);
        Assert.Contains("Write-TextFileWithRetry -Path $summaryPath -Content $json -MaxAttempts $summaryWriteAttempts -RetryDelayMilliseconds $summaryWriteRetryDelayMilliseconds", script, StringComparison.Ordinal);
        Assert.Contains("Write-TextFileWithRetry -Path $legacySummaryPath -Content $json -MaxAttempts $summaryWriteAttempts -RetryDelayMilliseconds $summaryWriteRetryDelayMilliseconds", script, StringComparison.Ordinal);
        Assert.Contains("[int]$MaxAttempts = 20", script, StringComparison.Ordinal);
        Assert.Contains("[int]$RetryDelayMilliseconds = 80", script, StringComparison.Ordinal);
        Assert.Contains("if ($null -ne $primarySummaryWriteException -and -not $legacySummaryWritten)", script, StringComparison.Ordinal);
        Assert.Contains("Both UX summary writes failed after bounded retries.", script, StringComparison.Ordinal);
        Assert.Contains("[System.Exception[]]@($primarySummaryWriteException, $legacySummaryWriteException)", script, StringComparison.Ordinal);
        Assert.Contains("Primary UX summary write failed after bounded retries", script, StringComparison.Ordinal);
        Assert.Contains("Legacy UX summary write failed after bounded retries", script, StringComparison.Ordinal);
        Assert.DoesNotContain("if ($null -ne $summaryWriteFailure)", script, StringComparison.Ordinal);
    }

    [Fact]
    public void GuestUxDeepExercise_SupportsSshWorkspaceRoots_AndWritesLocalTraceBundles()
    {
        string script = ReadRepoText("build",
            "vm",
            "Guest-UxDeepExercise.ps1");

        Assert.Contains("[string]$RootPath", script, StringComparison.Ordinal);
        Assert.Contains("[string]$ResultRootPath", script, StringComparison.Ordinal);
        Assert.Contains("[ValidateRange(1, 10080)]", script, StringComparison.Ordinal);
        Assert.Contains("[int]$DisplayWidth", script, StringComparison.Ordinal);
        Assert.Contains("[int]$DisplayHeight", script, StringComparison.Ordinal);
        Assert.Contains("[string]$DisplayProfile = 'default'", script, StringComparison.Ordinal);
        Assert.Contains("[ValidateSet('Apply', 'Cancel')]", script, StringComparison.Ordinal);
        Assert.Contains("[string]$ValidationCompletionMode = 'Apply'", script, StringComparison.Ordinal);
        Assert.Contains("[string]$FaultProfile = 'none'", script, StringComparison.Ordinal);
        Assert.Contains("offline-then-recover-runtime", script, StringComparison.Ordinal);
        Assert.Contains("Clear-YFinanceFaultProfile", script, StringComparison.Ordinal);
        Assert.Contains("DNPPV_YFINANCE_FAULT_PROFILE_PATH", script, StringComparison.Ordinal);
        Assert.Contains("yfinance-fault-profile.json", script, StringComparison.Ordinal);
        Assert.Contains("fault-injection-events.log", script, StringComparison.Ordinal);
        Assert.Contains("function Set-YFinanceFaultProfile", script, StringComparison.Ordinal);
        Assert.Contains("$summary.FaultProfile = $FaultProfile", script, StringComparison.Ordinal);
        Assert.Contains("$root = $RootPath", script, StringComparison.Ordinal);
        Assert.Contains("$summary.ExportMode = 'LocalWorkspace'", script, StringComparison.Ordinal);
        Assert.Contains("$localTraceTarget = Join-Path $results 'trace'", script, StringComparison.Ordinal);
        Assert.Contains("yfinance.circular.log", script, StringComparison.Ordinal);
        Assert.Contains("yfinance.circular.idx", script, StringComparison.Ordinal);
        Assert.Contains("function Reset-PortfolioTraceRoot", script, StringComparison.Ordinal);
        Assert.Contains("function Try-ApplyDisplayResolution", script, StringComparison.Ordinal);
        Assert.Contains("function Get-AvailableDisplayModes", script, StringComparison.Ordinal);
        Assert.Contains("function Get-CimSupportedDisplayModes", script, StringComparison.Ordinal);
        Assert.Contains("function Format-DisplayModeNames", script, StringComparison.Ordinal);
        Assert.Contains("function Get-CachedDisplayModes", script, StringComparison.Ordinal);
        Assert.Contains("$script:cachedDisplayModes = $null", script, StringComparison.Ordinal);
        Assert.Contains("$script:cachedDisplayModesTimestamp = $null", script, StringComparison.Ordinal);
        Assert.Contains("($modes.Count -gt 0)", script, StringComparison.Ordinal);
        Assert.Contains("TotalSeconds -lt 300", script, StringComparison.Ordinal);
        Assert.Contains("$availableModes = @(Get-CachedDisplayModes)", script, StringComparison.Ordinal);
        Assert.Contains("function Clear-CachedDisplayModes", script, StringComparison.Ordinal);
        Assert.Contains("function Find-TopLevelWindowByNameLike", script, StringComparison.Ordinal);
        Assert.Contains("function Try-ApplyDisplayResolutionViaSettings", script, StringComparison.Ordinal);
        Assert.Contains("Start-Process -FilePath 'cmd.exe' -ArgumentList '/c start \"\" ms-settings:display' | Out-Null", script, StringComparison.Ordinal);
        Assert.Contains("CIM_VideoControllerResolution", script, StringComparison.Ordinal);
        Assert.Contains("Find-TopLevelWindowByNameLike -NameLike '*Settings*'", script, StringComparison.Ordinal);
        Assert.Contains("ms-settings:display", script, StringComparison.Ordinal);
        Assert.Contains("Display resolution", script, StringComparison.Ordinal);
        Assert.Contains("Keep changes", script, StringComparison.Ordinal);
        Assert.Contains("settings-apply-nochange", script, StringComparison.Ordinal);
        Assert.Contains("settings-mode-not-found", script, StringComparison.Ordinal);
        Assert.Contains("settings-applied", script, StringComparison.Ordinal);
        Assert.Contains("$mode.dmFields = 0x180000", script, StringComparison.Ordinal);
        Assert.Contains("[NativeDisplaySettings]::ChangeDisplaySettings([ref]$mode, 0)", script, StringComparison.Ordinal);
        Assert.Contains("if ($payload.Applied)", script, StringComparison.Ordinal);
        Assert.Contains("Clear-CachedDisplayModes", script, StringComparison.Ordinal);
        Assert.Contains("SupportedDisplayModes = @()", script, StringComparison.Ordinal);
        Assert.Contains("$summary.SupportedDisplayModes = @(Format-DisplayModeNames -Modes $displayApply.AvailableModes)", script, StringComparison.Ordinal);
        Assert.Contains("RequestedDisplayProfile", script, StringComparison.Ordinal);
        Assert.Contains("RuntimeDesktopResolution = Get-CurrentVirtualScreenSize", script, StringComparison.Ordinal);
        Assert.Contains("[Alias('ScreensaverDurationMinutes')]", script, StringComparison.Ordinal);
        Assert.Contains("VisualizerRuntimeDurationMinutes (or ScreensaverDurationMinutes alias) must be greater than zero.", script, StringComparison.Ordinal);
        Assert.Contains("$recoveryAt = $captureLoopStartedAt.AddSeconds(($VisualizerRuntimeDurationMinutes * 60.0) / 2.0)", script, StringComparison.Ordinal);
        Assert.Contains("$recoveryApplied = $false", script, StringComparison.Ordinal);
        Assert.Contains("Start-Sleep -Seconds 6", script, StringComparison.Ordinal);
        Assert.Contains("Reset-PortfolioTraceRoot", script, StringComparison.Ordinal);
        Assert.Contains("$summary.DesktopPhaseStatus = \"Running\"", script, StringComparison.Ordinal);
        Assert.Contains("$effectiveCaptureIntervalSeconds = if ($VisualizerRuntimeDurationMinutes -ge 120 -and $CaptureIntervalSeconds -lt 30) { 30 } else { $CaptureIntervalSeconds }", script, StringComparison.Ordinal);
        Assert.Contains("$isLongRunSoak = $VisualizerRuntimeDurationMinutes -ge 120", script, StringComparison.Ordinal);
        Assert.Contains("RequestedCaptureIntervalSeconds = $CaptureIntervalSeconds", script, StringComparison.Ordinal);
        Assert.Contains("EffectiveCaptureIntervalSeconds = $effectiveCaptureIntervalSeconds", script, StringComparison.Ordinal);
        Assert.Contains("TargetCaptureFrames = $targetFrames", script, StringComparison.Ordinal);
        Assert.Contains("$severityLabel = if ($lastCaptureIndex -lt [Math]::Floor($targetFrames * 0.5)) { 'blocking' } else { 'low coverage' }", script, StringComparison.Ordinal);
        Assert.Contains("capture loop remained wall-clock bounded", script, StringComparison.Ordinal);
        Assert.Contains("Capture interval raised from $CaptureIntervalSeconds to $effectiveCaptureIntervalSeconds seconds for long-run soak stability.", script, StringComparison.Ordinal);
        Assert.Contains("Long-run soak mode enabled; desktop visualizer remains the fullscreen runtime host.", script, StringComparison.Ordinal);
        Assert.Contains("$summary.RuntimePhaseStatus = \"Running\"", script, StringComparison.Ordinal);
        Assert.DoesNotContain("PortfolioSaver.Screensaver", script, StringComparison.Ordinal);
        Assert.DoesNotContain("$screensaverExe", script, StringComparison.Ordinal);
        Assert.Contains("$env:PORTFOLIOSAVER_DISABLE_INPUT_EXIT = '1'", script, StringComparison.Ordinal);
        Assert.Contains("Remove-Item Env:PORTFOLIOSAVER_DISABLE_INPUT_EXIT", script, StringComparison.Ordinal);
        Assert.Contains("Remove-Item Env:DNPPV_YFINANCE_FAULT_PROFILE_PATH", script, StringComparison.Ordinal);
        Assert.Contains("Visual host did not enter true fullscreen during long-run soak.", script, StringComparison.Ordinal);
        Assert.Contains("$summary.RuntimeVersionCheck = \"SoftFailed\"", script, StringComparison.Ordinal);
        Assert.Contains("Runtime version element containing the expected 1.0 marker was not detected during long-run soak; continuing.", script, StringComparison.Ordinal);
        Assert.Contains("$nextCaptureAt = $frameStartedAt.AddSeconds($effectiveCaptureIntervalSeconds)", script, StringComparison.Ordinal);
        Assert.Contains("Start-Sleep -Seconds $sleepSeconds", script, StringComparison.Ordinal);
        Assert.Contains("if ($isLongRunSoak) {\n                $summary.RuntimeShots++\n            }", script.ReplaceLineEndings("\n"), StringComparison.Ordinal);
        Assert.True(script.Split("$summary.RuntimeShots++", StringSplitOptions.None).Length - 1 >= 2);
        Assert.Contains("desktop-after-recovery-clear-{0:D3}.png", script, StringComparison.Ordinal);
        Assert.Contains("$script:screenCaptureManifestPath = Join-Path $results 'screen-captures.jsonl'", script, StringComparison.Ordinal);
        Assert.Contains("Set-Content -LiteralPath $script:screenCaptureManifestPath -Value '' -Encoding UTF8", script, StringComparison.Ordinal);
        Assert.Contains("$capturedAt = (Get-Date).ToString('o')", script, StringComparison.Ordinal);
        Assert.Contains("CapturedAt = $capturedAt", script, StringComparison.Ordinal);
        Assert.Contains("FileName = $fileInfo.Name", script, StringComparison.Ordinal);
        Assert.Contains("Length = $fileInfo.Length", script, StringComparison.Ordinal);
        Assert.Contains("Add-Content -LiteralPath $script:screenCaptureManifestPath", script, StringComparison.Ordinal);
        Assert.Contains("Write-SummaryFiles", script, StringComparison.Ordinal);
        Assert.DoesNotContain("VBOXSVR", script, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidationSmokeCoversOfflineRecoveryArtifactAnalysis()
    {
        string script = ReadRepoText("build",
            "validation",
            "Test-ValidationScripts.ps1");
        string analyzer = ReadRepoText("build",
            "validation",
            "Analyze-VisualValidationArtifacts.ps1");
        string guestHarness = ReadRepoText("build",
            "vm",
            "Guest-UxDeepExercise.ps1");

        Assert.Contains("offline-then-recover-runtime", script, StringComparison.Ordinal);
        Assert.Contains("offline-recovery-ux-state-unverified", script, StringComparison.Ordinal);
        Assert.Contains("offline-recovery-insufficient-captures", script, StringComparison.Ordinal);
        Assert.Contains("offline-ux-state-delay", script, StringComparison.Ordinal);
        Assert.Contains("RuntimeDataFreshnessChanged", script, StringComparison.Ordinal);
        Assert.Contains("recovery-no-activation-analysis.json", script, StringComparison.Ordinal);
        Assert.Contains("data_freshness_text=LIVE quote feed", script, StringComparison.Ordinal);
        Assert.Contains("Get-TraceLineTimestampUtc", analyzer, StringComparison.Ordinal);
        Assert.Contains("Expected RuntimeDataFreshnessChanged to OFFLINE within 2.5 seconds", analyzer, StringComparison.Ordinal);
        Assert.Contains("function Test-TraceAgeFieldFresh", analyzer, StringComparison.Ordinal);
        Assert.Contains("[double]::TryParse", analyzer, StringComparison.Ordinal);
        Assert.Contains("Minimum blocking completion ratio is 50 percent", analyzer, StringComparison.Ordinal);
        Assert.Contains("$hasFreshTraceAge = Test-TraceAgeFieldFresh -Line $_.Line -FieldName 'trace_age_seconds'", analyzer, StringComparison.Ordinal);
        Assert.Contains("$hasDirectUiFreshness = $_.Line -match 'latest_freshness_source=ui(\\s|$)' -and $_.Line -match 'ui_freshness=LIVE quote feed'", analyzer, StringComparison.Ordinal);
        Assert.Contains("elseif (-not [string]::IsNullOrWhiteSpace($uiFreshnessText) -and $uiFreshnessText -eq $latestFreshnessText)", guestHarness, StringComparison.Ordinal);
    }

    [Fact]
    public void GuestUxDeepExercise_CapturesReferenceSpotChecksForLongRuns()
    {
        string script = ReadRepoText("build",
            "vm",
            "Guest-UxDeepExercise.ps1");
        string sharedParser = ReadRepoText("build",
            "vm",
            "VmTraceQuoteEvidence.ps1");

        Assert.Contains("reference-spot-checks.jsonl", script, StringComparison.Ordinal);
        Assert.Contains("reference-spot-check-comparisons.jsonl", script, StringComparison.Ordinal);
        Assert.Contains("function Write-ReferenceSpotCheck", script, StringComparison.Ordinal);
        Assert.Contains("function Get-ReferenceSpotCheckResults", script, StringComparison.Ordinal);
        Assert.Contains("function Get-LatestDisplayedTapeSample", script, StringComparison.Ordinal);
        Assert.Contains("function Get-PreferredDisplayedTapeSample", script, StringComparison.Ordinal);
        Assert.Contains("function Test-IsDisplayedSampleFullyLive", script, StringComparison.Ordinal);
        Assert.Contains("$parsedSamples.Add([object]$items.ToArray())", script, StringComparison.Ordinal);
        Assert.Contains("for ($sampleIndex = $parsedSamples.Count - 1; $sampleIndex -ge 0; $sampleIndex--)", script, StringComparison.Ordinal);
        Assert.DoesNotContain("Select-Object -Reverse", script, StringComparison.Ordinal);
        Assert.Contains("function Write-ReferenceSpotCheckComparison", script, StringComparison.Ordinal);
        Assert.Contains("YFinanceTrace", script, StringComparison.Ordinal);
        Assert.Contains("VmTraceQuoteEvidence.ps1", script, StringComparison.Ordinal);
        Assert.Contains("Test-YFinanceQuoteEvidenceParser", script, StringComparison.Ordinal);
        Assert.Contains("DONOTPANICPORTFOLIOVISUALIZER_LOCALDATA_ROOT", script, StringComparison.Ordinal);
        Assert.Contains("PORTFOLIOSAVER_LOCALDATA_ROOT", script, StringComparison.Ordinal);
        Assert.Contains("function Get-HarnessAppDataRoot", script, StringComparison.Ordinal);
        Assert.Contains("function Get-HarnessTracePath", script, StringComparison.Ordinal);
        Assert.Contains("function Get-KnownRuntimeFreshnessValues", script, StringComparison.Ordinal);
        Assert.Contains("function Get-VisibleRuntimeFreshnessText", script, StringComparison.Ordinal);
        Assert.Contains("function Get-LatestRuntimeQuoteEvidence", script, StringComparison.Ordinal);
        Assert.Contains("RuntimeDataFreshnessText", script, StringComparison.Ordinal);
        Assert.Contains("$knownFreshnessValues = Get-KnownRuntimeFreshnessValues", script, StringComparison.Ordinal);
        Assert.Contains("function Write-RuntimeFreshnessSnapshot", script, StringComparison.Ordinal);
        Assert.Contains("function Test-IsExpectedValidationUnavailableStatus", script, StringComparison.Ordinal);
        Assert.Contains("function Test-ConfigExpectsValidationUnavailable", script, StringComparison.Ordinal);
        Assert.Contains("function Close-ConfigWindowPatternFallback", script, StringComparison.Ordinal);
        Assert.Contains("function Close-ConfigWindowMessageFallback", script, StringComparison.Ordinal);
        Assert.Contains("NativeWindowMessaging", script, StringComparison.Ordinal);
        Assert.Contains("WM_CLOSE", script, StringComparison.Ordinal);
        Assert.Contains("function Close-ConfigForExpectedValidationUnavailable", script, StringComparison.Ordinal);
        Assert.Contains("[switch]$ExpectedValidationUnavailable", script, StringComparison.Ordinal);
        Assert.Contains("ExpectedValidationUnavailableObserved", script, StringComparison.Ordinal);
        Assert.Contains("ExpectedValidationUnavailableCloseAttempt", script, StringComparison.Ordinal);
        Assert.Contains("ExpectedValidationUnavailableCloseException", script, StringComparison.Ordinal);
        Assert.Contains("ExpectedValidationUnavailableCloseMethodDidNotDismiss", script, StringComparison.Ordinal);
        Assert.Contains("ExpectedValidationUnavailableRetryScheduled", script, StringComparison.Ordinal);
        Assert.Contains("main window can close. This is intentionally idempotent", script, StringComparison.Ordinal);
        Assert.Contains("ExpectedValidationUnavailableChildDialogCloseWait", script, StringComparison.Ordinal);
        Assert.Contains("Wait-UIAutomationCondition -TimeoutSeconds 10 -PollMilliseconds 200 -TraceEvent 'ExpectedValidationUnavailableCloseWait'", script, StringComparison.Ordinal);
        Assert.Contains("Close-ConfigWindowPatternFallback", script, StringComparison.Ordinal);
        Assert.Contains("Close-ConfigWindowMessageFallback", script, StringComparison.Ordinal);
        Assert.Contains("ConfigWindowMessageCloseFallback", script, StringComparison.Ordinal);
        Assert.DoesNotContain("Click-ConfigTitleBarCloseFallback", script, StringComparison.Ordinal);
        Assert.Contains("Runtime-only profiles intentionally stay out of this list", script, StringComparison.Ordinal);
        Assert.Contains("return $Profile -in @('offline-at-start', 'offline-during-config-validation', 'upstream-throttled', 'timeout')", script, StringComparison.Ordinal);
        Assert.Contains("$expectedValidationUnavailable = Test-ConfigExpectsValidationUnavailable -Profile $FaultProfile", script, StringComparison.Ordinal);
        Assert.Contains("-ExpectedValidationUnavailable:$expectedValidationUnavailable", script, StringComparison.Ordinal);
        Assert.Contains("finally {", script, StringComparison.Ordinal);
        Assert.Contains("Clear-YFinanceFaultProfile", script, StringComparison.Ordinal);
        Assert.Contains("[switch]$IncludeVisibleFreshness", script, StringComparison.Ordinal);
        Assert.Contains("if ($IncludeVisibleFreshness -and $null -ne $DesktopProcess", script, StringComparison.Ordinal);
        Assert.Contains("Get-VisibleRuntimeFreshnessText -DesktopProcess $DesktopProcess", script, StringComparison.Ordinal);
        Assert.Contains("latest_freshness_source=", script, StringComparison.Ordinal);
        Assert.Contains("trace_age_seconds=", script, StringComparison.Ordinal);
        Assert.Contains("ui_freshness=", script, StringComparison.Ordinal);
        Assert.Contains("latest_applied_quote_symbol=", script, StringComparison.Ordinal);
        Assert.Contains("latest_applied_quote_trace_age_seconds=", script, StringComparison.Ordinal);
        Assert.Contains("latest_applied_quote_fetch_age_seconds=", script, StringComparison.Ordinal);
        Assert.Contains("latest_server_quote_symbol=", script, StringComparison.Ordinal);
        Assert.Contains("latest_server_quote_trace_age_seconds=", script, StringComparison.Ordinal);
        Assert.Contains("latest_server_quote_fetch_age_seconds=", script, StringComparison.Ordinal);
        Assert.Contains("function Get-TracePayloadFieldValue", script, StringComparison.Ordinal);
        Assert.Contains("$env:DNPPV_RUNTIME_QUOTE_DEBUG_TRACE = '1'", script, StringComparison.Ordinal);
        Assert.Contains("Get-LatestRuntimeQuoteEvidence -RelativePath 'Trace\\trace.circular.log' -EventName 'RuntimeQuoteApplied' -SymbolFieldName 'requested_symbol' -FetchTimestampFieldName 'latest_fetch_timestamp_utc'", script, StringComparison.Ordinal);
        Assert.Contains("Get-LatestRuntimeQuoteEvidence -RelativePath 'Trace\\yfinance.circular.log' -EventName 'QuoteResponseObserved' -SymbolFieldName 'symbol' -FetchTimestampFieldName 'fetch_timestamp_utc'", script, StringComparison.Ordinal);
        Assert.Contains("[DateTimeOffset]::UtcNow", script, StringComparison.Ordinal);
        Assert.Contains("-split '\\r?\\n'", script, StringComparison.Ordinal);
        Assert.Contains("$traceFreshnessAgeSeconds -gt 90", script, StringComparison.Ordinal);
        Assert.Contains("$freshnessSource = 'ui-trace-stale'", script, StringComparison.Ordinal);
        Assert.Contains("-DesktopProcess $desktop", script, StringComparison.Ordinal);
        Assert.Contains("-IncludeVisibleFreshness", script, StringComparison.Ordinal);
        Assert.Contains("desktop-after-recovery-clear-{0:D3}.png", script, StringComparison.Ordinal);
        Assert.Contains("function Try-WriteReferenceSpotCheck", script, StringComparison.Ordinal);
        Assert.Contains("Reference spot-checks are advisory", script, StringComparison.Ordinal);
        Assert.Contains("Reference spot-check ignored {0} malformed displayed sample item(s).", script, StringComparison.Ordinal);
        Assert.Contains("$postRecoverySpotCheckSucceeded = Try-WriteReferenceSpotCheck -OutputPath $referenceSpotCheckPath -CaptureIndex $i -Context 'after-recovery-clear'", script, StringComparison.Ordinal);
        Assert.Contains("if (-not $postRecoverySpotCheckSucceeded) {", script, StringComparison.Ordinal);
        Assert.Contains("Write-SummaryFiles", script, StringComparison.Ordinal);
        Assert.Contains("Reference spot-check failed during ${Context}:", script, StringComparison.Ordinal);
        Assert.Contains("step=$script:referenceSpotCheckStep", script, StringComparison.Ordinal);
        Assert.Contains("script_stack=$($_.ScriptStackTrace)", script, StringComparison.Ordinal);
        Assert.Contains("reference-spot-check-errors.log", script, StringComparison.Ordinal);
        Assert.Contains("Length -gt 10240", script, StringComparison.Ordinal);
        Assert.Contains("[System.IO.Path]::GetFullPath($OutputPath)", script, StringComparison.Ordinal);
        Assert.Contains("Reference spot-check error-log write failed", script, StringComparison.Ordinal);
        Assert.Contains("return $false", script, StringComparison.Ordinal);
        Assert.Contains("$includeVisibleFreshnessForCapture = $true", script, StringComparison.Ordinal);
        Assert.DoesNotContain("$includeVisibleFreshnessForCapture = $FaultProfile", script, StringComparison.Ordinal);
        Assert.Contains("-IncludeVisibleFreshness:$includeVisibleFreshnessForCapture", script, StringComparison.Ordinal);
        Assert.Contains("runtime-freshness-events.log", script, StringComparison.Ordinal);
        Assert.Contains("latest_freshness=", script, StringComparison.Ordinal);
        Assert.Contains("Runtime freshness snapshot failed", script, StringComparison.Ordinal);
        Assert.Contains("[System.IO.FileShare]::ReadWrite", script, StringComparison.Ordinal);
        Assert.Contains("$captureDeadline = $captureLoopStartedAt.AddMinutes($VisualizerRuntimeDurationMinutes)", script, StringComparison.Ordinal);
        Assert.Contains("} while ((Get-Date) -lt $captureDeadline)", script, StringComparison.Ordinal);
        Assert.Contains("Get-HarnessTracePath -RelativePath 'Trace\\trace.circular.log'", script, StringComparison.Ordinal);
        Assert.Contains("Get-HarnessTracePath -RelativePath 'Trace\\yfinance.circular.log'", script, StringComparison.Ordinal);
        Assert.Contains("Join-Path (Join-Path $env:LOCALAPPDATA 'PortfolioSaver') $RelativePath", script, StringComparison.Ordinal);
        Assert.DoesNotContain("PortfolioSaver\\Trace\\trace.circular.log", script, StringComparison.Ordinal);
        Assert.Contains("'Process', 'User', 'Machine'", script, StringComparison.Ordinal);
        Assert.Contains("FileShare]::ReadWrite", script, StringComparison.Ordinal);
        Assert.Contains("QuoteResponseObserved", sharedParser, StringComparison.Ordinal);
        Assert.Contains("ReferenceComparisonSchemaVersion = 2", sharedParser, StringComparison.Ordinal);
        Assert.Contains("ComparisonSchemaVersion", script, StringComparison.Ordinal);
        Assert.Contains("Error = $null", script, StringComparison.Ordinal);
        Assert.Contains("not independent market-data correctness", sharedParser, StringComparison.Ordinal);
        Assert.Contains("YFinanceEvidenceStatus", script, StringComparison.Ordinal);
        Assert.Contains("AbsoluteDifference", script, StringComparison.Ordinal);
        Assert.Contains("PercentDifference", script, StringComparison.Ordinal);
        Assert.Contains("DisplayedVsReferenceFeed", script, StringComparison.Ordinal);
        Assert.Contains(".Replace(\"`0\", '')", script, StringComparison.Ordinal);
        Assert.Contains("[System.IO.Path]::ChangeExtension($Path, '.idx')", script, StringComparison.Ordinal);
        Assert.Contains("$bytesToRead = [int][Math]::Min([int64]$MaxBytes, $length)", script, StringComparison.Ordinal);
        Assert.DoesNotContain("Read-AllBytesShared -Path $Path", script, StringComparison.Ordinal);
        Assert.Contains("function Read-AllBytesShared", sharedParser, StringComparison.Ordinal);
        Assert.Contains("$displayedSample = @(Get-PreferredDisplayedTapeSample)", script, StringComparison.Ordinal);
        Assert.DoesNotContain("query1.finance.yahoo.com/v7/finance/quote", script, StringComparison.Ordinal);
        Assert.DoesNotContain("function Get-TwelveDataReferenceResults", script, StringComparison.Ordinal);
        Assert.DoesNotContain("function Get-YahooReferenceResults", script, StringComparison.Ordinal);
        Assert.DoesNotContain("PORTFOLIOSAVER_TWELVEDATA_API_KEY", script, StringComparison.Ordinal);
    }

    [Fact]
    public void PostProcessReferenceSpotChecks_BuildsComparisonsFromPulledCircularTrace()
    {
        string script = ReadRepoText("build",
            "vm",
            "PostProcess-ReferenceSpotChecks.ps1");
        string sharedParser = ReadRepoText("build",
            "vm",
            "VmTraceQuoteEvidence.ps1");

        Assert.Contains("function Read-CircularTraceText", script, StringComparison.Ordinal);
        Assert.Contains("function Parse-DisplayedTapeSamples", script, StringComparison.Ordinal);
        Assert.Contains("if (-not [string]::IsNullOrWhiteSpace($traceText))", script, StringComparison.Ordinal);
        Assert.Contains("function Get-PreferredDisplayedTapeSample", script, StringComparison.Ordinal);
        Assert.Contains("function Test-IsDisplayedSampleFullyLive", script, StringComparison.Ordinal);
        Assert.Contains("function Get-ReferenceResults", script, StringComparison.Ordinal);
        Assert.Contains("DisplayedVsReferenceFeed", script, StringComparison.Ordinal);
        Assert.Contains("reference-spot-check-comparisons.jsonl", script, StringComparison.Ordinal);
        Assert.Contains("combined-trace-tail.txt", script, StringComparison.Ordinal);
        Assert.Contains("yfinance.circular.log", script, StringComparison.Ordinal);
        Assert.Contains("SampleSelection", script, StringComparison.Ordinal);
        Assert.Contains("latest-fully-live", script, StringComparison.Ordinal);
        Assert.Contains("YFinanceTrace", script, StringComparison.Ordinal);
        Assert.Contains("VmTraceQuoteEvidence.ps1", script, StringComparison.Ordinal);
        Assert.Contains("Test-YFinanceQuoteEvidenceParser", script, StringComparison.Ordinal);
        Assert.Contains("FileShare]::ReadWrite", sharedParser, StringComparison.Ordinal);
        Assert.Contains("QuoteResponseObserved", sharedParser, StringComparison.Ordinal);
        Assert.Contains("ReferenceComparisonSchemaVersion = 2", sharedParser, StringComparison.Ordinal);
        Assert.Contains("ComparisonSchemaVersion", script, StringComparison.Ordinal);
        Assert.Contains("Error = $null", script, StringComparison.Ordinal);
        Assert.Contains("not independent market-data correctness", sharedParser, StringComparison.Ordinal);
        Assert.Contains("YFinanceEvidenceStatus", script, StringComparison.Ordinal);
        Assert.Contains("AbsoluteDifference", script, StringComparison.Ordinal);
        Assert.Contains("PercentDifference", script, StringComparison.Ordinal);
        Assert.DoesNotContain("query1.finance.yahoo.com/v7/finance/quote", script, StringComparison.Ordinal);
        Assert.DoesNotContain("function Get-YahooReferenceResults", script, StringComparison.Ordinal);
        Assert.DoesNotContain("0m", script, StringComparison.Ordinal);
        Assert.DoesNotContain("Select-Object -First 6", script, StringComparison.Ordinal);
    }

    [Fact]
    public void VmTraceQuoteEvidenceParser_DefinesSelfTestForObservedQuoteLines()
    {
        string parser = ReadRepoText("build", "vm", "VmTraceQuoteEvidence.ps1");

        Assert.Contains("function Test-YFinanceQuoteEvidenceParser", parser, StringComparison.Ordinal);
        Assert.Contains("event=QuoteResponseObserved / operation=get_quotes / symbol=SPY / price=600.12", parser, StringComparison.Ordinal);
        Assert.Contains("Parse-YFinanceQuoteEvidence -TraceText $sample -Symbols @('SPY')", parser, StringComparison.Ordinal);
        Assert.Contains("[Math]::Abs([decimal]$parsed[0].Last - [decimal]600.12)", parser, StringComparison.Ordinal);
    }

    [Fact]
    public void SshHarnessScripts_UseVmharnessWorkspaceAndDoNotDependOnVBox()
    {
        string push = ReadRepoText("build", "vm", "Push-VmWorkspace.ps1");
        string invoke = ReadRepoText("build", "vm", "Invoke-VmBuildTest.ps1");
        string pull = ReadRepoText("build", "vm", "Pull-VmResults.ps1");
        string bootstrap = ReadRepoText("build", "vm", "Guest-BootstrapVmRemoteTools.ps1");
        string applySecrets = ReadRepoText("build", "vm", "Guest-ApplyTestSecrets.ps1");
        string helper = ReadRepoText("build", "vm", "VmSshCommon.ps1");

        Assert.Contains(@"C:\vmharness\portfolio-saver", push, StringComparison.Ordinal);
        Assert.Contains(@"C:\vmharness\portfolio-saver", invoke, StringComparison.Ordinal);
        Assert.Contains(@"C:\vmharness\portfolio-saver", pull, StringComparison.Ordinal);
        Assert.Contains(@"C:\vmharness\portfolio-saver", bootstrap, StringComparison.Ordinal);
        Assert.Contains(@"C:\vmharness\portfolio-saver", applySecrets, StringComparison.Ordinal);
        Assert.Contains("Posh-SSH", helper, StringComparison.Ordinal);
        Assert.Contains("repo-snapshot.tar", push, StringComparison.Ordinal);
        Assert.Contains("& tar -xf `$archivePath -C `$repoPath", push, StringComparison.Ordinal);
        Assert.Contains("& tar @arguments", helper, StringComparison.Ordinal);
        Assert.Contains("Ensure-VmFreeSpace -Bundle $bundle -RootPath $RootPath -MinimumFreeGb 8", push, StringComparison.Ordinal);
        Assert.Contains("Ensure-VmFreeSpace -Bundle $bundle -RootPath $RootPath -MinimumFreeGb 8", invoke, StringComparison.Ordinal);
        Assert.Contains("function Ensure-VmFreeSpace", helper, StringComparison.Ordinal);
        Assert.Contains("function Invoke-VmWorkspaceCleanup", helper, StringComparison.Ordinal);
        Assert.Contains("build\\vm\\artifacts", helper, StringComparison.Ordinal);
        Assert.Contains("Remove-Item -LiteralPath $target -Recurse -Force", helper, StringComparison.Ordinal);
        Assert.Contains("build\\vm\\test-secrets.json", push, StringComparison.Ordinal);
        Assert.Contains("[string]$FaultProfile = 'none'", invoke, StringComparison.Ordinal);
        Assert.Contains("[ValidateSet('none', 'offline-at-start', 'offline-during-config-validation', 'offline-during-runtime', 'offline-then-recover-runtime'", invoke, StringComparison.Ordinal);
        Assert.Contains("offline-then-recover-runtime", invoke, StringComparison.Ordinal);
        Assert.Contains("FaultProfile = $FaultProfile", invoke, StringComparison.Ordinal);
        string agent = ReadRepoText("src", "PortfolioSaver.VmAgent", "Program.cs");
        Assert.Contains("\"offline-then-recover-runtime\"", agent, StringComparison.Ordinal);
        Assert.Contains("public int? VisualizerRuntimeDurationMinutes { get; set; }", agent, StringComparison.Ordinal);
        Assert.Contains("public int? ScreensaverDurationMinutes { get; set; }", agent, StringComparison.Ordinal);
        Assert.Contains("int duration = command.Payload?.VisualizerRuntimeDurationMinutes", agent, StringComparison.Ordinal);
        Assert.Contains("?? command.Payload?.ScreensaverDurationMinutes", agent, StringComparison.Ordinal);
        Assert.Contains("-VisualizerRuntimeDurationMinutes {duration}", agent, StringComparison.Ordinal);
        Assert.Contains("DEEPSEEK_API_KEY", applySecrets, StringComparison.Ordinal);
        Assert.DoesNotContain("OpenRouterApiKey", applySecrets, StringComparison.Ordinal);
        Assert.DoesNotContain("OPENROUTER_AI_API_KEY", applySecrets, StringComparison.Ordinal);
        Assert.DoesNotContain("OPENROUTER_API_KEY", applySecrets, StringComparison.Ordinal);
        Assert.DoesNotContain("PORTFOLIOSAVER_FINNHUB_API_KEY", applySecrets, StringComparison.Ordinal);
        Assert.DoesNotContain("PORTFOLIOSAVER_TWELVEDATA_API_KEY", applySecrets, StringComparison.Ordinal);
        Assert.DoesNotContain("PORTFOLIOSAVER_TIINGO_API_KEY", applySecrets, StringComparison.Ordinal);
        Assert.DoesNotContain("VBoxManage", push, StringComparison.Ordinal);
        Assert.DoesNotContain("VBoxManage", invoke, StringComparison.Ordinal);
        Assert.DoesNotContain("VBOXSVR", push, StringComparison.Ordinal);
        Assert.DoesNotContain("guestcontrol", invoke, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void VmSshCommon_IgnoresKnownPwshStartupNoiseWithoutMaskingRealFailures()
    {
        string helper = ReadRepoText("build", "vm", "VmSshCommon.ps1");

        Assert.Contains("function Test-IsIgnorableVmPwshFailure", helper, StringComparison.Ordinal);
        Assert.Contains("InitializeDefaultDrives operation", helper, StringComparison.Ordinal);
        Assert.Contains("if (Test-IsIgnorableVmPwshFailure -Result $result)", helper, StringComparison.Ordinal);
    }

    [Fact]
    public void InstalledSoakTraceAnalyzer_UsesStrictTransportFailureDetection()
    {
        string analyzer = ReadRepoText("build", "validation", "Analyze-InstalledSoakTrace.ps1");

        Assert.Contains("function Select-StrictTransportFailure", analyzer, StringComparison.Ordinal);
        Assert.Contains("PendingReadDrainFailed", analyzer, StringComparison.Ordinal);
        Assert.Contains("Unable to read data from the transport connection", analyzer, StringComparison.Ordinal);
        Assert.Contains("YFinanceServerFaultInjection", analyzer, StringComparison.Ordinal);
        Assert.Contains("event=ServerStartup", analyzer, StringComparison.Ordinal);
        Assert.Contains("strictTransportFailure", analyzer, StringComparison.Ordinal);
        Assert.Contains("runtimeQuoteApplied", analyzer, StringComparison.Ordinal);
        Assert.Contains("event=RuntimeQuoteApplied(?!\\w)", analyzer, StringComparison.Ordinal);
        Assert.Contains("function Select-Http429Evidence", analyzer, StringComparison.Ordinal);
        Assert.Contains("status(_code)?=429", analyzer, StringComparison.Ordinal);
        Assert.Contains("Too Many Requests", analyzer, StringComparison.Ordinal);
        Assert.DoesNotContain("\\b429\\b|rate limit", analyzer, StringComparison.Ordinal);
        Assert.DoesNotContain("transport_fail", analyzer, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InstalledSoakHarness_CapturesVisualAndResourceEvidence()
    {
        string script = ReadRepoText("build", "validation", "Run-InstalledSoakOnce.local.ps1");
        string sceneControl = ReadRepoText("src", "PortfolioSaver.Presentation", "Controls", "VisualizerSceneControl.xaml.cs");

        Assert.Contains("[ValidateRange(5, 1440)]", script, StringComparison.Ordinal);
        Assert.Contains("[int]$DurationMinutes = 30", script, StringComparison.Ordinal);
        Assert.Contains("$durationSeconds=[int]($DurationMinutes * 60)", script, StringComparison.Ordinal);
        Assert.Contains("$lastCaptureDelaySeconds=[Math]::Max(30, $durationSeconds - 45)", script, StringComparison.Ordinal);
        Assert.Contains("$captureDelayText=($captureDelays -join ',')", script, StringComparison.Ordinal);
        Assert.Contains("$invokeTimeoutSeconds=[Math]::Max(600, $durationSeconds + 300)", script, StringComparison.Ordinal);
        Assert.Contains("$captureDir=Join-Path `$result 'scene-captures'", script, StringComparison.Ordinal);
        Assert.Contains("$metricsPath=Join-Path `$result 'resource-samples.csv'", script, StringComparison.Ordinal);
        Assert.Contains("function Write-ResourceSample", script, StringComparison.Ordinal);
        Assert.Contains("set PORTFOLIOSAVER_CAPTURE_DIR=`$captureDir", script, StringComparison.Ordinal);
        Assert.Contains("set PORTFOLIOSAVER_CAPTURE_STEM=installed-soak", script, StringComparison.Ordinal);
        Assert.Contains("\"set PORTFOLIOSAVER_CAPTURE_DELAYS=$captureDelayText\"", script, StringComparison.Ordinal);
        Assert.DoesNotContain("'set PORTFOLIOSAVER_CAPTURE_DELAYS=$captureDelayText'", script, StringComparison.Ordinal);
        Assert.Contains("`$deadline=(Get-Date).AddSeconds($durationSeconds)", script, StringComparison.Ordinal);
        Assert.Contains("-TimeOutSeconds $invokeTimeoutSeconds", script, StringComparison.Ordinal);
        Assert.Contains("launch-installed-soak.cmd", script, StringComparison.Ordinal);
        Assert.Contains("$taskAction='cmd.exe /c \"\"' + `$launchCmd + '\"\"'", script, StringComparison.Ordinal);
        Assert.Contains("Delete it as soon as", script, StringComparison.Ordinal);
        Assert.Contains("Write-ResourceSample 'before-stop'", script, StringComparison.Ordinal);
        Assert.Contains("Get-Process PortfolioSaver.Desktop,YFinance.NET.Server", script, StringComparison.Ordinal);
        Assert.Contains("/IT is intentional: the installed-soak lane validates real GUI rendering", script, StringComparison.Ordinal);
        Assert.Contains("/ST `$taskTime /IT /RU '$remoteUser'", script, StringComparison.Ordinal);
        Assert.Contains("Installed soak did not capture sufficient screenshot evidence.", script, StringComparison.Ordinal);
        Assert.Contains("Installed soak did not capture resource-sample evidence.", script, StringComparison.Ordinal);
        Assert.Contains("Installed soak exceeded desktop private-memory guardrail", script, StringComparison.Ordinal);
        Assert.Contains("Installed soak exceeded desktop thread-count guardrail", script, StringComparison.Ordinal);
        Assert.Contains("$resourceSampleFileName = Split-Path -Leaf ([string]$summary.ResourceSampleFile)", script, StringComparison.Ordinal);
        Assert.Contains("$localResourceSamplePath = Join-Path $localResultRoot $resourceSampleFileName", script, StringComparison.Ordinal);
        Assert.Contains("$resourceSamples = @(Import-Csv -LiteralPath $localResourceSamplePath", script, StringComparison.Ordinal);
        Assert.Contains("$maxPrivateMemoryMb = ($resourceSamples | Measure-Object -Property PrivateMemoryMB -Maximum).Maximum", script, StringComparison.Ordinal);
        Assert.Contains("$maxThreads = ($resourceSamples | Measure-Object -Property Threads -Maximum).Maximum", script, StringComparison.Ordinal);
        Assert.DoesNotContain("CopyFromScreen", script, StringComparison.Ordinal);
        Assert.Contains("PORTFOLIOSAVER_CAPTURE_DIR", sceneControl, StringComparison.Ordinal);
        Assert.Contains("PORTFOLIOSAVER_CAPTURE_STEM", sceneControl, StringComparison.Ordinal);
        Assert.Contains("PORTFOLIOSAVER_CAPTURE_DELAYS", sceneControl, StringComparison.Ordinal);
        Assert.Contains("CaptureSceneSequenceAsync", sceneControl, StringComparison.Ordinal);
        Assert.Contains("SaveSceneCapture", sceneControl, StringComparison.Ordinal);
        Assert.Contains("SceneCaptureComplete", sceneControl, StringComparison.Ordinal);
        Assert.Contains("SceneCaptureCleanupQueued", sceneControl, StringComparison.Ordinal);
        Assert.Contains("GC.Collect(2, GCCollectionMode.Optimized, blocking: false", sceneControl, StringComparison.Ordinal);
        Assert.DoesNotContain("GC.WaitForPendingFinalizers", sceneControl, StringComparison.Ordinal);
    }

    [Fact]
    public void DeepSeekArtifactReview_IncludesAppNativeSceneCaptureTiming()
    {
        string script = ReadRepoText("build", "validation", "Invoke-DeepSeekArtifactReview.ps1");

        Assert.Contains("$sceneCaptureTraceLines", script, StringComparison.Ordinal);
        Assert.Contains("SceneCaptureComplete|SceneCaptureCleanupQueued", script, StringComparison.Ordinal);
        Assert.Contains("# App-Native Scene Capture Trace Timing", script, StringComparison.Ordinal);
        Assert.Contains("File modified timestamps may reflect later artifact copy time.", script, StringComparison.Ordinal);
    }

    private static readonly ConcurrentDictionary<string, Lazy<string>> SourceTextCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Lazy<string> RepoRoot = new(GetRepoRoot, LazyThreadSafetyMode.ExecutionAndPublication);

    private static string ReadRepoText(params string[] relativeParts)
    {
        string path = Path.Combine(new[] { RepoRoot.Value }.Concat(relativeParts).ToArray());
        return SourceTextCache.GetOrAdd(
            path,
            static key => new Lazy<string>(() => File.ReadAllText(key), LazyThreadSafetyMode.ExecutionAndPublication)).Value;
    }

    private static string GetRepoRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            string candidate = Path.Combine(current.FullName, "DoNotPanicPortfolioVisualizer.sln");
            if (File.Exists(candidate))
                return current.FullName;

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root from test base directory.");
    }

    private static string ResolvePowerShellExe()
    {
        string? path = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(path))
        {
            foreach (string directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                string candidate = Path.Combine(directory.Trim(), "pwsh.exe");
                if (File.Exists(candidate))
                    return candidate;
            }
        }

        return "powershell.exe";
    }

}



