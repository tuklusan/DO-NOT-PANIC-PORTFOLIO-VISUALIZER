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
        ApplySecret(settings, dto.DeepSeekApiKey, static s => s.DeepSeekApiKey, static (s, v) => s.DeepSeekApiKey = v);
    }

    public void Save(AppSettings settings)
    {
        ProviderSecretsDto dto = LoadSecretsDto();

        dto.DeepSeekApiKey = ResolvePersistedProtectedValue(
            settings.DeepSeekApiKey,
            Defaults.AiApiKeyEnvironmentVariableNames,
            dto.DeepSeekApiKey);

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

    private string ResolvePersistedProtectedValue(string currentValue, IEnumerable<string> environmentVariableNames, string existingProtectedValue)
    {
        string trimmed = (currentValue ?? string.Empty).Trim();
        string environmentValue = GetFirstEnvironmentVariableValue(environmentVariableNames);

        if (!string.IsNullOrWhiteSpace(environmentValue) &&
            string.Equals(trimmed, environmentValue, StringComparison.Ordinal))
        {
            return existingProtectedValue ?? string.Empty;
        }

        // Environment-sourced keys are intentionally not persisted; the normalizer reapplies them on load.
        if (string.IsNullOrWhiteSpace(trimmed))
            return string.Empty;

        return _settingsProtectionService.Protect(trimmed);
    }

    private static string GetFirstEnvironmentVariableValue(IEnumerable<string> environmentVariableNames)
    {
        foreach (string name in environmentVariableNames)
        {
            string value = (Environment.GetEnvironmentVariable(name) ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        return string.Empty;
    }

    private ProviderSecretsDto LoadSecretsDto()
    {
        if (!File.Exists(SecretsPath))
            return new ProviderSecretsDto();

        try
        {
            string json = File.ReadAllText(SecretsPath);
            return JsonSerializer.Deserialize<ProviderSecretsDto>(json, JsonOptions) ?? new ProviderSecretsDto();
        }
        catch
        {
            return new ProviderSecretsDto();
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
        public string DeepSeekApiKey { get; set; } = string.Empty;

        public bool HasAnySecrets()
            => !string.IsNullOrWhiteSpace(DeepSeekApiKey);
    }
}
