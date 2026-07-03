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
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace PortfolioSaver.Tests.Services;

public sealed class ItchPublishWorkflowTests
{
    [Fact]
    public void ItchPublishWorkflow_IsFrozenBeforeReleaseAssetsAreDownloaded()
    {
        string workflow = File.ReadAllText(Path.Combine(GetRepositoryRoot(), ".github", "workflows", "itch-publish.yml"));

        Assert.Contains("${{ vars.DNPPV_RELEASE_DISTRIBUTION_FROZEN || 'true' }}", workflow, StringComparison.Ordinal);
        int freezeIndex = RequireIndex(workflow, "- name: Enforce Distribution Freeze");
        int validationIndex = RequireIndex(workflow, "- name: Validate Itch Publish Inputs");
        int waitIndex = RequireIndex(workflow, "- name: Wait for Complete GitHub Release Asset Set");
        int butlerIndex = RequireIndex(workflow, "- name: Push Installer and Advisory Files to Itch.io");

        Assert.True(freezeIndex < validationIndex, "Freeze guard must run before input validation.");
        Assert.True(freezeIndex < waitIndex, "Freeze guard must run before GitHub release asset download.");
        Assert.True(freezeIndex < butlerIndex, "Freeze guard must run before Butler publication.");
        Assert.Contains("DNPPV_RELEASE_DISTRIBUTION_FROZEN=false", workflow[freezeIndex..validationIndex], StringComparison.Ordinal);
        Assert.Contains("Itch.io publishing is frozen for the 1.0 development cycle.", workflow[freezeIndex..validationIndex], StringComparison.Ordinal);
    }

    private static int RequireIndex(string text, string marker)
    {
        int index = text.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(index >= 0, $"Expected marker not found: {marker}");
        return index;
    }

    private static string GetRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DoNotPanicPortfolioVisualizer.sln")))
        {
            directory = directory.Parent;
        }

        if (directory is null)
            throw new DirectoryNotFoundException("DoNotPanicPortfolioVisualizer.sln not found from test base directory.");

        return directory.FullName;
    }
}
