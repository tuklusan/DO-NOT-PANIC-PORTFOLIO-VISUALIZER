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
namespace PortfolioSaver.Data.Services;

public sealed class RetryPolicyService
{
    public async Task<T> ExecuteAsync<T>(Func<Task<T>> action, int maxAttempts = 3, CancellationToken cancellationToken = default)
    {
        Exception? lastException = null;
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return await action();
            }
            catch (Exception ex) when (attempt < maxAttempts)
            {
                lastException = ex;
                await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt), cancellationToken);
            }
        }

        throw lastException ?? new InvalidOperationException("Retry policy failed without a captured exception.");
    }
}
