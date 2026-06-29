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
using System.Security.Cryptography;
using System.Text;
using PortfolioSaver.Data.Interfaces;

namespace PortfolioSaver.Data.Services;

public sealed class SettingsProtectionService : ISettingsProtectionService
{
    public string Protect(string plainText)
    {
        if (string.IsNullOrWhiteSpace(plainText))
            return string.Empty;

        byte[] data = Encoding.UTF8.GetBytes(plainText);
        byte[] protectedData = ProtectedData.Protect(data, null, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(protectedData);
    }

    public string Unprotect(string protectedText)
    {
        if (string.IsNullOrWhiteSpace(protectedText))
            return string.Empty;

        byte[] data = Convert.FromBase64String(protectedText);
        byte[] plainData = ProtectedData.Unprotect(data, null, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(plainData);
    }
}
