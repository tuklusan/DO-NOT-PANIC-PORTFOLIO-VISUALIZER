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
using System.Text.Json;
using YFinance.NET.Diagnostics;
using YFinance.NET.Protocol.Constants;
using YFinance.NET.Protocol.Dtos;
using YFinance.NET.Protocol.Errors;
using YFinance.NET.Protocol.Integrity;
using YFinance.NET.Protocol.Messages;

namespace YFinance.NET.Server.Hosting;

internal static class YFinanceServerFaultInjection
{
    internal const string ProfileEnvironmentVariable = "DNPPV_YFINANCE_FAULT_PROFILE";
    internal const string ProfilePathEnvironmentVariable = "DNPPV_YFINANCE_FAULT_PROFILE_PATH";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly object Sync = new();
    private static readonly SemaphoreSlim ProfileReadGate = new(1, 1);
    private static FaultProfile? _cachedProfile;
    private static string? _cachedPath;
    private static DateTime _cachedPathWriteUtc;
    private static string? _lastProfileTraceKey;

    public static async Task<ProtocolResponse<EmptyPayload>?> TryApplyAsync(ProtocolRequest<JsonElement> request, CancellationToken cancellationToken)
    {
        FaultProfile profile = await ResolveProfileAsync(cancellationToken).ConfigureAwait(false);
        TraceProfileIfChanged(profile);

        if (!profile.Enabled || !profile.AppliesTo(request.Operation))
            return null;

        if (profile.DelayMilliseconds > 0)
        {
            YFinanceCircularTraceSink.Instance.InfoState(
                "YFinanceServerFaultInjection",
                "FaultInjectionDelayStart",
                [new("profile", profile.Profile), new("operation", request.Operation), new("delay_ms", profile.DelayMilliseconds)]);
            await Task.Delay(profile.DelayMilliseconds, cancellationToken).ConfigureAwait(false);
        }

        if (string.Equals(profile.Mode, FaultModes.DelayOnly, StringComparison.OrdinalIgnoreCase))
        {
            TraceApplied(profile, request.Operation, "delay_only");
            return null;
        }

        TraceApplied(profile, request.Operation, profile.ErrorCode);
        ProtocolResponse<EmptyPayload> response = new()
        {
            RequestId = request.RequestId,
            Operation = request.Operation,
            Status = ProtocolResponseStatuses.Error,
            Error = new ProtocolError(profile.ErrorCode, profile.Message, profile.Retryable),
            Payload = new EmptyPayload()
        };
        ProtocolIntegrity.Stamp(response, response.Payload);
        return response;
    }

    private static void TraceApplied(FaultProfile profile, string operation, string effect)
        => YFinanceCircularTraceSink.Instance.WarnState(
            "YFinanceServerFaultInjection",
            "FaultInjectionApplied",
            [
                new("profile", profile.Profile),
                new("mode", profile.Mode),
                new("operation", operation),
                new("effect", effect),
                new("retryable", profile.Retryable),
                new("source", profile.Source)
            ]);

    private static void TraceProfileIfChanged(FaultProfile profile)
    {
        string traceKey = $"{profile.Source}|{profile.Profile}|{profile.Mode}|{profile.DelayMilliseconds}|{profile.ErrorCode}|{string.Join(",", profile.Operations)}|{profile.Enabled}";
        lock (Sync)
        {
            if (string.Equals(_lastProfileTraceKey, traceKey, StringComparison.Ordinal))
                return;

            _lastProfileTraceKey = traceKey;
        }

        YFinanceCircularTraceSink.Instance.InfoState(
            "YFinanceServerFaultInjection",
            "FaultInjectionProfileLoaded",
            [
                new("enabled", profile.Enabled),
                new("profile", profile.Profile),
                new("mode", profile.Mode),
                new("delay_ms", profile.DelayMilliseconds),
                new("error_code", profile.ErrorCode),
                new("operations", string.Join(",", profile.Operations)),
                new("source", profile.Source)
            ]);
    }

    private static async Task<FaultProfile> ResolveProfileAsync(CancellationToken cancellationToken)
    {
        string? path = Environment.GetEnvironmentVariable(ProfilePathEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(path))
        {
            FaultProfile? fileProfile = await TryReadProfileFileAsync(path, cancellationToken).ConfigureAwait(false);
            if (fileProfile is not null)
                return fileProfile;
        }

        string? environmentProfile = Environment.GetEnvironmentVariable(ProfileEnvironmentVariable);
        return FaultProfile.FromProfileName(environmentProfile, "environment");
    }

    private static async Task<FaultProfile?> TryReadProfileFileAsync(string path, CancellationToken cancellationToken)
    {
        await ProfileReadGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            string fullPath = Path.GetFullPath(path);
            if (!File.Exists(fullPath))
                return FaultProfile.FromProfileName("none", "missing-file:" + fullPath);

            DateTime writeUtc = File.GetLastWriteTimeUtc(fullPath);
            lock (Sync)
            {
                if (_cachedProfile is not null &&
                    string.Equals(_cachedPath, fullPath, StringComparison.OrdinalIgnoreCase) &&
                    _cachedPathWriteUtc == writeUtc)
                {
                    return _cachedProfile;
                }
            }

            string json = await File.ReadAllTextAsync(fullPath, cancellationToken).ConfigureAwait(false);
            FaultProfileFile? file = JsonSerializer.Deserialize<FaultProfileFile>(json, JsonOptions);
            FaultProfile profile = FaultProfile.FromFile(file, fullPath);
            lock (Sync)
            {
                _cachedPath = fullPath;
                _cachedPathWriteUtc = writeUtc;
                _cachedProfile = profile;
            }

            return profile;
        }
        catch (Exception ex)
        {
            ResetCache();
            YFinanceCircularTraceSink.Instance.WarnState(
                "YFinanceServerFaultInjection",
                "FaultInjectionProfileReadFailed",
                [new("path", path), new("message", ex.Message)]);
            return FaultProfile.FromProfileName("none", "read-failed:" + path);
        }
        finally
        {
            ProfileReadGate.Release();
        }
    }

    internal static void ResetCacheForTests()
        => ResetCache();

    private static void ResetCache()
    {
        lock (Sync)
        {
            _cachedProfile = null;
            _cachedPath = null;
            _cachedPathWriteUtc = default;
            _lastProfileTraceKey = null;
        }
    }

    private sealed record FaultProfile(
        string Profile,
        string Mode,
        int DelayMilliseconds,
        string ErrorCode,
        bool Retryable,
        string Message,
        string[] Operations,
        string Source)
    {
        public bool Enabled => !string.Equals(Profile, "none", StringComparison.OrdinalIgnoreCase);

        public bool AppliesTo(string operation)
            => Operations.Length == 0 ||
               Operations.Contains(operation, StringComparer.OrdinalIgnoreCase) ||
               Operations.Contains("market-data", StringComparer.OrdinalIgnoreCase) && IsMarketDataOperation(operation);

        public static FaultProfile FromFile(FaultProfileFile? file, string source)
        {
            FaultProfile profile = FromProfileName(file?.Profile, source);
            string mode = string.IsNullOrWhiteSpace(file?.Mode) ? profile.Mode : file?.Mode ?? profile.Mode;
            int delay = file?.DelayMilliseconds ?? profile.DelayMilliseconds;
            string errorCode = string.IsNullOrWhiteSpace(file?.ErrorCode) ? profile.ErrorCode : file?.ErrorCode ?? profile.ErrorCode;
            bool retryable = file?.Retryable ?? profile.Retryable;
            string message = string.IsNullOrWhiteSpace(file?.Message) ? profile.Message : file?.Message ?? profile.Message;
            string[] operations = NormalizeOperations(file?.Operations, profile.Operations);
            return new FaultProfile(profile.Profile, mode, Math.Max(0, delay), errorCode, retryable, message, operations, source);
        }

        public static FaultProfile FromProfileName(string? rawProfile, string source)
        {
            string profile = string.IsNullOrWhiteSpace(rawProfile) ? "none" : rawProfile.Trim();
            return profile.ToLowerInvariant() switch
            {
                "offline" or "offline-at-start" or "offline-during-runtime" or "offline-during-config-validation"
                    => new(profile, FaultModes.Error, 0, ProtocolErrorCodes.NetworkLost, true, "Simulated network outage.", ["market-data"], source),
                "high-latency-yfinance"
                    => new(profile, FaultModes.DelayOnly, 3500, ProtocolErrorCodes.Timeout, true, "Simulated slow YFinance response.", ["market-data"], source),
                "upstream-throttled" or "http-429"
                    => new(profile, FaultModes.Error, 0, ProtocolErrorCodes.UpstreamThrottled, true, "Simulated upstream throttling.", ["market-data"], source),
                "timeout"
                    => new(profile, FaultModes.Error, 6000, ProtocolErrorCodes.Timeout, true, "Simulated YFinance timeout.", ["market-data"], source),
                _ => new("none", FaultModes.DelayOnly, 0, ProtocolErrorCodes.InternalError, false, string.Empty, [], source)
            };
        }

        private static string[] NormalizeOperations(string[]? configured, string[] fallback)
            => configured is { Length: > 0 }
                ? configured.Where(static value => !string.IsNullOrWhiteSpace(value)).Select(static value => value.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
                : fallback;
    }

    private sealed class FaultProfileFile
    {
        public string? Profile { get; set; }
        public string? Mode { get; set; }
        public int? DelayMilliseconds { get; set; }
        public string? ErrorCode { get; set; }
        public bool? Retryable { get; set; }
        public string? Message { get; set; }
        public string[]? Operations { get; set; }
    }

    private static bool IsMarketDataOperation(string operation)
        => string.Equals(operation, ProtocolOperations.GetQuote, StringComparison.OrdinalIgnoreCase) ||
           string.Equals(operation, ProtocolOperations.GetQuotes, StringComparison.OrdinalIgnoreCase) ||
           string.Equals(operation, ProtocolOperations.GetHistory, StringComparison.OrdinalIgnoreCase) ||
           string.Equals(operation, ProtocolOperations.GetMarketTiming, StringComparison.OrdinalIgnoreCase) ||
           string.Equals(operation, ProtocolOperations.GetTickerInfo, StringComparison.OrdinalIgnoreCase);

    private static class FaultModes
    {
        public const string DelayOnly = "delay-only";
        public const string Error = "error";
    }
}
