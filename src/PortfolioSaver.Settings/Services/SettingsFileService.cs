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
using System.IO;
using System.Text.Json;
using PortfolioSaver.Core.Constants;
using PortfolioSaver.Core.Models;
using PortfolioSaver.Core.Services;
using PortfolioSaver.Data.Services;
using PortfolioSaver.Shared.Helpers;

namespace PortfolioSaver.Config.Services;

public sealed class SettingsFileService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly ProviderSecretStoreService _providerSecretStoreService = new();
    private readonly object _sync = new();
    private readonly string? _settingsPathOverride;

    public SettingsFileService(string? settingsPath = null)
    {
        _settingsPathOverride = string.IsNullOrWhiteSpace(settingsPath)
            ? null
            : settingsPath;
    }

    public string SettingsPath => _settingsPathOverride ?? Path.Combine(PathHelper.GetAppDataDirectory(), "settings.json");

    public AppSettings Load()
    {
        lock (_sync)
        {
            AppSettings settings = Defaults.CreateSettings();
            if (File.Exists(SettingsPath))
            {
                string json = File.ReadAllText(SettingsPath);
                settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? settings;
            }

            _providerSecretStoreService.OverlaySecrets(settings);
            return AppSettingsNormalizer.Normalize(settings);
        }
    }

    public void Save(AppSettings settings)
    {
        lock (_sync)
        {
            _providerSecretStoreService.Save(settings);

            AppSettings persisted = CreateSanitizedCopy(settings);
            string json = JsonSerializer.Serialize(persisted, new JsonSerializerOptions { WriteIndented = true });
            WriteAllTextAtomically(SettingsPath, json);
        }
    }

    private static AppSettings CreateSanitizedCopy(AppSettings settings)
    {
        AppSettings copy = JsonSerializer.Deserialize<AppSettings>(
            JsonSerializer.Serialize(settings),
            JsonOptions) ?? Defaults.CreateSettings();

        copy.DeepSeekApiKey = string.Empty;

        return copy;
    }

    /// <summary>
    /// Writes a complete file through a flushed same-directory temporary file before replacing the target.
    /// </summary>
    private static void WriteAllTextAtomically(string path, string contents)
    {
        string targetPath = Path.GetFullPath(path);
        string directory = Path.GetDirectoryName(targetPath) ?? Environment.CurrentDirectory;
        Directory.CreateDirectory(directory);

        string tempPath = Path.Combine(directory, Path.GetFileName(targetPath) + "." + Path.GetRandomFileName() + ".tmp");
        try
        {
            using (FileStream stream = new(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (StreamWriter writer = new(stream))
            {
                writer.Write(contents);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            File.Move(tempPath, targetPath, overwrite: true);
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
            catch
            {
            }
        }
    }
}
