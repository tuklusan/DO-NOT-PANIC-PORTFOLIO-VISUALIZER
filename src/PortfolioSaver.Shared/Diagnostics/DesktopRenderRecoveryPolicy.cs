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
using System.Text.Json;
using System.Security;

namespace PortfolioSaver.Shared.Diagnostics;

public enum DesktopRenderModeSelection
{
    HardwareDefault,
    SoftwareOnly
}

public sealed record DesktopRenderRecoveryDecision(
    DesktopRenderModeSelection SelectedMode,
    string Reason,
    bool PreviousRunWasAbnormal,
    string PreviousRunStatus,
    string? PreviousRunId,
    bool IsExplicitOverride,
    bool RecoveryWasDisabled)
{
    public bool ForceSoftwareRendering => SelectedMode == DesktopRenderModeSelection.SoftwareOnly;

    public string SelectedModeName
        => SelectedMode == DesktopRenderModeSelection.SoftwareOnly ? "software_only" : "hardware_default";
}

public sealed record DesktopRenderRunRegistration(string RunId, string StatePath);

public static class DesktopRenderRecoveryPolicy
{
    public const string StateFileName = "desktop-render-recovery-state.json";
    public const string ForceSoftwareEnvironmentVariable = "DONOTPANIC_FORCE_SOFTWARE_RENDERING";
    public const string ForceHardwareEnvironmentVariable = "DONOTPANIC_FORCE_HARDWARE_RENDERING";
    public const string DisableRecoveryEnvironmentVariable = "DONOTPANIC_DISABLE_RENDER_RECOVERY";
    public const string LegacyForceSoftwareEnvironmentVariable = "PORTFOLIO_SAVER_FORCE_SOFTWARE_RENDER";
    public const string SoftwareRenderingArgument = "--software-rendering";
    public const string HardwareRenderingArgument = "--hardware-rendering";
    public const string DisableRecoveryArgument = "--disable-render-recovery";

    private const int StateVersion = 1;
    private const string RunningStatus = "running";
    private const string CleanExitStatus = "clean_exit";
    private const string OrderlyNonzeroExitStatus = "orderly_nonzero_exit";
    private const string ProcessExitObservedStatus = "process_exit_observed";
    private const string ManagedFatalStatus = "managed_fatal_exception";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly object StateFileSync = new();

    public static DesktopRenderRecoveryDecision Select(
        IEnumerable<string> args,
        string appDataRoot,
        Func<string, string?>? environment = null)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentException.ThrowIfNullOrWhiteSpace(appDataRoot);
        environment ??= Environment.GetEnvironmentVariable;

        string[] normalizedArgs = args
            .Where(arg => !string.IsNullOrWhiteSpace(arg))
            .Select(arg => arg.Trim())
            .ToArray();

        if (ContainsArgument(normalizedArgs, SoftwareRenderingArgument))
            return Explicit(DesktopRenderModeSelection.SoftwareOnly, "command_line_software");

        if (ContainsArgument(normalizedArgs, HardwareRenderingArgument))
            return Explicit(DesktopRenderModeSelection.HardwareDefault, "command_line_hardware");

        if (IsEnabled(environment(ForceSoftwareEnvironmentVariable)) ||
            IsEnabled(environment(LegacyForceSoftwareEnvironmentVariable)))
        {
            return Explicit(DesktopRenderModeSelection.SoftwareOnly, "environment_software");
        }

        if (IsEnabled(environment(ForceHardwareEnvironmentVariable)))
            return Explicit(DesktopRenderModeSelection.HardwareDefault, "environment_hardware");

        bool recoveryDisabled =
            ContainsArgument(normalizedArgs, DisableRecoveryArgument) ||
            IsEnabled(environment(DisableRecoveryEnvironmentVariable));
        StateReadResult previous = ReadState(GetStatePath(appDataRoot));
        if (recoveryDisabled)
        {
            return new DesktopRenderRecoveryDecision(
                DesktopRenderModeSelection.HardwareDefault,
                "recovery_disabled",
                PreviousRunWasAbnormal(previous),
                previous.Status,
                previous.RunId,
                IsExplicitOverride: false,
                RecoveryWasDisabled: true);
        }

        if (previous.WasCorrupt)
        {
            return new DesktopRenderRecoveryDecision(
                DesktopRenderModeSelection.SoftwareOnly,
                "previous_state_unreadable",
                true,
                previous.Status,
                previous.RunId,
                false,
                false);
        }

        if (previous.State is not null && !IsCleanExit(previous.State.Status))
        {
            return new DesktopRenderRecoveryDecision(
                DesktopRenderModeSelection.SoftwareOnly,
                "previous_run_abnormal",
                true,
                previous.State.Status ?? "unknown",
                previous.State.RunId,
                false,
                false);
        }

        return new DesktopRenderRecoveryDecision(
            DesktopRenderModeSelection.HardwareDefault,
            "default",
            false,
            previous.Status,
            previous.RunId,
            false,
            false);
    }

    public static DesktopRenderRunRegistration MarkRunStarted(
        string appDataRoot,
        DesktopRenderRecoveryDecision decision,
        int processId,
        DateTimeOffset startedUtc,
        int? wpfRenderingTier,
        string processRenderMode,
        Action<string>? warningSink = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appDataRoot);
        ArgumentNullException.ThrowIfNull(decision);

        string runId = Guid.NewGuid().ToString("N");
        string statePath = GetStatePath(appDataRoot);
        lock (StateFileSync)
        {
            try
            {
                WriteState(
                    statePath,
                    new DesktopRenderRecoveryState
                    {
                        Version = StateVersion,
                        RunId = runId,
                        Status = RunningStatus,
                        ProcessId = processId,
                        StartedUtc = startedUtc,
                        SelectedRenderMode = decision.SelectedModeName,
                        SelectionReason = decision.Reason,
                        PreviousRunStatus = decision.PreviousRunStatus,
                        PreviousRunWasAbnormal = decision.PreviousRunWasAbnormal,
                        WpfRenderingTier = wpfRenderingTier,
                        ProcessRenderMode = processRenderMode
                    });
            }
            catch (Exception ex) when (IsRecoverableFileSystemException(ex))
            {
                warningSink?.Invoke($"Render recovery state start marker could not be written to '{statePath}': {ex.Message}");
            }
        }

        return new DesktopRenderRunRegistration(runId, statePath);
    }

    public static bool TryMarkCleanExit(DesktopRenderRunRegistration? registration, int exitCode, DateTimeOffset exitedUtc, Action<string>? warningSink = null)
        => TryUpdateCurrentRun(registration, state =>
        {
            if (string.Equals(state.Status, ManagedFatalStatus, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            state.Status = exitCode == 0 ? CleanExitStatus : OrderlyNonzeroExitStatus;
            state.ExitCode = exitCode;
            state.ExitedUtc = exitedUtc;
        }, warningSink);

    public static bool TryMarkProcessExitObserved(DesktopRenderRunRegistration? registration, int exitCode, DateTimeOffset exitedUtc, Action<string>? warningSink = null)
        => TryUpdateCurrentRun(registration, state =>
        {
            if (IsCleanExit(state.Status) || string.Equals(state.Status, ManagedFatalStatus, StringComparison.OrdinalIgnoreCase))
                return;

            state.Status = ProcessExitObservedStatus;
            state.ExitCode = exitCode;
            state.ExitedUtc = exitedUtc;
        }, warningSink);

    public static bool TryMarkManagedFatalException(DesktopRenderRunRegistration? registration, Exception exception, DateTimeOffset observedUtc, Action<string>? warningSink = null)
        => TryUpdateCurrentRun(registration, state =>
        {
            state.Status = ManagedFatalStatus;
            state.ExceptionType = exception.GetType().FullName;
            state.ExceptionMessage = SensitiveDataRedactor.RedactSensitivePatterns(exception.Message);
            state.ExitedUtc = observedUtc;
        }, warningSink);

    public static string GetStatePath(string appDataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appDataRoot);
        return Path.Combine(appDataRoot, "Diagnostics", StateFileName);
    }

    private static DesktopRenderRecoveryDecision Explicit(DesktopRenderModeSelection mode, string reason)
        => new(
            mode,
            reason,
            PreviousRunWasAbnormal: false,
            PreviousRunStatus: "not_considered",
            PreviousRunId: null,
            IsExplicitOverride: true,
            RecoveryWasDisabled: false);

    private static bool TryUpdateCurrentRun(DesktopRenderRunRegistration? registration, Action<DesktopRenderRecoveryState> update, Action<string>? warningSink)
    {
        if (registration is null)
            return false;

        lock (StateFileSync)
        {
            try
            {
                StateReadResult result = ReadState(registration.StatePath);
                DesktopRenderRecoveryState state = result.State ?? new DesktopRenderRecoveryState { Version = StateVersion };
                if (!string.IsNullOrWhiteSpace(state.RunId) && !string.Equals(state.RunId, registration.RunId, StringComparison.Ordinal))
                {
                    warningSink?.Invoke($"Render recovery state update skipped for '{registration.StatePath}' because the run id no longer matches.");
                    return false;
                }

                state.Version = StateVersion;
                state.RunId = registration.RunId;
                update(state);
                WriteState(registration.StatePath, state);
                return true;
            }
            catch (Exception ex) when (IsRecoverableFileSystemException(ex) || ex is JsonException)
            {
                warningSink?.Invoke($"Render recovery state update failed for '{registration.StatePath}': {ex.Message}");
                return false;
            }
        }
    }

    private static bool PreviousRunWasAbnormal(StateReadResult previous)
        => previous.WasCorrupt || (previous.State is not null && !IsCleanExit(previous.State.Status));

    private static bool IsCleanExit(string? status)
        => string.Equals(status, CleanExitStatus, StringComparison.OrdinalIgnoreCase) ||
           string.Equals(status, OrderlyNonzeroExitStatus, StringComparison.OrdinalIgnoreCase);

    private static bool ContainsArgument(IEnumerable<string> args, string expected)
        => args.Any(arg => string.Equals(arg, expected, StringComparison.OrdinalIgnoreCase));

    private static bool IsEnabled(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        string normalized = value.Trim();
        return string.Equals(normalized, "1", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalized, "true", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalized, "yes", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalized, "on", StringComparison.OrdinalIgnoreCase);
    }

    private static StateReadResult ReadState(string statePath)
    {
        try
        {
            if (!File.Exists(statePath))
                return new StateReadResult(null, WasCorrupt: false, Status: "missing", RunId: null);

            string raw = File.ReadAllText(statePath);
            DesktopRenderRecoveryState? state = JsonSerializer.Deserialize<DesktopRenderRecoveryState>(raw);
            if (state is null)
                return new StateReadResult(null, WasCorrupt: true, Status: "unreadable", RunId: null);

            return new StateReadResult(state, WasCorrupt: false, state.Status ?? "unknown", state.RunId);
        }
        catch (Exception ex) when (IsRecoverableFileSystemException(ex) || ex is JsonException)
        {
            return new StateReadResult(null, WasCorrupt: true, Status: "unreadable", RunId: null);
        }
    }

    private static void WriteState(string statePath, DesktopRenderRecoveryState state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(statePath)!);
        string temporaryPath = statePath + ".tmp";
        bool moved = false;
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(state, JsonOptions));
            File.Move(temporaryPath, statePath, overwrite: true);
            moved = true;
        }
        finally
        {
            if (!moved)
                TryDeleteTemporaryStateFile(temporaryPath);
        }
    }

    private static void TryDeleteTemporaryStateFile(string temporaryPath)
    {
        try
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
        catch (Exception ex) when (IsRecoverableFileSystemException(ex))
        {
        }
    }

    private static bool IsRecoverableFileSystemException(Exception ex)
        => ex is IOException or UnauthorizedAccessException or NotSupportedException or SecurityException;

    private sealed record StateReadResult(
        DesktopRenderRecoveryState? State,
        bool WasCorrupt,
        string Status,
        string? RunId);

    private sealed class DesktopRenderRecoveryState
    {
        public int Version { get; set; }
        public string? RunId { get; set; }
        public string? Status { get; set; }
        public int ProcessId { get; set; }
        public DateTimeOffset StartedUtc { get; set; }
        public DateTimeOffset? ExitedUtc { get; set; }
        public int? ExitCode { get; set; }
        public string? SelectedRenderMode { get; set; }
        public string? SelectionReason { get; set; }
        public string? PreviousRunStatus { get; set; }
        public bool PreviousRunWasAbnormal { get; set; }
        public int? WpfRenderingTier { get; set; }
        public string? ProcessRenderMode { get; set; }
        public string? ExceptionType { get; set; }
        public string? ExceptionMessage { get; set; }
    }
}
