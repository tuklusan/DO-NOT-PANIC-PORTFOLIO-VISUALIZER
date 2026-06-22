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
using Xunit;

namespace PortfolioSaver.Tests.Services;

public sealed class CappedFileLogWriterTests
{
    [Fact]
    public void WriteLine_RotatesPrimaryLogBeforeExceedingCap()
    {
        string directory = CreateTempDirectory();
        try
        {
            string logPath = Path.Combine(directory, "vm-agent.log");
            File.WriteAllText(logPath, new string('A', 1_000));
            CappedFileLogWriter writer = new(logPath, maxBytes: 1_024);

            writer.WriteLine(new string('B', 80));

            Assert.True(File.Exists(logPath));
            Assert.True(File.Exists(logPath + ".1"));
            Assert.True(new FileInfo(logPath).Length <= 1_024);
            Assert.Equal(1_000, new FileInfo(logPath + ".1").Length);
            Assert.Contains("BBBB", File.ReadAllText(logPath), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task WriteLine_SerializesConcurrentWriters()
    {
        string directory = CreateTempDirectory();
        try
        {
            string logPath = Path.Combine(directory, "vm-agent.log");
            CappedFileLogWriter writer = new(logPath, maxBytes: 4096);

            await Task.WhenAll(Enumerable.Range(0, 100).Select(index =>
                Task.Run(() => writer.WriteLine($"line-{index:000}"))));

            string combined = File.ReadAllText(logPath);
            if (File.Exists(logPath + ".1"))
                combined += File.ReadAllText(logPath + ".1");

            for (int index = 0; index < 100; index++)
                Assert.Contains($"line-{index:000}", combined, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string CreateTempDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), "dnppv-log-writer-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}
