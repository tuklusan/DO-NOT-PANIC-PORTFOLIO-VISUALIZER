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
using System.Reflection;

namespace PortfolioSaver.Shared.Licensing;

public static class ProjectLicenseService
{
    public const string LicenseFileName = "LICENSE";
    // Keep this fixed to match the explicit LogicalName in PortfolioSaver.Shared.csproj.
    internal const string EmbeddedLicenseResourceName = "PortfolioSaver.LICENSE";
    private const int MaxParentSearchDepth = 10;

    public static string GetLicenseText()
        => GetLicenseText([AppContext.BaseDirectory], File.ReadAllText);

    internal static string GetLicenseText(
        IEnumerable<string> candidateRoots,
        Func<string, string> readAllText)
    {
        try
        {
            string? licensePath = FindLicenseFile(candidateRoots);
            if (licensePath is not null)
            {
                string text = readAllText(licensePath);
                if (!string.IsNullOrWhiteSpace(text))
                    return NormalizeLineEndings(text);
            }
        }
        catch
        {
            // Fall back to the embedded copy so About/installer remain usable.
        }

        return GetEmbeddedLicenseText();
    }

    internal static string GetEmbeddedLicenseText()
    {
        Assembly assembly = typeof(ProjectLicenseService).Assembly;
        using Stream stream = assembly.GetManifestResourceStream(EmbeddedLicenseResourceName)
            ?? throw new InvalidOperationException($"Embedded license resource '{EmbeddedLicenseResourceName}' is missing.");
        using StreamReader reader = new(stream);
        return NormalizeLineEndings(reader.ReadToEnd());
    }

    private static string? FindLicenseFile(IEnumerable<string> candidateRoots)
    {
        foreach (string root in candidateRoots)
        {
            string? found = FindLicenseFileFrom(root);
            if (found is not null)
                return found;
        }

        return null;
    }

    private static string? FindLicenseFileFrom(string startDirectory)
    {
        DirectoryInfo? directory;
        try
        {
            directory = Directory.Exists(startDirectory)
                ? new DirectoryInfo(startDirectory)
                : Directory.GetParent(startDirectory);
        }
        catch
        {
            return null;
        }

        for (int depth = 0; directory is not null && depth <= MaxParentSearchDepth; depth++)
        {
            string candidate = Path.Combine(directory.FullName, LicenseFileName);
            if (File.Exists(candidate))
                return candidate;

            directory = directory.Parent;
        }

        return null;
    }

    private static string NormalizeLineEndings(string value)
        => value.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
}
