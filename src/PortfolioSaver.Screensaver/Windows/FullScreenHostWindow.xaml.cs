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
using System.Windows;
using System.Windows.Automation;
using PortfolioSaver.Screensaver.Services;
using PortfolioSaver.Shared;

namespace PortfolioSaver.Screensaver.Windows;

public partial class FullScreenHostWindow : Window
{
    public FullScreenHostWindow()
    {
        InitializeComponent();
        Title = $"Portfolio Screensaver {PortfolioVersion.Version}";
        AutomationProperties.SetAutomationId(this, "ScreensaverHostWindow");
        AutomationProperties.SetName(this, Title);
        AutomationProperties.SetHelpText(this, PortfolioVersion.Version);

        InputExitMonitor inputExitMonitor = new(this);
        inputExitMonitor.Attach();
    }
}
