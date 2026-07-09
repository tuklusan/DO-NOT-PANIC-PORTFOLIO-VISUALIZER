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
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using PortfolioSaver.Core.Constants;
using PortfolioSaver.Core.Enums;
using PortfolioSaver.Core.Models;
using PortfolioSaver.Core.Services;
using PortfolioSaver.Data.Services;
using PortfolioSaver.Shared.Helpers;

namespace PortfolioSaver.Presentation.Services;

public sealed class VisualizerSettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly ProviderSecretStoreService _providerSecretStoreService = new();
    public string SettingsPath => Path.Combine(PathHelper.GetAppDataDirectory(), "settings.json");

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        AppSettings settings = Defaults.CreateSettings();
        if (File.Exists(SettingsPath))
        {
            try
            {
                await using FileStream stream = new(
                    SettingsPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    bufferSize: 4096,
                    useAsync: true);
                settings = await JsonSerializer.DeserializeAsync<AppSettings>(
                    stream,
                    JsonOptions,
                    cancellationToken).ConfigureAwait(false) ?? settings;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                settings = Defaults.CreateSettings();
            }
        }

        _providerSecretStoreService.OverlaySecrets(settings);
        AppSettings normalized = AppSettingsNormalizer.Normalize(settings);
        return normalized;
    }
}
