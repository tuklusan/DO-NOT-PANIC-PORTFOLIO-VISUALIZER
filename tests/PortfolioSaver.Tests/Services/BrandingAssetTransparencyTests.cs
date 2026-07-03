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
using System.Buffers.Binary;
using System.Drawing;
using Xunit;

namespace PortfolioSaver.Tests.Services;

public sealed class BrandingAssetTransparencyTests
{
    private static readonly Lazy<string> RepoRoot = new(FindRepoRoot);

    [Theory]
    [InlineData("dnppv-icon-rev-3.png")]
    [InlineData("dnppv-icon-rev-3-splash.png")]
    public void BrandingPng_CornersAreTransparent(string fileName)
    {
        string path = Path.Combine(RepoRoot.Value, "src", "PortfolioSaver.Shared", "Assets", "Branding", fileName);

        using Bitmap bitmap = new(path);

        AssertCornerAlphaIsTransparent(bitmap);
    }

    [Fact]
    public void BrandingIco_LoadedFrameCornersAreTransparent()
    {
        string path = Path.Combine(RepoRoot.Value, "src", "PortfolioSaver.Shared", "Assets", "Branding", "dnppv-icon-rev-3.ico");

        using Icon icon = new(path);
        using Bitmap bitmap = icon.ToBitmap();

        AssertCornerAlphaIsTransparent(bitmap);
    }

    [Fact]
    public void BrandingIco_ContainsExpectedWindowsFrameSizes()
    {
        string path = Path.Combine(RepoRoot.Value, "src", "PortfolioSaver.Shared", "Assets", "Branding", "dnppv-icon-rev-3.ico");
        byte[] ico = File.ReadAllBytes(path);

        HashSet<int> sizes = ReadIcoFrameSizes(ico);

        foreach (int expectedSize in new[] { 16, 24, 32, 48, 64, 128, 256 })
        {
            Assert.Contains(expectedSize, sizes);
        }
    }

    private static void AssertCornerAlphaIsTransparent(Bitmap bitmap)
    {
        Assert.Equal(0, bitmap.GetPixel(0, 0).A);
        Assert.Equal(0, bitmap.GetPixel(bitmap.Width - 1, 0).A);
        Assert.Equal(0, bitmap.GetPixel(0, bitmap.Height - 1).A);
        Assert.Equal(0, bitmap.GetPixel(bitmap.Width - 1, bitmap.Height - 1).A);
    }

    private static HashSet<int> ReadIcoFrameSizes(byte[] ico)
    {
        if (ico.Length < 6 ||
            BinaryPrimitives.ReadUInt16LittleEndian(ico.AsSpan(0, 2)) != 0 ||
            BinaryPrimitives.ReadUInt16LittleEndian(ico.AsSpan(2, 2)) != 1)
        {
            throw new InvalidOperationException("Invalid ICO header.");
        }

        int count = BinaryPrimitives.ReadUInt16LittleEndian(ico.AsSpan(4, 2));
        HashSet<int> sizes = [];
        for (int i = 0; i < count; i++)
        {
            int entry = 6 + (i * 16);
            if (entry + 16 > ico.Length)
                throw new InvalidOperationException("ICO directory entry exceeds file length.");

            int width = ico[entry] == 0 ? 256 : ico[entry];
            int height = ico[entry + 1] == 0 ? 256 : ico[entry + 1];
            int size = BinaryPrimitives.ReadInt32LittleEndian(ico.AsSpan(entry + 8, 4));
            int offset = BinaryPrimitives.ReadInt32LittleEndian(ico.AsSpan(entry + 12, 4));
            if (width != height)
                throw new InvalidOperationException($"Expected square ICO frame, got {width}x{height}.");

            if (offset < 0 || size <= 0 || offset + size > ico.Length)
                throw new InvalidOperationException("ICO frame points outside file length.");

            sizes.Add(width);
        }

        return sizes;
    }

    private static string FindRepoRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "DoNotPanicPortfolioVisualizer.sln")))
                return current.FullName;

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root from test base directory.");
    }
}
