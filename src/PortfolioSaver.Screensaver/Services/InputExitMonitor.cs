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
using System.Windows;
using System.Windows.Input;
using PortfolioSaver.Shared.Diagnostics;

namespace PortfolioSaver.Screensaver.Services;

public sealed class InputExitMonitor
{
    private readonly Window _window;
    private NativeMethods.POINT? _startMousePosition;
    private DateTimeOffset _armedAt = DateTimeOffset.MaxValue;

    public InputExitMonitor(Window window)
    {
        _window = window;
    }

    public void Attach()
    {
        string? disableInputExit = Environment.GetEnvironmentVariable("PORTFOLIOSAVER_DISABLE_INPUT_EXIT");
        if (string.Equals(disableInputExit, "1", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(disableInputExit, "true", StringComparison.OrdinalIgnoreCase))
        {
            TraceLog.Info("InputExitMonitor", "Input-based exit disabled by environment variable.");
            return;
        }

        _window.Loaded += OnLoaded;
        _window.MouseMove += OnMouseMove;
        _window.MouseDown += (_, _) =>
        {
            TraceLog.Info("InputExitMonitor", "Closing screensaver on mouse down.");
            _window.Close();
        };
        _window.KeyDown += (_, _) =>
        {
            TraceLog.Info("InputExitMonitor", "Closing screensaver on key down.");
            _window.Close();
        };
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _startMousePosition = GetCursorPosition();
        _armedAt = DateTimeOffset.UtcNow.AddSeconds(1);
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (DateTimeOffset.UtcNow < _armedAt)
            return;

        if (_startMousePosition is null)
        {
            _startMousePosition = GetCursorPosition();
            return;
        }

        NativeMethods.POINT current = GetCursorPosition();
        if (Math.Abs(current.X - _startMousePosition.Value.X) > 8 || Math.Abs(current.Y - _startMousePosition.Value.Y) > 8)
        {
            TraceLog.Info("InputExitMonitor", $"Closing screensaver on cursor movement. Start=({_startMousePosition.Value.X},{_startMousePosition.Value.Y}) Current=({current.X},{current.Y})");
            _window.Close();
        }
    }

    private static NativeMethods.POINT GetCursorPosition()
    {
        if (!NativeMethods.GetCursorPos(out NativeMethods.POINT point))
            return default;

        return point;
    }
}
