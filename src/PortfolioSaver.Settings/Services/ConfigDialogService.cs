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
using WpfMessageBox = System.Windows.MessageBox;

namespace PortfolioSaver.Config.Services;

public interface IConfigDialogService
{
    void Show(
        string message,
        string caption,
        MessageBoxButton button,
        MessageBoxImage image);
}

public sealed class WpfConfigDialogService : IConfigDialogService
{
    public void Show(
        string message,
        string caption,
        MessageBoxButton button,
        MessageBoxImage image)
        => WpfMessageBox.Show(message, caption, button, image);
}
