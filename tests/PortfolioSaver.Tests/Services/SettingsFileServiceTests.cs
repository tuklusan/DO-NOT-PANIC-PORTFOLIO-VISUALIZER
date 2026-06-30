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
using System.Text.Json;
using PortfolioSaver.Config.Services;
using PortfolioSaver.Core.Constants;
using PortfolioSaver.Core.Models;
using PortfolioSaver.Screensaver.Services;
using Xunit;

namespace PortfolioSaver.Tests.Services;

[Collection("EnvironmentSerial")]
public sealed class SettingsFileServiceTests
{
    private static void DeleteDirectoryWithRetry(string path)
    {
        for (int attempt = 0; attempt < 10; attempt++)
        {
            if (!Directory.Exists(path))
                return;

            try
            {
                Directory.Delete(path, recursive: true);
                return;
            }
            catch (IOException) when (attempt < 9)
            {
                Thread.Sleep(100);
            }
            catch (UnauthorizedAccessException) when (attempt < 9)
            {
                Thread.Sleep(100);
            }
        }
    }

    [Fact]
    public void Save_StripsSecretsFromPersistedSettingsFile()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "PortfolioSaverTests", Guid.NewGuid().ToString("N"));
        string? previousRoot = Environment.GetEnvironmentVariable("PORTFOLIOSAVER_APPDATA_ROOT");
        Environment.SetEnvironmentVariable("PORTFOLIOSAVER_APPDATA_ROOT", tempRoot);

        try
        {
            SettingsFileService service = new();
            AppSettings settings = Defaults.CreateSettings();
            settings.DeepSeekApiKey = "placeholder-value";

            service.Save(settings);

            string json = File.ReadAllText(service.SettingsPath);
            AppSettings persisted = JsonSerializer.Deserialize<AppSettings>(json)!;

            Assert.True(string.IsNullOrWhiteSpace(persisted.DeepSeekApiKey));
            Assert.DoesNotContain("placeholder-value", json, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PORTFOLIOSAVER_APPDATA_ROOT", previousRoot);
            if (Directory.Exists(tempRoot))
                DeleteDirectoryWithRetry(tempRoot);
        }
    }

    [Fact]
    public void Load_WhenSettingsFileMissing_DoesNotSeedPlaintextSecretPlaceholders()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "PortfolioSaverTests", Guid.NewGuid().ToString("N"));
        string? previousRoot = Environment.GetEnvironmentVariable("PORTFOLIOSAVER_APPDATA_ROOT");
        Dictionary<string, string?> previousAiKeyVariables = CaptureEnvironmentVariables(Defaults.AiApiKeyEnvironmentVariableNames);
        Environment.SetEnvironmentVariable("PORTFOLIOSAVER_APPDATA_ROOT", tempRoot);
        ClearEnvironmentVariables(Defaults.AiApiKeyEnvironmentVariableNames);

        try
        {
            SettingsFileService service = new();

            AppSettings settings = service.Load();

            Assert.True(string.IsNullOrWhiteSpace(settings.DeepSeekApiKey));
        }
        finally
        {
            Environment.SetEnvironmentVariable("PORTFOLIOSAVER_APPDATA_ROOT", previousRoot);
            RestoreEnvironmentVariables(previousAiKeyVariables);
            if (Directory.Exists(tempRoot))
                DeleteDirectoryWithRetry(tempRoot);
        }
    }

    [Fact]
    public void Save_PersistsDeepSeekSecretSecurely_AndRuntimeLoadRestoresIt()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "PortfolioSaverTests", Guid.NewGuid().ToString("N"));
        string? previousRoot = Environment.GetEnvironmentVariable("PORTFOLIOSAVER_APPDATA_ROOT");
        Dictionary<string, string?> previousAiKeyVariables = CaptureEnvironmentVariables(Defaults.AiApiKeyEnvironmentVariableNames);
        Environment.SetEnvironmentVariable("PORTFOLIOSAVER_APPDATA_ROOT", tempRoot);
        ClearEnvironmentVariables(Defaults.AiApiKeyEnvironmentVariableNames);

        try
        {
            SettingsFileService service = new();
            AppSettings settings = Defaults.CreateSettings();
            settings.DeepSeekApiKey = "placeholder-value";

            service.Save(settings);

            string settingsJson = File.ReadAllText(service.SettingsPath);
            string secretsPath = Path.Combine(tempRoot, "provider-secrets.json");
            string secretsJson = File.ReadAllText(secretsPath);

            Assert.DoesNotContain("placeholder-value", settingsJson, StringComparison.Ordinal);
            Assert.DoesNotContain("placeholder-value", secretsJson, StringComparison.Ordinal);

            AppSettings configLoaded = service.Load();
            ScreensaverSettingsService runtimeService = new();
            AppSettings runtimeLoaded = runtimeService.Load();

            Assert.Equal("placeholder-value", configLoaded.DeepSeekApiKey);

            Assert.Equal("placeholder-value", runtimeLoaded.DeepSeekApiKey);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PORTFOLIOSAVER_APPDATA_ROOT", previousRoot);
            RestoreEnvironmentVariables(previousAiKeyVariables);
            if (Directory.Exists(tempRoot))
                DeleteDirectoryWithRetry(tempRoot);
        }
    }

    [Fact]
    public void Save_UsesAtomicReplacementWithoutLeavingTempFiles()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "PortfolioSaverTests", Guid.NewGuid().ToString("N"));
        string? previousRoot = Environment.GetEnvironmentVariable("PORTFOLIOSAVER_APPDATA_ROOT");
        Environment.SetEnvironmentVariable("PORTFOLIOSAVER_APPDATA_ROOT", tempRoot);

        try
        {
            SettingsFileService service = new();
            AppSettings settings = Defaults.CreateSettings();
            settings.NewsRefreshMinutes = 15;

            service.Save(settings);
            settings.NewsRefreshMinutes = 30;
            service.Save(settings);

            string json = File.ReadAllText(service.SettingsPath);
            AppSettings persisted = JsonSerializer.Deserialize<AppSettings>(json)!;

            Assert.Equal(30, persisted.NewsRefreshMinutes);
            Assert.DoesNotContain(
                Directory.EnumerateFiles(tempRoot),
                path => path.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Environment.SetEnvironmentVariable("PORTFOLIOSAVER_APPDATA_ROOT", previousRoot);
            if (Directory.Exists(tempRoot))
                DeleteDirectoryWithRetry(tempRoot);
        }
    }

    [Fact]
    public void Save_AndLoad_HonorExplicitSettingsPathOverride()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "PortfolioSaverTests", Guid.NewGuid().ToString("N"));
        string? previousRoot = Environment.GetEnvironmentVariable("PORTFOLIOSAVER_APPDATA_ROOT");
        Environment.SetEnvironmentVariable("PORTFOLIOSAVER_APPDATA_ROOT", tempRoot);

        try
        {
            string explicitSettingsPath = Path.Combine(tempRoot, "CustomSettings", "settings.custom.json");
            SettingsFileService service = new(explicitSettingsPath);
            AppSettings settings = Defaults.CreateSettings();
            settings.NewsRefreshMinutes = 45;

            service.Save(settings);
            AppSettings loaded = service.Load();

            Assert.Equal(explicitSettingsPath, service.SettingsPath);
            Assert.True(File.Exists(explicitSettingsPath));
            Assert.False(File.Exists(Path.Combine(tempRoot, "settings.json")));
            Assert.Equal(45, loaded.NewsRefreshMinutes);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PORTFOLIOSAVER_APPDATA_ROOT", previousRoot);
            if (Directory.Exists(tempRoot))
                DeleteDirectoryWithRetry(tempRoot);
        }
    }

    [Fact]
    public void Constructor_WhitespaceSettingsPathOverrideUsesDefaultPath()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "PortfolioSaverTests", Guid.NewGuid().ToString("N"));
        string? previousRoot = Environment.GetEnvironmentVariable("PORTFOLIOSAVER_APPDATA_ROOT");
        Environment.SetEnvironmentVariable("PORTFOLIOSAVER_APPDATA_ROOT", tempRoot);

        try
        {
            SettingsFileService service = new("   ");

            Assert.Equal(Path.Combine(tempRoot, "settings.json"), service.SettingsPath);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PORTFOLIOSAVER_APPDATA_ROOT", previousRoot);
            if (Directory.Exists(tempRoot))
                DeleteDirectoryWithRetry(tempRoot);
        }
    }

    [Fact]
    public async Task Save_SerializesConcurrentCallsOnSameServiceInstance()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "PortfolioSaverTests", Guid.NewGuid().ToString("N"));
        string? previousRoot = Environment.GetEnvironmentVariable("PORTFOLIOSAVER_APPDATA_ROOT");
        Environment.SetEnvironmentVariable("PORTFOLIOSAVER_APPDATA_ROOT", tempRoot);

        try
        {
            SettingsFileService service = new();
            await Task.WhenAll(Enumerable.Range(0, 8).Select(index => Task.Run(() =>
            {
                AppSettings settings = Defaults.CreateSettings();
                settings.NewsRefreshMinutes = 15 + index;
                service.Save(settings);
            })));

            string json = File.ReadAllText(service.SettingsPath);
            AppSettings persisted = JsonSerializer.Deserialize<AppSettings>(json)!;

            Assert.InRange(persisted.NewsRefreshMinutes, 15, 22);
            Assert.DoesNotContain(
                Directory.EnumerateFiles(tempRoot),
                path => path.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Environment.SetEnvironmentVariable("PORTFOLIOSAVER_APPDATA_ROOT", previousRoot);
            if (Directory.Exists(tempRoot))
                DeleteDirectoryWithRetry(tempRoot);
        }
    }

    private static Dictionary<string, string?> CaptureEnvironmentVariables(IEnumerable<string> names)
        => names.ToDictionary(
            static name => name,
            static name => Environment.GetEnvironmentVariable(name),
            StringComparer.Ordinal);

    private static void ClearEnvironmentVariables(IEnumerable<string> names)
    {
        foreach (string name in names)
            Environment.SetEnvironmentVariable(name, null);
    }

    private static void RestoreEnvironmentVariables(IReadOnlyDictionary<string, string?> previous)
    {
        foreach ((string name, string? value) in previous)
            Environment.SetEnvironmentVariable(name, value);
    }
}
