using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace PortfolioSaver.Tests.Services;

public sealed class DeepSeekCodeReviewGateTests
{
    [Fact]
    public void ReviewScript_SelfTestAndWhatIfExecuteSuccessfully()
    {
        if (!OperatingSystem.IsWindows())
            return;

        string repoRoot = GetRepoRoot();

        CommandResult selfTest = RunPowerShell(repoRoot, "-NoProfile -ExecutionPolicy Bypass -File .\\build\\Run-DeepSeekCodeReview.ps1 -SelfTest");
        Assert.Equal(0, selfTest.ExitCode);
        Assert.Contains("self-test passed", selfTest.Output, StringComparison.OrdinalIgnoreCase);

        string tempRoot = Path.Combine(Path.GetTempPath(), "DeepSeekCodeReviewGate_" + Guid.NewGuid().ToString("N"));
        try
        {
            // Keep dry-run branch coverage isolated so tests never write probe files into the real checkout.
            // The self-test and documentation checks still exercise the script from the actual repository.
            Directory.CreateDirectory(Path.Combine(tempRoot, "build"));
            File.Copy(
                Path.Combine(repoRoot, "build", "Run-DeepSeekCodeReview.ps1"),
                Path.Combine(tempRoot, "build", "Run-DeepSeekCodeReview.ps1"));
            File.WriteAllText(Path.Combine(tempRoot, ".gitignore"), "build/deepseek-review/" + Environment.NewLine);
            RunPowerShellAndAssertSuccess(tempRoot, "-NoProfile -ExecutionPolicy Bypass -Command \"git init | Out-Null\"");
            RunPowerShellAndAssertSuccess(tempRoot, "-NoProfile -ExecutionPolicy Bypass -Command \"git config user.email test@example.invalid; git config user.name Test\"");
            RunPowerShellAndAssertSuccess(tempRoot, "-NoProfile -ExecutionPolicy Bypass -Command \"git add .; git commit -m init | Out-Null\"");
            File.WriteAllText(Path.Combine(tempRoot, "pending-change.txt"), "pending review");

            CommandResult whatIf = RunPowerShell(tempRoot, "-NoProfile -ExecutionPolicy Bypass -File .\\build\\Run-DeepSeekCodeReview.ps1 -IncludeUntracked -WhatIf");

            Assert.Equal(0, whatIf.ExitCode);
            Assert.Contains("WhatIf requested", whatIf.Output, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("No tracked code/documentation changes found", whatIf.Output, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteTempDirectory(tempRoot);
        }
    }

    [Fact]
    public void ReviewScript_RejectsKnownSecretPatternBeforePacketSend()
    {
        if (!OperatingSystem.IsWindows())
            return;

        string repoRoot = GetRepoRoot();
        string tempRoot = Path.Combine(Path.GetTempPath(), "DeepSeekCodeReviewGate_" + Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(Path.Combine(tempRoot, "build"));
            File.Copy(
                Path.Combine(repoRoot, "build", "Run-DeepSeekCodeReview.ps1"),
                Path.Combine(tempRoot, "build", "Run-DeepSeekCodeReview.ps1"));
            File.WriteAllText(Path.Combine(tempRoot, ".gitignore"), "build/deepseek-review/" + Environment.NewLine);
            RunPowerShellAndAssertSuccess(tempRoot, "-NoProfile -ExecutionPolicy Bypass -Command \"git init | Out-Null\"");
            RunPowerShellAndAssertSuccess(tempRoot, "-NoProfile -ExecutionPolicy Bypass -Command \"git config user.email test@example.invalid; git config user.name Test\"");
            RunPowerShellAndAssertSuccess(tempRoot, "-NoProfile -ExecutionPolicy Bypass -Command \"git commit --allow-empty -m init | Out-Null\"");

            string probePath = Path.Combine(tempRoot, "deepseek-scan-temp.txt");
            string syntheticSecret = "sk-" + "realisticsecretpattern1234567890";
            File.WriteAllText(probePath, $"API_KEY=\"{syntheticSecret}\"");

            CommandResult result = RunPowerShell(tempRoot, "-NoProfile -ExecutionPolicy Bypass -File .\\build\\Run-DeepSeekCodeReview.ps1 -IncludeUntracked -PacketOnly");

            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("Potential secret material detected", result.Error + result.Output, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteTempDirectory(tempRoot);
        }
    }

    [Fact]
    public void ReviewScript_NoChangesReportsSuccess()
    {
        if (!OperatingSystem.IsWindows())
            return;

        string repoRoot = GetRepoRoot();
        string tempRoot = Path.Combine(Path.GetTempPath(), "DeepSeekCodeReviewGate_" + Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(Path.Combine(tempRoot, "build"));
            File.Copy(
                Path.Combine(repoRoot, "build", "Run-DeepSeekCodeReview.ps1"),
                Path.Combine(tempRoot, "build", "Run-DeepSeekCodeReview.ps1"));
            File.WriteAllText(Path.Combine(tempRoot, ".gitignore"), "build/deepseek-review/" + Environment.NewLine);
            RunPowerShellAndAssertSuccess(tempRoot, "-NoProfile -ExecutionPolicy Bypass -Command \"git init | Out-Null\"");
            RunPowerShellAndAssertSuccess(tempRoot, "-NoProfile -ExecutionPolicy Bypass -Command \"git config user.email test@example.invalid; git config user.name Test\"");
            RunPowerShellAndAssertSuccess(tempRoot, "-NoProfile -ExecutionPolicy Bypass -Command \"git add .; git commit -m init | Out-Null\"");

            CommandResult result = RunPowerShell(tempRoot, "-NoProfile -ExecutionPolicy Bypass -File .\\build\\Run-DeepSeekCodeReview.ps1 -IncludeUntracked -PacketOnly");

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("No tracked code/documentation changes found", result.Output, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteTempDirectory(tempRoot);
        }
    }

    [Fact]
    public void ProcessDocs_MakeDeepSeekReviewMandatoryBeforeCommitAndValidation()
    {
        string agents = File.ReadAllText(Path.Combine(GetRepoRoot(), "AGENTS.md"));
        string build = File.ReadAllText(Path.Combine(GetRepoRoot(), "BUILD_AND_DEPLOY.md"));
        string vmRunbook = File.ReadAllText(Path.Combine(GetRepoRoot(), "build", "vm", "VM_OPERATIONS_RUNBOOK.md"));

        Assert.Contains("DeepSeek", agents, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("mandatory", agents, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Run-DeepSeekCodeReview.ps1", agents, StringComparison.Ordinal);
        Assert.Contains("-SendForReview", agents, StringComparison.Ordinal);
        Assert.Contains("-AcknowledgeSecretScan", agents, StringComparison.Ordinal);
        Assert.Contains("commit", agents, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("validation", agents, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("DeepSeek", build, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("mandatory", build, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Run-DeepSeekCodeReview.ps1", build, StringComparison.Ordinal);
        Assert.Contains("-SendForReview", build, StringComparison.Ordinal);
        Assert.Contains("-AcknowledgeSecretScan", build, StringComparison.Ordinal);
        Assert.Contains("local/VM validation", build, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("DeepSeek", vmRunbook, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("mandatory", vmRunbook, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Run-DeepSeekCodeReview.ps1", vmRunbook, StringComparison.Ordinal);
        Assert.Contains("-SendForReview", vmRunbook, StringComparison.Ordinal);
        Assert.Contains("-AcknowledgeSecretScan", vmRunbook, StringComparison.Ordinal);
        Assert.Contains("commit/push", vmRunbook, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RepoRootDetection_SupportsVmSnapshotWithoutGitMetadata()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "DeepSeekCodeReviewGate_" + Guid.NewGuid().ToString("N"));
        string nestedBase = Path.Combine(tempRoot, "tests", "bin", "Release");

        try
        {
            Directory.CreateDirectory(Path.Combine(tempRoot, "build"));
            Directory.CreateDirectory(nestedBase);
            File.WriteAllText(Path.Combine(tempRoot, "PortfolioScreensaver.SLN"), string.Empty);
            File.WriteAllText(Path.Combine(tempRoot, "build", "Run-DeepSeekCodeReview.ps1"), string.Empty);

            Assert.Equal(tempRoot, FindRepoRoot(nestedBase));
        }
        finally
        {
            DeleteTempDirectory(tempRoot);
        }
    }

    private static string GetRepoRoot()
        => FindRepoRoot(AppContext.BaseDirectory);

    private static string FindRepoRoot(string startDirectory)
    {
        DirectoryInfo? current = new(startDirectory);
        while (current is not null)
        {
            string gitMarker = Path.Combine(current.FullName, ".git");
            string reviewScript = Path.Combine(current.FullName, "build", "Run-DeepSeekCodeReview.ps1");
            // The VM harness uploads a clean repository snapshot without .git metadata, so accept
            // the root-level solution file as the snapshot marker when the review script is present.
            bool hasCheckoutOrSnapshotMarker =
                Directory.Exists(gitMarker) ||
                HasTopLevelSolution(current.FullName);
            if (File.Exists(reviewScript) && hasCheckoutOrSnapshotMarker)
                return current.FullName;

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root from test base directory.");
    }

    private static bool HasTopLevelSolution(string directory)
    {
        try
        {
            return Directory
                .EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly)
                .Any(path => string.Equals(Path.GetExtension(path), ".sln", StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return false;
        }
    }

    private static CommandResult RunPowerShell(string workingDirectory, string arguments)
    {
        using Process process = new()
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            }
        };

        process.Start();
        Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
        Task<string> errorTask = process.StandardError.ReadToEndAsync();
        Task exitTask = process.WaitForExitAsync();
        Task timeoutTask = Task.Delay(TimeSpan.FromSeconds(120));
        if (Task.WhenAny(exitTask, timeoutTask).GetAwaiter().GetResult() != exitTask)
        {
            try
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5000);
            }
            catch
            {
                // Best-effort cleanup for a hung local tool process.
            }

            Assert.Fail($"PowerShell command timed out: {arguments}");
        }

        exitTask.GetAwaiter().GetResult();
        string output = outputTask.GetAwaiter().GetResult();
        string error = errorTask.GetAwaiter().GetResult();

        return new CommandResult(process.ExitCode, output, error);
    }

    private static void RunPowerShellAndAssertSuccess(string workingDirectory, string arguments)
    {
        CommandResult result = RunPowerShell(workingDirectory, arguments);
        Assert.Equal(0, result.ExitCode);
    }

    private readonly record struct CommandResult(int ExitCode, string Output, string Error);

    private static void DeleteTempDirectory(string path)
    {
        if (!Directory.Exists(path))
            return;

        try
        {
            foreach (string file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);

            Directory.Delete(path, recursive: true);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Best-effort cleanup failed for temp directory '{path}': {ex.Message}");
        }
    }
}
