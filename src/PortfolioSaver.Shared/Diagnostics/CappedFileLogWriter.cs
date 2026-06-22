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
namespace PortfolioSaver.Shared.Diagnostics;

public sealed class CappedFileLogWriter
{
    private const string BackupExtension = ".1";
    private readonly object _gate = new();
    private readonly string _logPath;
    private readonly long _maxBytes;

    public CappedFileLogWriter(string logPath, long maxBytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logPath);
        _logPath = logPath;
        _maxBytes = Math.Max(1024, maxBytes);
        Directory.CreateDirectory(Path.GetDirectoryName(_logPath) ?? ".");
    }

    public void WriteLine(string message)
    {
        string line = $"{message}{Environment.NewLine}";
        int incomingByteCount = System.Text.Encoding.UTF8.GetByteCount(line);
        lock (_gate)
        {
            RotateIfNeeded(incomingByteCount);
            File.AppendAllText(_logPath, line);
        }
    }

    private void RotateIfNeeded(int incomingByteCount)
    {
        FileInfo logFile = new(_logPath);
        if (!logFile.Exists || logFile.Length + incomingByteCount <= _maxBytes)
            return;

        string backupPath = _logPath + BackupExtension;
        try
        {
            // VmAgent keeps one backup only; newer rotations replace older archived logs.
            if (File.Exists(backupPath))
                File.Delete(backupPath);

            File.Move(_logPath, backupPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Trace.WriteLine("Capped log rotation failed; appending to current log. " + ex);
        }
    }
}
