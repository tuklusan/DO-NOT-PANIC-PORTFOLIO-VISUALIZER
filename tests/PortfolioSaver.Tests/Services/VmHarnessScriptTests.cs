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
    public void GuestPrepareScript_ClearsPersistedPortfolioSaverStateForCleanBaseline()
    {
        string script = File.ReadAllText(Path.Combine(
            GetRepoRoot(),
            "build",
            "vm",
            "Guest-PrepareVmUxFromShare.ps1"));

        Assert.Contains("$roamingData = Join-Path $env:APPDATA \"PortfolioSaver\"", script, StringComparison.Ordinal);
        Assert.Contains("$localData = Join-Path $env:LOCALAPPDATA \"PortfolioSaver\"", script, StringComparison.Ordinal);
        Assert.Contains("Remove-Item -LiteralPath $roamingData -Recurse -Force", script, StringComparison.Ordinal);
        Assert.Contains("Remove-Item -LiteralPath $localData -Recurse -Force", script, StringComparison.Ordinal);
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

