// ============================================================================
// Copyright (c) 2026 Supratim Sanyal of SANYALnet Labs.
// Proprietary rights reserved except as expressly licensed herein.
//
// DO NOT PANIC PORTFOLIO VIEWER
// This software and its derivatives are licensed for STRICTLY NON-COMMERCIAL,
// personal, educational, or hobbyist use only. Commercial exploitation,
// corporate internal operations, or AI model training are strictly forbidden.
//
// ATTRIBUTION & DEPENDENCIES: This application incorporates the YFinance library,
// which is licensed under the Apache License, Version 2.0. A copy of the Apache
// License is provided within the distribution environment.
//
// FINANCIAL DISCLAIMER: This software is a passive visualization tool only.
// It does not provide financial, investment, legal, or tax advice. All data
// calculation and scraping outputs are provided 'AS IS' with zero guarantee
// of real-time accuracy or upstream availability.
//
// This file is subject to the terms and conditions defined in the LICENSE
// file located in the root directory of this source code repository.
// Removal or modification of this legal notice constitutes copyright infringement.
// ============================================================================
using System.Windows;
using System.Windows.Media;
using PortfolioSaver.Shared.Diagnostics;
using PortfolioSaver.Shared.Integrity;
using PortfolioSaver.Shared.Services;

namespace PortfolioSaver.Screensaver;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        if (TraceLog.ShouldForceSoftwareRendering())
        {
            RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
            TraceLog.Info("Screensaver.App", "Software rendering enabled.");
        }

        DispatcherUnhandledException += (_, args) =>
        {
            TraceLog.Error("Screensaver.App", "DispatcherUnhandledException", args.Exception);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            TraceLog.Error("Screensaver.App", $"UnhandledException: {args.ExceptionObject}");
        };

        TraceLog.Info("Screensaver.App", $"Startup args: {string.Join(" ", e.Args)}");
        try
        {
            await YFinanceServerProcessManager.EnsureOwnedServerAsync("PortfolioSaver.Screensaver");
        }
        catch (Exception ex)
        {
            TraceLog.Error("Screensaver.App", "Owned YFinance server startup failed.", ex);
            Shutdown(-1);
            return;
        }

        base.OnStartup(e);
        QueueReleaseIntegrityValidation();
    }

    private void QueueReleaseIntegrityValidation()
        => ReleaseManifestGuard.ValidateCurrentExecutableInBackground(
            "Screensaver.App",
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

    protected override void OnExit(ExitEventArgs e)
    {
        OwnedServerShutdownQueue.QueueShutdown("Screensaver.App");
        base.OnExit(e);
    }
}
