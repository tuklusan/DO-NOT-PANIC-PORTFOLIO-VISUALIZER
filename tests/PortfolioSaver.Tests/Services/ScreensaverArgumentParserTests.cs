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
