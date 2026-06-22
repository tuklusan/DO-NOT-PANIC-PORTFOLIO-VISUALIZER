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
using PortfolioSaver.Data.Services;
using Xunit;

namespace PortfolioSaver.Tests.Services;

public sealed class ProviderHealthServiceTests
{
    [Fact]
    public void Snapshot_DefaultState_IsHealthy()
    {
        ProviderHealthService service = new();

        var snapshot = service.Snapshot;

        Assert.True(snapshot.IsHealthy);
        Assert.Equal("OK", snapshot.StatusMessage);
        Assert.Equal(0, snapshot.ConsecutiveFailures);
        Assert.Null(snapshot.LastSuccessUtc);
        Assert.Null(snapshot.LastFailureUtc);
    }

    [Fact]
    public async Task MarkFailure_CountsConcurrentFailuresAccurately()
    {
        ProviderHealthService service = new();
        const int failureCount = 1_000;
        DateTimeOffset startedUtc = DateTimeOffset.UtcNow;

        await Task.WhenAll(Enumerable.Range(0, failureCount)
            .Select(index => Task.Run(() => service.MarkFailure($"failure-{index}"))));

        var snapshot = service.Snapshot;
        Assert.False(snapshot.IsHealthy);
        Assert.Equal(failureCount, snapshot.ConsecutiveFailures);
        Assert.StartsWith("failure-", snapshot.StatusMessage, StringComparison.Ordinal);
        Assert.NotNull(snapshot.LastFailureUtc);
        Assert.True(snapshot.LastFailureUtc >= startedUtc);
    }

    [Fact]
    public async Task ConcurrentSuccessAndFailure_LeavesConsistentSnapshot()
    {
        ProviderHealthService service = new();

        await Task.WhenAll(Enumerable.Range(0, 500).Select(index => Task.Run(() =>
        {
            service.MarkFailure($"failure-{index}");
            service.MarkSuccess();
        })));

        var snapshot = service.Snapshot;
        if (snapshot.IsHealthy)
        {
            Assert.Equal("OK", snapshot.StatusMessage);
            Assert.Equal(0, snapshot.ConsecutiveFailures);
            Assert.NotNull(snapshot.LastSuccessUtc);
        }
        else
        {
            Assert.StartsWith("failure-", snapshot.StatusMessage, StringComparison.Ordinal);
            Assert.True(snapshot.ConsecutiveFailures > 0);
            Assert.NotNull(snapshot.LastFailureUtc);
        }
    }

    [Fact]
    public void MarkSuccess_ResetsPreviousFailures()
    {
        ProviderHealthService service = new();
        service.MarkFailure("first");
        service.MarkFailure("second");

        service.MarkSuccess();

        var snapshot = service.Snapshot;
        Assert.True(snapshot.IsHealthy);
        Assert.Equal("OK", snapshot.StatusMessage);
        Assert.Equal(0, snapshot.ConsecutiveFailures);
        Assert.NotNull(snapshot.LastSuccessUtc);
        Assert.NotNull(snapshot.LastFailureUtc);
    }

    [Fact]
    public void MarkFailure_NormalizesNullOrBlankMessage()
    {
        ProviderHealthService service = new();

        service.MarkFailure(null!);

        Assert.Equal("Unknown failure", service.Snapshot.StatusMessage);

        service.MarkFailure("   ");

        Assert.Equal("Unknown failure", service.Snapshot.StatusMessage);
    }

    [Fact]
    public void Snapshot_ReturnsDefensiveCopy()
    {
        ProviderHealthService service = new();
        service.MarkFailure("original");

        var snapshot = service.Snapshot;
        snapshot.StatusMessage = "mutated";
        snapshot.ConsecutiveFailures = 99;
        snapshot.IsHealthy = true;

        var current = service.Snapshot;
        Assert.False(current.IsHealthy);
        Assert.Equal("original", current.StatusMessage);
        Assert.Equal(1, current.ConsecutiveFailures);
    }
}
