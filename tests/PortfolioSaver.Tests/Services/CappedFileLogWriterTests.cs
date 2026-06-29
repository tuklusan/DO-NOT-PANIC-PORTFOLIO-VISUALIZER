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
