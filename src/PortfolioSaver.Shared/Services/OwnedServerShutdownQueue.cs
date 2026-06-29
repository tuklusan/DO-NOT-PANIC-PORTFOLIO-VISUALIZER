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
