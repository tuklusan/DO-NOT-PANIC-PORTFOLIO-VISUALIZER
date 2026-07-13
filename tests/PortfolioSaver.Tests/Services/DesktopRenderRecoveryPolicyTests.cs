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
using PortfolioSaver.Shared.Diagnostics;
using Xunit;

namespace PortfolioSaver.Tests.Services;

public sealed class DesktopRenderRecoveryPolicyTests
{
    private static readonly string ApiStyleSensitiveKey = string.Concat("api", "_", "key");
    private static readonly string PasswordStyleSensitiveKey = string.Concat("pass", "word");

    [Fact]
    public void Select_DefaultsToHardwareWhenNoPriorStateExists()
    {
        string root = CreateTemporaryRoot();
        try
        {
            DesktopRenderRecoveryDecision decision = DesktopRenderRecoveryPolicy.Select([], root, _ => null);

            Assert.Equal(DesktopRenderModeSelection.HardwareDefault, decision.SelectedMode);
            Assert.Equal("default", decision.Reason);
            Assert.False(decision.PreviousRunWasAbnormal);
            Assert.False(decision.ForceSoftwareRendering);
        }
        finally
        {
            DeleteTemporaryRoot(root);
        }
    }

    [Fact]
    public void Select_ThrowsForNullArgs()
    {
        string root = CreateTemporaryRoot();
        try
        {
            Assert.Throws<ArgumentNullException>(() => DesktopRenderRecoveryPolicy.Select(null!, root, _ => null));
        }
        finally
        {
            DeleteTemporaryRoot(root);
        }
    }

    [Fact]
    public void Select_UsesSoftwareWhenPreviousRunDidNotMarkCleanExit()
    {
        string root = CreateTemporaryRoot();
        try
        {
            DesktopRenderRecoveryDecision firstDecision = DesktopRenderRecoveryPolicy.Select([], root, _ => null);
            DesktopRenderRecoveryPolicy.MarkRunStarted(
                root,
                firstDecision,
                processId: 1234,
                DateTimeOffset.Parse("2026-07-13T12:00:00Z"),
                wpfRenderingTier: 2,
                processRenderMode: "Default");

            DesktopRenderRecoveryDecision secondDecision = DesktopRenderRecoveryPolicy.Select([], root, _ => null);

            Assert.Equal(DesktopRenderModeSelection.SoftwareOnly, secondDecision.SelectedMode);
            Assert.Equal("previous_run_abnormal", secondDecision.Reason);
            Assert.True(secondDecision.PreviousRunWasAbnormal);
            Assert.Equal("running", secondDecision.PreviousRunStatus);
        }
        finally
        {
            DeleteTemporaryRoot(root);
        }
    }

    [Fact]
    public void Select_ReturnsToHardwareAfterCleanExitMarker()
    {
        string root = CreateTemporaryRoot();
        try
        {
            DesktopRenderRecoveryDecision firstDecision = DesktopRenderRecoveryPolicy.Select([], root, _ => null);
            DesktopRenderRunRegistration registration = DesktopRenderRecoveryPolicy.MarkRunStarted(
                root,
                firstDecision,
                processId: 1234,
                DateTimeOffset.Parse("2026-07-13T12:00:00Z"),
                wpfRenderingTier: 2,
                processRenderMode: "Default");
            Assert.True(DesktopRenderRecoveryPolicy.TryMarkCleanExit(
                registration,
                exitCode: 0,
                DateTimeOffset.Parse("2026-07-13T12:05:00Z")));

            DesktopRenderRecoveryDecision secondDecision = DesktopRenderRecoveryPolicy.Select([], root, _ => null);

            Assert.Equal(DesktopRenderModeSelection.HardwareDefault, secondDecision.SelectedMode);
            Assert.Equal("default", secondDecision.Reason);
            Assert.False(secondDecision.PreviousRunWasAbnormal);
            Assert.Equal("clean_exit", secondDecision.PreviousRunStatus);
        }
        finally
        {
            DeleteTemporaryRoot(root);
        }
    }

    [Fact]
    public void Select_ReturnsToHardwareAfterOrderlyNonzeroExitMarker()
    {
        string root = CreateTemporaryRoot();
        try
        {
            DesktopRenderRecoveryDecision firstDecision = DesktopRenderRecoveryPolicy.Select([], root, _ => null);
            DesktopRenderRunRegistration registration = DesktopRenderRecoveryPolicy.MarkRunStarted(
                root,
                firstDecision,
                processId: 1234,
                DateTimeOffset.Parse("2026-07-13T12:00:00Z"),
                wpfRenderingTier: 2,
                processRenderMode: "Default");
            Assert.True(DesktopRenderRecoveryPolicy.TryMarkCleanExit(
                registration,
                exitCode: -1,
                DateTimeOffset.Parse("2026-07-13T12:05:00Z")));

            DesktopRenderRecoveryDecision secondDecision = DesktopRenderRecoveryPolicy.Select([], root, _ => null);

            Assert.Equal(DesktopRenderModeSelection.HardwareDefault, secondDecision.SelectedMode);
            Assert.Equal("default", secondDecision.Reason);
            Assert.False(secondDecision.PreviousRunWasAbnormal);
            Assert.Equal("orderly_nonzero_exit", secondDecision.PreviousRunStatus);
        }
        finally
        {
            DeleteTemporaryRoot(root);
        }
    }


    [Theory]
    [InlineData(DesktopRenderRecoveryPolicy.SoftwareRenderingArgument, DesktopRenderModeSelection.SoftwareOnly, "command_line_software")]
    [InlineData(DesktopRenderRecoveryPolicy.HardwareRenderingArgument, DesktopRenderModeSelection.HardwareDefault, "command_line_hardware")]
    public void Select_CommandLineOverrideWinsOverPreviousAbnormalState(string argument, DesktopRenderModeSelection expectedMode, string expectedReason)
    {
        string root = CreateTemporaryRoot();
        try
        {
            DesktopRenderRecoveryDecision firstDecision = DesktopRenderRecoveryPolicy.Select([], root, _ => null);
            DesktopRenderRecoveryPolicy.MarkRunStarted(
                root,
                firstDecision,
                processId: 1234,
                DateTimeOffset.Parse("2026-07-13T12:00:00Z"),
                wpfRenderingTier: 2,
                processRenderMode: "Default");

            DesktopRenderRecoveryDecision secondDecision = DesktopRenderRecoveryPolicy.Select([argument], root, _ => null);

            Assert.Equal(expectedMode, secondDecision.SelectedMode);
            Assert.Equal(expectedReason, secondDecision.Reason);
            Assert.True(secondDecision.IsExplicitOverride);
            Assert.False(secondDecision.PreviousRunWasAbnormal);
        }
        finally
        {
            DeleteTemporaryRoot(root);
        }
    }

    [Theory]
    [InlineData(DesktopRenderRecoveryPolicy.ForceSoftwareEnvironmentVariable, "1")]
    [InlineData(DesktopRenderRecoveryPolicy.ForceSoftwareEnvironmentVariable, "true")]
    [InlineData(DesktopRenderRecoveryPolicy.ForceSoftwareEnvironmentVariable, "yes")]
    [InlineData(DesktopRenderRecoveryPolicy.ForceSoftwareEnvironmentVariable, "on")]
    [InlineData(DesktopRenderRecoveryPolicy.LegacyForceSoftwareEnvironmentVariable, "true")]
    public void Select_EnvironmentSoftwareOverrideForcesSoftware(string variableName, string value)
    {
        string root = CreateTemporaryRoot();
        try
        {
            DesktopRenderRecoveryDecision decision = DesktopRenderRecoveryPolicy.Select(
                [],
                root,
                name => string.Equals(name, variableName, StringComparison.Ordinal) ? value : null);

            Assert.Equal(DesktopRenderModeSelection.SoftwareOnly, decision.SelectedMode);
            Assert.Equal("environment_software", decision.Reason);
            Assert.True(decision.IsExplicitOverride);
        }
        finally
        {
            DeleteTemporaryRoot(root);
        }
    }

    [Theory]
    [InlineData("0")]
    [InlineData("false")]
    [InlineData("off")]
    public void Select_EnvironmentFalseValuesDoNotForceSoftware(string value)
    {
        string root = CreateTemporaryRoot();
        try
        {
            DesktopRenderRecoveryDecision decision = DesktopRenderRecoveryPolicy.Select(
                [],
                root,
                name => string.Equals(name, DesktopRenderRecoveryPolicy.ForceSoftwareEnvironmentVariable, StringComparison.Ordinal) ? value : null);

            Assert.Equal(DesktopRenderModeSelection.HardwareDefault, decision.SelectedMode);
            Assert.Equal("default", decision.Reason);
        }
        finally
        {
            DeleteTemporaryRoot(root);
        }
    }

    [Theory]
    [InlineData("1")]
    [InlineData("true")]
    [InlineData("yes")]
    [InlineData("on")]
    public void Select_EnvironmentHardwareOverrideWinsOverPreviousAbnormalState(string value)
    {
        string root = CreateTemporaryRoot();
        try
        {
            DesktopRenderRecoveryDecision firstDecision = DesktopRenderRecoveryPolicy.Select([], root, _ => null);
            DesktopRenderRecoveryPolicy.MarkRunStarted(
                root,
                firstDecision,
                processId: 1234,
                DateTimeOffset.Parse("2026-07-13T12:00:00Z"),
                wpfRenderingTier: 2,
                processRenderMode: "Default");

            DesktopRenderRecoveryDecision decision = DesktopRenderRecoveryPolicy.Select(
                [],
                root,
                name => string.Equals(name, DesktopRenderRecoveryPolicy.ForceHardwareEnvironmentVariable, StringComparison.Ordinal) ? value : null);

            Assert.Equal(DesktopRenderModeSelection.HardwareDefault, decision.SelectedMode);
            Assert.Equal("environment_hardware", decision.Reason);
            Assert.True(decision.IsExplicitOverride);
            Assert.False(decision.PreviousRunWasAbnormal);
        }
        finally
        {
            DeleteTemporaryRoot(root);
        }
    }

    [Fact]
    public void Select_RecoveryDisableLeavesHardwareButReportsPriorAbnormalState()
    {
        string root = CreateTemporaryRoot();
        try
        {
            DesktopRenderRecoveryDecision firstDecision = DesktopRenderRecoveryPolicy.Select([], root, _ => null);
            DesktopRenderRecoveryPolicy.MarkRunStarted(
                root,
                firstDecision,
                processId: 1234,
                DateTimeOffset.Parse("2026-07-13T12:00:00Z"),
                wpfRenderingTier: 2,
                processRenderMode: "Default");

            DesktopRenderRecoveryDecision secondDecision = DesktopRenderRecoveryPolicy.Select(
                [DesktopRenderRecoveryPolicy.DisableRecoveryArgument],
                root,
                _ => null);

            Assert.Equal(DesktopRenderModeSelection.HardwareDefault, secondDecision.SelectedMode);
            Assert.Equal("recovery_disabled", secondDecision.Reason);
            Assert.True(secondDecision.PreviousRunWasAbnormal);
            Assert.True(secondDecision.RecoveryWasDisabled);
        }
        finally
        {
            DeleteTemporaryRoot(root);
        }
    }

    [Theory]
    [InlineData("1")]
    [InlineData("true")]
    [InlineData("yes")]
    [InlineData("on")]
    public void Select_EnvironmentRecoveryDisableLeavesHardwareButReportsPriorAbnormalState(string value)
    {
        string root = CreateTemporaryRoot();
        try
        {
            DesktopRenderRecoveryDecision firstDecision = DesktopRenderRecoveryPolicy.Select([], root, _ => null);
            DesktopRenderRecoveryPolicy.MarkRunStarted(
                root,
                firstDecision,
                processId: 1234,
                DateTimeOffset.Parse("2026-07-13T12:00:00Z"),
                wpfRenderingTier: 2,
                processRenderMode: "Default");

            DesktopRenderRecoveryDecision decision = DesktopRenderRecoveryPolicy.Select(
                [],
                root,
                name => string.Equals(name, DesktopRenderRecoveryPolicy.DisableRecoveryEnvironmentVariable, StringComparison.Ordinal) ? value : null);

            Assert.Equal(DesktopRenderModeSelection.HardwareDefault, decision.SelectedMode);
            Assert.Equal("recovery_disabled", decision.Reason);
            Assert.True(decision.PreviousRunWasAbnormal);
            Assert.True(decision.RecoveryWasDisabled);
        }
        finally
        {
            DeleteTemporaryRoot(root);
        }
    }

    [Fact]
    public void Select_RecoveryDisableWithCorruptStateUsesHardwareAndReportsAbnormalState()
    {
        string root = CreateTemporaryRoot();
        try
        {
            string statePath = DesktopRenderRecoveryPolicy.GetStatePath(root);
            Directory.CreateDirectory(Path.GetDirectoryName(statePath)!);
            File.WriteAllText(statePath, "{ this is not json");

            DesktopRenderRecoveryDecision decision = DesktopRenderRecoveryPolicy.Select(
                [DesktopRenderRecoveryPolicy.DisableRecoveryArgument],
                root,
                _ => null);

            Assert.Equal(DesktopRenderModeSelection.HardwareDefault, decision.SelectedMode);
            Assert.Equal("recovery_disabled", decision.Reason);
            Assert.True(decision.PreviousRunWasAbnormal);
            Assert.Equal("unreadable", decision.PreviousRunStatus);
            Assert.True(decision.RecoveryWasDisabled);
        }
        finally
        {
            DeleteTemporaryRoot(root);
        }
    }

    [Fact]
    public void Select_CorruptMarkerFailsTowardSoftwareRecovery()
    {
        string root = CreateTemporaryRoot();
        try
        {
            string statePath = DesktopRenderRecoveryPolicy.GetStatePath(root);
            Directory.CreateDirectory(Path.GetDirectoryName(statePath)!);
            File.WriteAllText(statePath, "{ this is not json");

            DesktopRenderRecoveryDecision decision = DesktopRenderRecoveryPolicy.Select([], root, _ => null);

            Assert.Equal(DesktopRenderModeSelection.SoftwareOnly, decision.SelectedMode);
            Assert.Equal("previous_state_unreadable", decision.Reason);
            Assert.True(decision.PreviousRunWasAbnormal);
        }
        finally
        {
            DeleteTemporaryRoot(root);
        }
    }

    [Fact]
    public void ProcessExitObservedMarkerTriggersSoftwareRecovery()
    {
        string root = CreateTemporaryRoot();
        try
        {
            DesktopRenderRecoveryDecision firstDecision = DesktopRenderRecoveryPolicy.Select([], root, _ => null);
            DesktopRenderRunRegistration registration = DesktopRenderRecoveryPolicy.MarkRunStarted(
                root,
                firstDecision,
                processId: 1234,
                DateTimeOffset.Parse("2026-07-13T12:00:00Z"),
                wpfRenderingTier: 2,
                processRenderMode: "Default");
            Assert.True(DesktopRenderRecoveryPolicy.TryMarkProcessExitObserved(
                registration,
                exitCode: -1,
                DateTimeOffset.Parse("2026-07-13T12:05:00Z")));

            DesktopRenderRecoveryDecision secondDecision = DesktopRenderRecoveryPolicy.Select([], root, _ => null);

            Assert.Equal(DesktopRenderModeSelection.SoftwareOnly, secondDecision.SelectedMode);
            Assert.Equal("previous_run_abnormal", secondDecision.Reason);
            Assert.Equal("process_exit_observed", secondDecision.PreviousRunStatus);
        }
        finally
        {
            DeleteTemporaryRoot(root);
        }
    }

    [Fact]
    public void ProcessExitObservedMarkerDoesNotOverwriteManagedFatalException()
    {
        string root = CreateTemporaryRoot();
        try
        {
            DesktopRenderRecoveryDecision firstDecision = DesktopRenderRecoveryPolicy.Select([], root, _ => null);
            DesktopRenderRunRegistration registration = DesktopRenderRecoveryPolicy.MarkRunStarted(
                root,
                firstDecision,
                processId: 1234,
                DateTimeOffset.Parse("2026-07-13T12:00:00Z"),
                wpfRenderingTier: 2,
                processRenderMode: "Default");
            Assert.True(DesktopRenderRecoveryPolicy.TryMarkManagedFatalException(
                registration,
                new InvalidOperationException($"{ApiStyleSensitiveKey}=should-not-survive"),
                DateTimeOffset.Parse("2026-07-13T12:04:00Z")));
            Assert.True(DesktopRenderRecoveryPolicy.TryMarkProcessExitObserved(
                registration,
                exitCode: -1,
                DateTimeOffset.Parse("2026-07-13T12:05:00Z")));

            string state = File.ReadAllText(DesktopRenderRecoveryPolicy.GetStatePath(root));

            Assert.Contains("managed_fatal_exception", state, StringComparison.Ordinal);
            Assert.Contains(ApiStyleSensitiveKey + "=", state, StringComparison.Ordinal);
            Assert.Contains("redacted", state, StringComparison.Ordinal);
            Assert.DoesNotContain("process_exit_observed", state, StringComparison.Ordinal);
            Assert.DoesNotContain("should-not-survive", state, StringComparison.Ordinal);
        }
        finally
        {
            DeleteTemporaryRoot(root);
        }
    }

    [Fact]
    public void ProcessExitObservedMarkerDoesNotOverwriteCleanExit()
    {
        string root = CreateTemporaryRoot();
        try
        {
            DesktopRenderRecoveryDecision firstDecision = DesktopRenderRecoveryPolicy.Select([], root, _ => null);
            DesktopRenderRunRegistration registration = DesktopRenderRecoveryPolicy.MarkRunStarted(
                root,
                firstDecision,
                processId: 1234,
                DateTimeOffset.Parse("2026-07-13T12:00:00Z"),
                wpfRenderingTier: 2,
                processRenderMode: "Default");
            Assert.True(DesktopRenderRecoveryPolicy.TryMarkCleanExit(
                registration,
                exitCode: 0,
                DateTimeOffset.Parse("2026-07-13T12:04:00Z")));
            Assert.True(DesktopRenderRecoveryPolicy.TryMarkProcessExitObserved(
                registration,
                exitCode: -1,
                DateTimeOffset.Parse("2026-07-13T12:05:00Z")));

            string state = File.ReadAllText(DesktopRenderRecoveryPolicy.GetStatePath(root));

            Assert.Contains("clean_exit", state, StringComparison.Ordinal);
            Assert.DoesNotContain("process_exit_observed", state, StringComparison.Ordinal);
            Assert.Contains("\"ExitCode\": 0", state, StringComparison.Ordinal);
        }
        finally
        {
            DeleteTemporaryRoot(root);
        }
    }

    [Fact]
    public void CleanExitMarkerDoesNotOverwriteManagedFatalException()
    {
        string root = CreateTemporaryRoot();
        try
        {
            DesktopRenderRecoveryDecision firstDecision = DesktopRenderRecoveryPolicy.Select([], root, _ => null);
            DesktopRenderRunRegistration registration = DesktopRenderRecoveryPolicy.MarkRunStarted(
                root,
                firstDecision,
                processId: 1234,
                DateTimeOffset.Parse("2026-07-13T12:00:00Z"),
                wpfRenderingTier: 2,
                processRenderMode: "Default");
            Assert.True(DesktopRenderRecoveryPolicy.TryMarkManagedFatalException(
                registration,
                new InvalidOperationException($"{PasswordStyleSensitiveKey}=should-not-survive"),
                DateTimeOffset.Parse("2026-07-13T12:04:00Z")));
            Assert.True(DesktopRenderRecoveryPolicy.TryMarkCleanExit(
                registration,
                exitCode: 0,
                DateTimeOffset.Parse("2026-07-13T12:05:00Z")));

            string state = File.ReadAllText(DesktopRenderRecoveryPolicy.GetStatePath(root));

            Assert.Contains("managed_fatal_exception", state, StringComparison.Ordinal);
            Assert.Contains(PasswordStyleSensitiveKey + "=", state, StringComparison.Ordinal);
            Assert.Contains("redacted", state, StringComparison.Ordinal);
            Assert.DoesNotContain("clean_exit", state, StringComparison.Ordinal);
            Assert.DoesNotContain("should-not-survive", state, StringComparison.Ordinal);
        }
        finally
        {
            DeleteTemporaryRoot(root);
        }
    }

    [Fact]
    public void CleanExitMarkerOverwritesProcessExitObserved()
    {
        string root = CreateTemporaryRoot();
        try
        {
            DesktopRenderRecoveryDecision firstDecision = DesktopRenderRecoveryPolicy.Select([], root, _ => null);
            DesktopRenderRunRegistration registration = DesktopRenderRecoveryPolicy.MarkRunStarted(
                root,
                firstDecision,
                processId: 1234,
                DateTimeOffset.Parse("2026-07-13T12:00:00Z"),
                wpfRenderingTier: 2,
                processRenderMode: "Default");
            Assert.True(DesktopRenderRecoveryPolicy.TryMarkProcessExitObserved(
                registration,
                exitCode: -1,
                DateTimeOffset.Parse("2026-07-13T12:04:00Z")));
            Assert.True(DesktopRenderRecoveryPolicy.TryMarkCleanExit(
                registration,
                exitCode: 0,
                DateTimeOffset.Parse("2026-07-13T12:05:00Z")));

            string state = File.ReadAllText(DesktopRenderRecoveryPolicy.GetStatePath(root));

            Assert.Contains("clean_exit", state, StringComparison.Ordinal);
            Assert.DoesNotContain("process_exit_observed", state, StringComparison.Ordinal);
        }
        finally
        {
            DeleteTemporaryRoot(root);
        }
    }

    [Fact]
    public void TryMarkCleanExitRejectsRunIdMismatchWithoutChangingFile()
    {
        string root = CreateTemporaryRoot();
        try
        {
            DesktopRenderRecoveryDecision firstDecision = DesktopRenderRecoveryPolicy.Select([], root, _ => null);
            DesktopRenderRunRegistration registration = DesktopRenderRecoveryPolicy.MarkRunStarted(
                root,
                firstDecision,
                processId: 1234,
                DateTimeOffset.Parse("2026-07-13T12:00:00Z"),
                wpfRenderingTier: 2,
                processRenderMode: "Default");
            string statePath = DesktopRenderRecoveryPolicy.GetStatePath(root);
            string mismatchedState = File.ReadAllText(statePath).Replace(registration.RunId, "different-run-id", StringComparison.Ordinal);
            File.WriteAllText(statePath, mismatchedState);

            bool updated = DesktopRenderRecoveryPolicy.TryMarkCleanExit(
                registration,
                exitCode: 0,
                DateTimeOffset.Parse("2026-07-13T12:05:00Z"));

            string state = File.ReadAllText(statePath);
            Assert.False(updated);
            Assert.Contains("different-run-id", state, StringComparison.Ordinal);
            Assert.Contains("running", state, StringComparison.Ordinal);
            Assert.DoesNotContain("clean_exit", state, StringComparison.Ordinal);
        }
        finally
        {
            DeleteTemporaryRoot(root);
        }
    }

    [Fact]
    public void TryMarkCleanExitNormalizesStateVersion()
    {
        string root = CreateTemporaryRoot();
        try
        {
            DesktopRenderRecoveryDecision firstDecision = DesktopRenderRecoveryPolicy.Select([], root, _ => null);
            DesktopRenderRunRegistration registration = DesktopRenderRecoveryPolicy.MarkRunStarted(
                root,
                firstDecision,
                processId: 1234,
                DateTimeOffset.Parse("2026-07-13T12:00:00Z"),
                wpfRenderingTier: 2,
                processRenderMode: "Default");
            string statePath = DesktopRenderRecoveryPolicy.GetStatePath(root);
            File.WriteAllText(statePath, File.ReadAllText(statePath).Replace("\"Version\": 1", "\"Version\": 0", StringComparison.Ordinal));

            Assert.True(DesktopRenderRecoveryPolicy.TryMarkCleanExit(
                registration,
                exitCode: 0,
                DateTimeOffset.Parse("2026-07-13T12:05:00Z")));

            string state = File.ReadAllText(statePath);
            Assert.Contains("\"Version\": 1", state, StringComparison.Ordinal);
            Assert.Contains("clean_exit", state, StringComparison.Ordinal);
        }
        finally
        {
            DeleteTemporaryRoot(root);
        }
    }

    [Fact]
    public void MarkRunStartedWriteFailureUsesNewRunIdSoCleanExitDoesNotEraseStaleMarker()
    {
        string root = CreateTemporaryRoot();
        try
        {
            DesktopRenderRecoveryDecision firstDecision = DesktopRenderRecoveryPolicy.Select([], root, _ => null);
            DesktopRenderRunRegistration staleRegistration = DesktopRenderRecoveryPolicy.MarkRunStarted(
                root,
                firstDecision,
                processId: 1234,
                DateTimeOffset.Parse("2026-07-13T12:00:00Z"),
                wpfRenderingTier: 2,
                processRenderMode: "Default");
            string statePath = DesktopRenderRecoveryPolicy.GetStatePath(root);
            List<string> warnings = [];
            DesktopRenderRunRegistration failedStartRegistration;

            using (new FileStream(statePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                failedStartRegistration = DesktopRenderRecoveryPolicy.MarkRunStarted(
                    root,
                    firstDecision,
                    processId: 5678,
                    DateTimeOffset.Parse("2026-07-13T12:10:00Z"),
                    wpfRenderingTier: 2,
                    processRenderMode: "Default",
                    warnings.Add);

                Assert.NotEqual(staleRegistration.RunId, failedStartRegistration.RunId);
            }

            Assert.NotEmpty(warnings);
            Assert.False(DesktopRenderRecoveryPolicy.TryMarkCleanExit(
                failedStartRegistration,
                exitCode: 0,
                DateTimeOffset.Parse("2026-07-13T12:15:00Z")));

            string state = File.ReadAllText(statePath);
            Assert.Contains("running", state, StringComparison.Ordinal);
            Assert.Contains(staleRegistration.RunId, state, StringComparison.Ordinal);
            Assert.DoesNotContain("clean_exit", state, StringComparison.Ordinal);
        }
        finally
        {
            DeleteTemporaryRoot(root);
        }
    }

    [Fact]
    public void MarkRunStarted_ThrowsForInvalidArguments()
    {
        string root = CreateTemporaryRoot();
        try
        {
            DesktopRenderRecoveryDecision decision = DesktopRenderRecoveryPolicy.Select([], root, _ => null);

            Assert.Throws<ArgumentException>(() => DesktopRenderRecoveryPolicy.MarkRunStarted(
                " ",
                decision,
                processId: 1234,
                DateTimeOffset.Parse("2026-07-13T12:00:00Z"),
                wpfRenderingTier: 2,
                processRenderMode: "Default"));
            Assert.Throws<ArgumentNullException>(() => DesktopRenderRecoveryPolicy.MarkRunStarted(
                root,
                null!,
                processId: 1234,
                DateTimeOffset.Parse("2026-07-13T12:00:00Z"),
                wpfRenderingTier: 2,
                processRenderMode: "Default"));
        }
        finally
        {
            DeleteTemporaryRoot(root);
        }
    }

    [Fact]
    public void TryMarkMethods_ReturnFalseForNullRegistration()
    {
        Assert.False(DesktopRenderRecoveryPolicy.TryMarkCleanExit(null, exitCode: 0, DateTimeOffset.Parse("2026-07-13T12:05:00Z")));
        Assert.False(DesktopRenderRecoveryPolicy.TryMarkProcessExitObserved(null, exitCode: -1, DateTimeOffset.Parse("2026-07-13T12:05:00Z")));
        Assert.False(DesktopRenderRecoveryPolicy.TryMarkManagedFatalException(
            null,
            new InvalidOperationException("boom"),
            DateTimeOffset.Parse("2026-07-13T12:05:00Z")));
    }

    private static string CreateTemporaryRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), "PortfolioSaverTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void DeleteTemporaryRoot(string root)
    {
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
    }
}
