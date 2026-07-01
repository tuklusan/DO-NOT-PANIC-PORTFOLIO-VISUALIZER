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
                MigrateLegacySerializedAiSettings(settings, json);
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

        copy.AiApiKey = string.Empty;

        return copy;
    }

    private static void MigrateLegacySerializedAiSettings(AppSettings settings, string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return;

        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            if (string.IsNullOrWhiteSpace(settings.AiApiKey))
                settings.AiApiKey = GetString(root, "DeepSeekApiKey");
            if (string.IsNullOrWhiteSpace(settings.AiEndpointUrl) ||
                string.Equals(settings.AiEndpointUrl, Defaults.DefaultAiEndpointUrl, StringComparison.OrdinalIgnoreCase))
            {
                settings.AiEndpointUrl = GetString(root, "DeepSeekEndpointUrl", settings.AiEndpointUrl);
            }

            if (string.IsNullOrWhiteSpace(settings.AiModelId) ||
                string.Equals(settings.AiModelId, Defaults.DefaultAiModelId, StringComparison.OrdinalIgnoreCase))
            {
                settings.AiModelId = GetString(root, "DeepSeekModelId", settings.AiModelId);
            }

            if (!root.TryGetProperty("AiWritingStyle", out _) &&
                root.TryGetProperty("DeepSeekWritingStyle", out JsonElement styleElement))
            {
                settings.AiWritingStyle = ReadAiWritingStyle(styleElement, settings.AiWritingStyle);
            }
        }
        catch (JsonException)
        {
        }
    }

    private static string GetString(JsonElement root, string propertyName, string fallback = "")
        => root.TryGetProperty(propertyName, out JsonElement element) && element.ValueKind == JsonValueKind.String
            ? element.GetString() ?? fallback
            : fallback;

    private static Core.Enums.AiWritingStyle ReadAiWritingStyle(JsonElement element, Core.Enums.AiWritingStyle fallback)
    {
        if (element.ValueKind == JsonValueKind.Number &&
            element.TryGetInt32(out int numeric) &&
            Enum.IsDefined(typeof(Core.Enums.AiWritingStyle), numeric))
        {
            return (Core.Enums.AiWritingStyle)numeric;
        }

        if (element.ValueKind == JsonValueKind.String &&
            Enum.TryParse(element.GetString(), ignoreCase: true, out Core.Enums.AiWritingStyle parsed) &&
            Enum.IsDefined(typeof(Core.Enums.AiWritingStyle), parsed))
        {
            return parsed;
        }

        return fallback;
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
