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
        Assert.Contains("setvideomodehint", runbook, StringComparison.OrdinalIgnoreCase);
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
    public void InvokeVmBuildTest_UsesGuestSideLauncherAndPollsForFinishedSummary()
    {
        string script = File.ReadAllText(Path.Combine(
            GetRepoRoot(),
            "build",
            "vm",
            "Invoke-VmBuildTest.ps1"));

        Assert.Contains("scripts\\launch-$uxResultName.cmd", script, StringComparison.Ordinal);
        Assert.Contains("PsExec.exe", script, StringComparison.Ordinal);
        Assert.Contains("cmd /c", script, StringComparison.Ordinal);
        Assert.Contains("$summary.PSObject.Properties.Name -contains 'FinishedAt'", script, StringComparison.Ordinal);
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
        Assert.Contains("MainWindowTitleFallback", script, StringComparison.Ordinal);
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
        Assert.Contains("$root = $RootPath", script, StringComparison.Ordinal);
        Assert.Contains("$summary.ExportMode = 'LocalWorkspace'", script, StringComparison.Ordinal);
        Assert.Contains("$localTraceTarget = Join-Path $results 'trace'", script, StringComparison.Ordinal);
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
        Assert.Contains("function Get-LatestDisplayedTapeSample", script, StringComparison.Ordinal);
        Assert.Contains("function Write-ReferenceSpotCheckComparison", script, StringComparison.Ordinal);
        Assert.Contains("query1.finance.yahoo.com/v7/finance/quote", script, StringComparison.Ordinal);
        Assert.Contains("DisplayedVsYahooFinance", script, StringComparison.Ordinal);
    }

    [Fact]
    public void SshHarnessScripts_UseVmharnessWorkspaceAndDoNotDependOnVBox()
    {
        string push = File.ReadAllText(Path.Combine(GetRepoRoot(), "build", "vm", "Push-VmWorkspace.ps1"));
        string invoke = File.ReadAllText(Path.Combine(GetRepoRoot(), "build", "vm", "Invoke-VmBuildTest.ps1"));
        string pull = File.ReadAllText(Path.Combine(GetRepoRoot(), "build", "vm", "Pull-VmResults.ps1"));
        string bootstrap = File.ReadAllText(Path.Combine(GetRepoRoot(), "build", "vm", "Guest-BootstrapVmRemoteTools.ps1"));
        string helper = File.ReadAllText(Path.Combine(GetRepoRoot(), "build", "vm", "VmSshCommon.ps1"));

        Assert.Contains(@"C:\vmharness\portfolio-saver", push, StringComparison.Ordinal);
        Assert.Contains(@"C:\vmharness\portfolio-saver", invoke, StringComparison.Ordinal);
        Assert.Contains(@"C:\vmharness\portfolio-saver", pull, StringComparison.Ordinal);
        Assert.Contains(@"C:\vmharness\portfolio-saver", bootstrap, StringComparison.Ordinal);
        Assert.Contains("Posh-SSH", helper, StringComparison.Ordinal);
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

