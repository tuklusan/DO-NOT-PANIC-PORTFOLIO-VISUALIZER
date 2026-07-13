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
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using PortfolioSaver.Config.Services;
using PortfolioSaver.Core.Enums;
using PortfolioSaver.Core.Models;
using PortfolioSaver.Shared.Diagnostics;
using PortfolioSaver.Shared.Integrity;
using PortfolioSaver.Shared.Services;
using PortfolioSaver.Desktop.Windows;
using PortfolioSaver.Presentation.Services;

namespace PortfolioSaver.Desktop;

public partial class App : Application
{
    private const string SingleInstanceMutexName = "Local\\DoNotPanicPortfolioVisualizer.Desktop";
    // OpenRouter free models can take longer than a fast LAN probe; run this after the window is visible.
    private static readonly TimeSpan AiNewsStartupProbeTimeout = TimeSpan.FromSeconds(15);
    private static Mutex? singleInstanceMutex;
    private static bool ownsSingleInstance;
    private static DesktopRenderRunRegistration? renderRunRegistration;
    // 0 = unclaimed, 1 = WPF OnExit owns clean-exit marking, 2 = AppDomain.ProcessExit fallback owns abnormal marking.
    private static int exitMarkerClaim;

    protected override async void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += (_, args) =>
        {
            TraceLog.Error("Desktop.App", "DispatcherUnhandledException", args.Exception);
            DesktopRenderRecoveryPolicy.TryMarkManagedFatalException(renderRunRegistration, args.Exception, DateTimeOffset.UtcNow, LogRenderRecoveryWarning);
            args.Handled = true;
            Shutdown(-1);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            TraceLog.Error("Desktop.App", $"UnhandledException: {args.ExceptionObject}");
            if (args.ExceptionObject is Exception ex)
                DesktopRenderRecoveryPolicy.TryMarkManagedFatalException(renderRunRegistration, ex, DateTimeOffset.UtcNow, LogRenderRecoveryWarning);
        };
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            if (Interlocked.CompareExchange(ref exitMarkerClaim, 2, 0) == 0)
            {
                bool marked = DesktopRenderRecoveryPolicy.TryMarkProcessExitObserved(renderRunRegistration, Environment.ExitCode, DateTimeOffset.UtcNow, LogRenderRecoveryWarning);
                if (!marked)
                    LogRenderRecoveryWarning("Render recovery process-exit marker was not written; previous running state may remain for next launch.");
            }
        };

        DesktopRenderRecoveryDataRoot dataRoot = DesktopRenderRecoveryDataRootResolver.Resolve(LogRenderRecoveryWarning);
        DesktopRenderRecoveryDecision renderDecision = DesktopRenderRecoveryPolicy.Select(e.Args, dataRoot.Root);
        TraceLog.Info("Desktop.App", $"Startup args: {string.Join(" ", e.Args)}");
        if (!TryAcquireSingleInstance())
        {
            TraceLog.Warn("Desktop.App", "Duplicate desktop instance launch blocked.");
            base.OnStartup(e);
            ShowDuplicateInstanceNotice();
            Shutdown(0);
            return;
        }

        renderRunRegistration = DesktopRenderRecoveryPolicy.MarkRunStarted(
            dataRoot.Root,
            renderDecision,
            Environment.ProcessId,
            DateTimeOffset.UtcNow,
            wpfRenderingTier: null,
            processRenderMode: "pending_apply",
            LogRenderRecoveryWarning);
        ApplyRenderPolicy(renderDecision);
        TraceRenderPolicy(renderDecision);

        try
        {
            await YFinanceServerProcessManager.EnsureOwnedServerAsync("PortfolioSaver.Desktop");
        }
        catch (Exception ex)
        {
            TraceLog.Error("Desktop.App", "Owned YFinance server startup failed.", ex);
            Shutdown(-1);
            return;
        }

        bool startFullScreen = e.Args.Any(arg => string.Equals(arg, "--fullscreen", StringComparison.OrdinalIgnoreCase));
        base.OnStartup(e);

        var window = new MainWindow();
        if (startFullScreen)
        {
            window.Loaded += (_, _) =>
            {
                window.Dispatcher.BeginInvoke(
                    DispatcherPriority.ApplicationIdle,
                    new Action(window.EnterFullScreen));
            };
        }

        MainWindow = window;
        window.Show();

        QueueConfiguredAiNewsAccessCheck();
        QueueReleaseIntegrityValidation();
    }

    private static void ApplyRenderPolicy(DesktopRenderRecoveryDecision decision)
    {
        RenderOptions.ProcessRenderMode = decision.ForceSoftwareRendering
            ? System.Windows.Interop.RenderMode.SoftwareOnly
            : System.Windows.Interop.RenderMode.Default;
    }

    private static void TraceRenderPolicy(DesktopRenderRecoveryDecision decision)
        => TraceLog.InfoState(
            "Desktop.Render",
            "RenderPolicySelected",
            [
                new("selected_mode", decision.SelectedModeName),
                new("reason", decision.Reason),
                new("is_explicit_override", decision.IsExplicitOverride),
                new("recovery_was_disabled", decision.RecoveryWasDisabled),
                new("previous_run_was_abnormal", decision.PreviousRunWasAbnormal),
                new("previous_run_status", decision.PreviousRunStatus),
                new("previous_run_id", decision.PreviousRunId ?? "<none>"),
                new("wpf_rendering_tier", GetWpfRenderingTier()),
                new("process_render_mode", RenderOptions.ProcessRenderMode)
            ]);

    private static int GetWpfRenderingTier()
        => RenderCapability.Tier >> 16;

    private static void LogRenderRecoveryWarning(string message)
        => TraceLog.Warn("Desktop.Render", message);

    private static void QueueConfiguredAiNewsAccessCheck()
    {
        Task probeTask = Task.Run(CheckConfiguredAiNewsAccessAsync);
        _ = probeTask.ContinueWith(
            faulted => TraceLog.Error("Desktop.App", "AI summarized news startup probe task faulted before internal handling.", faulted.Exception),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);
    }

    private void QueueReleaseIntegrityValidation()
        => ReleaseManifestGuard.ValidateCurrentExecutableInBackground(
            "Desktop.App",
            integritySummary => Dispatcher.BeginInvoke(new Action(() =>
            {
                if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
                    return;

                MessageBox.Show(
                    "Release integrity check failed. This build may be stale or corrupted." +
                    Environment.NewLine + Environment.NewLine +
                    integritySummary,
                    "DO NOT PANIC PORTFOLIO VISUALIZER",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                Shutdown(-1);
            })));

    private static bool TryAcquireSingleInstance()
    {
        singleInstanceMutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out bool createdNew);
        if (createdNew)
        {
            ownsSingleInstance = true;
            return true;
        }

        try
        {
            ownsSingleInstance = singleInstanceMutex.WaitOne(0);
            return ownsSingleInstance;
        }
        catch (AbandonedMutexException)
        {
            ownsSingleInstance = true;
            return true;
        }
    }

    private static void ShowDuplicateInstanceNotice()
    {
        var message = new TextBlock
        {
            Text = "Sorry, DO NOT PANIC is already active.",
            Margin = new Thickness(24),
            TextWrapping = TextWrapping.Wrap,
            FontSize = 18,
            FontWeight = FontWeights.SemiBold
        };

        var window = new Window
        {
            Title = "DO NOT PANIC PORTFOLIO VISUALIZER",
            Content = message,
            Width = 420,
            Height = 150,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Topmost = true,
            ShowInTaskbar = false
        };

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            window.Close();
        };

        timer.Start();
        window.ShowDialog();
    }

    private static async Task CheckConfiguredAiNewsAccessAsync()
    {
        SettingsFileService settingsFileService = new();
        AppSettings settings = settingsFileService.Load();
        if (settings.NewsScrollerMode != NewsScrollerMode.SummarizedFinancialNews ||
            string.IsNullOrWhiteSpace(settings.AiApiKey))
        {
            return;
        }

        try
        {
            // Keep the startup probe bounded as a whole; ordinary refreshes still retry AI independently.
            using CancellationTokenSource cts = new(AiNewsStartupProbeTimeout);
            using HttpClient httpClient = new();
            FinanceNewsService service = new();
            FinanceNewsService.AiNewsAccessCheckResult result =
                await service.CheckSummarizedNewsAccessAsync(httpClient, settings, cts.Token);
            if (!result.WasChecked || result.Succeeded)
                return;

            // Startup probe only: ordinary feed refreshes keep retrying AI and fall back locally per refresh.
            TraceLog.Warn("Desktop.App", $"AI summarized news access failed at startup; summarized news will retry on the normal refresh cadence. reason={result.Reason}");
        }
        catch (Exception ex)
        {
            TraceLog.Error("Desktop.App", "AI summarized news startup check failed unexpectedly; summarized news will retry on the normal refresh cadence.", ex);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        int previousExitMarkerClaim = Interlocked.Exchange(ref exitMarkerClaim, 1);
        bool shouldMarkCleanExit = previousExitMarkerClaim == 0;
        TraceLog.InfoState("Desktop.App", "OnExitStart", [new("exit_code", e.ApplicationExitCode)]);
        try
        {
            if (ownsSingleInstance)
            {
                try
                {
                    OwnedServerShutdownQueue.QueueShutdown("Desktop.App");
                    singleInstanceMutex?.ReleaseMutex();
                }
                catch (Exception ex) when (ex is ApplicationException or ObjectDisposedException)
                {
                    TraceLog.Warn("Desktop.App", $"Single instance mutex release skipped: {ex.Message}");
                }
                catch (Exception ex)
                {
                    TraceLog.Error("Desktop.App", "OnExit owned-resource cleanup failed.", ex);
                }
            }
        }
        finally
        {
            if (shouldMarkCleanExit)
                DesktopRenderRecoveryPolicy.TryMarkCleanExit(renderRunRegistration, e.ApplicationExitCode, DateTimeOffset.UtcNow, LogRenderRecoveryWarning);

            TraceLog.InfoState(
                "Desktop.App",
                "OnExitComplete",
                [
                    new("exit_code", e.ApplicationExitCode),
                    new("marked_clean_exit", shouldMarkCleanExit),
                    new("previous_exit_marker_claim", previousExitMarkerClaim)
                ]);
            singleInstanceMutex?.Dispose();
            base.OnExit(e);
        }
    }
}
