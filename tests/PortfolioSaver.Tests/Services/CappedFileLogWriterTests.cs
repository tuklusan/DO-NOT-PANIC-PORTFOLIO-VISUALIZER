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
using PortfolioSaver.Shared.Diagnostics;
using PortfolioSaver.Shared.Infrastructure;
using Xunit;

namespace PortfolioSaver.Tests.Services;

public sealed class CappedFileLogWriterTests
{
    [Fact]
    public void WriteLine_RotatesPrimaryLogBeforeExceedingCap()
    {
        InMemoryFileSystem fileSystem = new();
        string logPath = "/logs/vm-agent.log";
        CappedFileLogWriter writer = new(logPath, maxBytes: 1_024, fileSystem);
        fileSystem.WriteAllText(logPath, new string('A', 1_000));

        writer.WriteLine(new string('B', 80));

        Assert.True(fileSystem.FileExists(logPath));
        Assert.True(fileSystem.FileExists(logPath + ".1"));
        Assert.True(fileSystem.GetFileLength(logPath) <= 1_024);
        Assert.Equal(1_000, fileSystem.GetFileLength(logPath + ".1"));
        Assert.Contains("BBBB", fileSystem.ReadAllText(logPath), StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteLine_SerializesConcurrentWriters()
    {
        InMemoryFileSystem fileSystem = new();
        string logPath = "/logs/vm-agent.log";
        CappedFileLogWriter writer = new(logPath, maxBytes: 4096, fileSystem);

        await Task.WhenAll(Enumerable.Range(0, 100).Select(index =>
            Task.Run(() => writer.WriteLine($"line-{index:000}"))));

        string combined = fileSystem.ReadAllText(logPath);
        if (fileSystem.FileExists(logPath + ".1"))
            combined += fileSystem.ReadAllText(logPath + ".1");

        for (int index = 0; index < 100; index++)
            Assert.Contains($"line-{index:000}", combined, StringComparison.Ordinal);
    }

    [Fact]
    public void WriteLine_CreatesBrandNewLogFile()
    {
        InMemoryFileSystem fileSystem = new();
        string logPath = "/logs/vm-agent.log";
        CappedFileLogWriter writer = new(logPath, maxBytes: 1_024, fileSystem);

        writer.WriteLine("hello");

        Assert.True(fileSystem.FileExists(logPath));
        Assert.False(fileSystem.FileExists(logPath + ".1"));
        Assert.Equal($"hello{Environment.NewLine}", fileSystem.ReadAllText(logPath));
    }

    [Fact]
    public void Constructor_CreatesParentDirectory()
    {
        InMemoryFileSystem fileSystem = new();
        string logPath = "/logs/vm-agent.log";

        _ = new CappedFileLogWriter(logPath, 4096, fileSystem);

        Assert.True(fileSystem.DirectoryExists(Path.GetDirectoryName(logPath)!));
    }

    [Fact]
    public void Constructor_UsesCurrentDirectoryForFileNameOnlyPath()
    {
        InMemoryFileSystem fileSystem = new();

        _ = new CappedFileLogWriter("vm-agent.log", 4096, fileSystem);

        Assert.True(fileSystem.DirectoryExists("."));
    }

    [Fact]
    public void Constructor_ThrowsOnNullFileSystem()
    {
        Assert.Throws<ArgumentNullException>(() => new CappedFileLogWriter("/logs/vm-agent.log", 4096, fileSystem: null!));
    }

    [Fact]
    public void WriteLine_DoesNotRotateWhenIncomingLineExactlyReachesCap()
    {
        InMemoryFileSystem fileSystem = new();
        string logPath = "/logs/vm-agent.log";
        CappedFileLogWriter writer = new(logPath, maxBytes: 1_024, fileSystem);
        int incomingByteCount = System.Text.Encoding.UTF8.GetByteCount($"BBBB{Environment.NewLine}");
        fileSystem.WriteAllText(logPath, new string('A', 1_024 - incomingByteCount));

        writer.WriteLine("BBBB");

        Assert.False(fileSystem.FileExists(logPath + ".1"));
        Assert.Equal(1_024, fileSystem.GetFileLength(logPath));
    }

    [Fact]
    public void WriteLine_ReplacesExistingBackupDuringRotation()
    {
        InMemoryFileSystem fileSystem = new();
        string logPath = "/logs/vm-agent.log";
        CappedFileLogWriter writer = new(logPath, maxBytes: 1_024, fileSystem);
        fileSystem.WriteAllText(logPath, new string('A', 1_000));
        fileSystem.WriteAllText(logPath + ".1", "old backup");

        writer.WriteLine(new string('B', 80));

        Assert.Equal(1_000, fileSystem.GetFileLength(logPath + ".1"));
        Assert.DoesNotContain("old backup", fileSystem.ReadAllText(logPath + ".1"), StringComparison.Ordinal);
        Assert.Contains("BBBB", fileSystem.ReadAllText(logPath), StringComparison.Ordinal);
    }

    [Fact]
    public void WriteLine_AppendsToCurrentLog_WhenRotationMoveFails()
    {
        InMemoryFileSystem fileSystem = new() { ThrowOnMove = true };
        string logPath = "/logs/vm-agent.log";
        CappedFileLogWriter writer = new(logPath, maxBytes: 1_024, fileSystem);
        fileSystem.WriteAllText(logPath, new string('A', 1_020));

        writer.WriteLine("BBBB");

        Assert.False(fileSystem.FileExists(logPath + ".1"));
        string logText = fileSystem.ReadAllText(logPath);
        Assert.StartsWith(new string('A', 1_020), logText, StringComparison.Ordinal);
        Assert.Contains("BBBB", logText, StringComparison.Ordinal);
    }

    private sealed class InMemoryFileSystem : IFileSystem
    {
        private readonly object _gate = new();
        private readonly HashSet<string> _directories = new(StringComparer.Ordinal);
        private readonly Dictionary<string, byte[]> _files = new(StringComparer.Ordinal);

        public bool ThrowOnMove { get; init; }

        public void CreateDirectory(string path)
        {
            lock (_gate)
                _directories.Add(path);
        }

        public bool DirectoryExists(string path)
        {
            lock (_gate)
                return _directories.Contains(path);
        }

        public bool FileExists(string path)
        {
            lock (_gate)
                return _files.ContainsKey(path);
        }

        public void DeleteFile(string path)
        {
            lock (_gate)
                _files.Remove(path);
        }

        public void MoveFile(string sourcePath, string destinationPath)
        {
            lock (_gate)
            {
                if (ThrowOnMove)
                    throw new IOException("Test configured to fail on MoveFile.");

                if (!_files.TryGetValue(sourcePath, out byte[]? contents))
                    throw new FileNotFoundException("Source file was not found.", sourcePath);

                if (_files.ContainsKey(destinationPath))
                    throw new IOException("Destination file already exists.");

                _files[destinationPath] = contents;
                _files.Remove(sourcePath);
            }
        }

        public void AppendAllText(string path, string contents)
        {
            lock (_gate)
            {
                EnsureParentDirectoryExists(path);
                byte[] existing = ReadAllBytesLocked(path);
                byte[] appended = System.Text.Encoding.UTF8.GetBytes(contents);
                byte[] combined = new byte[existing.Length + appended.Length];
                Buffer.BlockCopy(existing, 0, combined, 0, existing.Length);
                Buffer.BlockCopy(appended, 0, combined, existing.Length, appended.Length);
                _files[path] = combined;
            }
        }

        public long GetFileLength(string path)
        {
            lock (_gate)
            {
                if (!_files.TryGetValue(path, out byte[]? contents))
                    throw new FileNotFoundException("File was not found.", path);

                return contents.LongLength;
            }
        }

        public void WriteAllText(string path, string contents)
        {
            lock (_gate)
            {
                EnsureParentDirectoryExists(path);
                _files[path] = System.Text.Encoding.UTF8.GetBytes(contents);
            }
        }

        public string ReadAllText(string path)
        {
            lock (_gate)
            {
                EnsureParentDirectoryExists(path);
                return ReadAllTextLocked(path);
            }
        }

        private string ReadAllTextLocked(string path)
            => System.Text.Encoding.UTF8.GetString(ReadAllBytesLocked(path));

        private byte[] ReadAllBytesLocked(string path)
            => _files.GetValueOrDefault(path, []);

        private void EnsureParentDirectoryExists(string path)
        {
            string? directoryPath = Path.GetDirectoryName(path);
            string parentPath = string.IsNullOrEmpty(directoryPath) ? "." : directoryPath;

            if (!_directories.Contains(parentPath))
                throw new DirectoryNotFoundException($"Directory '{parentPath}' was not created.");
        }
    }
}
