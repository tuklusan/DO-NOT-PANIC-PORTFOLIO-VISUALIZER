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

namespace PortfolioSaver.Config.Windows;

public partial class ValidationProgressWindow : Window
{
    public ValidationProgressWindow()
    {
        InitializeComponent();
        AutomationProperties.SetAutomationId(this, "ValidationProgressWindow");
        AutomationProperties.SetName(this, Title);
    }

    private void OnLogTextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        LogTextBox.CaretIndex = LogTextBox.Text.Length;
        LogTextBox.ScrollToEnd();
    }
}
