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
namespace PortfolioSaver.Shared.Infrastructure;

using System.IO;

public sealed class RealFileSystem : IFileSystem
{
    public static RealFileSystem Instance { get; } = new();

    private RealFileSystem()
    {
    }

    public void CreateDirectory(string path)
        => Directory.CreateDirectory(path);

    public bool FileExists(string path)
        => File.Exists(path);

    public void DeleteFile(string path)
        => File.Delete(path);

    public void MoveFile(string sourcePath, string destinationPath)
        => File.Move(sourcePath, destinationPath);

    public void AppendAllText(string path, string contents)
        => File.AppendAllText(path, contents);

    public long GetFileLength(string path)
        => new FileInfo(path).Length;
}
