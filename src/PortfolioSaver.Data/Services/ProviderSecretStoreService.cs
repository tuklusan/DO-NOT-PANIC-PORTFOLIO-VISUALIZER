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
using PortfolioSaver.Core.Constants;
using PortfolioSaver.Core.Models;
using PortfolioSaver.Data.Interfaces;
using PortfolioSaver.Shared.Helpers;

namespace PortfolioSaver.Data.Services;

public sealed class ProviderSecretStoreService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly ISettingsProtectionService _settingsProtectionService;

    public ProviderSecretStoreService(ISettingsProtectionService? settingsProtectionService = null)
    {
        _settingsProtectionService = settingsProtectionService ?? new SettingsProtectionService();
    }

    public string SecretsPath => Path.Combine(PathHelper.GetAppDataDirectory(), "provider-secrets.json");

    public void OverlaySecrets(AppSettings settings)
    {
        ProviderSecretsDto dto = LoadSecretsDto();
        ApplySecret(settings, dto.AiApiKey, static s => s.AiApiKey, static (s, v) => s.AiApiKey = v);
    }

    public void Save(AppSettings settings)
    {
        ProviderSecretsDto dto = LoadSecretsDto();

        dto.AiApiKey = ResolvePersistedProtectedValue(settings.AiApiKey, dto.AiApiKey);

        if (!dto.HasAnySecrets())
        {
            if (File.Exists(SecretsPath))
                File.Delete(SecretsPath);

            return;
        }

        string? directory = Path.GetDirectoryName(SecretsPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        string json = JsonSerializer.Serialize(dto, JsonOptions);
        File.WriteAllText(SecretsPath, json);
    }

    private void ApplySecret(
        AppSettings settings,
        string protectedValue,
        Func<AppSettings, string> getter,
        Action<AppSettings, string> setter)
    {
        if (!string.IsNullOrWhiteSpace(getter(settings)))
            return;

        string unprotected = UnprotectSafe(protectedValue);
        if (!string.IsNullOrWhiteSpace(unprotected))
            setter(settings, unprotected);
    }

    private string ResolvePersistedProtectedValue(string currentValue, string persistedProtectedValue)
    {
        string trimmed = (currentValue ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            return string.Empty;

        if (string.Equals(UnprotectSafe(persistedProtectedValue), trimmed, StringComparison.Ordinal))
            return persistedProtectedValue;

        return _settingsProtectionService.Protect(trimmed);
    }

    private ProviderSecretsDto LoadSecretsDto()
    {
        if (!File.Exists(SecretsPath))
            return new ProviderSecretsDto();

        try
        {
            string json = File.ReadAllText(SecretsPath);
            ProviderSecretsDto dto = JsonSerializer.Deserialize<ProviderSecretsDto>(json, JsonOptions) ?? new ProviderSecretsDto();
            MigrateLegacySerializedAiSecrets(dto, json);
            return dto;
        }
        catch
        {
            return new ProviderSecretsDto();
        }
    }

    private static void MigrateLegacySerializedAiSecrets(ProviderSecretsDto dto, string json)
    {
        if (!string.IsNullOrWhiteSpace(dto.AiApiKey) || string.IsNullOrWhiteSpace(json))
            return;

        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            if (document.RootElement.TryGetProperty("DeepSeekApiKey", out JsonElement element) &&
                element.ValueKind == JsonValueKind.String)
            {
                dto.AiApiKey = element.GetString() ?? string.Empty;
            }
        }
        catch (JsonException)
        {
        }
    }

    private string UnprotectSafe(string protectedValue)
    {
        if (string.IsNullOrWhiteSpace(protectedValue))
            return string.Empty;

        try
        {
            return _settingsProtectionService.Unprotect(protectedValue);
        }
        catch
        {
            return string.Empty;
        }
    }

    private sealed class ProviderSecretsDto
    {
        public string AiApiKey { get; set; } = string.Empty;

        public bool HasAnySecrets()
            => !string.IsNullOrWhiteSpace(AiApiKey);
    }
}
