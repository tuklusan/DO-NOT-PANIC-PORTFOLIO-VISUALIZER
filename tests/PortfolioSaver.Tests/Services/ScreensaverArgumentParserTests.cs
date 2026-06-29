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
using PortfolioSaver.Screensaver.Services;
using Xunit;

namespace PortfolioSaver.Tests.Services;

public sealed class ScreensaverArgumentParserTests
{
    [Theory]
    [InlineData("/s")]
    [InlineData("-s")]
    [InlineData("/S")]
    public void Parse_FullscreenArguments_ReturnFullscreen(string arg)
    {
        ScreensaverLaunchArguments result = new ScreensaverArgumentParser().Parse([arg]);

        Assert.Equal(ScreensaverMode.Fullscreen, result.Mode);
        Assert.Equal(IntPtr.Zero, result.PreviewHandle);
    }

    [Theory]
    [InlineData("/p", "12345")]
    [InlineData("-p", "12345")]
    [InlineData("/P", "12345")]
    public void Parse_PreviewArgumentsWithSeparateHandle_ReturnPreview(string arg, string hwnd)
    {
        ScreensaverLaunchArguments result = new ScreensaverArgumentParser().Parse([arg, hwnd]);

        Assert.Equal(ScreensaverMode.Preview, result.Mode);
        Assert.Equal(new IntPtr(12345), result.PreviewHandle);
    }

    [Theory]
    [InlineData("/p:12345")]
    [InlineData("-p:12345")]
    [InlineData("/P:12345")]
    public void Parse_PreviewArgumentsWithInlineHandle_ReturnPreview(string arg)
    {
        ScreensaverLaunchArguments result = new ScreensaverArgumentParser().Parse([arg]);

        Assert.Equal(ScreensaverMode.Preview, result.Mode);
        Assert.Equal(new IntPtr(12345), result.PreviewHandle);
    }

    [Fact]
    public void Parse_PreviewArgumentsWithDecimalHandleThatCouldBeHex_PrefersDecimal()
    {
        ScreensaverLaunchArguments result = new ScreensaverArgumentParser().Parse(["/p:10"]);

        Assert.Equal(ScreensaverMode.Preview, result.Mode);
        Assert.Equal(new IntPtr(10), result.PreviewHandle);
    }

    [Theory]
    [InlineData("/p")]
    [InlineData("/p:0")]
    [InlineData("/p:-1")]
    [InlineData("/p:invalid")]
    [InlineData("/p:0x")]
    [InlineData("/p:0xFFFFFFFFFFFFFFFFF")]
    public void Parse_PreviewArgumentsWithoutValidHandle_ReturnPreviewWithZeroHandle(string arg)
    {
        ScreensaverLaunchArguments result = new ScreensaverArgumentParser().Parse([arg]);

        Assert.Equal(ScreensaverMode.Preview, result.Mode);
        Assert.Equal(IntPtr.Zero, result.PreviewHandle);
    }

    [Fact]
    public void Parse_PreviewArgumentsWithInvalidSeparateHandle_ReturnPreviewWithZeroHandle()
    {
        ScreensaverLaunchArguments result = new ScreensaverArgumentParser().Parse(["/p", "-1"]);

        Assert.Equal(ScreensaverMode.Preview, result.Mode);
        Assert.Equal(IntPtr.Zero, result.PreviewHandle);
    }

    [Fact]
    public void Parse_PreviewArgumentsWithSeparatePrefixedHexHandle_ReturnPreview()
    {
        ScreensaverLaunchArguments result = new ScreensaverArgumentParser().Parse(["/p", "0xFF"]);

        Assert.Equal(ScreensaverMode.Preview, result.Mode);
        Assert.Equal(new IntPtr(255), result.PreviewHandle);
    }

    [Fact]
    public void Parse_PreviewArgumentsWithUnprefixedHexHandle_ReturnPreview()
    {
        ScreensaverLaunchArguments result = new ScreensaverArgumentParser().Parse(["/p:FF"]);

        Assert.Equal(ScreensaverMode.Preview, result.Mode);
        Assert.Equal(new IntPtr(255), result.PreviewHandle);
    }

    [Fact]
    public void Parse_PreviewArgumentsWithInlineAndSeparateHandle_PrefersInlineHandle()
    {
        ScreensaverLaunchArguments result = new ScreensaverArgumentParser().Parse(["/p:12345", "67890"]);

        Assert.Equal(ScreensaverMode.Preview, result.Mode);
        Assert.Equal(new IntPtr(12345), result.PreviewHandle);
    }

    [Fact]
    public void Parse_PreviewArgumentsWithPrefixedHexHandle_ReturnPreview()
    {
        ScreensaverLaunchArguments result = new ScreensaverArgumentParser().Parse(["/p:0x3039"]);

        Assert.Equal(ScreensaverMode.Preview, result.Mode);
        Assert.Equal(new IntPtr(12345), result.PreviewHandle);
    }

    [Theory]
    [InlineData("/c")]
    [InlineData("/c:12345")]
    [InlineData("-c")]
    [InlineData("/C")]
    [InlineData("/showconfig")]
    [InlineData("-showconfig")]
    [InlineData("--showconfig")]
    [InlineData("/SHOWCONFIG")]
    public void Parse_ConfigArguments_ReturnConfigure(string arg)
    {
        ScreensaverLaunchArguments result = new ScreensaverArgumentParser().Parse([arg]);

        Assert.Equal(ScreensaverMode.Configure, result.Mode);
        Assert.Equal(IntPtr.Zero, result.PreviewHandle);
    }

    [Fact]
    public void Parse_ConfigArgumentsWithExtraValues_ReturnConfigure()
    {
        ScreensaverLaunchArguments result = new ScreensaverArgumentParser().Parse(["/c", "12345"]);

        Assert.Equal(ScreensaverMode.Configure, result.Mode);
        Assert.Equal(IntPtr.Zero, result.PreviewHandle);
    }

    [Fact]
    public void Parse_UnknownArgument_ReturnConfigure()
    {
        ScreensaverLaunchArguments result = new ScreensaverArgumentParser().Parse(["/mystery"]);

        Assert.Equal(ScreensaverMode.Configure, result.Mode);
    }
}
