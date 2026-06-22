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
using System.Windows.Media;
using PortfolioSaver.Core.Enums;

namespace PortfolioSaver.Render.Services;

public sealed class TapeAnimationController
{
    private UIElement? _element;
    private TranslateTransform? _transform;
    private double _cycleDistance;
    private double _pixelsPerSecond;
    private double _progress;
    private double _anchorOffset;
    private ScrollDirection _direction = ScrollDirection.Left;
    private TimeSpan? _lastRenderingTime;
    private bool _running;

    internal bool IsRunning => _running;

    public void Attach(UIElement element)
    {
        if (ReferenceEquals(_element, element))
            return;

        _element = element;
        _transform = element.RenderTransform as TranslateTransform ?? new TranslateTransform();
        element.RenderTransform = _transform;
        ApplyOffset();
    }

    public void Update(double cycleDistance, double pixelsPerSecond, ScrollDirection direction, double anchorOffset = 0d)
    {
        _cycleDistance = Math.Max(1d, cycleDistance);
        _pixelsPerSecond = Math.Max(1d, pixelsPerSecond);
        _direction = direction;
        _anchorOffset = anchorOffset;
        NormalizeProgress();

        ApplyOffset();
    }

    public void Start()
    {
        if (_element is null)
            return;

        if (_running)
            return;

        _running = true;
        _lastRenderingTime = null;
        CompositionTarget.Rendering += OnRendering;
    }

    public void Stop()
    {
        if (!_running)
            return;

        CompositionTarget.Rendering -= OnRendering;
        _running = false;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        if (_element is null || _transform is null || _cycleDistance <= 0 || _pixelsPerSecond <= 0)
            return;

        if (e is not RenderingEventArgs renderingArgs)
            return;

        TimeSpan renderingTime = renderingArgs.RenderingTime;
        if (!_element.IsVisible || !_element.IsArrangeValid || _element.RenderSize.IsEmpty)
        {
            // Resume with a small first-frame delta instead of accumulating hidden-time jumps.
            _lastRenderingTime = null;
            return;
        }

        double elapsedSeconds = _lastRenderingTime is null
            ? 1d / 60d
            : Math.Max(1d / 240d, (renderingTime - _lastRenderingTime.Value).TotalSeconds);
        _lastRenderingTime = renderingTime;

        _progress += _pixelsPerSecond * elapsedSeconds;
        NormalizeProgress();

        ApplyOffset();
    }

    private void NormalizeProgress()
    {
        if (_cycleDistance <= 0)
            return;

        _progress %= _cycleDistance;
        if (_progress < 0d)
            _progress += _cycleDistance;
    }

    private void ApplyOffset()
    {
        if (_transform is not null)
            _transform.X = _anchorOffset + (_direction == ScrollDirection.Right ? _progress : -_progress);
    }
}
