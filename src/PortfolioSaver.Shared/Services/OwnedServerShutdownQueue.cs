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
using PortfolioSaver.Shared.Diagnostics;

namespace PortfolioSaver.Shared.Services;

public static class OwnedServerShutdownQueue
{
    public static void QueueShutdown(string sourceName)
    {
        TraceLog.Info(sourceName, "Queueing owned YFinance server shutdown.");
        Thread shutdownThread = new(static state =>
        {
            string sourceName = (string)state!;
            using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(1));
            try
            {
                YFinanceServerProcessManager.StopOwnedServerAsync(timeout.Token).GetAwaiter().GetResult();
                TraceLog.Info(sourceName, "Owned YFinance server shutdown completed.");
            }
            catch (OperationCanceledException)
            {
                TraceLog.Warn(sourceName, "Owned YFinance server shutdown timed out; owned server will also exit when owner PID disappears.");
            }
            catch (Exception ex)
            {
                TraceLog.Error(sourceName, "Owned YFinance server shutdown failed.", ex);
            }
        })
        {
            IsBackground = false,
            Name = "Owned YFinance shutdown"
        };

        shutdownThread.Start(sourceName);
    }
}
