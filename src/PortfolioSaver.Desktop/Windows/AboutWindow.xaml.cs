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
using PortfolioSaver.Shared;
using PortfolioSaver.Shared.Licensing;

namespace PortfolioSaver.Desktop.Windows;

public partial class AboutWindow : Window
{
    public string VersionText => $"Version: {PortfolioVersion.SemanticVersion}";
    public string PublisherText => $"Publisher: {AppIdentity.PublisherName}";
    public string AuthorText => $"Author: {AppIdentity.AuthorName}";
    public string LicenseText => $"License: {AppIdentity.LicenseName}";
    public string FullLicenseText => ProjectLicenseService.GetLicenseText();

    public AboutWindow()
    {
        InitializeComponent();
        DataContext = this;
    }

    private void OnOkClick(object sender, RoutedEventArgs e)
        => Close();
}
