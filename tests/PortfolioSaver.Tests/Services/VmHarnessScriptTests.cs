using Xunit;

namespace PortfolioSaver.Tests.Services;

public sealed class VmHarnessScriptTests
{
    [Fact]
    public void RunVmUxValidation_UsesCurrentConfigTitle_AndReturnsToGeneralTab()
    {
        string script = File.ReadAllText(Path.Combine(
            GetRepoRoot(),
            "build",
            "vm",
            "Run-VmUxValidation.ps1"));

        Assert.Contains("DO NOT PANIC PORTFOLIO VISUALIZER Config - BETA-5.5", script, StringComparison.Ordinal);
        Assert.Contains("Select-Tab -Window $window -Name 'Advanced'", script, StringComparison.Ordinal);
        Assert.Contains("Select-Tab -Window $window -Name 'General'", script, StringComparison.Ordinal);
        Assert.Contains("function Capture-WindowByScreenCrop", script, StringComparison.Ordinal);
        Assert.Contains("RuntimeDesktopResolution", script, StringComparison.Ordinal);
        Assert.Contains("ResolutionChecks", script, StringComparison.Ordinal);
    }

    [Fact]
    public void RunVmUxValidation_RecordsActualCaptureDimensions_AndFlagsFramebufferMismatch()
    {
        string script = File.ReadAllText(Path.Combine(
            GetRepoRoot(),
            "build",
            "vm",
            "Run-VmUxValidation.ps1"));
        string runbook = File.ReadAllText(Path.Combine(
            GetRepoRoot(),
            "build",
            "vm",
            "VM_OPERATIONS_RUNBOOK.md"));

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
        string script = File.ReadAllText(Path.Combine(
            GetRepoRoot(),
            "build",
            "vm",
            "Run-VmUxValidation.ps1"));

        Assert.Contains("[string]$RootPath", script, StringComparison.Ordinal);
        Assert.Contains("[string]$ResultName", script, StringComparison.Ordinal);
        Assert.Contains("$root = $RootPath", script, StringComparison.Ordinal);
        Assert.Contains("$results = Join-Path $root ('results\\' + $ResultName)", script, StringComparison.Ordinal);
    }

    [Fact]
    public void InvokeVmBuildTest_UsesDesktopSessionAgentAndPollsForFinishedSummary()
    {
        string script = File.ReadAllText(Path.Combine(
            GetRepoRoot(),
            "build",
            "vm",
            "Invoke-VmBuildTest.ps1"));

        Assert.Contains("Guest-ConfigureDesktopAutomation.ps1", script, StringComparison.Ordinal);
        Assert.Contains("Guest-ApplyTestSecrets.ps1", script, StringComparison.Ordinal);
        Assert.Contains("[ValidateRange(1, 10080)]", script, StringComparison.Ordinal);
        Assert.Contains("agent\\agent-status.json", script, StringComparison.Ordinal);
        Assert.Contains("agent\\command-results\\$uxResultName.result.json", script, StringComparison.Ordinal);
        Assert.Contains("commands\\$uxResultName.json", script, StringComparison.Ordinal);
        Assert.Contains("PsExec.exe", script, StringComparison.Ordinal);
        Assert.Contains("Starting desktop-session agent", script, StringComparison.Ordinal);
        Assert.Contains("Queuing UX run through desktop-session agent", script, StringComparison.Ordinal);
        Assert.Contains("Set-Content -LiteralPath $localAgentCommandPath -Encoding UTF8", script, StringComparison.Ordinal);
        Assert.Contains("Send-VmItem -Bundle $bundle -LocalPath $localAgentCommandPath", script, StringComparison.Ordinal);
        Assert.Contains("PostProcess-ReferenceSpotChecks.ps1", script, StringComparison.Ordinal);
        Assert.Contains("LOCAL_RESULT_DIR=", script, StringComparison.Ordinal);
        Assert.Contains("Timed out waiting for remote desktop-session agent heartbeat", script, StringComparison.Ordinal);
        Assert.Contains("$summary.PSObject.Properties.Name -contains 'FinishedAt'", script, StringComparison.Ordinal);
    }

    [Fact]
    public void GuestConfigureDesktopAutomation_SetsStartupLauncherAndDisablesScreenSaver()
    {
        string script = File.ReadAllText(Path.Combine(
            GetRepoRoot(),
            "build",
            "vm",
            "Guest-ConfigureDesktopAutomation.ps1"));

        Assert.Contains("PortfolioSaver VmAgent.lnk", script, StringComparison.Ordinal);
        Assert.Contains("Start-PortfolioSaverVmAgent.cmd", script, StringComparison.Ordinal);
        Assert.Contains("if not exist \"$agentPath\" exit /b 0", script, StringComparison.Ordinal);
        Assert.Contains("ScreenSaveActive", script, StringComparison.Ordinal);
        Assert.Contains("AutoAdminLogon", script, StringComparison.Ordinal);
        Assert.Contains("DefaultPassword", script, StringComparison.Ordinal);
    }

    [Fact]
    public void GuestUxDeepExercise_LogsAndValidatesPhaseAndVersionChecks()
    {
        string script = File.ReadAllText(Path.Combine(
            GetRepoRoot(),
            "build",
            "vm",
            "Guest-UxDeepExercise.ps1"));

        Assert.Contains("Start-Transcript -Path $logPath -Force", script, StringComparison.Ordinal);
        Assert.Contains("ConfigPhaseStatus", script, StringComparison.Ordinal);
        Assert.Contains("ScreensaverPhaseStatus", script, StringComparison.Ordinal);
        Assert.Contains("ConfigVersionCheck", script, StringComparison.Ordinal);
        Assert.Contains("ScreensaverVersionCheck", script, StringComparison.Ordinal);
        Assert.Contains("ScreensaverHostWindow", script, StringComparison.Ordinal);
        Assert.Contains("DesktopMainWindow", script, StringComparison.Ordinal);
        Assert.Contains("MainWindowTitleFallback", script, StringComparison.Ordinal);
        Assert.Contains("ViewFullScreenMenuItem", script, StringComparison.Ordinal);
        Assert.Contains("Get-ProcessWindowElement", script, StringComparison.Ordinal);
        Assert.Contains("Invoke-AutomationElement", script, StringComparison.Ordinal);
    }

    [Fact]
    public void GuestUxDeepExercise_SupportsSshWorkspaceRoots_AndWritesLocalTraceBundles()
    {
        string script = File.ReadAllText(Path.Combine(
            GetRepoRoot(),
            "build",
            "vm",
            "Guest-UxDeepExercise.ps1"));

        Assert.Contains("[string]$RootPath", script, StringComparison.Ordinal);
        Assert.Contains("[string]$ResultRootPath", script, StringComparison.Ordinal);
        Assert.Contains("[ValidateRange(1, 10080)]", script, StringComparison.Ordinal);
        Assert.Contains("$root = $RootPath", script, StringComparison.Ordinal);
        Assert.Contains("$summary.ExportMode = 'LocalWorkspace'", script, StringComparison.Ordinal);
        Assert.Contains("$localTraceTarget = Join-Path $results 'trace'", script, StringComparison.Ordinal);
        Assert.Contains("function Reset-PortfolioTraceRoot", script, StringComparison.Ordinal);
        Assert.Contains("Reset-PortfolioTraceRoot", script, StringComparison.Ordinal);
        Assert.Contains("$summary.DesktopPhaseStatus = \"Running\"", script, StringComparison.Ordinal);
        Assert.Contains("Write-SummaryFiles", script, StringComparison.Ordinal);
        Assert.DoesNotContain("VBOXSVR", script, StringComparison.Ordinal);
    }

    [Fact]
    public void GuestUxDeepExercise_CapturesReferenceSpotChecksForLongRuns()
    {
        string script = File.ReadAllText(Path.Combine(
            GetRepoRoot(),
            "build",
            "vm",
            "Guest-UxDeepExercise.ps1"));

        Assert.Contains("reference-spot-checks.jsonl", script, StringComparison.Ordinal);
        Assert.Contains("reference-spot-check-comparisons.jsonl", script, StringComparison.Ordinal);
        Assert.Contains("function Write-ReferenceSpotCheck", script, StringComparison.Ordinal);
        Assert.Contains("function Get-ReferenceSpotCheckResults", script, StringComparison.Ordinal);
        Assert.Contains("function Get-TwelveDataReferenceResults", script, StringComparison.Ordinal);
        Assert.Contains("PORTFOLIOSAVER_TWELVEDATA_API_KEY", script, StringComparison.Ordinal);
        Assert.Contains("function Get-LatestDisplayedTapeSample", script, StringComparison.Ordinal);
        Assert.Contains("function Write-ReferenceSpotCheckComparison", script, StringComparison.Ordinal);
        Assert.Contains("query1.finance.yahoo.com/v7/finance/quote", script, StringComparison.Ordinal);
        Assert.Contains("DisplayedVsReferenceFeed", script, StringComparison.Ordinal);
        Assert.Contains(".Replace(\"`0\", '')", script, StringComparison.Ordinal);
        Assert.Contains("[System.IO.Path]::ChangeExtension($Path, '.idx')", script, StringComparison.Ordinal);
        Assert.Contains("[System.IO.File]::ReadAllBytes($Path)", script, StringComparison.Ordinal);
    }

    [Fact]
    public void PostProcessReferenceSpotChecks_BuildsComparisonsFromPulledCircularTrace()
    {
        string script = File.ReadAllText(Path.Combine(
            GetRepoRoot(),
            "build",
            "vm",
            "PostProcess-ReferenceSpotChecks.ps1"));

        Assert.Contains("function Read-CircularTraceText", script, StringComparison.Ordinal);
        Assert.Contains("function Parse-DisplayedTapeSamples", script, StringComparison.Ordinal);
        Assert.Contains("function Get-ReferenceResults", script, StringComparison.Ordinal);
        Assert.Contains("DisplayedVsReferenceFeed", script, StringComparison.Ordinal);
        Assert.Contains("reference-spot-check-comparisons.jsonl", script, StringComparison.Ordinal);
        Assert.Contains("[decimal]::Zero", script, StringComparison.Ordinal);
        Assert.DoesNotContain("0m", script, StringComparison.Ordinal);
        Assert.DoesNotContain("Select-Object -First 6", script, StringComparison.Ordinal);
    }

    [Fact]
    public void SshHarnessScripts_UseVmharnessWorkspaceAndDoNotDependOnVBox()
    {
        string push = File.ReadAllText(Path.Combine(GetRepoRoot(), "build", "vm", "Push-VmWorkspace.ps1"));
        string invoke = File.ReadAllText(Path.Combine(GetRepoRoot(), "build", "vm", "Invoke-VmBuildTest.ps1"));
        string pull = File.ReadAllText(Path.Combine(GetRepoRoot(), "build", "vm", "Pull-VmResults.ps1"));
        string bootstrap = File.ReadAllText(Path.Combine(GetRepoRoot(), "build", "vm", "Guest-BootstrapVmRemoteTools.ps1"));
        string applySecrets = File.ReadAllText(Path.Combine(GetRepoRoot(), "build", "vm", "Guest-ApplyTestSecrets.ps1"));
        string helper = File.ReadAllText(Path.Combine(GetRepoRoot(), "build", "vm", "VmSshCommon.ps1"));

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
        Assert.Contains("DEEPSEEK_API_KEY", applySecrets, StringComparison.Ordinal);
        Assert.DoesNotContain("VBoxManage", push, StringComparison.Ordinal);
        Assert.DoesNotContain("VBoxManage", invoke, StringComparison.Ordinal);
        Assert.DoesNotContain("VBOXSVR", push, StringComparison.Ordinal);
        Assert.DoesNotContain("guestcontrol", invoke, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void VmSshCommon_IgnoresKnownPwshStartupNoiseWithoutMaskingRealFailures()
    {
        string helper = File.ReadAllText(Path.Combine(GetRepoRoot(), "build", "vm", "VmSshCommon.ps1"));

        Assert.Contains("function Test-IsIgnorableVmPwshFailure", helper, StringComparison.Ordinal);
        Assert.Contains("InitializeDefaultDrives operation", helper, StringComparison.Ordinal);
        Assert.Contains("if (Test-IsIgnorableVmPwshFailure -Result $result)", helper, StringComparison.Ordinal);
    }

    private static string GetRepoRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            string candidate = Path.Combine(current.FullName, "PortfolioScreensaver.sln");
            if (File.Exists(candidate))
                return current.FullName;

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root from test base directory.");
    }
}

