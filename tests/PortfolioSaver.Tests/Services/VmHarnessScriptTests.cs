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

        Assert.Contains("DO NOT PANIC PORTFOLIO VISUALIZER Config - BETA-5.6", script, StringComparison.Ordinal);
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
        Assert.Contains("[int]$DisplayWidth", script, StringComparison.Ordinal);
        Assert.Contains("[int]$DisplayHeight", script, StringComparison.Ordinal);
        Assert.Contains("[string]$DisplayProfile", script, StringComparison.Ordinal);
        Assert.Contains("agent\\agent-status.json", script, StringComparison.Ordinal);
        Assert.Contains("agent\\command-results\\$uxResultName.result.json", script, StringComparison.Ordinal);
        Assert.Contains("commands\\$uxResultName.json", script, StringComparison.Ordinal);
        Assert.Contains("PsExec.exe", script, StringComparison.Ordinal);
        Assert.Contains("Starting desktop-session agent", script, StringComparison.Ordinal);
        Assert.Contains("Queuing UX run through desktop-session agent", script, StringComparison.Ordinal);
        Assert.Contains("taskkill /IM PortfolioSaver.VmAgent.exe /F >nul 2>&1", script, StringComparison.Ordinal);
        Assert.Contains("Desktop-session agent start attempt failed once; retrying.", script, StringComparison.Ordinal);
        Assert.Contains("Set-Content -LiteralPath $localAgentCommandPath -Encoding UTF8", script, StringComparison.Ordinal);
        Assert.Contains("Send-VmItem -Bundle $bundle -LocalPath $localAgentCommandPath", script, StringComparison.Ordinal);
        Assert.Contains("DisplayWidth = if ($DisplayWidth -gt 0) { $DisplayWidth } else { $null }", script, StringComparison.Ordinal);
        Assert.Contains("DisplayHeight = if ($DisplayHeight -gt 0) { $DisplayHeight } else { $null }", script, StringComparison.Ordinal);
        Assert.Contains("DisplayProfile = if (-not [string]::IsNullOrWhiteSpace($DisplayProfile)) { $DisplayProfile } else { $null }", script, StringComparison.Ordinal);
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
        Assert.Contains("function Perform-KeyboardScrollPass", script, StringComparison.Ordinal);
        Assert.Contains("function Perform-VisibleScrollSequence", script, StringComparison.Ordinal);
        Assert.Contains("function Perform-VisibleConfigActivity", script, StringComparison.Ordinal);
        Assert.Contains("function Write-ConfigWindowTrace", script, StringComparison.Ordinal);
        Assert.Contains("function Get-TopLevelWindowSnapshot", script, StringComparison.Ordinal);
        Assert.Contains("function Test-ConfigPhaseBudget", script, StringComparison.Ordinal);
        Assert.Contains("function Validate-AndCloseConfigWindow", script, StringComparison.Ordinal);
        Assert.Contains("function Get-ConfigBlockingDialog", script, StringComparison.Ordinal);
        Assert.Contains("function Get-ProcessOwnedWindows", script, StringComparison.Ordinal);
        Assert.Contains("function Close-ConfigWindowIfPresent", script, StringComparison.Ordinal);
        Assert.Contains("function Click-AutomationElementCenter", script, StringComparison.Ordinal);
        Assert.Contains("function Get-ConfigStatusText", script, StringComparison.Ordinal);
        Assert.Contains("$automationId -eq 'DesktopMainWindow'", script, StringComparison.Ordinal);
        Assert.Contains("$title -like 'DO NOT PANIC PORTFOLIO VISUALIZER*'", script, StringComparison.Ordinal);
        Assert.Contains("$windowPattern.Current.IsModal", script, StringComparison.Ordinal);
        Assert.Contains("$window = Find-ConfigWindow -Process $desktop -TimeoutSeconds 2", script, StringComparison.Ordinal);
        Assert.Contains("[System.Windows.Forms.SendKeys]::SendWait(' ')", script, StringComparison.Ordinal);
        Assert.Contains("$selected.Current.IsSelected", script, StringComparison.Ordinal);
        Assert.Contains("Find-DescendantByAutomationId -Root $Window -AutomationId 'ConfigStatusText'", script, StringComparison.Ordinal);
        Assert.Contains("$text -like '*Validation passed. Saving and closing in *'", script, StringComparison.Ordinal);
        Assert.Contains("throw 'Validate did not close the config window automatically.'", script, StringComparison.Ordinal);
        Assert.Contains("Validate-AndCloseConfigWindow -Process $desktop -Window $window", script, StringComparison.Ordinal);
        Assert.Contains("Close-ConfigWindowIfPresent -Process $desktop -Window $window", script, StringComparison.Ordinal);
        Assert.Contains("[System.Windows.Automation.AutomationElement]::RootElement.FindAll(", script, StringComparison.Ordinal);
        Assert.Contains("[System.Windows.Automation.TreeScope]::Children", script, StringComparison.Ordinal);
        Assert.Contains("[System.Windows.Forms.SendKeys]::SendWait('{ENTER}')", script, StringComparison.Ordinal);
        Assert.Contains("[System.Windows.Forms.SendKeys]::SendWait('%{F4}')", script, StringComparison.Ordinal);
        Assert.Contains("Capture-Screen -Path (Join-Path $results (\"config-tab-{0:D3}-{1}-scrolled.png\"", script, StringComparison.Ordinal);
        Assert.Contains("return Perform-VisibleScrollSequence -Window $Window -TabName $TabName -PageDownCount $pageDownCount", script, StringComparison.Ordinal);
        Assert.Contains("Try-ScrollWindowContent -Window $Window -TabName $TabName -PageCount ([Math]::Max(1, $PageDownCount))", script, StringComparison.Ordinal);
        Assert.Contains("Invoke-MouseWheelScroll -Element $scrollTarget.Element", script, StringComparison.Ordinal);
        Assert.Contains("config-window-events.log", script, StringComparison.Ordinal);
        Assert.Contains("Config phase exceeded 60 seconds", script, StringComparison.Ordinal);
        Assert.Contains("Write-ConfigWindowTrace -Event 'TabActivityComplete'", script, StringComparison.Ordinal);
        Assert.DoesNotContain("Send-KeySequence -Keys @('{TAB}') -DelayMilliseconds 28", script, StringComparison.Ordinal);
        Assert.DoesNotContain("Send-KeySequence -Keys @('{PGUP}','{HOME}') -DelayMilliseconds 70", script, StringComparison.Ordinal);
        Assert.DoesNotContain("$clickedTab = Click-AutomationElementCenter -Element $tab", script, StringComparison.Ordinal);
        Assert.DoesNotContain("Start-Process -FilePath $configExe", script, StringComparison.Ordinal);
    }

    [Fact]
    public void GuestUxDeepExercise_ConfigInteractionWaitsStayAtOrBelowHalfSecond()
    {
        string script = File.ReadAllText(Path.Combine(
            GetRepoRoot(),
            "build",
            "vm",
            "Guest-UxDeepExercise.ps1"));

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
        Assert.Contains("[int]$DisplayWidth", script, StringComparison.Ordinal);
        Assert.Contains("[int]$DisplayHeight", script, StringComparison.Ordinal);
        Assert.Contains("[string]$DisplayProfile = 'default'", script, StringComparison.Ordinal);
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
        Assert.Contains("SupportedDisplayModes = @()", script, StringComparison.Ordinal);
        Assert.Contains("$summary.SupportedDisplayModes = @(Format-DisplayModeNames -Modes $displayApply.AvailableModes)", script, StringComparison.Ordinal);
        Assert.Contains("RequestedDisplayProfile", script, StringComparison.Ordinal);
        Assert.Contains("RuntimeDesktopResolution = Get-CurrentVirtualScreenSize", script, StringComparison.Ordinal);
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
        Assert.Contains("function Get-LatestDisplayedTapeSample", script, StringComparison.Ordinal);
        Assert.Contains("function Get-PreferredDisplayedTapeSample", script, StringComparison.Ordinal);
        Assert.Contains("function Test-IsDisplayedSampleFullyLive", script, StringComparison.Ordinal);
        Assert.Contains("function Write-ReferenceSpotCheckComparison", script, StringComparison.Ordinal);
        Assert.Contains("query1.finance.yahoo.com/v7/finance/quote", script, StringComparison.Ordinal);
        Assert.Contains("DisplayedVsReferenceFeed", script, StringComparison.Ordinal);
        Assert.Contains(".Replace(\"`0\", '')", script, StringComparison.Ordinal);
        Assert.Contains("[System.IO.Path]::ChangeExtension($Path, '.idx')", script, StringComparison.Ordinal);
        Assert.Contains("[System.IO.File]::ReadAllBytes($Path)", script, StringComparison.Ordinal);
        Assert.Contains("$displayedSample = @(Get-PreferredDisplayedTapeSample)", script, StringComparison.Ordinal);
        Assert.DoesNotContain("function Get-TwelveDataReferenceResults", script, StringComparison.Ordinal);
        Assert.DoesNotContain("PORTFOLIOSAVER_TWELVEDATA_API_KEY", script, StringComparison.Ordinal);
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
        Assert.Contains("function Get-PreferredDisplayedTapeSample", script, StringComparison.Ordinal);
        Assert.Contains("function Test-IsDisplayedSampleFullyLive", script, StringComparison.Ordinal);
        Assert.Contains("function Get-ReferenceResults", script, StringComparison.Ordinal);
        Assert.Contains("DisplayedVsReferenceFeed", script, StringComparison.Ordinal);
        Assert.Contains("reference-spot-check-comparisons.jsonl", script, StringComparison.Ordinal);
        Assert.Contains("combined-trace-tail.txt", script, StringComparison.Ordinal);
        Assert.Contains("yfinance.circular.log", script, StringComparison.Ordinal);
        Assert.Contains("SampleSelection", script, StringComparison.Ordinal);
        Assert.Contains("latest-fully-live", script, StringComparison.Ordinal);
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

