using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Forms;
using PortfolioSaver.Shared.Diagnostics;

namespace PortfolioSaver.VmAgent;

internal static class Program
{
    private const string DefaultRootPath = @"C:\vmharness\portfolio-saver";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    [STAThread]
    private static async Task Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        string rootPath = ParseRootPath(args);
        var agent = new VmDesktopAgent(rootPath);
        await agent.RunAsync();
    }

    private static string ParseRootPath(IReadOnlyList<string> args)
    {
        for (int i = 0; i < args.Count - 1; i++)
        {
            if (string.Equals(args[i], "--root-path", StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }

        return DefaultRootPath;
    }

    private sealed class VmDesktopAgent
    {
        private const long MaxAgentLogBytes = 10 * 1024 * 1024;
        private readonly string _rootPath;
        private readonly string _commandsPath;
        private readonly string _processingPath;
        private readonly string _resultsPath;
        private readonly string _agentPath;
        private readonly string _agentLogsPath;
        private readonly string _logPath;
        private readonly string _statusPath;
        private readonly string _repoRoot;
        private readonly string _publishRoot;
        private readonly string _desktopExe;
        private readonly string _configExe;
        private readonly string _uxScript;
        private readonly string _pwshPath;
        private readonly string _winAppDriverPath;
        private readonly CappedFileLogWriter _logWriter;

        public VmDesktopAgent(string rootPath)
        {
            _rootPath = rootPath;
            _commandsPath = Path.Combine(rootPath, "commands");
            _processingPath = Path.Combine(rootPath, "agent", "processing");
            _resultsPath = Path.Combine(rootPath, "agent", "command-results");
            _agentPath = Path.Combine(rootPath, "agent");
            _agentLogsPath = Path.Combine(_agentPath, "logs");
            _logPath = Path.Combine(_agentPath, "vm-agent.log");
            _statusPath = Path.Combine(_agentPath, "agent-status.json");
            _repoRoot = Path.Combine(rootPath, "repo");
            _publishRoot = Path.Combine(rootPath, "publish");
            _desktopExe = Path.Combine(_publishRoot, "desktop", "PortfolioSaver.Desktop.exe");
            _configExe = Path.Combine(_publishRoot, "config", "PortfolioSaver.Config.exe");
            _uxScript = Path.Combine(_repoRoot, "build", "vm", "Guest-UxDeepExercise.ps1");
            _pwshPath = ResolvePwshPath();
            _winAppDriverPath = @"C:\Program Files (x86)\Windows Application Driver\WinAppDriver.exe";
            _logWriter = new CappedFileLogWriter(_logPath, MaxAgentLogBytes);
        }

        public async Task RunAsync()
        {
            EnsureDirectories();
            Log("VmAgent starting.");

            while (true)
            {
                try
                {
                    EnsureWinAppDriverStarted();
                    UpdateStatus();
                    ProcessCommands();
                }
                catch (Exception ex)
                {
                    Log("Agent loop error: " + ex);
                }

                await Task.Delay(TimeSpan.FromSeconds(2)).ConfigureAwait(true);
            }
        }

        private void EnsureDirectories()
        {
            Directory.CreateDirectory(_rootPath);
            Directory.CreateDirectory(_commandsPath);
            Directory.CreateDirectory(_processingPath);
            Directory.CreateDirectory(_resultsPath);
            Directory.CreateDirectory(_agentPath);
            Directory.CreateDirectory(_agentLogsPath);
        }

        private void ProcessCommands()
        {
            foreach (string commandFile in Directory.GetFiles(_commandsPath, "*.json").OrderBy(Path.GetFileName))
            {
                string fileName = Path.GetFileName(commandFile);
                string processingFile = Path.Combine(_processingPath, fileName);
                string resultFile = Path.Combine(_resultsPath, Path.ChangeExtension(fileName, ".result.json"));

                try
                {
                    if (File.Exists(processingFile))
                    {
                        File.Delete(processingFile);
                    }

                    File.Move(commandFile, processingFile);
                    Log("Processing command " + fileName);

                    AgentCommand? command = JsonSerializer.Deserialize<AgentCommand>(File.ReadAllText(processingFile), JsonOptions);
                    if (command is null)
                    {
                        throw new InvalidOperationException("Command payload was empty.");
                    }

                    AgentCommandResult result = Execute(command);
                    File.WriteAllText(resultFile, JsonSerializer.Serialize(result, JsonOptions));
                    File.Delete(processingFile);
                }
                catch (Exception ex)
                {
                    var failure = new AgentCommandResult
                    {
                        Id = Path.GetFileNameWithoutExtension(fileName),
                        Type = "unknown",
                        Status = "failed",
                        StartedAtUtc = DateTime.UtcNow,
                        FinishedAtUtc = DateTime.UtcNow,
                        Error = ex.ToString()
                    };
                    File.WriteAllText(resultFile, JsonSerializer.Serialize(failure, JsonOptions));
                    Log("Command failed: " + ex);
                    if (File.Exists(processingFile))
                    {
                        File.Delete(processingFile);
                    }
                }
            }
        }

        private AgentCommandResult Execute(AgentCommand command)
        {
            var result = new AgentCommandResult
            {
                Id = command.Id,
                Type = command.Type,
                StartedAtUtc = DateTime.UtcNow
            };

            switch (command.Type?.Trim().ToLowerInvariant())
            {
                case "status":
                    result.Status = "completed";
                    result.Details = Snapshot();
                    break;
                case "ensure-winappdriver":
                    EnsureWinAppDriverStarted(forceRestart: command.Payload?.ForceRestart ?? false);
                    result.Status = "completed";
                    result.Details = Snapshot();
                    break;
                case "launch-desktop":
                    result.Details = LaunchDesktop(command.Payload);
                    result.Status = "completed";
                    break;
                case "run-ux-deep":
                    result.Details = LaunchUxDeep(command);
                    result.Status = "completed";
                    break;
                case "stop-apps":
                    StopPortfolioSaverProcesses();
                    result.Status = "completed";
                    result.Details = Snapshot();
                    break;
                case "capture-screen":
                    result.Details = CaptureScreen(command);
                    result.Status = "completed";
                    break;
                default:
                    throw new InvalidOperationException("Unknown command type: " + command.Type);
            }

            result.FinishedAtUtc = DateTime.UtcNow;
            return result;
        }

        private object LaunchDesktop(AgentCommandPayload? payload)
        {
            StopPortfolioSaverProcesses();

            string arguments = payload?.Arguments ?? string.Empty;
            var startInfo = new ProcessStartInfo
            {
                FileName = _desktopExe,
                Arguments = arguments,
                WorkingDirectory = Path.GetDirectoryName(_desktopExe) ?? _publishRoot,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            Process process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Desktop process did not start.");

            return new
            {
                ProcessId = process.Id,
                Executable = _desktopExe
            };
        }

        private object LaunchUxDeep(AgentCommand command)
        {
            StopPortfolioSaverProcesses();

            string resultName = command.Payload?.ResultName
                ?? ("ux-deep-agent-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            string resultRoot = command.Payload?.ResultRootPath ?? Path.Combine(_rootPath, "results");
            Directory.CreateDirectory(resultRoot);
            string scriptsPath = Path.Combine(_rootPath, "scripts");
            Directory.CreateDirectory(scriptsPath);
            string stdoutPath = Path.Combine(_agentLogsPath, $"{resultName}.stdout.log");
            string stderrPath = Path.Combine(_agentLogsPath, $"{resultName}.stderr.log");
            string launcherPath = Path.Combine(scriptsPath, $"agent-launch-{resultName}.cmd");
            int duration = command.Payload?.ScreensaverDurationMinutes ?? 20;
            int captureInterval = command.Payload?.CaptureIntervalSeconds ?? 5;
            string validationCompletionMode = string.Equals(command.Payload?.ValidationCompletionMode, "Cancel", StringComparison.OrdinalIgnoreCase)
                ? "Cancel"
                : "Apply";
            int? displayWidth = command.Payload?.DisplayWidth;
            int? displayHeight = command.Payload?.DisplayHeight;
            string? displayProfile = command.Payload?.DisplayProfile;
            string displayArguments = string.Empty;
            if (displayWidth.HasValue && displayHeight.HasValue && displayWidth.Value > 0 && displayHeight.Value > 0)
            {
                displayArguments = $" -DisplayWidth {displayWidth.Value} -DisplayHeight {displayHeight.Value}";
            }

            if (!string.IsNullOrWhiteSpace(displayProfile))
            {
                displayArguments += $" -DisplayProfile \"{displayProfile.Replace("\"", "\\\"")}\"";
            }

            string launcherContents = string.Join(Environment.NewLine, new[]
            {
                "@echo off",
                $"cd /d \"{_rootPath}\"",
                $"\"{_pwshPath}\" -NoLogo -NoProfile -ExecutionPolicy Bypass -File \"{_uxScript}\" -RootPath \"{_rootPath}\" -ResultName \"{resultName}\" -ResultRootPath \"{resultRoot}\" -ValidationCompletionMode \"{validationCompletionMode}\" -ScreensaverDurationMinutes {duration} -CaptureIntervalSeconds {captureInterval}{displayArguments} 1>\"{stdoutPath}\" 2>\"{stderrPath}\""
            });
            File.WriteAllText(launcherPath, launcherContents);

            var startInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/c \"" + launcherPath + "\"",
                WorkingDirectory = _rootPath,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            Process process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("UX deep process did not start.");

            return new
            {
                ProcessId = process.Id,
                ResultName = resultName,
                SummaryPath = Path.Combine(resultRoot, resultName, "ux-deep-summary.json"),
                StdoutPath = stdoutPath,
                StderrPath = stderrPath
            };
        }

        private object CaptureScreen(AgentCommand command)
        {
            string outputPath = command.Payload?.OutputPath
                ?? Path.Combine(_resultsPath, $"{command.Id}.png");
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

            Rectangle bounds = SystemInformation.VirtualScreen;
            using var bitmap = new Bitmap(bounds.Width, bounds.Height);
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.CopyFromScreen(bounds.X, bounds.Y, 0, 0, bitmap.Size);
            }

            bitmap.Save(outputPath, ImageFormat.Png);
            return new { OutputPath = outputPath };
        }

        private void EnsureWinAppDriverStarted(bool forceRestart = false)
        {
            if (!File.Exists(_winAppDriverPath))
            {
                Log("WinAppDriver missing at " + _winAppDriverPath);
                return;
            }

            Process[] existing = Process.GetProcessesByName("WinAppDriver");
            if (forceRestart)
            {
                foreach (Process process in existing)
                {
                    try { process.Kill(true); }
                    catch { }
                }
                existing = Array.Empty<Process>();
            }

            if (existing.Length > 0)
            {
                return;
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = _winAppDriverPath,
                Arguments = "127.0.0.1 4723/wd/hub",
                WorkingDirectory = Path.GetDirectoryName(_winAppDriverPath) ?? @"C:\",
                UseShellExecute = false,
                CreateNoWindow = true
            };

            Process.Start(startInfo);
            Log("Started WinAppDriver.");
        }

        private void StopPortfolioSaverProcesses()
        {
            foreach (string processName in new[] { "PortfolioSaver.Desktop", "PortfolioSaver.Config", "PortfolioSaver.Screensaver", "pwsh", "powershell" })
            {
                foreach (Process process in Process.GetProcessesByName(processName))
                {
                    try
                    {
                        string? fileName = null;
                        try { fileName = process.MainModule?.FileName; }
                        catch { }

                        bool isPortfolioProcess = processName.StartsWith("PortfolioSaver", StringComparison.OrdinalIgnoreCase)
                            || (!string.IsNullOrWhiteSpace(fileName) && fileName.StartsWith(_rootPath, StringComparison.OrdinalIgnoreCase));

                        if (!isPortfolioProcess)
                        {
                            continue;
                        }

                        process.Kill(true);
                        Log("Stopped process " + process.ProcessName + " (" + process.Id + ")");
                    }
                    catch (Exception ex)
                    {
                        Log("Failed to stop " + process.ProcessName + ": " + ex.Message);
                    }
                }
            }
        }

        private AgentStatus Snapshot()
        {
            Process current = Process.GetCurrentProcess();
            return new AgentStatus
            {
                RootPath = _rootPath,
                LastHeartbeatUtc = DateTime.UtcNow,
                ProcessId = current.Id,
                SessionId = current.SessionId,
                UserInteractive = Environment.UserInteractive,
                DesktopExecutableExists = File.Exists(_desktopExe),
                ConfigExecutableExists = File.Exists(_configExe),
                WinAppDriverRunning = Process.GetProcessesByName("WinAppDriver").Length > 0,
                PortfolioProcesses = Process.GetProcesses()
                    .Where(p => p.ProcessName.StartsWith("PortfolioSaver", StringComparison.OrdinalIgnoreCase))
                    .Select(p => new AgentProcessSnapshot
                    {
                        ProcessName = p.ProcessName,
                        ProcessId = p.Id,
                        SessionId = p.SessionId
                    })
                    .ToArray()
            };
        }

        private void UpdateStatus()
        {
            File.WriteAllText(_statusPath, JsonSerializer.Serialize(Snapshot(), JsonOptions));
        }

        private void Log(string message)
        {
            try
            {
                _logWriter.WriteLine($"[{DateTime.Now:O}] {message}");
            }
            catch (Exception ex)
            {
                Trace.WriteLine("VmAgent log write failed: " + ex);
            }
        }

        private static string ResolvePwshPath()
        {
            const string preferred = @"C:\Program Files\PowerShell\7\pwsh.exe";
            return File.Exists(preferred) ? preferred : "powershell.exe";
        }

        private static string Quote(string value)
        {
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }
    }

    private sealed class AgentCommand
    {
        public string Id { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public AgentCommandPayload? Payload { get; set; }
    }

    private sealed class AgentCommandPayload
    {
        public string? Arguments { get; set; }
        public string? ResultName { get; set; }
        public string? ResultRootPath { get; set; }
        public int? ScreensaverDurationMinutes { get; set; }
        public int? CaptureIntervalSeconds { get; set; }
        public string? ValidationCompletionMode { get; set; }
        public int? DisplayWidth { get; set; }
        public int? DisplayHeight { get; set; }
        public string? DisplayProfile { get; set; }
        public string? OutputPath { get; set; }
        public bool ForceRestart { get; set; }
    }

    private sealed class AgentCommandResult
    {
        public string Id { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public DateTime StartedAtUtc { get; set; }
        public DateTime FinishedAtUtc { get; set; }
        public object? Details { get; set; }
        public string? Error { get; set; }
    }

    private sealed class AgentStatus
    {
        public string RootPath { get; set; } = string.Empty;
        public DateTime LastHeartbeatUtc { get; set; }
        public int ProcessId { get; set; }
        public int SessionId { get; set; }
        public bool UserInteractive { get; set; }
        public bool DesktopExecutableExists { get; set; }
        public bool ConfigExecutableExists { get; set; }
        public bool WinAppDriverRunning { get; set; }
        public AgentProcessSnapshot[] PortfolioProcesses { get; set; } = Array.Empty<AgentProcessSnapshot>();
    }

    private sealed class AgentProcessSnapshot
    {
        public string ProcessName { get; set; } = string.Empty;
        public int ProcessId { get; set; }
        public int SessionId { get; set; }
    }
}
