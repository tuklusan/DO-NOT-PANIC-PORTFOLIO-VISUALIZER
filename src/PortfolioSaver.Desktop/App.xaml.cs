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
using System.Windows.Threading;
using PortfolioSaver.Config.Services;
using PortfolioSaver.Core.Enums;
using PortfolioSaver.Core.Models;
using PortfolioSaver.Shared.Diagnostics;
using PortfolioSaver.Shared.Integrity;
using PortfolioSaver.Shared.Services;
using PortfolioSaver.Desktop.Windows;
using PortfolioSaver.Screensaver.Services;

namespace PortfolioSaver.Desktop;

public partial class App : Application
{
    private const string SingleInstanceMutexName = "Local\\DoNotPanicPortfolioVisualizer.Desktop";
    private static Mutex? singleInstanceMutex;
    private static bool ownsSingleInstance;

    protected override async void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += (_, args) =>
        {
            TraceLog.Error("Desktop.App", "DispatcherUnhandledException", args.Exception);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            TraceLog.Error("Desktop.App", $"UnhandledException: {args.ExceptionObject}");
        };

        TraceLog.Info("Desktop.App", $"Startup args: {string.Join(" ", e.Args)}");
        if (!TryAcquireSingleInstance())
        {
            TraceLog.Warn("Desktop.App", "Duplicate desktop instance launch blocked.");
            base.OnStartup(e);
            ShowDuplicateInstanceNotice();
            Shutdown(0);
            return;
        }

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

        string? aiFallbackWarning = await CheckConfiguredAiNewsAccessAsync();

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
        if (!string.IsNullOrWhiteSpace(aiFallbackWarning))
        {
            Dispatcher.BeginInvoke(
                DispatcherPriority.ApplicationIdle,
                new Action(() => MessageBox.Show(
                    window,
                    aiFallbackWarning,
                    "DO NOT PANIC PORTFOLIO VISUALIZER",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning)));
        }

        QueueReleaseIntegrityValidation();
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

    private static async Task<string?> CheckConfiguredAiNewsAccessAsync()
    {
        SettingsFileService settingsFileService = new();
        AppSettings settings = settingsFileService.Load();
        if (settings.NewsScrollerMode != NewsScrollerMode.SummarizedFinancialNews ||
            string.IsNullOrWhiteSpace(settings.DeepSeekApiKey))
        {
            return null;
        }

        try
        {
            using HttpClient httpClient = new() { Timeout = TimeSpan.FromSeconds(4) };
            using CancellationTokenSource cts = new(TimeSpan.FromSeconds(4));
            FinanceNewsService service = new();
            FinanceNewsService.AiNewsAccessCheckResult result =
                await service.CheckSummarizedNewsAccessAsync(httpClient, settings, cts.Token);
            if (!result.WasChecked || result.Succeeded)
                return null;

            TraceLog.Warn("Desktop.App", $"AI summarized news access failed at startup; falling back to RSS. reason={result.Reason}");
            ScreensaverSettingsService.ForceRssNewsForCurrentSession();
            return "AI summarized financial news is not available right now. DO NOT PANIC will use RSS financial news instead.";
        }
        catch (Exception ex)
        {
            TraceLog.Error("Desktop.App", "AI summarized news startup check failed unexpectedly; falling back to RSS.", ex);
            ScreensaverSettingsService.ForceRssNewsForCurrentSession();
            return "AI summarized financial news could not be verified. DO NOT PANIC will use RSS financial news instead.";
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (ownsSingleInstance)
        {
            OwnedServerShutdownQueue.QueueShutdown("Desktop.App");
            try
            {
                singleInstanceMutex?.ReleaseMutex();
            }
            catch (ApplicationException ex)
            {
                TraceLog.Warn("Desktop.App", $"Single instance mutex release skipped: {ex.Message}");
            }
        }

        singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }
}
