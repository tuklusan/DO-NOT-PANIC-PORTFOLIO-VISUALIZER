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

public static class ProtocolConstants
{
    public const int Version = 1;
    public const int DefaultPort = 14870;
    public const int MaxConcurrentClients = 1024;
    public const int LengthPrefixBytes = 4;
    public const int MaxMessageBytes = 4 * 1024 * 1024;

    public static string GetMutexName(int port)
        => $"Global\\PortfolioSaver.YFinance.NET.Server.{port}";
}
