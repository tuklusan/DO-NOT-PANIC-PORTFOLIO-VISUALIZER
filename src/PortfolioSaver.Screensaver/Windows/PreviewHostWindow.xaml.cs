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
using System.Windows.Interop;
using PortfolioSaver.Screensaver.Services;

namespace PortfolioSaver.Screensaver.Windows;

public partial class PreviewHostWindow : Window
{
    private readonly IntPtr _previewHandle;

    public PreviewHostWindow(IntPtr previewHandle)
    {
        _previewHandle = previewHandle;
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_previewHandle == IntPtr.Zero)
            return;

        WindowInteropHelper helper = new(this);
        NativeMethods.SetParent(helper.Handle, _previewHandle);
        if (NativeMethods.GetClientRect(_previewHandle, out NativeMethods.RECT rect))
        {
            Width = rect.Right - rect.Left;
            Height = rect.Bottom - rect.Top;
        }
    }
}
