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
namespace PortfolioSaver.Shared.Diagnostics;

using PortfolioSaver.Shared.Infrastructure;

public sealed class CappedFileLogWriter
{
    private const string BackupExtension = ".1";
    private readonly object _gate = new();
    private readonly string _logPath;
    private readonly long _maxBytes;
    private readonly IFileSystem _fileSystem;

    public CappedFileLogWriter(string logPath, long maxBytes)
        : this(logPath, maxBytes, RealFileSystem.Instance)
    {
    }

    public CappedFileLogWriter(string logPath, long maxBytes, IFileSystem fileSystem)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logPath);
        _logPath = logPath;
        _maxBytes = Math.Max(1024, maxBytes);
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        string? directoryPath = Path.GetDirectoryName(_logPath);
        _fileSystem.CreateDirectory(string.IsNullOrEmpty(directoryPath) ? "." : directoryPath);
    }

    public void WriteLine(string message)
    {
        string line = $"{message}{Environment.NewLine}";
        int incomingByteCount = System.Text.Encoding.UTF8.GetByteCount(line);
        lock (_gate)
        {
            RotateIfNeeded(incomingByteCount);
            _fileSystem.AppendAllText(_logPath, line);
        }
    }

    private void RotateIfNeeded(int incomingByteCount)
    {
        if (!_fileSystem.FileExists(_logPath) ||
            _fileSystem.GetFileLength(_logPath) + incomingByteCount <= _maxBytes)
        {
            return;
        }

        string backupPath = _logPath + BackupExtension;
        try
        {
            // VmAgent keeps one backup only; newer rotations replace older archived logs.
            if (_fileSystem.FileExists(backupPath))
                _fileSystem.DeleteFile(backupPath);

            _fileSystem.MoveFile(_logPath, backupPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Trace.WriteLine("Capped log rotation failed; appending to current log. " + ex);
        }
    }
}
