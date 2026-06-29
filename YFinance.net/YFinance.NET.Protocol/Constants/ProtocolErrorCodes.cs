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
namespace YFinance.NET.Protocol.Constants;

public static class ProtocolErrorCodes
{
    public const string InvalidSymbol = "invalid_symbol";
    public const string NetworkLost = "network_lost";
    public const string UpstreamUnavailable = "upstream_unavailable";
    public const string UpstreamThrottled = "upstream_throttled";
    public const string Timeout = "timeout";
    public const string CacheMiss = "cache_miss";
    public const string InternalError = "internal_error";
    public const string UnsupportedOperation = "unsupported_operation";
    public const string ProtocolError = "protocol_error";
    public const string ProtocolViolation = "protocol_violation";
    public const string ServerOverloaded = "server_overloaded";
}
