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

public static class ProtocolEventTypes
{
    public const string ServerShuttingDown = "server_shutting_down";
    public const string ServerOverloaded = "server_overloaded";
    public const string ProtocolViolation = "protocol_violation";
    public const string ConnectionIdleTimeout = "connection_idle_timeout";
}
