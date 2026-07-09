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
using System.Runtime.ExceptionServices;

namespace PortfolioSaver.Data.Services;

public sealed class RetryPolicyService
{
    public static readonly TimeSpan DefaultBaseDelay = TimeSpan.FromMilliseconds(250);
    public static readonly TimeSpan DefaultMaxDelay = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan DefaultMaxJitter = TimeSpan.FromMilliseconds(200);

    private readonly TimeSpan _baseDelay;
    private readonly TimeSpan _maxDelay;
    private readonly int _maxJitterMilliseconds;
    private readonly Func<int, int> _jitterMillisecondsProvider;
    private readonly Func<TimeSpan, CancellationToken, Task> _delayAsync;

    public RetryPolicyService(
        TimeSpan? baseDelay = null,
        TimeSpan? maxDelay = null,
        TimeSpan? maxJitter = null,
        Func<int, int>? jitterMillisecondsProvider = null,
        Func<TimeSpan, CancellationToken, Task>? delayAsync = null)
    {
        _baseDelay = PositiveDelayOrDefault(baseDelay, DefaultBaseDelay, nameof(baseDelay));
        _maxDelay = PositiveDelayOrDefault(maxDelay, DefaultMaxDelay, nameof(maxDelay));
        _maxJitterMilliseconds = Math.Max(0, (int)(maxJitter ?? DefaultMaxJitter).TotalMilliseconds);
        _jitterMillisecondsProvider = jitterMillisecondsProvider ?? (exclusiveMax => exclusiveMax <= 0 ? 0 : Random.Shared.Next(0, exclusiveMax));
        _delayAsync = delayAsync ?? Task.Delay;
    }

    public async Task<T> ExecuteAsync<T>(Func<Task<T>> action, int maxAttempts = 3, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (maxAttempts < 1)
            throw new ArgumentOutOfRangeException(nameof(maxAttempts), maxAttempts, "Retry attempts must be at least 1.");

        ExceptionDispatchInfo? finalException = null;
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return await action().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                if (ex is OperationCanceledException)
                    throw;

                if (attempt >= maxAttempts)
                {
                    finalException = ExceptionDispatchInfo.Capture(ex);
                    break;
                }

                await _delayAsync(GetDelayAfterFailedAttemptNumber(attempt), cancellationToken).ConfigureAwait(false);
            }
        }

        finalException?.Throw();
        throw new InvalidOperationException("Retry policy failed without returning a result.");
    }

    private TimeSpan GetDelayAfterFailedAttemptNumber(int attemptNumber)
    {
        double cappedMilliseconds = _baseDelay.TotalMilliseconds;
        for (int i = 1; i < attemptNumber; i++)
        {
            cappedMilliseconds = Math.Min(cappedMilliseconds * 2, _maxDelay.TotalMilliseconds);
            if (cappedMilliseconds >= _maxDelay.TotalMilliseconds)
                break;
        }

        int jitterMilliseconds = _maxJitterMilliseconds == 0
            ? 0
            : Math.Clamp(_jitterMillisecondsProvider(_maxJitterMilliseconds), 0, _maxJitterMilliseconds);
        return TimeSpan.FromMilliseconds(cappedMilliseconds + jitterMilliseconds);
    }

    private static TimeSpan PositiveDelayOrDefault(TimeSpan? value, TimeSpan fallback, string parameterName)
    {
        if (value is null)
            return fallback;

        if (value <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(parameterName, value, "Retry delay must be greater than zero.");

        return value.Value;
    }
}
