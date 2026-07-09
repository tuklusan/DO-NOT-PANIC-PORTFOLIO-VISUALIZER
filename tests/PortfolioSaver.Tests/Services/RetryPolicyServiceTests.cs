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
using PortfolioSaver.Data.Services;
using Xunit;

namespace PortfolioSaver.Tests.Services;

public sealed class RetryPolicyServiceTests
{
    [Fact]
    public async Task ExecuteAsync_ReturnsFirstAttemptSuccessWithoutDelay()
    {
        List<TimeSpan> delays = new();
        RetryPolicyService service = new(
            delayAsync: (delay, _) =>
            {
                delays.Add(delay);
                return Task.CompletedTask;
            });

        string result = await service.ExecuteAsync(() => Task.FromResult("ok"));

        Assert.Equal("ok", result);
        Assert.Empty(delays);
    }

    [Fact]
    public async Task ExecuteAsync_RetriesWithExponentialBackoffCapAndJitter()
    {
        List<TimeSpan> delays = new();
        RetryPolicyService service = new(
            baseDelay: TimeSpan.FromMilliseconds(100),
            maxDelay: TimeSpan.FromMilliseconds(250),
            maxJitter: TimeSpan.FromMilliseconds(50),
            jitterMillisecondsProvider: _ => 25,
            delayAsync: (delay, _) =>
            {
                delays.Add(delay);
                return Task.CompletedTask;
            });

        int attempts = 0;
        string result = await service.ExecuteAsync(
            () =>
            {
                attempts++;
                return attempts < 4
                    ? Task.FromException<string>(new InvalidOperationException("Transient failure."))
                    : Task.FromResult("ok");
            },
            maxAttempts: 4);

        Assert.Equal("ok", result);
        Assert.Equal(4, attempts);
        Assert.Equal(
            new[]
            {
                TimeSpan.FromMilliseconds(125),
                TimeSpan.FromMilliseconds(225),
                TimeSpan.FromMilliseconds(275)
            },
            delays);
    }

    [Fact]
    public async Task ExecuteAsync_PropagatesFinalExceptionWithoutExtraDelay()
    {
        List<TimeSpan> delays = new();
        RetryPolicyService service = new(
            baseDelay: TimeSpan.FromMilliseconds(10),
            maxJitter: TimeSpan.Zero,
            delayAsync: (delay, _) =>
            {
                delays.Add(delay);
                return Task.CompletedTask;
            });

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ExecuteAsync<string>(
                () => Task.FromException<string>(new InvalidOperationException("boom")),
                maxAttempts: 2));

        Assert.Equal("boom", exception.Message);
        Assert.Single(delays);
        Assert.Equal(TimeSpan.FromMilliseconds(10), delays[0]);
    }

    [Fact]
    public async Task ExecuteAsync_HonorsCancellationDuringRetryDelay()
    {
        using CancellationTokenSource cancellation = new();
        RetryPolicyService service = new(
            baseDelay: TimeSpan.FromMilliseconds(10),
            maxJitter: TimeSpan.Zero,
            delayAsync: (_, token) =>
            {
                cancellation.Cancel();
                token.ThrowIfCancellationRequested();
                return Task.CompletedTask;
            });

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => service.ExecuteAsync<string>(
                () => Task.FromException<string>(new InvalidOperationException("Transient failure.")),
                maxAttempts: 2,
                cancellationToken: cancellation.Token));
    }

    [Fact]
    public async Task ExecuteAsync_DoesNotRetryCancellationThrownByAction()
    {
        List<TimeSpan> delays = new();
        RetryPolicyService service = new(
            delayAsync: (delay, _) =>
            {
                delays.Add(delay);
                return Task.CompletedTask;
            });

        int attempts = 0;

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => service.ExecuteAsync<string>(
                () =>
                {
                    attempts++;
                    return Task.FromException<string>(new OperationCanceledException("cancelled"));
                },
                maxAttempts: 3));

        Assert.Equal(1, attempts);
        Assert.Empty(delays);
    }

    [Fact]
    public async Task ExecuteAsync_ThrowsWhenCancellationAlreadyRequested()
    {
        using CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync();
        RetryPolicyService service = new();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => service.ExecuteAsync(() => Task.FromResult("unused"), cancellationToken: cancellation.Token));
    }

    [Fact]
    public async Task ExecuteAsync_MaxAttemptsOneDoesNotDelayBeforeThrowing()
    {
        List<TimeSpan> delays = new();
        RetryPolicyService service = new(
            delayAsync: (delay, _) =>
            {
                delays.Add(delay);
                return Task.CompletedTask;
            });

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ExecuteAsync<string>(
                () => Task.FromException<string>(new InvalidOperationException("single failure")),
                maxAttempts: 1));

        Assert.Equal("single failure", exception.Message);
        Assert.Empty(delays);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task ExecuteAsync_RejectsInvalidMaxAttempts(int maxAttempts)
    {
        RetryPolicyService service = new();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => service.ExecuteAsync(() => Task.FromResult("unused"), maxAttempts));
    }

    [Fact]
    public void Constructor_RejectsZeroOrNegativeBaseDelay()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RetryPolicyService(baseDelay: TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RetryPolicyService(baseDelay: TimeSpan.FromMilliseconds(-1)));
    }

    [Fact]
    public void Constructor_RejectsZeroOrNegativeMaxDelay()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RetryPolicyService(maxDelay: TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RetryPolicyService(maxDelay: TimeSpan.FromMilliseconds(-1)));
    }
}
