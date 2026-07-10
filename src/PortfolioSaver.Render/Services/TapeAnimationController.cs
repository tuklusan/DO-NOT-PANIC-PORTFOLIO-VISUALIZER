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
using System.Windows.Media;
using PortfolioSaver.Core.Enums;

namespace PortfolioSaver.Render.Services;

public sealed class TapeAnimationController
{
    private static readonly TimeSpan MinimumFrameInterval = TimeSpan.FromMilliseconds(33);
    private static readonly TimeSpan MaximumFrameStep = TimeSpan.FromMilliseconds(100);
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

    internal double CycleDistanceForTests => _cycleDistance;

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

        double elapsedSeconds;
        if (_lastRenderingTime is null)
        {
            _lastRenderingTime = renderingTime;
            elapsedSeconds = 1d / 60d;
        }
        else
        {
            TimeSpan elapsed = renderingTime - _lastRenderingTime.Value;
            if (elapsed < MinimumFrameInterval)
                return;

            _lastRenderingTime = renderingTime;
            // Throttle continuous tape invalidation to roughly 30 FPS while
            // preserving elapsed-time motion and preventing resume jumps.
            elapsedSeconds = Math.Clamp(
                elapsed.TotalSeconds,
                1d / 240d,
                MaximumFrameStep.TotalSeconds);
        }

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
